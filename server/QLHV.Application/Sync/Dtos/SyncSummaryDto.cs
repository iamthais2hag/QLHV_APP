namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Tóm tắt một lần chạy đồng bộ. Các trường đếm khớp với bảng dbo.App_DongBoLog.
/// </summary>
public sealed class SyncSummaryDto
{
    /// <summary>Tên job (ví dụ "DongBoV2.HocVien").</summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>Loại đối tượng đồng bộ (ví dụ "HocVien").</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Hệ nguồn (mặc định "V2").</summary>
    public string SourceSystem { get; init; } = "V2";

    /// <summary>Đây có phải lần chạy thử (dry-run) hay không.</summary>
    public bool IsDryRun { get; init; }

    /// <summary>Trạng thái: ví dụ "DuKien", "ThanhCong", "Loi", "ThieuCauHinh".</summary>
    public string Status { get; init; } = "DuKien";

    public int TotalRead { get; init; }
    public int TotalInserted { get; init; }
    public int TotalUpdated { get; init; }
    public int TotalSkipped { get; init; }
    public int TotalError { get; init; }
    public int RetryCount { get; init; }

    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public long? DurationMs { get; init; }

    /// <summary>Danh sách lỗi (nếu có).</summary>
    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();
}
