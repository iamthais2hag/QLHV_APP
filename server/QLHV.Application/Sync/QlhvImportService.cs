using Microsoft.Extensions.Options;
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

    public QlhvImportService(
        IQlhvImportReadRepository readRepository,
        IQlhvHocVienTargetRepository targetRepository,
        IQlhvImportWriteRepository writeRepository,
        IOptions<SyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions)
    {
        _readRepository = readRepository;
        _targetRepository = targetRepository;
        _writeRepository = writeRepository;
        _syncOptions = syncOptions.Value;
        _executionOptions = executionOptions.Value;
    }

    public async Task<QlhvImportPlanDto> GetPlanAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
        => (await BuildPlanContextAsync(Normalize(request), cancellationToken)).Plan;

    public async Task<QlhvImportDiagnosticsDto> GetDiagnosticsAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = (await BuildPlanContextAsync(Normalize(request), cancellationToken)).Plan;
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

        var context = await BuildPlanContextAsync(normalized, cancellationToken);
        if (!context.Plan.Executable)
        {
            return Blocked(context.Plan, "Import bi chan vi plan co blocker.");
        }

        if (_syncOptions.DryRun)
        {
            return Blocked(
                AddBlocker(context.Plan, "Ghi vao QLHV_APP bi chan: Sync:DryRun = true."),
                "Import bi chan boi cau hinh an toan.");
        }

        if (!_executionOptions.EnableTargetWrites)
        {
            return Blocked(
                AddBlocker(context.Plan, "Ghi vao QLHV_APP bi chan: SyncExecution.EnableTargetWrites = false."),
                "Import bi chan boi cau hinh an toan.");
        }

        try
        {
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

                return Blocked(
                    blockedPlan,
                    "Full sync bi chan boi kiem tra an toan trong transaction.");
            }

            return new QlhvImportExecuteResultDto
            {
                Executed = true,
                Status = "ThanhCong",
                Message = "Full sync hoc vien tu CSDT BAK vao QLHV_APP hoan tat.",
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
            throw;
        }
        catch (Exception ex)
        {
            return Blocked(
                AddBlocker(context.Plan, $"Import that bai. Chi tiet: {ex.GetType().Name}."),
                "Full sync hoc vien vao QLHV_APP that bai.");
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

    private sealed record ImportSourceDefinition(string MaCsdt, string ExpectedDatabaseName);

    private sealed record PlanContext(
        QlhvImportPlanDto Plan,
        IReadOnlyList<QlhvImportHocVienWriteModel> SourceModels);
}
