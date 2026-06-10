using Dapper;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Infrastructure.HocVien;

internal static class HocVienSearchSqlBuilder
{
    private const string FromClause = @"
FROM dbo.App_HocVien";

    private const string SelectColumns = @"
SELECT
    MaDK           AS MaDangKy,
    HoTen          AS HoVaTen,
    NgaySinh,
    GioiTinh,
    SoCCCD         AS SoCccd,
    DiaChiThuongTru,
    AnhRelativePath,
    SoGPLXDaCo     AS SoGplxDaCo,
    MaHangDT,
    HangGPLXHoc    AS HangGplxHoc,
    HangGPLXDaCo   AS HangGplxDaCo,
    NguoiNhanHoSo,
    TenKhoa,
    MaKhoa,
    LastSyncStatus";

    public static (string Sql, DynamicParameters Parameters) BuildCount(HocVienSearchRequest request)
    {
        var normalized = request.Normalized();
        var parameters = new DynamicParameters();
        var where = BuildWhere(normalized, parameters);
        return ("SELECT COUNT(1)" + FromClause + where + ";", parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildPage(HocVienSearchRequest request)
    {
        var normalized = request.Normalized();
        var parameters = new DynamicParameters();
        var where = BuildWhere(normalized, parameters);
        parameters.Add("@Offset", (normalized.Page - 1) * normalized.PageSize);
        parameters.Add("@PageSize", normalized.PageSize);

        var sql = SelectColumns + FromClause + where +
            "\nORDER BY HocVienId ASC" +
            "\nOFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        return (sql, parameters);
    }

    private static string BuildWhere(HocVienSearchRequest request, DynamicParameters parameters)
    {
        var conditions = new List<string> { "IsDeleted = 0" };

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            conditions.Add(
                "(MaDK LIKE @Keyword ESCAPE '\\' OR HoTen LIKE @Keyword ESCAPE '\\' OR SoCCCD LIKE @Keyword ESCAPE '\\')");
            parameters.Add("@Keyword", $"%{EscapeLike(request.Keyword)}%");
        }

        if (!string.IsNullOrWhiteSpace(request.MaKhoa))
        {
            conditions.Add("MaKhoa = @MaKhoa");
            parameters.Add("@MaKhoa", request.MaKhoa);
        }

        if (!string.IsNullOrWhiteSpace(request.HangGplx))
        {
            conditions.Add("(MaHangDT = @HangGplx OR HangGPLXHoc = @HangGplx)");
            parameters.Add("@HangGplx", request.HangGplx);
        }

        if (!string.IsNullOrWhiteSpace(request.MaHangDT))
        {
            conditions.Add("MaHangDT = @MaHangDT");
            parameters.Add("@MaHangDT", request.MaHangDT);
        }

        if (!string.IsNullOrWhiteSpace(request.GioiTinh))
        {
            conditions.Add("GioiTinh = @GioiTinh");
            parameters.Add("@GioiTinh", request.GioiTinh);
        }

        return "\nWHERE " + string.Join("\n  AND ", conditions);
    }

    private static string EscapeLike(string value) => value
        .Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal)
        .Replace("_", @"\_", StringComparison.Ordinal)
        .Replace("[", @"\[", StringComparison.Ordinal);

    public static (string Sql, DynamicParameters Parameters) BuildKhoaLookup(string? keyword, int limit)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@Limit", Math.Clamp(limit, 1, 50));
        AddLookupKeyword(parameters, keyword);

        var sql = @"
WITH DistinctKhoa AS (
    SELECT DISTINCT
        MaKhoa,
        TenKhoa
    FROM dbo.App_HocVien
    WHERE IsDeleted = 0
      AND MaKhoa IS NOT NULL
      AND (
          @Keyword IS NULL
          OR TenKhoa LIKE @KeywordContains ESCAPE '\'
          OR MaKhoa LIKE @KeywordContains ESCAPE '\'
      )
)
SELECT TOP (@Limit)
    MaKhoa,
    TenKhoa,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(TenKhoa, N''))), N'') IS NULL THEN MaKhoa
        ELSE TenKhoa + N' - ' + MaKhoa
    END AS Label
FROM DistinctKhoa
ORDER BY
    CASE
        WHEN @Keyword IS NULL THEN 3
        WHEN TenKhoa LIKE @KeywordPrefix ESCAPE '\' THEN 0
        WHEN MaKhoa LIKE @KeywordPrefix ESCAPE '\' THEN 1
        ELSE 2
    END,
    TenKhoa,
    MaKhoa;";

        return (sql, parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildHangHocLookup(string? keyword, int limit)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@Limit", Math.Clamp(limit, 1, 50));
        AddLookupKeyword(parameters, keyword);

        var sql = @"
WITH DistinctHangHoc AS (
    SELECT DISTINCT
        MaHangDT,
        HangGPLXHoc AS TenHangDT
    FROM dbo.App_HocVien
    WHERE IsDeleted = 0
      AND MaHangDT IS NOT NULL
      AND (
          @Keyword IS NULL
          OR MaHangDT LIKE @KeywordContains ESCAPE '\'
          OR HangGPLXHoc LIKE @KeywordContains ESCAPE '\'
      )
)
SELECT TOP (@Limit)
    MaHangDT,
    TenHangDT,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(TenHangDT, N''))), N'') IS NULL THEN MaHangDT
        ELSE MaHangDT + N' - ' + TenHangDT
    END AS Label
FROM DistinctHangHoc
ORDER BY
    CASE
        WHEN @Keyword IS NULL THEN 3
        WHEN MaHangDT LIKE @KeywordPrefix ESCAPE '\' THEN 0
        WHEN TenHangDT LIKE @KeywordPrefix ESCAPE '\' THEN 1
        ELSE 2
    END,
    MaHangDT,
    TenHangDT;";

        return (sql, parameters);
    }

    private static void AddLookupKeyword(DynamicParameters parameters, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            parameters.Add("@Keyword", null);
            parameters.Add("@KeywordPrefix", null);
            parameters.Add("@KeywordContains", null);
            return;
        }

        var escaped = EscapeLike(keyword.Trim());
        parameters.Add("@Keyword", keyword.Trim());
        parameters.Add("@KeywordPrefix", $"{escaped}%");
        parameters.Add("@KeywordContains", $"%{escaped}%");
    }
}
