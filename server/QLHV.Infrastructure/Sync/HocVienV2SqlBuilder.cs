using Dapper;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Dựng câu lệnh SQL CHỈ ĐỌC để truy vấn học viên từ CSDT_V2.
/// Cột/nguồn đã xác nhận theo database/reference/V2_schema_full.sql:
///   - dbo.NguoiLX        (MaDK, HoVaTen, NgaySinh varchar(8) yyyyMMdd, GioiTinh char(1), SoCMT, NoiTT, NoiTT_MaDVQL, NoiTT_MaDVHC)
///   - dbo.NguoiLX_HoSo   (MaDK [PK 1-1], MaKhoaHoc, HangDaoTao, SoGPLXDaCo, HangGPLXDaCo, NguoiNhanHSo)
///   - dbo.KhoaHoc        (MaKH, TenKH)
///   - dbo.DM_HangDT      (MaHangDT, TenHangDT)
///   - dbo.DM_DVHC        (MaDV, TenDayDu)
/// Mọi giá trị lọc đều truyền qua tham số Dapper (parameterized) để chống SQL injection.
/// KHÔNG có lệnh ghi (INSERT/UPDATE/DELETE/MERGE) trong lớp này.
///
/// Quy tắc dữ liệu (xem docs/hoc-vien-data-rules.md):
///   - Trả về giá trị gốc; không tự chuyển CMND 9 số sang CCCD 12 số; không hardcode quy đổi GioiTinh.
///   - Mặc định KHÔNG lọc theo TrangThai cho tới khi ý nghĩa được xác nhận chính thức.
/// </summary>
internal static class HocVienV2SqlBuilder
{
    private const string FromJoin = @"
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
LEFT JOIN dbo.KhoaHoc AS kh ON kh.MaKH = hs.MaKhoaHoc
LEFT JOIN dbo.DM_HangDT AS hdt ON hdt.MaHangDT = hs.HangDaoTao
LEFT JOIN dbo.DM_DVHC AS dvhc ON dvhc.MaDV = (
    LTRIM(RTRIM(nlx.NoiTT_MaDVQL)) + LTRIM(RTRIM(nlx.NoiTT_MaDVHC))
)";

    private const string SelectColumns = @"
SELECT
    nlx.MaDK                              AS MaDK,
    hs.MaKhoaHoc                          AS MaKhoaHoc,
    kh.TenKH                              AS TenKH,
    hs.HangDaoTao                         AS HangDaoTao,
    hdt.TenHangDT                         AS TenHangDT,
    nlx.HoVaTen                           AS HoVaTen,
    TRY_CONVERT(date, nlx.NgaySinh, 112)  AS NgaySinh,   -- NgaySinh luu varchar(8) yyyyMMdd
    nlx.SoCMT                             AS SoCMT,
    nlx.GioiTinh                          AS GioiTinh,    -- char(1), quy doi hien thi o tang tren
    nlx.NoiTT                             AS NoiTT,
    dvhc.TenDayDu                         AS NoiTTTenDayDu,
    hs.SoGPLXDaCo                         AS SoGPLXDaCo,
    hs.HangGPLXDaCo                       AS HangGPLXDaCo,
    hs.NguoiNhanHSo                       AS NguoiNhanHoSo";

    /// <summary>
    /// Tạo mệnh đề WHERE động theo bộ lọc và nạp tham số. Trả về chuỗi WHERE (có thể rỗng).
    /// </summary>
    private static string BuildWhere(HocVienSourceFilter filter, DynamicParameters parameters)
    {
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            conditions.Add("(nlx.HoVaTen LIKE @Keyword OR nlx.MaDK LIKE @Keyword OR nlx.SoCMT LIKE @Keyword)");
            parameters.Add("@Keyword", $"%{filter.Keyword}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.MaKhoa))
        {
            conditions.Add("hs.MaKhoaHoc = @MaKhoa");
            parameters.Add("@MaKhoa", filter.MaKhoa);
        }

        if (!string.IsNullOrWhiteSpace(filter.HangGPLX))
        {
            conditions.Add("(hs.HangDaoTao = @HangGPLX OR hdt.TenHangDT = @HangGPLX)");
            parameters.Add("@HangGPLX", filter.HangGPLX);
        }

        if (!string.IsNullOrWhiteSpace(filter.GioiTinh))
        {
            conditions.Add("nlx.GioiTinh = @GioiTinh");
            parameters.Add("@GioiTinh", filter.GioiTinh);
        }

        return conditions.Count == 0 ? string.Empty : "\nWHERE " + string.Join("\n  AND ", conditions);
    }

    /// <summary>Câu lệnh đếm (chỉ đọc) kèm tham số.</summary>
    public static (string Sql, DynamicParameters Parameters) BuildCount(HocVienSourceFilter filter)
    {
        var parameters = new DynamicParameters();
        var where = BuildWhere(filter, parameters);
        var sql = "SELECT COUNT(1)" + FromJoin + where + ";";
        return (sql, parameters);
    }

    /// <summary>Cau lenh chan doan nguon V2 (chi SELECT aggregate, khong tra du lieu dinh danh raw).</summary>
    public static string BuildDiagnostics()
    {
        var cte = @"
WITH SourceRows AS (
    SELECT
        nlx.MaDK AS NlxMaDK,
        hs.MaDK AS HsMaDK,
        COALESCE(NULLIF(LTRIM(RTRIM(nlx.MaDK)), ''), NULLIF(LTRIM(RTRIM(hs.MaDK)), '')) AS EffectiveMaDK,
        nlx.HoVaTen,
        nlx.GioiTinh,
        nlx.SoCMT,
        nlx.NgaySinh AS NgaySinhRaw,
        nlx.NoiTT_MaDVQL,
        nlx.NoiTT_MaDVHC,
        hs.MaKhoaHoc,
        kh.MaKH AS KhoaHocMaKH,
        hs.HangDaoTao,
        hdt.MaHangDT AS HangDtMaHangDT,
        dvhc.MaDV AS DvhcMaDV
" + FromJoin + @"
)
";

        return cte + @"
SELECT
    COUNT(1) AS SourceRows,
    COALESCE(SUM(CASE
        WHEN NULLIF(LTRIM(RTRIM(NlxMaDK)), '') IS NULL
          OR NULLIF(LTRIM(RTRIM(HsMaDK)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingMaDkCount,
    COALESCE(SUM(CASE WHEN NULLIF(LTRIM(RTRIM(HoVaTen)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingHoTenCount,
    COALESCE(SUM(CASE WHEN NULLIF(LTRIM(RTRIM(NgaySinhRaw)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingNgaySinhCount,
    COALESCE(SUM(CASE
        WHEN NULLIF(LTRIM(RTRIM(NgaySinhRaw)), '') IS NOT NULL
         AND TRY_CONVERT(date, NgaySinhRaw, 112) IS NULL THEN 1 ELSE 0 END), 0) AS NgaySinhParseIssueCount,
    COALESCE(SUM(CASE WHEN NULLIF(LTRIM(RTRIM(HangDaoTao)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingHangDaoTaoCount,
    COALESCE(SUM(CASE
        WHEN NULLIF(LTRIM(RTRIM(HangDaoTao)), '') IS NOT NULL
         AND HangDtMaHangDT IS NULL THEN 1 ELSE 0 END), 0) AS HangDaoTaoUnmatchedDmHangDtCount,
    COALESCE(SUM(CASE
        WHEN NULLIF(LTRIM(RTRIM(NoiTT_MaDVQL)), '') IS NULL
          OR NULLIF(LTRIM(RTRIM(NoiTT_MaDVHC)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingNoiTTCodesCount,
    COALESCE(SUM(CASE
        WHEN NULLIF(LTRIM(RTRIM(NoiTT_MaDVQL)), '') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(NoiTT_MaDVHC)), '') IS NOT NULL
         AND DvhcMaDV IS NULL THEN 1 ELSE 0 END), 0) AS NoiTTUnmatchedDmDvhcCount,
    COALESCE(SUM(CASE WHEN NULLIF(LTRIM(RTRIM(MaKhoaHoc)), '') IS NULL THEN 1 ELSE 0 END), 0) AS MissingMaKhoaHocCount,
    COALESCE(SUM(CASE
        WHEN NULLIF(LTRIM(RTRIM(MaKhoaHoc)), '') IS NOT NULL
         AND KhoaHocMaKH IS NULL THEN 1 ELSE 0 END), 0) AS MaKhoaHocUnmatchedKhoaHocCount
FROM SourceRows;
"
        + cte + @"
SELECT
    COUNT(1) AS DuplicateMaDkCount,
    COALESCE(SUM(DuplicateRows), 0) AS DuplicateMaDkRowCount
FROM (
    SELECT EffectiveMaDK, COUNT(1) AS DuplicateRows
    FROM SourceRows
    WHERE EffectiveMaDK IS NOT NULL
    GROUP BY EffectiveMaDK
    HAVING COUNT(1) > 1
) AS duplicates;
"
        + cte + @"
SELECT
    CASE
        WHEN NULLIF(LTRIM(RTRIM(GioiTinh)), '') IS NULL THEN 'null-empty'
        ELSE LTRIM(RTRIM(GioiTinh))
    END AS Value,
    COUNT(1) AS Total
FROM SourceRows
GROUP BY CASE
    WHEN NULLIF(LTRIM(RTRIM(GioiTinh)), '') IS NULL THEN 'null-empty'
    ELSE LTRIM(RTRIM(GioiTinh))
END
ORDER BY Total DESC, Value;
"
        + cte + @"
SELECT
    CASE
        WHEN NULLIF(LTRIM(RTRIM(SoCMT)), '') IS NULL THEN 'null-empty'
        WHEN LEN(LTRIM(RTRIM(SoCMT))) = 9 THEN '9'
        WHEN LEN(LTRIM(RTRIM(SoCMT))) = 12 THEN '12'
        ELSE 'other'
    END AS Value,
    COUNT(1) AS Total
FROM SourceRows
GROUP BY CASE
    WHEN NULLIF(LTRIM(RTRIM(SoCMT)), '') IS NULL THEN 'null-empty'
    WHEN LEN(LTRIM(RTRIM(SoCMT))) = 9 THEN '9'
    WHEN LEN(LTRIM(RTRIM(SoCMT))) = 12 THEN '12'
    ELSE 'other'
END
ORDER BY Value;";
    }

    /// <summary>Cau lenh doc mot trang hoc vien tu nguon V2 (chi SELECT voi OFFSET/FETCH).</summary>
    public static (string Sql, DynamicParameters Parameters) BuildPage(
        HocVienSourceFilter filter,
        int offset,
        int pageSize)
    {
        var parameters = new DynamicParameters();
        var where = BuildWhere(filter, parameters);
        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var sql = SelectColumns + FromJoin + where +
            "\nORDER BY hs.MaKhoaHoc, nlx.HoVaTen, nlx.MaDK" +
            "\nOFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        return (sql, parameters);
    }
}
