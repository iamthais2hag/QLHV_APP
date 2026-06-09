using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Tính V2RowHash để phát hiện thay đổi cho App_HocVien.
///
/// Chỉ dùng các trường dữ liệu ỔN ĐỊNH đã chuẩn hóa từ mapping (xem docs/hoc-vien-v2-mapping.md).
/// KHÔNG đưa vào hash các trường biến động như LastSyncFromV2At, LastSyncStatus, UpdatedAt...
/// (nếu không, mọi lần đồng bộ đều bị coi là "đổi"). Hàm THUẦN, không I/O.
/// </summary>
public static class V2RowHashCalculator
{
    /// <summary>
    /// Tạo hash SHA-256 (hex thường, 64 ký tự) từ các trường ổn định của mô hình ghi.
    /// NgaySinh chuẩn hóa về yyyy-MM-dd để ổn định không phụ thuộc culture.
    /// </summary>
    public static string Compute(HocVienTargetWriteModel model)
    {
        // Thứ tự cố định; mỗi trường có phân tách rõ ràng để tránh nhập nhằng nối chuỗi.
        var fields = new[]
        {
            N(model.MaDK),
            N(model.MaKhoa),
            N(model.TenKhoa),
            N(model.HangGPLXHoc),
            N(model.HoTen),
            model.NgaySinh?.ToString("yyyy-MM-dd") ?? string.Empty,
            N(model.GioiTinh),
            N(model.SoCCCD),
            N(model.DiaChiThuongTru),
            N(model.SoGPLXDaCo),
            N(model.HangGPLXDaCo),
            N(model.NguoiNhanHoSo),
        };

        var joined = string.Join("\u001F", fields); // dùng ký tự Unit Separator làm dấu phân tách
        var bytes = Encoding.UTF8.GetBytes(joined);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string N(string? value) => value ?? string.Empty;
}
