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

    public string? CardTitle { get; init; }

    public string? TrainingRankLabel { get; init; }

    public HocVienCardTypographyRequest? Typography { get; init; }

    public HocVienCardLogoSettingsRequest? Logo { get; init; }

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
        CardTitle = NormalizeTitle(CardTitle),
        TrainingRankLabel = NormalizeTitle(TrainingRankLabel),
        Typography = Typography?.Normalized(),
        Logo = Logo?.Normalized(),
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

public sealed class HocVienCardLogoSettingsRequest
{
    public const double MinimumHeaderSizeMm = 3d;
    public const double MaximumHeaderSizeMm = 9.4d;
    public const double MinimumWatermarkSizeMm = 8d;
    public const double MaximumWatermarkSizeMm = 38d;

    public HocVienCardLogoPlacementRequest? Header { get; init; }

    public HocVienCardLogoPlacementRequest? Watermark { get; init; }

    public HocVienCardLogoSettingsRequest Normalized() => new()
    {
        Header = NormalizePlacement(Header, MinimumHeaderSizeMm, MaximumHeaderSizeMm),
        Watermark = NormalizePlacement(Watermark, MinimumWatermarkSizeMm, MaximumWatermarkSizeMm),
    };

    private static HocVienCardLogoPlacementRequest? NormalizePlacement(
        HocVienCardLogoPlacementRequest? value,
        double minimumSizeMm,
        double maximumSizeMm)
    {
        if (value is null)
        {
            return null;
        }

        double? sizeMm = value.SizeMm is { } size && double.IsFinite(size)
            ? Math.Clamp(size, minimumSizeMm, maximumSizeMm)
            : null;
        return new HocVienCardLogoPlacementRequest
        {
            Enabled = value.Enabled,
            SizeMm = sizeMm,
        };
    }
}

public sealed class HocVienCardLogoPlacementRequest
{
    public bool? Enabled { get; init; }

    public double? SizeMm { get; init; }
}

public sealed class HocVienCardTypographyRequest
{
    public HocVienCardTextStyleRequest? OrganizationLine1 { get; init; }

    public HocVienCardTextStyleRequest? OrganizationLine2 { get; init; }

    public HocVienCardTextStyleRequest? CardTitle { get; init; }

    public HocVienCardTextStyleRequest? StudentName { get; init; }

    public HocVienCardTextStyleRequest? TrainingRank { get; init; }

    public HocVienCardTypographyRequest Normalized() => new()
    {
        OrganizationLine1 = OrganizationLine1?.Normalized(),
        OrganizationLine2 = OrganizationLine2?.Normalized(),
        CardTitle = CardTitle?.Normalized(),
        StudentName = StudentName?.Normalized(),
        TrainingRank = TrainingRank?.Normalized(),
    };
}

public sealed class HocVienCardTextStyleRequest
{
    public const double MinimumFontSizePt = 6d;
    public const double MaximumFontSizePt = 24d;

    private static readonly string[] AllowedFontFamilies =
    [
        "Times New Roman",
        "Arial",
        "Tahoma",
    ];

    public string? FontFamily { get; init; }

    public double? FontSizePt { get; init; }

    public bool? Bold { get; init; }

    public bool? Uppercase { get; init; }

    public string? TextCase { get; init; }

    public bool? Italic { get; init; }

    public HocVienCardTextStyleRequest Normalized()
    {
        var fontFamily = AllowedFontFamilies.FirstOrDefault(
            candidate => string.Equals(candidate, FontFamily?.Trim(), StringComparison.OrdinalIgnoreCase));
        double? fontSize = FontSizePt is { } value && double.IsFinite(value)
            ? Math.Clamp(value, MinimumFontSizePt, MaximumFontSizePt)
            : null;

        return new HocVienCardTextStyleRequest
        {
            FontFamily = fontFamily,
            FontSizePt = fontSize,
            Bold = Bold,
            Uppercase = Uppercase,
            TextCase = NormalizeTextCase(TextCase, Uppercase),
            Italic = Italic,
        };
    }

    private static string? NormalizeTextCase(string? value, bool? legacyUppercase)
    {
        var normalized = value?.Trim().ToLowerInvariant() switch
        {
            "original" => "original",
            "uppercase" => "uppercase",
            "titlecase" => "titleCase",
            "lowercase" => "lowercase",
            _ => null,
        };

        return normalized ?? legacyUppercase switch
        {
            true => "uppercase",
            false => "original",
            null => null,
        };
    }
}
