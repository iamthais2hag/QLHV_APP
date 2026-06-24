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
        Assert.Equal(new CardElementRect(0.3d, 0.3d, 9.4d, 9.4d), template.HeaderLogoRect);
        Assert.Equal(new CardElementRect(0d, 10d, 30d, 40d), template.PhotoRect);
        Assert.Equal(new CardElementRect(30d, 10d, 55d, 40d), template.BodyTextRect);
        Assert.Equal(new CardElementRect(44.5d, 17d, 26d, 26d), template.BodyWatermarkRect);
        Assert.Equal(HocVienCardLayout.CardHeightMm,
            template.HeaderRect.HeightMm + template.BodyTextRect.HeightMm);
        Assert.Equal(HocVienCardLayout.CardWidthMm,
            template.PhotoRect.WidthMm + template.BodyTextRect.WidthMm);
        Assert.True(template.HeaderLogoRect.XMm + template.HeaderLogoRect.WidthMm <= 10d);
        Assert.Equal(
            0.3d,
            template.HeaderRect.HeightMm
                - template.HeaderLogoRect.YMm
                - template.HeaderLogoRect.HeightMm,
            precision: 8);
        Assert.True(template.BodyWatermarkRect.XMm >= template.BodyTextRect.XMm);
        Assert.True(
            template.BodyWatermarkRect.XMm + template.BodyWatermarkRect.WidthMm
            <= template.BodyTextRect.XMm + template.BodyTextRect.WidthMm);
    }

    [Fact]
    public void Official_header_is_regular_ten_point_and_body_text_is_bold()
    {
        var lines = HocVienCardTemplate.Default.TextLines.ToDictionary(line => line.Kind);

        Assert.Equal(10d, lines[CardTextKind.OrganizationLine1].PreferredFontSizePt);
        Assert.False(lines[CardTextKind.OrganizationLine1].Bold);
        Assert.Equal(10d, lines[CardTextKind.OrganizationLine2].PreferredFontSizePt);
        Assert.False(lines[CardTextKind.OrganizationLine2].Bold);

        Assert.Equal(13d, lines[CardTextKind.Title].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.Title].Bold);
        Assert.Equal(14d, lines[CardTextKind.StudentName].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.StudentName].Bold);
        Assert.Equal(14d, lines[CardTextKind.TrainingRank].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.TrainingRank].Bold);
    }

    [Fact]
    public void Logo_settings_are_clamped_and_resolved_inside_existing_card_cells()
    {
        var normalized = new HocVienCardPrintRequest
        {
            Logo = new HocVienCardLogoSettingsRequest
            {
                Header = new HocVienCardLogoPlacementRequest
                {
                    Enabled = false,
                    SizeMm = 99d,
                },
                Watermark = new HocVienCardLogoPlacementRequest
                {
                    Enabled = true,
                    SizeMm = 1d,
                },
            },
        }.Normalized();
        var options = HocVienCardLogoOptions.FromRequest(normalized.Logo);

        Assert.False(options.Header.Enabled);
        Assert.Equal(9.4d, options.Header.SizeMm);
        Assert.True(options.Watermark.Enabled);
        Assert.Equal(8d, options.Watermark.SizeMm);

        var custom = new HocVienCardLogoOptions(
            new(true, 5d),
            new(true, 38d));
        Assert.Equal(
            new CardElementRect(0.3d, 2.5d, 5d, 5d),
            HocVienCardTemplate.Default.ResolveHeaderLogoRect(custom));
        Assert.Equal(
            new CardElementRect(38.5d, 11d, 38d, 38d),
            HocVienCardTemplate.Default.ResolveBodyWatermarkRect(custom));
    }

    [Fact]
    public void Custom_typography_controls_font_size_weight_case_and_italic()
    {
        var normalized = new HocVienCardPrintRequest
        {
            Typography = new HocVienCardTypographyRequest
            {
                OrganizationLine1 = new HocVienCardTextStyleRequest
                {
                    FontFamily = "Arial",
                    FontSizePt = 12d,
                    Bold = true,
                    Uppercase = false,
                    Italic = true,
                },
                TrainingRank = new HocVienCardTextStyleRequest
                {
                    Uppercase = false,
                    Bold = false,
                },
            },
        }.Normalized();
        var options = new HocVienCardTitleOptions(
            "Cơ quan quản lý",
            null,
            HocVienCardTypographyOptions.FromRequest(normalized.Typography));
        var lines = HocVienCardTemplate.Default.ResolveTextLines(options)
            .ToDictionary(line => line.Kind);
        var content = HocVienCardTemplate.Default.CreateContent(new HocVienListItemDto
        {
            HoVaTen = "Nguyễn Đức Đạt",
            HangGplxHoc = "Hạng Am",
        }, options);

        Assert.Equal("Arial", lines[CardTextKind.OrganizationLine1].FontFamily);
        Assert.Equal(12d, lines[CardTextKind.OrganizationLine1].PreferredFontSizePt);
        Assert.True(lines[CardTextKind.OrganizationLine1].Bold);
        Assert.True(lines[CardTextKind.OrganizationLine1].Italic);
        Assert.Equal(HocVienCardTextCase.Original, lines[CardTextKind.OrganizationLine1].TextCase);
        Assert.Equal("Cơ quan quản lý", content.OrganizationLine1);
        Assert.Equal("Tập lái xe hạng: Am", content.TrainingRank);
        Assert.False(lines[CardTextKind.TrainingRank].Bold);
    }

    [Fact]
    public void Typography_request_rejects_unknown_font_and_clamps_font_size()
    {
        var normalized = new HocVienCardTextStyleRequest
        {
            FontFamily = "Unknown Font",
            FontSizePt = 99d,
        }.Normalized();
        var options = HocVienCardTypographyOptions.FromRequest(new HocVienCardTypographyRequest
        {
            CardTitle = normalized,
        });

        Assert.Null(normalized.FontFamily);
        Assert.Equal(HocVienCardTextStyleRequest.MaximumFontSizePt, normalized.FontSizePt);
        Assert.Equal("Times New Roman", options.CardTitle.FontFamily);
        Assert.Equal(HocVienCardTextStyleRequest.MaximumFontSizePt, options.CardTitle.FontSizePt);
    }

    [Fact]
    public void Title_case_formats_vietnamese_training_rank_as_requested()
    {
        Assert.Equal(
            "Tập Lái Xe Hạng: Am",
            HocVienCardTemplate.FormatTrainingRank(
                "Hạng Am",
                null,
                HocVienCardTextCase.TitleCase,
                "Tập Lái Xe Hạng"));
    }

    [Fact]
    public void Card_title_and_training_rank_label_can_be_customized_without_replacing_student_data()
    {
        var typography = HocVienCardTypographyOptions.Official with
        {
            CardTitle = HocVienCardTypographyOptions.Official.CardTitle with
            {
                TextCase = HocVienCardTextCase.Original,
            },
            TrainingRank = HocVienCardTypographyOptions.Official.TrainingRank with
            {
                TextCase = HocVienCardTextCase.Original,
            },
        };
        var options = new HocVienCardTitleOptions(
            null,
            null,
            typography,
            "Giáo viên dạy lái xe",
            "Đào tạo hạng:");

        var content = HocVienCardTemplate.Default.CreateContent(new HocVienListItemDto
        {
            HoVaTen = "Nguyễn Đức Đạt",
            HangGplxHoc = "Hạng Am",
        }, options);

        Assert.Equal("Giáo viên dạy lái xe", content.Title);
        Assert.Equal("NGUYỄN ĐỨC ĐẠT", content.StudentName);
        Assert.Equal("Đào tạo hạng: Am", content.TrainingRank);
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
        Assert.Equal(0.5d, HocVienCardLayout.GapMm);
        Assert.Equal(20.5d, HocVienCardLayout.MarginXMm);
        Assert.Equal(4.25d, HocVienCardLayout.MarginYMm);

        var first = HocVienCardLayout.GetSlot(0);
        Assert.Equal(0, first.Column);
        Assert.Equal(0, first.Row);
        Assert.Equal(20.5d, first.XMm);
        Assert.Equal(4.25d, first.YMm);

        var last = HocVienCardLayout.GetSlot(11);
        Assert.Equal(2, last.Column);
        Assert.Equal(3, last.Row);
        Assert.Equal(191.5d, last.XMm);
        Assert.Equal(155.75d, last.YMm);
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
