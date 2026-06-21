namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienCardPrintPreviewItemDto
{
    public int HocVienId { get; init; }

    public string MaDangKy { get; init; } = string.Empty;

    public string HoVaTen { get; init; } = string.Empty;

    public string? MaKhoa { get; init; }

    public string? TenKhoa { get; init; }

    public string? MaHangDT { get; init; }

    public string? HangGplxHoc { get; init; }

    public bool HasPhoto { get; init; }

    public string PhotoStatus { get; init; } = string.Empty;
}
