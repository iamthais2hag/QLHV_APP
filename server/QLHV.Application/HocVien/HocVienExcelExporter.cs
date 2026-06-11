using System.Globalization;
using ClosedXML.Excel;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien;

public static class HocVienExcelExporter
{
    public const string SheetName = "HocVien";
    public const string DefaultFontName = "Times New Roman";
    public const double DefaultFontSize = 13d;

    private const int HeaderRow = 2;
    private const double DefaultRowMinHeight = 22d;
    private const double HeaderRowMinHeight = 32d;
    private const double MaxWrappedRowHeight = 90d;

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
    private static readonly int[] LeftAlignedColumnIndexes = [2, 3, 6, 7, 10, 12, 13, 14];
    private static readonly int[] CenterAlignedColumnIndexes = [1, 4, 5, 8, 9, 11];
    private static readonly int[] NoWrapTextColumnIndexes = [2, 6, 8, 9, 10, 11, 13, 14];
    private static readonly int[] WrapTextColumnIndexes = [7, 12];

    private static readonly ColumnWidthRule[] ColumnWidthRules =
    [
        new(1, 6d, 6d),
        new(2, 30d, 34d),
        new(3, 28d, 35d),
        new(4, 14d, 14d),
        new(5, 10d, 12d),
        new(6, 16d, 18d),
        new(7, 40d, 55d),
        new(8, 10d, 12d),
        new(9, 12d, 14d),
        new(10, 18d, 20d),
        new(11, 14d, 16d),
        new(12, 18d, 24d),
        new(13, 14d, 18d),
        new(14, 26d, 32d),
    ];

    public static byte[] CreateWorkbook(IReadOnlyList<HocVienListItemDto> rows, int totalCount)
    {
        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = DefaultFontName;
        workbook.Style.Font.FontSize = DefaultFontSize;

        var worksheet = workbook.Worksheets.Add(SheetName);
        worksheet.Style.Font.FontName = DefaultFontName;
        worksheet.Style.Font.FontSize = DefaultFontSize;

        worksheet.Range(1, 1, 1, Headers.Length).Merge();
        var totalCell = worksheet.Cell(1, 1);
        totalCell.Value = $"Tổng số: {totalCount.ToString("N0", VietnameseCulture)} học viên";
        totalCell.Style.Font.Bold = true;
        totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

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

        foreach (var columnIndex in LeftAlignedColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        foreach (var columnIndex in CenterAlignedColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        foreach (var columnIndex in NoWrapTextColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.Alignment.WrapText = false;
        }

        foreach (var columnIndex in WrapTextColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.Alignment.WrapText = true;
        }

        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");

        worksheet.Columns(1, Headers.Length).AdjustToContents();
        ApplyColumnWidthRules(worksheet);
        worksheet.RowsUsed().AdjustToContents();
        ApplyWrappedRowHeights(worksheet);
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

    private static void ApplyWrappedRowHeights(IXLWorksheet worksheet)
    {
        foreach (var row in worksheet.RowsUsed())
        {
            if (row.RowNumber() <= HeaderRow)
            {
                continue;
            }

            var estimatedLines = 1;
            foreach (var columnIndex in WrapTextColumnIndexes)
            {
                var text = worksheet.Cell(row.RowNumber(), columnIndex).GetString();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var width = Math.Max(8d, worksheet.Column(columnIndex).Width);
                estimatedLines = Math.Max(estimatedLines, (int)Math.Ceiling(text.Length / width));
            }

            if (estimatedLines > 1)
            {
                row.Height = Math.Min(MaxWrappedRowHeight, Math.Max(row.Height, DefaultRowMinHeight * estimatedLines));
            }
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
