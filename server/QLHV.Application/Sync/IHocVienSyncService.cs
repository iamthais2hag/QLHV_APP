using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

<<<<<<< HEAD
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
=======
/// <summary>
/// Nghiệp vụ đồng bộ học viên một chiều từ CSDT_V2 sang QLHV_APP.
/// Dry-run không ghi dữ liệu. Execute chỉ chạy khi đã bật công tắc và xác nhận thủ công.
/// </summary>
public interface IHocVienSyncService
{
    /// <summary>
    /// Chạy thử: kiểm tra cấu hình/kết nối, dựng kế hoạch và trả về tóm tắt an toàn.
    /// KHÔNG ghi dữ liệu, KHÔNG ghi App_DongBoLog.
    /// </summary>
    Task<DryRunResultDto> DryRunHocVienAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực thi đồng bộ ghi (manual, có kiểm soát). Bị chặn nếu EnableTargetWrites=false hoặc
    /// thiếu xác nhận. Trả về tóm tắt an toàn (không chứa bí mật).
    /// </summary>
    Task<SyncExecuteResultDto> ExecuteHocVienAsync(
        SyncExecuteRequest request,
>>>>>>> task5-phase-b3b-guarded-write-path
        CancellationToken cancellationToken = default);
}
