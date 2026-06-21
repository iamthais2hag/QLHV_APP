namespace QLHV.Application.HocVien.Printing;

public static class HocVienCardLayout
{
    public const double PageWidthMm = 297d;
    public const double PageHeightMm = 210d;
    public const double CardWidthMm = 85d;
    public const double CardHeightMm = 50d;
    public const double PhotoWidthMm = 30d;
    public const double PhotoHeightMm = 40d;
    public const double GapMm = 1d;
    public const int Columns = 3;
    public const int Rows = 4;
    public const int CardsPerPage = Columns * Rows;

    public static double MarginXMm => (PageWidthMm - (Columns * CardWidthMm + (Columns - 1) * GapMm)) / 2d;

    public static double MarginYMm => (PageHeightMm - (Rows * CardHeightMm + (Rows - 1) * GapMm)) / 2d;

    public static double MmToPoint(double value) => value * 72d / 25.4d;

    public static int GetPageCount(int itemCount)
        => itemCount <= 0 ? 1 : (int)Math.Ceiling(itemCount / (double)CardsPerPage);

    public static CardSlot GetSlot(int indexInPage)
    {
        if (indexInPage < 0 || indexInPage >= CardsPerPage)
        {
            throw new ArgumentOutOfRangeException(nameof(indexInPage));
        }

        var column = indexInPage % Columns;
        var row = indexInPage / Columns;
        return new CardSlot(
            column,
            row,
            MarginXMm + column * (CardWidthMm + GapMm),
            MarginYMm + row * (CardHeightMm + GapMm),
            CardWidthMm,
            CardHeightMm);
    }
}

public sealed record CardSlot(
    int Column,
    int Row,
    double XMm,
    double YMm,
    double WidthMm,
    double HeightMm);
