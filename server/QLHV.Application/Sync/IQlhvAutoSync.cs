using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvAutoSyncService
{
    Task<QlhvAutoSyncQueueResultDto> QueueAsync(
        string triggerType,
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncQueueResultDto> QueueSessionStartAsync(
        bool serverStartedByLauncher,
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncQueueResultDto> QueueEnsureFreshAsync(
        CancellationToken cancellationToken = default);

    Task<QlhvSessionStartStatusDto> GetSessionStartStatusAsync(
        bool serverStartedByLauncher,
        Guid? runId = null,
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncStatusDto> GetStatusAsync(
        Guid? runId = null,
        CancellationToken cancellationToken = default);
}

public interface IQlhvAutoSyncRunRepository
{
    Task<bool> TryCreateAsync(
        QlhvAutoSyncRunCreate entry,
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncRunRecord?> GetByIdAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncRunRecord?> GetActiveAsync(
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncRunRecord?> GetLatestAsync(
        CancellationToken cancellationToken = default);

    Task<QlhvAutoSyncRunRecord?> GetLatestByTriggerAsync(
        string triggerType,
        CancellationToken cancellationToken = default);

    Task<bool> MarkRunningAsync(
        Guid runId,
        DateTime startedAtUtc,
        CancellationToken cancellationToken = default);

    Task SetCurrentSourceAsync(
        Guid runId,
        string sourceType,
        CancellationToken cancellationToken = default);

    Task SetCurrentStageAsync(
        Guid runId,
        string stage,
        CancellationToken cancellationToken = default);

    Task SetSourceResultAsync(
        Guid runId,
        QlhvAutoSyncSourceResultDto result,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid runId,
        QlhvAutoSyncOutcome outcome,
        CancellationToken cancellationToken = default);

    Task<bool> RequeueInterruptedAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}

public interface IQlhvAutoSyncQueue
{
    ValueTask EnqueueAsync(
        QlhvAutoSyncWorkItem item,
        CancellationToken cancellationToken = default);
}

public interface IQlhvAutoSyncGlobalLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        CancellationToken cancellationToken = default);
}

public interface IQlhvAutoSyncSourceRunner
{
    Task<QlhvAutoSyncSourceResultDto> RunAsync(
        Guid runId,
        string sourceType,
        string actor,
        CancellationToken cancellationToken = default);
}

public sealed class QlhvAutoSyncStoreUnavailableException : InvalidOperationException
{
    public QlhvAutoSyncStoreUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
