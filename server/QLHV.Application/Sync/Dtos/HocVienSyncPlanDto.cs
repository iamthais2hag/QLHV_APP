namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Kế hoạch đồng bộ học viên (dry-run, CHƯA thực thi).
/// Cho biết số dòng đọc được, số dự kiến chèn/cập nhật/bỏ qua, cảnh báo dữ liệu và lỗi.
/// Tuyệt đối không ghi App_HocVien hay App_DongBoLog khi tạo kế hoạch.
/// </summary>
public sealed class HocVienSyncPlanDto
{
    /// <summary>Luôn true ở Phase B3A.</summary>
    public bool IsDryRun { get; init; } = true;
    public string SourceProfileCode { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;

    /// <summary>Số dòng đọc được từ nguồn V2.</summary>
    public int SourceRowsRead { get; init; }

    /// <summary>Số dòng dự kiến chèn mới.</summary>
    public int PlannedInsert { get; init; }

    /// <summary>Số dòng dự kiến cập nhật.</summary>
    public int PlannedUpdate { get; init; }

    /// <summary>Số dòng dự kiến bỏ qua.</summary>
    public int PlannedSkip { get; init; }

    /// <summary>Tổng số cảnh báo chất lượng dữ liệu.</summary>
    public int WarningCount { get; init; }

    /// <summary>Danh sách cảnh báo dữ liệu (có thể giới hạn số lượng khi hiển thị).</summary>
    public IReadOnlyList<HocVienDataWarningDto> Warnings { get; init; } = Array.Empty<HocVienDataWarningDto>();

    /// <summary>Danh sách lỗi xử lý (an toàn, không chứa bí mật).</summary>
    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();

    /// <summary>Một số mục kế hoạch tiêu biểu (preview).</summary>
    public IReadOnlyList<HocVienSyncPlanItemDto> Items { get; init; } = Array.Empty<HocVienSyncPlanItemDto>();
}
