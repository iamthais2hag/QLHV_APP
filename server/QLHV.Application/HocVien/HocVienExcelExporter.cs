using System.Globalization;
using ClosedXML.Excel;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Application.HocVien;

public static class HocVienExcelExporter
{
    public const string SheetName = "HocVien";
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

    private static readonly int[] TextColumnIndexes = [2, 6, 9, 10, 14];

    public static byte[] CreateWorkbook(IReadOnlyList<HocVienListItemDto> rows, int totalCount)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(SheetName);

        worksheet.Cell(1, 1).Value = $"Tổng số: {totalCount.ToString("N0", VietnameseCulture)} học viên";
        worksheet.Cell(1, 1).Style.Font.Bold = true;

        const int headerRow = 3;
        for (var index = 0; index < Headers.Length; index++)
        {
            var cell = worksheet.Cell(headerRow, index + 1);
            cell.Value = Headers[index];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF3FF");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            WriteDataRow(worksheet, headerRow + rowIndex + 1, rowIndex + 1, rows[rowIndex]);
        }

        foreach (var columnIndex in TextColumnIndexes)
        {
            worksheet.Column(columnIndex).Style.NumberFormat.Format = "@";
        }

        worksheet.Columns().AdjustToContents();

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
        worksheet.Cell(rowNumber, 8).Value = row.HangGplxHoc ?? string.Empty;
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

    private static string FormatDate(DateOnly? value)
        => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : string.Empty;
}
