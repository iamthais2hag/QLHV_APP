using System.Globalization;
using ClosedXML.Excel;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien;

public static class HocVienExcelExporter
{
    public const string SheetName = "HocVien";
    public const string DefaultFontName = "Times New Roman";
    public const double DefaultFontSize = 13d;

    private const int HeaderRow = 3;
    private const double DefaultRowMinHeight = 20d;
    private const double HeaderRowMinHeight = 24d;

    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    private static readonly string[] Headers =
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

    private static readonly int[] TextColumnIndexes = [2, 6, 8, 9, 10, 14];
    private static readonly int[] CenterAlignedColumnIndexes = [1, 4, 5, 8, 9, 11];
    private static readonly int[] WrapTextColumnIndexes = [2, 7, 14];

    private static readonly ColumnWidthRule[] ColumnWidthRules =
    [
        new(1, 6d, 8d),
        new(2, 22d, 32d),
        new(3, 18d, 35d),
        new(4, 12d, 14d),
        new(5, 10d, 12d),
        new(6, 15d, 18d),
        new(7, 25d, 55d),
        new(8, 10d, 12d),
        new(9, 12d, 15d),
        new(10, 16d, 20d),
        new(11, 12d, 16d),
        new(12, 18d, 28d),
        new(13, 12d, 24d),
        new(14, 18d, 32d),
    ];

    public static byte[] CreateWorkbook(IReadOnlyList<HocVienListItemDto> rows, int totalCount)
    {
        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = DefaultFontName;
        workbook.Style.Font.FontSize = DefaultFontSize;

        var worksheet = workbook.Worksheets.Add(SheetName);
        worksheet.Style.Font.FontName = DefaultFontName;
        worksheet.Style.Font.FontSize = DefaultFontSize;

        worksheet.Cell(1, 1).Value = $"Tổng số: {totalCount.ToString("N0", VietnameseCulture)} học viên";
        worksheet.Cell(1, 1).Style.Font.Bold = true;

        for (var index = 0; index < Headers.Length; index++)
        {
            var cell = worksheet.Cell(HeaderRow, index + 1);
            cell.Value = Headers[index];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            WriteDataRow(worksheet, HeaderRow + rowIndex + 1, rowIndex + 1, rows[rowIndex]);
        }

        foreach (var columnIndex in TextColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.NumberFormat.Format = "@";
        }

        ApplyWorksheetLayout(worksheet);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteDataRow(IXLWorksheet worksheet, int rowNumber, int stt, HocVienListItemDto row)
    {
        worksheet.Cell(rowNumber, 1).Value = stt;
        SetText(worksheet.Cell(rowNumber, 2), row.MaDangKy);
        worksheet.Cell(rowNumber, 3).Value = row.HoVaTen;
        worksheet.Cell(rowNumber, 4).Value = FormatDate(row.NgaySinh);
        worksheet.Cell(rowNumber, 5).Value = HocVienGender.ToDisplayValue(row.GioiTinh) ?? string.Empty;
        SetText(worksheet.Cell(rowNumber, 6), row.SoCccd);
        worksheet.Cell(rowNumber, 7).Value = row.DiaChiThuongTru ?? string.Empty;
        SetText(worksheet.Cell(rowNumber, 8), row.MaHangDT);
        SetText(worksheet.Cell(rowNumber, 9), row.MaHangDT);
        SetText(worksheet.Cell(rowNumber, 10), row.SoGplxDaCo);
        worksheet.Cell(rowNumber, 11).Value = row.HangGplxDaCo ?? string.Empty;
        worksheet.Cell(rowNumber, 12).Value = row.NguoiNhanHoSo ?? string.Empty;
        worksheet.Cell(rowNumber, 13).Value = row.TenKhoa ?? string.Empty;
        SetText(worksheet.Cell(rowNumber, 14), row.MaKhoa);
    }

    private static void SetText(IXLCell cell, string? value)
    {
        cell.Style.NumberFormat.Format = "@";
        cell.Value = value ?? string.Empty;
    }

    private static void ApplyWorkbookFont(IXLWorksheet worksheet)
    {
        var range = worksheet.RangeUsed();
        if (range is null)
        {
            return;
        }

        range.Style.Font.FontName = DefaultFontName;
        range.Style.Font.FontSize = DefaultFontSize;
    }

    private static void ApplyWorksheetLayout(IXLWorksheet worksheet)
    {
        ApplyWorkbookFont(worksheet);

        var usedRange = worksheet.RangeUsed();
        if (usedRange is not null)
        {
            usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        var headerRange = worksheet.Range(HeaderRow, 1, HeaderRow, Headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");

        worksheet.Cell(1, 1).Style.Font.Bold = true;

        foreach (var columnIndex in CenterAlignedColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        foreach (var columnIndex in WrapTextColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.Alignment.WrapText = true;
        }

        worksheet.Columns(1, Headers.Length).AdjustToContents();
        ApplyColumnWidthRules(worksheet);
        worksheet.RowsUsed().AdjustToContents();
        ApplyMinimumRowHeights(worksheet);
        worksheet.SheetView.FreezeRows(HeaderRow);
    }

    private static void ApplyColumnWidthRules(IXLWorksheet worksheet)
    {
        foreach (var rule in ColumnWidthRules)
        {
            var column = worksheet.Column(rule.Index);
            column.Width = Math.Clamp(column.Width, rule.MinWidth, rule.MaxWidth);
        }
    }

    private static void ApplyMinimumRowHeights(IXLWorksheet worksheet)
    {
        foreach (var row in worksheet.RowsUsed())
        {
            var minHeight = row.RowNumber() == HeaderRow ? HeaderRowMinHeight : DefaultRowMinHeight;
            if (row.Height < minHeight)
            {
                row.Height = minHeight;
            }
        }
    }

    private static string FormatDate(DateOnly? value)
        => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : string.Empty;

    private sealed record ColumnWidthRule(int Index, double MinWidth, double MaxWidth);
}
