using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Đọc dữ liệu học viên từ nguồn CSDT_V2 (CHỈ ĐỌC, dùng Dapper).
/// Tuyệt đối không ghi vào bất kỳ database nào. Kết nối được phân giải qua
/// <c>IConnectionSettingsProvider</c>; nếu nguồn chưa cấu hình/đang là placeholder thì
/// các phương thức sẽ ném lỗi (người gọi phải kiểm tra cấu hình trước - xem dry-run).
/// </summary>
public interface IV2HocVienSourceRepository
{
    /// <summary>Đếm số học viên ở nguồn V2 theo bộ lọc (chỉ đọc - SELECT COUNT).</summary>
    Task<int> CountAsync(
        HocVienSourceFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc một trang học viên từ nguồn V2 theo bộ lọc và phân trang (chỉ đọc - SELECT ... OFFSET/FETCH).
    /// </summary>
    Task<IReadOnlyList<V2HocVienSourceRow>> ReadPageAsync(
        HocVienSourceFilter filter,
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default);
}
