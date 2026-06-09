using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Ánh xạ một dòng nguồn CSDT_V2 sang mô hình ghi QLHV_APP, áp dụng quy tắc dữ liệu đã chốt
/// (xem docs/hoc-vien-data-rules.md). Hàm THUẦN (pure), không I/O, không ghi DB.
///
/// Quy tắc:
///   - Chỉ TRIM; bảo toàn giá trị gốc; rỗng → null.
///   - SoCCCD ← SoCMT: KHÔNG chuyển CMND 9 số sang CCCD 12 số. Nếu khác 12 chữ số → cảnh báo (không sửa).
///   - GioiTinh: giữ nguyên giá trị gốc (đã trim).
///   - SourceOfTruth = "V2".
/// </summary>
public static class HocVienSyncMapper
{
    /// <summary>Kết quả ánh xạ kèm cảnh báo dữ liệu.</summary>
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
            // Không có khóa hợp lệ → bỏ qua (không thể upsert theo MaDK).
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

        var model = new HocVienTargetWriteModel
        {
            MaDK = maDK,
            MaKhoa = Trim(source.MaKhoaHoc),
            TenKhoa = Trim(source.TenKH),
            HangGPLXHoc = Trim(source.HangGPLX),
            HoTen = Trim(source.HoVaTen),
            NgaySinh = source.NgaySinh,
            GioiTinh = Trim(source.GioiTinh),   // giữ nguyên giá trị gốc
            SoCCCD = soCccd,                     // chỉ trim, không chuyển đổi
            DiaChiThuongTru = Trim(source.DiaChiThuongTru),
            SoGPLXDaCo = Trim(source.SoGPLXDaCo),
            HangGPLXDaCo = Trim(source.HangGPLXDaCo),
            NguoiNhanHoSo = Trim(source.NguoiNhanHoSo),
            SourceOfTruth = HocVienDataRules.SourceOfTruthV2,
        };

        return new MapResult(model, warnings, ShouldSkip: false);
    }

    /// <summary>Kiểm tra số CCCD có đúng 12 chữ số hay không (không sửa giá trị).</summary>
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
