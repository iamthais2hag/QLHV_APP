using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IMotoCenterTransferRunHistoryRepository
{
    Task<long> CreateAsync(
        MotoCenterTransferRunHistoryCreateDto entry,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MotoCenterTransferRunHistoryListItemDto>> SearchAsync(
        MotoCenterTransferRunHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoCenterTransferRunHistoryDetailDto?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);
}
