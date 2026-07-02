using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncKhoaHocOptionPlannerTests
{
    [Fact]
    public void Normalize_take_uses_default_and_max_limit()
    {
        Assert.Equal(50, MotoSyncKhoaHocOptionPlanner.NormalizeTake(0));
        Assert.Equal(50, MotoSyncKhoaHocOptionPlanner.NormalizeTake(-1));
        Assert.Equal(75, MotoSyncKhoaHocOptionPlanner.NormalizeTake(75));
        Assert.Equal(200, MotoSyncKhoaHocOptionPlanner.NormalizeTake(500));
    }

    [Fact]
    public void Detect_columns_tolerates_missing_optional_display_columns()
    {
        var khoaHocKey = MotoSyncKhoaHocOptionPlanner.DetectKhoaHocCourseKeyColumn(["MaKH"]);
        var hoSoKey = MotoSyncKhoaHocOptionPlanner.DetectHoSoCourseKeyColumn(["MaDK", "MaKhoaHoc"]);
        var display = MotoSyncKhoaHocOptionPlanner.DetectDisplayColumns(["MaKH"]);

        Assert.Equal("MaKH", khoaHocKey);
        Assert.Equal("MaKhoaHoc", hoSoKey);
        Assert.Null(display.TenKhoaHocColumn);
        Assert.Null(display.HangColumn);
        Assert.Null(display.NgayKhaiGiangColumn);
    }

    [Fact]
    public void Build_options_sql_uses_search_parameter_not_literal()
    {
        var shape = new MotoSyncKhoaHocQueryShape(
            "MaKH",
            new MotoSyncKhoaHocDisplayColumns("TenKH", "HangDaoTao", "NgayKhaiGiang"));

        var sql = MotoSyncKhoaHocOptionPlanner.BuildOptionsSql(shape, hasSearch: true);

        Assert.Contains("@SearchLike", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("66016K26A", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[MaKH] LIKE @SearchLike", sql, StringComparison.Ordinal);
        Assert.Contains("[TenKH] LIKE @SearchLike", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [NgayKhaiGiang] DESC, [MaKH] DESC", sql, StringComparison.Ordinal);
    }
}
