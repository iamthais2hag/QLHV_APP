namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Kết quả xử lý cho một bản ghi trong quá trình đồng bộ.
/// </summary>
public sealed class SyncResultDto
{
    /// <summary>Khóa bản ghi (ví dụ MaDK).</summary>
    public string RecordKey { get; init; } = string.Empty;

    /// <summary>Hành động dự kiến/đã thực hiện: "Insert", "Update", "Skip", "Error".</summary>
    public string Action { get; init; } = "Skip";

    /// <summary>Mô tả ngắn gọn, an toàn (không chứa dữ liệu nhạy cảm).</summary>
    public string? Note { get; init; }
}
