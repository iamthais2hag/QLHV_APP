using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncInsertColumnPlannerTests
{
    [Fact]
    public void SelectCommonSafeInsertColumns_ignores_source_only_HTDaoTao_for_V2_to_V1()
    {
        var source = new[]
        {
            Column("MaKH"),
            Column("TenKH"),
            Column("HTDaoTao"),
        };
        var target = new[]
        {
            Column("MaKH"),
            Column("TenKH"),
        };

        var columns = MotoSyncInsertColumnPlanner.SelectCommonSafeInsertColumns(source, target);

        Assert.Equal(new[] { "MaKH", "TenKH" }, columns);
        Assert.DoesNotContain("HTDaoTao", columns);
    }

    [Fact]
    public void SelectCommonSafeInsertColumns_excludes_identity_computed_rowversion_and_binary_columns()
    {
        var source = new[]
        {
            Column("MaKH"),
            Column("IdentityColumn", isIdentity: true),
            Column("ComputedColumn", isComputed: true),
            Column("RowVersionColumn", dataType: "rowversion", isRowVersion: true),
            Column("ImageColumn", dataType: "image"),
        };
        var target = source;

        var columns = MotoSyncInsertColumnPlanner.SelectCommonSafeInsertColumns(source, target);

        Assert.Equal(new[] { "MaKH" }, columns);
    }

    [Fact]
    public void FindMissingRequiredTargetColumns_blocks_target_only_not_null_without_default()
    {
        var source = new[]
        {
            Column("MaKH"),
        };
        var target = new[]
        {
            Column("MaKH"),
            Column("RequiredTargetOnly", isNullable: false, hasDefault: false),
        };

        var missing = MotoSyncInsertColumnPlanner.FindMissingRequiredTargetColumns(source, target);

        Assert.Equal(new[] { "RequiredTargetOnly" }, missing);
    }

    [Fact]
    public void FindMissingRequiredTargetColumns_allows_target_only_nullable_or_default_columns()
    {
        var source = new[]
        {
            Column("MaKH"),
        };
        var target = new[]
        {
            Column("MaKH"),
            Column("NullableTargetOnly", isNullable: true),
            Column("DefaultTargetOnly", isNullable: false, hasDefault: true),
        };

        var missing = MotoSyncInsertColumnPlanner.FindMissingRequiredTargetColumns(source, target);

        Assert.Empty(missing);
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
}
