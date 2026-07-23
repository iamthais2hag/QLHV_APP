using Dapper;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

internal static class QlhvImportSqlBuilder
{
    public static (string Sql, DynamicParameters Parameters) BuildSourceRead(
        QlhvImportRequest request,
        bool hasKhoaHocMaCsdt)
    {
        var parameters = BuildParameters(request);
        var hocVienCenterFilter = hasKhoaHocMaCsdt
            ? "(LTRIM(RTRIM(nlx.MaDK)) LIKE @MaDkPrefix OR LTRIM(RTRIM(kh.MaCSDT)) = @MaCSDT)"
            : "LTRIM(RTRIM(nlx.MaDK)) LIKE @MaDkPrefix";
        var khoaHocCenterFilter = hasKhoaHocMaCsdt
            ? "LTRIM(RTRIM(kh.MaCSDT)) = @MaCSDT"
            : "LTRIM(RTRIM(kh.MaKH)) LIKE @MaDkPrefix";

        var sql = $@"
SELECT
    nlx.MaDK                              AS MaDK,
    hs.MaKhoaHoc                          AS MaKhoaHoc,
    kh.TenKH                              AS TenKH,
    hs.HangDaoTao                         AS HangDaoTao,
    hdt.TenHangDT                         AS TenHangDT,
    nlx.HoVaTen                           AS HoVaTen,
    TRY_CONVERT(date, nlx.NgaySinh, 112)  AS NgaySinh,
    nlx.SoCMT                             AS SoCMT,
    nlx.GioiTinh                          AS GioiTinh,
    nlx.NoiTT                             AS NoiTT,
    dvhc.TenDayDu                         AS NoiTTTenDayDu,
    hs.SoGPLXDaCo                         AS SoGPLXDaCo,
    hs.HangGPLXDaCo                       AS HangGPLXDaCo,
    hs.NguoiNhanHSo                       AS NguoiNhanHoSo,
    hs.DuongDanAnh                        AS DuongDanAnh,
    hs.ChatLuongAnh                       AS ChatLuongAnh,
    hs.NgayThuNhanAnh                     AS NgayThuNhanAnh,
    hs.NguoiThuNhanAnh                    AS NguoiThuNhanAnh
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
LEFT JOIN dbo.KhoaHoc AS kh ON kh.MaKH = hs.MaKhoaHoc
LEFT JOIN dbo.DM_HangDT AS hdt ON hdt.MaHangDT = hs.HangDaoTao
LEFT JOIN dbo.DM_DVHC AS dvhc ON dvhc.MaDV =
    LTRIM(RTRIM(nlx.NoiTT_MaDVQL)) + LTRIM(RTRIM(nlx.NoiTT_MaDVHC))
WHERE {hocVienCenterFilter}
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(hs.MaKhoaHoc)) = @MaKhoaHoc)
ORDER BY hs.MaKhoaHoc, nlx.HoVaTen, nlx.MaDK;

SELECT
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
ORDER BY kh.MaKH;

SELECT
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
ORDER BY gv.MaGV;

SELECT
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
ORDER BY relation.MaLichLV;";

        return (sql, parameters);
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
