using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Nghiệp vụ đồng bộ học viên một chiều từ CSDT_V2 sang QLHV_APP.
/// Phase A chỉ hỗ trợ dry-run (không ghi dữ liệu).
/// </summary>
public interface IHocVienSyncService
{
    /// <summary>
    /// Chạy thử: kiểm tra cấu hình/kết nối, dựng kế hoạch ánh xạ và trả về tóm tắt an toàn.
    /// KHÔNG mở kết nối thật, KHÔNG ghi dữ liệu.
    /// </summary>
    Task<DryRunResultDto> DryRunHocVienAsync(CancellationToken cancellationToken = default);
}
