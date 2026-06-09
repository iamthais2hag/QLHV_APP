using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>
/// Đọc dữ liệu học viên từ nguồn CSDT_V2 (chỉ đọc, dùng Dapper).
/// Cài đặt cụ thể ở tầng Infrastructure. Phase A chưa mở kết nối thật.
/// </summary>
public interface IV2HocVienSourceRepository
{
    /// <summary>Đếm số học viên ở nguồn V2 (chỉ đọc). Phase B sẽ hiện thực truy vấn thật.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Đọc một lô học viên từ nguồn V2 theo offset/limit (chỉ đọc).</summary>
    Task<IReadOnlyList<V2HocVienSourceRow>> ReadBatchAsync(
        int offset,
        int batchSize,
        CancellationToken cancellationToken = default);
}
