using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvOperationHistoryRepository
{
    Task<bool> TryCreateAsync(
        QlhvOperationHistoryCreate entry,
        CancellationToken cancellationToken = default);

    Task MarkRunningAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task CompleteAsync(
        QlhvOperationHistoryCompletion completion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QlhvOperationHistoryDto>> SearchAsync(
        string sourceType,
        int take,
        CancellationToken cancellationToken = default);

    Task<QlhvOperationHistoryDto?> GetActiveAsync(
        string sourceType,
        CancellationToken cancellationToken = default);

    Task<QlhvOperationHistoryDto?> GetByOperationIdAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
        => Task.FromException<QlhvOperationHistoryDto?>(
            new NotSupportedException("Repository does not support lookup by operation id."));

    Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
        string sourceType,
        string operationType,
        CancellationToken cancellationToken = default);

    async Task<QlhvOperationHistoryDto?> GetLatestSuccessfulAsync(
        string sourceType,
        string operationType,
        CancellationToken cancellationToken = default)
    {
        var history = await SearchAsync(sourceType, 200, cancellationToken);
        return history.FirstOrDefault(entry =>
            string.Equals(entry.OperationType, operationType, StringComparison.Ordinal)
            && entry.Status is QlhvOperationTypes.Succeeded or QlhvOperationTypes.PartialSuccess);
    }
}
