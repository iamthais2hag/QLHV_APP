using QLHV.Application.Sync;

namespace QLHV.Application.Sync.Mapping;

public sealed record QlhvImportKhoaHocWriteModel(
    string SourceProfileCode,
    string SourceMaKhoaHoc,
    string SourceHash,
    string MaKhoa,
    string? TenKhoa,
    string? MaCSDT,
    string? MaSoGTVT,
    string? HangGPLX,
    string? HangDaoTao,
    string? SoQuyetDinhKhaiGiang,
    DateTime? NgayQuyetDinhKhaiGiang,
    DateTime? NgayKhaiGiang,
    DateTime? NgayBeGiang,
    string? MucTieuDaoTao,
    DateTime? NgayThi,
    DateTime? NgaySatHach,
    int? TongSoHocVien,
    int? SoHocVienTotNghiep,
    int? SoHocVienDuocCapGPLX,
    int? ThoiGianDaoTao,
    int? SoNgayOnKiemTra,
    int? SoNgayThucHoc,
    int? SoNgayNghiLe,
    int? TongSoNgay,
    string? GhiChu,
    bool TrangThaiNguon,
    int? TtXuLy,
    int? HinhThucDaoTao);

public sealed record QlhvImportGiaoVienWriteModel(
    string SourceProfileCode,
    string SourceMaGV,
    string SourceHash,
    string MaGV,
    string HoTen,
    string? HoTenDem,
    string? TenGV,
    DateTime? NgaySinh,
    string? SoCCCD,
    string? GioiTinh,
    string? DienThoai,
    string? DiaChi,
    string? NoiCtMaDvhc,
    string? NoiCtMaDvql,
    string? HinhThucTuyenDung,
    string? TrinhDoVanHoa,
    string? TrinhDoChuyenMon,
    string? TrinhDoSuPham,
    string? HangGPLX,
    DateTime? NgayCapGPLX,
    DateTime? NgayHetHanGPLX,
    int? ThamNienLaiXe,
    string? MonHocGiangDay,
    string? AnhRelativePath,
    string? GhiChu,
    string? MaCSDT,
    string? MaSoGTVT,
    string? SoQuyetDinhGCN,
    DateTime? NgayQuyetDinhGCN,
    string? LoaiHinhDaoTao,
    string? CacHangGPLXDuocDaoTao,
    string? CauTaoSuaChua,
    string? DaoDucLaiXe,
    string? NghiepVuVanTai,
    string? LuatGTDB,
    string? KyThuatLaiXe,
    string? MaFileTiepNhanXml,
    DateTime? ThoiGianTiepNhanXml,
    string? NoiCapGCN,
    string? LoaiGiaoVien,
    string? CacHangDaCo,
    bool TrangThaiNguon);

public sealed record QlhvImportKhoaHocGiaoVienWriteModel(
    string SourceProfileCode,
    long SourceMaLichLV,
    string SourceMaKhoaHoc,
    string SourceMaGV,
    string SourceHash,
    string MaKhoa,
    string MaGV,
    string? TenGV,
    string? MaMonHoc,
    string? TenMonHoc,
    string? BienSoXe,
    string? LoaiGV,
    int? SoHocVien,
    DateTime? NgayHieuLuc,
    DateTime? NgayHetHieuLuc,
    DateTime? NgayBatDau,
    DateTime? NgayKetThuc,
    bool IsKhoaHocGiaoVien,
    string? GhiChu,
    bool TrangThaiNguon);

public sealed record QlhvImportFullSyncPayload(
    IReadOnlyList<QlhvImportKhoaHocWriteModel> KhoaHocRows,
    IReadOnlyList<QlhvImportGiaoVienWriteModel> GiaoVienRows,
    IReadOnlyList<QlhvImportKhoaHocGiaoVienWriteModel> RelationRows,
    IReadOnlyList<QlhvImportHocVienWriteModel> HocVienRows,
    string BackupSnapshotToken = "",
    IReadOnlyCollection<string>? ExecutableDomains = null,
    IReadOnlyDictionary<string, string>? SkippedDomainReasons = null)
{
    /// <summary>
    /// Privacy-safe identities whose exact source/target divergence has passed the
    /// shared RT03 V9 reviewed-retained evaluator. Recovery writers must preserve
    /// these live target rows and verification must re-evaluate them after commit.
    /// </summary>
    public IReadOnlySet<string> ReviewedRetainedSourceBusinessIdentityHashes { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> DomainsToExecute =>
        ExecutableDomains ?? QlhvImportDomains.Ordered;

    public IReadOnlyDictionary<string, string> DomainSkipReasons =>
        SkippedDomainReasons ??
        new Dictionary<string, string>(StringComparer.Ordinal);
}
