using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvOperationsRepository
{
    Task<QlhvOperationDataSnapshot> ReadStatusSnapshotAsync(
        QlhvOperationSourceDefinition source,
        CancellationToken cancellationToken = default);
}
