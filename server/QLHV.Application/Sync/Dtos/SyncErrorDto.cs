namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Mô tả một lỗi xảy ra trong quá trình đồng bộ.
/// KHÔNG chứa dữ liệu nhạy cảm (CCCD, GPLX, mật khẩu, token, chuỗi kết nối).
/// </summary>
public sealed class SyncErrorDto
{
    /// <summary>Mã định danh bản ghi gặp lỗi (ví dụ MaDK), nếu có.</summary>
    public string? RecordKey { get; init; }

    /// <summary>Mã lỗi ngắn gọn để phân loại.</summary>
    public string Code { get; init; } = "UNKNOWN";

    /// <summary>Thông điệp lỗi đã được làm sạch, an toàn để hiển thị/ghi log.</summary>
    public string Message { get; init; } = string.Empty;
}
