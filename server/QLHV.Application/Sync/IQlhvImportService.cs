using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvImportService
{
    Task<QlhvImportPlanDto> GetPlanAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default);

    Task<QlhvImportDiagnosticsDto> GetDiagnosticsAsync(
        QlhvImportRequest request,
        CancellationToken cancellationToken = default);

    Task<QlhvImportExecuteResultDto> ExecuteAsync(
        QlhvImportExecuteRequest request,
        CancellationToken cancellationToken = default);
}
