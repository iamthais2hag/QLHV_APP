namespace QLHV.Infrastructure.Sync;

internal static class MotoSyncDonViGTVTOptionPlanner
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    private static readonly string[] MaSoGTVTCandidates = ["MaSoGTVT"];

    public static int NormalizeTake(int take)
    {
        if (take <= 0)
        {
            return DefaultTake;
        }

        return Math.Min(take, MaxTake);
    }

    public static MotoSyncDonViGTVTDisplayColumns DetectDisplayColumns(IEnumerable<string> columnNames)
    {
        var names = columnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new MotoSyncDonViGTVTDisplayColumns(
            names.Contains("TenDV") ? "TenDV" : null,
            MaSoGTVTCandidates.FirstOrDefault(names.Contains));
    }

    public static string BuildOptionsSql(MotoSyncDonViGTVTQueryShape shape, bool hasSearch)
    {
        var searchFilter = hasSearch
            ? "WHERE (LTRIM(RTRIM([MaDV])) LIKE @SearchLike" +
              (shape.DisplayColumns.TenDVColumn is null ? ")" : $" OR {Quote(shape.DisplayColumns.TenDVColumn)} LIKE @SearchLike)")
            : string.Empty;
        var orderBy = hasSearch
            ? $@"ORDER BY
    CASE
        WHEN LTRIM(RTRIM([MaDV])) = @SearchExact THEN 0
        WHEN LTRIM(RTRIM([MaDV])) LIKE @SearchPrefix THEN 1
        WHEN LTRIM(RTRIM([MaDV])) LIKE @SearchLike THEN 2
        {(shape.DisplayColumns.TenDVColumn is null ? string.Empty : $"WHEN {Quote(shape.DisplayColumns.TenDVColumn)} LIKE @SearchLike THEN 3")}
        ELSE 4
    END,
    [MaDV]"
            : "ORDER BY [MaDV]";

        return $@"
SELECT TOP (@Take)
    LTRIM(RTRIM([MaDV])) AS [MaDV],
    {SelectOptionalColumn(shape.DisplayColumns.TenDVColumn, "TenDV")},
    {SelectOptionalColumn(shape.DisplayColumns.MaSoGTVTColumn, "MaSoGTVT")}
FROM dbo.DM_DonViGTVT
{searchFilter}
{orderBy};";
    }

    private static string SelectOptionalColumn(string? columnName, string alias)
        => columnName is null
            ? $"CAST(NULL AS nvarchar(4000)) AS {Quote(alias)}"
            : $"{Quote(columnName)} AS {Quote(alias)}";

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

internal sealed record MotoSyncDonViGTVTDisplayColumns(
    string? TenDVColumn,
    string? MaSoGTVTColumn);

internal sealed record MotoSyncDonViGTVTQueryShape(
    MotoSyncDonViGTVTDisplayColumns DisplayColumns);
