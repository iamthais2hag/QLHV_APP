using System.Globalization;

namespace QLHV.Infrastructure.Sync;

internal static class MotoSyncKhoaHocOptionPlanner
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    private static readonly string[] KhoaHocKeyCandidates = ["MaKH", "MaKhoaHoc"];
    private static readonly string[] HoSoCourseKeyCandidates = ["MaKhoaHoc", "MaKH"];
    private static readonly string[] TenKhoaHocCandidates = ["TenKhoaHoc", "TenKH", "TenKhoa"];
    private static readonly string[] HangCandidates = ["HangDaoTao", "HangGPLX", "HangGPLXHoc", "MaHangDT"];
    private static readonly string[] NgayKhaiGiangCandidates = ["NgayKhaiGiang", "NgayKG"];

    public static int NormalizeTake(int take)
    {
        if (take <= 0)
        {
            return DefaultTake;
        }

        return Math.Min(take, MaxTake);
    }

    public static string? DetectKhoaHocCourseKeyColumn(IEnumerable<string> columnNames)
        => FirstExisting(columnNames, KhoaHocKeyCandidates);

    public static string? DetectHoSoCourseKeyColumn(IEnumerable<string> columnNames)
        => FirstExisting(columnNames, HoSoCourseKeyCandidates);

    public static MotoSyncKhoaHocDisplayColumns DetectDisplayColumns(IEnumerable<string> columnNames)
        => new(
            FirstExisting(columnNames, TenKhoaHocCandidates),
            FirstExisting(columnNames, HangCandidates),
            FirstExisting(columnNames, NgayKhaiGiangCandidates));

    public static bool IsHangGplxColumn(string? columnName)
        => columnName is not null &&
           (string.Equals(columnName, "HangGPLX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(columnName, "HangGPLXHoc", StringComparison.OrdinalIgnoreCase));

    public static string? FormatNgayKhaiGiang(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return NullIfWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    public static string BuildOptionsSql(
        MotoSyncKhoaHocQueryShape shape,
        bool hasSearch)
    {
        var searchFilter = hasSearch
            ? $"WHERE ({Quote(shape.CourseKeyColumn)} LIKE @SearchLike" +
              (shape.DisplayColumns.TenKhoaHocColumn is null ? ")" : $" OR {Quote(shape.DisplayColumns.TenKhoaHocColumn)} LIKE @SearchLike)")
            : string.Empty;
        var orderBy = shape.DisplayColumns.NgayKhaiGiangColumn is null
            ? $"{Quote(shape.CourseKeyColumn)} DESC"
            : $"{Quote(shape.DisplayColumns.NgayKhaiGiangColumn)} DESC, {Quote(shape.CourseKeyColumn)} DESC";

        return $@"
SELECT TOP (@Take)
    {Quote(shape.CourseKeyColumn)} AS MaKhoaHoc,
    {SelectOptionalColumn(shape.DisplayColumns.TenKhoaHocColumn, "TenKhoaHoc")},
    {SelectOptionalColumn(shape.DisplayColumns.HangColumn, "HangValue")},
    {SelectOptionalColumn(shape.DisplayColumns.NgayKhaiGiangColumn, "NgayKhaiGiang")}
FROM dbo.KhoaHoc
{searchFilter}
ORDER BY {orderBy};";
    }

    private static string SelectOptionalColumn(string? columnName, string alias)
        => columnName is null
            ? $"CAST(NULL AS nvarchar(4000)) AS {Quote(alias)}"
            : $"{Quote(columnName)} AS {Quote(alias)}";

    private static string? FirstExisting(IEnumerable<string> columnNames, IEnumerable<string> candidates)
    {
        var names = columnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(names.Contains);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

internal sealed record MotoSyncKhoaHocDisplayColumns(
    string? TenKhoaHocColumn,
    string? HangColumn,
    string? NgayKhaiGiangColumn);

internal sealed record MotoSyncKhoaHocQueryShape(
    string CourseKeyColumn,
    MotoSyncKhoaHocDisplayColumns DisplayColumns);
