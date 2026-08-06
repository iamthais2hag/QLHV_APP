using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Rt03;
using QLHV.Application.Sync.TeacherVehicleProjection;

namespace QLHV.Infrastructure.Sync.Rt03;

/// <summary>
/// Database-controlled production worker. The Windows service may remain
/// running while the authoritative control row is OFF or BLOCKED. No Change
/// Tracking read or writer lock is taken in either state.
/// </summary>
public sealed class Rt03ProductionRealtimeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Rt03ProductionOptions _options;
    private readonly ILogger<Rt03ProductionRealtimeWorker> _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    public Rt03ProductionRealtimeWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<Rt03ProductionOptions> options,
        ILogger<Rt03ProductionRealtimeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableRt03ProductionRealtime)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunControlIterationAsync(stoppingToken);
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service stop.
        }
        finally
        {
            await SafeRecordStoppedAsync();
        }
    }

    private async Task RunControlIterationAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var state = scope.ServiceProvider
            .GetRequiredService<IRt03ProductionRuntimeStateStore>();
        var controlStore = scope.ServiceProvider
            .GetRequiredService<IRt03RealtimeControlStore>();

        Rt03RealtimeControlRecord control;
        Rt03RealtimeRunRequest? activeRun;
        try
        {
            control = await controlStore.ReadAsync(cancellationToken);
            activeRun = string.Equals(
                control.State,
                Rt03RealtimeControlStates.On,
                StringComparison.Ordinal)
                ? await controlStore.ReadActiveRunOnceAsync(cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SafeRecordAsync(
                state,
                Rt03WorkerStatuses.Blocked,
                Rt03RealtimeMasterErrors.ControlUnavailable);
            _logger.LogError(exception,
                "RT-03 master control cannot be read; worker remains fail-closed.");
            return;
        }

        var workKind = Rt03RealtimeMasterPolicy.Decide(
            control.State,
            string.Equals(activeRun?.Status, Rt03RealtimeRunRequestStatuses.Pending,
                StringComparison.Ordinal));
        if (workKind == Rt03MasterWorkKind.HeartbeatOnly)
        {
            await state.RecordWorkerAsync(
                _instanceId,
                Rt03WorkerStatuses.RealtimeOff,
                null,
                false,
                null,
                cancellationToken);
            return;
        }

        if (workKind == Rt03MasterWorkKind.BlockedHeartbeat)
        {
            await state.RecordWorkerAsync(
                _instanceId,
                Rt03WorkerStatuses.Blocked,
                null,
                false,
                control.Reason,
                cancellationToken);
            return;
        }

        Rt03RealtimeRunRequest? claimedRun = null;
        if (workKind == Rt03MasterWorkKind.RunOnce)
        {
            claimedRun = await controlStore.TryClaimRunOnceAsync(
                _instanceId, cancellationToken);
            if (claimedRun is null)
            {
                return;
            }
        }

        try
        {
            await state.RecordWorkerAsync(
                _instanceId,
                Rt03WorkerStatuses.Starting,
                null,
                false,
                null,
                cancellationToken);
            var outcome = await RunEventDrivenPassAsync(
                scope.ServiceProvider,
                state,
                workKind == Rt03MasterWorkKind.RunOnce,
                cancellationToken);
            if (claimedRun is not null)
            {
                await controlStore.CompleteRunOnceAsync(
                    claimedRun.RunRequestId,
                    Rt03RealtimeRunRequestStatuses.Completed,
                    outcome,
                    null,
                    cancellationToken);
            }
        }
        catch (Rt03SafetyException exception) when (
            Rt03WorkerFailurePolicy.IsRetryable(exception.Code))
        {
            _logger.LogInformation(
                "RT-03 source changed while a bounded cycle was planned; " +
                "checkpoint remains unchanged and the next event-driven pass will retry.");
            if (claimedRun is not null)
            {
                await controlStore.CompleteRunOnceAsync(
                    claimedRun.RunRequestId,
                    Rt03RealtimeRunRequestStatuses.Completed,
                    "DEFERRED_SOURCE_CHANGED",
                    null,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var code = RedactedCode(exception);
            await SafeTransitionBlockedAsync(controlStore, code);
            await SafeRecordAsync(state, Rt03WorkerStatuses.Blocked, code);
            if (claimedRun is not null)
            {
                await SafeCompleteBlockedRunAsync(controlStore, claimedRun.RunRequestId, code);
            }

            _logger.LogError(exception,
                "RT-03 entered BLOCKED; service remains running without mutation retries.");
        }
    }

    private async Task<string> RunEventDrivenPassAsync(
        IServiceProvider services,
        IRt03ProductionRuntimeStateStore state,
        bool runOnce,
        CancellationToken cancellationToken)
    {
        var timeAuthority = services.GetRequiredService<ITimeAuthorityService>();
        var timeHealth = await timeAuthority.GetWriteAuthorizationAsync(cancellationToken);
        if (!TimeAuthorityPolicy.IsMutationAllowed(timeHealth))
        {
            throw new Rt03SafetyException(
                Rt03Errors.TimeAuthorityBlocked,
                "RT-03 mutation is blocked because SQL SYSUTCDATETIME() is unavailable.");
        }

        await services.GetRequiredService<IRt03WorkerPermissionProbe>()
            .VerifyAsync(cancellationToken);

        var globalLock = services.GetRequiredService<IQlhvDirectRealtimeGlobalLock>();
        await using var globalLease = await globalLock.TryAcquireAsync(cancellationToken);
        if (globalLease is null)
        {
            throw new Rt03SafetyException(
                Rt03Errors.WorkerAlreadyActive,
                "Another writer owns the realtime/global writer lock.");
        }

        var feature = await state.ReadFeatureStateAsync(cancellationToken);
        ValidateFeatureState(feature);
        var profiles = await state.ReadProfileStatesAsync(cancellationToken);
        ValidateProfileSequence(profiles);
        var enabledProfileCodes = profiles
            .Where(item => item.Enabled)
            .OrderBy(item => item.SequenceOrder)
            .Select(item => item.SourceProfileCode)
            .ToArray();

        var backlogs = await services.GetRequiredService<IRt03EventBacklogProbe>()
            .ReadAsync(enabledProfileCodes, cancellationToken);
        var projection = services
            .GetRequiredService<ITeacherVehicleProjectionCoordinator>();
        var projectionBacklogs = new Dictionary<string, TeacherVehicleProjectionBacklog>(
            StringComparer.Ordinal);
        foreach (var profileCode in enabledProfileCodes)
        {
            projectionBacklogs[profileCode] = await projection.ReadBacklogAsync(
                profileCode, cancellationToken);
        }
        foreach (var backlog in backlogs)
        {
            if (!backlog.IsWindowValid)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.ChangeTrackingWindowRejected,
                    $"Change Tracking window is invalid for {backlog.SourceProfileCode}.");
            }
        }

        if (backlogs.All(item => item.CurrentVersion == item.CheckpointVersion) &&
            projectionBacklogs.Values.All(item => !item.HasPending))
        {
            var idleOutcome = Rt03RealtimeMasterPolicy.IdleOutcome(runOnce);
            await state.RecordIdleAsync(_instanceId, idleOutcome, cancellationToken);
            return idleOutcome;
        }

        var sourceOperationLock = services.GetRequiredService<IQlhvSourceOperationLock>();
        var lastOutcome = Rt03RealtimeOutcomes.Applied;
        var otoPassed = false;
        foreach (var profileCode in enabledProfileCodes)
        {
            var backlog = backlogs.Single(item =>
                string.Equals(item.SourceProfileCode, profileCode, StringComparison.Ordinal));
            var projectionBacklog = projectionBacklogs[profileCode];
            if (profileCode == Rt03Profiles.Moto && !otoPassed)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.OtoMustPassFirst,
                    "MOTO cycle was requested before a healthy OTO decision.");
            }

            if (backlog.CurrentVersion == backlog.CheckpointVersion &&
                !projectionBacklog.HasPending)
            {
                if (profileCode == Rt03Profiles.Oto)
                {
                    otoPassed = true;
                }
                continue;
            }

            var operationSource = ResolveOperationSource(profileCode);
            await using var profileLease = await sourceOperationLock.TryAcquireAsync(
                operationSource,
                cancellationToken);
            if (profileLease is null)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.AutoSyncActive,
                    $"Another writer owns the {profileCode} source-operation lock.");
            }

            // Re-read the authoritative switch immediately before entering the
            // mutation-capable processor. Disable is therefore effective before
            // any new event cycle begins.
            var latestControl = await services
                .GetRequiredService<IRt03RealtimeControlStore>()
                .ReadAsync(cancellationToken);
            if (!string.Equals(latestControl.State,
                    Rt03RealtimeControlStates.On, StringComparison.Ordinal))
            {
                await state.RecordWorkerAsync(
                    _instanceId,
                    Rt03WorkerStatuses.RealtimeOff,
                    null,
                    false,
                    null,
                    cancellationToken);
                return Rt03RealtimeOutcomes.RealtimeOff;
            }

            await state.RecordWorkerAsync(
                _instanceId,
                Rt03WorkerStatuses.Healthy,
                profileCode,
                true,
                null,
                cancellationToken);
            if (projectionBacklog.HasPending)
            {
                var projectionResult = await projection.ProcessPendingAsync(
                    profileCode, cancellationToken);
                lastOutcome = projectionResult.Outcome;
            }

            if (backlog.CurrentVersion == backlog.CheckpointVersion)
            {
                if (profileCode == Rt03Profiles.Oto)
                {
                    otoPassed = true;
                }
                continue;
            }

            var processor = services
                .GetRequiredService<IRt03ProductionRealtimeCycleProcessor>();
            var result = await processor.ProcessAsync(
                profileCode, _instanceId, cancellationToken);
            if (result.DeletedOrDeactivatedRows != 0 ||
                result.DuplicateActiveRows != 0 ||
                string.Equals(result.Status, Rt03CycleStatuses.Blocked,
                    StringComparison.Ordinal))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.TargetDrift,
                    $"RT-03 {profileCode} cycle violated a final invariant.");
            }

            await state.RecordCycleAsync(_instanceId, result, cancellationToken);
            lastOutcome = string.IsNullOrWhiteSpace(result.CycleOutcome)
                ? result.Status
                : result.CycleOutcome;
            if (profileCode == Rt03Profiles.Oto)
            {
                otoPassed = true;
            }
        }

        return lastOutcome;
    }

    internal static QlhvOperationSourceDefinition ResolveOperationSource(
        string profileCode) => profileCode switch
        {
            Rt03Profiles.Oto => QlhvOperationSourceCatalog.GetRequired("OTO"),
            Rt03Profiles.Moto => QlhvOperationSourceCatalog.GetRequired("MOTO"),
            _ => throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                "RT-03 source-operation lock profile is not allowlisted."),
        };

    internal static bool ShouldDeferForUnavailableProfileLease(
        IAsyncDisposable? profileLease) => profileLease is null;

    private static void ValidateFeatureState(Rt03ProductionFeatureState state)
    {
        if (!state.EnableProductionRealtime ||
            !state.EnableProductionShadow ||
            !state.EnableProductionWrites ||
            state.EnableProductionCanary ||
            !state.EnableControlledCutover ||
            state.EnableProductionDeletes)
        {
            throw new Rt03SafetyException(
                Rt03Errors.FeatureStateRejected,
                "Production control-plane flags are not controlled-cutover safe.");
        }
    }

    private void ValidateProfileSequence(
        IReadOnlyCollection<Rt03ProductionProfileState> profiles)
    {
        if (profiles.Count != 2 ||
            profiles.SingleOrDefault(item => item.SourceProfileCode == Rt03Profiles.Oto)
                is not { SequenceOrder: 1 } oto ||
            profiles.SingleOrDefault(item => item.SourceProfileCode == Rt03Profiles.Moto)
                is not { SequenceOrder: 2 } moto ||
            !oto.Enabled ||
            (moto.Enabled && !_options.EnableMoto) ||
            !_options.EnableOto)
        {
            throw new Rt03SafetyException(
                Rt03Errors.OtoMustPassFirst,
                "Production profile activation/order does not match OTO then MOTO.");
        }
    }

    private static string RedactedCode(Exception exception) =>
        exception is Rt03SafetyException safety
            ? safety.Code
            : "RT03_MASTER_UNEXPECTED_FAILURE";

    private async Task SafeTransitionBlockedAsync(
        IRt03RealtimeControlStore store,
        string code)
    {
        try
        {
            await store.TransitionToBlockedAsync(_instanceId, code, CancellationToken.None);
        }
        catch
        {
            // Preserve the original blocker and remain fail-closed.
        }
    }

    private async Task SafeCompleteBlockedRunAsync(
        IRt03RealtimeControlStore store,
        Guid requestId,
        string code)
    {
        try
        {
            await store.CompleteRunOnceAsync(
                requestId,
                Rt03RealtimeRunRequestStatuses.Blocked,
                Rt03RealtimeOutcomes.Blocked,
                code,
                CancellationToken.None);
        }
        catch
        {
            // The control row remains the authoritative blocker.
        }
    }

    private Task SafeRecordAsync(
        IRt03ProductionRuntimeStateStore state,
        string status,
        string? error) => state.RecordWorkerAsync(
            _instanceId,
            status,
            null,
            false,
            error,
            CancellationToken.None);

    private async Task SafeRecordStoppedAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<IRt03ProductionRuntimeStateStore>()
                .RecordWorkerAsync(
                    _instanceId,
                    Rt03WorkerStatuses.Stopped,
                    null,
                    false,
                    null,
                    CancellationToken.None);
        }
        catch
        {
            // Best effort during host shutdown.
        }
    }
}
