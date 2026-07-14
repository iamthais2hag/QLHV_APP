using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncDonViGTVTOptionPlannerTests
{
    [Fact]
    public void Normalize_take_uses_default_and_max_limit()
    {
        Assert.Equal(20, MotoSyncDonViGTVTOptionPlanner.NormalizeTake(0));
        Assert.Equal(20, MotoSyncDonViGTVTOptionPlanner.NormalizeTake(-1));
        Assert.Equal(75, MotoSyncDonViGTVTOptionPlanner.NormalizeTake(75));
        Assert.Equal(100, MotoSyncDonViGTVTOptionPlanner.NormalizeTake(500));
    }

    [Fact]
    public void Detect_columns_tolerates_missing_optional_ma_so_gtvt()
    {
        var display = MotoSyncDonViGTVTOptionPlanner.DetectDisplayColumns(["MaDV", "TenDV"]);

        Assert.Equal("TenDV", display.TenDVColumn);
        Assert.Null(display.MaSoGTVTColumn);
    }

    [Fact]
    public void Build_options_sql_searches_by_ma_dv_and_ten_dv_with_parameter()
    {
        var shape = new MotoSyncDonViGTVTQueryShape(
            new MotoSyncDonViGTVTDisplayColumns("TenDV", "MaSoGTVT"));

        var sql = MotoSyncDonViGTVTOptionPlanner.BuildOptionsSql(shape, hasSearch: true);

        Assert.Contains("LTRIM(RTRIM([MaDV])) = @SearchExact THEN 0", sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM([MaDV])) LIKE @SearchPrefix THEN 1", sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM([MaDV])) LIKE @SearchLike THEN 2", sql, StringComparison.Ordinal);
        Assert.Contains("[TenDV] LIKE @SearchLike THEN 3", sql, StringComparison.Ordinal);
        Assert.Contains("@SearchLike", sql, StringComparison.Ordinal);
        Assert.Contains("@SearchExact", sql, StringComparison.Ordinal);
        Assert.Contains("@SearchPrefix", sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM([MaDV])) LIKE @SearchLike", sql, StringComparison.Ordinal);
        Assert.Contains("[TenDV] LIKE @SearchLike", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("    [MaDV]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("01001", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("99", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_options_sql_returns_null_for_missing_ma_so_gtvt_column()
    {
        var shape = new MotoSyncDonViGTVTQueryShape(
            new MotoSyncDonViGTVTDisplayColumns("TenDV", null));

        var sql = MotoSyncDonViGTVTOptionPlanner.BuildOptionsSql(shape, hasSearch: false);

        Assert.Contains("CAST(NULL AS nvarchar(4000)) AS [MaSoGTVT]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_options_sql_combines_ranking_and_missing_optional_ma_so_gtvt()
    {
        var shape = new MotoSyncDonViGTVTQueryShape(
            new MotoSyncDonViGTVTDisplayColumns("TenDV", null));

        var sql = MotoSyncDonViGTVTOptionPlanner.BuildOptionsSql(shape, hasSearch: true);

        Assert.Contains("CAST(NULL AS nvarchar(4000)) AS [MaSoGTVT]", sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM([MaDV])) = @SearchExact THEN 0", sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM([MaDV])) LIKE @SearchPrefix THEN 1", sql, StringComparison.Ordinal);
        Assert.Contains("LTRIM(RTRIM([MaDV])) LIKE @SearchLike THEN 2", sql, StringComparison.Ordinal);
        Assert.Contains("[TenDV] LIKE @SearchLike THEN 3", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE (LTRIM(RTRIM([MaDV])) LIKE @SearchLike OR [TenDV] LIKE @SearchLike)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[MaSoGTVT] AS [MaSoGTVT]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("01001", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("99", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Hà Nội", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_options_sql_empty_search_remains_ma_dv_ascending()
    {
        var shape = new MotoSyncDonViGTVTQueryShape(
            new MotoSyncDonViGTVTDisplayColumns("TenDV", null));

        var sql = MotoSyncDonViGTVTOptionPlanner.BuildOptionsSql(shape, hasSearch: false);

        Assert.Contains("ORDER BY [MaDV];", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@SearchExact", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@SearchPrefix", sql, StringComparison.Ordinal);
    }
}
