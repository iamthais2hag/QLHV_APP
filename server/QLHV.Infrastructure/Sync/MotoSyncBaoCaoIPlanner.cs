using System.Data;

namespace QLHV.Infrastructure.Sync;

public static class MotoSyncBaoCaoIPlanner
{
    private static readonly IReadOnlyList<string> SupportedCourseColumns = ["MaKhoaHoc", "MaKH"];
    private static readonly IReadOnlyList<string> FallbackIdentityColumns = ["MaBCI"];

    public static string? DetectCourseScopeColumn(
        IEnumerable<string> sourceColumnNames,
        IEnumerable<string> targetColumnNames)
    {
        var source = sourceColumnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var target = targetColumnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return SupportedCourseColumns.FirstOrDefault(column => source.Contains(column) && target.Contains(column));
    }

    public static IReadOnlyList<string> ResolveRowIdentityColumns(
        IEnumerable<MotoSyncInsertColumnInfo> sourceColumns,
        IEnumerable<MotoSyncInsertColumnInfo> targetColumns,
        IReadOnlyList<string> sourcePrimaryKeyColumns,
        IReadOnlyList<string> targetPrimaryKeyColumns)
    {
        var commonSafeColumns = MotoSyncInsertColumnPlanner.SelectCommonSafeInsertColumns(
                sourceColumns,
                targetColumns)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sourcePrimaryKeyColumns.Count > 0 || targetPrimaryKeyColumns.Count > 0)
        {
            if (sourcePrimaryKeyColumns.Count == 0 ||
                targetPrimaryKeyColumns.Count == 0 ||
                !sourcePrimaryKeyColumns.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(targetPrimaryKeyColumns))
            {
                return Array.Empty<string>();
            }

            return targetPrimaryKeyColumns.All(commonSafeColumns.Contains)
                ? targetPrimaryKeyColumns
                : Array.Empty<string>();
        }

        return FallbackIdentityColumns
            .Where(commonSafeColumns.Contains)
            .ToArray();
    }

    public static bool IdentityIncludesCourseScope(
        IReadOnlyList<string> identityColumns,
        string courseColumn)
        => identityColumns.Contains(courseColumn, StringComparer.OrdinalIgnoreCase);

    public static DataTable FilterInsertRowsForSelectedCourse(
        DataTable sourceRows,
        DataTable targetSelectedCourseKeyRows,
        IReadOnlyList<string> keyColumns,
        string courseColumn,
        string maKhoaHoc)
    {
        var targetKeys = BuildRowKeySet(targetSelectedCourseKeyRows, keyColumns);
        var result = sourceRows.Clone();
        foreach (DataRow row in sourceRows.Rows)
        {
            var rowCourse = Convert.ToString(row[courseColumn])?.Trim() ?? string.Empty;
            if (!string.Equals(rowCourse, maKhoaHoc, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryBuildRowKey(row, keyColumns, out var key) && !targetKeys.Contains(key))
            {
                result.ImportRow(row);
            }
        }

        return result;
    }

    public static bool HasCrossCourseKeyConflict(
        DataTable plannedRows,
        DataTable targetOutsideCourseKeyRows,
        IReadOnlyList<string> keyColumns,
        string courseColumn)
    {
        if (IdentityIncludesCourseScope(keyColumns, courseColumn))
        {
            return false;
        }

        var targetOutsideKeys = BuildRowKeySet(targetOutsideCourseKeyRows, keyColumns);
        foreach (DataRow plannedRow in plannedRows.Rows)
        {
            if (TryBuildRowKey(plannedRow, keyColumns, out var key) && targetOutsideKeys.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryBuildRowKey(DataRow row, IReadOnlyList<string> keyColumns, out string key)
    {
        var values = new List<string>(keyColumns.Count);
        foreach (var keyColumn in keyColumns)
        {
            if (!row.Table.Columns.Contains(keyColumn))
            {
                key = string.Empty;
                return false;
            }

            var value = Convert.ToString(row[keyColumn])?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                key = string.Empty;
                return false;
            }

            values.Add(value);
        }

        key = string.Join("\u001F", values);
        return true;
    }

    private static HashSet<string> BuildRowKeySet(DataTable rows, IReadOnlyList<string> keyColumns)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in rows.Rows)
        {
            if (TryBuildRowKey(row, keyColumns, out var key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
