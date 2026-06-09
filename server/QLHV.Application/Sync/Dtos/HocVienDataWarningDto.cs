namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Cảnh báo chất lượng dữ liệu cho một bản ghi học viên (chỉ cảnh báo, KHÔNG chặn, KHÔNG sửa dữ liệu).
/// Không chứa dữ liệu nhạy cảm dạng tự do; chỉ nêu khóa bản ghi, trường và mã cảnh báo.
/// </summary>
public sealed class HocVienDataWarningDto
{
    /// <summary>Khóa bản ghi (MaDK).</summary>
    public string MaDK { get; init; } = string.Empty;

    /// <summary>Tên trường liên quan (ví dụ "SoCCCD").</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>Mã cảnh báo ngắn gọn để phân loại (ví dụ "CCCD_LENGTH").</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Thông điệp an toàn để hiển thị.</summary>
    public string Message { get; init; } = string.Empty;
}
