using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncUpdateSqlBuilderTests
{
    [Fact]
    public void NguoiLX_update_where_uses_MaDK_only()
    {
        var sql = MotoSyncUpdateSqlBuilder.BuildUpdateSql(
            MotoSyncUpdateSqlBuilder.NguoiLxTable,
            new[] { "HoVaTen" });

        Assert.Equal("UPDATE dbo.[NguoiLX] SET [HoVaTen] = @v0 WHERE MaDK = @MaDK;", sql);
        Assert.Contains("WHERE MaDK = @MaDK", sql);
        Assert.DoesNotContain("@MaKhoaHoc", sql);
    }

    [Fact]
    public void NguoiLX_HoSo_update_where_uses_MaDK_and_MaKhoaHoc()
    {
        var sql = MotoSyncUpdateSqlBuilder.BuildUpdateSql(
            MotoSyncUpdateSqlBuilder.NguoiLxHoSoTable,
            new[] { "TT_XuLy" });

        Assert.Equal("UPDATE dbo.[NguoiLX_HoSo] SET [TT_XuLy] = @v0 WHERE MaDK = @MaDK AND MaKhoaHoc = @MaKhoaHoc;", sql);
        Assert.Contains("WHERE MaDK = @MaDK AND MaKhoaHoc = @MaKhoaHoc", sql);
    }

    [Fact]
    public void MaKhoaHoc_is_treated_as_scope_column_not_update_column()
    {
        Assert.True(MotoSyncUpdateSqlBuilder.IsKeyOrScopeColumn("MaKhoaHoc"));
        Assert.True(MotoSyncUpdateSqlBuilder.IsKeyOrScopeColumn("MaDK"));
    }

    [Fact]
    public void HoSo_update_cannot_affect_same_MaDK_in_another_MaKhoaHoc()
    {
        var sql = MotoSyncUpdateSqlBuilder.BuildUpdateSql(
            MotoSyncUpdateSqlBuilder.NguoiLxHoSoTable,
            new[] { "TT_XuLy" });

        Assert.Contains("MaDK = @MaDK", sql);
        Assert.Contains("MaKhoaHoc = @MaKhoaHoc", sql);
    }
}
