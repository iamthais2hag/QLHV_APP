namespace QLHV.Application.Sync;

/// <summary>
/// Đọc dữ liệu học viên từ hệ nguồn CSDT_V2 (chỉ đọc, dùng Dapper).
/// Cài đặt nằm ở tầng Infrastructure. KHÔNG được gọi trong Phase A.
/// </summary>
public interface IV2HocVienReadRepository
{
    /// <summary>Đếm số học viên ở nguồn V2 (dùng cho lập kế hoạch lô ở Phase B).</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
