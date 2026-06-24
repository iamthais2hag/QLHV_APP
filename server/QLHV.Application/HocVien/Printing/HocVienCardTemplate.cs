using System.Globalization;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien.Printing;

public sealed class HocVienCardTemplate
{
    private const double TextPaddingMm = 1.5d;
    private const string DefaultTrainingRankLabel = "Tập lái xe hạng";

    public static HocVienCardTemplate Default { get; } = new();

    public string OrganizationLine1 { get; init; } = "SỞ XÂY DỰNG TỈNH GIA LAI";

    public string OrganizationLine2 { get; init; } = "TRUNG TÂM ĐÀO TẠO LÁI XE THÀNH CÔNG";

    public string Title { get; init; } = "Học viên tập lái xe";

    public string TrainingRankLabel { get; init; } = DefaultTrainingRankLabel;

    public string FontFamily { get; init; } = "Times New Roman";

    public CardElementRect HeaderRect { get; init; } = new(
        0d,
        0d,
        HocVienCardLayout.CardWidthMm,
        HocVienCardLayout.HeaderHeightMm);

    public CardElementRect HeaderLogoRect { get; init; } = new(
        0.3d,
        0.3d,
        9.4d,
        9.4d);

    public CardElementRect PhotoRect { get; init; } = new(
        0d,
        HocVienCardLayout.HeaderHeightMm,
        HocVienCardLayout.PhotoCellWidthMm,
        HocVienCardLayout.BodyHeightMm);

    public CardElementRect BodyTextRect { get; init; } = new(
        HocVienCardLayout.PhotoCellWidthMm,
        HocVienCardLayout.HeaderHeightMm,
        HocVienCardLayout.TextCellWidthMm,
        HocVienCardLayout.BodyHeightMm);

    public CardElementRect BodyWatermarkRect { get; init; } = new(
        44.5d,
        17d,
        26d,
        26d);

    public CardElementRect ResolveHeaderLogoRect(HocVienCardLogoOptions? options = null)
    {
        var sizeMm = options?.Header.SizeMm ?? HeaderLogoRect.WidthMm;
        var yMm = (HeaderRect.HeightMm - sizeMm) / 2d;
        return new CardElementRect(HeaderLogoRect.XMm, yMm, sizeMm, sizeMm);
    }

    public CardElementRect ResolveBodyWatermarkRect(HocVienCardLogoOptions? options = null)
    {
        var sizeMm = options?.Watermark.SizeMm ?? BodyWatermarkRect.WidthMm;
        return new CardElementRect(
            BodyTextRect.XMm + (BodyTextRect.WidthMm - sizeMm) / 2d,
            BodyTextRect.YMm + (BodyTextRect.HeightMm - sizeMm) / 2d,
            sizeMm,
            sizeMm);
    }

    public IReadOnlyList<string> MissingPhotoPlaceholderLines { get; init; } =
    [
        "Ảnh màu",
        "3 cm x 4 cm",
        "chưa có ảnh",
    ];

    public IReadOnlyList<CardTextLine> TextLines { get; init; } =
    [
        new(
            CardTextKind.OrganizationLine1,
            new(10d, 0d, HocVienCardLayout.CardWidthMm - 10d - TextPaddingMm, 5d),
            10d,
            8.5d,
            false),
        new(
            CardTextKind.OrganizationLine2,
            new(10d, 5d, HocVienCardLayout.CardWidthMm - 10d - TextPaddingMm, 5d),
            10d,
            8.5d,
            false),
        new(
            CardTextKind.Title,
            new(HocVienCardLayout.PhotoCellWidthMm + TextPaddingMm, 14d,
                HocVienCardLayout.TextCellWidthMm - 2d * TextPaddingMm, 9d),
            13d,
            10.5d,
            true),
        new(
            CardTextKind.StudentName,
            new(HocVienCardLayout.PhotoCellWidthMm + TextPaddingMm, 24d,
                HocVienCardLayout.TextCellWidthMm - 2d * TextPaddingMm, 11d),
            14d,
            9d,
            true),
        new(
            CardTextKind.TrainingRank,
            new(HocVienCardLayout.PhotoCellWidthMm + TextPaddingMm, 36d,
                HocVienCardLayout.TextCellWidthMm - 2d * TextPaddingMm, 10d),
            14d,
            9d,
            true),
    ];

    public IReadOnlyList<CardTextLine> ResolveTextLines(HocVienCardTitleOptions? options = null)
    {
        var typography = options?.Typography ?? HocVienCardTypographyOptions.Official;
        var logo = options?.Logo ?? HocVienCardLogoOptions.Official;
        var headerLogoRect = ResolveHeaderLogoRect(logo);
        var headerTextX = logo.Header.Enabled
            ? headerLogoRect.XMm + headerLogoRect.WidthMm + 0.3d
            : TextPaddingMm;
        return TextLines
            .Select(line => line.Kind is CardTextKind.OrganizationLine1
                or CardTextKind.OrganizationLine2
                ? line with
                {
                    Bounds = new CardElementRect(
                        headerTextX,
                        line.Bounds.YMm,
                        HocVienCardLayout.CardWidthMm - headerTextX - TextPaddingMm,
                        line.Bounds.HeightMm),
                }
                : line)
            .Select(line => ApplyStyle(line, typography.Get(line.Kind)))
            .ToArray();
    }

    public HocVienCardTitles ResolveTitles(HocVienCardTitleOptions? options = null)
    {
        var styles = ResolveTextLines(options).ToDictionary(line => line.Kind);
        return new HocVienCardTitles(
            ApplyTextCase(
                ResolveTitle(options?.TitleLine1, OrganizationLine1),
                styles[CardTextKind.OrganizationLine1].TextCase),
            ApplyTextCase(
                ResolveTitle(options?.TitleLine2, OrganizationLine2),
                styles[CardTextKind.OrganizationLine2].TextCase));
    }

    public string ResolveCardTitle(HocVienCardTitleOptions? options = null)
    {
        var style = ResolveTextLines(options).Single(line => line.Kind == CardTextKind.Title);
        return ApplyTextCase(
            ResolveTitle(options?.CardTitle, Title),
            style.TextCase);
    }

    public HocVienCardContent CreateContent(
        HocVienListItemDto hocVien,
        HocVienCardTitleOptions? titleOptions = null)
    {
        var titles = ResolveTitles(titleOptions);
        var styles = ResolveTextLines(titleOptions).ToDictionary(line => line.Kind);

        return new HocVienCardContent(
            titles.TitleLine1,
            titles.TitleLine2,
            ApplyTextCase(
                ResolveTitle(titleOptions?.CardTitle, Title),
                styles[CardTextKind.Title].TextCase),
            ApplyTextCase(hocVien.HoVaTen.Trim(), styles[CardTextKind.StudentName].TextCase),
            FormatTrainingRank(
                hocVien.HangGplxHoc,
                hocVien.MaHangDT,
                styles[CardTextKind.TrainingRank].TextCase,
                titleOptions?.TrainingRankLabel));
    }

    public static string FormatTrainingRank(
        string? hangGplxHoc,
        string? maHangDt,
        HocVienCardTextCase textCase = HocVienCardTextCase.Uppercase,
        string? trainingRankLabel = null)
    {
        var value = FirstValue(hangGplxHoc, maHangDt);
        while (value.Length > 0)
        {
            if (value.StartsWith("Hạng ", StringComparison.OrdinalIgnoreCase))
            {
                value = value[5..].TrimStart();
                continue;
            }

            if (value.StartsWith("Hang ", StringComparison.OrdinalIgnoreCase))
            {
                value = value[5..].TrimStart();
                continue;
            }

            break;
        }

        var label = NormalizeTrainingRankLabel(trainingRankLabel);
        var trainingRank = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"{label}: {value}";
        return ApplyTextCase(trainingRank, textCase);
    }

    private static string NormalizeTrainingRankLabel(string? value)
    {
        var normalized = ResolveTitle(value, DefaultTrainingRankLabel)
            .TrimEnd()
            .TrimEnd(':')
            .TrimEnd();
        return normalized.Length == 0 ? DefaultTrainingRankLabel : normalized;
    }

    private static string ResolveTitle(string? value, string fallback)
    {
        var normalized = (string.IsNullOrWhiteSpace(value) ? fallback : value)
            .Trim();
        if (normalized.Length > HocVienCardPrintRequest.MaxTitleLength)
        {
            normalized = normalized[..HocVienCardPrintRequest.MaxTitleLength];
        }

        return normalized;
    }

    private static CardTextLine ApplyStyle(
        CardTextLine line,
        HocVienCardTextStyleOptions style)
        => line with
        {
            PreferredFontSizePt = style.FontSizePt,
            MinimumFontSizePt = Math.Min(line.MinimumFontSizePt, style.FontSizePt),
            Bold = style.Bold,
            FontFamily = style.FontFamily,
            Italic = style.Italic,
            TextCase = style.TextCase,
        };

    private static string ApplyTextCase(string value, HocVienCardTextCase textCase)
    {
        var culture = CultureInfo.GetCultureInfo("vi-VN");
        return textCase switch
        {
            HocVienCardTextCase.Uppercase => value.ToUpper(culture),
            HocVienCardTextCase.TitleCase => culture.TextInfo.ToTitleCase(value.ToLower(culture)),
            HocVienCardTextCase.Lowercase => value.ToLower(culture),
            _ => value,
        };
    }

    private static string FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

}

public sealed record HocVienCardTitleOptions(
    string? TitleLine1,
    string? TitleLine2,
    HocVienCardTypographyOptions? Typography = null,
    string? CardTitle = null,
    string? TrainingRankLabel = null,
    HocVienCardLogoOptions? Logo = null);

public sealed record HocVienCardLogoPlacementOptions(bool Enabled, double SizeMm);

public sealed record HocVienCardLogoOptions(
    HocVienCardLogoPlacementOptions Header,
    HocVienCardLogoPlacementOptions Watermark)
{
    public static HocVienCardLogoOptions Official { get; } = new(
        new(true, 9.4d),
        new(true, 26d));

    public static HocVienCardLogoOptions FromRequest(HocVienCardLogoSettingsRequest? request)
    {
        var official = Official;
        return new HocVienCardLogoOptions(
            new HocVienCardLogoPlacementOptions(
                request?.Header?.Enabled ?? official.Header.Enabled,
                request?.Header?.SizeMm ?? official.Header.SizeMm),
            new HocVienCardLogoPlacementOptions(
                request?.Watermark?.Enabled ?? official.Watermark.Enabled,
                request?.Watermark?.SizeMm ?? official.Watermark.SizeMm));
    }
}

public sealed record HocVienCardTextStyleOptions(
    string FontFamily,
    double FontSizePt,
    bool Bold,
    HocVienCardTextCase TextCase,
    bool Italic);

public sealed record HocVienCardTypographyOptions(
    HocVienCardTextStyleOptions OrganizationLine1,
    HocVienCardTextStyleOptions OrganizationLine2,
    HocVienCardTextStyleOptions CardTitle,
    HocVienCardTextStyleOptions StudentName,
    HocVienCardTextStyleOptions TrainingRank)
{
    public static HocVienCardTypographyOptions Official { get; } = new(
        new("Times New Roman", 10d, false, HocVienCardTextCase.Uppercase, false),
        new("Times New Roman", 10d, false, HocVienCardTextCase.Uppercase, false),
        new("Times New Roman", 13d, true, HocVienCardTextCase.Uppercase, false),
        new("Times New Roman", 14d, true, HocVienCardTextCase.Uppercase, false),
        new("Times New Roman", 14d, true, HocVienCardTextCase.Uppercase, false));

    public static HocVienCardTypographyOptions FromRequest(HocVienCardTypographyRequest? request)
    {
        var official = Official;
        return new HocVienCardTypographyOptions(
            Merge(request?.OrganizationLine1, official.OrganizationLine1),
            Merge(request?.OrganizationLine2, official.OrganizationLine2),
            Merge(request?.CardTitle, official.CardTitle),
            Merge(request?.StudentName, official.StudentName),
            Merge(request?.TrainingRank, official.TrainingRank));
    }

    public HocVienCardTextStyleOptions Get(CardTextKind kind) => kind switch
    {
        CardTextKind.OrganizationLine1 => OrganizationLine1,
        CardTextKind.OrganizationLine2 => OrganizationLine2,
        CardTextKind.Title => CardTitle,
        CardTextKind.StudentName => StudentName,
        CardTextKind.TrainingRank => TrainingRank,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static HocVienCardTextStyleOptions Merge(
        HocVienCardTextStyleRequest? request,
        HocVienCardTextStyleOptions fallback)
        => new(
            request?.FontFamily ?? fallback.FontFamily,
            request?.FontSizePt ?? fallback.FontSizePt,
            request?.Bold ?? fallback.Bold,
            ParseTextCase(request?.TextCase) ?? fallback.TextCase,
            request?.Italic ?? fallback.Italic);

    private static HocVienCardTextCase? ParseTextCase(string? value) => value switch
    {
        "original" => HocVienCardTextCase.Original,
        "uppercase" => HocVienCardTextCase.Uppercase,
        "titleCase" => HocVienCardTextCase.TitleCase,
        "lowercase" => HocVienCardTextCase.Lowercase,
        _ => null,
    };
}

public sealed record HocVienCardTitles(
    string TitleLine1,
    string TitleLine2);

public sealed record HocVienCardContent(
    string OrganizationLine1,
    string OrganizationLine2,
    string Title,
    string StudentName,
    string TrainingRank)
{
    public string GetText(CardTextKind kind) => kind switch
    {
        CardTextKind.OrganizationLine1 => OrganizationLine1,
        CardTextKind.OrganizationLine2 => OrganizationLine2,
        CardTextKind.Title => Title,
        CardTextKind.StudentName => StudentName,
        CardTextKind.TrainingRank => TrainingRank,
        _ => string.Empty,
    };
}

public sealed record CardElementRect(
    double XMm,
    double YMm,
    double WidthMm,
    double HeightMm);

public sealed record CardTextLine(
    CardTextKind Kind,
    CardElementRect Bounds,
    double PreferredFontSizePt,
    double MinimumFontSizePt,
    bool Bold,
    string FontFamily = "Times New Roman",
    bool Italic = false,
    HocVienCardTextCase TextCase = HocVienCardTextCase.Uppercase);

public enum HocVienCardTextCase
{
    Original,
    Uppercase,
    TitleCase,
    Lowercase,
}

public enum CardTextKind
{
    OrganizationLine1,
    OrganizationLine2,
    Title,
    StudentName,
    TrainingRank,
}
