using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Computes V2RowHash for App_HocVien change detection.
/// Uses stable normalized source fields only; volatile sync metadata is intentionally excluded.
/// </summary>
public static class V2RowHashCalculator
{
    public static string Compute(HocVienTargetWriteModel model)
    {
        var fields = new[]
        {
            N(model.MaDK),
            N(model.MaKhoa),
            N(model.TenKhoa),
            N(model.MaHangDT),
            N(model.HangGPLXHoc),
            N(model.HoTen),
            model.NgaySinh?.ToString("yyyy-MM-dd") ?? string.Empty,
            N(model.GioiTinh),
            N(model.SoCCCD),
            N(model.DiaChiThuongTru),
            N(model.SoGPLXDaCo),
            N(model.HangGPLXDaCo),
            N(model.NguoiNhanHoSo),
            N(model.SourceOfTruth),
        };

        var canonical = string.Join("|", fields.Select(value => $"{value.Length}:{value}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string N(string? value) => value ?? string.Empty;
}
