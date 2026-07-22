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

    private static readonly IReadOnlyDictionary<string, string> SupportedProfiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CsdtConnectionProfileCodes.CsdtMoto] = "66030",
            [CsdtConnectionProfileCodes.CsdtOto] = "66029",
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
        var context = await BuildPlanContextAsync(Normalize(request), cancellationToken);
        var normalizedMaDks = context.SourceRows
            .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
            .Select(row => row.MaDK.Trim())
            .ToArray();
        var distinctMaDks = normalizedMaDks
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var duplicateMaDks = CountDuplicateSourceMaDks(normalizedMaDks);
        var target = context.Target;

        return new QlhvImportDiagnosticsDto
        {
            SourceProfileCode = context.Plan.SourceProfileCode,
            MaCSDT = context.Plan.MaCSDT,
            MaKhoaHoc = context.Plan.MaKhoaHoc,
            SourceHocVienRows = context.Plan.SourceHocVienRows,
            SourceDistinctMaDkRows = distinctMaDks,
            DuplicateSourceMaDkRows = duplicateMaDks,
            CurrentAppHocVienRows = context.Plan.CurrentAppHocVienRows,
            TargetRowsForSourceProfile = target?.TargetRowsForSourceProfile ?? 0,
            TargetExactIdentityMatches = target?.TargetExactIdentityMatches ?? 0,
            TargetMaDkConflictsOtherProfiles = target?.TargetMaDkConflictsOtherProfiles ?? 0,
            SoftDeletedIdentityConflicts = target?.SoftDeletedIdentityConflicts ?? 0,
            SourceProfileConstraintExists = target?.SourceProfileConstraintExists ?? false,
            SourceProfileAllowedByConstraint = target?.SourceProfileAllowedByConstraint ?? false,
            Blockers = context.Plan.Blockers,
            Warnings = context.Plan.Warnings,
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
            var sourceIdentity = CreateSourceIdentity(normalized.SourceProfileCode);
            var models = context.SourceRows
                .Select(row => HocVienSyncMapper.MapAndValidate(row, sourceIdentity))
                .Where(result => !result.ShouldSkip && result.Model is not null)
                .Select(result => result.Model!)
                .ToArray();

            // One repository call keeps the complete import inside the target repository's
            // single SqlBulkCopy + MERGE transaction. This prevents partially committed imports.
            var guardedWrite = models.Length == 0
                ? QlhvImportGuardedUpsertResult.Empty
                : await _writeRepository.UpsertWithGuardsAsync(models, cancellationToken);
            if (guardedWrite.HasConflicts)
            {
                var blockedPlan = context.Plan;
                if (guardedWrite.TargetMaDkConflictsOtherProfiles > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Target co {guardedWrite.TargetMaDkConflictsOtherProfiles} MaDK trung voi profile khac tai thoi diem ghi.");
                }

                if (guardedWrite.SoftDeletedIdentityConflicts > 0)
                {
                    blockedPlan = AddBlocker(
                        blockedPlan,
                        $"Target co {guardedWrite.SoftDeletedIdentityConflicts} identity da xoa mem tai thoi diem ghi.");
                }

                return Blocked(
                    blockedPlan,
                    "Import bi chan boi kiem tra conflict trong giao dich ghi.");
            }

            var counts = guardedWrite.Counts;

            return new QlhvImportExecuteResultDto
            {
                Executed = true,
                Status = "ThanhCong",
                Message = "Import hoc vien vao QLHV_APP hoan tat.",
                Plan = context.Plan,
                InsertedHocVienRows = counts.Inserted,
                UpdatedHocVienRows = counts.Updated,
                SkippedHocVienRows = counts.Skipped,
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
                "Import hoc vien vao QLHV_APP that bai.");
        }
    }

    private async Task<PlanContext> BuildPlanContextAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken)
    {
        var blockers = Validate(request).ToList();
        if (blockers.Count > 0)
        {
            return new PlanContext(CreateBasePlan(request, blockers), Array.Empty<V2HocVienSourceRow>(), null);
        }

        QlhvHocVienTargetDiagnosticsDto targetDiagnostics;
        try
        {
            targetDiagnostics = await _targetRepository.GetDiagnosticsAsync(cancellationToken);
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
            return new PlanContext(CreateBasePlan(request, blockers), Array.Empty<V2HocVienSourceRow>(), null);
        }

        if (blockers.Count > 0)
        {
            return new PlanContext(CreateBasePlan(request, blockers), Array.Empty<V2HocVienSourceRow>(), null);
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
            return new PlanContext(CreateBasePlan(request, blockers), Array.Empty<V2HocVienSourceRow>(), null);
        }
        catch (Exception ex)
        {
            blockers.Add($"Khong doc duoc nguon {request.SourceProfileCode}. Chi tiet: {ex.GetType().Name}.");
            return new PlanContext(CreateBasePlan(request, blockers), Array.Empty<V2HocVienSourceRow>(), null);
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
            blockers.Add($"Nguon co {duplicateSourceMaDks} MaDK bi trung trong pham vi import.");
        }

        var sourceMaDks = distinctSourceMaDks;

        QlhvImportTargetSnapshot target;
        try
        {
            target = await _readRepository.ReadTargetAsync(request, sourceMaDks, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QlhvImportReadException ex)
        {
            blockers.Add(ex.Message);
            var failedPlan = CreateBasePlan(request, blockers, source.HocVienRows.Count, source.KhoaHocRows);
            return new PlanContext(failedPlan, source.HocVienRows, null);
        }
        catch (Exception ex)
        {
            blockers.Add($"Khong doc duoc du lieu hien tai trong QLHV_APP. Chi tiet: {ex.GetType().Name}.");
            var failedPlan = CreateBasePlan(request, blockers, source.HocVienRows.Count, source.KhoaHocRows);
            return new PlanContext(failedPlan, source.HocVienRows, null);
        }

        if (target.SourceProfileConstraintExists && !target.SourceProfileAllowedByConstraint)
        {
            blockers.Add(
                $"CHECK constraint cua App_HocVien.SourceProfileCode hien khong cho phep {request.SourceProfileCode}.");
        }

        if (target.TargetMaDkConflictsOtherProfiles > 0)
        {
            blockers.Add(
                $"Target co {target.TargetMaDkConflictsOtherProfiles} MaDK trung voi profile khac.");
        }

        if (target.SoftDeletedIdentityConflicts > 0)
        {
            blockers.Add(
                $"Target co {target.SoftDeletedIdentityConflicts} identity da xoa mem trong pham vi import; task nay khong tu khoi phuc dong da xoa.");
        }

        var sourceIdentity = CreateSourceIdentity(request.SourceProfileCode);
        var hocVienPlan = HocVienSyncPlanner.BuildPlan(
            source.HocVienRows,
            target.ExistingHocVienHashes,
            sourceIdentity);
        var warnings = new List<string> { AppKhoaHocNotSupportedWarning };
        warnings.AddRange(hocVienPlan.Warnings
            .Select(warning => $"{warning.MaDK}: {warning.Message}")
            .Distinct(StringComparer.Ordinal)
            .Take(50));
        if (source.HocVienRows.Count == 0)
        {
            warnings.Add("Khong co hoc vien nguon khop bo loc; plan van hop le va khong ghi du lieu.");
        }

        var plan = new QlhvImportPlanDto
        {
            SourceProfileCode = request.SourceProfileCode,
            MaCSDT = request.MaCSDT,
            MaKhoaHoc = request.MaKhoaHoc,
            SourceHocVienRows = source.HocVienRows.Count,
            SourceKhoaHocRows = source.KhoaHocRows,
            CurrentAppHocVienRows = target.CurrentAppHocVienRows,
            CurrentAppKhoaHocRows = target.AppKhoaHocRows,
            PlannedInsertHocVienRows = hocVienPlan.PlannedInsert,
            PlannedUpdateHocVienRows = hocVienPlan.PlannedUpdate,
            PlannedSkipHocVienRows = hocVienPlan.PlannedSkip,
            PlannedUpsertHocVienRows = hocVienPlan.PlannedInsert + hocVienPlan.PlannedUpdate,
            PlannedUpsertKhoaHocRows = 0,
            Blockers = blockers,
            Warnings = warnings,
        };

        return new PlanContext(plan, source.HocVienRows, target);
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
        if (!SupportedProfiles.TryGetValue(request.SourceProfileCode, out var expectedMaCsdt))
        {
            blockers.Add("SourceProfileCode chi ho tro CSDT_MOTO hoac CSDT_OTO.");
        }

        if (string.IsNullOrWhiteSpace(request.MaCSDT))
        {
            blockers.Add("MaCSDT la bat buoc.");
        }
        else if (expectedMaCsdt is not null &&
                 !string.Equals(request.MaCSDT, expectedMaCsdt, StringComparison.Ordinal))
        {
            blockers.Add($"MaCSDT khong khop profile {request.SourceProfileCode}; gia tri mong doi la {expectedMaCsdt}.");
        }

        return blockers;
    }

    private static HocVienSourceIdentityContext CreateSourceIdentity(string sourceProfileCode)
        => new(sourceProfileCode, "V2");

    private static QlhvImportPlanDto CreateBasePlan(
        QlhvImportRequest request,
        IReadOnlyList<string> blockers,
        int sourceHocVienRows = 0,
        int sourceKhoaHocRows = 0)
        => new()
        {
            SourceProfileCode = request.SourceProfileCode,
            MaCSDT = request.MaCSDT,
            MaKhoaHoc = request.MaKhoaHoc,
            SourceHocVienRows = sourceHocVienRows,
            SourceKhoaHocRows = sourceKhoaHocRows,
            PlannedUpsertKhoaHocRows = 0,
            Blockers = blockers,
            Warnings = new[] { AppKhoaHocNotSupportedWarning },
        };

    private static QlhvImportPlanDto AddBlocker(QlhvImportPlanDto plan, string blocker)
        => new()
        {
            SourceProfileCode = plan.SourceProfileCode,
            MaCSDT = plan.MaCSDT,
            MaKhoaHoc = plan.MaKhoaHoc,
            SourceHocVienRows = plan.SourceHocVienRows,
            SourceKhoaHocRows = plan.SourceKhoaHocRows,
            CurrentAppHocVienRows = plan.CurrentAppHocVienRows,
            CurrentAppKhoaHocRows = plan.CurrentAppKhoaHocRows,
            PlannedInsertHocVienRows = plan.PlannedInsertHocVienRows,
            PlannedUpdateHocVienRows = plan.PlannedUpdateHocVienRows,
            PlannedSkipHocVienRows = plan.PlannedSkipHocVienRows,
            PlannedUpsertHocVienRows = plan.PlannedUpsertHocVienRows,
            PlannedUpsertKhoaHocRows = plan.PlannedUpsertKhoaHocRows,
            Blockers = plan.Blockers.Concat(new[] { blocker }).ToArray(),
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

    private sealed record PlanContext(
        QlhvImportPlanDto Plan,
        IReadOnlyList<V2HocVienSourceRow> SourceRows,
        QlhvImportTargetSnapshot? Target);
}
