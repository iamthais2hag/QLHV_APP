namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienPhotoAuditRequest
{
    public string? MaKhoa { get; init; }

    public string? MaHangDT { get; init; }

    public string? Keyword { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public bool ValidateDecode { get; init; }

    public bool OnlyMissing { get; init; }

    public bool OnlyInvalid { get; init; }

    public HocVienPhotoAuditRequest Normalized() => new()
    {
        MaKhoa = string.IsNullOrWhiteSpace(MaKhoa) ? null : MaKhoa.Trim(),
        MaHangDT = string.IsNullOrWhiteSpace(MaHangDT) ? null : MaHangDT.Trim(),
        Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize < 1 ? 20 : Math.Min(PageSize, 100),
        ValidateDecode = ValidateDecode,
        OnlyMissing = OnlyMissing,
        OnlyInvalid = OnlyInvalid,
    };
}
