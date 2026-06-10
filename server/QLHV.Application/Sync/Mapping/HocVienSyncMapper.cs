using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Pure mapper from a CSDT_V2 source row to the QLHV_APP write model.
/// It performs no I/O and follows docs/hoc-vien-data-rules.md: trim only, preserve original values.
/// </summary>
public static class HocVienSyncMapper
{
    public sealed record MapResult(
        HocVienTargetWriteModel? Model,
        IReadOnlyList<HocVienDataWarningDto> Warnings,
        bool ShouldSkip);

    public static MapResult MapAndValidate(V2HocVienSourceRow source)
    {
        var warnings = new List<HocVienDataWarningDto>();

        var maDK = Trim(source.MaDK);
        if (string.IsNullOrEmpty(maDK))
        {
            return new MapResult(null, warnings, ShouldSkip: true);
        }

        var soCccd = Trim(source.SoCMT);
        if (!string.IsNullOrEmpty(soCccd) && !IsCccdLengthValid(soCccd))
        {
            warnings.Add(new HocVienDataWarningDto
            {
                MaDK = maDK,
                Field = "SoCCCD",
                Code = "CCCD_LENGTH",
                Message = $"So CCCD khong du {HocVienDataRules.CccdExpectedLength} chu so (giu nguyen gia tri goc).",
            });
        }

        var modelWithoutHash = new HocVienTargetWriteModel
        {
            MaDK = maDK,
            MaKhoa = Trim(source.MaKhoaHoc),
            TenKhoa = Trim(source.TenKH),
            HangGPLXHoc = Trim(source.HangGPLX),
            HoTen = Trim(source.HoVaTen),
            NgaySinh = source.NgaySinh,
            GioiTinh = Trim(source.GioiTinh),
            SoCCCD = soCccd,
            DiaChiThuongTru = Trim(source.DiaChiThuongTru),
            SoGPLXDaCo = Trim(source.SoGPLXDaCo),
            HangGPLXDaCo = Trim(source.HangGPLXDaCo),
            NguoiNhanHoSo = Trim(source.NguoiNhanHoSo),
            SourceOfTruth = HocVienDataRules.SourceOfTruthV2,
        };

        var model = new HocVienTargetWriteModel
        {
            MaDK = modelWithoutHash.MaDK,
            MaKhoa = modelWithoutHash.MaKhoa,
            TenKhoa = modelWithoutHash.TenKhoa,
            HangGPLXHoc = modelWithoutHash.HangGPLXHoc,
            HoTen = modelWithoutHash.HoTen,
            NgaySinh = modelWithoutHash.NgaySinh,
            GioiTinh = modelWithoutHash.GioiTinh,
            SoCCCD = modelWithoutHash.SoCCCD,
            DiaChiThuongTru = modelWithoutHash.DiaChiThuongTru,
            SoGPLXDaCo = modelWithoutHash.SoGPLXDaCo,
            HangGPLXDaCo = modelWithoutHash.HangGPLXDaCo,
            NguoiNhanHoSo = modelWithoutHash.NguoiNhanHoSo,
            SourceOfTruth = modelWithoutHash.SourceOfTruth,
            V2RowHash = CalculateV2RowHash(modelWithoutHash),
        };

        return new MapResult(model, warnings, ShouldSkip: false);
    }

    /// <summary>
    /// Stable SHA-256 over normalized source fields only.
    /// Excludes volatile metadata such as LastSyncFromV2At, LastSyncStatus, UpdatedAt, and RowVersion.
    /// </summary>
    public static string CalculateV2RowHash(HocVienTargetWriteModel model)
    {
        var parts = new[]
        {
            model.MaDK,
            model.MaKhoa,
            model.TenKhoa,
            model.HangGPLXHoc,
            model.HoTen,
            model.NgaySinh?.ToString("yyyy-MM-dd"),
            model.GioiTinh,
            model.SoCCCD,
            model.DiaChiThuongTru,
            model.SoGPLXDaCo,
            model.HangGPLXDaCo,
            model.NguoiNhanHoSo,
            model.SourceOfTruth,
        };

        var canonical = string.Join("|", parts.Select(ToLengthPrefixedValue));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    public static bool IsCccdLengthValid(string value)
    {
        if (value.Length != HocVienDataRules.CccdExpectedLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ToLengthPrefixedValue(string? value)
    {
        value ??= string.Empty;
        return $"{value.Length}:{value}";
    }
}
