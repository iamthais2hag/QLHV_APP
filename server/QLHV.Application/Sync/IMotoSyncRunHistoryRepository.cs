using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IMotoSyncRunHistoryRepository
{
    Task<long> CreateAsync(
        MotoSyncRunHistoryCreateDto entry,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MotoSyncRunHistoryListItemDto>> SearchAsync(
        MotoSyncRunHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoSyncRunHistoryDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);
}
