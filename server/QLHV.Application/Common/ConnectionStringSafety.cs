namespace QLHV.Application.Common;

/// <summary>
/// Tiện ích an toàn cho chuỗi kết nối: phát hiện giá trị placeholder và che giấu giá trị.
/// KHÔNG bao giờ trả về hoặc ghi log chuỗi kết nối/mật khẩu thật.
/// </summary>
public static class ConnectionStringSafety
{
    private static readonly string[] PlaceholderMarkers =
    {
        "__", "<", ">", "changeme", "your_", "placeholder", "example", "todo",
    };

    /// <summary>Trả về true nếu chuỗi kết nối rỗng hoặc còn ở dạng placeholder.</summary>
    public static bool IsNullOrPlaceholder(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        var lower = connectionString.ToLowerInvariant();
        foreach (var marker in PlaceholderMarkers)
        {
            if (lower.Contains(marker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Trả về chuỗi đã che giấu hoàn toàn để hiển thị/ghi log an toàn.
    /// Không tiết lộ server, database, user hay password.
    /// </summary>
    public static string Mask(string? connectionString)
        => string.IsNullOrWhiteSpace(connectionString) ? "(chưa cấu hình)" : "********";
}
