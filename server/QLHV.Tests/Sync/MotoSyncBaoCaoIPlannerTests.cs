using System.Data;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncBaoCaoIPlannerTests
{
    [Fact]
    public void DetectCourseScopeColumn_prefers_MaKhoaHoc_when_available()
    {
        var result = MotoSyncBaoCaoIPlanner.DetectCourseScopeColumn(
            new[] { "MaKhoaHoc", "MaKH" },
            new[] { "MaKhoaHoc", "MaKH" });

        Assert.Equal("MaKhoaHoc", result);
    }

    [Fact]
    public void DetectCourseScopeColumn_falls_back_to_MaKH()
    {
        var result = MotoSyncBaoCaoIPlanner.DetectCourseScopeColumn(
            new[] { "MaBCI", "MaKH" },
            new[] { "MaBCI", "MaKH" });

        Assert.Equal("MaKH", result);
    }

    [Fact]
    public void DetectCourseScopeColumn_returns_null_when_no_supported_common_column_exists()
    {
        var result = MotoSyncBaoCaoIPlanner.DetectCourseScopeColumn(
            new[] { "MaBCI", "CourseCode" },
            new[] { "MaBCI", "CourseCode" });

        Assert.Null(result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_uses_safe_common_primary_key_metadata()
    {
        var source = new[] { Column("MaBCI"), Column("MaKH") };
        var target = new[] { Column("MaBCI"), Column("MaKH") };

        var result = MotoSyncBaoCaoIPlanner.ResolveRowIdentityColumns(
            source,
            target,
            new[] { "MaBCI" },
            new[] { "MaBCI" });

        Assert.Equal(new[] { "MaBCI" }, result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_falls_back_to_MaBCI_when_primary_key_is_absent()
    {
        var source = new[] { Column("MaBCI"), Column("MaKH") };
        var target = new[] { Column("MaBCI"), Column("MaKH") };

        var result = MotoSyncBaoCaoIPlanner.ResolveRowIdentityColumns(
            source,
            target,
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Equal(new[] { "MaBCI" }, result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_blocks_when_primary_key_metadata_is_not_safe_common()
    {
        var source = new[] { Column("MaBCI"), Column("MaKH") };
        var target = new[] { Column("MaBCI", isIdentity: true), Column("MaKH") };

        var result = MotoSyncBaoCaoIPlanner.ResolveRowIdentityColumns(
            source,
            target,
            new[] { "MaBCI" },
            new[] { "MaBCI" });

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveRowIdentityColumns_blocks_when_no_primary_key_or_allowlisted_key_exists()
    {
        var source = new[] { Column("SoBaoCao"), Column("MaKH") };
        var target = new[] { Column("SoBaoCao"), Column("MaKH") };

        var result = MotoSyncBaoCaoIPlanner.ResolveRowIdentityColumns(
            source,
            target,
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void FilterInsertRowsForSelectedCourse_compares_against_selected_course_target_keys_only()
    {
        var sourceRows = BaoCaoIRows(("BCI001", "K001"));
        var selectedCourseTargetRows = BaoCaoIRows();

        var result = MotoSyncBaoCaoIPlanner.FilterInsertRowsForSelectedCourse(
            sourceRows,
            selectedCourseTargetRows,
            new[] { "MaBCI" },
            "MaKH",
            "K001");

        Assert.Equal(1, result.Rows.Count);
        Assert.Equal("BCI001", result.Rows[0]["MaBCI"]);
    }

    [Fact]
    public void FilterInsertRowsForSelectedCourse_excludes_rows_already_present_in_selected_course_target()
    {
        var sourceRows = BaoCaoIRows(("BCI001", "K001"));
        var selectedCourseTargetRows = BaoCaoIRows(("BCI001", "K001"));

        var result = MotoSyncBaoCaoIPlanner.FilterInsertRowsForSelectedCourse(
            sourceRows,
            selectedCourseTargetRows,
            new[] { "MaBCI" },
            "MaKH",
            "K001");

        Assert.Empty(result.Rows);
    }

    [Fact]
    public void FilterInsertRowsForSelectedCourse_never_inserts_source_rows_from_other_courses()
    {
        var sourceRows = BaoCaoIRows(("BCI001", "K001"), ("BCI002", "K002"));
        var selectedCourseTargetRows = BaoCaoIRows();

        var result = MotoSyncBaoCaoIPlanner.FilterInsertRowsForSelectedCourse(
            sourceRows,
            selectedCourseTargetRows,
            new[] { "MaBCI" },
            "MaKH",
            "K001");

        Assert.Equal(1, result.Rows.Count);
        Assert.Equal("K001", result.Rows[0]["MaKH"]);
    }

    [Fact]
    public void Cross_course_same_key_does_not_block_when_identity_includes_course_scope()
    {
        var plannedRows = BaoCaoIRows(("BCI001", "K001"));
        var targetOutsideRows = BaoCaoIRows(("BCI001", "K002"));

        var result = MotoSyncBaoCaoIPlanner.HasCrossCourseKeyConflict(
            plannedRows,
            targetOutsideRows,
            new[] { "MaKH", "MaBCI" },
            "MaKH");

        Assert.False(result);
    }

    [Fact]
    public void Cross_course_same_key_blocks_when_identity_does_not_include_course_scope()
    {
        var plannedRows = BaoCaoIRows(("BCI001", "K001"));
        var targetOutsideRows = BaoCaoIRows(("BCI001", "K002"));

        var result = MotoSyncBaoCaoIPlanner.HasCrossCourseKeyConflict(
            plannedRows,
            targetOutsideRows,
            new[] { "MaBCI" },
            "MaKH");

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

    private static DataTable BaoCaoIRows(params (string MaBCI, string MaKH)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("MaBCI", typeof(string));
        table.Columns.Add("MaKH", typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row.MaBCI, row.MaKH);
        }

        return table;
    }
}
