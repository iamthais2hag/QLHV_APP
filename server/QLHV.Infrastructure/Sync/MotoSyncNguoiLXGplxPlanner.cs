using System.Data;

namespace QLHV.Infrastructure.Sync;

public static class MotoSyncNguoiLXGplxPlanner
{
    private static readonly IReadOnlyList<string> SupportedCourseColumns = ["MaKhoaHoc", "MaKH"];

    private static readonly IReadOnlyList<IReadOnlyList<string>> FallbackIdentityColumnSets =
    [
        ["MaDK", "HangGPLX"],
        ["MaDK", "SoGPLX"],
        ["MaDK", "HangGPLX", "SoGPLX"],
        ["MaGPLX"],
    ];

    public static MotoSyncNguoiLXGplxScope? DetectScope(
        IEnumerable<string> sourceColumnNames,
        IEnumerable<string> targetColumnNames)
    {
        var source = sourceColumnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var target = targetColumnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var courseColumn = SupportedCourseColumns.FirstOrDefault(column => source.Contains(column) && target.Contains(column));
        if (courseColumn is not null)
        {
            return new MotoSyncNguoiLXGplxScope(courseColumn, UsesMaDkScope: false);
        }

        return source.Contains("MaDK") && target.Contains("MaDK")
            ? new MotoSyncNguoiLXGplxScope(CourseColumn: null, UsesMaDkScope: true)
            : null;
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

        return FallbackIdentityColumnSets
            .FirstOrDefault(columns => columns.All(commonSafeColumns.Contains))
            ?.ToArray() ?? Array.Empty<string>();
    }

    public static bool IdentityIncludesScope(
        IReadOnlyList<string> identityColumns,
        MotoSyncNguoiLXGplxScope scope)
    {
        if (scope.CourseColumn is not null)
        {
            return identityColumns.Contains(scope.CourseColumn, StringComparer.OrdinalIgnoreCase);
        }

        return scope.UsesMaDkScope &&
               identityColumns.Contains("MaDK", StringComparer.OrdinalIgnoreCase);
    }

    public static DataTable FilterInsertRowsForSelectedScope(
        DataTable sourceRows,
        DataTable targetSelectedScopeKeyRows,
        IReadOnlyList<string> keyColumns,
        MotoSyncNguoiLXGplxScope scope,
        string maKhoaHoc,
        IReadOnlyCollection<string> selectedMaDks)
    {
        var selectedMaDkSet = selectedMaDks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetKeys = BuildRowKeySet(targetSelectedScopeKeyRows, keyColumns);
        var result = sourceRows.Clone();
        foreach (DataRow row in sourceRows.Rows)
        {
            if (!IsInSelectedScope(row, scope, maKhoaHoc, selectedMaDkSet))
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

    public static bool HasCrossScopeKeyConflict(
        DataTable plannedRows,
        DataTable targetOutsideScopeKeyRows,
        IReadOnlyList<string> keyColumns,
        MotoSyncNguoiLXGplxScope scope)
    {
        if (IdentityIncludesScope(keyColumns, scope))
        {
            return false;
        }

        var targetOutsideKeys = BuildRowKeySet(targetOutsideScopeKeyRows, keyColumns);
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

    private static bool IsInSelectedScope(
        DataRow row,
        MotoSyncNguoiLXGplxScope scope,
        string maKhoaHoc,
        HashSet<string> selectedMaDks)
    {
        if (scope.CourseColumn is not null)
        {
            var rowCourse = Convert.ToString(row[scope.CourseColumn])?.Trim() ?? string.Empty;
            return string.Equals(rowCourse, maKhoaHoc, StringComparison.OrdinalIgnoreCase);
        }

        if (!scope.UsesMaDkScope || !row.Table.Columns.Contains("MaDK"))
        {
            return false;
        }

        var maDk = Convert.ToString(row["MaDK"])?.Trim() ?? string.Empty;
        return selectedMaDks.Contains(maDk);
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

public sealed record MotoSyncNguoiLXGplxScope(
    string? CourseColumn,
    bool UsesMaDkScope);
