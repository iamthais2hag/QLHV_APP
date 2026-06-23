using ImageMagick;
using PdfSharp.Pdf.IO;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien.Printing;

namespace QLHV.Tests.HocVien;

public sealed class HocVienCardPdfGeneratorTests
{
    [Fact]
    public void Pdf_generator_creates_valid_a4_landscape_pdf_with_missing_photo_placeholder()
    {
        var bytes = CreateGenerator().CreatePdf([CreateHocVien(1)]);

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.InRange(document.Pages[0].Width.Point, 841.8d, 842d);
        Assert.InRange(document.Pages[0].Height.Point, 595.1d, 595.4d);
    }

    [Fact]
    public void Pdf_generator_starts_new_page_after_twelve_cards()
    {
        var rows = Enumerable.Range(1, 13).Select(CreateHocVien).ToArray();
        var bytes = CreateGenerator().CreatePdf(rows);

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }

    [Fact]
    public void Pdf_generator_accepts_custom_titles_and_returns_pdf()
    {
        var typography = HocVienCardTypographyOptions.Official with
        {
            OrganizationLine1 = new HocVienCardTextStyleOptions(
                "Arial", 11d, true, false, true),
        };
        var bytes = CreateGenerator().CreatePdf(
            [CreateHocVien(1)],
            titleOptions: new HocVienCardTitleOptions(
                "Cơ quan chủ quản",
                "Cơ sở đào tạo",
                typography));

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        var titles = HocVienCardTemplate.Default.ResolveTitles(
            new HocVienCardTitleOptions("Cơ quan chủ quản", "Cơ sở đào tạo"));
        Assert.Equal("CƠ QUAN CHỦ QUẢN", titles.TitleLine1);
        Assert.Equal("CƠ SỞ ĐÀO TẠO", titles.TitleLine2);
    }

    [Fact]
    public void Pdf_generator_embeds_jpeg_photo_when_available()
    {
        using var source = new MagickImage(MagickColors.Blue, 30u, 40u);
        source.Format = MagickFormat.Jpeg;
        var photo = new HocVienPhotoPreviewDto
        {
            ContentType = "image/jpeg",
            Content = source.ToByteArray(),
            PixelWidth = 30,
            PixelHeight = 40,
        };

        var bytes = CreateGenerator().CreatePdf(
            [CreateHocVien(1)],
            new Dictionary<int, HocVienPhotoPreviewDto> { [1] = photo });
        var rawPdf = System.Text.Encoding.Latin1.GetString(bytes);

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.Contains("/Subtype/Image", rawPdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_generator_uses_placeholder_instead_of_failing_for_invalid_photo_bytes()
    {
        var invalidPhoto = new HocVienPhotoPreviewDto
        {
            ContentType = "image/jpeg",
            Content = [0x00, 0x01, 0x02, 0x03],
            PixelWidth = 30,
            PixelHeight = 40,
        };

        var bytes = CreateGenerator().CreatePdf(
            [CreateHocVien(1)],
            new Dictionary<int, HocVienPhotoPreviewDto> { [1] = invalidPhoto });

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void Official_template_pdf_excludes_registration_and_course_codes()
    {
        var hocVien = CreateHocVien(1);
        var template = HocVienCardTemplate.Default;
        var content = template.CreateContent(hocVien);
        var renderedText = string.Join('|', template.TextLines.Select(line => content.GetText(line.Kind)));

        var bytes = CreateGenerator().CreatePdf([hocVien]);
        var rawPdf = System.Text.Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.DoesNotContain(hocVien.MaDangKy, renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain(hocVien.MaKhoa!, renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain(hocVien.MaDangKy, rawPdf, StringComparison.Ordinal);
        Assert.DoesNotContain(hocVien.MaKhoa!, rawPdf, StringComparison.Ordinal);
    }

    private static HocVienCardPdfGenerator CreateGenerator()
        => new(HocVienCardTemplate.Default);

    private static HocVienListItemDto CreateHocVien(int id) => new()
    {
        HocVienId = id,
        MaDangKy = $"66016-20251229-{id:000000000}",
        HoVaTen = id == 1 ? "NGUYỄN ĐỨC ĐẠT" : $"Học viên {id}",
        MaHangDT = "Am",
        HangGplxHoc = "Hạng Am",
        MaKhoa = "66016K26A0001",
        TenKhoa = "AK01",
    };
}
