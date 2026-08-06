using Microsoft.Extensions.Options;
using QLHV.Application.Auth;

namespace QLHV.Application.Sync.Realtime;

public sealed class CsdtRealtimeService : ICsdtRealtimeService
{
    private const string InvalidRouteBlocker =
        "Cau hinh stream trong state khong khop allowlist server.";
    private const string MissingStateBlocker =
        "Realtime state chua san sang; can ap dung patch QLHV_APP.";
    private const string GlobalDisabledBlocker =
        "CsdtRealtimeSync dang bi tat trong cau hinh may chu.";

    private readonly ICsdtRealtimeStateRepository _stateRepository;
    private readonly ICsdtRealtimeCommandRepository _commandRepository;
    private readonly ICsdtReversePlanRepository _reversePlanRepository;
    private readonly CsdtRealtimeSyncOptions _options;
    private readonly IReadOnlyDictionary<string, CsdtRealtimeRouteDefinition> _routesByStream;

    public CsdtRealtimeService(
        ICsdtRealtimeStateRepository stateRepository,
        ICsdtRealtimeCommandRepository commandRepository,
        ICsdtReversePlanRepository reversePlanRepository,
        IOptions<CsdtRealtimeSyncOptions> options)
    {
        _stateRepository = stateRepository;
        _commandRepository = commandRepository;
        _reversePlanRepository = reversePlanRepository;
        _options = options.Value;

        // Fail closed during startup/service construction if a host bypassed
        // Options validation or supplied a cross-vehicle profile pair.
        var validation = new CsdtRealtimeSyncOptionsValidator().Validate(
            Options.DefaultName,
            _options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(CsdtRealtimeSyncOptions),
                validation.Failures);
        }

        _routesByStream = CsdtRealtimeStreamCatalog
            .GetConfiguredRoutes(_options)
            .ToDictionary(route => route.StreamCode, StringComparer.Ordinal);
    }

    public async Task<CsdtRealtimeStreamsResponseDto> GetStreamsAsync(
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var stored = await _stateRepository.GetStreamsAsync(cancellationToken);
        var duplicateStream = stored
            .GroupBy(item => item.StreamCode, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateStream is not null)
        {
            throw new CsdtRealtimeStoreUnavailableException(
                $"Realtime state co nhieu dong cho stream {duplicateStream.Key}.");
        }

        var storedByStream = stored.ToDictionary(item => item.StreamCode, StringComparer.Ordinal);
        var response = new List<CsdtRealtimeStreamStatusDto>(_routesByStream.Count);
        foreach (var route in _routesByStream.Values.OrderBy(RouteOrder))
        {
            response.Add(storedByStream.TryGetValue(route.StreamCode, out var status)
                ? MapStatus(status, route, user)
                : MissingStatus(route, user));
        }

        return new CsdtRealtimeStreamsResponseDto
        {
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Streams = response,
        };
    }

    public Task<IReadOnlyList<CsdtRealtimeHistoryItemDto>> GetHistoryAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default)
    {
        _ = GetConfiguredRoute(streamCode);
        return _stateRepository.GetHistoryAsync(streamCode, NormalizeTake(take), cancellationToken);
    }

    public Task<IReadOnlyList<CsdtRealtimeTombstoneDto>> GetTombstonesAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default)
    {
        _ = GetConfiguredRoute(streamCode);
        return _stateRepository.GetTombstonesAsync(
            streamCode,
            NormalizeTake(take),
            cancellationToken);
    }

    public async Task<CsdtRealtimeActionResultDto> SetEnabledAsync(
        string streamCode,
        CsdtRealtimeEnableRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAdmin(user);
        CsdtRealtimeIdentityRules.RequireStateToken(
            request.ExpectedStateToken,
            nameof(request.ExpectedStateToken));
        var route = await RequireWritableConfiguredStateAsync(streamCode, cancellationToken);
        return await _commandRepository.EnqueueAsync(
            CreateCommand(
                route,
                CsdtRealtimeCommandTypes.SetEnabled,
                user,
                request.ExpectedStateToken) with
            {
                Enabled = request.Enabled,
            },
            cancellationToken);
    }

    public async Task<CsdtRealtimeActionResultDto> QueueBaselineAsync(
        string streamCode,
        CsdtRealtimeBaselineRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAdmin(user);
        CsdtRealtimeIdentityRules.RequireStateToken(
            request.ExpectedStateToken,
            nameof(request.ExpectedStateToken));
        var route = await RequireWritableConfiguredStateAsync(streamCode, cancellationToken);
        return await _commandRepository.EnqueueAsync(
            CreateCommand(
                route,
                CsdtRealtimeCommandTypes.Baseline,
                user,
                request.ExpectedStateToken),
            cancellationToken);
    }

    public async Task<CsdtRealtimeActionResultDto> QueueRetryAsync(
        string streamCode,
        CsdtRealtimeRetryRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAdmin(user);
        CsdtRealtimeIdentityRules.RequireStateToken(
            request.ExpectedStateToken,
            nameof(request.ExpectedStateToken));
        var route = await RequireWritableConfiguredStateAsync(streamCode, cancellationToken);
        return await _commandRepository.EnqueueAsync(
            CreateCommand(
                route,
                CsdtRealtimeCommandTypes.Retry,
                user,
                request.ExpectedStateToken),
            cancellationToken);
    }

    public async Task<CsdtReversePlanDto> GetReversePlanAsync(
        string vehicleType,
        string? maKhoaHoc,
        CancellationToken cancellationToken = default)
    {
        var route = GetConfiguredRouteByVehicle(vehicleType).Reverse();
        var rawCourseCode = ValidateOptionalCourseCode(maKhoaHoc, route.MaCSDT);
        var plan = await _reversePlanRepository.BuildPlanAsync(
            route,
            rawCourseCode,
            cancellationToken);
        return SecurePlan(plan, route, rawCourseCode);
    }

    public async Task<CsdtReverseExecuteResultDto> ExecuteReverseAsync(
        CsdtReverseExecuteRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAdmin(user);
        CsdtRealtimeIdentityRules.RequirePlanToken(
            request.ExpectedPlanToken,
            nameof(request.ExpectedPlanToken));

        var forwardRoute = GetConfiguredRouteByVehicle(request.VehicleType);
        await RequireWritableConfiguredStateAsync(forwardRoute.StreamCode, cancellationToken);
        var reverseRoute = forwardRoute.Reverse();
        var rawCourseCode = ValidateOptionalCourseCode(request.MaKhoaHoc, reverseRoute.MaCSDT);
        var currentPlan = SecurePlan(
            await _reversePlanRepository.BuildPlanAsync(
                reverseRoute,
                rawCourseCode,
                cancellationToken),
            reverseRoute,
            rawCourseCode);

        var planMatches = string.Equals(
            request.ExpectedPlanToken,
            currentPlan.PlanToken,
            StringComparison.Ordinal);
        if (!planMatches &&
            !await _commandRepository.HasRetryableReverseAsync(
                forwardRoute.StreamCode,
                rawCourseCode,
                request.ExpectedPlanToken,
                cancellationToken))
        {
            return RejectedReverse(
                CsdtRealtimeActionStatuses.Conflict,
                "Plan da thay doi; hay tai lai plan truoc khi thuc thi.",
                currentPlan);
        }

        if (!currentPlan.Executable)
        {
            return RejectedReverse(
                CsdtRealtimeActionStatuses.Rejected,
                "Plan con blocker hoac xung dot can xu ly.",
                currentPlan);
        }

        var result = await _commandRepository.EnqueueAsync(
            CreateCommand(
                reverseRoute,
                CsdtRealtimeCommandTypes.ReverseExecute,
                user,
                expectedStateToken: null) with
            {
                MaKhoaHoc = rawCourseCode,
                ExpectedPlanToken = request.ExpectedPlanToken,
            },
            cancellationToken);

        return new CsdtReverseExecuteResultDto
        {
            Accepted = result.Accepted,
            JoinedExisting = result.JoinedExisting,
            RunId = result.RunId,
            Status = result.Status,
            Message = result.Message,
            Plan = currentPlan,
        };
    }

    private async Task<CsdtRealtimeRouteDefinition> RequireWritableConfiguredStateAsync(
        string streamCode,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException(GlobalDisabledBlocker);
        }

        var route = GetConfiguredRoute(streamCode);
        var stored = await _stateRepository.GetStreamsAsync(cancellationToken);
        var matching = stored.Where(item =>
                string.Equals(item.StreamCode, streamCode, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matching.Length != 1)
        {
            throw new CsdtRealtimeStoreUnavailableException(
                matching.Length == 0
                    ? MissingStateBlocker
                    : $"Realtime state co nhieu dong cho stream {streamCode}.");
        }

        if (!StateMatchesRoute(matching[0], route))
        {
            throw new InvalidOperationException(InvalidRouteBlocker);
        }

        return route;
    }

    private CsdtRealtimeRouteDefinition GetConfiguredRoute(string streamCode)
        => _routesByStream.TryGetValue(streamCode, out var route)
            ? route
            : CsdtRealtimeStreamCatalog.GetLiveByStream(streamCode);

    private CsdtRealtimeRouteDefinition GetConfiguredRouteByVehicle(string vehicleType)
    {
        // GetLiveByVehicle performs the strict, case-sensitive request validation.
        var live = CsdtRealtimeStreamCatalog.GetLiveByVehicle(vehicleType);
        return GetConfiguredRoute(live.StreamCode);
    }

    private CsdtRealtimeStreamStatusDto MapStatus(
        CsdtRealtimeStreamStatusDto status,
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeUserContext user)
    {
        var routeValid = StateMatchesRoute(status, route);
        var blockers = status.ActionBlockers.ToList();
        if (!routeValid && !blockers.Contains(InvalidRouteBlocker, StringComparer.Ordinal))
        {
            blockers.Add(InvalidRouteBlocker);
        }

        if (!_options.Enabled && !blockers.Contains(GlobalDisabledBlocker, StringComparer.Ordinal))
        {
            blockers.Add(GlobalDisabledBlocker);
        }

        return status with
        {
            StreamCode = route.StreamCode,
            VehicleType = route.VehicleType,
            SourceProfileCode = route.SourceProfileCode,
            TargetProfileCode = route.TargetProfileCode,
            SourceDatabaseName = route.SourceDatabaseName,
            TargetDatabaseName = route.TargetDatabaseName,
            MaCSDT = route.MaCSDT,
            CurrentUserRole = user.Role,
            WriteAuthorized = user.WriteAuthorized &&
                              string.Equals(user.Role, AppRoles.Admin, StringComparison.Ordinal) &&
                              routeValid &&
                              _options.Enabled,
            ActionBlockers = blockers,
        };
    }

    private CsdtRealtimeStreamStatusDto MissingStatus(
        CsdtRealtimeRouteDefinition route,
        CsdtRealtimeUserContext user)
    {
        var blockers = new List<string> { MissingStateBlocker };
        if (!_options.Enabled)
        {
            blockers.Add(GlobalDisabledBlocker);
        }

        return new CsdtRealtimeStreamStatusDto
        {
            StreamCode = route.StreamCode,
            VehicleType = route.VehicleType,
            SourceProfileCode = route.SourceProfileCode,
            TargetProfileCode = route.TargetProfileCode,
            SourceDatabaseName = route.SourceDatabaseName,
            TargetDatabaseName = route.TargetDatabaseName,
            MaCSDT = route.MaCSDT,
            State = "NOT_CONFIGURED",
            BaselineStatus = "NOT_STARTED",
            CurrentUserRole = user.Role,
            WriteAuthorized = false,
            ActionBlockers = blockers,
        };
    }

    private static bool StateMatchesRoute(
        CsdtRealtimeStreamStatusDto state,
        CsdtRealtimeRouteDefinition route)
        => string.Equals(state.StreamCode, route.StreamCode, StringComparison.Ordinal) &&
           string.Equals(state.VehicleType, route.VehicleType, StringComparison.Ordinal) &&
           string.Equals(state.SourceProfileCode, route.SourceProfileCode, StringComparison.Ordinal) &&
           string.Equals(state.TargetProfileCode, route.TargetProfileCode, StringComparison.Ordinal) &&
           string.Equals(state.SourceDatabaseName, route.SourceDatabaseName, StringComparison.Ordinal) &&
           string.Equals(state.TargetDatabaseName, route.TargetDatabaseName, StringComparison.Ordinal) &&
           string.Equals(state.MaCSDT, route.MaCSDT, StringComparison.Ordinal);

    private static string? ValidateOptionalCourseCode(string? maKhoaHoc, string maCsdt)
    {
        if (maKhoaHoc is null || maKhoaHoc.Length == 0)
        {
            return null;
        }

        if (!CsdtRealtimeIdentityRules.IsRawCourseCodeOrStorableLegacy(maKhoaHoc, maCsdt))
        {
            throw new ArgumentException(
                "MaKhoaHoc khong hop le hoac da bi thay doi ky tu.",
                nameof(maKhoaHoc));
        }

        return maKhoaHoc;
    }

    private static CsdtReversePlanDto SecurePlan(
        CsdtReversePlanDto plan,
        CsdtRealtimeRouteDefinition reverseRoute,
        string? rawCourseCode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var blockers = plan.Blockers.ToList();
        var hasUsableToken = !string.IsNullOrWhiteSpace(plan.PlanToken) &&
                             plan.PlanToken.Length <= 512 &&
                             !char.IsWhiteSpace(plan.PlanToken[0]) &&
                             !char.IsWhiteSpace(plan.PlanToken[^1]) &&
                             plan.PlanToken.IndexOfAny(['\0', '\r', '\n']) < 0;
        if (!hasUsableToken)
        {
            blockers.Add("PlanToken khong hop le; khong the execute.");
        }

        AddCountBlocker(
            blockers,
            plan.V1OnlyRequiresReview,
            "V1_ONLY_REQUIRES_REVIEW");
        AddCountBlocker(
            blockers,
            plan.IdentityChanged,
            "IDENTITY_CHANGED");
        AddCountBlocker(
            blockers,
            plan.ConflictRequiresReview,
            "CONFLICT_REQUIRES_REVIEW");

        return plan with
        {
            IsReadOnly = true,
            VehicleType = reverseRoute.VehicleType,
            Direction = CsdtRealtimeDirections.V1ToV2,
            SourceDatabaseName = reverseRoute.SourceDatabaseName,
            TargetDatabaseName = reverseRoute.TargetDatabaseName,
            MaKhoaHoc = rawCourseCode,
            Executable = plan.Executable && blockers.Count == 0 && hasUsableToken,
            Blockers = blockers,
        };
    }

    private static void AddCountBlocker(
        ICollection<string> blockers,
        long count,
        string code)
    {
        if (count > 0 && !blockers.Any(item =>
                item.Contains(code, StringComparison.Ordinal)))
        {
            blockers.Add($"{code}: {count} dong can Admin review.");
        }
    }

    private static CsdtRealtimeCommand CreateCommand(
        CsdtRealtimeRouteDefinition route,
        string commandType,
        CsdtRealtimeUserContext user,
        string? expectedStateToken)
        => new()
        {
            CommandType = commandType,
            StreamCode = route.StreamCode,
            VehicleType = route.VehicleType,
            SourceProfileCode = route.SourceProfileCode,
            TargetProfileCode = route.TargetProfileCode,
            SourceDatabaseName = route.SourceDatabaseName,
            TargetDatabaseName = route.TargetDatabaseName,
            MaCSDT = route.MaCSDT,
            ExpectedStateToken = expectedStateToken,
            RequestedBy = RequireActor(user.Actor),
        };

    private static CsdtReverseExecuteResultDto RejectedReverse(
        string status,
        string message,
        CsdtReversePlanDto plan)
        => new()
        {
            Accepted = false,
            Status = status,
            Message = message,
            Plan = plan,
        };

    private static void RequireAdmin(CsdtRealtimeUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.WriteAuthorized ||
            !string.Equals(user.Role, AppRoles.Admin, StringComparison.Ordinal))
        {
            throw new CsdtRealtimeAuthorizationException();
        }
    }

    private static string RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) ||
            actor.Length > 255 ||
            actor.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Actor khong hop le.", nameof(actor));
        }

        return actor;
    }

    private static int NormalizeTake(int take)
        => Math.Clamp(take, 1, 200);

    private static int RouteOrder(CsdtRealtimeRouteDefinition route)
        => string.Equals(route.VehicleType, CsdtRealtimeVehicleTypes.Oto, StringComparison.Ordinal)
            ? 0
            : 1;
}
