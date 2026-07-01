using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed class MotoSyncService : IMotoSyncService
{
    public const string ConfirmationText = "SYNC TEST DATABASE";
    public const string UpdateConfirmationText = "SYNC TEST DATABASE UPDATE";

    private const string CsdtV1 = "CSDT_V1";
    private const string CsdtV2 = "CSDT_V2";

    private readonly IMotoSyncRepository _repository;

    public MotoSyncService(IMotoSyncRepository repository)
    {
        _repository = repository;
    }

    public async Task<MotoSyncPlanDto> GetPlanAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new MotoSyncPlanRequest();
        var normalized = Normalize(request);
        var validationBlockers = ValidateTestProfiles(normalized);
        if (validationBlockers.Count > 0)
        {
            return BlockedPlan(normalized, validationBlockers);
        }

        try
        {
            return await _repository.BuildPlanAsync(normalized, cancellationToken);
        }
        catch (Exception ex)
        {
            return BlockedPlan(
                normalized,
                new[] { "Khong tao duoc plan Moto sync read-only." },
                new[]
                {
                    new SyncErrorDto
                    {
                        Code = "MOTO_SYNC_PLAN_FAILED",
                        Message = $"Khong tao duoc plan Moto sync read-only. Chi tiet: {ex.GetType().Name}.",
                    },
                });
        }
    }

    public async Task<MotoSyncExecuteResultDto> ExecuteTestAsync(
        MotoSyncTestExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new MotoSyncTestExecuteRequest();
        var syncMode = request.SyncMode;
        var planRequest = Normalize(new MotoSyncPlanRequest
        {
            Direction = request.Direction,
            SourceProfileCode = request.SourceProfileCode,
            TargetProfileCode = request.TargetProfileCode,
            MaKhoaHoc = request.MaKhoaHoc,
            AllowDirtyData = false,
        });

        if (syncMode is not MotoSyncMode.INSERT_ONLY and not MotoSyncMode.INSERT_AND_UPDATE)
        {
            return BlockedExecute(
                "SyncMode khong hop le. Chi ho tro INSERT_ONLY hoac INSERT_AND_UPDATE.",
                BlockedPlan(planRequest, new[] { "SyncMode khong hop le." }));
        }

        var requiredConfirmText = syncMode == MotoSyncMode.INSERT_AND_UPDATE
            ? UpdateConfirmationText
            : ConfirmationText;
        if (!string.Equals(request.ConfirmText, requiredConfirmText, StringComparison.Ordinal))
        {
            return BlockedExecute(
                $"Thieu chuoi xac nhan chinh xac: {requiredConfirmText}.",
                BlockedPlan(planRequest, new[] { "ConfirmText khong khop." }));
        }

        var beforePlan = await GetPlanAsync(planRequest, cancellationToken);
        if (!beforePlan.Executable || beforePlan.Blockers.Count > 0 || beforePlan.Errors.Count > 0)
        {
            return BlockedExecute("Sync test bi chan vi plan co blocker.", beforePlan);
        }

        try
        {
            var summary = syncMode == MotoSyncMode.INSERT_AND_UPDATE
                ? await _repository.ExecuteInsertAndUpdateAsync(planRequest, cancellationToken)
                : await _repository.ExecuteInsertOnlyAsync(planRequest, cancellationToken);
            var afterPlan = await GetPlanAsync(planRequest, cancellationToken);
            return new MotoSyncExecuteResultDto
            {
                Executed = true,
                Status = "ThanhCong",
                Message = syncMode == MotoSyncMode.INSERT_AND_UPDATE
                    ? "Moto sync TEST insert-and-update hoan tat."
                    : "Moto sync TEST insert-only hoan tat.",
                Summary = summary,
                Plan = beforePlan,
                BeforePlan = beforePlan,
                AfterPlan = afterPlan,
                HasRemainingWork = HasRemainingWork(afterPlan),
            };
        }
        catch (Exception ex)
        {
            return new MotoSyncExecuteResultDto
            {
                Executed = true,
                Status = "Loi",
                Message = $"Moto sync TEST that bai va da rollback transaction. Chi tiet: {ex.GetType().Name}.",
                Plan = beforePlan,
                BeforePlan = beforePlan,
            };
        }
    }

    private static MotoSyncPlanRequest Normalize(MotoSyncPlanRequest request)
    {
        var source = NormalizeProfile(request.SourceProfileCode);
        var target = NormalizeProfile(request.TargetProfileCode);

        return new MotoSyncPlanRequest
        {
            Direction = request.Direction,
            SourceProfileCode = source,
            TargetProfileCode = target,
            MaKhoaHoc = string.IsNullOrWhiteSpace(request.MaKhoaHoc) ? null : request.MaKhoaHoc.Trim(),
            AllowDirtyData = request.AllowDirtyData,
        };
    }

    private static IReadOnlyList<string> ValidateTestProfiles(MotoSyncPlanRequest request)
    {
        var blockers = new List<string>();
        if (request.Direction is not MotoSyncDirection.V1_TO_V2 and not MotoSyncDirection.V2_TO_V1)
        {
            blockers.Add("Direction khong hop le. Chi ho tro V1_TO_V2 hoac V2_TO_V1.");
        }

        if (!IsAllowedTestProfile(request.SourceProfileCode) || !IsAllowedTestProfile(request.TargetProfileCode))
        {
            blockers.Add("Chi cho phep profile TEST CSDT_V1 va CSDT_V2 trong task nay.");
        }

        if (string.Equals(request.SourceProfileCode, request.TargetProfileCode, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("SourceProfileCode va TargetProfileCode phai khac nhau.");
        }

        var expectedSource = request.Direction == MotoSyncDirection.V1_TO_V2 ? CsdtV1 : CsdtV2;
        var expectedTarget = request.Direction == MotoSyncDirection.V1_TO_V2 ? CsdtV2 : CsdtV1;
        if (!string.Equals(request.SourceProfileCode, expectedSource, StringComparison.Ordinal) ||
            !string.Equals(request.TargetProfileCode, expectedTarget, StringComparison.Ordinal))
        {
            blockers.Add($"Profile khong khop direction {request.Direction}. Source phai la {expectedSource}, target phai la {expectedTarget}.");
        }

        return blockers;
    }

    private static bool IsAllowedTestProfile(string value)
        => string.Equals(value, CsdtV1, StringComparison.Ordinal) ||
           string.Equals(value, CsdtV2, StringComparison.Ordinal);

    private static string NormalizeProfile(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static MotoSyncPlanDto BlockedPlan(
        MotoSyncPlanRequest request,
        IReadOnlyList<string> blockers,
        IReadOnlyList<SyncErrorDto>? errors = null) => new()
    {
        Direction = request.Direction,
        SourceProfileCode = request.SourceProfileCode,
        TargetProfileCode = request.TargetProfileCode,
        MaKhoaHoc = request.MaKhoaHoc,
        AllowDirtyData = request.AllowDirtyData,
        Executable = false,
        Blockers = blockers,
        Errors = errors ?? Array.Empty<SyncErrorDto>(),
    };

    private static MotoSyncExecuteResultDto BlockedExecute(string message, MotoSyncPlanDto plan) => new()
    {
        Executed = false,
        Status = "BiChan",
        Message = message,
        Plan = plan,
        BeforePlan = plan,
    };

    private static bool HasRemainingWork(MotoSyncPlanDto afterPlan)
        => afterPlan.PlannedInsertKhoaHoc > 0 ||
           afterPlan.PlannedInsertBaoCaoI > 0 ||
           afterPlan.PlannedInsertNguoiLX > 0 ||
           afterPlan.PlannedInsertNguoiLXGPLX > 0 ||
           afterPlan.PlannedInsertNguoiLXHoSo > 0 ||
           afterPlan.PlannedInsertGiayTo > 0 ||
           afterPlan.PlannedUpdate > 0 ||
           afterPlan.PlannedUpdateNguoiLX > 0 ||
           afterPlan.PlannedUpdateNguoiLXHoSo > 0 ||
           afterPlan.Blockers.Count > 0 ||
           afterPlan.Errors.Count > 0;
}
