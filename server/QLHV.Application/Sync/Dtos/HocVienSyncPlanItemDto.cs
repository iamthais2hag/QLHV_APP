namespace QLHV.Application.Sync.Dtos;

/// <summary>Hành động dự kiến cho một bản ghi khi đồng bộ (chưa thực thi).</summary>
public enum PlannedSyncAction
{
    /// <summary>Sẽ chèn mới (MaDK chưa có ở đích).</summary>
    Insert = 1,

    /// <summary>Sẽ cập nhật (MaDK đã có ở đích).</summary>
    Update = 2,

    /// <summary>Bỏ qua (ví dụ thiếu MaDK hợp lệ).</summary>
    Skip = 3,
}

/// <summary>
/// Một dòng trong kế hoạch đồng bộ (dry-run). Không ghi dữ liệu.
/// </summary>
public sealed class HocVienSyncPlanItemDto
{
    /// <summary>Mã đăng ký.</summary>
    public string MaDK { get; init; } = string.Empty;

    /// <summary>Hành động dự kiến.</summary>
    public PlannedSyncAction Action { get; init; }

    /// <summary>Tên hành động dễ đọc cho người rà soát thủ công.</summary>
    public string ActionName => Action.ToString();

    /// <summary>Cảnh báo chất lượng dữ liệu của bản ghi (nếu có).</summary>
    public IReadOnlyList<HocVienDataWarningDto> Warnings { get; init; } = Array.Empty<HocVienDataWarningDto>();
}
