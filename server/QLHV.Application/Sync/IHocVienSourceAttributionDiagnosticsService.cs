using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IHocVienSourceAttributionDiagnosticsService
{
    Task<HocVienSourceAttributionDiagnosticsResultDto> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default);
}
