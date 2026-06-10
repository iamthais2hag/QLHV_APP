using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Tests.HocVien;

public sealed class HocVienExcelExporterTests
{
    [Fact]
    public void Export_workbook_writes_expected_sheet_headers_and_display_values()
    {
        var bytes = HocVienExcelExporter.CreateWorkbook(
        [
            new HocVienListItemDto
            {
                MaDangKy = "001-DK",
                HoVaTen = "Nguyen Van A",
                NgaySinh = new DateOnly(1990, 1, 2),
                GioiTinh = "M",
                SoCccd = "001234567890",
                DiaChiThuongTru = "Phuong 1",
                HangGplxHoc = "Hang B2",
                MaHangDT = "B2",
                SoGplxDaCo = "000GPLX",
                HangGplxDaCo = "B1",
                NguoiNhanHoSo = "Can bo tiep nhan",
                TenKhoa = "AK01",
                MaKhoa = "000K01",
            },
        ], totalCount: 1);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        Assert.Equal("Tong so: 1 hoc vien", RemoveVietnameseMarks(worksheet.Cell(1, 1).GetString()));
        Assert.Equal("Ma dang ky", RemoveVietnameseMarks(worksheet.Cell(3, 2).GetString()));
        Assert.Equal("001-DK", worksheet.Cell(4, 2).GetString());
        Assert.Equal("Nguyen Van A", worksheet.Cell(4, 3).GetString());
        Assert.Equal("02/01/1990", worksheet.Cell(4, 4).GetString());
        Assert.Equal("Nam", worksheet.Cell(4, 5).GetString());
        Assert.Equal("001234567890", worksheet.Cell(4, 6).GetString());
        Assert.Equal("B2", worksheet.Cell(4, 9).GetString());
        Assert.Equal("000GPLX", worksheet.Cell(4, 10).GetString());
        Assert.Equal("000K01", worksheet.Cell(4, 14).GetString());
    }

    [Fact]
    public void Export_workbook_formats_identity_license_registration_and_course_codes_as_text()
    {
        var bytes = HocVienExcelExporter.CreateWorkbook(
        [
            new HocVienListItemDto
            {
                MaDangKy = "001-DK",
                HoVaTen = "Nguyen Van A",
                GioiTinh = "F",
                SoCccd = "001234567890",
                MaHangDT = "A1m",
                SoGplxDaCo = "000GPLX",
                MaKhoa = "000K01",
            },
        ], totalCount: 1);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        Assert.Equal("Nu", RemoveVietnameseMarks(worksheet.Cell(4, 5).GetString()));
        AssertTextCell(worksheet.Cell(4, 2), "001-DK");
        AssertTextCell(worksheet.Cell(4, 6), "001234567890");
        AssertTextCell(worksheet.Cell(4, 9), "A1m");
        AssertTextCell(worksheet.Cell(4, 10), "000GPLX");
        AssertTextCell(worksheet.Cell(4, 14), "000K01");
    }

    private static void AssertTextCell(IXLCell cell, string expected)
    {
        Assert.Equal(expected, cell.GetString());
        Assert.Equal("@", cell.Style.NumberFormat.Format);
    }

    private static string RemoveVietnameseMarks(string value)
    {
        var normalized = value
            .Replace("đ", "d", StringComparison.Ordinal)
            .Replace("Đ", "D", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
