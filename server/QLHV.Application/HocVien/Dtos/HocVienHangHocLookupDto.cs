namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienHangHocLookupDto
{
    public string MaHangDT { get; set; } = string.Empty;
    public string? TenHangDT { get; set; }
    public string? HangGplxHoc { get; set; }
    public string Label { get; set; } = string.Empty;
}
