using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvRefreshBackupQueue
{
    ValueTask EnqueueAsync(
        QlhvRefreshBackupWorkItem item,
        CancellationToken cancellationToken = default);
}
