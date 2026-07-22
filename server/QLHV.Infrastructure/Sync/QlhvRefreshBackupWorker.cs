using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvRefreshBackupWorker : BackgroundService
{
    private readonly QlhvRefreshBackupQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    public QlhvRefreshBackupWorker(
        QlhvRefreshBackupQueue queue,
        IServiceScopeFactory scopeFactory)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            await ReconcilePersistedOperationsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ReconcilePersistedOperationsAsync(CancellationToken stoppingToken)
    {
        foreach (var source in QlhvOperationSourceCatalog.All)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var history = scope.ServiceProvider.GetRequiredService<IQlhvOperationHistoryRepository>();
                var active = await history.GetActiveAsync(source.SourceType, stoppingToken);
                if (active is null)
                {
                    continue;
                }

                if (string.Equals(active.Status, QlhvOperationTypes.Queued, StringComparison.Ordinal))
                {
                    // The history row is the durable hand-off. MarkRunning is atomic, so when
                    // several API processes restart only one worker can claim this queued item.
                    await ProcessAsync(
                        new QlhvRefreshBackupWorkItem(active.OperationId, source.SourceType),
                        stoppingToken);
                    continue;
                }

                if (string.Equals(active.Status, QlhvOperationTypes.Running, StringComparison.Ordinal))
                {
                    var operationLock = scope.ServiceProvider.GetRequiredService<IQlhvSourceOperationLock>();
                    await using var lease = await operationLock.TryAcquireAsync(source, stoppingToken);
                    if (lease is not null)
                    {
                        var recoveryMessage = string.Empty;
                        if (string.Equals(
                                active.OperationType,
                                QlhvOperationTypes.RefreshBackup,
                                StringComparison.Ordinal))
                        {
                            var executor = scope.ServiceProvider.GetRequiredService<IQlhvBackupRefreshExecutor>();
                            var accessRecovered = await executor.TryRecoverDatabaseAccessAsync(
                                source,
                                CancellationToken.None);
                            recoveryMessage = accessRecovered
                                ? " BAK da duoc dua ve ONLINE/MULTI_USER;"
                                : " Khong xac nhan duoc BAK ONLINE/MULTI_USER;";
                        }

                        // A session-owned applock disappears when its owning process dies. If we
                        // can acquire it, this RUNNING row has no live owner and must not block the
                        // source forever. We do not claim whether the interrupted restore committed.
                        await CompleteFailureAsync(
                            history,
                            active.OperationId,
                            "Operation RUNNING bi mat owner sau khi host khoi dong lai;" +
                            recoveryMessage +
                            " ket qua truoc do khong xac dinh, can refresh lai truoc khi sync.",
                            CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (QlhvOperationsStoreUnavailableException)
            {
                // The schema patch is intentionally deployed separately. Do not crash the API
                // host before that one-time patch is applied.
                return;
            }
            catch
            {
                // Status will expose repository/database errors. A recovery probe must never take
                // down the API process or disclose connection details.
            }
        }
    }

    private async Task ProcessAsync(
        QlhvRefreshBackupWorkItem item,
        CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<IQlhvOperationHistoryRepository>();
        var operationLock = scope.ServiceProvider.GetRequiredService<IQlhvSourceOperationLock>();
        var executor = scope.ServiceProvider.GetRequiredService<IQlhvBackupRefreshExecutor>();
        var source = QlhvOperationSourceCatalog.GetRequired(item.SourceType);
        var markedRunning = false;
        try
        {
            // Hold the cross-process lease before exposing RUNNING. Startup reconciliation
            // may safely classify a RUNNING row with no lease owner as abandoned.
            await using var lease = await operationLock.TryAcquireAsync(source, stoppingToken);
            if (lease is null)
            {
                // Another process can be claiming the same durable QUEUED row. It owns the
                // history transition and completion while its session lease is held.
                return;
            }

            // Only one process can transition a queued row to RUNNING.
            await history.MarkRunningAsync(item.OperationId, stoppingToken);
            markedRunning = true;
            var result = await executor.ExecuteAsync(source, stoppingToken);
            await CompleteCommittedRefreshAsync(history, item.OperationId, result);
            return;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            if (markedRunning)
            {
                await CompleteFailureAsync(
                    history,
                    item.OperationId,
                    "API host dung khi refresh BAK dang chay.",
                    CancellationToken.None);
            }
        }
        catch (InvalidOperationException) when (!markedRunning)
        {
            // Another process already claimed this queued operation.
        }
        catch (Exception ex)
        {
            if (markedRunning)
            {
                var safeError = ex is QlhvBackupRefreshException
                    ? ex.Message
                    : $"Refresh BAK failed: {ex.GetType().Name}.";
                await CompleteFailureAsync(
                    history,
                    item.OperationId,
                    safeError,
                    CancellationToken.None);
            }
        }
    }

    private static async Task CompleteCommittedRefreshAsync(
        IQlhvOperationHistoryRepository history,
        Guid operationId,
        QlhvRefreshBackupExecutionResult result)
    {
        var succeeded = new QlhvOperationHistoryCompletion(
            operationId,
            QlhvOperationTypes.Succeeded,
            DateTime.UtcNow,
            result.LiveRows.NguoiLX,
            0, 0, 0, 0, 0,
            result.SnapshotToken,
            null,
            result.DetailJson,
            LiveRows: result.LiveRows.NguoiLX,
            BackupRows: result.BackupRows.NguoiLX);
        try
        {
            await history.CompleteAsync(succeeded, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The restore, count verification and snapshot-token write have already committed.
            // Never relabel that database outcome as rolled back. If possible, clear the active
            // row with an explicit telemetry failure that states data was committed.
            try
            {
                await history.CompleteAsync(
                    succeeded with
                    {
                        Status = QlhvOperationTypes.Failed,
                        ErrorMessage = $"Refresh BAK da commit nhung ghi history SUCCEEDED that bai: {ex.GetType().Name}.",
                    },
                    CancellationToken.None);
            }
            catch
            {
                // A timeout may mean the first update actually committed. Status/history probing
                // will resolve that state, and startup reconciliation prevents a permanent lock.
            }
        }
    }

    private static async Task CompleteFailureAsync(
        IQlhvOperationHistoryRepository history,
        Guid operationId,
        string safeError,
        CancellationToken cancellationToken)
    {
        try
        {
            await history.CompleteAsync(
                new QlhvOperationHistoryCompletion(
                    operationId,
                    QlhvOperationTypes.Failed,
                    DateTime.UtcNow,
                    0, 0, 0, 0, 0, 0,
                    null,
                    safeError,
                    null),
                cancellationToken);
        }
        catch
        {
            // Do not leak internal SQL/connection details through the background worker.
        }
    }
}
