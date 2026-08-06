using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime.ControlPlane;

/// <summary>
/// Durable, monotonic checkpoint adapter for one fixed target route. It owns
/// only its short SQL transactions; it is intentionally not registered in the
/// production composition root.
/// </summary>
internal sealed class CsdtSqlGlobalCheckpointStore : ICsdtGlobalCheckpointStore
{
    private readonly string _targetConnectionString;
    private readonly CsdtRealtimeRouteDefinition _route;

    internal CsdtSqlGlobalCheckpointStore(
        string targetConnectionString,
        CsdtRealtimeRouteDefinition route)
    {
        if (!CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                route.StreamCode,
                route.SourceProfileCode,
                route.TargetProfileCode,
                out var allowed) ||
            allowed != route)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ProfileMismatch);
        }

        CsdtRealtimeConnectionResolver.ValidateInitialCatalog(
            targetConnectionString,
            route.TargetDatabaseName);
        _targetConnectionString = targetConnectionString;
        _route = route;
    }

    public async Task PublishAsync(
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(checkpoint);
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var conflictCommitted = false;
        try
        {
            await RequireCommittedCycleAndCoverageAsync(
                connection,
                transaction,
                checkpoint,
                cancellationToken);
            var current = await ReadRowAsync(
                connection,
                transaction,
                checkpoint.SourceProfile,
                checkpoint.TargetProfile,
                checkpoint.StreamCode,
                lockForUpdate: true,
                cancellationToken);
            if (current is null)
            {
                await InsertAsync(
                    connection,
                    transaction,
                    checkpoint,
                    cancellationToken);
            }
            else if (current.CheckpointStatus != "ACTIVE")
            {
                conflictCommitted = true;
            }
            else if (current.AppliedSourceVersion > checkpoint.SourceWatermark)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.CheckpointStale);
            }
            else if (!ConfigurationFingerprintsEqual(current, checkpoint))
            {
                await SetConflictAsync(
                    connection,
                    transaction,
                    checkpoint.SourceProfile,
                    checkpoint.TargetProfile,
                    checkpoint.StreamCode,
                    cancellationToken);
                conflictCommitted = true;
            }
            else if (current.AppliedSourceVersion == checkpoint.SourceWatermark)
            {
                if (current.CommittedCycleId != checkpoint.CycleId ||
                    !current.StagedKeySetHash.AsSpan().SequenceEqual(
                        checkpoint.StagedKeySetHash.ToArray()))
                {
                    await SetConflictAsync(
                        connection,
                        transaction,
                        checkpoint.SourceProfile,
                        checkpoint.TargetProfile,
                        checkpoint.StreamCode,
                        cancellationToken);
                    conflictCommitted = true;
                }
                else
                {
                    await TouchVerifiedAsync(
                        connection,
                        transaction,
                        checkpoint,
                        cancellationToken);
                }
            }
            else
            {
                await AdvanceAsync(
                    connection,
                    transaction,
                    checkpoint,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }

        if (conflictCommitted)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.CheckpointConflict);
        }
    }

    public async Task<CsdtGlobalCheckpoint?> ReadAsync(
        string sourceProfile,
        string targetProfile,
        string streamCode,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(sourceProfile, targetProfile, streamCode);
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await ReadRowAsync(
            connection,
            transaction: null,
            sourceProfile,
            targetProfile,
            streamCode,
            lockForUpdate: false,
            cancellationToken);
        return row?.ToCheckpoint();
    }

    public async Task<bool> VerifyAsync(
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(checkpoint);
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var current = await ReadRowAsync(
                connection,
                transaction,
                checkpoint.SourceProfile,
                checkpoint.TargetProfile,
                checkpoint.StreamCode,
                lockForUpdate: true,
                cancellationToken);
            var verified = current is not null &&
                current.CheckpointStatus == "ACTIVE" &&
                current.AppliedSourceVersion == checkpoint.SourceWatermark &&
                current.CommittedCycleId == checkpoint.CycleId &&
                ConfigurationFingerprintsEqual(current, checkpoint) &&
                current.StagedKeySetHash.AsSpan().SequenceEqual(
                    checkpoint.StagedKeySetHash.ToArray());
            if (verified)
            {
                await TouchVerifiedAsync(
                    connection,
                    transaction,
                    checkpoint,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return verified;
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    public async Task MarkConflictAsync(
        string sourceProfile,
        string targetProfile,
        string streamCode,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(sourceProfile, targetProfile, streamCode);
        await using var connection = new SqlConnection(_targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await SetConflictAsync(
                connection,
                transaction,
                sourceProfile,
                targetProfile,
                streamCode,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await SafeRollbackAsync(transaction);
            throw;
        }
    }

    private async Task RequireCommittedCycleAndCoverageAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var cycle = await connection.QuerySingleOrDefaultAsync<CycleRow>(
            new CommandDefinition(
                """
                SELECT
                    CycleId, TargetProfile, SourceProfile, StreamCode,
                    EndSourceVersion, CycleStatus, EnabledDomainCount,
                    MappingFingerprint, RouteFingerprint,
                    SourceSchemaFingerprint, TargetSchemaFingerprint
                FROM dbo.QLHV_CsdtRealtimeCycle WITH (UPDLOCK, HOLDLOCK)
                WHERE CycleId = @CycleId;
                """,
                new { CycleId = checkpoint.CycleId },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        if (cycle is null ||
            cycle.CycleStatus != "TARGET_COMMITTED" ||
            cycle.EnabledDomainCount != CsdtAtomicCoreDomains.ApplyOrder.Count ||
            cycle.EndSourceVersion != checkpoint.SourceWatermark ||
            !string.Equals(cycle.TargetProfile, checkpoint.TargetProfile, StringComparison.Ordinal) ||
            !string.Equals(cycle.SourceProfile, checkpoint.SourceProfile, StringComparison.Ordinal) ||
            !string.Equals(cycle.StreamCode, checkpoint.StreamCode, StringComparison.Ordinal) ||
            !BytesEqual(cycle.MappingFingerprint, checkpoint.MappingFingerprint) ||
            !BytesEqual(cycle.RouteFingerprint, checkpoint.RouteFingerprint) ||
            !BytesEqual(cycle.SourceSchemaFingerprint, checkpoint.SourceSchemaFingerprint) ||
            !BytesEqual(cycle.TargetSchemaFingerprint, checkpoint.TargetSchemaFingerprint))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
        }

        var coverage = (await connection.QueryAsync<CoverageRow>(
            new CommandDefinition(
                """
                SELECT
                    coverage.TableName,
                    coverage.BaselineSourceVersion,
                    coverage.CompletedCycleId,
                    coverage.MappingFingerprint,
                    coverage.RouteFingerprint,
                    coverage.SourceSchemaFingerprint,
                    coverage.TargetSchemaFingerprint,
                    coverage.SourceKeySetHash,
                    coverage.MembershipCount,
                    coverage.IsComplete,
                    domainResult.SourceRowCount,
                    domainResult.SourceKeySetHash AS DomainSourceKeySetHash,
                    domainResult.DomainStatus
                FROM dbo.QLHV_CsdtRealtimeStreamCoverage AS coverage
                    WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.QLHV_CsdtRealtimeCycleDomain AS domainResult
                    WITH (UPDLOCK, HOLDLOCK)
                  ON domainResult.CycleId = coverage.CompletedCycleId
                 AND domainResult.DomainName = coverage.TableName
                WHERE coverage.TargetProfile = @TargetProfile
                  AND coverage.SourceProfile = @SourceProfile
                  AND coverage.StreamCode = @StreamCode;
                """,
                new
                {
                    checkpoint.TargetProfile,
                    checkpoint.SourceProfile,
                    checkpoint.StreamCode,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).ToArray();
        if (coverage.Length != CsdtAtomicCoreDomains.ApplyOrder.Count ||
            !coverage.Select(item => item.TableName)
                .SequenceEqual(
                    CsdtAtomicCoreDomains.ApplyOrder,
                    StringComparer.Ordinal) &&
            !coverage.Select(item => item.TableName)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    CsdtAtomicCoreDomains.ApplyOrder.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal) ||
            coverage.Any(item =>
                !item.IsComplete ||
                item.CompletedCycleId != checkpoint.CycleId ||
                item.BaselineSourceVersion != checkpoint.SourceWatermark ||
                item.MembershipCount != item.SourceRowCount ||
                item.DomainStatus != "COMMITTED" ||
                !item.SourceKeySetHash.AsSpan().SequenceEqual(
                    item.DomainSourceKeySetHash) ||
                !BytesEqual(item.MappingFingerprint, checkpoint.MappingFingerprint) ||
                !BytesEqual(item.RouteFingerprint, checkpoint.RouteFingerprint) ||
                !BytesEqual(item.SourceSchemaFingerprint, checkpoint.SourceSchemaFingerprint) ||
                !BytesEqual(item.TargetSchemaFingerprint, checkpoint.TargetSchemaFingerprint)))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.CoverageIncomplete);
        }
    }

    private static async Task<CheckpointRow?> ReadRowAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sourceProfile,
        string targetProfile,
        string streamCode,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        var lockHint = lockForUpdate ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
        var sql = $"""
            SELECT
                CheckpointId, TargetProfile, SourceProfile, StreamCode,
                AppliedSourceVersion, CommittedCycleId, MappingFingerprint,
                RouteFingerprint, SourceSchemaFingerprint,
                TargetSchemaFingerprint, StagedKeySetHash, CheckpointStatus,
                PublishedAtUtc, VerifiedAtUtc
            FROM dbo.QLHV_CsdtRealtimeCheckpoint{lockHint}
            WHERE TargetProfile = @TargetProfile
              AND SourceProfile = @SourceProfile
              AND StreamCode = @StreamCode;
            """;
        return await connection.QuerySingleOrDefaultAsync<CheckpointRow>(
            new CommandDefinition(
                sql,
                new
                {
                    TargetProfile = targetProfile,
                    SourceProfile = sourceProfile,
                    StreamCode = streamCode,
                },
                transaction,
                commandTimeout: 30,
                cancellationToken: cancellationToken));
    }

    private static Task InsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.QLHV_CsdtRealtimeCheckpoint
            (
                TargetProfile, SourceProfile, StreamCode,
                AppliedSourceVersion, CommittedCycleId,
                MappingFingerprint, RouteFingerprint,
                SourceSchemaFingerprint, TargetSchemaFingerprint,
                StagedKeySetHash, CheckpointStatus, PublishedAtUtc, VerifiedAtUtc
            )
            VALUES
            (
                @TargetProfile, @SourceProfile, @StreamCode,
                @AppliedSourceVersion, @CommittedCycleId,
                @MappingFingerprint, @RouteFingerprint,
                @SourceSchemaFingerprint, @TargetSchemaFingerprint,
                @StagedKeySetHash, 'ACTIVE', SYSUTCDATETIME(), SYSUTCDATETIME()
            );
            """,
            Parameters(checkpoint),
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));

    private static Task AdvanceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.QLHV_CsdtRealtimeCheckpoint WITH (UPDLOCK, HOLDLOCK)
            SET AppliedSourceVersion = @AppliedSourceVersion,
                CommittedCycleId = @CommittedCycleId,
                MappingFingerprint = @MappingFingerprint,
                RouteFingerprint = @RouteFingerprint,
                SourceSchemaFingerprint = @SourceSchemaFingerprint,
                TargetSchemaFingerprint = @TargetSchemaFingerprint,
                StagedKeySetHash = @StagedKeySetHash,
                CheckpointStatus = 'ACTIVE',
                PublishedAtUtc = SYSUTCDATETIME(),
                VerifiedAtUtc = SYSUTCDATETIME()
            WHERE TargetProfile = @TargetProfile
              AND SourceProfile = @SourceProfile
              AND StreamCode = @StreamCode
              AND AppliedSourceVersion < @AppliedSourceVersion;
            """,
            Parameters(checkpoint),
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));

    private static Task TouchVerifiedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CsdtGlobalCheckpoint checkpoint,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.QLHV_CsdtRealtimeCheckpoint
            SET VerifiedAtUtc = SYSUTCDATETIME()
            WHERE TargetProfile = @TargetProfile
              AND SourceProfile = @SourceProfile
              AND StreamCode = @StreamCode
              AND AppliedSourceVersion = @AppliedSourceVersion
              AND CommittedCycleId = @CommittedCycleId
              AND CheckpointStatus = 'ACTIVE';
            """,
            Parameters(checkpoint),
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));

    private static Task SetConflictAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sourceProfile,
        string targetProfile,
        string streamCode,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.QLHV_CsdtRealtimeCheckpoint WITH (UPDLOCK, HOLDLOCK)
            SET CheckpointStatus = 'CONFLICT',
                VerifiedAtUtc = SYSUTCDATETIME()
            WHERE TargetProfile = @TargetProfile
              AND SourceProfile = @SourceProfile
              AND StreamCode = @StreamCode;
            """,
            new
            {
                TargetProfile = targetProfile,
                SourceProfile = sourceProfile,
                StreamCode = streamCode,
            },
            transaction,
            commandTimeout: 30,
            cancellationToken: cancellationToken));

    private static object Parameters(CsdtGlobalCheckpoint checkpoint)
        => new
        {
            checkpoint.TargetProfile,
            checkpoint.SourceProfile,
            checkpoint.StreamCode,
            AppliedSourceVersion = checkpoint.SourceWatermark,
            CommittedCycleId = checkpoint.CycleId,
            MappingFingerprint = checkpoint.MappingFingerprint.ToArray(),
            RouteFingerprint = checkpoint.RouteFingerprint.ToArray(),
            SourceSchemaFingerprint =
                checkpoint.SourceSchemaFingerprint!.ToArray(),
            TargetSchemaFingerprint =
                checkpoint.TargetSchemaFingerprint!.ToArray(),
            StagedKeySetHash = checkpoint.StagedKeySetHash.ToArray(),
        };

    private void ValidateIdentity(CsdtGlobalCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateIdentity(
            checkpoint.SourceProfile,
            checkpoint.TargetProfile,
            checkpoint.StreamCode);
        if (checkpoint.CycleId == Guid.Empty ||
            checkpoint.SourceWatermark < 0 ||
            checkpoint.SourceSchemaFingerprint is null ||
            checkpoint.TargetSchemaFingerprint is null ||
            checkpoint.Status != CsdtCheckpointStatus.Active)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.CheckpointMismatch);
        }
    }

    private void ValidateIdentity(
        string sourceProfile,
        string targetProfile,
        string streamCode)
    {
        if (!string.Equals(
                sourceProfile,
                _route.SourceProfileCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                targetProfile,
                _route.TargetProfileCode,
                StringComparison.Ordinal) ||
            !string.Equals(streamCode, _route.StreamCode, StringComparison.Ordinal))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ProfileMismatch);
        }
    }

    private static bool ConfigurationFingerprintsEqual(
        CheckpointRow row,
        CsdtGlobalCheckpoint checkpoint)
        => BytesEqual(row.MappingFingerprint, checkpoint.MappingFingerprint) &&
           BytesEqual(row.RouteFingerprint, checkpoint.RouteFingerprint) &&
           BytesEqual(
               row.SourceSchemaFingerprint,
               checkpoint.SourceSchemaFingerprint) &&
           BytesEqual(
               row.TargetSchemaFingerprint,
               checkpoint.TargetSchemaFingerprint);

    private static bool BytesEqual(
        byte[] left,
        ControlPlaneFingerprint? right)
        => right is not null && left.AsSpan().SequenceEqual(right.ToArray());

    private static async Task SafeRollbackAsync(SqlTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the checkpoint failure that caused rollback.
        }
    }

    private sealed class CycleRow
    {
        public Guid CycleId { get; init; }
        public string TargetProfile { get; init; } = string.Empty;
        public string SourceProfile { get; init; } = string.Empty;
        public string StreamCode { get; init; } = string.Empty;
        public long EndSourceVersion { get; init; }
        public string CycleStatus { get; init; } = string.Empty;
        public int EnabledDomainCount { get; init; }
        public byte[] MappingFingerprint { get; init; } = [];
        public byte[] RouteFingerprint { get; init; } = [];
        public byte[] SourceSchemaFingerprint { get; init; } = [];
        public byte[] TargetSchemaFingerprint { get; init; } = [];
    }

    private sealed class CoverageRow
    {
        public string TableName { get; init; } = string.Empty;
        public long BaselineSourceVersion { get; init; }
        public Guid? CompletedCycleId { get; init; }
        public byte[] MappingFingerprint { get; init; } = [];
        public byte[] RouteFingerprint { get; init; } = [];
        public byte[] SourceSchemaFingerprint { get; init; } = [];
        public byte[] TargetSchemaFingerprint { get; init; } = [];
        public byte[] SourceKeySetHash { get; init; } = [];
        public long MembershipCount { get; init; }
        public bool IsComplete { get; init; }
        public long SourceRowCount { get; init; }
        public byte[] DomainSourceKeySetHash { get; init; } = [];
        public string DomainStatus { get; init; } = string.Empty;
    }

    private sealed class CheckpointRow
    {
        public long CheckpointId { get; init; }
        public string TargetProfile { get; init; } = string.Empty;
        public string SourceProfile { get; init; } = string.Empty;
        public string StreamCode { get; init; } = string.Empty;
        public long AppliedSourceVersion { get; init; }
        public Guid CommittedCycleId { get; init; }
        public byte[] MappingFingerprint { get; init; } = [];
        public byte[] RouteFingerprint { get; init; } = [];
        public byte[] SourceSchemaFingerprint { get; init; } = [];
        public byte[] TargetSchemaFingerprint { get; init; } = [];
        public byte[] StagedKeySetHash { get; init; } = [];
        public string CheckpointStatus { get; init; } = string.Empty;
        public DateTimeOffset PublishedAtUtc { get; init; }
        public DateTimeOffset? VerifiedAtUtc { get; init; }

        public CsdtGlobalCheckpoint ToCheckpoint()
            => new(
                CommittedCycleId,
                SourceProfile,
                TargetProfile,
                StreamCode,
                AppliedSourceVersion,
                new ControlPlaneFingerprint(MappingFingerprint),
                new ControlPlaneFingerprint(RouteFingerprint),
                new ControlPlaneFingerprint(StagedKeySetHash),
                new ControlPlaneFingerprint(SourceSchemaFingerprint),
                new ControlPlaneFingerprint(TargetSchemaFingerprint),
                CheckpointStatus switch
                {
                    "ACTIVE" => CsdtCheckpointStatus.Active,
                    "CONFLICT" => CsdtCheckpointStatus.Conflict,
                    "DISABLED" => CsdtCheckpointStatus.Disabled,
                    _ => throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.CheckpointMismatch),
                },
                PublishedAtUtc,
                VerifiedAtUtc);
    }
}
