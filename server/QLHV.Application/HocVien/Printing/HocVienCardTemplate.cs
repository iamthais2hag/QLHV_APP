using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien.Printing;

public sealed class HocVienCardTemplate
{
    private const double TextPaddingMm = 1.5d;

    public static HocVienCardTemplate Default { get; } = new();

    public string OrganizationLine1 { get; init; } = "SỞ XÂY DỰNG TỈNH GIA LAI";

    public string OrganizationLine2 { get; init; } = "TRUNG TÂM ĐÀO TẠO LÁI XE THÀNH CÔNG";

    public string Title { get; init; } = "HỌC VIÊN TẬP LÁI XE";

    public string FontFamily { get; init; } = "Times New Roman";

    public CardElementRect HeaderRect { get; init; } = new(
        0d,
        0d,
        HocVienCardLayout.CardWidthMm,
        HocVienCardLayout.HeaderHeightMm);

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
            new(TextPaddingMm, 0d, HocVienCardLayout.CardWidthMm - 2d * TextPaddingMm, 5d),
            10d,
            8.5d,
            false),
        new(
            CardTextKind.OrganizationLine2,
            new(TextPaddingMm, 5d, HocVienCardLayout.CardWidthMm - 2d * TextPaddingMm, 5d),
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
            false),
    ];

    public HocVienCardContent CreateContent(HocVienListItemDto hocVien)
    {
        var trainingRank = FirstValue(hocVien.HangGplxHoc, hocVien.MaHangDT);

        return new HocVienCardContent(
            OrganizationLine1.Trim().ToUpperInvariant(),
            OrganizationLine2.Trim().ToUpperInvariant(),
            Title.Trim().ToUpperInvariant(),
            hocVien.HoVaTen.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(trainingRank) ? string.Empty : $"Tập lái xe hạng: {trainingRank}");
    }

    private static string FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

}

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
    bool Bold);

public enum CardTextKind
{
    OrganizationLine1,
    OrganizationLine2,
    Title,
    StudentName,
    TrainingRank,
}
