namespace QLHV.Application.Sync.Configuration;

/// <summary>
/// Công tắc an toàn cho việc THỰC THI ghi đồng bộ. Mặc định tất cả ở trạng thái an toàn nhất.
/// Đây không phải bí mật; không chứa chuỗi kết nối/mật khẩu.
/// Bind từ section "SyncExecution".
/// </summary>
public sealed class SyncExecutionOptions
{
    public const string SectionName = "SyncExecution";

    /// <summary>Cho phép GHI vào QLHV_APP. Mặc định FALSE (chặn mọi ghi).</summary>
    public bool EnableTargetWrites { get; set; } = false;

    /// <summary>Bắt buộc xác nhận thủ công khi chạy endpoint thực thi. Mặc định TRUE.</summary>
    public bool RequireManualConfirmation { get; set; } = true;

    /// <summary>Cho phép lập lịch Hangfire recurring. Mặc định FALSE (Phase B4 trở đi).</summary>
    public bool AllowHangfireSchedule { get; set; } = false;

    /// <summary>
    /// Chuỗi xác nhận thủ công bắt buộc phải khớp khi gọi endpoint thực thi
    /// (chống chạy nhầm từ Swagger). Không phải bí mật.
    /// </summary>
    public string ConfirmationPhrase { get; set; } = "DONG-Y-GHI-QLHV_APP";
}
