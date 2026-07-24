using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Read and guarded write access to QLHV_APP.dbo.App_HocVien.
/// Writes use SqlBulkCopy into a temp staging table and MERGE in a transaction; no physical delete.
/// </summary>
public sealed class QlhvHocVienTargetRepository :
    IQlhvHocVienTargetRepository,
    IQlhvImportWriteRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;
    private readonly SyncExecutionOptions _execution;

    public QlhvHocVienTargetRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options,
        IOptions<SyncExecutionOptions> execution)
    {
        _connections = connections;
        _options = options.Value;
        _execution = execution.Value;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.App_HocVien WHERE IsDeleted = 0;",
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<int>(command);
    }

    public async Task<IReadOnlyCollection<string>> GetExistingSourceKeysAsync(
        string sourceProfileCode,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken = default)
    {
        if (sourceMaDks is null || sourceMaDks.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalizedProfile = NormalizeRequired(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant();
        var normalizedSourceMaDks = sourceMaDks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSourceMaDks.Length == 0)
        {
            return Array.Empty<string>();
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            @"
SELECT SourceProfileCode, SourceMaDK
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND SourceMaDK IN @SourceMaDks;",
            new
            {
                SourceProfileCode = normalizedProfile,
                SourceMaDks = normalizedSourceMaDks,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var keys = await connection.QueryAsync<ExistingSourceKeyRow>(command);
        return keys
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.SourceProfileCode) &&
                !string.IsNullOrWhiteSpace(row.SourceMaDK))
            .Select(row => HocVienSourceIdentityKey.Create(row.SourceProfileCode, row.SourceMaDK))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetExistingSourceHashesAsync(
        string sourceProfileCode,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken = default)
    {
        if (sourceMaDks is null || sourceMaDks.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var normalizedProfile = NormalizeRequired(sourceProfileCode, nameof(sourceProfileCode)).ToUpperInvariant();
        var normalizedSourceMaDks = sourceMaDks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSourceMaDks.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var command = new CommandDefinition(
            @"
SELECT SourceProfileCode, SourceMaDK, V2RowHash
FROM dbo.App_HocVien
WHERE SourceProfileCode = @SourceProfileCode
  AND SourceMaDK IN @SourceMaDks;",
            new
            {
                SourceProfileCode = normalizedProfile,
                SourceMaDks = normalizedSourceMaDks,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<ExistingSourceHashRow>(command);
        return rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.SourceProfileCode) &&
                !string.IsNullOrWhiteSpace(row.SourceMaDK))
            .GroupBy(
                row => HocVienSourceIdentityKey.Create(row.SourceProfileCode, row.SourceMaDK),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().V2RowHash ?? string.Empty,
                StringComparer.Ordinal);
    }

    public async Task<QlhvHocVienTargetDiagnosticsDto> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);

        var schema = await connection.QuerySingleAsync<TargetSchemaDiagnosticsRow>(new CommandDefinition(
            TargetSchemaDiagnosticsSql,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        var columns = (await connection.QueryAsync<RequiredColumnCheckDto>(new CommandDefinition(
            RequiredColumnsSql,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).ToList();

        int? targetRows = null;
        SoCmtLengthDiagnosticsDto? soCccdLength = null;
        if (schema.AppHocVienExists)
        {
            var activeFilter = schema.IsDeletedColumnExists ? " WHERE IsDeleted = 0" : string.Empty;
            targetRows = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM dbo.App_HocVien" + activeFilter + ";",
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            if (columns.Any(c => string.Equals(c.ColumnName, "SoCCCD", StringComparison.OrdinalIgnoreCase) && c.Exists))
            {
                var soCccdRows = (await connection.QueryAsync<SourceValueDistributionDto>(new CommandDefinition(
                    BuildTargetSoCccdLengthSql(activeFilter),
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken))).ToList();

                soCccdLength = new SoCmtLengthDiagnosticsDto
                {
                    NineDigits = GetBucket(soCccdRows, "9"),
                    TwelveDigits = GetBucket(soCccdRows, "12"),
                    Other = GetBucket(soCccdRows, "other"),
                    NullOrEmpty = GetBucket(soCccdRows, "null-empty"),
                };
            }
        }

        return new QlhvHocVienTargetDiagnosticsDto
        {
            CheckedAtUtc = DateTime.UtcNow,
            AppHocVienExists = schema.AppHocVienExists,
            AppDongBoLogExists = schema.AppDongBoLogExists,
            RequiredColumns = columns,
            TargetRows = targetRows,
            TargetRowsUseIsDeletedFilter = schema.IsDeletedColumnExists,
            SoCccdLength = soCccdLength,
        };
    }

    public async Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default)
    {
        if (_options.DryRun)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: Sync:DryRun = true.");
        }

        if (!_execution.EnableTargetWrites)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        if (rows.Count == 0)
        {
            return UpsertCounts.Empty;
        }

        ValidateSourceIdentity(rows);

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(
            ct => UpsertBatchCoreAsync(connectionString, rows, ct),
            cancellationToken);
    }

    public async Task<QlhvImportGuardedUpsertResult> UpsertWithGuardsAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesEnabled();

        if (rows.Count == 0)
        {
            return QlhvImportGuardedUpsertResult.Empty;
        }

        ValidateSourceIdentity(rows);

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(
            ct => UpsertCoreAsync(
                connectionString,
                rows,
                enforceImportGuards: true,
                cancellationToken: ct),
            cancellationToken);
    }

    public async Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
        string sourceProfileCode,
        IReadOnlyList<QlhvImportHocVienWriteModel> rows,
        CancellationToken cancellationToken = default)
        => await FullSyncAsync(
            sourceProfileCode,
            new QlhvImportFullSyncPayload(
                Array.Empty<QlhvImportKhoaHocWriteModel>(),
                Array.Empty<QlhvImportGiaoVienWriteModel>(),
                Array.Empty<QlhvImportKhoaHocGiaoVienWriteModel>(),
                rows,
                ExecutableDomains: [QlhvImportDomains.HocVien]),
            cancellationToken);

    public async Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
        string sourceProfileCode,
        QlhvImportFullSyncPayload payload,
        CancellationToken cancellationToken = default)
    {
        EnsureWritesEnabled();
        ArgumentNullException.ThrowIfNull(payload);

        var normalizedProfile = NormalizeRequired(sourceProfileCode, nameof(sourceProfileCode))
            .ToUpperInvariant();
        if (!string.Equals(normalizedProfile, CsdtConnectionProfileCodes.CsdtOto, StringComparison.Ordinal) &&
            !string.Equals(normalizedProfile, CsdtConnectionProfileCodes.CsdtMoto, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Full sync chi duoc ghi vao partition CSDT_OTO hoac CSDT_MOTO.");
        }

        var selectedDomains = payload.DomainsToExecute
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var unknownDomains = selectedDomains
            .Except(QlhvImportDomains.Ordered, StringComparer.Ordinal)
            .ToArray();
        if (unknownDomains.Length > 0)
        {
            throw new InvalidOperationException(
                "Full sync nhan domain khong hop le: " + string.Join(", ", unknownDomains) + ".");
        }

        if (!selectedDomains.Contains(QlhvImportDomains.HocVien))
        {
            return RequiredDomainRejected(
                payload,
                "HocVien la domain bat buoc va phai duoc chon de full sync.");
        }

        if (payload.HocVienRows.Count == 0)
        {
            return RequiredDomainRejected(
                payload,
                "Nguon hoc vien rong; repository tu choi full sync de bao ve partition.");
        }

        try
        {
            ValidateFullSyncSourceIdentity(normalizedProfile, payload.HocVienRows);
        }
        catch (InvalidOperationException ex)
        {
            return RequiredDomainRejected(payload, ex.Message);
        }

        var connectionString = await ResolveUsableTargetAsync(cancellationToken);
        return await FullSyncDomainsAsync(
            connectionString,
            normalizedProfile,
            payload,
            selectedDomains,
            cancellationToken);
    }

    private async Task<QlhvImportFullSyncWriteResult> FullSyncDomainsAsync(
        string connectionString,
        string sourceProfileCode,
        QlhvImportFullSyncPayload payload,
        IReadOnlySet<string> selectedDomains,
        CancellationToken cancellationToken)
    {
        var domainResults = new List<DomainTransactionResult>(QlhvImportDomains.Ordered.Count);

        domainResults.Add(await ExecuteOptionalDomainAsync(
            QlhvImportDomains.KhoaHoc,
            payload.KhoaHocRows.Count,
            selectedDomains,
            payload.DomainSkipReasons,
            async ct =>
            {
                ValidateEntitySourceIdentity(
                    sourceProfileCode,
                    payload.KhoaHocRows,
                    row => row.SourceProfileCode,
                    row => row.SourceMaKhoaHoc,
                    "KhoaHoc");
                ValidateKhoaHocRequiredValues(payload.KhoaHocRows);
                return await FullSyncEntityDomainCoreAsync(
                    connectionString,
                    sourceProfileCode,
                    QlhvImportDomains.KhoaHoc,
                    payload.KhoaHocRows.Count,
                    QlhvCourseTeacherFullSnapshotSyncSql.CreateKhoaHocStagingTable,
                    QlhvCourseTeacherFullSnapshotSyncSql.KhoaHocStagingTableName,
                    BuildKhoaHocStagingTable(payload.KhoaHocRows),
                    QlhvCourseTeacherFullSnapshotSyncSql.KhoaHocAtomicGuard,
                    QlhvCourseTeacherFullSnapshotSyncSql.MergeKhoaHoc,
                    QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteKhoaHoc,
                    QlhvCourseTeacherFullSnapshotSyncSql.DropKhoaHocStagingTable,
                    QlhvDataVersionSql.IncrementAfterKhoaHocCommit,
                    ct);
            },
            cancellationToken));

        domainResults.Add(await ExecuteOptionalDomainAsync(
            QlhvImportDomains.GiaoVien,
            payload.GiaoVienRows.Count,
            selectedDomains,
            payload.DomainSkipReasons,
            async ct =>
            {
                ValidateEntitySourceIdentity(
                    sourceProfileCode,
                    payload.GiaoVienRows,
                    row => row.SourceProfileCode,
                    row => row.SourceMaGV,
                    "GiaoVien");
                ValidateGiaoVienRequiredValues(payload.GiaoVienRows);
                return await FullSyncEntityDomainCoreAsync(
                    connectionString,
                    sourceProfileCode,
                    QlhvImportDomains.GiaoVien,
                    payload.GiaoVienRows.Count,
                    QlhvCourseTeacherFullSnapshotSyncSql.CreateGiaoVienStagingTable,
                    QlhvCourseTeacherFullSnapshotSyncSql.GiaoVienStagingTableName,
                    BuildGiaoVienStagingTable(payload.GiaoVienRows),
                    QlhvCourseTeacherFullSnapshotSyncSql.GiaoVienAtomicGuard,
                    QlhvCourseTeacherFullSnapshotSyncSql.MergeGiaoVien,
                    QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteGiaoVien,
                    QlhvCourseTeacherFullSnapshotSyncSql.DropGiaoVienStagingTable,
                    QlhvDataVersionSql.IncrementAfterGiaoVienCommit,
                    ct);
            },
            cancellationToken));

        var khoaHocReady = IsSuccessfulDomain(domainResults[0].Result);
        var giaoVienReady = IsSuccessfulDomain(domainResults[1].Result);
        if (selectedDomains.Contains(QlhvImportDomains.Relation) &&
            (!khoaHocReady || !giaoVienReady))
        {
            domainResults.Add(SkippedDomain(
                QlhvImportDomains.Relation,
                payload.RelationRows.Count,
                QlhvImportDomainStatuses.SkippedDependencyNotReady,
                "Quan he bi bo qua vi KhoaHoc hoac GiaoVien chua hoan tat an toan."));
        }
        else
        {
            domainResults.Add(await ExecuteOptionalDomainAsync(
                QlhvImportDomains.Relation,
                payload.RelationRows.Count,
                selectedDomains,
                payload.DomainSkipReasons,
                async ct =>
                {
                    ValidateEntitySourceIdentity(
                        sourceProfileCode,
                        payload.RelationRows,
                        row => row.SourceProfileCode,
                        row => row.SourceMaLichLV.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "KhoaHoc_GiaoVien");
                    ValidateRelationRequiredValues(payload.RelationRows);
                    return await FullSyncEntityDomainCoreAsync(
                        connectionString,
                        sourceProfileCode,
                        QlhvImportDomains.Relation,
                        payload.RelationRows.Count,
                        QlhvCourseTeacherFullSnapshotSyncSql.CreateRelationStagingTable,
                        QlhvCourseTeacherFullSnapshotSyncSql.RelationStagingTableName,
                        BuildRelationStagingTable(payload.RelationRows),
                        QlhvCourseTeacherFullSnapshotSyncSql.RelationAtomicGuard,
                        QlhvCourseTeacherFullSnapshotSyncSql.MergeRelation,
                        QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteRelation,
                        QlhvCourseTeacherFullSnapshotSyncSql.DropRelationStagingTable,
                        QlhvDataVersionSql.IncrementAfterRelationCommit,
                        ct);
                },
                cancellationToken));
        }

        DomainTransactionResult hocVienResult;
        try
        {
            var appliedDomains = domainResults
                .Where(result => IsSuccessfulDomain(result.Result))
                .Select(result => result.Result.Domain)
                .ToHashSet(StringComparer.Ordinal);
            hocVienResult = await FullSyncHocVienDomainCoreAsync(
                connectionString,
                sourceProfileCode,
                payload,
                appliedDomains,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            hocVienResult = FailedDomain(
                QlhvImportDomains.HocVien,
                payload.HocVienRows.Count,
                $"HocVien transaction that bai: {ex.GetType().Name}.");
        }

        domainResults.Add(hocVienResult);
        return BuildFullSyncResult(domainResults);
    }

    private async Task<DomainTransactionResult> ExecuteOptionalDomainAsync(
        string domain,
        int sourceRows,
        IReadOnlySet<string> selectedDomains,
        IReadOnlyDictionary<string, string> skipReasons,
        Func<CancellationToken, Task<DomainTransactionResult>> execute,
        CancellationToken cancellationToken)
    {
        if (!selectedDomains.Contains(domain))
        {
            skipReasons.TryGetValue(domain, out var reason);
            return SkippedDomain(
                domain,
                sourceRows,
                ResolveSkippedStatus(reason),
                string.IsNullOrWhiteSpace(reason)
                    ? $"{domain} khong duoc chon trong plan va da duoc bo qua an toan."
                    : reason);
        }

        if (sourceRows == 0)
        {
            return SkippedDomain(
                domain,
                sourceRows,
                QlhvImportDomainStatuses.SkippedSourceNotReady,
                $"Nguon {domain} rong; khong coi la snapshot hop le va khong soft-delete target.");
        }

        try
        {
            return await execute(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailedDomain(
                domain,
                sourceRows,
                $"{domain} transaction that bai: {ex.GetType().Name}.");
        }
    }

    private async Task<DomainTransactionResult> FullSyncEntityDomainCoreAsync(
        string connectionString,
        string sourceProfileCode,
        string domain,
        int sourceRows,
        string createStagingSql,
        string stagingTableName,
        DataTable stagingRows,
        string guardSql,
        string mergeSql,
        string softDeleteSql,
        string dropStagingSql,
        string incrementVersionSql,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                createStagingSql,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await BulkCopyAsync(
                connection,
                transaction,
                stagingTableName,
                stagingRows,
                cancellationToken);

            var guard = await connection.QuerySingleAsync<EntityDomainGuardRow>(new CommandDefinition(
                guardSql,
                new { SourceProfileCode = sourceProfileCode },
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            if (guard.StagedRows != sourceRows || guard.HasConflicts)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedDomain(
                    domain,
                    sourceRows,
                    BuildGuardFailureMessage(domain, sourceRows, guard),
                    guard);
            }

            var mergeActions = (await connection.QueryAsync<string>(new CommandDefinition(
                mergeSql,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToList();
            var softDeleteActions = (await connection.QueryAsync<string>(new CommandDefinition(
                softDeleteSql,
                new { SourceProfileCode = sourceProfileCode },
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToList();
            var counts = BuildWriteCounts(sourceRows, mergeActions, softDeleteActions);

            await connection.ExecuteAsync(new CommandDefinition(
                incrementVersionSql,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                dropStagingSql,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(CancellationToken.None);

            return SuccessfulDomain(domain, counts);
        }
        catch
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private async Task<DomainTransactionResult> FullSyncHocVienDomainCoreAsync(
        string connectionString,
        string sourceProfileCode,
        QlhvImportFullSyncPayload payload,
        IReadOnlySet<string> appliedOptionalDomains,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                QlhvFullSnapshotSyncSql.CreateStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await BulkCopyAsync(
                connection,
                transaction,
                QlhvFullSnapshotSyncSql.StagingTableName,
                BuildFullSyncStagingTable(payload.HocVienRows),
                cancellationToken);

            var guard = await connection.QuerySingleAsync<HocVienDomainGuardRow>(new CommandDefinition(
                QlhvFullSnapshotSyncSql.AtomicGuard,
                new { SourceProfileCode = sourceProfileCode },
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            if (guard.StagedRows != payload.HocVienRows.Count || guard.HasConflicts)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return FailedDomain(
                    QlhvImportDomains.HocVien,
                    payload.HocVienRows.Count,
                    BuildHocVienGuardFailureMessage(payload.HocVienRows.Count, guard),
                    guard);
            }

            var mergeActions = (await connection.QueryAsync<string>(new CommandDefinition(
                QlhvFullSnapshotSyncSql.Merge,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToList();
            var softDeleteActions = (await connection.QueryAsync<string>(new CommandDefinition(
                QlhvFullSnapshotSyncSql.SoftDeleteMissing,
                new { SourceProfileCode = sourceProfileCode },
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken))).ToList();
            var counts = BuildWriteCounts(
                payload.HocVienRows.Count,
                mergeActions,
                softDeleteActions);

            if (!string.IsNullOrWhiteSpace(payload.BackupSnapshotToken))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    QlhvDataVersionSql.UpsertPartitionStateAfterSuccessfulFullSync,
                    new
                    {
                        SourceType =
                            QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(sourceProfileCode),
                        SourceProfileCode = sourceProfileCode,
                        AppliedBackupSnapshotToken = payload.BackupSnapshotToken.Trim(),
                        HocVienRows = payload.HocVienRows.Count,
                        KhoaHocRows = payload.KhoaHocRows.Count,
                        GiaoVienRows = payload.GiaoVienRows.Count,
                        KhoaHocGiaoVienRows = payload.RelationRows.Count,
                        KhoaHocApplied =
                            appliedOptionalDomains.Contains(QlhvImportDomains.KhoaHoc),
                        GiaoVienApplied =
                            appliedOptionalDomains.Contains(QlhvImportDomains.GiaoVien),
                        RelationApplied =
                            appliedOptionalDomains.Contains(QlhvImportDomains.Relation),
                    },
                    transaction: transaction,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                QlhvDataVersionSql.IncrementAfterHocVienCommit,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                QlhvFullSnapshotSyncSql.DropStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(CancellationToken.None);

            return SuccessfulDomain(QlhvImportDomains.HocVien, counts);
        }
        catch
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private async Task<UpsertCounts> UpsertBatchCoreAsync(
        string connectionString,
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken)
        => (await UpsertCoreAsync(
            connectionString,
            rows,
            enforceImportGuards: false,
            cancellationToken)).Counts;

    private async Task<QlhvImportGuardedUpsertResult> UpsertCoreAsync(
        string connectionString,
        IReadOnlyList<HocVienTargetWriteModel> rows,
        bool enforceImportGuards,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            enforceImportGuards ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.CreateStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = HocVienTargetMergeSql.StagingTableName,
                BatchSize = Math.Max(1, _options.BatchSize),
                BulkCopyTimeout = _options.TimeoutSeconds,
            })
            {
                using var table = BuildStagingTable(rows);
                foreach (DataColumn column in table.Columns)
                {
                    bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                }

                await bulkCopy.WriteToServerAsync(table, cancellationToken);
            }

            if (enforceImportGuards)
            {
                var guard = await connection.QuerySingleAsync<AtomicImportGuardRow>(new CommandDefinition(
                    AtomicImportGuardSql,
                    transaction: transaction,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken));
                if (guard.TargetMaDkConflictsOtherProfiles > 0 ||
                    guard.SoftDeletedIdentityConflicts > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new QlhvImportGuardedUpsertResult(
                        UpsertCounts.Empty,
                        guard.TargetMaDkConflictsOtherProfiles,
                        guard.SoftDeletedIdentityConflicts);
                }
            }

            var actions = await connection.QueryAsync<string>(new CommandDefinition(
                HocVienTargetMergeSql.MergeStatement,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                HocVienTargetMergeSql.DropStagingTable,
                transaction: transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            var actionList = actions.ToList();
            var inserted = actionList.Count(a => string.Equals(a, "INSERT", StringComparison.OrdinalIgnoreCase));
            var updated = actionList.Count(a => string.Equals(a, "UPDATE", StringComparison.OrdinalIgnoreCase));
            var skipped = Math.Max(0, rows.Count - inserted - updated);
            return new QlhvImportGuardedUpsertResult(
                new UpsertCounts(inserted, updated, skipped),
                0,
                0);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void EnsureWritesEnabled()
    {
        if (_options.DryRun)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: Sync:DryRun = true.");
        }

        if (!_execution.EnableTargetWrites)
        {
            throw new InvalidOperationException(
                "Ghi vao QLHV_APP bi chan: SyncExecution.EnableTargetWrites = false.");
        }
    }

    private static DataTable BuildStagingTable(IReadOnlyList<HocVienTargetWriteModel> rows)
    {
        var table = new DataTable();
        table.Columns.Add("SourceProfileCode", typeof(string));
        table.Columns.Add("SourceMaDK", typeof(string));
        table.Columns.Add("SourceSystem", typeof(string));
        table.Columns.Add("SourceVersion", typeof(string));
        table.Columns.Add("MaDK", typeof(string));
        table.Columns.Add("MaKhoa", typeof(string));
        table.Columns.Add("TenKhoa", typeof(string));
        table.Columns.Add("MaHangDT", typeof(string));
        table.Columns.Add("HangGPLXHoc", typeof(string));
        table.Columns.Add("HoTen", typeof(string));
        table.Columns.Add("NgaySinh", typeof(DateTime));
        table.Columns.Add("GioiTinh", typeof(string));
        table.Columns.Add("SoCCCD", typeof(string));
        table.Columns.Add("DiaChiThuongTru", typeof(string));
        table.Columns.Add("SoGPLXDaCo", typeof(string));
        table.Columns.Add("HangGPLXDaCo", typeof(string));
        table.Columns.Add("NguoiNhanHoSo", typeof(string));
        table.Columns.Add("SourceOfTruth", typeof(string));
        table.Columns.Add("V2RowHash", typeof(string));

        foreach (var row in rows)
        {
            var sourceProfileCode = NormalizeRequired(row.SourceProfileCode, nameof(row.SourceProfileCode))
                .ToUpperInvariant();
            var sourceMaDK = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            var sourceSystem = NormalizeRequired(row.SourceSystem, nameof(row.SourceSystem))
                .ToUpperInvariant();
            var hash = string.IsNullOrWhiteSpace(row.V2RowHash)
                ? V2RowHashCalculator.Compute(row)
                : row.V2RowHash;

            table.Rows.Add(
                sourceProfileCode,
                sourceMaDK,
                sourceSystem,
                Db(row.SourceVersion),
                row.MaDK,
                Db(row.MaKhoa),
                Db(row.TenKhoa),
                Db(row.MaHangDT),
                Db(row.HangGPLXHoc),
                Db(row.HoTen),
                Db(row.NgaySinh),
                Db(row.GioiTinh),
                Db(row.SoCCCD),
                Db(row.DiaChiThuongTru),
                Db(row.SoGPLXDaCo),
                Db(row.HangGPLXDaCo),
                Db(row.NguoiNhanHoSo),
                row.SourceOfTruth,
                hash);
        }

        return table;
    }

    private static DataTable BuildFullSyncStagingTable(
        IReadOnlyList<QlhvImportHocVienWriteModel> rows)
    {
        var table = new DataTable();
        table.Columns.Add("SourceProfileCode", typeof(string));
        table.Columns.Add("SourceMaDK", typeof(string));
        table.Columns.Add("SourceSystem", typeof(string));
        table.Columns.Add("SourceVersion", typeof(string));
        table.Columns.Add("MaDK", typeof(string));
        table.Columns.Add("MaKhoa", typeof(string));
        table.Columns.Add("TenKhoa", typeof(string));
        table.Columns.Add("MaHangDT", typeof(string));
        table.Columns.Add("HangGPLXHoc", typeof(string));
        table.Columns.Add("HoTen", typeof(string));
        table.Columns.Add("NgaySinh", typeof(DateTime));
        table.Columns.Add("GioiTinh", typeof(string));
        table.Columns.Add("SoCCCD", typeof(string));
        table.Columns.Add("DiaChiThuongTru", typeof(string));
        table.Columns.Add("SoGPLXDaCo", typeof(string));
        table.Columns.Add("HangGPLXDaCo", typeof(string));
        table.Columns.Add("NguoiNhanHoSo", typeof(string));
        table.Columns.Add("AnhRelativePath", typeof(string));
        table.Columns.Add("ChatLuongAnh", typeof(int));
        table.Columns.Add("NgayThuNhanAnh", typeof(DateTime));
        table.Columns.Add("NguoiThuNhanAnh", typeof(string));
        table.Columns.Add("SourceOfTruth", typeof(string));
        table.Columns.Add("V2RowHash", typeof(string));

        foreach (var row in rows)
        {
            table.Rows.Add(
                row.SourceProfileCode,
                row.SourceMaDK,
                row.SourceSystem,
                Db(row.SourceVersion),
                row.MaDK,
                Db(row.MaKhoa),
                Db(row.TenKhoa),
                Db(row.MaHangDT),
                Db(row.HangGPLXHoc),
                Db(row.HoTen),
                Db(row.NgaySinh),
                Db(row.GioiTinh),
                Db(row.SoCCCD),
                Db(row.DiaChiThuongTru),
                Db(row.SoGPLXDaCo),
                Db(row.HangGPLXDaCo),
                Db(row.NguoiNhanHoSo),
                Db(row.AnhRelativePath),
                Db(row.ChatLuongAnh),
                Db(row.NgayThuNhanAnh),
                Db(row.NguoiThuNhanAnh),
                row.SourceOfTruth,
                row.V2RowHash);
        }

        return table;
    }

    private async Task BulkCopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string destinationTableName,
        DataTable table,
        CancellationToken cancellationToken)
    {
        using (table)
        {
            if (table.Rows.Count == 0)
            {
                return;
            }

            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction)
            {
                DestinationTableName = destinationTableName,
                BatchSize = Math.Max(1, _options.BatchSize),
                BulkCopyTimeout = _options.TimeoutSeconds,
            };
            foreach (DataColumn column in table.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(table, cancellationToken);
        }
    }

    private static DataTable BuildKhoaHocStagingTable(
        IReadOnlyList<QlhvImportKhoaHocWriteModel> rows)
        => BuildEntityStagingTable(rows);

    private static DataTable BuildGiaoVienStagingTable(
        IReadOnlyList<QlhvImportGiaoVienWriteModel> rows)
        => BuildEntityStagingTable(rows);

    private static DataTable BuildRelationStagingTable(
        IReadOnlyList<QlhvImportKhoaHocGiaoVienWriteModel> rows)
        => BuildEntityStagingTable(rows);

    private static DataTable BuildEntityStagingTable<T>(IReadOnlyList<T> rows)
    {
        var properties = typeof(T).GetProperties(System.Reflection.BindingFlags.Instance |
                                                 System.Reflection.BindingFlags.Public);
        var table = new DataTable();
        foreach (var property in properties)
        {
            table.Columns.Add(
                property.Name,
                Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        }

        foreach (var row in rows)
        {
            table.Rows.Add(properties
                .Select(property => property.GetValue(row) ?? DBNull.Value)
                .ToArray());
        }

        return table;
    }

    private static QlhvEntityWriteCounts BuildWriteCounts(
        int sourceRows,
        IReadOnlyCollection<string> mergeActions,
        IReadOnlyCollection<string> softDeleteActions)
    {
        var inserted = mergeActions.Count(action =>
            string.Equals(action, "INSERT", StringComparison.OrdinalIgnoreCase));
        var updated = mergeActions.Count(action =>
            string.Equals(action, "UPDATE", StringComparison.OrdinalIgnoreCase));
        var reactivated = mergeActions.Count(action =>
            string.Equals(action, "REACTIVATE", StringComparison.OrdinalIgnoreCase));
        var softDeleted = softDeleteActions.Count(action =>
            string.Equals(action, "SOFT_DELETE", StringComparison.OrdinalIgnoreCase));
        return new QlhvEntityWriteCounts(
            sourceRows,
            inserted,
            updated,
            reactivated,
            softDeleted,
            Math.Max(0, sourceRows - inserted - updated - reactivated));
    }

    private static QlhvImportFullSyncWriteResult BuildFullSyncResult(
        IReadOnlyList<DomainTransactionResult> domainResults)
    {
        QlhvEntityWriteCounts Counts(string domain) =>
            domainResults.FirstOrDefault(item =>
                string.Equals(item.Result.Domain, domain, StringComparison.Ordinal))?.Result.Counts ??
            QlhvEntityWriteCounts.Empty;

        return new QlhvImportFullSyncWriteResult(
            Counts(QlhvImportDomains.KhoaHoc),
            Counts(QlhvImportDomains.GiaoVien),
            Counts(QlhvImportDomains.Relation),
            Counts(QlhvImportDomains.HocVien),
            domainResults.Sum(item => item.InvalidSourceProfileRows),
            domainResults.Sum(item => item.InvalidTargetIdentityRows),
            domainResults.Sum(item => item.DuplicateTargetIdentityRows),
            domainResults.Sum(item => item.RelationConflicts),
            domainResults.Sum(item => item.EmptyPartitionRiskGroups),
            domainResults.Sum(item => item.NaturalKeyConflicts))
        {
            DomainResults = domainResults.Select(item => item.Result).ToArray(),
        };
    }

    private static QlhvImportFullSyncWriteResult RequiredDomainRejected(
        QlhvImportFullSyncPayload payload,
        string message)
    {
        var results = new List<DomainTransactionResult>
        {
            SkippedDomain(
                QlhvImportDomains.KhoaHoc,
                payload.KhoaHocRows.Count,
                QlhvImportDomainStatuses.SkippedDependencyNotReady,
                "KhoaHoc khong chay vi HocVien bat buoc khong san sang."),
            SkippedDomain(
                QlhvImportDomains.GiaoVien,
                payload.GiaoVienRows.Count,
                QlhvImportDomainStatuses.SkippedDependencyNotReady,
                "GiaoVien khong chay vi HocVien bat buoc khong san sang."),
            SkippedDomain(
                QlhvImportDomains.Relation,
                payload.RelationRows.Count,
                QlhvImportDomainStatuses.SkippedDependencyNotReady,
                "Quan he khong chay vi HocVien bat buoc khong san sang."),
            FailedDomain(
                QlhvImportDomains.HocVien,
                payload.HocVienRows.Count,
                message),
        };
        return BuildFullSyncResult(results);
    }

    private static DomainTransactionResult SuccessfulDomain(
        string domain,
        QlhvEntityWriteCounts counts)
    {
        var changed = counts.Inserted + counts.Updated + counts.Reactivated + counts.SoftDeleted;
        return new DomainTransactionResult(
            new QlhvDomainWriteResult(
                domain,
                changed == 0
                    ? QlhvImportDomainStatuses.NoOp
                    : QlhvImportDomainStatuses.Succeeded,
                changed == 0 ? "Khong co thay doi can ghi." : null,
                counts));
    }

    private static DomainTransactionResult SkippedDomain(
        string domain,
        int sourceRows,
        string status,
        string? message)
        => new(
            new QlhvDomainWriteResult(
                domain,
                status,
                message,
                new QlhvEntityWriteCounts(sourceRows, 0, 0, 0, 0, sourceRows)));

    private static DomainTransactionResult FailedDomain(
        string domain,
        int sourceRows,
        string message,
        EntityDomainGuardRow? guard = null)
        => new(
            new QlhvDomainWriteResult(
                domain,
                QlhvImportDomainStatuses.Failed,
                message,
                new QlhvEntityWriteCounts(sourceRows, 0, 0, 0, 0, 0)),
            guard?.InvalidSourceProfileRows ?? 0,
            guard?.InvalidTargetIdentityRows ?? 0,
            guard?.DuplicateTargetIdentityRows ?? 0,
            guard?.RelationConflicts ?? 0,
            guard?.EmptyPartitionRiskGroups ?? 0,
            guard?.NaturalKeyConflicts ?? 0);

    private static DomainTransactionResult FailedDomain(
        string domain,
        int sourceRows,
        string message,
        HocVienDomainGuardRow guard)
        => new(
            new QlhvDomainWriteResult(
                domain,
                QlhvImportDomainStatuses.Failed,
                message,
                new QlhvEntityWriteCounts(sourceRows, 0, 0, 0, 0, 0)),
            guard.InvalidSourceProfileRows,
            guard.InvalidTargetIdentityRows,
            guard.DuplicateTargetIdentityRows,
            0,
            0,
            0);

    private static bool IsSuccessfulDomain(QlhvDomainWriteResult result)
        => string.Equals(result.Status, QlhvImportDomainStatuses.Succeeded, StringComparison.Ordinal) ||
           string.Equals(result.Status, QlhvImportDomainStatuses.NoOp, StringComparison.Ordinal);

    private static string ResolveSkippedStatus(string? reason)
    {
        if (reason?.Contains("SCHEMA", StringComparison.OrdinalIgnoreCase) == true)
        {
            return QlhvImportDomainStatuses.SkippedSchemaNotReady;
        }

        if (reason?.Contains("DEPENDENCY", StringComparison.OrdinalIgnoreCase) == true)
        {
            return QlhvImportDomainStatuses.SkippedDependencyNotReady;
        }

        return QlhvImportDomainStatuses.SkippedSourceNotReady;
    }

    private static string BuildGuardFailureMessage(
        string domain,
        int expectedRows,
        EntityDomainGuardRow guard)
        => $"{domain} transaction guard bi chan " +
           $"(staged={guard.StagedRows}/{expectedRows}, " +
           $"sourceProfile={guard.InvalidSourceProfileRows}, " +
           $"targetIdentity={guard.InvalidTargetIdentityRows}, " +
           $"duplicateTarget={guard.DuplicateTargetIdentityRows}, " +
           $"relation={guard.RelationConflicts}, " +
           $"emptyRisk={guard.EmptyPartitionRiskGroups}, " +
           $"naturalKey={guard.NaturalKeyConflicts}).";

    private static string BuildHocVienGuardFailureMessage(
        int expectedRows,
        HocVienDomainGuardRow guard)
        => "HocVien transaction guard bi chan " +
           $"(staged={guard.StagedRows}/{expectedRows}, " +
           $"sourceProfile={guard.InvalidSourceProfileRows}, " +
           $"targetIdentity={guard.InvalidTargetIdentityRows}, " +
           $"duplicateTarget={guard.DuplicateTargetIdentityRows}).";

    private static void ValidateEntitySourceIdentity<T>(
        string sourceProfileCode,
        IReadOnlyList<T> rows,
        Func<T, string?> profileSelector,
        Func<T, string?> keySelector,
        string groupName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var profile = NormalizeRequired(profileSelector(row), $"{groupName}.SourceProfileCode")
                .ToUpperInvariant();
            if (!string.Equals(profile, sourceProfileCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Snapshot {groupName} chua SourceProfileCode ngoai partition duoc yeu cau.");
            }

            var key = NormalizeRequired(keySelector(row), $"{groupName}.SourceKey");
            if (!seen.Add(key))
            {
                throw new InvalidOperationException(
                    $"Snapshot {groupName} co source key trung: {key}.");
            }
        }
    }

    private static void ValidateKhoaHocRequiredValues(
        IReadOnlyList<QlhvImportKhoaHocWriteModel> rows)
    {
        foreach (var row in rows)
        {
            _ = NormalizeRequired(row.SourceHash, "KhoaHoc.SourceHash");
            _ = NormalizeRequired(row.MaKhoa, "KhoaHoc.MaKhoa");
        }
    }

    private static void ValidateGiaoVienRequiredValues(
        IReadOnlyList<QlhvImportGiaoVienWriteModel> rows)
    {
        foreach (var row in rows)
        {
            _ = NormalizeRequired(row.SourceHash, "GiaoVien.SourceHash");
            _ = NormalizeRequired(row.MaGV, "GiaoVien.MaGV");
            _ = NormalizeRequired(row.HoTen, "GiaoVien.HoTen");
        }
    }

    private static void ValidateRelationRequiredValues(
        IReadOnlyList<QlhvImportKhoaHocGiaoVienWriteModel> rows)
    {
        foreach (var row in rows)
        {
            if (row.SourceMaLichLV <= 0)
            {
                throw new InvalidOperationException("KhoaHoc_GiaoVien.SourceMaLichLV phai lon hon 0.");
            }

            _ = NormalizeRequired(row.SourceMaKhoaHoc, "KhoaHoc_GiaoVien.SourceMaKhoaHoc");
            _ = NormalizeRequired(row.SourceMaGV, "KhoaHoc_GiaoVien.SourceMaGV");
            _ = NormalizeRequired(row.SourceHash, "KhoaHoc_GiaoVien.SourceHash");
            _ = NormalizeRequired(row.MaKhoa, "KhoaHoc_GiaoVien.MaKhoa");
            _ = NormalizeRequired(row.MaGV, "KhoaHoc_GiaoVien.MaGV");
        }
    }

    private static void ValidateFullSyncSourceIdentity(
        string sourceProfileCode,
        IReadOnlyList<QlhvImportHocVienWriteModel> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var rowProfile = NormalizeRequired(row.SourceProfileCode, nameof(row.SourceProfileCode))
                .ToUpperInvariant();
            if (!string.Equals(rowProfile, sourceProfileCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Snapshot staging chua SourceProfileCode ngoai partition duoc yeu cau.");
            }

            var sourceMaDk = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            if (!seen.Add(sourceMaDk))
            {
                throw new InvalidOperationException(
                    $"Snapshot staging co SourceMaDK trung: {sourceMaDk}.");
            }

            _ = NormalizeRequired(row.SourceSystem, nameof(row.SourceSystem));
            _ = NormalizeRequired(row.MaDK, nameof(row.MaDK));
            _ = NormalizeRequired(row.SourceOfTruth, nameof(row.SourceOfTruth));
            _ = NormalizeRequired(row.V2RowHash, nameof(row.V2RowHash));
        }
    }

    private static void ValidateSourceIdentity(IReadOnlyList<HocVienTargetWriteModel> rows)
    {
        foreach (var row in rows)
        {
            _ = NormalizeRequired(row.SourceProfileCode, nameof(row.SourceProfileCode));
            _ = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            _ = NormalizeRequired(row.SourceSystem, nameof(row.SourceSystem));
        }
    }

    private async Task<string> ResolveUsableTargetAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return target.ConnectionString;
    }

    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static object Db(DateTime? value) => value.HasValue ? value.Value : DBNull.Value;
    private static object Db(int? value) => value.HasValue ? value.Value : DBNull.Value;
    private static string NormalizeRequired(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thieu thong tin dinh danh nguon bat buoc: {name}.")
            : value.Trim();

    private static int GetBucket(IEnumerable<SourceValueDistributionDto> rows, string value)
        => rows.FirstOrDefault(r => string.Equals(r.Value, value, StringComparison.OrdinalIgnoreCase))?.Total ?? 0;

    private static string BuildTargetSoCccdLengthSql(string activeFilter) => @"
SELECT
    CASE
        WHEN NULLIF(LTRIM(RTRIM(SoCCCD)), '') IS NULL THEN 'null-empty'
        WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 9 THEN '9'
        WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 12 THEN '12'
        ELSE 'other'
    END AS Value,
    COUNT(1) AS Total
FROM dbo.App_HocVien" + activeFilter + @"
GROUP BY CASE
    WHEN NULLIF(LTRIM(RTRIM(SoCCCD)), '') IS NULL THEN 'null-empty'
    WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 9 THEN '9'
    WHEN LEN(LTRIM(RTRIM(SoCCCD))) = 12 THEN '12'
    ELSE 'other'
END
ORDER BY Value;";

    private const string TargetSchemaDiagnosticsSql = @"
SELECT
    CAST(CASE WHEN OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS AppHocVienExists,
    CAST(CASE WHEN OBJECT_ID(N'dbo.App_DongBoLog', N'U') IS NULL THEN 0 ELSE 1 END AS bit) AS AppDongBoLogExists,
    CAST(CASE WHEN COL_LENGTH(N'dbo.App_HocVien', N'IsDeleted') IS NULL THEN 0 ELSE 1 END AS bit) AS IsDeletedColumnExists;";

    private const string AtomicImportGuardSql = @"
SELECT
    (
        SELECT COUNT(DISTINCT staging.MaDK)
        FROM #Sync_HocVien_Staging AS staging
        INNER JOIN dbo.App_HocVien AS target WITH (UPDLOCK, HOLDLOCK)
            ON target.MaDK = staging.MaDK
        WHERE target.SourceProfileCode IS NULL
           OR target.SourceProfileCode <> staging.SourceProfileCode
    ) AS TargetMaDkConflictsOtherProfiles,
    (
        SELECT COUNT(DISTINCT staging.SourceMaDK)
        FROM #Sync_HocVien_Staging AS staging
        INNER JOIN dbo.App_HocVien AS target WITH (UPDLOCK, HOLDLOCK)
            ON target.SourceProfileCode = staging.SourceProfileCode
           AND target.SourceMaDK = staging.SourceMaDK
        WHERE target.IsDeleted = 1
    ) AS SoftDeletedIdentityConflicts;";

    private const string RequiredColumnsSql = @"
SELECT
    requiredColumns.ColumnName,
    CAST(CASE WHEN sysColumns.column_id IS NULL THEN 0 ELSE 1 END AS bit) AS [Exists]
FROM (
    VALUES
        (1, N'MaDK'),
        (2, N'HoTen'),
        (3, N'NgaySinh'),
        (4, N'GioiTinh'),
        (5, N'SoCCCD'),
        (6, N'DiaChiThuongTru'),
        (7, N'MaKhoa'),
        (8, N'TenKhoa'),
        (9, N'MaHangDT'),
        (10, N'HangGPLXHoc'),
        (11, N'SourceProfileCode'),
        (12, N'SourceMaDK'),
        (13, N'SourceSystem'),
        (14, N'SourceVersion'),
        (15, N'V2RowHash')
) AS requiredColumns(SortOrder, ColumnName)
LEFT JOIN sys.objects AS sysObjects
    ON sysObjects.object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
LEFT JOIN sys.columns AS sysColumns
    ON sysColumns.object_id = sysObjects.object_id
   AND sysColumns.name = requiredColumns.ColumnName
ORDER BY requiredColumns.SortOrder;";

    private sealed class TargetSchemaDiagnosticsRow
    {
        public bool AppHocVienExists { get; init; }
        public bool AppDongBoLogExists { get; init; }
        public bool IsDeletedColumnExists { get; init; }
    }

    private sealed class ExistingSourceKeyRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public string SourceMaDK { get; init; } = string.Empty;
    }

    private sealed class ExistingSourceHashRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public string SourceMaDK { get; init; } = string.Empty;
        public string? V2RowHash { get; init; }
    }

    private sealed class AtomicImportGuardRow
    {
        public int TargetMaDkConflictsOtherProfiles { get; init; }
        public int SoftDeletedIdentityConflicts { get; init; }
    }

    private sealed class EntityDomainGuardRow
    {
        public int StagedRows { get; init; }
        public int InvalidSourceProfileRows { get; init; }
        public int InvalidTargetIdentityRows { get; init; }
        public int DuplicateTargetIdentityRows { get; init; }
        public int RelationConflicts { get; init; }
        public int EmptyPartitionRiskGroups { get; init; }
        public int NaturalKeyConflicts { get; init; }

        public bool HasConflicts =>
            InvalidSourceProfileRows > 0 ||
            InvalidTargetIdentityRows > 0 ||
            DuplicateTargetIdentityRows > 0 ||
            RelationConflicts > 0 ||
            EmptyPartitionRiskGroups > 0 ||
            NaturalKeyConflicts > 0;
    }

    private sealed class HocVienDomainGuardRow
    {
        public int StagedRows { get; init; }
        public int InvalidSourceProfileRows { get; init; }
        public int InvalidTargetIdentityRows { get; init; }
        public int DuplicateTargetIdentityRows { get; init; }

        public bool HasConflicts =>
            InvalidSourceProfileRows > 0 ||
            InvalidTargetIdentityRows > 0 ||
            DuplicateTargetIdentityRows > 0;
    }

    private sealed record DomainTransactionResult(
        QlhvDomainWriteResult Result,
        int InvalidSourceProfileRows = 0,
        int InvalidTargetIdentityRows = 0,
        int DuplicateTargetIdentityRows = 0,
        int RelationConflicts = 0,
        int EmptyPartitionRiskGroups = 0,
        int NaturalKeyConflicts = 0);
}
