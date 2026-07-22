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

SELECT COUNT(1)
FROM dbo.KhoaHoc AS kh
WHERE {khoaHocCenterFilter}
  AND (@MaKhoaHoc IS NULL OR LTRIM(RTRIM(kh.MaKH)) = @MaKhoaHoc);";

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
