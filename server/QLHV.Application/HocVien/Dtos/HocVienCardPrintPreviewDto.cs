namespace QLHV.Application.HocVien.Dtos;

public sealed class HocVienCardPrintPreviewDto
{
    public int TotalStudents { get; init; }

    public int TotalPages { get; init; }

    public int CardsPerPage { get; init; }

    public string LayoutName { get; init; } = "A4 ngang 3x4";

    public int MissingPhotoCount { get; init; }

    public IReadOnlyList<HocVienCardPrintPreviewItemDto> Items { get; init; } = [];
}
