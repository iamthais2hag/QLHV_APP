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

    Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
        string sourceType,
        string operationType,
        CancellationToken cancellationToken = default);
}
