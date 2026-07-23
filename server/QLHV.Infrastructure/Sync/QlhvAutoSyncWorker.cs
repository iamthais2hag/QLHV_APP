using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvAutoSyncWorker : BackgroundService
{
    private readonly QlhvAutoSyncQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QlhvAutoSyncOptions _options;

    public QlhvAutoSyncWorker(
        QlhvAutoSyncQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<QlhvAutoSyncOptions> options)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var reconciliationTask = RunReconciliationLoopAsync(stoppingToken);
        try
        {
            await foreach (var item in _queue.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(item, stoppingToken);
            }
        }
        finally
        {
            try
            {
                await reconciliationTask;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal host shutdown.
            }
        }
    }

    private async Task RunReconciliationLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            await ReconcileAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runs = scope.ServiceProvider.GetRequiredService<IQlhvAutoSyncRunRepository>();
            var active = await runs.GetActiveAsync(stoppingToken);
            if (active is null)
            {
                return;
            }

            if (string.Equals(active.Status, QlhvAutoSyncConstants.Running, StringComparison.Ordinal))
            {
                var globalLock = scope.ServiceProvider.GetRequiredService<IQlhvAutoSyncGlobalLock>();
                await using (var lease = await globalLock.TryAcquireAsync(stoppingToken))
                {
                    if (lease is null)
                    {
                        return;
                    }

                    // A session-owned lock disappears with its process. Owning the lock here means
                    // the persisted RUNNING row was interrupted and can be safely claimed again.
                    await runs.RequeueInterruptedAsync(active.RunId, CancellationToken.None);
                }
            }

            await ProcessAsync(new QlhvAutoSyncWorkItem(active.RunId), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (QlhvAutoSyncStoreUnavailableException)
        {
            // SQL patches are deployed separately; never take the host down while pending.
        }
        catch
        {
            // Status exposes the safe failure. Reconciliation is best-effort.
        }
    }

    private async Task ProcessAsync(
        QlhvAutoSyncWorkItem item,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IQlhvAutoSyncRunRepository>();
        var globalLock = scope.ServiceProvider.GetRequiredService<IQlhvAutoSyncGlobalLock>();
        var markedRunning = false;
        try
        {
            await using var lease = await globalLock.TryAcquireAsync(stoppingToken);
            if (lease is null)
            {
                // Another process owns the durable run.
                return;
            }

            var run = await runs.GetByIdAsync(item.RunId, stoppingToken);
            if (run is null ||
                !string.Equals(run.Status, QlhvAutoSyncConstants.Queued, StringComparison.Ordinal))
            {
                return;
            }

            markedRunning = await runs.MarkRunningAsync(
                run.RunId,
                DateTime.UtcNow,
                stoppingToken);
            if (!markedRunning)
            {
                return;
            }

            run = await runs.GetByIdAsync(run.RunId, stoppingToken)
                ?? throw new InvalidOperationException("Auto Sync run vua claim khong con ton tai.");
            var coordinator = scope.ServiceProvider.GetRequiredService<QlhvAutoSyncCoordinator>();
            var outcome = await coordinator.ExecuteAsync(run, stoppingToken);
            await runs.CompleteAsync(run.RunId, outcome, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Keep a claimed run as RUNNING. The session-owned applock is released
            // with this scope/process; startup reconciliation can then safely requeue
            // and resume the idempotent OTO -> MOTO sequence on the next host start.
        }
        catch (Exception ex)
        {
            if (markedRunning)
            {
                await SafeCompleteAsync(
                    runs,
                    item.RunId,
                    $"Auto Sync worker failed: {ex.GetType().Name}.");
            }
        }
    }

    private static async Task SafeCompleteAsync(
        IQlhvAutoSyncRunRepository runs,
        Guid runId,
        string safeError)
    {
        try
        {
            await runs.CompleteAsync(
                runId,
                new QlhvAutoSyncOutcome(
                    QlhvAutoSyncConstants.Failed,
                    safeError,
                    DateTime.UtcNow),
                CancellationToken.None);
        }
        catch
        {
            // Startup reconciliation will revisit an active row.
        }
    }
}
