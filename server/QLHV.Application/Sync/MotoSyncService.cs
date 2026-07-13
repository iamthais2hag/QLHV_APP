using QLHV.Application.Sync.Dtos;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QLHV.Application.Sync;

public sealed class MotoSyncService : IMotoSyncService
{
    public const string ConfirmationText = "SYNC TEST DATABASE";
    public const string UpdateConfirmationText = "SYNC TEST DATABASE UPDATE";
    public const string CenterTransferConfirmationText = "CHUYEN MA CSDT TEST";

    private const string CsdtV1 = "CSDT_V1";
    private const string CsdtV2 = "CSDT_V2";

    private static readonly JsonSerializerOptions HistoryJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IMotoSyncRepository _repository;
    private readonly IMotoSyncRunHistoryRepository? _runHistory;
    private readonly IMotoCenterTransferRunHistoryRepository? _centerTransferRunHistory;

    public MotoSyncService(
        IMotoSyncRepository repository,
        IMotoSyncRunHistoryRepository? runHistory = null,
        IMotoCenterTransferRunHistoryRepository? centerTransferRunHistory = null)
    {
        _repository = repository;
        _runHistory = runHistory;
        _centerTransferRunHistory = centerTransferRunHistory;
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

    public Task<IReadOnlyList<MotoSyncKhoaHocOptionDto>> GetKhoaHocOptionsAsync(
        MotoSyncKhoaHocOptionsQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new MotoSyncKhoaHocOptionsQuery();
        var normalized = Normalize(query);
        var validationBlockers = ValidateTestProfiles(new MotoSyncPlanRequest
        {
            Direction = normalized.Direction,
            SourceProfileCode = normalized.SourceProfileCode,
            TargetProfileCode = normalized.TargetProfileCode,
        });
        if (validationBlockers.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", validationBlockers));
        }

        return _repository.GetKhoaHocOptionsAsync(normalized, cancellationToken);
    }

    public Task<MotoTargetDonViGTVTOptionsResultDto> GetTargetDonViGTVTOptionsAsync(
        MotoTargetDonViGTVTOptionsQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new MotoTargetDonViGTVTOptionsQuery();
        var normalized = Normalize(query);
        if (!string.Equals(normalized.TargetProfileCode, CsdtV2, StringComparison.Ordinal))
        {
            throw new ArgumentException("Chi cho phep tim DM_DonViGTVT tren target TEST CSDT_V2 cho chuc nang chuyen MaCSDT.");
        }

        return _repository.GetTargetDonViGTVTOptionsAsync(normalized, cancellationToken);
    }

    public async Task<MotoCenterTransferPlanDto> GetCenterTransferPlanAsync(
        MotoCenterTransferPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new MotoCenterTransferPlanRequest();
        var normalized = Normalize(request);
        var blockers = ValidateCenterTransferRequest(normalized).ToList();
        if (blockers.Count > 0)
        {
            return BlockedCenterTransferPlan(normalized, blockers);
        }

        return await _repository.BuildCenterTransferPlanAsync(normalized, cancellationToken);
    }

    public async Task<MotoCenterTransferExecuteResultDto> ExecuteCenterTransferTestAsync(
        MotoCenterTransferTestRequest request,
        CancellationToken cancellationToken = default)
    {
        var attemptStartedAt = DateTime.UtcNow;
        request ??= new MotoCenterTransferTestRequest();
        var normalized = Normalize(request);
        if (!string.Equals(request.ConfirmText, CenterTransferConfirmationText, StringComparison.Ordinal))
        {
            var result = new MotoCenterTransferExecuteResultDto
            {
                Executed = false,
                Status = "BiChan",
                Message = $"Thieu chuoi xac nhan chinh xac: {CenterTransferConfirmationText}.",
                Plan = BlockedCenterTransferPlan(normalized, new[] { "ConfirmText khong khop." }),
            };
            return await WriteCenterTransferRunHistoryAndReturnAsync(
                normalized,
                confirmTextMatched: false,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }

        var plan = await GetCenterTransferPlanAsync(normalized, cancellationToken);
        if (!plan.Executable || plan.Blockers.Count > 0)
        {
            var result = new MotoCenterTransferExecuteResultDto
            {
                Executed = false,
                Status = "BiChan",
                Message = "Chuyen MaCSDT TEST bi chan vi plan co blocker.",
                Plan = plan,
            };
            return await WriteCenterTransferRunHistoryAndReturnAsync(
                normalized,
                confirmTextMatched: true,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }

        try
        {
            var summary = await _repository.ExecuteCenterTransferAsync(normalized, cancellationToken);
            var result = new MotoCenterTransferExecuteResultDto
            {
                Executed = true,
                Status = "ThanhCong",
                Message = "Chuyen MaCSDT TEST hoan tat.",
                Plan = plan,
                Summary = summary,
            };
            return await WriteCenterTransferRunHistoryAndReturnAsync(
                normalized,
                confirmTextMatched: true,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var result = new MotoCenterTransferExecuteResultDto
            {
                Executed = false,
                Status = "Loi",
                Message = $"Chuyen MaCSDT TEST that bai va da rollback transaction. Chi tiet: {ex.GetType().Name}.",
                Plan = plan,
            };
            return await WriteCenterTransferRunHistoryAndReturnAsync(
                normalized,
                confirmTextMatched: true,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }
    }

    public async Task<MotoSyncExecuteResultDto> ExecuteTestAsync(
        MotoSyncTestExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        var attemptStartedAt = DateTime.UtcNow;
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
            var result = BlockedExecute(
                "SyncMode khong hop le. Chi ho tro INSERT_ONLY hoac INSERT_AND_UPDATE.",
                BlockedPlan(planRequest, new[] { "SyncMode khong hop le." }));
            return await WriteRunHistoryAndReturnAsync(
                planRequest,
                syncMode,
                confirmTextMatched: false,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }

        var requiredConfirmText = syncMode == MotoSyncMode.INSERT_AND_UPDATE
            ? UpdateConfirmationText
            : ConfirmationText;
        var confirmTextMatched = string.Equals(request.ConfirmText, requiredConfirmText, StringComparison.Ordinal);
        if (!confirmTextMatched)
        {
            var result = BlockedExecute(
                $"Thieu chuoi xac nhan chinh xac: {requiredConfirmText}.",
                BlockedPlan(planRequest, new[] { "ConfirmText khong khop." }));
            return await WriteRunHistoryAndReturnAsync(
                planRequest,
                syncMode,
                confirmTextMatched,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }

        var beforePlan = await GetPlanAsync(planRequest, cancellationToken);
        if (!beforePlan.Executable || beforePlan.Blockers.Count > 0 || beforePlan.Errors.Count > 0)
        {
            var result = BlockedExecute("Sync test bi chan vi plan co blocker.", beforePlan);
            return await WriteRunHistoryAndReturnAsync(
                planRequest,
                syncMode,
                confirmTextMatched,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }

        try
        {
            var summary = syncMode == MotoSyncMode.INSERT_AND_UPDATE
                ? await _repository.ExecuteInsertAndUpdateAsync(planRequest, cancellationToken)
                : await _repository.ExecuteInsertOnlyAsync(planRequest, cancellationToken);
            var afterPlan = await GetPlanAsync(planRequest, cancellationToken);
            var result = new MotoSyncExecuteResultDto
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

            return await WriteRunHistoryAndReturnAsync(
                planRequest,
                syncMode,
                confirmTextMatched,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (Exception ex)
        {
            var result = new MotoSyncExecuteResultDto
            {
                Executed = true,
                Status = "Loi",
                Message = $"Moto sync TEST that bai va da rollback transaction. Chi tiet: {ex.GetType().Name}.",
                Plan = beforePlan,
                BeforePlan = beforePlan,
            };
            return await WriteRunHistoryAndReturnAsync(
                planRequest,
                syncMode,
                confirmTextMatched,
                result,
                attemptStartedAt,
                DateTime.UtcNow,
                cancellationToken);
        }
    }

    public Task<IReadOnlyList<MotoSyncRunHistoryListItemDto>> GetRunHistoryAsync(
        MotoSyncRunHistoryQuery query,
        CancellationToken cancellationToken = default)
        => _runHistory is null
            ? Task.FromResult<IReadOnlyList<MotoSyncRunHistoryListItemDto>>(Array.Empty<MotoSyncRunHistoryListItemDto>())
            : _runHistory.SearchAsync(query ?? new MotoSyncRunHistoryQuery(), cancellationToken);

    public Task<MotoSyncRunHistoryDetailDto?> GetRunHistoryDetailAsync(
        long id,
        CancellationToken cancellationToken = default)
        => _runHistory is null
            ? Task.FromResult<MotoSyncRunHistoryDetailDto?>(null)
            : _runHistory.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<MotoCenterTransferRunHistoryListItemDto>> GetCenterTransferRunHistoryAsync(
        MotoCenterTransferRunHistoryQuery query,
        CancellationToken cancellationToken = default)
        => _centerTransferRunHistory is null
            ? Task.FromResult<IReadOnlyList<MotoCenterTransferRunHistoryListItemDto>>(Array.Empty<MotoCenterTransferRunHistoryListItemDto>())
            : _centerTransferRunHistory.SearchAsync(query ?? new MotoCenterTransferRunHistoryQuery(), cancellationToken);

    public Task<MotoCenterTransferRunHistoryDetailDto?> GetCenterTransferRunHistoryDetailAsync(
        long id,
        CancellationToken cancellationToken = default)
        => _centerTransferRunHistory is null
            ? Task.FromResult<MotoCenterTransferRunHistoryDetailDto?>(null)
            : _centerTransferRunHistory.GetByIdAsync(id, cancellationToken);

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

    private static MotoSyncKhoaHocOptionsQuery Normalize(MotoSyncKhoaHocOptionsQuery query)
    {
        var source = NormalizeProfile(query.SourceProfileCode);
        var target = NormalizeProfile(query.TargetProfileCode);

        return new MotoSyncKhoaHocOptionsQuery
        {
            Direction = query.Direction,
            SourceProfileCode = source,
            TargetProfileCode = target,
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Take = Math.Clamp(query.Take <= 0 ? 50 : query.Take, 1, 200),
        };
    }

    private static MotoTargetDonViGTVTOptionsQuery Normalize(MotoTargetDonViGTVTOptionsQuery query)
        => new()
        {
            TargetProfileCode = NormalizeProfile(string.IsNullOrWhiteSpace(query.TargetProfileCode) ? CsdtV2 : query.TargetProfileCode),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Take = Math.Clamp(query.Take <= 0 ? 20 : query.Take, 1, 100),
        };

    private static MotoCenterTransferPlanRequest Normalize(MotoCenterTransferPlanRequest request)
    {
        var source = NormalizeProfile(string.IsNullOrWhiteSpace(request.SourceProfileCode) ? CsdtV1 : request.SourceProfileCode);
        var target = NormalizeProfile(string.IsNullOrWhiteSpace(request.TargetProfileCode) ? CsdtV2 : request.TargetProfileCode);

        return new MotoCenterTransferPlanRequest
        {
            SourceProfileCode = source,
            TargetProfileCode = target,
            MaKhoaHocCu = string.IsNullOrWhiteSpace(request.MaKhoaHocCu) ? null : request.MaKhoaHocCu.Trim(),
            MaCSDTCu = string.IsNullOrWhiteSpace(request.MaCSDTCu) ? null : request.MaCSDTCu.Trim(),
            MaCSDTMoi = string.IsNullOrWhiteSpace(request.MaCSDTMoi) ? null : request.MaCSDTMoi.Trim(),
            MaSoGTVTMoi = string.IsNullOrWhiteSpace(request.MaSoGTVTMoi) ? null : request.MaSoGTVTMoi.Trim(),
        };
    }

    private static MotoCenterTransferPlanRequest Normalize(MotoCenterTransferTestRequest request)
        => Normalize((MotoCenterTransferPlanRequest)request);

    private static IReadOnlyList<string> ValidateCenterTransferRequest(MotoCenterTransferPlanRequest request)
    {
        var blockers = new List<string>();
        if (!string.Equals(request.SourceProfileCode, CsdtV1, StringComparison.Ordinal) ||
            !string.Equals(request.TargetProfileCode, CsdtV2, StringComparison.Ordinal))
        {
            blockers.Add("Chi cho phep chuyen MaCSDT TEST tu CSDT_V1 sang CSDT_V2 trong task nay.");
        }

        if (string.IsNullOrWhiteSpace(request.MaKhoaHocCu))
        {
            blockers.Add("MaKhoaHocCu la bat buoc.");
        }

        if (string.IsNullOrWhiteSpace(request.MaCSDTCu))
        {
            blockers.Add("MaCSDTCu la bat buoc.");
        }

        if (string.IsNullOrWhiteSpace(request.MaCSDTMoi))
        {
            blockers.Add("MaCSDTMoi la bat buoc.");
        }

        if (string.IsNullOrWhiteSpace(request.MaSoGTVTMoi))
        {
            blockers.Add("MaSoGTVTMoi la bat buoc.");
        }

        return blockers;
    }

    private static MotoCenterTransferPlanDto BlockedCenterTransferPlan(
        MotoCenterTransferPlanRequest request,
        IReadOnlyList<string> blockers) => new()
    {
        SourceProfileCode = request.SourceProfileCode,
        TargetProfileCode = request.TargetProfileCode,
        MaKhoaHocCu = request.MaKhoaHocCu ?? string.Empty,
        MaKhoaHocMoi = ComputeMaKhoaHocMoi(request.MaKhoaHocCu, request.MaCSDTCu, request.MaCSDTMoi),
        MaCSDTCu = request.MaCSDTCu ?? string.Empty,
        MaCSDTMoi = request.MaCSDTMoi ?? string.Empty,
        MaSoGTVTMoi = request.MaSoGTVTMoi ?? string.Empty,
        Executable = false,
        Blockers = blockers,
    };

    private static string ComputeMaKhoaHocMoi(string? maKhoaHocCu, string? maCsdtCu, string? maCsdtMoi)
        => string.IsNullOrWhiteSpace(maKhoaHocCu) ||
           string.IsNullOrWhiteSpace(maCsdtCu) ||
           string.IsNullOrWhiteSpace(maCsdtMoi)
            ? string.Empty
            : maKhoaHocCu.Replace(maCsdtCu, maCsdtMoi, StringComparison.OrdinalIgnoreCase);

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

    private async Task<MotoSyncExecuteResultDto> WriteRunHistoryAndReturnAsync(
        MotoSyncPlanRequest request,
        MotoSyncMode syncMode,
        bool confirmTextMatched,
        MotoSyncExecuteResultDto result,
        DateTime attemptStartedAt,
        DateTime attemptEndedAt,
        CancellationToken cancellationToken)
    {
        if (_runHistory is null)
        {
            return result;
        }

        try
        {
            await _runHistory.CreateAsync(
                BuildRunHistoryEntry(
                    request,
                    syncMode,
                    confirmTextMatched,
                    result,
                    attemptStartedAt,
                    attemptEndedAt),
                cancellationToken);
            return result;
        }
        catch
        {
            return CopyWithMessage(
                result,
                $"{result.Message} Canh bao: khong ghi duoc lich su dong bo Moto TEST.");
        }
    }

    private static MotoSyncRunHistoryCreateDto BuildRunHistoryEntry(
        MotoSyncPlanRequest request,
        MotoSyncMode syncMode,
        bool confirmTextMatched,
        MotoSyncExecuteResultDto result,
        DateTime attemptStartedAt,
        DateTime attemptEndedAt)
    {
        var summary = result.Summary;
        var startedAt = summary is null || summary.StartedAt == default
            ? attemptStartedAt
            : summary.StartedAt;
        var endedAt = summary is null || summary.EndedAt == default
            ? attemptEndedAt
            : summary.EndedAt;
        var durationMs = summary is null
            ? (long)(endedAt - startedAt).TotalMilliseconds
            : summary.DurationMs;

        return new MotoSyncRunHistoryCreateDto
        {
            Direction = request.Direction,
            SyncMode = syncMode,
            SourceProfileCode = request.SourceProfileCode,
            TargetProfileCode = request.TargetProfileCode,
            MaKhoaHoc = request.MaKhoaHoc,
            ConfirmTextMatched = confirmTextMatched,
            Executed = result.Executed,
            Status = result.Status,
            Message = result.Message,
            InsertedKhoaHoc = summary?.InsertedKhoaHoc ?? 0,
            InsertedBaoCaoI = summary?.InsertedBaoCaoI ?? 0,
            InsertedNguoiLX = summary?.InsertedNguoiLX ?? 0,
            InsertedNguoiLXGPLX = summary?.InsertedNguoiLXGPLX ?? 0,
            InsertedNguoiLXHoSo = summary?.InsertedNguoiLXHoSo ?? 0,
            InsertedGiayTo = summary?.InsertedGiayTo ?? 0,
            UpdatedNguoiLX = summary?.UpdatedNguoiLX ?? 0,
            UpdatedNguoiLXHoSo = summary?.UpdatedNguoiLXHoSo ?? 0,
            UpdatedRows = summary?.UpdatedRows ?? 0,
            DeletedRows = summary?.DeletedRows ?? 0,
            DurationMs = Math.Max(0, durationMs),
            StartedAt = startedAt,
            EndedAt = endedAt,
            HasRemainingWork = result.HasRemainingWork,
            BeforePlanJson = SerializePlan(result.BeforePlan ?? result.Plan),
            AfterPlanJson = result.AfterPlan is null ? null : SerializePlan(result.AfterPlan),
        };
    }

    private static string? SerializePlan(MotoSyncPlanDto? plan)
        => plan is null ? null : JsonSerializer.Serialize(plan, HistoryJsonOptions);

    private async Task<MotoCenterTransferExecuteResultDto> WriteCenterTransferRunHistoryAndReturnAsync(
        MotoCenterTransferPlanRequest request,
        bool confirmTextMatched,
        MotoCenterTransferExecuteResultDto result,
        DateTime attemptStartedAt,
        DateTime attemptEndedAt,
        CancellationToken cancellationToken)
    {
        if (_centerTransferRunHistory is null)
        {
            return result;
        }

        try
        {
            await _centerTransferRunHistory.CreateAsync(
                BuildCenterTransferRunHistoryEntry(
                    request,
                    confirmTextMatched,
                    result,
                    attemptStartedAt,
                    attemptEndedAt),
                cancellationToken);
            return result;
        }
        catch
        {
            return CopyWithCenterTransferMessage(
                result,
                $"{result.Message} Canh bao: khong ghi duoc lich su chuyen MaCSDT Moto TEST.");
        }
    }

    private static MotoCenterTransferRunHistoryCreateDto BuildCenterTransferRunHistoryEntry(
        MotoCenterTransferPlanRequest request,
        bool confirmTextMatched,
        MotoCenterTransferExecuteResultDto result,
        DateTime attemptStartedAt,
        DateTime attemptEndedAt)
    {
        var summary = result.Summary;
        var plan = result.Plan;
        var startedAt = summary is null || summary.StartedAt == default
            ? attemptStartedAt
            : summary.StartedAt;
        var endedAt = summary is null || summary.EndedAt == default
            ? attemptEndedAt
            : summary.EndedAt;
        var durationMs = summary is null
            ? (long)(endedAt - startedAt).TotalMilliseconds
            : summary.DurationMs;

        return new MotoCenterTransferRunHistoryCreateDto
        {
            SourceProfileCode = request.SourceProfileCode,
            TargetProfileCode = request.TargetProfileCode,
            MaKhoaHocCu = request.MaKhoaHocCu ?? string.Empty,
            MaKhoaHocMoi = summary?.MaKhoaHocMoi ?? plan?.MaKhoaHocMoi ?? ComputeMaKhoaHocMoi(request.MaKhoaHocCu, request.MaCSDTCu, request.MaCSDTMoi),
            MaCSDTCu = request.MaCSDTCu ?? string.Empty,
            MaCSDTMoi = request.MaCSDTMoi ?? string.Empty,
            MaSoGTVTMoi = request.MaSoGTVTMoi,
            ConfirmTextMatched = confirmTextMatched,
            Executed = result.Executed,
            Status = result.Status,
            Message = result.Message,
            CopiedKhoaHoc = summary?.CopiedKhoaHoc ?? 0,
            CopiedBaoCaoI = summary?.CopiedBaoCaoI ?? 0,
            CopiedNguoiLX = summary?.CopiedNguoiLX ?? 0,
            CopiedNguoiLXHoSo = summary?.CopiedNguoiLXHoSo ?? 0,
            CopiedNguoiLXHSGiayTo = summary?.CopiedNguoiLXHSGiayTo ?? 0,
            UpdatedNguoiLXHoSo = summary?.UpdatedNguoiLXHoSo ?? 0,
            UpdatedNguoiLX = summary?.UpdatedNguoiLX ?? 0,
            UpdatedKhoaHoc = summary?.UpdatedKhoaHoc ?? 0,
            UpdatedBaoCaoI = summary?.UpdatedBaoCaoI ?? 0,
            UpdatedNguoiLXHSGiayTo = summary?.UpdatedNguoiLXHSGiayTo ?? summary?.UpdatedGiayTo ?? 0,
            TargetKhoaHocMoiCountAfter = summary?.TargetKhoaHocMoiCountAfter,
            TargetBaoCaoIMoiCountAfter = summary?.TargetBaoCaoIMoiCountAfter,
            TargetNguoiLXHoSoMoiCountAfter = summary?.TargetNguoiLXHoSoMoiCountAfter,
            TargetNguoiLXHSGiayToMoiCountAfter = summary?.TargetNguoiLXHSGiayToMoiCountAfter,
            TargetNguoiLXMoiCountAfter = summary?.TargetNguoiLXMoiCountAfter,
            DurationMs = Math.Max(0, durationMs),
            StartedAt = startedAt,
            EndedAt = endedAt,
            PlanJson = SerializeCenterTransferPlan(plan),
            SummaryJson = SerializeCenterTransferSummary(summary),
        };
    }

    private static string? SerializeCenterTransferPlan(MotoCenterTransferPlanDto? plan)
        => plan is null ? null : JsonSerializer.Serialize(plan, HistoryJsonOptions);

    private static string? SerializeCenterTransferSummary(MotoCenterTransferSummaryDto? summary)
        => summary is null ? null : JsonSerializer.Serialize(summary, HistoryJsonOptions);

    private static MotoCenterTransferExecuteResultDto CopyWithCenterTransferMessage(
        MotoCenterTransferExecuteResultDto result,
        string message) => new()
    {
        Executed = result.Executed,
        Status = result.Status,
        Message = message,
        Plan = result.Plan,
        Summary = result.Summary,
    };

    private static MotoSyncExecuteResultDto CopyWithMessage(
        MotoSyncExecuteResultDto result,
        string message) => new()
    {
        Executed = result.Executed,
        Status = result.Status,
        Message = message,
        Summary = result.Summary,
        Plan = result.Plan,
        BeforePlan = result.BeforePlan,
        AfterPlan = result.AfterPlan,
        HasRemainingWork = result.HasRemainingWork,
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
