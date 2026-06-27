using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Read-only repository for comparing existing target HocVien keys with DATA_V1/DATA_V2 keys.
/// Implementations must not write App_HocVien or App_DongBoLog.
/// </summary>
public interface IHocVienSourceAttributionDiagnosticsRepository
{
    Task<IReadOnlyList<HocVienComparableAttributionRowDto>> ReadTargetRowsAsync(
        CancellationToken cancellationToken = default);

    Task<HocVienSourceComparableReadResultDto> ReadSourceRowsAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default);
}
