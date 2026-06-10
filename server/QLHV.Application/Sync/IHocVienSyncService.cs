using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>Application service for one-way HocVien sync from CSDT_V2 to QLHV_APP.</summary>
public interface IHocVienSyncService
{
    /// <summary>Dry-run: configuration/source checks and safe plan only. No target writes.</summary>
    Task<DryRunResultDto> DryRunHocVienAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarded manual execution. Must reject unless server-side writes are enabled and explicit confirmation is present.
    /// </summary>
    Task<HocVienSyncExecuteResultDto> ExecuteHocVienAsync(
        HocVienSyncExecuteRequest request,
        CancellationToken cancellationToken = default);
}
