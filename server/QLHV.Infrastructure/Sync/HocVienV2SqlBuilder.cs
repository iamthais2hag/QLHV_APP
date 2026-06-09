using Dapper;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Dựng câu lệnh SQL CHỈ ĐỌC để truy vấn học viên từ CSDT_V2.
/// Cột/nguồn đã xác nhận theo database/reference/V2_schema_full.sql:
///   - dbo.NguoiLX        (MaDK, HoVaTen, NgaySinh varchar(8) yyyyMMdd, GioiTinh char(1), SoCMT, NoiTT)
///   - dbo.NguoiLX_HoSo   (MaDK [PK 1-1], MaKhoaHoc, HangGPLX, SoGPLXDaCo, HangGPLXDaCo, NguoiNhanHSo)
///   - dbo.KhoaHoc        (MaKH, TenKH)
/// Mọi giá trị lọc đều truyền qua tham số Dapper (parameterized) để chống SQL injection.
/// KHÔNG có lệnh ghi (INSERT/UPDATE/DELETE/MERGE) trong lớp này.
/// </summary>
internal static class HocVienV2SqlBuilder
{
    private const string FromJoin = @"
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
LEFT JOIN dbo.KhoaHoc AS kh ON kh.MaKH = hs.MaKhoaHoc";

    private const string SelectColumns = @"
SELECT
    nlx.MaDK                              AS MaDK,
    hs.MaKhoaHoc                          AS MaKhoaHoc,
    kh.TenKH                              AS TenKH,
    hs.HangGPLX                           AS HangGPLX,
    nlx.HoVaTen                           AS HoVaTen,
    TRY_CONVERT(date, nlx.NgaySinh, 112)  AS NgaySinh,   -- NgaySinh luu varchar(8) yyyyMMdd
    nlx.SoCMT                             AS SoCMT,
    nlx.GioiTinh                          AS GioiTinh,    -- char(1), quy doi hien thi o tang tren
    nlx.NoiTT                             AS DiaChiThuongTru,
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
            conditions.Add("hs.HangGPLX = @HangGPLX");
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

    /// <summary>Câu lệnh đọc một trang (chỉ đọc) kèm tham số phân trang OFFSET/FETCH.</summary>
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
