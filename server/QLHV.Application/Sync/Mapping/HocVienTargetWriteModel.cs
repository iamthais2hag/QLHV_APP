namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Values written to QLHV_APP.dbo.App_HocVien after applying documented data rules:
/// trim only, preserve source values, no guessed conversion.
/// </summary>
public sealed class HocVienTargetWriteModel
{
    public string MaDK { get; init; } = string.Empty;
    public string? MaKhoa { get; init; }
    public string? TenKhoa { get; init; }
    public string? HangGPLXHoc { get; init; }
    public string? HoTen { get; init; }
    public DateTime? NgaySinh { get; init; }
    public string? GioiTinh { get; init; }
    public string? SoCCCD { get; init; }
    public string? DiaChiThuongTru { get; init; }
    public string? SoGPLXDaCo { get; init; }
    public string? HangGPLXDaCo { get; init; }
    public string? NguoiNhanHoSo { get; init; }
    public string SourceOfTruth { get; init; } = HocVienDataRules.SourceOfTruthV2;

    /// <summary>SHA-256 hash of stable normalized V2 source fields, used for change detection.</summary>
    public string V2RowHash { get; init; } = string.Empty;
}
