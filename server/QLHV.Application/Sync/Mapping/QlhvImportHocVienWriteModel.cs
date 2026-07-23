namespace QLHV.Application.Sync.Mapping;

/// <summary>
/// Complete App_HocVien payload used only by the guarded QLHV full-snapshot import.
/// Keeping this model separate prevents photo fields and its hash format from changing
/// the existing DATA_V1/DATA_V2 synchronization flows.
/// </summary>
public sealed class QlhvImportHocVienWriteModel
{
    public string SourceProfileCode { get; init; } = string.Empty;
    public string SourceMaDK { get; init; } = string.Empty;
    public string SourceSystem { get; init; } = string.Empty;
    public string? SourceVersion { get; init; }
    public string MaDK { get; init; } = string.Empty;
    public string? MaKhoa { get; init; }
    public string? TenKhoa { get; init; }
    public string? MaHangDT { get; init; }
    public string? HangGPLXHoc { get; init; }
    public string? HoTen { get; init; }
    public DateTime? NgaySinh { get; init; }
    public string? GioiTinh { get; init; }
    public string? SoCCCD { get; init; }
    public string? DiaChiThuongTru { get; init; }
    public string? SoGPLXDaCo { get; init; }
    public string? HangGPLXDaCo { get; init; }
    public string? NguoiNhanHoSo { get; init; }
    public string? AnhRelativePath { get; init; }
    /// <summary>
    /// Import-only signal for the post-commit photo queue. It is never persisted as a path and
    /// deliberately does not block the database transaction.
    /// </summary>
    public bool SourcePhotoPathInvalid { get; init; }
    public int? ChatLuongAnh { get; init; }
    public DateTime? NgayThuNhanAnh { get; init; }
    public string? NguoiThuNhanAnh { get; init; }
    public string SourceOfTruth { get; init; } = HocVienDataRules.SourceOfTruthV2;
    public string V2RowHash { get; init; } = string.Empty;
}
