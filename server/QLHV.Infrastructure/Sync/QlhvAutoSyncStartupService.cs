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

    public QlhvAutoSyncStartupService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        IOptions<QlhvAutoSyncOptions> options)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Development/Test are intentionally hard-disabled for server-startup execution.
        if (!_environment.IsProduction() ||
            !_options.Enabled ||
            !_options.RunOnServerStartup)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Clamp(_options.ReadinessPollSeconds, 1, 60));
        while (!stoppingToken.IsCancellationRequested)
        {
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
                // A transient readiness probe failure is not a startup attempt.
                await Task.Delay(delay, stoppingToken);
                continue;
            }

            if (!status.IsReady)
            {
                await Task.Delay(delay, stoppingToken);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var autoSync = scope.ServiceProvider.GetRequiredService<IQlhvAutoSyncService>();
                // Exactly one call per process startup. A durable unique active slot and
                // sp_getapplock prevent concurrent server processes from duplicating the run.
                await autoSync.QueueAsync(QlhvAutoSyncConstants.StartupTrigger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Queue/store failure must not crash the API. Operators can use the manual
                // Admin action after repair.
            }

            return;
        }
    }
}
