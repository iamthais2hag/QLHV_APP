namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Yêu cầu thực thi đồng bộ ghi (manual). Bắt buộc xác nhận rõ ràng để tránh chạy nhầm.
/// </summary>
public sealed class SyncExecuteRequest
{
    /// <summary>
    /// Phải đặt true một cách rõ ràng. Mặc định false để Swagger "Try it out" không vô tình chạy ghi.
    /// </summary>
    public bool Confirm { get; set; } = false;

    /// <summary>
    /// Chuỗi xác nhận phải khớp với cấu hình SyncExecution.ConfirmationPhrase.
    /// </summary>
    public string? ConfirmationPhrase { get; set; }
}
