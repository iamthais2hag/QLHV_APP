using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Computes V2RowHash for App_HocVien change detection.
/// Only stable normalized source fields are included; sync metadata is intentionally excluded.
/// </summary>
public static class V2RowHashCalculator
{
    public static string Compute(HocVienTargetWriteModel model)
    {
        var fields = new[]
        {
            Normalize(model.MaDK),
            Normalize(model.MaKhoa),
            Normalize(model.TenKhoa),
            Normalize(model.HangGPLXHoc),
            Normalize(model.HoTen),
            model.NgaySinh?.ToString("yyyy-MM-dd") ?? string.Empty,
            Normalize(model.GioiTinh),
            Normalize(model.SoCCCD),
            Normalize(model.DiaChiThuongTru),
            Normalize(model.SoGPLXDaCo),
            Normalize(model.HangGPLXDaCo),
            Normalize(model.NguoiNhanHoSo),
            Normalize(model.SourceOfTruth),
        };

        var canonical = string.Join("|", fields.Select(ToLengthPrefixedValue));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string ToLengthPrefixedValue(string value) => $"{value.Length}:{value}";
}
