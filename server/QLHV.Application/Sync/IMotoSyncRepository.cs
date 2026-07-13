using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IMotoSyncRepository
{
    Task<MotoSyncPlanDto> BuildPlanAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MotoSyncKhoaHocOptionDto>> GetKhoaHocOptionsAsync(
        MotoSyncKhoaHocOptionsQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoTargetDonViGTVTOptionsResultDto> GetTargetDonViGTVTOptionsAsync(
        MotoTargetDonViGTVTOptionsQuery query,
        CancellationToken cancellationToken = default);

    Task<MotoCenterTransferPlanDto> BuildCenterTransferPlanAsync(
        MotoCenterTransferPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoCenterTransferSummaryDto> ExecuteCenterTransferAsync(
        MotoCenterTransferPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoSyncExecuteSummaryDto> ExecuteInsertOnlyAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoSyncExecuteSummaryDto> ExecuteInsertAndUpdateAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);
}
