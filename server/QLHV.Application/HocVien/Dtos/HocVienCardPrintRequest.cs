namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienCardPrintRequest
{
    public const int MaxTitleLength = 100;

    public string? Mode { get; init; }

    public int? HocVienId { get; init; }

    public IReadOnlyList<int>? HocVienIds { get; init; }

    public string? MaKhoa { get; init; }

    public int? GiaoVienId { get; init; }

    public string? MissingPhotoMode { get; init; }

    public string? SortBy { get; init; }

    public string? TitleLine1 { get; init; }

    public string? TitleLine2 { get; init; }

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
        TitleLine1 = NormalizeTitle(TitleLine1),
        TitleLine2 = NormalizeTitle(TitleLine2),
    };

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxTitleLength
            ? trimmed
            : trimmed[..MaxTitleLength];
    }
}
