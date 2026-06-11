using ClosedXML.Excel;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Tests.HocVien;

public sealed class HocVienExcelExporterTests
{
    private static readonly string[] ExpectedHeaders =
    [
        "STT",
        "Mã đăng ký",
        "Họ và tên",
        "Ngày sinh",
        "Giới tính",
        "Số CCCD",
        "Địa chỉ thường trú",
        "Hạng học",
        "Mã hạng học",
        "Số GPLX đã có",
        "Hạng GPLX đã có",
        "Người nhận hồ sơ",
        "Tên khóa",
        "Mã khóa",
    ];

    [Fact]
    public void Export_workbook_writes_expected_sheet_headers_and_display_values()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        Assert.Equal("Tổng số: 1 học viên", worksheet.Cell(1, 1).GetString());

        for (var index = 0; index < ExpectedHeaders.Length; index++)
        {
            Assert.Equal(ExpectedHeaders[index], worksheet.Cell(3, index + 1).GetString());
        }

        Assert.Equal("001-DK", worksheet.Cell(4, 2).GetString());
        Assert.Equal("Nguyen Van A", worksheet.Cell(4, 3).GetString());
        Assert.Equal("02/01/1990", worksheet.Cell(4, 4).GetString());
        Assert.Equal("Nam", worksheet.Cell(4, 5).GetString());
        Assert.Equal("001234567890", worksheet.Cell(4, 6).GetString());
        Assert.Equal("Phuong 1", worksheet.Cell(4, 7).GetString());
        Assert.Equal("B2", worksheet.Cell(4, 8).GetString());
        Assert.Equal("B2", worksheet.Cell(4, 9).GetString());
        Assert.Equal("000GPLX", worksheet.Cell(4, 10).GetString());
        Assert.Equal("B1", worksheet.Cell(4, 11).GetString());
        Assert.Equal("Can bo tiep nhan", worksheet.Cell(4, 12).GetString());
        Assert.Equal("AK01", worksheet.Cell(4, 13).GetString());
        Assert.Equal("000K01", worksheet.Cell(4, 14).GetString());
    }

    [Fact]
    public void Export_workbook_formats_header_as_times_new_roman_size_13_bold()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        for (var column = 1; column <= ExpectedHeaders.Length; column++)
        {
            var font = worksheet.Cell(3, column).Style.Font;
            Assert.Equal(HocVienExcelExporter.DefaultFontName, font.FontName);
            Assert.Equal(HocVienExcelExporter.DefaultFontSize, font.FontSize);
            Assert.True(font.Bold);
        }
    }

    [Fact]
    public void Export_workbook_formats_total_row_and_body_as_times_new_roman_size_13()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        var totalFont = worksheet.Cell(1, 1).Style.Font;
        Assert.Equal(HocVienExcelExporter.DefaultFontName, totalFont.FontName);
        Assert.Equal(HocVienExcelExporter.DefaultFontSize, totalFont.FontSize);
        Assert.True(totalFont.Bold);

        for (var column = 1; column <= ExpectedHeaders.Length; column++)
        {
            var bodyFont = worksheet.Cell(4, column).Style.Font;
            Assert.Equal(HocVienExcelExporter.DefaultFontName, bodyFont.FontName);
            Assert.Equal(HocVienExcelExporter.DefaultFontSize, bodyFont.FontSize);
            Assert.False(bodyFont.Bold);
        }
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

        Assert.Equal("Nữ", worksheet.Cell(4, 5).GetString());
        AssertTextCell(worksheet.Cell(4, 2), "001-DK");
        AssertTextCell(worksheet.Cell(4, 6), "001234567890");
        AssertTextCell(worksheet.Cell(4, 8), "A1m");
        AssertTextCell(worksheet.Cell(4, 9), "A1m");
        AssertTextCell(worksheet.Cell(4, 10), "000GPLX");
        AssertTextCell(worksheet.Cell(4, 14), "000K01");
    }

    private static byte[] CreateSampleWorkbook()
        => HocVienExcelExporter.CreateWorkbook(
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

    private static void AssertTextCell(IXLCell cell, string expected)
    {
        Assert.Equal(expected, cell.GetString());
        Assert.Equal("@", cell.Style.NumberFormat.Format);
    }
}
