using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien.Printing;

public sealed class HocVienCardTemplate
{
    public static HocVienCardTemplate Default { get; } = new();

    public string OrganizationLine1 { get; init; } = "SỞ XÂY DỰNG TỈNH GIA LAI";

    public string OrganizationLine2 { get; init; } = "TRUNG TÂM ĐÀO TẠO LÁI XE THÀNH CÔNG";

    public string Title { get; init; } = "HỌC VIÊN TẬP LÁI XE";

    public string FontFamily { get; init; } = "Arial";

    public CardElementRect PhotoRect { get; init; } = new(4d, 5d, 30d, 40d);

    public double TextLeftMm { get; init; } = 37d;

    public double TextRightPaddingMm { get; init; } = 3d;

    public IReadOnlyList<CardTextLine> TextLines { get; init; } =
    [
        new(CardTextKind.OrganizationLine1, 5d, 5.6d, 4.8d, false),
        new(CardTextKind.OrganizationLine2, 9d, 5.6d, 4.8d, false),
        new(CardTextKind.Title, 15d, 8.5d, 7d, true),
        new(CardTextKind.StudentName, 23d, 7.8d, 5.8d, true),
        new(CardTextKind.TrainingRank, 31d, 6.4d, 5d, false),
        new(CardTextKind.Course, 37d, 5.8d, 4.8d, false),
        new(CardTextKind.RegistrationCode, 42d, 5.2d, 4.4d, false),
    ];

    public HocVienCardContent CreateContent(HocVienListItemDto hocVien)
    {
        var trainingRank = FirstValue(hocVien.MaHangDT, hocVien.HangGplxHoc);
        var course = JoinValues(" - ", hocVien.TenKhoa, hocVien.MaKhoa);

        return new HocVienCardContent(
            OrganizationLine1,
            OrganizationLine2,
            Title,
            hocVien.HoVaTen.Trim(),
            string.IsNullOrWhiteSpace(trainingRank) ? string.Empty : $"TẬP LÁI XE HẠNG: {trainingRank}",
            course,
            string.IsNullOrWhiteSpace(hocVien.MaDangKy) ? string.Empty : $"MÃ ĐK: {hocVien.MaDangKy.Trim()}");
    }

    private static string FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string JoinValues(string separator, params string?[] values)
        => string.Join(separator, values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
}

public sealed record HocVienCardContent(
    string OrganizationLine1,
    string OrganizationLine2,
    string Title,
    string StudentName,
    string TrainingRank,
    string Course,
    string RegistrationCode)
{
    public string GetText(CardTextKind kind) => kind switch
    {
        CardTextKind.OrganizationLine1 => OrganizationLine1,
        CardTextKind.OrganizationLine2 => OrganizationLine2,
        CardTextKind.Title => Title,
        CardTextKind.StudentName => StudentName,
        CardTextKind.TrainingRank => TrainingRank,
        CardTextKind.Course => Course,
        CardTextKind.RegistrationCode => RegistrationCode,
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
    double TopMm,
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
    Course,
    RegistrationCode,
}
