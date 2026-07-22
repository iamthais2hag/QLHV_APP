using QLHV.Application.Sync;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Tests.Sync;

public sealed class QlhvFullSyncPlannerTests
{
    [Fact]
    public void Plan_classifies_insert_update_reactivate_soft_delete_and_skip_exclusively()
    {
        var source = new[]
        {
            Source("A", "hash-a"),
            Source("B", "hash-b-new"),
            Source("C", "hash-c"),
            Source("D", "hash-d"),
        };
        var target = new[]
        {
            Target("B", "hash-b-old"),
            Target("C", "hash-c", isDeleted: true),
            Target("D", "hash-d"),
            Target("E", "hash-e"),
            Target("F", "hash-f", isDeleted: true),
        };

        var plan = QlhvFullSyncPlanner.BuildPlan(source, target);

        Assert.Equal("CSDT_OTO", plan.SourceProfileCode);
        Assert.Equal(1, plan.PlannedInsertHocVienRows);
        Assert.Equal(1, plan.PlannedUpdateHocVienRows);
        Assert.Equal(1, plan.PlannedReactivateHocVienRows);
        Assert.Equal(1, plan.PlannedSoftDeleteHocVienRows);
        Assert.Equal(1, plan.PlannedSkipHocVienRows);
        Assert.Equal(3, plan.PlannedUpsertHocVienRows);
        Assert.Equal(
            source.Length,
            plan.PlannedInsertHocVienRows +
            plan.PlannedUpdateHocVienRows +
            plan.PlannedReactivateHocVienRows +
            plan.PlannedSkipHocVienRows);
    }

    [Fact]
    public void Reappearing_soft_deleted_identity_is_reactivated_even_when_hash_is_unchanged()
    {
        var plan = QlhvFullSyncPlanner.BuildPlan(
            new[] { Source("A", "same-hash") },
            new[] { Target("A", "same-hash", isDeleted: true) });

        Assert.Equal(1, plan.PlannedReactivateHocVienRows);
        Assert.Equal(0, plan.PlannedUpdateHocVienRows);
        Assert.Equal(0, plan.PlannedSkipHocVienRows);
    }

    [Fact]
    public void Same_snapshot_after_first_sync_is_idempotent_and_only_skips()
    {
        var source = new[]
        {
            Source("A", "hash-a"),
            Source("B", "hash-b"),
        };
        var synchronizedTarget = new[]
        {
            Target("A", "hash-a"),
            Target("B", "hash-b"),
        };

        var plan = QlhvFullSyncPlanner.BuildPlan(source, synchronizedTarget);

        Assert.Equal(0, plan.PlannedInsertHocVienRows);
        Assert.Equal(0, plan.PlannedUpdateHocVienRows);
        Assert.Equal(0, plan.PlannedReactivateHocVienRows);
        Assert.Equal(0, plan.PlannedSoftDeleteHocVienRows);
        Assert.Equal(2, plan.PlannedSkipHocVienRows);
    }

    [Fact]
    public void Only_missing_active_target_rows_are_planned_for_soft_delete()
    {
        var plan = QlhvFullSyncPlanner.BuildPlan(
            new[] { Source("A", "hash-a") },
            new[]
            {
                Target("A", "hash-a"),
                Target("B", "hash-b"),
                Target("C", "hash-c", isDeleted: true),
            });

        Assert.Equal(1, plan.PlannedSoftDeleteHocVienRows);
        Assert.Equal(1, plan.PlannedSkipHocVienRows);
    }

    [Fact]
    public void Mixed_source_profiles_are_rejected_to_preserve_partition_isolation()
    {
        var source = new[]
        {
            Source("A", "hash-a", "CSDT_OTO"),
            Source("B", "hash-b", "CSDT_MOTO"),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => QlhvFullSyncPlanner.BuildPlan(source, Array.Empty<QlhvFullSyncTargetRow>()));

        Assert.Contains("mot SourceProfileCode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_source_is_rejected_before_any_partition_wide_soft_delete_can_be_planned()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => QlhvFullSyncPlanner.BuildPlan(
                Array.Empty<QlhvImportHocVienWriteModel>(),
                new[] { Target("A", "hash-a") }));

        Assert.Contains("Nguon hoc vien rong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_source_identity_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => QlhvFullSyncPlanner.BuildPlan(
                new[] { Source("A", "one"), Source(" a ", "two") },
                Array.Empty<QlhvFullSyncTargetRow>()));

        Assert.Contains("Nguon co SourceMaDK trung", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_target_identity_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => QlhvFullSyncPlanner.BuildPlan(
                new[] { Source("A", "hash-a") },
                new[] { Target("A", "one"), Target(" a ", "two", isDeleted: true) }));

        Assert.Contains("Target co SourceMaDK trung", exception.Message, StringComparison.Ordinal);
    }

    private static QlhvImportHocVienWriteModel Source(
        string sourceMaDk,
        string hash,
        string profile = "CSDT_OTO")
        => new()
        {
            SourceProfileCode = profile,
            SourceMaDK = sourceMaDk,
            SourceSystem = "V2",
            MaDK = sourceMaDk.Trim(),
            SourceOfTruth = "V2",
            V2RowHash = hash,
        };

    private static QlhvFullSyncTargetRow Target(
        string sourceMaDk,
        string? hash,
        bool isDeleted = false)
        => new(sourceMaDk, hash, isDeleted);
}
