using Microsoft.Extensions.Options;
using System.Text.Json;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

public sealed class QlhvImportService : IQlhvImportService
{
    public const string ConfirmationText = "IMPORT QLHV CSĐT";
    public const string AppKhoaHocNotSupportedWarning =
        "App_KhoaHoc import chua duoc ho tro trong task nay.";

    private static readonly IReadOnlyDictionary<string, ImportSourceDefinition> SupportedProfiles =
        new Dictionary<string, ImportSourceDefinition>(StringComparer.Ordinal)
        {
            [CsdtConnectionProfileCodes.CsdtMoto] = new("66030", "CSDL_MOTO_BAK"),
            [CsdtConnectionProfileCodes.CsdtOto] = new("66029", "CSDL_OTO_BAK"),
        };

    private readonly IQlhvImportReadRepository _readRepository;
    private readonly IQlhvHocVienTargetRepository _targetRepository;
    private readonly IQlhvImportWriteRepository _writeRepository;
    private readonly SyncOptions _syncOptions;
    private readonly SyncExecutionOptions _executionOptions;
    private readonly IQlhvSourceOperationLock _operationLock;
    private readonly IQlhvOperationHistoryRepository _operationHistory;

    public QlhvImportService(
        IQlhvImportReadRepository readRepository,
        IQlhvHocVienTargetRepository targetRepository,
        IQlhvImportWriteRepository writeRepository,
        IOptions<SyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions,
        IQlhvSourceOperationLock operationLock,
        IQlhvOperationHistoryRepository operationHistory)
    {
        _readRepository = readRepository;
        _targetRepository = targetRepository;
        _writeRepository = writeRepository;
        _syncOptions = syncOptions.Value;
        _executionOptions = executionOptions.Value;
        _operationLock = operationLock;
        _operationHistory = operationHistory;
    }

    public async Task<QlhvImportPlanDto> GetPlanAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
        => (await BuildPlanContextWithReadLeaseAsync(Normalize(request), cancellationToken)).Plan;

    public async Task<QlhvImportDiagnosticsDto> GetDiagnosticsAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = (await BuildPlanContextWithReadLeaseAsync(Normalize(request), cancellationToken)).Plan;
        return new QlhvImportDiagnosticsDto
        {
            SourceProfileCode = plan.SourceProfileCode,
            SourceDatabaseName = plan.SourceDatabaseName,
            MaCSDT = plan.MaCSDT,
            MaKhoaHoc = plan.MaKhoaHoc,
            SourceHocVienRows = plan.SourceHocVienRows,
            SourceDistinctMaDkRows = plan.SourceDistinctMaDkRows,
            DuplicateSourceMaDkRows = plan.DuplicateSourceMaDkRows,
            CurrentAppHocVienRows = plan.CurrentAppHocVienRows,
            TargetRowsForSourceProfile = plan.TargetRowsForSourceProfile,
            TargetExactIdentityMatches = plan.TargetExactIdentityMatches,
            TargetMaDkConflictsOtherProfiles = plan.TargetMaDkConflictsOtherProfiles,
            SoftDeletedIdentityConflicts = plan.SoftDeletedIdentityConflicts,
            SourceProfileConstraintExists = plan.SourceProfileConstraintExists,
            SourceProfileAllowedByConstraint = plan.SourceProfileAllowedByConstraint,
            PlannedInsertHocVienRows = plan.PlannedInsertHocVienRows,
            PlannedUpdateHocVienRows = plan.PlannedUpdateHocVienRows,
            PlannedReactivateHocVienRows = plan.PlannedReactivateHocVienRows,
            PlannedSoftDeleteHocVienRows = plan.PlannedSoftDeleteHocVienRows,
            PlannedSkipHocVienRows = plan.PlannedSkipHocVienRows,
            PlannedUpsertHocVienRows = plan.PlannedUpsertHocVienRows,
            Blockers = plan.Blockers,
            Warnings = plan.Warnings,
        };
    }

    private async Task<PlanContext> BuildPlanContextWithReadLeaseAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation.Count > 0)
        {
            return new PlanContext(
                CreateBasePlan(request, validation),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        var sourceType = TryResolveSourceType(request.SourceProfileCode);
        if (!QlhvOperationSourceCatalog.TryGet(sourceType, out var operationSource))
        {
            return new PlanContext(
                AddBlocker(CreateBasePlan(request, validation), "Nguon import khong nam trong allowlist van hanh."),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        IAsyncDisposable? lease;
        try
        {
            lease = await _operationLock.TryAcquireAsync(operationSource, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PlanContext(
                AddBlocker(
                    CreateBasePlan(request, validation),
                    $"Khong kiem tra duoc operation lock. Chi tiet: {ex.GetType().Name}."),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        if (lease is null)
        {
            return new PlanContext(
                AddBlocker(
                    CreateBasePlan(request, validation),
                    $"Nguon {operationSource.SourceType} dang refresh BAK hoac full sync; hay lap plan lai sau."),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        await using (lease)
        {
            return await BuildPlanContextAsync(request, cancellationToken);
        }
    }

    public async Task<QlhvImportExecuteResultDto> ExecuteAsync(
        QlhvImportExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new QlhvImportExecuteRequest();
        var normalized = Normalize(request);

        if (!string.Equals(request.ConfirmText, ConfirmationText, StringComparison.Ordinal))
        {
            var blockedPlan = AddBlocker(
                CreateBasePlan(normalized, Validate(normalized)),
                $"ConfirmText phai khop chinh xac: {ConfirmationText}.");
            return Blocked(blockedPlan, "Import bi chan vi chuoi xac nhan khong khop.");
        }

        if (!QlhvOperationSourceCatalog.TryGet(
                TryResolveSourceType(normalized.SourceProfileCode),
                out var operationSource))
        {
            return Blocked(
                CreateBasePlan(normalized, Validate(normalized)),
                "Import bi chan vi nguon khong hop le.");
        }

        // Safety configuration gates are evaluated before any operation-history write.
        if (_syncOptions.DryRun)
        {
            return Blocked(
                AddBlocker(
                    CreateBasePlan(normalized, Validate(normalized)),
                    "Ghi vao QLHV_APP bi chan: Sync:DryRun = true."),
                "Import bi chan boi cau hinh an toan.");
        }

        if (!_executionOptions.EnableTargetWrites)
        {
            return Blocked(
                AddBlocker(
                    CreateBasePlan(normalized, Validate(normalized)),
                    "Ghi vao QLHV_APP bi chan: SyncExecution.EnableTargetWrites = false."),
                "Import bi chan boi cau hinh an toan.");
        }

        var operationId = Guid.NewGuid();
        var historyCreated = false;
        IAsyncDisposable? operationLease = null;
        try
        {
            operationLease = await _operationLock.TryAcquireAsync(operationSource, cancellationToken);
            if (operationLease is null)
            {
                var conflictPlan = AddBlocker(
                    CreateBasePlan(normalized, Validate(normalized)),
                    $"Nguon {operationSource.SourceType} dang bi khoa boi refresh hoac full sync khac.");
                return Blocked(conflictPlan, "Full sync bi chan do thao tac cung nguon dang chay.");
            }

            bool reserved;
            try
            {
                var startedAt = DateTime.UtcNow;
                reserved = await _operationHistory.TryCreateAsync(
                    new QlhvOperationHistoryCreate(
                        operationId,
                        operationSource,
                        QlhvOperationTypes.FullSync,
                        QlhvOperationTypes.Running,
                        startedAt,
                        startedAt),
                    cancellationToken);
            }
            catch (QlhvOperationsStoreUnavailableException ex)
            {
                return Blocked(
                    AddBlocker(CreateBasePlan(normalized, Validate(normalized)), ex.Message),
                    "Full sync bi chan vi lich su van hanh chua san sang.");
            }

            if (!reserved)
            {
                return Blocked(
                    AddBlocker(
                        CreateBasePlan(normalized, Validate(normalized)),
                        $"Nguon {operationSource.SourceType} dang co refresh hoac full sync chua ket thuc."),
                    "Full sync bi chan do thao tac cung nguon dang chay.");
            }

            historyCreated = true;

            // The plan and snapshot token are rebuilt only after the cross-process lock is held.
            var context = await BuildPlanContextAsync(normalized, cancellationToken);
            if (string.IsNullOrWhiteSpace(request.ExpectedSnapshotToken))
            {
                context = context with
                {
                    Plan = AddBlocker(
                        context.Plan,
                        "ExpectedSnapshotToken la bat buoc; hay lap plan lai truoc khi full sync."),
                };
            }
            else if (!string.Equals(
                         request.ExpectedSnapshotToken.Trim(),
                         context.Plan.BackupSnapshotToken,
                         StringComparison.Ordinal))
            {
                context = context with
                {
                    Plan = AddBlocker(
                        context.Plan,
                        "Plan da cu: snapshot BAK hien tai khong khop expectedSnapshotToken; hay lap plan lai."),
                };
            }

            if (!context.Plan.Executable)
            {
                await CompleteFailedHistoryAsync(
                    operationId,
                    context.Plan,
                    "Plan co blocker.",
                    CancellationToken.None);
                return WithOperation(Blocked(context.Plan, "Import bi chan vi plan co blocker."), operationId);
            }

            var write = await _writeRepository.FullSyncAsync(
                context.Plan.SourceProfileCode,
                context.SourceModels,
                cancellationToken);
            if (write.HasConflicts)
            {
                var blockedPlan = context.Plan;
                if (write.InvalidSourceProfileRows > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Transaction guard phat hien {write.InvalidSourceProfileRows} dong staging sai SourceProfileCode.");
                }

                if (write.InvalidTargetIdentityRows > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Transaction guard phat hien {write.InvalidTargetIdentityRows} dong target thieu SourceMaDK.");
                }

                if (write.DuplicateTargetIdentityRows > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Transaction guard phat hien {write.DuplicateTargetIdentityRows} identity target bi trung.");
                }

                await CompleteFailedHistoryAsync(
                    operationId,
                    blockedPlan,
                    "Transaction guard phat hien conflict.",
                    CancellationToken.None);
                return WithOperation(
                    Blocked(blockedPlan, "Full sync bi chan boi kiem tra an toan trong transaction."),
                    operationId);
            }

            var historyWarning = await TryCompleteSucceededHistoryAsync(
                operationId,
                context.Plan,
                write,
                cancellationToken);
            return new QlhvImportExecuteResultDto
            {
                OperationId = operationId,
                Executed = true,
                Status = "ThanhCong",
                Message = historyWarning is null
                    ? "Full sync hoc vien tu CSDT BAK vao QLHV_APP hoan tat."
                    : "Full sync da commit vao QLHV_APP, nhung cap nhat lich su that bai; khong duoc retry tu dong.",
                Plan = context.Plan,
                InsertedHocVienRows = write.Inserted,
                UpdatedHocVienRows = write.Updated,
                ReactivatedHocVienRows = write.Reactivated,
                SoftDeletedHocVienRows = write.SoftDeleted,
                SkippedHocVienRows = write.Skipped,
            };
        }
        catch (OperationCanceledException)
        {
            if (historyCreated)
            {
                await CompleteFailedHistoryAsync(
                    operationId,
                    CreateBasePlan(normalized, Validate(normalized)),
                    "Operation bi huy.",
                    CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            var failedPlan = AddBlocker(
                CreateBasePlan(normalized, Validate(normalized)),
                $"Import that bai. Chi tiet: {ex.GetType().Name}.");
            if (historyCreated)
            {
                await CompleteFailedHistoryAsync(
                    operationId,
                    failedPlan,
                    $"Import failed: {ex.GetType().Name}.",
                    CancellationToken.None);
            }

            var blocked = Blocked(failedPlan, "Full sync hoc vien vao QLHV_APP that bai.");
            return historyCreated ? WithOperation(blocked, operationId) : blocked;
        }
        finally
        {
            if (operationLease is not null)
            {
                await operationLease.DisposeAsync();
            }
        }
    }

    private async Task<PlanContext> BuildPlanContextAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken)
    {
        var blockers = Validate(request).ToList();
        if (blockers.Count > 0)
        {
            return new PlanContext(
                CreateBasePlan(request, blockers),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        try
        {
            var targetDiagnostics = await _targetRepository.GetDiagnosticsAsync(cancellationToken);
            if (!targetDiagnostics.AppHocVienExists)
            {
                blockers.Add("Target QLHV_APP thieu bang dbo.App_HocVien.");
            }

            var missingColumns = targetDiagnostics.RequiredColumns
                .Where(column => !column.Exists)
                .Select(column => column.ColumnName)
                .ToArray();
            if (missingColumns.Length > 0)
            {
                blockers.Add(
                    "Target dbo.App_HocVien thieu cot bat buoc: " +
                    string.Join(", ", missingColumns) + ".");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            blockers.Add($"Khong doc duoc schema QLHV_APP. Chi tiet: {ex.GetType().Name}.");
        }

        if (blockers.Count > 0)
        {
            return new PlanContext(
                CreateBasePlan(request, blockers),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        QlhvImportSourceSnapshot source;
        try
        {
            source = await _readRepository.ReadSourceAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QlhvImportReadException ex)
        {
            blockers.Add(ex.Message);
            return new PlanContext(
                CreateBasePlan(request, blockers),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }
        catch (Exception ex)
        {
            blockers.Add($"Khong doc duoc nguon {request.SourceProfileCode}. Chi tiet: {ex.GetType().Name}.");
            return new PlanContext(
                CreateBasePlan(request, blockers),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        var definition = SupportedProfiles[request.SourceProfileCode];
        if (!string.Equals(source.SourceDatabaseName, definition.ExpectedDatabaseName, StringComparison.Ordinal))
        {
            blockers.Add(
                $"Source database la {source.SourceDatabaseName}; bat buoc phai la {definition.ExpectedDatabaseName}.");
        }

        var normalizedSourceMaDks = source.HocVienRows
            .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
            .Select(row => row.MaDK.Trim())
            .ToArray();
        var distinctSourceMaDks = normalizedSourceMaDks
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateSourceMaDks = CountDuplicateSourceMaDks(normalizedSourceMaDks);
        if (duplicateSourceMaDks > 0)
        {
            blockers.Add($"Nguon co {duplicateSourceMaDks} MaDK bi trung trong pham vi full sync.");
        }

        if (source.HocVienRows.Count == 0)
        {
            blockers.Add(
                "Nguon co 0 hoc vien; full sync bi chan de khong soft-delete toan bo partition.");
        }

        var blankSourceKeys = source.HocVienRows.Count - normalizedSourceMaDks.Length;
        if (blankSourceKeys > 0)
        {
            blockers.Add($"Nguon co {blankSourceKeys} dong thieu MaDK/SourceMaDK.");
        }

        var sourceIdentity = new HocVienSourceIdentityContext(request.SourceProfileCode, "V2");
        var sourceModels = new List<QlhvImportHocVienWriteModel>(source.HocVienRows.Count);
        var warnings = new List<string> { AppKhoaHocNotSupportedWarning };
        foreach (var sourceRow in source.HocVienRows)
        {
            var mapped = QlhvImportHocVienMapper.MapAndValidate(sourceRow, sourceIdentity);
            warnings.AddRange(mapped.Warnings.Select(warning =>
                $"{warning.MaDK}: {warning.Message}"));
            blockers.AddRange(mapped.Blockers);
            if (!mapped.ShouldSkip && mapped.Model is not null)
            {
                sourceModels.Add(mapped.Model);
            }
        }

        if (source.HocVienRows.Count > 0 && sourceModels.Count == 0)
        {
            blockers.Add("Khong co dong hoc vien nao map duoc an toan de full sync.");
        }

        QlhvImportTargetSnapshot target;
        try
        {
            target = await _readRepository.ReadTargetAsync(
                request,
                distinctSourceMaDks,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QlhvImportReadException ex)
        {
            blockers.Add(ex.Message);
            return new PlanContext(
                CreateBasePlan(
                    request,
                    blockers,
                    source,
                    distinctSourceMaDks.Length,
                    duplicateSourceMaDks),
                sourceModels);
        }
        catch (Exception ex)
        {
            blockers.Add($"Khong doc duoc du lieu hien tai trong QLHV_APP. Chi tiet: {ex.GetType().Name}.");
            return new PlanContext(
                CreateBasePlan(
                    request,
                    blockers,
                    source,
                    distinctSourceMaDks.Length,
                    duplicateSourceMaDks),
                sourceModels);
        }

        if (target.SourceProfileConstraintExists && !target.SourceProfileAllowedByConstraint)
        {
            blockers.Add(
                $"CHECK constraint cua App_HocVien.SourceProfileCode hien khong cho phep {request.SourceProfileCode}.");
        }

        if (target.DuplicateTargetIdentityRows > 0)
        {
            blockers.Add(
                $"Target co {target.DuplicateTargetIdentityRows} identity SourceProfileCode + SourceMaDK bi trung.");
        }

        var invalidTargetIdentities = target.HocVienRows.Count(row =>
            string.IsNullOrWhiteSpace(row.SourceMaDK));
        if (invalidTargetIdentities > 0)
        {
            blockers.Add(
                $"Target partition co {invalidTargetIdentities} dong thieu SourceMaDK.");
        }

        if (target.TargetMaDkConflictsOtherProfiles > 0)
        {
            warnings.Add(
                $"Co {target.TargetMaDkConflictsOtherProfiles} MaDK cung xuat hien o profile khac; " +
                "full sync van tach biet bang SourceProfileCode + SourceMaDK.");
        }

        QlhvFullSyncPlan? fullSyncPlan = null;
        if (sourceModels.Count > 0 &&
            duplicateSourceMaDks == 0 &&
            target.DuplicateTargetIdentityRows == 0 &&
            invalidTargetIdentities == 0)
        {
            try
            {
                fullSyncPlan = QlhvFullSyncPlanner.BuildPlan(sourceModels, target.HocVienRows);
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add(ex.Message);
            }
        }

        warnings = warnings
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToList();
        blockers = blockers
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var plan = new QlhvImportPlanDto
        {
            SourceProfileCode = request.SourceProfileCode,
            SourceDatabaseName = source.SourceDatabaseName,
            BackupSnapshotToken = source.BackupSnapshotToken,
            GeneratedAtUtc = source.GeneratedAtUtc == default ? DateTime.UtcNow : source.GeneratedAtUtc,
            MaCSDT = request.MaCSDT,
            MaKhoaHoc = request.MaKhoaHoc,
            SourceHocVienRows = source.HocVienRows.Count,
            SourceDistinctMaDkRows = distinctSourceMaDks.Length,
            DuplicateSourceMaDkRows = duplicateSourceMaDks,
            SourceKhoaHocRows = source.KhoaHocRows,
            CurrentAppHocVienRows = target.CurrentAppHocVienRows,
            CurrentAppKhoaHocRows = target.AppKhoaHocRows,
            TargetRowsForSourceProfile = target.TargetRowsForSourceProfile,
            TargetExactIdentityMatches = target.TargetExactIdentityMatches,
            TargetMaDkConflictsOtherProfiles = target.TargetMaDkConflictsOtherProfiles,
            SoftDeletedIdentityConflicts = target.SoftDeletedIdentityConflicts,
            SourceProfileConstraintExists = target.SourceProfileConstraintExists,
            SourceProfileAllowedByConstraint = target.SourceProfileAllowedByConstraint,
            PlannedInsertHocVienRows = fullSyncPlan?.PlannedInsertHocVienRows ?? 0,
            PlannedUpdateHocVienRows = fullSyncPlan?.PlannedUpdateHocVienRows ?? 0,
            PlannedReactivateHocVienRows = fullSyncPlan?.PlannedReactivateHocVienRows ?? 0,
            PlannedSoftDeleteHocVienRows = fullSyncPlan?.PlannedSoftDeleteHocVienRows ?? 0,
            PlannedSkipHocVienRows = fullSyncPlan?.PlannedSkipHocVienRows ?? 0,
            PlannedUpsertHocVienRows = fullSyncPlan?.PlannedUpsertHocVienRows ?? 0,
            PlannedUpsertKhoaHocRows = 0,
            Blockers = blockers,
            Warnings = warnings,
        };

        return new PlanContext(plan, sourceModels);
    }

    private static QlhvImportRequest Normalize(QlhvImportRequest? request)
        => new()
        {
            SourceProfileCode = string.IsNullOrWhiteSpace(request?.SourceProfileCode)
                ? string.Empty
                : request.SourceProfileCode.Trim().ToUpperInvariant(),
            MaCSDT = string.IsNullOrWhiteSpace(request?.MaCSDT) ? string.Empty : request.MaCSDT.Trim(),
            MaKhoaHoc = string.IsNullOrWhiteSpace(request?.MaKhoaHoc) ? null : request.MaKhoaHoc.Trim(),
        };

    private static int CountDuplicateSourceMaDks(IEnumerable<string> normalizedMaDks)
        => normalizedMaDks
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Skip(1).Any());

    private static IReadOnlyList<string> Validate(QlhvImportRequest request)
    {
        var blockers = new List<string>();
        if (!SupportedProfiles.TryGetValue(request.SourceProfileCode, out var definition))
        {
            blockers.Add("SourceProfileCode chi ho tro CSDT_MOTO hoac CSDT_OTO.");
        }

        if (string.IsNullOrWhiteSpace(request.MaCSDT))
        {
            blockers.Add("MaCSDT la bat buoc.");
        }
        else if (definition is not null &&
                 !string.Equals(request.MaCSDT, definition.MaCsdt, StringComparison.Ordinal))
        {
            blockers.Add(
                $"MaCSDT khong khop profile {request.SourceProfileCode}; " +
                $"gia tri mong doi la {definition.MaCsdt}.");
        }

        if (request.MaKhoaHoc is not null)
        {
            blockers.Add(
                "Full snapshot chi ho tro toan bo maCSDT; maKhoaHoc phai de trong de tranh soft-delete ngoai pham vi.");
        }

        return blockers;
    }

    private static QlhvImportPlanDto CreateBasePlan(
        QlhvImportRequest request,
        IReadOnlyList<string> blockers,
        QlhvImportSourceSnapshot? source = null,
        int sourceDistinctMaDkRows = 0,
        int duplicateSourceMaDkRows = 0)
        => new()
        {
            SourceProfileCode = request.SourceProfileCode,
            SourceDatabaseName = source?.SourceDatabaseName ?? string.Empty,
            BackupSnapshotToken = source?.BackupSnapshotToken ?? string.Empty,
            GeneratedAtUtc = source is null || source.GeneratedAtUtc == default
                ? DateTime.UtcNow
                : source.GeneratedAtUtc,
            MaCSDT = request.MaCSDT,
            MaKhoaHoc = request.MaKhoaHoc,
            SourceHocVienRows = source?.HocVienRows.Count ?? 0,
            SourceDistinctMaDkRows = sourceDistinctMaDkRows,
            DuplicateSourceMaDkRows = duplicateSourceMaDkRows,
            SourceKhoaHocRows = source?.KhoaHocRows ?? 0,
            PlannedUpsertKhoaHocRows = 0,
            Blockers = blockers,
            Warnings = new[] { AppKhoaHocNotSupportedWarning },
        };

    private static QlhvImportPlanDto AddBlocker(QlhvImportPlanDto plan, string blocker)
        => new()
        {
            SourceProfileCode = plan.SourceProfileCode,
            SourceDatabaseName = plan.SourceDatabaseName,
            BackupSnapshotToken = plan.BackupSnapshotToken,
            GeneratedAtUtc = plan.GeneratedAtUtc,
            MaCSDT = plan.MaCSDT,
            MaKhoaHoc = plan.MaKhoaHoc,
            SourceHocVienRows = plan.SourceHocVienRows,
            SourceDistinctMaDkRows = plan.SourceDistinctMaDkRows,
            DuplicateSourceMaDkRows = plan.DuplicateSourceMaDkRows,
            SourceKhoaHocRows = plan.SourceKhoaHocRows,
            CurrentAppHocVienRows = plan.CurrentAppHocVienRows,
            CurrentAppKhoaHocRows = plan.CurrentAppKhoaHocRows,
            TargetRowsForSourceProfile = plan.TargetRowsForSourceProfile,
            TargetExactIdentityMatches = plan.TargetExactIdentityMatches,
            TargetMaDkConflictsOtherProfiles = plan.TargetMaDkConflictsOtherProfiles,
            SoftDeletedIdentityConflicts = plan.SoftDeletedIdentityConflicts,
            SourceProfileConstraintExists = plan.SourceProfileConstraintExists,
            SourceProfileAllowedByConstraint = plan.SourceProfileAllowedByConstraint,
            PlannedInsertHocVienRows = plan.PlannedInsertHocVienRows,
            PlannedUpdateHocVienRows = plan.PlannedUpdateHocVienRows,
            PlannedReactivateHocVienRows = plan.PlannedReactivateHocVienRows,
            PlannedSoftDeleteHocVienRows = plan.PlannedSoftDeleteHocVienRows,
            PlannedSkipHocVienRows = plan.PlannedSkipHocVienRows,
            PlannedUpsertHocVienRows = plan.PlannedUpsertHocVienRows,
            PlannedUpsertKhoaHocRows = plan.PlannedUpsertKhoaHocRows,
            Blockers = plan.Blockers.Concat(new[] { blocker }).Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = plan.Warnings,
        };

    private static QlhvImportExecuteResultDto Blocked(QlhvImportPlanDto plan, string message)
        => new()
        {
            Executed = false,
            Status = "BiChan",
            Message = message,
            Plan = plan,
        };

    private async Task<string?> TryCompleteSucceededHistoryAsync(
        Guid operationId,
        QlhvImportPlanDto plan,
        QlhvImportFullSyncWriteResult write,
        CancellationToken cancellationToken)
    {
        var completion = new QlhvOperationHistoryCompletion(
            operationId,
            QlhvOperationTypes.Succeeded,
            DateTime.UtcNow,
            plan.SourceHocVienRows,
            write.Inserted,
            write.Updated,
            write.Reactivated,
            write.SoftDeleted,
            write.Skipped,
            plan.BackupSnapshotToken,
            null,
            JsonSerializer.Serialize(new
            {
                plan.SourceDatabaseName,
                plan.GeneratedAtUtc,
                plan.Warnings,
                ImageFilesCopied = false,
            }),
            BackupRows: plan.SourceHocVienRows,
            TargetActiveRows: Math.Max(
                0,
                plan.CurrentAppHocVienRows + write.Inserted + write.Reactivated - write.SoftDeleted));
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await _operationHistory.CompleteAsync(completion, CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        // A SQL timeout can be reported after the UPDATE committed. Read history back before
        // warning the caller; the data write itself must never be reported as rolled back.
        try
        {
            var sourceType = QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(plan.SourceProfileCode);
            var persisted = (await _operationHistory.SearchAsync(
                    sourceType,
                    50,
                    CancellationToken.None))
                .FirstOrDefault(row => row.OperationId == operationId);
            if (persisted is { Status: QlhvOperationTypes.Succeeded })
            {
                return null;
            }
        }
        catch
        {
            // Preserve the safe history warning below.
        }

        return lastError?.GetType().Name ?? "UnknownHistoryError";
    }

    private async Task CompleteFailedHistoryAsync(
        Guid operationId,
        QlhvImportPlanDto plan,
        string safeError,
        CancellationToken cancellationToken)
    {
        try
        {
            await _operationHistory.CompleteAsync(
                new QlhvOperationHistoryCompletion(
                    operationId,
                    QlhvOperationTypes.Failed,
                    DateTime.UtcNow,
                    plan.SourceHocVienRows,
                    0, 0, 0, 0, 0,
                    string.IsNullOrWhiteSpace(plan.BackupSnapshotToken) ? null : plan.BackupSnapshotToken,
                    safeError.Length <= 2000 ? safeError : safeError[..2000],
                    JsonSerializer.Serialize(new
                    {
                        plan.SourceDatabaseName,
                        plan.GeneratedAtUtc,
                        plan.Blockers,
                        plan.Warnings,
                        ImageFilesCopied = false,
                    }),
                    BackupRows: plan.SourceHocVienRows,
                    TargetActiveRows: plan.CurrentAppHocVienRows),
                cancellationToken);
        }
        catch
        {
            // The original cancellation/failure remains the primary result.
        }
    }

    private static QlhvImportExecuteResultDto WithOperation(
        QlhvImportExecuteResultDto result,
        Guid operationId)
        => new()
        {
            OperationId = operationId,
            Executed = result.Executed,
            Status = result.Status,
            Message = result.Message,
            Plan = result.Plan,
            InsertedHocVienRows = result.InsertedHocVienRows,
            UpdatedHocVienRows = result.UpdatedHocVienRows,
            ReactivatedHocVienRows = result.ReactivatedHocVienRows,
            SoftDeletedHocVienRows = result.SoftDeletedHocVienRows,
            SkippedHocVienRows = result.SkippedHocVienRows,
        };

    private static string TryResolveSourceType(string sourceProfileCode)
    {
        try
        {
            return QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(sourceProfileCode);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private sealed record ImportSourceDefinition(string MaCsdt, string ExpectedDatabaseName);

    private sealed record PlanContext(
        QlhvImportPlanDto Plan,
        IReadOnlyList<QlhvImportHocVienWriteModel> SourceModels);
}
