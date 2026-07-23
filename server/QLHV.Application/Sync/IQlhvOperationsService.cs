using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvOperationsService
{
    Task<QlhvOperationsStatusDto> GetStatusAsync(
        string sourceType,
        string currentUserRole,
        bool writeAuthorized,
        CancellationToken cancellationToken = default);

    Task<QlhvRefreshBackupResultDto> QueueRefreshBackupAsync(
        QlhvRefreshBackupRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QlhvOperationHistoryDto>> GetHistoryAsync(
        string sourceType,
        CancellationToken cancellationToken = default);
}
