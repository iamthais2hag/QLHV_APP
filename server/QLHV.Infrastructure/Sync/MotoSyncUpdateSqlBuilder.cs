namespace QLHV.Infrastructure.Sync;

public static class MotoSyncUpdateSqlBuilder
{
    public const string NguoiLxTable = "NguoiLX";
    public const string NguoiLxHoSoTable = "NguoiLX_HoSo";

    public static string BuildUpdateSql(string tableName, IReadOnlyList<string> changedColumns)
    {
        if (changedColumns.Count == 0)
        {
            throw new ArgumentException("Changed columns are required.", nameof(changedColumns));
        }

        var assignments = changedColumns
            .Select((column, index) => $"{Quote(column)} = @v{index}")
            .ToArray();
        return tableName switch
        {
            NguoiLxTable => $"UPDATE dbo.[NguoiLX] SET {string.Join(", ", assignments)} WHERE MaDK = @MaDK;",
            NguoiLxHoSoTable => $"UPDATE dbo.[NguoiLX_HoSo] SET {string.Join(", ", assignments)} WHERE MaDK = @MaDK AND MaKhoaHoc = @MaKhoaHoc;",
            _ => throw new InvalidOperationException($"Bang {tableName} khong nam trong allowlist update Moto sync."),
        };
    }

    public static bool RequiresMaKhoaHocScope(string tableName)
        => string.Equals(tableName, NguoiLxHoSoTable, StringComparison.Ordinal);

    public static bool IsSupportedUpdateTable(string tableName)
        => string.Equals(tableName, NguoiLxTable, StringComparison.Ordinal) ||
           string.Equals(tableName, NguoiLxHoSoTable, StringComparison.Ordinal);

    public static bool IsKeyOrScopeColumn(string columnName)
        => string.Equals(columnName, "MaDK", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(columnName, "MaKhoaHoc", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(columnName, "ID", StringComparison.OrdinalIgnoreCase) ||
           columnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase) ||
           columnName.EndsWith("Id", StringComparison.Ordinal);

    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
