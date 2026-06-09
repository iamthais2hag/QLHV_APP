namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Kết quả an toàn trả về sau khi gọi endpoint thực thi đồng bộ.
/// Chỉ chứa số liệu tóm tắt và trạng thái; KHÔNG chứa dữ liệu nhạy cảm, chuỗi kết nối hay mật khẩu.
/// </summary>
public sealed class SyncExecuteResultDto
{
    /// <summary>Đã thực thi ghi hay bị chặn (do công tắc/ xác nhận).</summary>
    public bool Executed { get; init; }

    /// <summary>Trạng thái: "ThanhCong", "BiChan", "Loi".</summary>
    public string Status { get; init; } = "BiChan";

    /// <summary>Lý do an toàn khi bị chặn hoặc lỗi (không chứa bí mật).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Tóm tắt lần chạy (null nếu bị chặn trước khi chạy).</summary>
    public SyncSummaryDto? Summary { get; init; }
}
