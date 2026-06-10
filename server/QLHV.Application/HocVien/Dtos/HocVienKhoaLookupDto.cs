namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienKhoaLookupDto
{
    public string MaKhoa { get; set; } = string.Empty;
    public string? TenKhoa { get; set; }
    public string Label { get; set; } = string.Empty;
}
