namespace QLHV.Application.Sync;

public interface IQlhvSourceOperationLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default);
}
