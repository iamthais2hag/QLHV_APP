namespace QLHV.Application.Sync.Configuration;

/// <summary>
/// Cấu hình đồng bộ một chiều từ CSDT_V2 sang QLHV_APP.
/// Các giá trị này KHÔNG phải bí mật (không chứa chuỗi kết nối/mật khẩu).
/// Chuỗi kết nối được lấy qua <c>IConnectionSettingsProvider</c>, không nằm trong lớp này.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Số bản ghi xử lý mỗi lô.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Thời gian chờ tối đa cho một thao tác (giây).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Mặc định chạy ở chế độ thử (dry-run).</summary>
    public bool DryRunByDefault { get; set; } = true;

    /// <summary>Số lần thử lại tối đa (Polly).</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Độ trễ cơ sở giữa các lần thử lại (giây), dùng cho backoff lũy thừa.</summary>
    public int RetryBaseDelaySeconds { get; set; } = 2;
}
