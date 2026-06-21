using System.Text;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.HocVien.Printing;

namespace QLHV.Tests.HocVien;

public sealed class HocVienCardPdfGeneratorTests
{
    [Fact]
    public void Pdf_generator_creates_valid_pdf_with_photo_placeholder()
    {
        var bytes = new HocVienCardPdfGenerator().CreatePdf([CreateHocVien(1)]);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("(Anh)", text, StringComparison.Ordinal);
        Assert.Equal(1, CountPages(text));
    }

    [Fact]
    public void Pdf_generator_starts_new_page_after_twelve_cards()
    {
        var rows = Enumerable.Range(1, 13).Select(CreateHocVien).ToArray();
        var bytes = new HocVienCardPdfGenerator().CreatePdf(rows);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.Equal(2, CountPages(text));
    }

    [Fact]
    public void Pdf_generator_embeds_jpeg_photo_when_available()
    {
        var photo = new HocVienPhotoPreviewDto
        {
            ContentType = "image/jpeg",
            Content = [0xFF, 0xD8, 0xFF, 0xD9],
            PixelWidth = 3,
            PixelHeight = 4,
        };
        var bytes = new HocVienCardPdfGenerator().CreatePdf(
            [CreateHocVien(1)],
            new Dictionary<int, HocVienPhotoPreviewDto> { [1] = photo });
        var text = Encoding.ASCII.GetString(bytes);

        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.Contains("/Filter /DCTDecode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(Anh)", text, StringComparison.Ordinal);
    }

    private static int CountPages(string pdfText)
        => pdfText.Split("/Type /Page /Parent", StringSplitOptions.None).Length - 1;

    private static HocVienListItemDto CreateHocVien(int id) => new()
    {
        HocVienId = id,
        MaDangKy = $"66016-20251229-{id:000000000}",
        HoVaTen = $"Hoc vien {id}",
        MaHangDT = "Am",
        HangGplxHoc = "Hang Am",
        MaKhoa = "66016K26A0001",
        TenKhoa = "AK01",
    };
}
