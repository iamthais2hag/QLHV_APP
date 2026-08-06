using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime.ControlPlane;

internal static class CsdtAtomicTargetLockName
{
    internal const string Prefix = "QLHV:CSDT_ATOMIC:";

    internal static string Build(
        string targetProfile,
        string sourceProfile,
        string streamCode)
    {
        foreach (var value in new[] { targetProfile, sourceProfile, streamCode })
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '_'))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.ProfileMismatch);
            }
        }

        return $"{Prefix}{targetProfile}:{sourceProfile}:{streamCode}";
    }
}

/// <summary>
/// Applies all six approved core domains under one target connection and one
/// caller-owned transaction. This type is intentionally absent from worker DI.
/// </summary>
internal sealed class CsdtTargetCycleApplier : ICsdtTargetCycleApplier
{
    private readonly string _targetConnectionString;
    private readonly CsdtRealtimeTargetWriter _writer;
    private readonly ICsdtRealtimeTargetControlPlaneRepository _controlPlane;
    private readonly CsdtMembershipBootstrapTransactionApplier? _membershipBootstrap;
    private readonly int _lockTimeoutMilliseconds;

    internal CsdtTargetCycleApplier(
        string targetConnectionString,
        CsdtRealtimeTargetWriter writer,
        ICsdtRealtimeTargetControlPlaneRepository controlPlane,
        int lockTimeoutMilliseconds = 5_000,
        CsdtMembershipBootstrapTransactionApplier? membershipBootstrap = null)
    {
        if (lockTimeoutMilliseconds is < 0 or > 60_000)
        {
            throw new ArgumentOutOfRangeException(nameof(lockTimeoutMilliseconds));
        }

        _targetConnectionString = targetConnectionString;
        _writer = writer;
        _controlPlane = controlPlane;
        _membershipBootstrap = membershipBootstrap;
        _lockTimeoutMilliseconds = lockTimeoutMilliseconds;
    }

    public async Task<CsdtTargetCycleCommitMarker> ApplyAsync(
        CsdtStagedCycle stagedCycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagedCycle);
        CsdtAtomicCoreDomains.RequireExactScope(
            stagedCycle.Domains.Select(domain => domain.DomainName));
        if (stagedCycle.DeleteCount != 0)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.DeleteExecutionNotEnabled);
        }

        var route = ResolveRoute(stagedCycle);
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await AcquireLockAsync(connection, transaction, stagedCycle, cancellationToken);
            await VerifyTargetIdentityAsync(
                connection,
                transaction,
                route.TargetDatabaseName,
                cancellationToken);

            var snapshots = await BuildAndValidateSnapshotsAsync(
                connection,
                transaction,
                stagedCycle,
                cancellationToken);
            var before = await _controlPlane.ReadCycleMarkerAsync(
                connection,
                transaction,
                stagedCycle.CycleId,
                cancellationToken);
            if (before is null ||
                before.Status != SyncCycleStatus.Validated ||
                !MarkerMatchesStageIdentity(before, stagedCycle))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
            }

            if (!await _controlPlane.MarkTargetCommittingAsync(
                    connection,
                    transaction,
                    stagedCycle.CycleId,
                    cancellationToken))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
            }

            var startedAt = DateTimeOffset.UtcNow;
            foreach (var pair in snapshots)
            {
                var stagedDomain = stagedCycle.Domains.Single(domain =>
                    string.Equals(
                        domain.DomainName,
                        pair.Domain.Name,
                        StringComparison.Ordinal));
                var write = await _writer.UpsertAsync(
                    connection,
                    transaction,
                    pair.Snapshot,
                    stagedCycle.MaCsdt,
                    cancellationToken);
                if (write.Conflicts.Count != 0 && write.SkippedRows != 0)
                {
                    throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.ValidationFailed);
                }

                var membershipCommit = _membershipBootstrap is null
                    ? null
                    : await _membershipBootstrap.ApplyDomainAsync(
                        connection,
                        transaction,
                        stagedCycle,
                        stagedDomain,
                        cancellationToken);
                var resultHash = ComputeResultHash(stagedDomain, write);
                await _controlPlane.UpsertCycleDomainResultAsync(
                    connection,
                    transaction,
                    new SyncCycleDomainResult(
                        stagedCycle.CycleId,
                        stagedDomain.DomainName,
                        SyncCycleDomainStatus.Committed,
                        stagedDomain.SourceRowCount,
                        write.InsertedRows,
                        write.UpdatedRows,
                        DeleteCount: 0,
                        PreservedExcludedCount: 0,
                        ConflictCount: write.Conflicts.Count,
                        stagedDomain.SourceKeySetHash,
                        resultHash,
                        ErrorCode: null,
                        startedAt,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
                if (membershipCommit is not null)
                {
                    await _membershipBootstrap!.WriteCoverageAsync(
                        connection,
                        transaction,
                        stagedCycle,
                        stagedDomain,
                        membershipCommit,
                        cancellationToken);
                }
            }

            if (!await _controlPlane.MarkTargetCommittedAsync(
                    connection,
                    transaction,
                    stagedCycle.CycleId,
                    cancellationToken))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
            }

            var marker = await _controlPlane.ReadCycleMarkerAsync(
                connection,
                transaction,
                stagedCycle.CycleId,
                cancellationToken);
            if (marker is null)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
            }

            CsdtAtomicMappedTableCycleCoordinator.VerifyMarker(
                stagedCycle,
                marker);
            await transaction.CommitAsync(cancellationToken);
            return marker;
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private async Task<IReadOnlyList<PreparedSnapshot>> BuildAndValidateSnapshotsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtStagedCycle stagedCycle,
        CancellationToken cancellationToken)
    {
        using var schemaHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var result = new List<PreparedSnapshot>(
            CsdtAtomicCoreDomains.ApplyOrder.Count);
        foreach (var stagedDomain in stagedCycle.Domains)
        {
            var domain = CsdtRealtimeDomainCatalog.GetRequired(stagedDomain.DomainName);
            if (domain.IsOptional)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
            }

            var metadata = await CsdtRealtimeSourceReader.ReadMetadataAsync(
                connection,
                domain,
                cancellationToken,
                transaction);
            CsdtRealtimeSourceReader.ValidatePrimaryKey(metadata);
            var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain.Name);
            policy.ValidateSourceSchema(metadata);
            AppendSchema(schemaHash, metadata);
            result.Add(new PreparedSnapshot(
                domain,
                ToSnapshot(stagedDomain, metadata, policy)));
        }

        var observed = new ControlPlaneFingerprint(schemaHash.GetHashAndReset());
        if (!observed.Equals(stagedCycle.TargetSchemaFingerprint))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SchemaMismatch);
        }

        return result;
    }

    private static CsdtRealtimeSnapshot ToSnapshot(
        CsdtStagedDomain staged,
        CsdtRealtimeTableMetadata metadata,
        CsdtRealtimeDomainColumnPolicy policy)
    {
        var selected = policy.SelectForwardReadColumns(metadata);
        var selectedNames = selected.Select(column => column.Name)
            .ToHashSet(StringComparer.Ordinal);
        var table = new DataTable
        {
            CaseSensitive = true,
            Locale = CultureInfo.InvariantCulture,
        };
        foreach (var column in selected)
        {
            table.Columns.Add(
                new DataColumn(column.Name, ToClrType(column.SqlType))
                {
                    AllowDBNull = column.IsNullable,
                });
        }

        foreach (var stagedRow in staged.Rows)
        {
            var values = stagedRow.CopyValues();
            if (!values.Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(selectedNames))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.UnknownColumn);
            }

            var row = table.NewRow();
            foreach (var column in selected)
            {
                row[column.Name] = values[column.Name] ?? DBNull.Value;
            }

            table.Rows.Add(row);
        }

        return new CsdtRealtimeSnapshot(metadata, table);
    }

    private async Task AcquireLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtStagedCycle staged,
        CancellationToken cancellationToken)
    {
        var resource = CsdtAtomicTargetLockName.Build(
            staged.TargetProfile,
            staged.SourceProfile,
            staged.StreamCode);
        var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = @Resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = @LockTimeout,
                @DbPrincipal = N'public';
            SELECT @Result;
            """,
            new
            {
                Resource = resource,
                LockTimeout = _lockTimeoutMilliseconds,
            },
            transaction,
            commandTimeout: Math.Max(30, _lockTimeoutMilliseconds / 1000 + 5),
            cancellationToken: cancellationToken));
        if (result < 0)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetLockTimeout);
        }
    }

    private static async Task VerifyTargetIdentityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string expectedDatabase,
        CancellationToken cancellationToken)
    {
        var actual = await connection.ExecuteScalarAsync<string>(
            new CommandDefinition(
                "SELECT DB_NAME();",
                transaction: transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        if (!string.Equals(actual, expectedDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ProfileMismatch);
        }
    }

    private static CsdtRealtimeRouteDefinition ResolveRoute(
        CsdtStagedCycle staged)
    {
        if (!CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                staged.StreamCode,
                staged.SourceProfile,
                staged.TargetProfile,
                out var route) ||
            !string.Equals(route.MaCSDT, staged.MaCsdt, StringComparison.Ordinal))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ProfileMismatch);
        }

        return route;
    }

    private static bool MarkerMatchesStageIdentity(
        CsdtTargetCycleCommitMarker marker,
        CsdtStagedCycle staged)
        => marker.CycleId == staged.CycleId &&
           marker.StartSourceVersion == staged.StartSourceVersion &&
           marker.EndSourceVersion == staged.EndSourceVersion &&
           marker.EnabledDomainCount == staged.Domains.Count &&
           string.Equals(marker.SourceProfile, staged.SourceProfile, StringComparison.Ordinal) &&
           string.Equals(marker.TargetProfile, staged.TargetProfile, StringComparison.Ordinal) &&
           string.Equals(marker.StreamCode, staged.StreamCode, StringComparison.Ordinal) &&
           string.Equals(marker.MaCsdt, staged.MaCsdt, StringComparison.Ordinal) &&
           marker.MappingFingerprint.Equals(staged.MappingFingerprint) &&
           marker.RouteFingerprint.Equals(staged.RouteFingerprint) &&
           marker.SourceSchemaFingerprint?.Equals(
               staged.SourceSchemaFingerprint) == true &&
           marker.TargetSchemaFingerprint?.Equals(
               staged.TargetSchemaFingerprint) == true &&
           marker.StagedKeySetHash?.Equals(staged.StagedKeySetHash) == true;

    private static ControlPlaneFingerprint ComputeResultHash(
        CsdtStagedDomain staged,
        CsdtRealtimeWriteResult result)
    {
        var components = new List<ReadOnlyMemory<byte>>
        {
            Encoding.UTF8.GetBytes(staged.DomainName),
            staged.StageResultHash.ToArray(),
            BitConverter.GetBytes(result.SourceRows),
            BitConverter.GetBytes(result.InsertedRows),
            BitConverter.GetBytes(result.UpdatedRows),
            BitConverter.GetBytes(result.SkippedRows),
            BitConverter.GetBytes(result.Conflicts.Count),
        };
        foreach (var entity in result.Entities.OrderBy(
                     value => Convert.ToHexString(
                         CsdtRealtimeTargetWriter.HashKey(value.KeyJson)),
                     StringComparer.Ordinal))
        {
            components.Add(CsdtRealtimeTargetWriter.HashKey(entity.KeyJson));
            components.Add(entity.SourceHash);
            components.Add(entity.TargetHash);
        }

        return CsdtAtomicHash.Compute(components.ToArray());
    }

    private static void AppendSchema(
        IncrementalHash hash,
        CsdtRealtimeTableMetadata metadata)
    {
        Append(hash, metadata.Domain.Name);
        foreach (var column in metadata.Columns.OrderBy(column => column.ColumnId))
        {
            Append(hash, column.Name);
            Append(hash, column.SqlType);
            Append(hash, column.MaxLength.ToString(CultureInfo.InvariantCulture));
            Append(hash, column.Precision.ToString(CultureInfo.InvariantCulture));
            Append(hash, column.Scale.ToString(CultureInfo.InvariantCulture));
            Append(hash, column.IsNullable ? "1" : "0");
            Append(hash, column.IsIdentity ? "1" : "0");
            Append(hash, column.IsComputed ? "1" : "0");
            Append(
                hash,
                column.PrimaryKeyOrdinal?.ToString(CultureInfo.InvariantCulture) ??
                "-");
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static Type ToClrType(string sqlType)
        => sqlType switch
        {
            "bigint" => typeof(long),
            "binary" or "image" or "timestamp" or "varbinary" => typeof(byte[]),
            "bit" => typeof(bool),
            "date" or "datetime" or "datetime2" or "smalldatetime" => typeof(DateTime),
            "datetimeoffset" => typeof(DateTimeOffset),
            "decimal" or "money" or "numeric" or "smallmoney" => typeof(decimal),
            "float" => typeof(double),
            "int" => typeof(int),
            "real" => typeof(float),
            "smallint" => typeof(short),
            "time" => typeof(TimeSpan),
            "tinyint" => typeof(byte),
            "uniqueidentifier" => typeof(Guid),
            _ => typeof(string),
        };

    private static async Task SafeRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the target failure that caused the atomic rollback.
        }
    }

    private sealed record PreparedSnapshot(
        CsdtRealtimeDomainDefinition Domain,
        CsdtRealtimeSnapshot Snapshot);
}
