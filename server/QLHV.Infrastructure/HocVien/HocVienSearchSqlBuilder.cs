using Dapper;
using QLHV.Application.HocVien.Dtos;

namespace QLHV.Infrastructure.HocVien;

internal static class HocVienSearchSqlBuilder
{
    private const string FromClause = @"
FROM dbo.App_HocVien";

    private const string SelectColumns = @"
SELECT
    HocVienId,
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

    public static (string Sql, DynamicParameters Parameters) BuildExport(
        HocVienSearchRequest request,
        int maxRows)
    {
        var normalized = request.Normalized();
        var parameters = new DynamicParameters();
        var where = BuildWhere(normalized, parameters);
        parameters.Add("@MaxRows", Math.Max(1, maxRows));

        var sql = SelectColumns.Replace("SELECT", "SELECT TOP (@MaxRows)", StringComparison.Ordinal) +
            FromClause + where +
            "\nORDER BY HocVienId ASC;";

        return (sql, parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildById(int hocVienId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@HocVienId", hocVienId);
        return (SelectColumns + FromClause + "\nWHERE IsDeleted = 0\n  AND HocVienId = @HocVienId;", parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildByIds(
        IReadOnlyList<int> hocVienIds,
        int maxRows)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@HocVienIds", hocVienIds.Distinct().ToArray());
        parameters.Add("@MaxRows", Math.Max(1, maxRows));
        var sql = SelectColumns.Replace("SELECT", "SELECT TOP (@MaxRows)", StringComparison.Ordinal) +
            FromClause +
            "\nWHERE IsDeleted = 0" +
            "\n  AND HocVienId IN @HocVienIds" +
            "\nORDER BY HocVienId ASC;";
        return (sql, parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildByCourse(
        string maKhoa,
        int maxRows)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@MaKhoa", maKhoa);
        parameters.Add("@MaxRows", Math.Max(1, maxRows));
        var sql = SelectColumns.Replace("SELECT", "SELECT TOP (@MaxRows)", StringComparison.Ordinal) +
            FromClause +
            "\nWHERE IsDeleted = 0" +
            "\n  AND MaKhoa = @MaKhoa" +
            "\nORDER BY HocVienId ASC;";
        return (sql, parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildKhoaLookup(
        string? keyword,
        int limit,
        string? maHangDT = null)
    {
        var parameters = BuildLookupParameters(keyword, limit);
        var normalizedMaHangDT = string.IsNullOrWhiteSpace(maHangDT) ? null : maHangDT.Trim();
        var maHangFilter = string.Empty;
        if (normalizedMaHangDT is not null)
        {
            maHangFilter = "\n      AND UPPER(LTRIM(RTRIM(MaHangDT))) = UPPER(@MaHangDT)";
            parameters.Add("@MaHangDT", normalizedMaHangDT);
        }

        var sql = $@"
WITH DistinctKhoa AS
(
    SELECT DISTINCT
        LTRIM(RTRIM(MaKhoa)) AS MaKhoa,
        NULLIF(LTRIM(RTRIM(TenKhoa)), N'') AS TenKhoa
    FROM dbo.App_HocVien
    WHERE IsDeleted = 0
{maHangFilter}
      AND NULLIF(LTRIM(RTRIM(MaKhoa)), N'') IS NOT NULL
)
SELECT TOP (@Limit)
    MaKhoa,
    TenKhoa,
    CASE
        WHEN TenKhoa IS NULL THEN MaKhoa
        ELSE CONCAT(TenKhoa, N' - ', MaKhoa)
    END AS Label
FROM DistinctKhoa
WHERE @LookupContains IS NULL
   OR UPPER(TenKhoa) LIKE UPPER(@LookupContains) ESCAPE '\'
   OR UPPER(MaKhoa) LIKE UPPER(@LookupContains) ESCAPE '\'
ORDER BY
    CASE
        WHEN @LookupPrefix IS NULL THEN 3
        WHEN UPPER(TenKhoa) LIKE UPPER(@LookupPrefix) ESCAPE '\' THEN 0
        WHEN UPPER(MaKhoa) LIKE UPPER(@LookupPrefix) ESCAPE '\' THEN 1
        ELSE 2
    END,
    TenKhoa,
    MaKhoa;";

        return (sql, parameters);
    }

    public static (string Sql, DynamicParameters Parameters) BuildHangHocLookup(
        string? keyword,
        int limit)
    {
        var parameters = BuildLookupParameters(keyword, limit);
        var sql = @"
WITH DistinctHangHoc AS
(
    SELECT DISTINCT
        LTRIM(RTRIM(MaHangDT)) AS MaHangDT,
        NULLIF(LTRIM(RTRIM(HangGPLXHoc)), N'') AS HangGplxHoc
    FROM dbo.App_HocVien
    WHERE IsDeleted = 0
      AND NULLIF(LTRIM(RTRIM(MaHangDT)), N'') IS NOT NULL
)
SELECT TOP (@Limit)
    MaHangDT,
    HangGplxHoc AS TenHangDT,
    HangGplxHoc,
    CASE
        WHEN HangGplxHoc IS NULL THEN MaHangDT
        ELSE CONCAT(MaHangDT, N' - ', HangGplxHoc)
    END AS Label
FROM DistinctHangHoc
WHERE @LookupContains IS NULL
   OR UPPER(MaHangDT) LIKE UPPER(@LookupContains) ESCAPE '\'
   OR UPPER(HangGplxHoc) LIKE UPPER(@LookupContains) ESCAPE '\'
ORDER BY
    CASE
        WHEN @LookupPrefix IS NULL THEN 3
        WHEN UPPER(MaHangDT) LIKE UPPER(@LookupPrefix) ESCAPE '\' THEN 0
        WHEN UPPER(HangGplxHoc) LIKE UPPER(@LookupPrefix) ESCAPE '\' THEN 1
        ELSE 2
    END,
    MaHangDT,
    HangGplxHoc;";

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

        if (!string.IsNullOrWhiteSpace(request.MaHangDT))
        {
            conditions.Add("MaHangDT = @MaHangDT");
            parameters.Add("@MaHangDT", request.MaHangDT);
        }

        if (!string.IsNullOrWhiteSpace(request.HangGplx))
        {
            conditions.Add("(MaHangDT = @HangGplx OR HangGPLXHoc = @HangGplx)");
            parameters.Add("@HangGplx", request.HangGplx);
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

    private static DynamicParameters BuildLookupParameters(string? keyword, int limit)
    {
        var normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var parameters = new DynamicParameters();
        parameters.Add("@Limit", Math.Clamp(limit <= 0 ? 20 : limit, 1, 50));
        parameters.Add("@LookupPrefix", normalizedKeyword is null ? null : $"{EscapeLike(normalizedKeyword)}%");
        parameters.Add("@LookupContains", normalizedKeyword is null ? null : $"%{EscapeLike(normalizedKeyword)}%");
        return parameters;
    }
}
