using QLHV.Application.HocVien.Printing;

namespace QLHV.Tests.HocVien;

public sealed class HocVienCardLayoutTests
{
    [Fact]
    public void A4_landscape_layout_has_twelve_cards_per_page()
    {
        Assert.Equal(297d, HocVienCardLayout.PageWidthMm);
        Assert.Equal(210d, HocVienCardLayout.PageHeightMm);
        Assert.Equal(85d, HocVienCardLayout.CardWidthMm);
        Assert.Equal(50d, HocVienCardLayout.CardHeightMm);
        Assert.Equal(30d, HocVienCardLayout.PhotoWidthMm);
        Assert.Equal(40d, HocVienCardLayout.PhotoHeightMm);
        Assert.Equal(3, HocVienCardLayout.Columns);
        Assert.Equal(4, HocVienCardLayout.Rows);
        Assert.Equal(12, HocVienCardLayout.CardsPerPage);
    }

    [Fact]
    public void A4_layout_uses_expected_margins_and_slots()
    {
        Assert.Equal(20d, HocVienCardLayout.MarginXMm);
        Assert.Equal(3.5d, HocVienCardLayout.MarginYMm);

        var first = HocVienCardLayout.GetSlot(0);
        Assert.Equal(0, first.Column);
        Assert.Equal(0, first.Row);
        Assert.Equal(20d, first.XMm);
        Assert.Equal(3.5d, first.YMm);

        var last = HocVienCardLayout.GetSlot(11);
        Assert.Equal(2, last.Column);
        Assert.Equal(3, last.Row);
        Assert.Equal(192d, last.XMm);
        Assert.Equal(156.5d, last.YMm);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    public void Page_count_uses_twelve_cards_per_page(int count, int expectedPages)
    {
        Assert.Equal(expectedPages, HocVienCardLayout.GetPageCount(count));
    }
}
