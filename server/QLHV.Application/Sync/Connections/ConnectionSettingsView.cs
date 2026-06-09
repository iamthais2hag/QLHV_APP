namespace QLHV.Application.Sync.Connections;

/// <summary>
/// Bản hiển thị an toàn của một cấu hình kết nối, dùng cho màn hình
/// "Cấu hình kết nối dữ liệu" (vai trò Admin / Giám đốc trung tâm).
/// KHÔNG bao giờ chứa mật khẩu hay chuỗi kết nối đầy đủ.
/// </summary>
public sealed class ConnectionSettingsView
{
    /// <summary>Khóa cấu hình (ví dụ "CSDT_V2").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Tên hiển thị.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Đã cấu hình hay chưa.</summary>
    public bool IsConfigured { get; init; }

    /// <summary>Giá trị còn ở dạng placeholder.</summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>Đang bật sử dụng hay không.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Mật khẩu luôn được che (ví dụ "********"). Không bao giờ trả về giá trị thật.</summary>
    public string PasswordMasked { get; init; } = "********";

    /// <summary>Thời điểm kiểm tra kết nối gần nhất (nếu có).</summary>
    public DateTime? LastTestedAt { get; init; }

    /// <summary>Kết quả kiểm tra kết nối gần nhất (mô tả ngắn, không chứa bí mật).</summary>
    public string? LastTestResult { get; init; }
}
