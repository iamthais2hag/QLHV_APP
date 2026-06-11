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
            Assert.Equal(ExpectedHeaders[index], worksheet.Cell(2, index + 1).GetString());
        }

        Assert.Equal("001-DK", worksheet.Cell(3, 2).GetString());
        Assert.Equal("Nguyen Van A Co Ten Dai De Kiem Tra Do Rong Cot", worksheet.Cell(3, 3).GetString());
        Assert.Equal("02/01/1990", worksheet.Cell(3, 4).GetString());
        Assert.Equal("Nam", worksheet.Cell(3, 5).GetString());
        Assert.Equal("001234567890", worksheet.Cell(3, 6).GetString());
        Assert.Equal(
            "So 123 Duong Rat Dai, Phuong 1, Quan Trung Tam, Thanh pho Can wrap text, " +
            "tiep tuc them noi dung dai de kiem tra chieu cao dong tu dong tang khi dia chi bi xuong dong",
            worksheet.Cell(3, 7).GetString());
        Assert.Equal("B2", worksheet.Cell(3, 8).GetString());
        Assert.Equal("B2", worksheet.Cell(3, 9).GetString());
        Assert.Equal("000GPLX", worksheet.Cell(3, 10).GetString());
        Assert.Equal("B1", worksheet.Cell(3, 11).GetString());
        Assert.Equal("Can bo tiep nhan", worksheet.Cell(3, 12).GetString());
        Assert.Equal("AK01", worksheet.Cell(3, 13).GetString());
        Assert.Equal("000K01", worksheet.Cell(3, 14).GetString());
    }

    [Fact]
    public void Export_workbook_places_header_immediately_after_total_row()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        Assert.Equal("Tổng số: 1 học viên", worksheet.Cell(1, 1).GetString());
        Assert.Equal("STT", worksheet.Cell(2, 1).GetString());
        Assert.Equal("Mã đăng ký", worksheet.Cell(2, 2).GetString());
        Assert.Equal("1", worksheet.Cell(3, 1).GetString());
        Assert.Equal("001-DK", worksheet.Cell(3, 2).GetString());
        Assert.False(worksheet.Row(2).IsEmpty());
        Assert.False(worksheet.Row(3).IsEmpty());
    }

    [Fact]
    public void Export_workbook_formats_header_as_times_new_roman_size_13_bold()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        for (var column = 1; column <= ExpectedHeaders.Length; column++)
        {
            var font = worksheet.Cell(2, column).Style.Font;
            Assert.Equal(HocVienExcelExporter.DefaultFontName, font.FontName);
            Assert.Equal(HocVienExcelExporter.DefaultFontSize, font.FontSize);
            Assert.True(font.Bold);
            Assert.Equal(XLAlignmentHorizontalValues.Center, worksheet.Cell(2, column).Style.Alignment.Horizontal);
            Assert.Equal(XLAlignmentVerticalValues.Center, worksheet.Cell(2, column).Style.Alignment.Vertical);
            Assert.True(worksheet.Cell(2, column).Style.Alignment.WrapText);
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
        Assert.Equal("A1:N1", worksheet.MergedRanges.Single().RangeAddress.ToString());

        for (var column = 1; column <= ExpectedHeaders.Length; column++)
        {
            var bodyFont = worksheet.Cell(3, column).Style.Font;
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

        Assert.Equal("Nữ", worksheet.Cell(3, 5).GetString());
        AssertTextCell(worksheet.Cell(3, 2), "001-DK");
        AssertTextCell(worksheet.Cell(3, 6), "001234567890");
        AssertTextCell(worksheet.Cell(3, 8), "A1m");
        AssertTextCell(worksheet.Cell(3, 9), "A1m");
        AssertTextCell(worksheet.Cell(3, 10), "000GPLX");
        AssertTextCell(worksheet.Cell(3, 14), "000K01");
    }

    [Fact]
    public void Export_workbook_applies_column_widths_wrap_text_and_row_heights()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        AssertColumnWidth(worksheet, 2, min: 30d, max: 34d);
        AssertColumnWidth(worksheet, 3, min: 28d, max: 35d);
        AssertColumnWidth(worksheet, 6, min: 16d, max: 18d);
        AssertColumnWidth(worksheet, 7, min: 40d, max: 55d);
        AssertColumnWidth(worksheet, 9, min: 12d, max: 14d);
        AssertColumnWidth(worksheet, 11, min: 14d, max: 16d);
        AssertColumnWidth(worksheet, 12, min: 18d, max: 24d);
        AssertColumnWidth(worksheet, 13, min: 14d, max: 18d);
        AssertColumnWidth(worksheet, 14, min: 26d, max: 32d);

        Assert.True(worksheet.Column(7).Style.Alignment.WrapText);
        Assert.True(worksheet.Column(12).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(2).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(6).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(8).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(9).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(10).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(11).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(13).Style.Alignment.WrapText);
        Assert.False(worksheet.Column(14).Style.Alignment.WrapText);
        Assert.True(worksheet.Row(2).Height >= 28d);
        Assert.True(worksheet.Row(3).Height > 22d);
        Assert.Equal(2, worksheet.SheetView.SplitRow);
    }

    [Fact]
    public void Export_workbook_aligns_body_columns_by_content_type()
    {
        using var stream = new MemoryStream(CreateSampleWorkbook());
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(HocVienExcelExporter.SheetName);

        foreach (var column in new[] { 2, 3, 6, 7, 10, 12, 13, 14 })
        {
            Assert.Equal(XLAlignmentHorizontalValues.Left, worksheet.Cell(3, column).Style.Alignment.Horizontal);
        }

        foreach (var column in new[] { 1, 4, 5, 8, 9, 11 })
        {
            Assert.Equal(XLAlignmentHorizontalValues.Center, worksheet.Cell(3, column).Style.Alignment.Horizontal);
        }
    }

    private static byte[] CreateSampleWorkbook()
        => HocVienExcelExporter.CreateWorkbook(
        [
            new HocVienListItemDto
            {
                MaDangKy = "001-DK",
                HoVaTen = "Nguyen Van A Co Ten Dai De Kiem Tra Do Rong Cot",
                NgaySinh = new DateOnly(1990, 1, 2),
                GioiTinh = "M",
                SoCccd = "001234567890",
                DiaChiThuongTru =
                    "So 123 Duong Rat Dai, Phuong 1, Quan Trung Tam, Thanh pho Can wrap text, " +
                    "tiep tuc them noi dung dai de kiem tra chieu cao dong tu dong tang khi dia chi bi xuong dong",
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

    private static void AssertColumnWidth(IXLWorksheet worksheet, int column, double min, double max)
    {
        var width = worksheet.Column(column).Width;
        Assert.True(width >= min, $"Column {column} width {width} should be >= {min}.");
        Assert.True(width <= max, $"Column {column} width {width} should be <= {max}.");
    }
}
