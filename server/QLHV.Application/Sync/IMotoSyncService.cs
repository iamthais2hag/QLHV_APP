using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IMotoSyncService
{
    Task<MotoSyncPlanDto> GetPlanAsync(
        MotoSyncPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MotoSyncExecuteResultDto> ExecuteTestAsync(
        MotoSyncTestExecuteRequest request,
        CancellationToken cancellationToken = default);
}
