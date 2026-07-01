namespace QLHV.Infrastructure.Sync;

public static class MotoSyncInsertColumnPlanner
{
    public static IReadOnlyList<string> SelectCommonSafeInsertColumns(
        IEnumerable<MotoSyncInsertColumnInfo> sourceColumns,
        IEnumerable<MotoSyncInsertColumnInfo> targetColumns)
    {
        var sourceSafeNames = sourceColumns
            .Where(IsSafeInsertColumn)
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return targetColumns
            .Where(column => IsSafeInsertColumn(column) && sourceSafeNames.Contains(column.Name))
            .Select(column => column.Name)
            .ToArray();
    }

    public static IReadOnlyList<string> FindMissingRequiredTargetColumns(
        IEnumerable<MotoSyncInsertColumnInfo> sourceColumns,
        IEnumerable<MotoSyncInsertColumnInfo> targetColumns)
    {
        var sourceNames = sourceColumns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return targetColumns
            .Where(column => IsSafeInsertColumn(column))
            .Where(column => !column.IsNullable && !column.HasDefault && !sourceNames.Contains(column.Name))
            .Select(column => column.Name)
            .ToArray();
    }

    public static bool IsSafeInsertColumn(MotoSyncInsertColumnInfo column)
        => !column.IsIdentity &&
           !column.IsComputed &&
           !column.IsRowVersion &&
           !IsBinaryType(column.DataType);

    public static bool IsBinaryType(string dataType)
        => dataType is "binary" or "varbinary" or "image" or "timestamp" or "rowversion";
}

public sealed record MotoSyncInsertColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    bool IsRowVersion,
    bool HasDefault);
