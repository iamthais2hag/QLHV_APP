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
            conditions.Add("HangGPLXHoc = @HangGplx");
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
}
