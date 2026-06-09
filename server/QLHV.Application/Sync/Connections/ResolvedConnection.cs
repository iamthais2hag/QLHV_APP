using System.Text.Json.Serialization;
using QLHV.Application.Common;

namespace QLHV.Application.Sync.Connections;

/// <summary>
/// Kết quả phân giải một kết nối từ <see cref="IConnectionSettingsProvider"/>.
/// Chuỗi kết nối thật chỉ dùng nội bộ ở backend, KHÔNG bao giờ trả ra frontend hay ghi log.
/// </summary>
public sealed class ResolvedConnection
{
    /// <summary>Tên hiển thị (ví dụ "QLHV_APP", "CSDT_V2").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Đã có cấu hình kết nối hay chưa.</summary>
    public bool IsConfigured { get; init; }

    /// <summary>Giá trị hiện tại còn ở dạng placeholder (chưa dùng được thật).</summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>
    /// Chuỗi kết nối thật - chỉ dành cho tầng Infrastructure khi mở kết nối.
    /// Được đánh dấu bỏ qua khi serialize JSON để không lộ ra frontend.
    /// </summary>
    [JsonIgnore]
    public string? ConnectionString { get; init; }

    /// <summary>Có thể dùng để kết nối thật hay không (đã cấu hình và không phải placeholder).</summary>
    [JsonIgnore]
    public bool IsUsable => IsConfigured && !IsPlaceholder && !string.IsNullOrWhiteSpace(ConnectionString);

    public static ResolvedConnection NotConfigured(string name) => new()
    {
        Name = name,
        IsConfigured = false,
        IsPlaceholder = false,
        ConnectionString = null,
    };

    public static ResolvedConnection FromConfiguration(string name, string? connectionString) => new()
    {
        Name = name,
        IsConfigured = !string.IsNullOrWhiteSpace(connectionString),
        IsPlaceholder = ConnectionStringSafety.IsNullOrPlaceholder(connectionString),
        ConnectionString = connectionString,
    };
}
