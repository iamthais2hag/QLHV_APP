using QLHV.Application.HocVien.Dtos;
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
        Assert.Equal(10d, HocVienCardLayout.HeaderHeightMm);
        Assert.Equal(40d, HocVienCardLayout.BodyHeightMm);
        Assert.Equal(30d, HocVienCardLayout.PhotoCellWidthMm);
        Assert.Equal(55d, HocVienCardLayout.TextCellWidthMm);
        Assert.Equal(30d, HocVienCardLayout.PhotoWidthMm);
        Assert.Equal(40d, HocVienCardLayout.PhotoHeightMm);
        Assert.Equal(3, HocVienCardLayout.Columns);
        Assert.Equal(4, HocVienCardLayout.Rows);
        Assert.Equal(12, HocVienCardLayout.CardsPerPage);
    }

    [Fact]
    public void Official_template_has_header_and_two_column_body_geometry()
    {
        var template = HocVienCardTemplate.Default;

        Assert.Equal(new CardElementRect(0d, 0d, 85d, 10d), template.HeaderRect);
        Assert.Equal(new CardElementRect(0d, 10d, 30d, 40d), template.PhotoRect);
        Assert.Equal(new CardElementRect(30d, 10d, 55d, 40d), template.BodyTextRect);
        Assert.Equal(HocVienCardLayout.CardHeightMm,
            template.HeaderRect.HeightMm + template.BodyTextRect.HeightMm);
        Assert.Equal(HocVienCardLayout.CardWidthMm,
            template.PhotoRect.WidthMm + template.BodyTextRect.WidthMm);
    }

    [Fact]
    public void Official_header_uses_regular_first_line_and_bold_second_line()
    {
        var lines = HocVienCardTemplate.Default.TextLines.ToDictionary(line => line.Kind);

        Assert.Equal(10d, lines[CardTextKind.OrganizationLine1].PreferredFontSizePt);
        Assert.False(lines[CardTextKind.OrganizationLine1].Bold);
        Assert.Equal(10d, lines[CardTextKind.OrganizationLine2].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.OrganizationLine2].Bold);

        Assert.Equal(13d, lines[CardTextKind.Title].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.Title].Bold);
        Assert.Equal(14d, lines[CardTextKind.StudentName].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.StudentName].Bold);
        Assert.Equal(14d, lines[CardTextKind.TrainingRank].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.TrainingRank].Bold);
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
        var template = HocVienCardTemplate.Default;
        var hocVien = new QLHV.Application.HocVien.Dtos.HocVienListItemDto
        {
            MaDangKy = "66016-20251229-145551540",
            HoVaTen = "NGUYỄN ĐỨC ĐẠT",
            MaHangDT = "Am",
            HangGplxHoc = "Hạng Am",
            TenKhoa = "AK01",
            MaKhoa = "66016K26A0001",
        };
        var content = template.CreateContent(hocVien);
        var renderedText = string.Join('|', template.TextLines.Select(line => content.GetText(line.Kind)));

        Assert.Equal("NGUYỄN ĐỨC ĐẠT", content.StudentName);
        Assert.Equal("SỞ XÂY DỰNG TỈNH GIA LAI", content.OrganizationLine1);
        Assert.Equal("HỌC VIÊN TẬP LÁI XE", content.Title);
        Assert.Equal("TẬP LÁI XE HẠNG: AM", content.TrainingRank);
        Assert.Equal("Times New Roman", template.FontFamily);
        Assert.DoesNotContain(hocVien.MaDangKy, renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain(hocVien.MaKhoa, renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain(hocVien.TenKhoa, renderedText, StringComparison.Ordinal);
        Assert.Equal(
            [
                CardTextKind.OrganizationLine1,
                CardTextKind.OrganizationLine2,
                CardTextKind.Title,
                CardTextKind.StudentName,
                CardTextKind.TrainingRank,
            ],
            template.TextLines.Select(line => line.Kind).ToArray());
    }

    [Theory]
    [InlineData("Hạng Am", null, "TẬP LÁI XE HẠNG: AM")]
    [InlineData("Hang A1m", null, "TẬP LÁI XE HẠNG: A1M")]
    [InlineData("Hạng Hạng B", null, "TẬP LÁI XE HẠNG: B")]
    [InlineData(null, "C", "TẬP LÁI XE HẠNG: C")]
    [InlineData(null, null, "")]
    public void Training_rank_removes_repeated_prefix_and_uses_uppercase(
        string? hangGplxHoc,
        string? maHangDt,
        string expected)
    {
        Assert.Equal(expected, HocVienCardTemplate.FormatTrainingRank(hangGplxHoc, maHangDt));
    }

    [Fact]
    public void Custom_titles_are_trimmed_uppercase_and_limited()
    {
        var longTitle = $"  {new string('a', HocVienCardPrintRequest.MaxTitleLength + 20)}  ";
        var titles = HocVienCardTemplate.Default.ResolveTitles(
            new HocVienCardTitleOptions(longTitle, "  Cơ sở đào tạo  "));

        Assert.Equal(HocVienCardPrintRequest.MaxTitleLength, titles.TitleLine1.Length);
        Assert.Equal(new string('A', HocVienCardPrintRequest.MaxTitleLength), titles.TitleLine1);
        Assert.Equal("CƠ SỞ ĐÀO TẠO", titles.TitleLine2);
    }

    [Fact]
    public void Blank_custom_titles_use_official_defaults()
    {
        var titles = HocVienCardTemplate.Default.ResolveTitles(
            new HocVienCardTitleOptions("  ", null));

        Assert.Equal(HocVienCardTemplate.Default.OrganizationLine1, titles.TitleLine1);
        Assert.Equal(HocVienCardTemplate.Default.OrganizationLine2, titles.TitleLine2);
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
