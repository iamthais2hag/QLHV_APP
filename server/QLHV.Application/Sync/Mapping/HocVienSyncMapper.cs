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
        => MapAndValidate(source, HocVienSourceIdentityContext.DataV2);

    public static MapResult MapAndValidate(
        V2HocVienSourceRow source,
        HocVienSourceIdentityContext sourceIdentity)
    {
        ArgumentNullException.ThrowIfNull(sourceIdentity);

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
            SourceProfileCode = sourceIdentity.SourceProfileCode,
            SourceMaDK = maDK,
            SourceSystem = sourceIdentity.SourceSystem,
            SourceVersion = sourceIdentity.SourceVersion,
            MaDK = maDK,
            MaKhoa = Trim(source.MaKhoaHoc),
            TenKhoa = Trim(source.TenKH),
            MaHangDT = Trim(source.HangDaoTao),
            HangGPLXHoc = Trim(source.TenHangDT),
            HoTen = Trim(source.HoVaTen),
            NgaySinh = source.NgaySinh,
            GioiTinh = Trim(source.GioiTinh),
            SoCCCD = soCccd,
            DiaChiThuongTru = Trim(source.NoiTTTenDayDu) ?? Trim(source.NoiTT) ?? Trim(source.DiaChiThuongTru),
            SoGPLXDaCo = Trim(source.SoGPLXDaCo),
            HangGPLXDaCo = Trim(source.HangGPLXDaCo),
            NguoiNhanHoSo = Trim(source.NguoiNhanHoSo),
            SourceOfTruth = HocVienDataRules.SourceOfTruthV2,
        };

        var model = new HocVienTargetWriteModel
        {
            SourceProfileCode = modelWithoutHash.SourceProfileCode,
            SourceMaDK = modelWithoutHash.SourceMaDK,
            SourceSystem = modelWithoutHash.SourceSystem,
            SourceVersion = modelWithoutHash.SourceVersion,
            MaDK = modelWithoutHash.MaDK,
            MaKhoa = modelWithoutHash.MaKhoa,
            TenKhoa = modelWithoutHash.TenKhoa,
            MaHangDT = modelWithoutHash.MaHangDT,
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
            V2RowHash = V2RowHashCalculator.Compute(modelWithoutHash),
        };

        return new MapResult(model, warnings, ShouldSkip: false);
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
}
