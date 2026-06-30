using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IMotoSyncRepository
{
    Task<MotoSyncPlanDto> BuildPlanAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoSyncExecuteSummaryDto> ExecuteInsertOnlyAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoSyncExecuteSummaryDto> ExecuteInsertAndUpdateAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);
}
