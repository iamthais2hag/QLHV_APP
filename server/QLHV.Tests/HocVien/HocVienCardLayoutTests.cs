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

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(25.4d, 72d)]
    [InlineData(297d, 841.8897637795276d)]
    public void Millimeters_are_converted_to_pdf_points_in_one_place(
        double millimeters,
        double expectedPoints)
    {
        Assert.Equal(expectedPoints, HocVienCardLayout.MmToPoint(millimeters), precision: 8);
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

    [Fact]
    public void Every_card_slot_stays_inside_a4_page()
    {
        for (var index = 0; index < HocVienCardLayout.CardsPerPage; index++)
        {
            var slot = HocVienCardLayout.GetSlot(index);

            Assert.True(slot.XMm >= 0d);
            Assert.True(slot.YMm >= 0d);
            Assert.True(slot.XMm + slot.WidthMm <= HocVienCardLayout.PageWidthMm);
            Assert.True(slot.YMm + slot.HeightMm <= HocVienCardLayout.PageHeightMm);
        }
    }

    [Fact]
    public void Card_template_preserves_vietnamese_student_name_and_business_text()
    {
        var content = HocVienCardTemplate.Default.CreateContent(new QLHV.Application.HocVien.Dtos.HocVienListItemDto
        {
            MaDangKy = "66016-20251229-145551540",
            HoVaTen = "NGUYỄN ĐỨC ĐẠT",
            MaHangDT = "Am",
            TenKhoa = "AK01",
            MaKhoa = "66016K26A0001",
        });

        Assert.Equal("NGUYỄN ĐỨC ĐẠT", content.StudentName);
        Assert.Equal("SỞ XÂY DỰNG TỈNH GIA LAI", content.OrganizationLine1);
        Assert.Equal("HỌC VIÊN TẬP LÁI XE", content.Title);
        Assert.Equal("TẬP LÁI XE HẠNG: Am", content.TrainingRank);
        Assert.Equal("AK01 - 66016K26A0001", content.Course);
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
