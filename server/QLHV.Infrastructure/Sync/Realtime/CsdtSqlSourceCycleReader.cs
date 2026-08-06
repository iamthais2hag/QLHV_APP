using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Infrastructure.Sync.Realtime;

/// <summary>
/// Task 02 source boundary. It is intentionally not registered in the worker:
/// an explicit test/approved caller must supply the fixed source connection.
/// </summary>
internal sealed class CsdtSqlSourceCycleReader : ICsdtSourceCycleReader
{
    private readonly string _sourceConnectionString;
    private readonly CsdtRealtimeSourceReader _reader;

    internal CsdtSqlSourceCycleReader(
        string sourceConnectionString,
        CsdtRealtimeSourceReader reader)
    {
        _sourceConnectionString = sourceConnectionString;
        _reader = reader;
    }

    public async Task<CsdtSourceCapabilityResult> PreflightAsync(
        CsdtAtomicCycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CsdtAtomicCoreDomains.RequireExactScope(request.RequestedDomains);

        await using var connection = new SqlConnection(_sourceConnectionString);
        await connection.OpenAsync(cancellationToken);
        var capability = await connection.QuerySingleAsync<CapabilityRow>(
            new CommandDefinition(
                """
                SELECT
                    DB_NAME() AS DatabaseName,
                    databaseState.snapshot_isolation_state_desc AS SnapshotState,
                    CONVERT(bit, CASE WHEN tracking.database_id IS NULL THEN 0 ELSE 1 END)
                        AS ChangeTrackingEnabled,
                    CHANGE_TRACKING_CURRENT_VERSION() AS CurrentVersion
                FROM sys.databases AS databaseState
                LEFT JOIN sys.change_tracking_databases AS tracking
                  ON tracking.database_id = databaseState.database_id
                WHERE databaseState.database_id = DB_ID();
                """,
                commandTimeout: 30,
                cancellationToken: cancellationToken));

        if (!string.Equals(
                capability.DatabaseName,
                request.Route.SourceDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(CsdtSourceCapabilityStatus.ProfileMismatch);
        }

        if (!string.Equals(capability.SnapshotState, "ON", StringComparison.Ordinal))
        {
            return Result(CsdtSourceCapabilityStatus.SnapshotNotEnabled);
        }

        if (!capability.ChangeTrackingEnabled || !capability.CurrentVersion.HasValue)
        {
            return Result(CsdtSourceCapabilityStatus.CtNotEnabled);
        }

        var tracked = (await connection.QueryAsync<TrackingRow>(
            new CommandDefinition(
                """
                SELECT
                    tableMetadata.name AS TableName,
                    CHANGE_TRACKING_MIN_VALID_VERSION(tableMetadata.object_id)
                        AS MinimumValidVersion
                FROM sys.tables AS tableMetadata
                INNER JOIN sys.schemas AS schemaMetadata
                  ON schemaMetadata.schema_id = tableMetadata.schema_id
                INNER JOIN sys.change_tracking_tables AS tracking
                  ON tracking.object_id = tableMetadata.object_id
                WHERE schemaMetadata.name = N'dbo'
                  AND tableMetadata.name IN
                  (
                      N'DM_DonViGTVT', N'KhoaHoc', N'BaoCaoI', N'NguoiLX',
                      N'NguoiLX_HoSo', N'NguoiLXHS_GiayTo'
                  );
                """,
                commandTimeout: 30,
                cancellationToken: cancellationToken))).AsList();
        if (tracked.Count != CsdtAtomicCoreDomains.ApplyOrder.Count ||
            tracked.Any(row =>
                !CsdtAtomicCoreDomains.Names.Contains(row.TableName) ||
                !row.MinimumValidVersion.HasValue))
        {
            return Result(CsdtSourceCapabilityStatus.CtTableNotTracked);
        }

        var minimums = tracked.ToDictionary(
            row => row.TableName,
            row => row.MinimumValidVersion!.Value,
            StringComparer.Ordinal);
        if (request.OperationMode == CsdtAtomicOperationMode.Incremental &&
            minimums.Values.Any(minimum => request.StartSourceVersion < minimum))
        {
            return new CsdtSourceCapabilityResult(
                CsdtSourceCapabilityStatus.CtCheckpointExpired,
                capability.CurrentVersion,
                minimums,
                null);
        }

        if (!CsdtAtomicMappingContract.ComputeFingerprint()
                .Equals(request.MappingFingerprint))
        {
            return new CsdtSourceCapabilityResult(
                CsdtSourceCapabilityStatus.SchemaMismatch,
                capability.CurrentVersion,
                minimums,
                null);
        }

        var observedSchema = await ComputeSchemaFingerprintAsync(
            connection,
            transaction: null,
            cancellationToken);
        if (!observedSchema.Equals(request.SourceSchemaFingerprint))
        {
            return new CsdtSourceCapabilityResult(
                CsdtSourceCapabilityStatus.SchemaMismatch,
                capability.CurrentVersion,
                minimums,
                observedSchema);
        }

        return new CsdtSourceCapabilityResult(
            CsdtSourceCapabilityStatus.Ready,
            capability.CurrentVersion,
            minimums,
            observedSchema);

        CsdtSourceCapabilityResult Result(CsdtSourceCapabilityStatus status)
            => new(
                status,
                capability.CurrentVersion,
                new Dictionary<string, long>(StringComparer.Ordinal),
                null);
    }

    public async Task<ICsdtMappedTableSourceSnapshot> OpenSnapshotAsync(
        CsdtAtomicCycleRequest request,
        CsdtSourceCapabilityResult preflight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preflight);
        if (!preflight.IsReady)
        {
            throw new CsdtAtomicCycleException(preflight.ErrorCode);
        }

        var connection = new SqlConnection(_sourceConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Snapshot,
                cancellationToken);
            try
            {
                var identity = await connection.ExecuteScalarAsync<string>(
                    new CommandDefinition(
                        "SELECT DB_NAME();",
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                if (!string.Equals(
                        identity,
                        request.Route.SourceDatabaseName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.ProfileMismatch);
                }

                var watermark = await connection.ExecuteScalarAsync<long?>(
                    new CommandDefinition(
                        "SELECT CHANGE_TRACKING_CURRENT_VERSION();",
                        transaction: transaction,
                        commandTimeout: 30,
                        cancellationToken: cancellationToken));
                if (!watermark.HasValue || watermark.Value < request.StartSourceVersion)
                {
                    throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.CtNotEnabled);
                }

                var schema = await ComputeSchemaFingerprintAsync(
                    connection,
                    transaction,
                    cancellationToken);
                if (!schema.Equals(request.SourceSchemaFingerprint))
                {
                    throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.SchemaMismatch);
                }

                return new SqlMappedTableSourceSnapshot(
                    connection,
                    transaction,
                    _reader,
                    request,
                    watermark.Value,
                    schema);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<ControlPlaneFingerprint> ComputeSchemaFingerprintAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in CsdtAtomicCoreDomains.ApplyOrder)
        {
            var domain = CsdtRealtimeDomainCatalog.GetRequired(name);
            var metadata = await CsdtRealtimeSourceReader.ReadMetadataAsync(
                connection,
                domain,
                cancellationToken,
                transaction);
            CsdtRealtimeSourceReader.ValidatePrimaryKey(metadata);
            CsdtRealtimeColumnOwnershipPolicy.GetRequired(name)
                .ValidateSourceSchema(metadata);
            Append(hash, name);
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

        return new ControlPlaneFingerprint(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private sealed class CapabilityRow
    {
        public string DatabaseName { get; init; } = string.Empty;
        public string SnapshotState { get; init; } = string.Empty;
        public bool ChangeTrackingEnabled { get; init; }
        public long? CurrentVersion { get; init; }
    }

    private sealed class TrackingRow
    {
        public string TableName { get; init; } = string.Empty;
        public long? MinimumValidVersion { get; init; }
    }

    private sealed class SqlMappedTableSourceSnapshot :
        ICsdtMappedTableSourceSnapshot
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly CsdtRealtimeSourceReader _reader;
        private readonly CsdtAtomicCycleRequest _request;
        private readonly ControlPlaneFingerprint _schemaFingerprint;
        private bool _staged;
        private bool _disposed;

        internal SqlMappedTableSourceSnapshot(
            SqlConnection connection,
            SqlTransaction transaction,
            CsdtRealtimeSourceReader reader,
            CsdtAtomicCycleRequest request,
            long watermark,
            ControlPlaneFingerprint schemaFingerprint)
        {
            _connection = connection;
            _transaction = transaction;
            _reader = reader;
            _request = request;
            Watermark = watermark;
            _schemaFingerprint = schemaFingerprint;
        }

        public long Watermark { get; }

        public async Task<CsdtStagedCycle> StageCoreAsync(
            CancellationToken cancellationToken = default)
        {
            if (_disposed || _staged)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
            }

            _staged = true;
            var domains = new List<CsdtStagedDomain>(
                CsdtAtomicCoreDomains.ApplyOrder.Count);
            foreach (var name in CsdtAtomicCoreDomains.ApplyOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var domain = CsdtRealtimeDomainCatalog.GetRequired(name);
                var source = await _reader.ReadForwardPartitionSnapshotAsync(
                    _connection,
                    _transaction,
                    domain,
                    _request.Route.MaCSDT,
                    cancellationToken);
                var rows = source.Rows.Rows.Cast<DataRow>()
                    .Select(row => ToStagedRow(row, source.SourceMetadata))
                    .ToArray();
                var keys = rows.Select(row => row.CopyCanonicalKey()).ToArray();
                IReadOnlyList<CsdtStagedChange> changes =
                    _request.OperationMode == CsdtAtomicOperationMode.Incremental
                        ? await ReadChangesAsync(domain, rows, cancellationToken)
                        : [];
                domains.Add(CsdtAtomicStageFactory.CreateDomain(
                    name,
                    _request.OperationMode,
                    rows,
                    changes,
                    keys));
            }

            return new CsdtStagedCycle(
                _request.CycleId,
                _request.Route.SourceProfileCode,
                _request.Route.TargetProfileCode,
                _request.Route.StreamCode,
                _request.Route.MaCSDT,
                _request.StartSourceVersion,
                Watermark,
                _request.MappingFingerprint,
                _request.RouteFingerprint,
                _schemaFingerprint,
                _request.TargetSchemaFingerprint,
                DateTimeOffset.UtcNow,
                keySchemaVersion: 1,
                TargetEqualityProof.ProofId,
                domains,
                CsdtAtomicStageFactory.ComputeCycleKeySetHash(domains));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await _transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // The source transaction is read-only. Closing the connection
                // still ends the snapshot when rollback cannot be delivered.
            }
            finally
            {
                await _transaction.DisposeAsync();
                await _connection.DisposeAsync();
            }
        }

        private async Task<IReadOnlyList<CsdtStagedChange>> ReadChangesAsync(
            CsdtRealtimeDomainDefinition domain,
            IReadOnlyList<CsdtStagedRow> currentRows,
            CancellationToken cancellationToken)
        {
            var current = currentRows
                .Select(row => Convert.ToHexString(row.CopyCanonicalKey()))
                .ToHashSet(StringComparer.Ordinal);
            var changes = await _reader.ReadChangesAsync(
                _connection,
                _transaction,
                domain,
                _request.StartSourceVersion,
                Watermark,
                _request.Route.MaCSDT,
                cancellationToken);
            var result = new List<CsdtStagedChange>(changes.Count);
            foreach (var change in changes)
            {
                var key = CanonicalKeyFromJson(change.KeyJson, domain);
                var operation = change.Operation switch
                {
                    "I" when change.CurrentRowIsInPartition =>
                        CsdtStagedChangeOperation.Insert,
                    "U" when change.CurrentRowIsInPartition =>
                        CsdtStagedChangeOperation.Update,
                    "D" => CsdtStagedChangeOperation.Delete,
                    _ => CsdtStagedChangeOperation.Delete,
                };
                if (operation != CsdtStagedChangeOperation.Delete &&
                    !current.Contains(Convert.ToHexString(key)))
                {
                    throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
                }

                result.Add(new CsdtStagedChange(
                    change.Version,
                    operation,
                    key));
            }

            return result;
        }

        private static CsdtStagedRow ToStagedRow(
            DataRow row,
            CsdtRealtimeTableMetadata metadata)
        {
            var values = row.Table.Columns.Cast<DataColumn>()
                .ToDictionary(
                    column => column.ColumnName,
                    column => row[column] is DBNull ? null : row[column],
                    StringComparer.Ordinal);
            return new CsdtStagedRow(
                CanonicalKeyFromValues(
                    metadata.Domain,
                    metadata.PrimaryKey.Select(column => row[column.Name])),
                values);
        }

        private static byte[] CanonicalKeyFromJson(
            string keyJson,
            CsdtRealtimeDomainDefinition domain)
        {
            using var document = JsonDocument.Parse(keyJson);
            var components = domain.KeyColumns.Select(column =>
            {
                var value = document.RootElement.GetProperty(column);
                return value.ValueKind == JsonValueKind.Number
                    ? value.TryGetInt32(out var intValue)
                        ? CanonicalKeyComponent.FromInt32(intValue)
                        : CanonicalKeyComponent.FromInt64(value.GetInt64())
                    : CanonicalKeyComponent.FromString(value.GetString() ?? string.Empty);
            }).ToArray();
            return CanonicalBusinessKeyEncoder.Encode(1, components).ToArray();
        }

        private static byte[] CanonicalKeyFromValues(
            CsdtRealtimeDomainDefinition domain,
            IEnumerable<object> values)
        {
            var components = values.Select(value => value switch
            {
                int number => CanonicalKeyComponent.FromInt32(number),
                long number => CanonicalKeyComponent.FromInt64(number),
                Guid guid => CanonicalKeyComponent.FromGuid(guid),
                byte[] bytes => CanonicalKeyComponent.FromBinary(bytes),
                _ => CanonicalKeyComponent.FromString(
                    Convert.ToString(value, CultureInfo.InvariantCulture) ??
                    string.Empty),
            }).ToArray();
            if (components.Length != domain.KeyColumns.Count)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
            }

            return CanonicalBusinessKeyEncoder.Encode(1, components).ToArray();
        }
    }
}

internal static class CsdtAtomicMappingContract
{
    internal static ControlPlaneFingerprint ComputeFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var domainName in CsdtAtomicCoreDomains.ApplyOrder)
        {
            Append(domainName);
            var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired(domainName);
            foreach (var rule in policy.Rules.OrderBy(rule => rule.Name, StringComparer.Ordinal))
            {
                Append(rule.Name);
                Append(((int)rule.Owner).ToString(CultureInfo.InvariantCulture));
                Append(rule.AllowInsert ? "1" : "0");
                Append(rule.AllowUpdate ? "1" : "0");
                Append(rule.ReadForward ? "1" : "0");
                Append(((int)rule.MergeRule).ToString(CultureInfo.InvariantCulture));
            }
        }

        Append("ATOMIC_DOSSIER_MERGE_V1");
        Append("V2_DIRECT:TrangThai|MaKhoaHoc|MaBC1");
        Append("SHARED_PRESERVE:TT_XuLy|GiayCNSK|GhiChu|GiaiTrinh");
        Append("TRAINING:01|02|03|04|05|06|07|09|10");
        Append("DOWNSTREAM:00|11|12|13|14|16|17|18|19|90");
        Append(TargetEqualityProof.ProofId);

        return new ControlPlaneFingerprint(hash.GetHashAndReset());

        void Append(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
    }
}
