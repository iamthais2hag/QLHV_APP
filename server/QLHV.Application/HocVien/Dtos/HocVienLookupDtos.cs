namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienKhoaLookupDto
{
    public string MaKhoa { get; init; } = string.Empty;
    public string? TenKhoa { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class HocVienHangHocLookupDto
{
    public string MaHangDT { get; init; } = string.Empty;
    public string? TenHangDT { get; init; }
    public string Label { get; init; } = string.Empty;
}
