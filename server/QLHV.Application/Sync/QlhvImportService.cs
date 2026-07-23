using Microsoft.Extensions.Options;
using System.Text.Json;
using QLHV.Application.CsdtConnections;
using QLHV.Application.HocVien.Photos;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

public sealed class QlhvImportService : IQlhvImportService
{
    // Kept as an API compatibility symbol only; this warning is no longer emitted.
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
    private readonly IHocVienPhotoProcessingService? _photoProcessing;

    public QlhvImportService(
        IQlhvImportReadRepository readRepository,
        IQlhvHocVienTargetRepository targetRepository,
        IQlhvImportWriteRepository writeRepository,
        IOptions<SyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions,
        IQlhvSourceOperationLock operationLock,
        IQlhvOperationHistoryRepository operationHistory,
        IHocVienPhotoProcessingService? photoProcessing = null)
    {
        _readRepository = readRepository;
        _targetRepository = targetRepository;
        _writeRepository = writeRepository;
        _syncOptions = syncOptions.Value;
        _executionOptions = executionOptions.Value;
        _operationLock = operationLock;
        _operationHistory = operationHistory;
        _photoProcessing = photoProcessing;
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
            HocVien = plan.HocVien,
            KhoaHoc = plan.KhoaHoc,
            GiaoVien = plan.GiaoVien,
            KhoaHocGiaoVien = plan.KhoaHocGiaoVien,
            Photo = plan.Photo,
            DuplicateSourceKeys = plan.DuplicateSourceKeys,
            RelationConflicts = plan.RelationConflicts,
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
        var operationActor = QlhvOperationActors.NormalizeInternal(request.Actor);
        var normalized = Normalize(request);

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
                        startedAt,
                        operationActor),
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
                context.Payload,
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

                if (write.RelationConflicts > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Transaction guard phat hien {write.RelationConflicts} quan he khong co khoa hoc/giao vien hop le.");
                }

                if (write.EmptyPartitionRiskGroups > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Transaction guard chan {write.EmptyPartitionRiskGroups} nhom source rong trong khi target con du lieu active.");
                }

                if (write.NaturalKeyConflicts > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Transaction guard phat hien {write.NaturalKeyConflicts} ma dich trung voi partition/du lieu legacy khac.");
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

            var (photoQueue, photoWarning) = await QueuePhotosAfterCommitSafelyAsync(
                context.Payload.HocVienRows,
                operationActor);
            var completedPlan = photoWarning is null
                ? context.Plan
                : AddWarning(context.Plan, photoWarning);

            var historyWarning = await TryCompleteSucceededHistoryAsync(
                operationId,
                completedPlan,
                write,
                photoQueue,
                cancellationToken);
            return new QlhvImportExecuteResultDto
            {
                OperationId = operationId,
                Executed = true,
                Status = "ThanhCong",
                Message = historyWarning is not null
                    ? "Full sync da commit vao QLHV_APP, nhung cap nhat lich su that bai; khong duoc retry tu dong."
                    : photoWarning is not null
                        ? "Full sync DB da commit hoan tat; xu ly anh duoc tach rieng va co canh bao."
                        : "Full sync khoa hoc, giao vien va hoc vien tu CSDT BAK vao QLHV_APP hoan tat.",
                Plan = completedPlan,
                InsertedHocVienRows = write.Inserted,
                UpdatedHocVienRows = write.Updated,
                ReactivatedHocVienRows = write.Reactivated,
                SoftDeletedHocVienRows = write.SoftDeleted,
                SkippedHocVienRows = write.Skipped,
                HocVien = ToDto(write.HocVien),
                KhoaHoc = ToDto(write.KhoaHoc),
                GiaoVien = ToDto(write.GiaoVien),
                KhoaHocGiaoVien = ToDto(write.Relation),
                PhotoQueue = photoQueue,
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

            var blocked = Blocked(failedPlan, "Full sync vao QLHV_APP that bai.");
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
        var khoaHocModels = new List<QlhvImportKhoaHocWriteModel>(source.KhoaHocSourceRows.Count);
        var giaoVienModels = new List<QlhvImportGiaoVienWriteModel>(source.GiaoVienRows.Count);
        var relationModels = new List<QlhvImportKhoaHocGiaoVienWriteModel>(source.KhoaHocGiaoVienRows.Count);
        var warnings = new List<string>();
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

        foreach (var sourceRow in source.KhoaHocSourceRows)
        {
            try
            {
                var mapped = QlhvImportCourseTeacherMapper.MapKhoaHoc(
                    sourceRow,
                    request.SourceProfileCode);
                blockers.AddRange(mapped.Blockers);
                warnings.AddRange(mapped.Warnings);
                if (mapped.Model is not null) khoaHocModels.Add(mapped.Model);
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add($"Khong map duoc KhoaHoc {sourceRow.MaKH}: {ex.Message}");
            }
        }

        foreach (var sourceRow in source.GiaoVienRows)
        {
            try
            {
                var mapped = QlhvImportCourseTeacherMapper.MapGiaoVien(
                    sourceRow,
                    request.SourceProfileCode);
                blockers.AddRange(mapped.Blockers);
                warnings.AddRange(mapped.Warnings);
                if (mapped.Model is not null) giaoVienModels.Add(mapped.Model);
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add($"Khong map duoc GiaoVien {sourceRow.MaGV}: {ex.Message}");
            }
        }

        foreach (var sourceRow in source.KhoaHocGiaoVienRows)
        {
            try
            {
                var mapped = QlhvImportCourseTeacherMapper.MapRelation(
                    sourceRow,
                    request.SourceProfileCode);
                blockers.AddRange(mapped.Blockers);
                warnings.AddRange(mapped.Warnings);
                if (mapped.Model is not null) relationModels.Add(mapped.Model);
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add($"Khong map duoc KhoaHoc_GiaoVien {sourceRow.MaLichLV}: {ex.Message}");
            }
        }

        if (source.HocVienRows.Count > 0 && sourceModels.Count == 0)
        {
            blockers.Add("Khong co dong hoc vien nao map duoc an toan de full sync.");
        }

        var duplicateKhoaHocKeys = CountDuplicateKeys(
            source.KhoaHocSourceRows.Select(row => row.MaKH));
        var duplicateGiaoVienKeys = CountDuplicateKeys(
            source.GiaoVienRows.Select(row => row.MaGV));
        var duplicateRelationKeys = source.KhoaHocGiaoVienRows
            .Where(row => row.MaLichLV > 0)
            .GroupBy(row => row.MaLichLV)
            .Count(group => group.Skip(1).Any());
        if (duplicateKhoaHocKeys > 0)
            blockers.Add($"Nguon co {duplicateKhoaHocKeys} SourceMaKhoaHoc bi trung.");
        if (duplicateGiaoVienKeys > 0)
            blockers.Add($"Nguon co {duplicateGiaoVienKeys} SourceMaGV bi trung.");
        if (duplicateRelationKeys > 0)
            blockers.Add($"Nguon co {duplicateRelationKeys} SourceMaLichLV bi trung.");

        var courseSourceKeys = khoaHocModels
            .Select(row => row.SourceMaKhoaHoc)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teacherSourceKeys = giaoVienModels
            .Select(row => row.SourceMaGV)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationConflicts = relationModels.Count(row =>
            !courseSourceKeys.Contains(row.SourceMaKhoaHoc) ||
            !teacherSourceKeys.Contains(row.SourceMaGV));
        var courseNaturalKeys = khoaHocModels
            .Select(row => row.MaKhoa)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        relationConflicts += sourceModels.Count(row =>
            !string.IsNullOrWhiteSpace(row.MaKhoa) &&
            !courseNaturalKeys.Contains(row.MaKhoa));

        if (relationConflicts > 0)
        {
            blockers.Add(
                $"Nguon co {relationConflicts} quan he hoc vien/phan cong khong tim thay khoa hoc hoac giao vien cung snapshot.");
        }

        var duplicateSourceKeys = duplicateSourceMaDks + duplicateKhoaHocKeys +
                                  duplicateGiaoVienKeys + duplicateRelationKeys;

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
                sourceModels,
                khoaHocModels,
                giaoVienModels,
                relationModels);
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
                sourceModels,
                khoaHocModels,
                giaoVienModels,
                relationModels);
        }

        if (target.SourceProfileConstraintExists && !target.SourceProfileAllowedByConstraint)
        {
            blockers.Add(
                $"CHECK constraint cua App_HocVien.SourceProfileCode hien khong cho phep {request.SourceProfileCode}.");
        }

        if (target.DuplicateTargetIdentityRows > 0)
        {
            blockers.Add(
                $"Target co {target.DuplicateTargetIdentityRows} source identity bi trung trong cac partition.");
        }

        var invalidTargetIdentities = target.HocVienRows.Count(row =>
            string.IsNullOrWhiteSpace(row.SourceMaDK)) +
            target.KhoaHocRows.Count(row => string.IsNullOrWhiteSpace(row.SourceKey)) +
            target.GiaoVienRows.Count(row => string.IsNullOrWhiteSpace(row.SourceKey)) +
            target.RelationRows.Count(row => string.IsNullOrWhiteSpace(row.SourceKey));
        if (invalidTargetIdentities > 0)
        {
            blockers.Add(
                $"Target partition co {invalidTargetIdentities} dong thieu source identity.");
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

        var khoaHocPlan = QlhvEntityFullSyncPlan.Empty;
        var giaoVienPlan = QlhvEntityFullSyncPlan.Empty;
        var relationPlan = QlhvEntityFullSyncPlan.Empty;
        if (target.DuplicateTargetIdentityRows == 0 && invalidTargetIdentities == 0)
        {
            try
            {
                khoaHocPlan = QlhvEntityFullSyncPlanner.BuildPlan(
                    khoaHocModels,
                    target.KhoaHocRows,
                    row => row.SourceMaKhoaHoc,
                    row => row.SourceHash,
                    "KhoaHoc");
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add(ex.Message);
            }

            try
            {
                giaoVienPlan = QlhvEntityFullSyncPlanner.BuildPlan(
                    giaoVienModels,
                    target.GiaoVienRows,
                    row => row.SourceMaGV,
                    row => row.SourceHash,
                    "GiaoVien");
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add(ex.Message);
            }

            try
            {
                relationPlan = QlhvEntityFullSyncPlanner.BuildPlan(
                    relationModels,
                    target.RelationRows,
                    row => row.SourceMaLichLV.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row => row.SourceHash,
                    "KhoaHoc_GiaoVien");
            }
            catch (InvalidOperationException ex)
            {
                blockers.Add(ex.Message);
            }
        }

        var photoPlan = new HocVienPhotoPlanDto(0, 0, 0, 0, 0);
        if (_photoProcessing is not null)
        {
            try
            {
                photoPlan = await _photoProcessing.BuildPlanAsync(
                    ToPhotoSources(sourceModels),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Photo inventory is advisory. A missing photo patch/model or an inaccessible
                // source root must never turn a valid database full-sync plan into a blocker.
                warnings.Add(
                    $"Khong lap duoc ke hoach anh the ({ex.GetType().Name}); full sync DB van duoc phep.");
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

        var hocVienDto = new QlhvEntitySyncCountsDto
        {
            SourceRows = source.HocVienRows.Count,
            Insert = fullSyncPlan?.PlannedInsertHocVienRows ?? 0,
            Update = fullSyncPlan?.PlannedUpdateHocVienRows ?? 0,
            Reactivate = fullSyncPlan?.PlannedReactivateHocVienRows ?? 0,
            SoftDelete = fullSyncPlan?.PlannedSoftDeleteHocVienRows ?? 0,
            Skip = fullSyncPlan?.PlannedSkipHocVienRows ?? 0,
            DuplicateSourceKeys = duplicateSourceMaDks,
        };
        var khoaHocDto = ToDto(
            khoaHocPlan,
            source.KhoaHocSourceRows.Count > 0 ? source.KhoaHocSourceRows.Count : source.KhoaHocRows,
            duplicateKhoaHocKeys);
        var giaoVienDto = ToDto(giaoVienPlan, source.GiaoVienRows.Count, duplicateGiaoVienKeys);
        var relationDto = ToDto(
            relationPlan,
            source.KhoaHocGiaoVienRows.Count,
            relationConflicts);

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
            PlannedInsertHocVienRows = hocVienDto.Insert,
            PlannedUpdateHocVienRows = hocVienDto.Update,
            PlannedReactivateHocVienRows = hocVienDto.Reactivate,
            PlannedSoftDeleteHocVienRows = hocVienDto.SoftDelete,
            PlannedSkipHocVienRows = hocVienDto.Skip,
            PlannedUpsertHocVienRows = hocVienDto.Upsert,
            PlannedUpsertKhoaHocRows = khoaHocDto.Upsert,
            HocVien = hocVienDto,
            KhoaHoc = khoaHocDto,
            GiaoVien = giaoVienDto,
            KhoaHocGiaoVien = relationDto,
            Photo = photoPlan,
            DuplicateSourceKeys = duplicateSourceKeys,
            RelationConflicts = relationConflicts,
            SourceRelationRows = source.KhoaHocGiaoVienRows.Count,
            Blockers = blockers,
            Warnings = warnings,
        };

        return new PlanContext(
            plan,
            sourceModels,
            khoaHocModels,
            giaoVienModels,
            relationModels,
            relationPlan);
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

    private static IReadOnlyList<HocVienPhotoProcessingSource> ToPhotoSources(
        IReadOnlyList<QlhvImportHocVienWriteModel> rows)
        => rows
            .Select(row => new HocVienPhotoProcessingSource(
                row.SourceProfileCode,
                row.SourceMaDK,
                row.MaKhoa,
                row.AnhRelativePath,
                row.SourcePhotoPathInvalid))
            .ToArray();

    private static int CountDuplicateKeys(IEnumerable<string?> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Skip(1).Any());

    private static QlhvEntitySyncCountsDto ToDto(
        QlhvEntityFullSyncPlan plan,
        int sourceRows,
        int duplicateSourceKeys)
        => new()
        {
            SourceRows = sourceRows,
            Insert = plan.Insert,
            Update = plan.Update,
            Reactivate = plan.Reactivate,
            SoftDelete = plan.SoftDelete,
            Skip = plan.Skip,
            DuplicateSourceKeys = duplicateSourceKeys,
        };

    private static QlhvEntitySyncCountsDto ToDto(QlhvEntityWriteCounts counts)
        => new()
        {
            SourceRows = counts.SourceRows,
            Insert = counts.Inserted,
            Update = counts.Updated,
            Reactivate = counts.Reactivated,
            SoftDelete = counts.SoftDeleted,
            Skip = counts.Skipped,
        };

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
            HocVien = new QlhvEntitySyncCountsDto
            {
                SourceRows = source?.HocVienRows.Count ?? 0,
                DuplicateSourceKeys = duplicateSourceMaDkRows,
            },
            KhoaHoc = new QlhvEntitySyncCountsDto
            {
                SourceRows = source?.KhoaHocSourceRows.Count > 0
                    ? source.KhoaHocSourceRows.Count
                    : source?.KhoaHocRows ?? 0,
            },
            GiaoVien = new QlhvEntitySyncCountsDto
            {
                SourceRows = source?.GiaoVienRows.Count ?? 0,
            },
            KhoaHocGiaoVien = new QlhvEntitySyncCountsDto
            {
                SourceRows = source?.KhoaHocGiaoVienRows.Count ?? 0,
            },
            DuplicateSourceKeys = duplicateSourceMaDkRows,
            SourceRelationRows = source?.KhoaHocGiaoVienRows.Count ?? 0,
            Blockers = blockers,
            Warnings = Array.Empty<string>(),
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
            HocVien = plan.HocVien,
            KhoaHoc = plan.KhoaHoc,
            GiaoVien = plan.GiaoVien,
            KhoaHocGiaoVien = plan.KhoaHocGiaoVien,
            Photo = plan.Photo,
            DuplicateSourceKeys = plan.DuplicateSourceKeys,
            RelationConflicts = plan.RelationConflicts,
            SourceRelationRows = plan.SourceRelationRows,
            Blockers = plan.Blockers.Concat(new[] { blocker }).Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = plan.Warnings,
        };

    private static QlhvImportPlanDto AddWarning(QlhvImportPlanDto plan, string warning)
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
            HocVien = plan.HocVien,
            KhoaHoc = plan.KhoaHoc,
            GiaoVien = plan.GiaoVien,
            KhoaHocGiaoVien = plan.KhoaHocGiaoVien,
            Photo = plan.Photo,
            DuplicateSourceKeys = plan.DuplicateSourceKeys,
            RelationConflicts = plan.RelationConflicts,
            SourceRelationRows = plan.SourceRelationRows,
            Blockers = plan.Blockers,
            Warnings = plan.Warnings.Concat(new[] { warning })
                .Distinct(StringComparer.Ordinal)
                .Take(100)
                .ToArray(),
        };

    private static QlhvImportExecuteResultDto Blocked(QlhvImportPlanDto plan, string message)
        => new()
        {
            Executed = false,
            Status = "BiChan",
            Message = message,
            Plan = plan,
        };

    private async Task<(HocVienPhotoQueueBatchResult? Result, string? Warning)>
        QueuePhotosAfterCommitSafelyAsync(
            IReadOnlyList<QlhvImportHocVienWriteModel> rows,
            string actor)
    {
        if (_photoProcessing is null)
        {
            return (null, null);
        }

        try
        {
            var result = await _photoProcessing.QueueAfterSyncAsync(
                ToPhotoSources(rows),
                actor,
                CancellationToken.None);
            var warning = result.Failed > 0
                ? $"Co {result.Failed} anh thieu, khong hop le hoac chua xu ly duoc; full sync DB da commit an toan."
                : null;
            return (result, warning);
        }
        catch (Exception ex)
        {
            // This method is called strictly after the database transaction returned as
            // committed. Photo failures are reported separately and must never change the
            // full-sync outcome or invite an unsafe automatic retry.
            return (
                null,
                $"Khong khoi tao duoc hang doi anh ({ex.GetType().Name}); full sync DB da commit an toan.");
        }
    }

    private async Task<string?> TryCompleteSucceededHistoryAsync(
        Guid operationId,
        QlhvImportPlanDto plan,
        QlhvImportFullSyncWriteResult write,
        HocVienPhotoQueueBatchResult? photoQueue,
        CancellationToken cancellationToken)
    {
        var sourceRows = write.TotalSourceRows;
        var completion = new QlhvOperationHistoryCompletion(
            operationId,
            QlhvOperationTypes.Succeeded,
            DateTime.UtcNow,
            sourceRows,
            write.TotalInserted,
            write.TotalUpdated,
            write.TotalReactivated,
            write.TotalSoftDeleted,
            write.TotalSkipped,
            plan.BackupSnapshotToken,
            null,
            JsonSerializer.Serialize(new
            {
                plan.SourceDatabaseName,
                plan.GeneratedAtUtc,
                plan.Warnings,
                HocVien = write.HocVien,
                KhoaHoc = write.KhoaHoc,
                GiaoVien = write.GiaoVien,
                KhoaHocGiaoVien = write.Relation,
                PhotoQueue = photoQueue,
                TargetActiveHocVienRows = Math.Max(
                    0,
                    plan.CurrentAppHocVienRows + write.Inserted + write.Reactivated - write.SoftDeleted),
                ImageFilesCopied = false,
            }),
            BackupRows: sourceRows,
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
                    plan.HocVien.SourceRows + plan.KhoaHoc.SourceRows + plan.GiaoVien.SourceRows + plan.SourceRelationRows,
                    0, 0, 0, 0, 0,
                    string.IsNullOrWhiteSpace(plan.BackupSnapshotToken) ? null : plan.BackupSnapshotToken,
                    safeError.Length <= 2000 ? safeError : safeError[..2000],
                    JsonSerializer.Serialize(new
                    {
                        plan.SourceDatabaseName,
                        plan.GeneratedAtUtc,
                        plan.Blockers,
                        plan.Warnings,
                        plan.HocVien,
                        plan.KhoaHoc,
                        plan.GiaoVien,
                        plan.RelationConflicts,
                        KhoaHocGiaoVienSourceRows = plan.SourceRelationRows,
                        ImageFilesCopied = false,
                    }),
                    BackupRows: plan.HocVien.SourceRows + plan.KhoaHoc.SourceRows + plan.GiaoVien.SourceRows + plan.SourceRelationRows,
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
            HocVien = result.HocVien,
            KhoaHoc = result.KhoaHoc,
            GiaoVien = result.GiaoVien,
            KhoaHocGiaoVien = result.KhoaHocGiaoVien,
            PhotoQueue = result.PhotoQueue,
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
        IReadOnlyList<QlhvImportHocVienWriteModel> SourceModels,
        IReadOnlyList<QlhvImportKhoaHocWriteModel> KhoaHocModels,
        IReadOnlyList<QlhvImportGiaoVienWriteModel> GiaoVienModels,
        IReadOnlyList<QlhvImportKhoaHocGiaoVienWriteModel> RelationModels,
        QlhvEntityFullSyncPlan RelationPlan)
    {
        public PlanContext(
            QlhvImportPlanDto plan,
            IReadOnlyList<QlhvImportHocVienWriteModel> sourceModels)
            : this(
                plan,
                sourceModels,
                Array.Empty<QlhvImportKhoaHocWriteModel>(),
                Array.Empty<QlhvImportGiaoVienWriteModel>(),
                Array.Empty<QlhvImportKhoaHocGiaoVienWriteModel>(),
                QlhvEntityFullSyncPlan.Empty)
        {
        }

        public PlanContext(
            QlhvImportPlanDto plan,
            IReadOnlyList<QlhvImportHocVienWriteModel> sourceModels,
            IReadOnlyList<QlhvImportKhoaHocWriteModel> khoaHocModels,
            IReadOnlyList<QlhvImportGiaoVienWriteModel> giaoVienModels,
            IReadOnlyList<QlhvImportKhoaHocGiaoVienWriteModel> relationModels)
            : this(
                plan,
                sourceModels,
                khoaHocModels,
                giaoVienModels,
                relationModels,
                QlhvEntityFullSyncPlan.Empty)
        {
        }

        public QlhvImportFullSyncPayload Payload => new(
            KhoaHocModels,
            GiaoVienModels,
            RelationModels,
            SourceModels,
            Plan.BackupSnapshotToken);
    }
}
