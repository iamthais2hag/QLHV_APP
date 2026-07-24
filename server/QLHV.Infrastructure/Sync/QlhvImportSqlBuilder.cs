using Dapper;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

internal static class QlhvImportSqlBuilder
{
    public static (string Sql, DynamicParameters Parameters) BuildSourceRead(
        QlhvImportRequest request,
        bool hasKhoaHocMaCsdt)
    {
        var reads = BuildSourceReads(
            request,
            new QlhvImportSourceReadCapabilities(
                KhoaHocExists: true,
                KhoaHocStudentJoinReady: true,
                GiaoVienExists: true,
                RelationExists: true,
                DmHangDtExists: true,
                DmHangDtJoinReady: true,
                DmDvhcExists: true,
                DmDvhcJoinReady: true,
                KhoaHocHasMaCsdt: hasKhoaHocMaCsdt,
                HasDuongDanAnh: true,
                HasChatLuongAnh: true,
                HasNgayThuNhanAnh: true,
                HasNguoiThuNhanAnh: true));

        return (
            string.Join(
                Environment.NewLine,
                new[] { reads.HocVienSql, reads.KhoaHocSql, reads.GiaoVienSql, reads.RelationSql }
                    .Where(sql => !string.IsNullOrWhiteSpace(sql))),
            reads.Parameters);
    }

    public static QlhvImportSourceReadSql BuildSourceReads(
        QlhvImportRequest request,
        QlhvImportSourceReadCapabilities capabilities)
    {
        var parameters = BuildParameters(request);
        var hocVienCenterFilter =
            capabilities.KhoaHocStudentJoinReady && capabilities.KhoaHocHasMaCsdt
            ? "(LTRIM(RTRIM(nlx.MaDK)) LIKE @MaDkPrefix OR LTRIM(RTRIM(kh.MaCSDT)) = @MaCSDT)"
            : "LTRIM(RTRIM(nlx.MaDK)) LIKE @MaDkPrefix";
        var khoaHocCenterFilter = capabilities.KhoaHocHasMaCsdt
            ? "LTRIM(RTRIM(kh.MaCSDT)) = @MaCSDT"
            : "LTRIM(RTRIM(kh.MaKH)) LIKE @MaDkPrefix";
        var khoaHocJoin = capabilities.KhoaHocStudentJoinReady
            ? "LEFT JOIN dbo.KhoaHoc AS kh ON kh.MaKH = hs.MaKhoaHoc"
            : string.Empty;
        var hangDtJoin = capabilities.DmHangDtJoinReady
            ? "LEFT JOIN dbo.DM_HangDT AS hdt ON hdt.MaHangDT = hs.HangDaoTao"
            : string.Empty;
        var dvhcJoin = capabilities.DmDvhcJoinReady
            ? @"LEFT JOIN dbo.DM_DVHC AS dvhc ON dvhc.MaDV =
    LTRIM(RTRIM(nlx.NoiTT_MaDVQL)) + LTRIM(RTRIM(nlx.NoiTT_MaDVHC))"
            : string.Empty;
        var tenKhoa = capabilities.KhoaHocStudentJoinReady
            ? "kh.TenKH"
            : "CAST(NULL AS nvarchar(255))";
        var tenHangDt = capabilities.DmHangDtJoinReady
            ? "hdt.TenHangDT"
            : "CAST(NULL AS nvarchar(255))";
        var noiTtTenDayDu = capabilities.DmDvhcJoinReady
            ? "dvhc.TenDayDu"
            : "CAST(NULL AS nvarchar(500))";
        var duongDanAnh = capabilities.HasDuongDanAnh
            ? "hs.DuongDanAnh"
            : "CAST(NULL AS nvarchar(500))";
        var chatLuongAnh = capabilities.HasChatLuongAnh
            ? "hs.ChatLuongAnh"
            : "CAST(NULL AS int)";
        var ngayThuNhanAnh = capabilities.HasNgayThuNhanAnh
            ? "hs.NgayThuNhanAnh"
            : "CAST(NULL AS datetime2)";
        var nguoiThuNhanAnh = capabilities.HasNguoiThuNhanAnh
            ? "hs.NguoiThuNhanAnh"
            : "CAST(NULL AS nvarchar(100))";

        var hocVienSql = $@"
SELECT
    nlx.MaDK                              AS MaDK,
    hs.MaKhoaHoc                          AS MaKhoaHoc,
    {tenKhoa}                             AS TenKH,
    hs.HangDaoTao                         AS HangDaoTao,
    {tenHangDt}                           AS TenHangDT,
    nlx.HoVaTen                           AS HoVaTen,
    TRY_CONVERT(date, nlx.NgaySinh, 112)  AS NgaySinh,
    nlx.SoCMT                             AS SoCMT,
    nlx.GioiTinh                          AS GioiTinh,
    nlx.NoiTT                             AS NoiTT,
    {noiTtTenDayDu}                       AS NoiTTTenDayDu,
    hs.SoGPLXDaCo                         AS SoGPLXDaCo,
    hs.HangGPLXDaCo                       AS HangGPLXDaCo,
    hs.NguoiNhanHSo                       AS NguoiNhanHoSo,
    {duongDanAnh}                          AS DuongDanAnh,
    {chatLuongAnh}                        AS ChatLuongAnh,
    {ngayThuNhanAnh}                      AS NgayThuNhanAnh,
    {nguoiThuNhanAnh}                     AS NguoiThuNhanAnh
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
{khoaHocJoin}
{hangDtJoin}
{dvhcJoin}
WHERE {hocVienCenterFilter}
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(hs.MaKhoaHoc)) = @MaKhoaHoc)
ORDER BY hs.MaKhoaHoc, nlx.HoVaTen, nlx.MaDK;";

        var khoaHocSql = capabilities.KhoaHocExists
            ? $@"SELECT
    kh.MaKH,
    kh.MaCSDT,
    kh.MaSoGTVT,
    kh.TenKH,
    kh.HangGPLX,
    kh.HangDT,
    kh.SoQD_KhaiGiang,
    kh.NgayQD_KhaiGiang,
    kh.NgayKG,
    kh.NgayBG,
    kh.MucTieuDT,
    kh.NgayThi,
    kh.NgaySH,
    kh.TongSoHV,
    kh.SoHVTotNghiep,
    kh.SoHVDuocCapGPLX,
    kh.ThoiGianDT,
    kh.SoNgayOnKT,
    kh.SoNgayThucHoc,
    kh.SoNgayNghiLe,
    kh.TongSoNgay,
    kh.GhiChu,
    kh.TrangThai,
    kh.TT_Xuly,
    kh.HTDaoTao
FROM dbo.KhoaHoc AS kh
WHERE {khoaHocCenterFilter}
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(kh.MaKH)) = @MaKhoaHoc)
ORDER BY kh.MaKH;"
            : null;

        var giaoVienSql = capabilities.GiaoVienExists
            ? @"SELECT
    gv.MaGV,
    gv.MaSoGTVT,
    gv.MaCSDT,
    gv.HoTenDem,
    gv.TenGV,
    gv.NgaySinh,
    gv.AnhCD,
    gv.SoCMT,
    gv.NoiCT,
    gv.NoiCT_MaDVHC,
    gv.NoiCT_MaDVQL,
    gv.GioiTinh,
    gv.DienThoai,
    gv.HinhThuc_TuyenDung,
    gv.TrinhDo_VanHoa,
    gv.TrinhDo_ChuyenMon,
    gv.TrinhDo_SuPham,
    gv.HangGPLX,
    gv.NgayCapGPLX,
    gv.ThamNien_LaiXe,
    gv.SoQD_GCN,
    gv.NgayQD_GCN,
    gv.LoaiHinh_DaoTao,
    gv.GhiChu,
    gv.TrangThai,
    gv.CacHangGPLXDuocDT,
    gv.CauTaoSuaChua,
    gv.DaoDucLaixe,
    gv.NghiepVuVanTai,
    gv.LuatGTDB,
    gv.KyThuatLaixe,
    gv.MaFileTiepNhanXML,
    gv.ThoiGianTiepNhanXML,
    gv.NgayHHGPLX,
    gv.NoiCapGCN,
    gv.CacMonHoc,
    gv.LoaiGiaoVien,
    gv.CacHangDaCo
FROM dbo.GiaoVien AS gv
WHERE LTRIM(RTRIM(gv.MaCSDT)) = @MaCSDT
ORDER BY gv.MaGV;"
            : null;

        var relationSql = capabilities.RelationExists && capabilities.KhoaHocExists
            ? $@"SELECT
    relation.MaLichLV,
    relation.MaKH,
    relation.MaGV,
    relation.TenGV,
    relation.BienSoXe,
    relation.LoaiGV,
    relation.SoHV,
    relation.NgayHL,
    relation.NgayHetHL,
    relation.GhiChu,
    relation.TrangThai,
    relation.NgayBD,
    relation.NgayKT,
    relation.IsKhoaHocGiaoVien,
    relation.MaMonHoc,
    relation.TenMonHoc
FROM dbo.KhoaHoc_GiaoVien AS relation
INNER JOIN dbo.KhoaHoc AS kh ON kh.MaKH = relation.MaKH
WHERE {khoaHocCenterFilter}
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(relation.MaKH)) = @MaKhoaHoc)
ORDER BY relation.MaLichLV;"
            : null;

        return new QlhvImportSourceReadSql(
            hocVienSql,
            khoaHocSql,
            giaoVienSql,
            relationSql,
            parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildTargetKhoaHocCount(
        QlhvImportRequest request,
        bool appKhoaHocExists,
        bool appKhoaHocHasMaKhoa,
        bool appKhoaHocHasIsDeleted)
    {
        var parameters = BuildParameters(request);
        var appKhoaHocCount = appKhoaHocExists && appKhoaHocHasMaKhoa
            ? $@"SELECT COUNT(1)
FROM dbo.App_KhoaHoc
WHERE LTRIM(RTRIM(MaKhoa)) LIKE @MaDkPrefix
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(MaKhoa)) = @MaKhoaHoc)
  {(appKhoaHocHasIsDeleted ? "AND IsDeleted = 0" : string.Empty)};"
            : "SELECT CAST(0 AS int);";

        return (appKhoaHocCount, parameters);
    }

    private static DynamicParameters BuildParameters(QlhvImportRequest request)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@SourceProfileCode", request.SourceProfileCode);
        parameters.Add("@MaCSDT", request.MaCSDT);
        parameters.Add("@MaDkPrefix", request.MaCSDT + "%");
        parameters.Add("@MaKhoaHoc", request.MaKhoaHoc);
        return parameters;
    }
}

internal sealed record QlhvImportSourceReadCapabilities(
    bool KhoaHocExists,
    bool KhoaHocStudentJoinReady,
    bool GiaoVienExists,
    bool RelationExists,
    bool DmHangDtExists,
    bool DmHangDtJoinReady,
    bool DmDvhcExists,
    bool DmDvhcJoinReady,
    bool KhoaHocHasMaCsdt,
    bool HasDuongDanAnh,
    bool HasChatLuongAnh,
    bool HasNgayThuNhanAnh,
    bool HasNguoiThuNhanAnh);

internal sealed record QlhvImportSourceReadSql(
    string HocVienSql,
    string? KhoaHocSql,
    string? GiaoVienSql,
    string? RelationSql,
    DynamicParameters Parameters);
