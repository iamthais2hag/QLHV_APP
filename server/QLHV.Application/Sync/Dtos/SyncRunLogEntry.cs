namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Bản ghi nhật ký một lần chạy đồng bộ, ánh xạ tới dbo.App_DongBoLog.
/// Không chứa chuỗi kết nối/mật khẩu trong bất kỳ trường nào.
/// </summary>
public sealed class SyncRunLogEntry
{
    public string JobName { get; init; } = "DongBoV2.HocVien";
    public string EntityType { get; init; } = "HocVien";
    public string SourceSystem { get; init; } = "V2";
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public long? DurationMs { get; init; }

    /// <summary>Trạng thái: "DuKien" (dry-run), "ThanhCong", "Loi"...</summary>
    public string Status { get; init; } = "DuKien";

    public int TotalRead { get; init; }
    public int TotalInserted { get; init; }
    public int TotalUpdated { get; init; }
    public int TotalSkipped { get; init; }
    public int TotalError { get; init; }
    public int RetryCount { get; init; }

    /// <summary>Thông điệp lỗi đã làm sạch (không chứa bí mật).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>JSON chi tiết an toàn (ví dụ thống kê cảnh báo).</summary>
    public string? DetailJson { get; init; }

    public string? CreatedBy { get; init; } = "SyncV2";
}
