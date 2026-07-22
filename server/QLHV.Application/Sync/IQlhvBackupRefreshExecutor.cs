using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvBackupRefreshExecutor
{
    Task<QlhvRefreshBackupExecutionResult> ExecuteAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default);

    Task<bool> TryRecoverDatabaseAccessAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default);
}
