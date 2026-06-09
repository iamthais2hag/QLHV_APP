namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Kết quả kiểm tra một kết nối cho mục đích dry-run.
/// Chỉ chứa trạng thái và thông điệp an toàn, KHÔNG chứa chuỗi kết nối/mật khẩu.
/// </summary>
public sealed class ConnectionCheckDto
{
    /// <summary>Tên kết nối (ví dụ "QLHV_APP", "CSDT_V2").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Đã cấu hình hay chưa.</summary>
    public bool IsConfigured { get; init; }

    /// <summary>Còn ở dạng placeholder hay không.</summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>Có thể dùng để chạy đồng bộ thật hay không.</summary>
    public bool IsUsable { get; init; }

    /// <summary>Thông điệp an toàn để hiển thị.</summary>
    public string Message { get; init; } = string.Empty;
}
