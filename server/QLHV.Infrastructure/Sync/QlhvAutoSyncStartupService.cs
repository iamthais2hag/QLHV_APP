using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvAutoSyncStartupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly QlhvAutoSyncOptions _options;
    private readonly IQlhvAutoSyncPollingState _pollingState;

    public QlhvAutoSyncStartupService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        IOptions<QlhvAutoSyncOptions> options,
        IQlhvAutoSyncPollingState? pollingState = null)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _options = options.Value;
        _pollingState = pollingState ?? new QlhvAutoSyncPollingState();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _environment.IsProduction() &&
            _options.Enabled &&
            _options.RunOnServerStartup &&
            _options.PollingEnabled &&
            _options.FallbackModeEnabled;
        var disabledReason = !_environment.IsProduction()
            ? "Polling chi chay trong moi truong Production."
            : !_options.Enabled
                ? "QlhvAutoSync.Enabled=false."
                : !_options.RunOnServerStartup
                    ? "QlhvAutoSync.RunOnServerStartup=false."
                    : !_options.PollingEnabled
                        ? "QlhvAutoSync.PollingEnabled=false."
                        : !_options.FallbackModeEnabled
                            ? "QlhvAutoSync.FallbackModeEnabled=false."
                    : string.Empty;
        _pollingState.Configure(
            enabled,
            disabledReason,
            _options.PollingIntervalSeconds);

        // Development/Test are intentionally hard-disabled for server-startup execution.
        if (!enabled)
        {
            return;
        }

        var readinessDelay = TimeSpan.FromSeconds(
            Math.Clamp(_options.ReadinessPollSeconds, 1, 60));
        var pollingDelay = TimeSpan.FromSeconds(
            Math.Clamp(_options.PollingIntervalSeconds, 1, 3600));
        while (!stoppingToken.IsCancellationRequested)
        {
            _pollingState.MarkPollStarted();
            RuntimeStatusDto status;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var readiness = scope.ServiceProvider.GetRequiredService<IRuntimeReadinessService>();
                status = await readiness.GetStatusAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                _pollingState.MarkPollCompleted(
                    "READINESS_FAILED",
                    "Khong the hoan tat kiem tra san sang cho Auto Sync.",
                    readinessDelay);
                // A transient readiness probe failure is not a startup attempt.
                if (!await DelayAsync(readinessDelay, stoppingToken))
                {
                    return;
                }
                continue;
            }

            if (!status.IsReady)
            {
                _pollingState.MarkPollCompleted(
                    "RUNTIME_NOT_READY",
                    null,
                    readinessDelay);
                if (!await DelayAsync(readinessDelay, stoppingToken))
                {
                    return;
                }
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var autoSync = scope.ServiceProvider.GetRequiredService<IQlhvAutoSyncService>();
                // Each tick re-evaluates Live/BAK/QLHV_APP freshness. Up-to-date ticks do
                // not create a run; the durable active slot and sp_getapplock join/reject
                // overlap with a manual or another server-side trigger.
                var result = await autoSync.QueueAsync(
                    QlhvAutoSyncConstants.StartupTrigger,
                    stoppingToken);
                _pollingState.MarkPollCompleted(
                    result.Decision,
                    result.IsUnavailable ? result.Message : null,
                    pollingDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                _pollingState.MarkPollCompleted(
                    "POLL_FAILED",
                    "Khong the hoan tat luot kiem tra Auto Sync.",
                    pollingDelay);
                // Queue/store failure must not crash the API or permanently stop polling.
                // A created run records queue failure in the durable run repository.
            }

            if (!await DelayAsync(pollingDelay, stoppingToken))
            {
                return;
            }
        }
    }

    private static async Task<bool> DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
