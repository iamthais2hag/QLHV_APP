using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvImportReadRepository
{
    Task<QlhvImportSourceSnapshot> ReadSourceAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default);

    Task<QlhvImportTargetSnapshot> ReadTargetAsync(
        QlhvImportRequest request,
        IReadOnlyCollection<string> sourceMaDks,
        CancellationToken cancellationToken = default);
}
