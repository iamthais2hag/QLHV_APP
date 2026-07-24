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
            HocVienStatus = plan.HocVienStatus,
            KhoaHocStatus = plan.KhoaHocStatus,
            GiaoVienStatus = plan.GiaoVienStatus,
            RelationStatus = plan.RelationStatus,
            HocVienBlockers = plan.HocVienBlockers,
            KhoaHocBlockers = plan.KhoaHocBlockers,
            GiaoVienBlockers = plan.GiaoVienBlockers,
            RelationBlockers = plan.RelationBlockers,
            OptionalWarnings = plan.OptionalWarnings,
            ExecutableDomains = plan.ExecutableDomains,
            SkippedDomains = plan.SkippedDomains,
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

            // Once the first domain transaction can commit, an HTTP disconnect must not
            // cancel the remaining domains and leave an unaudited partial write. The
            // durable operation lock/history own completion from this point onward.
            var write = await _writeRepository.FullSyncAsync(
                context.Plan.SourceProfileCode,
                context.Payload,
                CancellationToken.None);
            if (write.RequiredDomainFailed && write.DomainResults.Count > 0)
            {
                var failedPlan = context.Plan;
                var hocVienFailure = write.DomainResults.First(result =>
                    string.Equals(result.Domain, QlhvImportDomains.HocVien, StringComparison.Ordinal) &&
                    string.Equals(result.Status, QlhvImportDomainStatuses.Failed, StringComparison.Ordinal));
                failedPlan = AddHocVienBlocker(
                    failedPlan,
                    hocVienFailure.Message ?? "HocVien transaction that bai.");
                foreach (var optionalResult in write.DomainResults.Where(result =>
                             !string.Equals(
                                 result.Domain,
                                 QlhvImportDomains.HocVien,
                                 StringComparison.Ordinal)))
                {
                    failedPlan = AddWarning(
                        failedPlan,
                        $"{optionalResult.Domain}: {optionalResult.Message ?? optionalResult.Status}.");
                }

                var safeError = hocVienFailure.Message ?? "HocVien transaction that bai.";
                var failureHistoryWarning = await TryCompleteWriteHistoryAsync(
                    operationId,
                    failedPlan,
                    write,
                    photoQueue: null,
                    QlhvImportOverallStatuses.Failed,
                    safeError);
                return new QlhvImportExecuteResultDto
                {
                    OperationId = operationId,
                    Executed = true,
                    Status = QlhvImportOverallStatuses.Failed,
                    Message = failureHistoryWarning is null
                        ? "HocVien that bai; cac module truoc do co the da commit. " +
                          "Xem ket qua tung module va khong tu dong retry."
                        : "HocVien that bai sau khi mot so module co the da commit; " +
                          "cap nhat lich su cung that bai, khong duoc retry tu dong.",
                    Plan = failedPlan,
                    InsertedHocVienRows = write.Inserted,
                    UpdatedHocVienRows = write.Updated,
                    ReactivatedHocVienRows = write.Reactivated,
                    SoftDeletedHocVienRows = write.SoftDeleted,
                    SkippedHocVienRows = write.Skipped,
                    HocVien = ToDto(write.HocVien),
                    KhoaHoc = ToDto(write.KhoaHoc),
                    GiaoVien = ToDto(write.GiaoVien),
                    KhoaHocGiaoVien = ToDto(write.Relation),
                    DomainResults = write.DomainResults.Select(ToDto).ToArray(),
                };
            }

            if (write.HasConflicts)
            {
                var blockedPlan = context.Plan;
                var hocVienFailure = write.DomainResults.FirstOrDefault(result =>
                    string.Equals(result.Domain, QlhvImportDomains.HocVien, StringComparison.Ordinal) &&
                    string.Equals(result.Status, QlhvImportDomainStatuses.Failed, StringComparison.Ordinal));
                if (hocVienFailure is not null)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        hocVienFailure.Message ?? "HocVien transaction that bai.");
                }
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

            var domainResults = write.DomainResults.Select(ToDto).ToArray();
            var optionalIssues = write.DomainResults
                .Where(result =>
                    !string.Equals(result.Domain, QlhvImportDomains.HocVien, StringComparison.Ordinal) &&
                    !string.Equals(result.Status, QlhvImportDomainStatuses.Succeeded, StringComparison.Ordinal) &&
                    !string.Equals(result.Status, QlhvImportDomainStatuses.NoOp, StringComparison.Ordinal))
                .ToArray();
            var overallStatus = optionalIssues.Length > 0
                ? QlhvImportOverallStatuses.PartialSuccess
                : write.TotalInserted + write.TotalUpdated + write.TotalReactivated + write.TotalSoftDeleted == 0
                    ? QlhvImportOverallStatuses.NoOp
                    : QlhvImportOverallStatuses.Success;
            var resultPlan = context.Plan;
            foreach (var issue in optionalIssues)
            {
                resultPlan = AddWarning(
                    resultPlan,
                    $"{issue.Domain}: {issue.Message ?? issue.Status}; module da khong xoa du lieu cu.");
            }

            var (photoQueue, photoWarning) = await QueuePhotosAfterCommitSafelyAsync(
                context.Payload.HocVienRows,
                operationActor);
            var completedPlan = photoWarning is null
                ? resultPlan
                : AddWarning(resultPlan, photoWarning);

            var historyWarning = await TryCompleteWriteHistoryAsync(
                operationId,
                completedPlan,
                write,
                photoQueue,
                overallStatus,
                safeError: null);
            return new QlhvImportExecuteResultDto
            {
                OperationId = operationId,
                Executed = true,
                Status = overallStatus,
                Message = historyWarning is not null
                    ? "Full sync da commit vao QLHV_APP, nhung cap nhat lich su that bai; khong duoc retry tu dong."
                    : string.Equals(overallStatus, QlhvImportOverallStatuses.PartialSuccess, StringComparison.Ordinal)
                        ? "HocVien da dong bo; module tuy chon chua san sang hoac that bai da duoc bo qua, khong bi xoa."
                    : photoWarning is not null
                        ? "Full sync DB da commit hoan tat; xu ly anh duoc tach rieng va co canh bao."
                        : string.Equals(overallStatus, QlhvImportOverallStatuses.NoOp, StringComparison.Ordinal)
                            ? "Snapshot da khop QLHV_APP; khong co thay doi can ghi."
                            : "Cac module san sang da dong bo tu CSDT BAK vao QLHV_APP.",
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
                DomainResults = domainResults,
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
        var globalBlockers = Validate(request).ToList();
        var hocVienBlockers = new List<string>();
        var khoaHocBlockers = new List<string>();
        var giaoVienBlockers = new List<string>();
        var relationBlockers = new List<string>();
        var warnings = new List<string>();
        var optionalWarnings = new List<string>();
        if (globalBlockers.Count > 0)
        {
            return new PlanContext(
                CreateBasePlan(request, globalBlockers) with
                {
                    HocVienBlockers = ["Khong the lap plan HocVien khi yeu cau dau vao khong hop le."],
                },
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        try
        {
            var targetDiagnostics = await _targetRepository.GetDiagnosticsAsync(cancellationToken);
            if (!targetDiagnostics.AppHocVienExists)
            {
                hocVienBlockers.Add("Target QLHV_APP thieu bang dbo.App_HocVien.");
            }

            var missingColumns = targetDiagnostics.RequiredColumns
                .Where(column => !column.Exists)
                .Select(column => column.ColumnName)
                .ToArray();
            if (missingColumns.Length > 0)
            {
                hocVienBlockers.Add(
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
            globalBlockers.Add($"Khong doc duoc schema QLHV_APP. Chi tiet: {ex.GetType().Name}.");
        }

        if (globalBlockers.Count > 0 || hocVienBlockers.Count > 0)
        {
            return new PlanContext(
                CreateBasePlan(request, globalBlockers) with
                {
                    HocVienBlockers = Distinct(hocVienBlockers),
                },
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
            hocVienBlockers.Add(ex.Message);
            return new PlanContext(
                CreateBasePlan(request, globalBlockers) with
                {
                    HocVienBlockers = Distinct(hocVienBlockers),
                },
                Array.Empty<QlhvImportHocVienWriteModel>());
        }
        catch (Exception ex)
        {
            globalBlockers.Add(
                $"Khong doc duoc nguon {request.SourceProfileCode}. Chi tiet: {ex.GetType().Name}.");
            return new PlanContext(
                CreateBasePlan(request, globalBlockers),
                Array.Empty<QlhvImportHocVienWriteModel>());
        }

        var definition = SupportedProfiles[request.SourceProfileCode];
        if (!string.Equals(source.SourceDatabaseName, definition.ExpectedDatabaseName, StringComparison.Ordinal))
        {
            globalBlockers.Add(
                $"Source database la {source.SourceDatabaseName}; bat buoc phai la {definition.ExpectedDatabaseName}.");
        }

        warnings.AddRange(source.HocVienWarnings);
        khoaHocBlockers.AddRange(source.KhoaHocBlockers);
        giaoVienBlockers.AddRange(source.GiaoVienBlockers);
        relationBlockers.AddRange(source.RelationBlockers);

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
            hocVienBlockers.Add($"Nguon co {duplicateSourceMaDks} MaDK bi trung trong pham vi full sync.");
        }

        if (source.HocVienRows.Count == 0)
        {
            hocVienBlockers.Add(
                "Nguon co 0 hoc vien; full sync HocVien bi chan de khong soft-delete toan bo partition.");
        }

        var blankSourceKeys = source.HocVienRows.Count - normalizedSourceMaDks.Length;
        if (blankSourceKeys > 0)
        {
            hocVienBlockers.Add($"Nguon co {blankSourceKeys} dong thieu MaDK/SourceMaDK.");
        }

        var sourceIdentity = new HocVienSourceIdentityContext(request.SourceProfileCode, "V2");
        var sourceModels = new List<QlhvImportHocVienWriteModel>(source.HocVienRows.Count);
        var khoaHocModels = new List<QlhvImportKhoaHocWriteModel>(source.KhoaHocSourceRows.Count);
        var giaoVienModels = new List<QlhvImportGiaoVienWriteModel>(source.GiaoVienRows.Count);
        var relationModels = new List<QlhvImportKhoaHocGiaoVienWriteModel>(source.KhoaHocGiaoVienRows.Count);
        foreach (var sourceRow in source.HocVienRows)
        {
            var mapped = QlhvImportHocVienMapper.MapAndValidate(sourceRow, sourceIdentity);
            warnings.AddRange(mapped.Warnings.Select(warning =>
                $"{warning.MaDK}: {warning.Message}"));
            hocVienBlockers.AddRange(mapped.Blockers);
            if (!mapped.ShouldSkip && mapped.Model is not null)
            {
                sourceModels.Add(mapped.Model);
            }
        }

        if (khoaHocBlockers.Count == 0)
        {
            foreach (var sourceRow in source.KhoaHocSourceRows)
            {
                try
                {
                    var mapped = QlhvImportCourseTeacherMapper.MapKhoaHoc(
                        sourceRow,
                        request.SourceProfileCode);
                    khoaHocBlockers.AddRange(mapped.Blockers);
                    optionalWarnings.AddRange(mapped.Warnings.Select(value => $"KhoaHoc: {value}"));
                    if (mapped.Model is not null) khoaHocModels.Add(mapped.Model);
                }
                catch (InvalidOperationException ex)
                {
                    khoaHocBlockers.Add($"Khong map duoc KhoaHoc {sourceRow.MaKH}: {ex.Message}");
                }
            }
        }

        if (giaoVienBlockers.Count == 0)
        {
            foreach (var sourceRow in source.GiaoVienRows)
            {
                try
                {
                    var mapped = QlhvImportCourseTeacherMapper.MapGiaoVien(
                        sourceRow,
                        request.SourceProfileCode);
                    giaoVienBlockers.AddRange(mapped.Blockers);
                    optionalWarnings.AddRange(mapped.Warnings.Select(value => $"GiaoVien: {value}"));
                    if (mapped.Model is not null) giaoVienModels.Add(mapped.Model);
                }
                catch (InvalidOperationException ex)
                {
                    giaoVienBlockers.Add($"Khong map duoc GiaoVien {sourceRow.MaGV}: {ex.Message}");
                }
            }
        }

        if (relationBlockers.Count == 0)
        {
            foreach (var sourceRow in source.KhoaHocGiaoVienRows)
            {
                try
                {
                    var mapped = QlhvImportCourseTeacherMapper.MapRelation(
                        sourceRow,
                        request.SourceProfileCode);
                    relationBlockers.AddRange(mapped.Blockers);
                    optionalWarnings.AddRange(mapped.Warnings.Select(value => $"QuanHe: {value}"));
                    if (mapped.Model is not null) relationModels.Add(mapped.Model);
                }
                catch (InvalidOperationException ex)
                {
                    relationBlockers.Add(
                        $"Khong map duoc KhoaHoc_GiaoVien {sourceRow.MaLichLV}: {ex.Message}");
                }
            }
        }

        if (source.HocVienRows.Count > 0 && sourceModels.Count == 0)
        {
            hocVienBlockers.Add("Khong co dong hoc vien nao map duoc an toan de full sync.");
        }
        if (source.KhoaHocSourceRows.Count == 0)
        {
            khoaHocBlockers.Add(
                "Source KhoaHoc co 0 dong; module duoc bo qua de khong soft-delete du lieu cu.");
        }
        if (source.GiaoVienRows.Count == 0)
        {
            giaoVienBlockers.Add(
                "Source GiaoVien co 0 dong; module duoc bo qua de khong soft-delete du lieu cu.");
        }
        if (source.KhoaHocGiaoVienRows.Count == 0)
        {
            relationBlockers.Add(
                "Source quan he co 0 dong; module duoc bo qua de khong soft-delete du lieu cu.");
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
            khoaHocBlockers.Add($"Nguon co {duplicateKhoaHocKeys} SourceMaKhoaHoc bi trung.");
        if (duplicateGiaoVienKeys > 0)
            giaoVienBlockers.Add($"Nguon co {duplicateGiaoVienKeys} SourceMaGV bi trung.");
        if (duplicateRelationKeys > 0)
            relationBlockers.Add($"Nguon co {duplicateRelationKeys} SourceMaLichLV bi trung.");

        var courseSourceKeys = khoaHocModels
            .Select(row => row.SourceMaKhoaHoc)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teacherSourceKeys = giaoVienModels
            .Select(row => row.SourceMaGV)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationConflicts = relationModels.Count(row =>
            !courseSourceKeys.Contains(row.SourceMaKhoaHoc) ||
            !teacherSourceKeys.Contains(row.SourceMaGV));
        if (relationConflicts > 0)
        {
            relationBlockers.Add(
                $"Nguon co {relationConflicts} quan he khong tim thay KhoaHoc/GiaoVien cung snapshot.");
        }

        var courseNaturalKeys = khoaHocModels
            .Select(row => row.MaKhoa)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hocVienCourseReferencesMissing = sourceModels.Count(row =>
            !string.IsNullOrWhiteSpace(row.MaKhoa) &&
            !courseNaturalKeys.Contains(row.MaKhoa));
        if (hocVienCourseReferencesMissing > 0)
        {
            optionalWarnings.Add(
                $"Co {hocVienCourseReferencesMissing} HocVien tham chieu khoa hoc khong co trong module KhoaHoc; " +
                "HocVien van duoc dong bo doc lap.");
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
        catch (QlhvImportGlobalBlockerException ex)
        {
            globalBlockers.Add(ex.Message);
            return new PlanContext(
                CreateBasePlan(
                    request,
                    globalBlockers,
                    source,
                    distinctSourceMaDks.Length,
                    duplicateSourceMaDks) with
                {
                    HocVienBlockers = Distinct(hocVienBlockers),
                    KhoaHocBlockers = Distinct(khoaHocBlockers),
                    GiaoVienBlockers = Distinct(giaoVienBlockers),
                    RelationBlockers = Distinct(relationBlockers),
                },
                sourceModels,
                khoaHocModels,
                giaoVienModels,
                relationModels);
        }
        catch (QlhvImportReadException ex)
        {
            hocVienBlockers.Add(ex.Message);
            return new PlanContext(
                CreateBasePlan(
                    request,
                    globalBlockers,
                    source,
                    distinctSourceMaDks.Length,
                    duplicateSourceMaDks) with
                {
                    HocVienBlockers = Distinct(hocVienBlockers),
                    KhoaHocBlockers = Distinct(khoaHocBlockers),
                    GiaoVienBlockers = Distinct(giaoVienBlockers),
                    RelationBlockers = Distinct(relationBlockers),
                },
                sourceModels,
                khoaHocModels,
                giaoVienModels,
                relationModels);
        }
        catch (Exception ex)
        {
            globalBlockers.Add(
                $"Khong doc duoc du lieu hien tai trong QLHV_APP. Chi tiet: {ex.GetType().Name}.");
            return new PlanContext(
                CreateBasePlan(
                    request,
                    globalBlockers,
                    source,
                    distinctSourceMaDks.Length,
                    duplicateSourceMaDks) with
                {
                    HocVienBlockers = Distinct(hocVienBlockers),
                    KhoaHocBlockers = Distinct(khoaHocBlockers),
                    GiaoVienBlockers = Distinct(giaoVienBlockers),
                    RelationBlockers = Distinct(relationBlockers),
                },
                sourceModels,
                khoaHocModels,
                giaoVienModels,
                relationModels);
        }

        khoaHocBlockers.AddRange(target.KhoaHocBlockers);
        giaoVienBlockers.AddRange(target.GiaoVienBlockers);
        relationBlockers.AddRange(target.RelationBlockers);

        if (target.SourceProfileConstraintExists && !target.SourceProfileAllowedByConstraint)
        {
            hocVienBlockers.Add(
                $"CHECK constraint cua App_HocVien.SourceProfileCode hien khong cho phep {request.SourceProfileCode}.");
        }
        if (target.DuplicateHocVienTargetIdentityRows > 0)
        {
            hocVienBlockers.Add(
                $"Target HocVien co {target.DuplicateHocVienTargetIdentityRows} source identity bi trung.");
        }
        if (target.DuplicateKhoaHocTargetIdentityRows > 0)
        {
            khoaHocBlockers.Add(
                $"Target KhoaHoc co {target.DuplicateKhoaHocTargetIdentityRows} source identity bi trung.");
        }
        if (target.DuplicateGiaoVienTargetIdentityRows > 0)
        {
            giaoVienBlockers.Add(
                $"Target GiaoVien co {target.DuplicateGiaoVienTargetIdentityRows} source identity bi trung.");
        }
        if (target.DuplicateRelationTargetIdentityRows > 0)
        {
            relationBlockers.Add(
                $"Target quan he co {target.DuplicateRelationTargetIdentityRows} source identity bi trung.");
        }

        var invalidHocVienTargetIdentities = target.HocVienRows.Count(row =>
            string.IsNullOrWhiteSpace(row.SourceMaDK));
        var invalidKhoaHocTargetIdentities = target.KhoaHocRows.Count(row =>
            string.IsNullOrWhiteSpace(row.SourceKey));
        var invalidGiaoVienTargetIdentities = target.GiaoVienRows.Count(row =>
            string.IsNullOrWhiteSpace(row.SourceKey));
        var invalidRelationTargetIdentities = target.RelationRows.Count(row =>
            string.IsNullOrWhiteSpace(row.SourceKey));
        if (invalidHocVienTargetIdentities > 0)
            hocVienBlockers.Add($"Target HocVien co {invalidHocVienTargetIdentities} dong thieu source identity.");
        if (invalidKhoaHocTargetIdentities > 0)
            khoaHocBlockers.Add($"Target KhoaHoc co {invalidKhoaHocTargetIdentities} dong thieu source identity.");
        if (invalidGiaoVienTargetIdentities > 0)
            giaoVienBlockers.Add($"Target GiaoVien co {invalidGiaoVienTargetIdentities} dong thieu source identity.");
        if (invalidRelationTargetIdentities > 0)
            relationBlockers.Add($"Target quan he co {invalidRelationTargetIdentities} dong thieu source identity.");

        if (target.TargetMaDkConflictsOtherProfiles > 0)
        {
            warnings.Add(
                $"Co {target.TargetMaDkConflictsOtherProfiles} MaDK cung xuat hien o profile khac; " +
                "full sync van tach biet bang SourceProfileCode + SourceMaDK.");
        }

        QlhvFullSyncPlan? fullSyncPlan = null;
        if (sourceModels.Count > 0 && hocVienBlockers.Count == 0)
        {
            try
            {
                fullSyncPlan = QlhvFullSyncPlanner.BuildPlan(sourceModels, target.HocVienRows);
            }
            catch (InvalidOperationException ex)
            {
                hocVienBlockers.Add(ex.Message);
            }
        }

        var khoaHocPlan = QlhvEntityFullSyncPlan.Empty;
        if (khoaHocModels.Count > 0 && khoaHocBlockers.Count == 0)
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
                khoaHocBlockers.Add(ex.Message);
            }
        }

        var giaoVienPlan = QlhvEntityFullSyncPlan.Empty;
        if (giaoVienModels.Count > 0 && giaoVienBlockers.Count == 0)
        {
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
                giaoVienBlockers.Add(ex.Message);
            }
        }

        var khoaHocExecutable = khoaHocModels.Count > 0 && khoaHocBlockers.Count == 0;
        var giaoVienExecutable = giaoVienModels.Count > 0 && giaoVienBlockers.Count == 0;
        if (!khoaHocExecutable || !giaoVienExecutable)
        {
            relationBlockers.Add(
                "Quan he duoc bo qua vi KhoaHoc hoac GiaoVien chua san sang.");
        }

        var relationPlan = QlhvEntityFullSyncPlan.Empty;
        if (relationModels.Count > 0 &&
            relationBlockers.Count == 0 &&
            khoaHocExecutable &&
            giaoVienExecutable)
        {
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
                relationBlockers.Add(ex.Message);
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
                warnings.Add(
                    $"Khong lap duoc ke hoach anh the ({ex.GetType().Name}); full sync DB van duoc phep.");
            }
        }

        globalBlockers = Distinct(globalBlockers).ToList();
        hocVienBlockers = Distinct(hocVienBlockers).ToList();
        khoaHocBlockers = Distinct(khoaHocBlockers).ToList();
        giaoVienBlockers = Distinct(giaoVienBlockers).ToList();
        relationBlockers = Distinct(relationBlockers).ToList();
        optionalWarnings.AddRange(khoaHocBlockers.Select(value => $"KhoaHoc tam bo qua: {value}"));
        optionalWarnings.AddRange(giaoVienBlockers.Select(value => $"GiaoVien tam bo qua: {value}"));
        optionalWarnings.AddRange(relationBlockers.Select(value => $"QuanHe tam bo qua: {value}"));
        optionalWarnings = Distinct(optionalWarnings).Take(100).ToList();
        warnings.AddRange(optionalWarnings);
        warnings = Distinct(warnings).Take(100).ToList();

        var executableDomains = new List<string>();
        if (globalBlockers.Count == 0 &&
            khoaHocModels.Count > 0 &&
            khoaHocBlockers.Count == 0)
            executableDomains.Add(QlhvImportDomains.KhoaHoc);
        if (globalBlockers.Count == 0 &&
            giaoVienModels.Count > 0 &&
            giaoVienBlockers.Count == 0)
            executableDomains.Add(QlhvImportDomains.GiaoVien);
        if (globalBlockers.Count == 0 &&
            relationModels.Count > 0 &&
            relationBlockers.Count == 0)
            executableDomains.Add(QlhvImportDomains.Relation);
        if (globalBlockers.Count == 0 &&
            hocVienBlockers.Count == 0 &&
            sourceModels.Count > 0 &&
            fullSyncPlan is not null)
        {
            executableDomains.Add(QlhvImportDomains.HocVien);
        }

        var skippedDomains = QlhvImportDomains.Ordered
            .Where(domain => !executableDomains.Contains(domain, StringComparer.Ordinal))
            .ToArray();
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
            source.KhoaHocSourceRows.Count,
            duplicateKhoaHocKeys);
        var giaoVienDto = ToDto(giaoVienPlan, source.GiaoVienRows.Count, duplicateGiaoVienKeys);
        var relationDto = ToDto(
            relationPlan,
            source.KhoaHocGiaoVienRows.Count,
            duplicateRelationKeys);

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
            DuplicateSourceKeys = duplicateSourceMaDks,
            RelationConflicts = relationConflicts,
            SourceRelationRows = source.KhoaHocGiaoVienRows.Count,
            HocVienStatus = executableDomains.Contains(QlhvImportDomains.HocVien, StringComparer.Ordinal)
                ? QlhvImportDomainStatuses.Executable
                : QlhvImportDomainStatuses.Blocked,
            KhoaHocStatus = ResolveOptionalStatus(
                executableDomains,
                QlhvImportDomains.KhoaHoc,
                khoaHocBlockers,
                target.KhoaHocBlockers,
                source.KhoaHocBlockers,
                dependencyNotReady: false),
            GiaoVienStatus = ResolveOptionalStatus(
                executableDomains,
                QlhvImportDomains.GiaoVien,
                giaoVienBlockers,
                target.GiaoVienBlockers,
                source.GiaoVienBlockers,
                dependencyNotReady: false),
            RelationStatus = ResolveOptionalStatus(
                executableDomains,
                QlhvImportDomains.Relation,
                relationBlockers,
                target.RelationBlockers,
                source.RelationBlockers,
                dependencyNotReady: !khoaHocExecutable || !giaoVienExecutable),
            HocVienBlockers = hocVienBlockers,
            KhoaHocBlockers = khoaHocBlockers,
            GiaoVienBlockers = giaoVienBlockers,
            RelationBlockers = relationBlockers,
            OptionalWarnings = optionalWarnings,
            ExecutableDomains = executableDomains,
            SkippedDomains = skippedDomains,
            Blockers = globalBlockers,
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

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string ResolveOptionalStatus(
        IReadOnlyCollection<string> executableDomains,
        string domain,
        IReadOnlyCollection<string> allDomainBlockers,
        IReadOnlyCollection<string> targetSchemaBlockers,
        IReadOnlyCollection<string> sourceBlockers,
        bool dependencyNotReady)
    {
        if (executableDomains.Contains(domain, StringComparer.Ordinal))
        {
            return QlhvImportDomainStatuses.Executable;
        }

        if (targetSchemaBlockers.Count > 0)
        {
            return QlhvImportDomainStatuses.SkippedSchemaNotReady;
        }

        if (dependencyNotReady)
        {
            return QlhvImportDomainStatuses.SkippedDependencyNotReady;
        }

        return sourceBlockers.Count > 0 || allDomainBlockers.Count > 0
            ? QlhvImportDomainStatuses.SkippedSourceNotReady
            : QlhvImportDomainStatuses.Blocked;
    }

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

    private static QlhvImportDomainResultDto ToDto(QlhvDomainWriteResult result)
        => new()
        {
            Domain = result.Domain,
            Status = result.Status,
            Message = result.Message,
            Counts = ToDto(result.Counts),
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
            HocVienStatus = QlhvImportDomainStatuses.Blocked,
            KhoaHocStatus = QlhvImportDomainStatuses.SkippedSourceNotReady,
            GiaoVienStatus = QlhvImportDomainStatuses.SkippedSourceNotReady,
            RelationStatus = QlhvImportDomainStatuses.SkippedDependencyNotReady,
            ExecutableDomains = Array.Empty<string>(),
            SkippedDomains = QlhvImportDomains.Ordered,
            Blockers = blockers,
            Warnings = Array.Empty<string>(),
        };

    private static QlhvImportPlanDto AddBlocker(QlhvImportPlanDto plan, string blocker)
        => plan with
        {
            Blockers = plan.Blockers.Concat(new[] { blocker }).Distinct(StringComparer.Ordinal).ToArray(),
        };

    private static QlhvImportPlanDto AddHocVienBlocker(
        QlhvImportPlanDto plan,
        string blocker)
        => plan with
        {
            HocVienStatus = QlhvImportDomainStatuses.Blocked,
            HocVienBlockers = plan.HocVienBlockers
                .Concat(new[] { blocker })
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ExecutableDomains = plan.ExecutableDomains
                .Where(domain => !string.Equals(
                    domain,
                    QlhvImportDomains.HocVien,
                    StringComparison.Ordinal))
                .ToArray(),
            SkippedDomains = plan.SkippedDomains
                .Concat(new[] { QlhvImportDomains.HocVien })
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };

    private static QlhvImportPlanDto AddWarning(QlhvImportPlanDto plan, string warning)
        => plan with
        {
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

    private async Task<string?> TryCompleteWriteHistoryAsync(
        Guid operationId,
        QlhvImportPlanDto plan,
        QlhvImportFullSyncWriteResult write,
        HocVienPhotoQueueBatchResult? photoQueue,
        string overallStatus,
        string? safeError)
    {
        var sourceRows = write.TotalSourceRows;
        var historyStatus = overallStatus switch
        {
            QlhvImportOverallStatuses.PartialSuccess => QlhvOperationTypes.PartialSuccess,
            QlhvImportOverallStatuses.Failed => QlhvOperationTypes.Failed,
            _ => QlhvOperationTypes.Succeeded,
        };
        var completion = new QlhvOperationHistoryCompletion(
            operationId,
            historyStatus,
            DateTime.UtcNow,
            sourceRows,
            write.TotalInserted,
            write.TotalUpdated,
            write.TotalReactivated,
            write.TotalSoftDeleted,
            write.TotalSkipped,
            plan.BackupSnapshotToken,
            string.IsNullOrWhiteSpace(safeError)
                ? null
                : safeError.Length <= 2000
                    ? safeError
                    : safeError[..2000],
            JsonSerializer.Serialize(new
            {
                OverallStatus = overallStatus,
                plan.SourceDatabaseName,
                plan.GeneratedAtUtc,
                plan.Warnings,
                plan.ExecutableDomains,
                plan.SkippedDomains,
                HocVien = write.HocVien,
                KhoaHoc = write.KhoaHoc,
                GiaoVien = write.GiaoVien,
                KhoaHocGiaoVien = write.Relation,
                DomainResults = write.DomainResults,
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
            if (persisted is not null &&
                string.Equals(persisted.Status, historyStatus, StringComparison.Ordinal))
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
            DomainResults = result.DomainResults,
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
            Plan.BackupSnapshotToken,
            Plan.ExecutableDomains,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [QlhvImportDomains.KhoaHoc] =
                    $"{Plan.KhoaHocStatus}: " +
                    (Plan.KhoaHocBlockers.FirstOrDefault() ?? "Module KhoaHoc khong duoc chon."),
                [QlhvImportDomains.GiaoVien] =
                    $"{Plan.GiaoVienStatus}: " +
                    (Plan.GiaoVienBlockers.FirstOrDefault() ?? "Module GiaoVien khong duoc chon."),
                [QlhvImportDomains.Relation] =
                    $"{Plan.RelationStatus}: " +
                    (Plan.RelationBlockers.FirstOrDefault() ?? "Module quan he khong duoc chon."),
                [QlhvImportDomains.HocVien] =
                    $"{Plan.HocVienStatus}: " +
                    (Plan.HocVienBlockers.FirstOrDefault() ?? "Module HocVien khong duoc chon."),
            });
    }
}
