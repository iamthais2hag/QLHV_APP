using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// One-way HocVien sync service from CSDT_V2 to QLHV_APP.
/// Dry-run does not write. Execute only runs when target writes and manual confirmation are enabled.
/// </summary>
public interface IHocVienSyncService
{
    Task<SyncConfigCheckDto> ConfigCheckHocVienAsync(CancellationToken cancellationToken = default);

    Task<DryRunResultDto> DryRunHocVienAsync(CancellationToken cancellationToken = default);

    Task<V2HocVienSourceDiagnosticsResultDto> GetHocVienSourceDiagnosticsAsync(
        CancellationToken cancellationToken = default);

    Task<SyncExecuteResultDto> ExecuteHocVienAsync(
        SyncExecuteRequest request,
        CancellationToken cancellationToken = default);
}
