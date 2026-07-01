using System.Data;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncNguoiLXGplxPlannerTests
{
    [Fact]
    public void DetectScope_prefers_MaKhoaHoc_when_available()
    {
        var result = MotoSyncNguoiLXGplxPlanner.DetectScope(
            new[] { "MaKhoaHoc", "MaDK" },
            new[] { "MaKhoaHoc", "MaDK" });

        Assert.NotNull(result);
        Assert.Equal("MaKhoaHoc", result!.CourseColumn);
        Assert.False(result.UsesMaDkScope);
    }

    [Fact]
    public void DetectScope_falls_back_to_MaKH()
    {
        var result = MotoSyncNguoiLXGplxPlanner.DetectScope(
            new[] { "MaKH", "MaDK" },
            new[] { "MaKH", "MaDK" });

        Assert.NotNull(result);
        Assert.Equal("MaKH", result!.CourseColumn);
        Assert.False(result.UsesMaDkScope);
    }

    [Fact]
    public void DetectScope_uses_MaDK_scope_when_no_course_column_exists()
    {
        var result = MotoSyncNguoiLXGplxPlanner.DetectScope(
            new[] { "MaDK", "SoGPLX" },
            new[] { "MaDK", "SoGPLX" });

        Assert.NotNull(result);
        Assert.Null(result!.CourseColumn);
        Assert.True(result.UsesMaDkScope);
    }

    [Fact]
    public void DetectScope_returns_null_when_no_safe_scope_exists()
    {
        var result = MotoSyncNguoiLXGplxPlanner.DetectScope(
            new[] { "SoGPLX" },
            new[] { "SoGPLX" });

        Assert.Null(result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_uses_safe_common_primary_key_metadata()
    {
        var source = new[] { Column("MaDK"), Column("SoGPLX") };
        var target = new[] { Column("MaDK"), Column("SoGPLX") };

        var result = MotoSyncNguoiLXGplxPlanner.ResolveRowIdentityColumns(
            source,
            target,
            new[] { "MaDK" },
            new[] { "MaDK" });

        Assert.Equal(new[] { "MaDK" }, result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_falls_back_to_first_supported_key_when_primary_key_absent()
    {
        var source = new[] { Column("MaDK"), Column("HangGPLX"), Column("SoGPLX") };
        var target = new[] { Column("MaDK"), Column("HangGPLX"), Column("SoGPLX") };

        var result = MotoSyncNguoiLXGplxPlanner.ResolveRowIdentityColumns(
            source,
            target,
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Equal(new[] { "MaDK", "HangGPLX" }, result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_falls_back_to_MaGPLX_when_pair_keys_are_absent()
    {
        var source = new[] { Column("MaGPLX"), Column("SoGPLX") };
        var target = new[] { Column("MaGPLX"), Column("SoGPLX") };

        var result = MotoSyncNguoiLXGplxPlanner.ResolveRowIdentityColumns(
            source,
            target,
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Equal(new[] { "MaGPLX" }, result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_blocks_when_primary_key_metadata_is_not_safe_common()
    {
        var source = new[] { Column("MaDK"), Column("SoGPLX") };
        var target = new[] { Column("MaDK", isIdentity: true), Column("SoGPLX") };

        var result = MotoSyncNguoiLXGplxPlanner.ResolveRowIdentityColumns(
            source,
            target,
            new[] { "MaDK" },
            new[] { "MaDK" });

        Assert.Empty(result);
    }

    [Fact]
    public void FilterInsertRowsForSelectedScope_never_inserts_source_rows_from_other_courses()
    {
        var sourceRows = GplxRows(
            ("DK001", "K001", "A1", "GPLX001"),
            ("DK002", "K002", "A1", "GPLX002"));
        var targetRows = GplxRows();
        var scope = new MotoSyncNguoiLXGplxScope("MaKhoaHoc", UsesMaDkScope: false);

        var result = MotoSyncNguoiLXGplxPlanner.FilterInsertRowsForSelectedScope(
            sourceRows,
            targetRows,
            new[] { "MaDK" },
            scope,
            "K001",
            new[] { "DK001" });

        Assert.Single(result.Rows.Cast<DataRow>());
        Assert.Equal("DK001", result.Rows[0]["MaDK"]);
    }

    [Fact]
    public void FilterInsertRowsForSelectedScope_uses_selected_MaDKs_when_table_has_no_course_column()
    {
        var sourceRows = GplxRows(
            ("DK001", "K001", "A1", "GPLX001"),
            ("DK002", "K002", "A1", "GPLX002"));
        var targetRows = GplxRows();
        var scope = new MotoSyncNguoiLXGplxScope(CourseColumn: null, UsesMaDkScope: true);

        var result = MotoSyncNguoiLXGplxPlanner.FilterInsertRowsForSelectedScope(
            sourceRows,
            targetRows,
            new[] { "MaDK" },
            scope,
            "K001",
            new[] { "DK001" });

        Assert.Single(result.Rows.Cast<DataRow>());
        Assert.Equal("DK001", result.Rows[0]["MaDK"]);
    }

    [Fact]
    public void FilterInsertRowsForSelectedScope_excludes_rows_already_present_in_target_scope()
    {
        var sourceRows = GplxRows(("DK001", "K001", "A1", "GPLX001"));
        var targetRows = GplxRows(("DK001", "K001", "A1", "GPLX001"));
        var scope = new MotoSyncNguoiLXGplxScope(CourseColumn: null, UsesMaDkScope: true);

        var result = MotoSyncNguoiLXGplxPlanner.FilterInsertRowsForSelectedScope(
            sourceRows,
            targetRows,
            new[] { "MaDK" },
            scope,
            "K001",
            new[] { "DK001" });

        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Cross_scope_same_key_does_not_block_when_identity_includes_MaDK_scope()
    {
        var plannedRows = GplxRows(("DK001", "K001", "A1", "GPLX001"));
        var targetOutsideRows = GplxRows(("DK001", "K002", "A1", "GPLX001"));
        var scope = new MotoSyncNguoiLXGplxScope(CourseColumn: null, UsesMaDkScope: true);

        var result = MotoSyncNguoiLXGplxPlanner.HasCrossScopeKeyConflict(
            plannedRows,
            targetOutsideRows,
            new[] { "MaDK" },
            scope);

        Assert.False(result);
    }

    [Fact]
    public void Cross_scope_same_key_blocks_when_identity_does_not_include_scope()
    {
        var plannedRows = GplxRows(("DK001", "K001", "A1", "GPLX001"));
        var targetOutsideRows = GplxRows(("DK002", "K002", "A2", "GPLX001"));
        var scope = new MotoSyncNguoiLXGplxScope(CourseColumn: null, UsesMaDkScope: true);

        var result = MotoSyncNguoiLXGplxPlanner.HasCrossScopeKeyConflict(
            plannedRows,
            targetOutsideRows,
            new[] { "MaGPLX" },
            scope);

        Assert.True(result);
    }

    private static MotoSyncInsertColumnInfo Column(
        string name,
        string dataType = "nvarchar",
        bool isNullable = true,
        bool isIdentity = false,
        bool isComputed = false,
        bool isRowVersion = false,
        bool hasDefault = false)
        => new(name, dataType, isNullable, isIdentity, isComputed, isRowVersion, hasDefault);

    private static DataTable GplxRows(params (string MaDK, string MaKhoaHoc, string HangGPLX, string MaGPLX)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("MaDK", typeof(string));
        table.Columns.Add("MaKhoaHoc", typeof(string));
        table.Columns.Add("HangGPLX", typeof(string));
        table.Columns.Add("SoGPLX", typeof(string));
        table.Columns.Add("MaGPLX", typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row.MaDK, row.MaKhoaHoc, row.HangGPLX, row.MaGPLX, row.MaGPLX);
        }

        return table;
    }
}
