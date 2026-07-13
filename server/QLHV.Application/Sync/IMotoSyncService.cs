using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IMotoSyncService
{
    Task<MotoSyncPlanDto> GetPlanAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MotoSyncKhoaHocOptionDto>> GetKhoaHocOptionsAsync(
        MotoSyncKhoaHocOptionsQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoTargetDonViGTVTOptionsResultDto> GetTargetDonViGTVTOptionsAsync(
        MotoTargetDonViGTVTOptionsQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoCenterTransferPlanDto> GetCenterTransferPlanAsync(
        MotoCenterTransferPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoCenterTransferExecuteResultDto> ExecuteCenterTransferTestAsync(
        MotoCenterTransferTestRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoSyncExecuteResultDto> ExecuteTestAsync(
        MotoSyncTestExecuteRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MotoSyncRunHistoryListItemDto>> GetRunHistoryAsync(
        MotoSyncRunHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoSyncRunHistoryDetailDto?> GetRunHistoryDetailAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MotoCenterTransferRunHistoryListItemDto>> GetCenterTransferRunHistoryAsync(
        MotoCenterTransferRunHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoCenterTransferRunHistoryDetailDto?> GetCenterTransferRunHistoryDetailAsync(
        long id,
        CancellationToken cancellationToken = default);
}
