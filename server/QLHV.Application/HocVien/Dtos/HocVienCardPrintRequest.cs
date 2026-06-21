namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienCardPrintRequest
{
    public string? Mode { get; init; }

    public int? HocVienId { get; init; }

    public IReadOnlyList<int>? HocVienIds { get; init; }

    public string? MaKhoa { get; init; }

    public int? GiaoVienId { get; init; }

    public string? MissingPhotoMode { get; init; }

    public string? SortBy { get; init; }

    public HocVienCardPrintRequest Normalized() => new()
    {
        Mode = string.IsNullOrWhiteSpace(Mode) ? null : Mode.Trim(),
        HocVienId = HocVienId,
        HocVienIds = HocVienIds?
            .Where(id => id > 0)
            .Distinct()
            .ToArray(),
        MaKhoa = string.IsNullOrWhiteSpace(MaKhoa) ? null : MaKhoa.Trim(),
        GiaoVienId = GiaoVienId,
        MissingPhotoMode = string.IsNullOrWhiteSpace(MissingPhotoMode)
            ? "placeholder"
            : MissingPhotoMode.Trim(),
        SortBy = string.IsNullOrWhiteSpace(SortBy) ? "current" : SortBy.Trim(),
    };
}
