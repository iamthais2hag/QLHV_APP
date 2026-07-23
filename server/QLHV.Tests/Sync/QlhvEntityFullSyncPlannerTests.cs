using QLHV.Application.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvEntityFullSyncPlannerTests
{
    [Fact]
    public void Plan_classifies_all_actions_and_is_idempotent_after_sync()
    {
        var source = new[]
        {
            new Entity("A", "hash-a"),
            new Entity("B", "hash-b-new"),
            new Entity("C", "hash-c"),
            new Entity("D", "hash-d"),
        };
        var target = new[]
        {
            new QlhvEntityFullSyncTargetRow("B", "hash-b-old", false),
            new QlhvEntityFullSyncTargetRow("C", "hash-c", true),
            new QlhvEntityFullSyncTargetRow("D", "hash-d", false),
            new QlhvEntityFullSyncTargetRow("E", "hash-e", false),
        };

        var plan = Build(source, target);

        Assert.Equal(1, plan.Insert);
        Assert.Equal(1, plan.Update);
        Assert.Equal(1, plan.Reactivate);
        Assert.Equal(1, plan.SoftDelete);
        Assert.Equal(1, plan.Skip);

        var synchronized = source
            .Select(row => new QlhvEntityFullSyncTargetRow(row.Key, row.Hash, false))
            .ToArray();
        var rerun = Build(source, synchronized);
        Assert.Equal(source.Length, rerun.Skip);
        Assert.Equal(0, rerun.Upsert);
        Assert.Equal(0, rerun.SoftDelete);
    }

    [Fact]
    public void Empty_source_and_empty_or_deleted_only_target_is_a_safe_no_op()
    {
        var empty = Build(Array.Empty<Entity>(), Array.Empty<QlhvEntityFullSyncTargetRow>());
        var deletedOnly = Build(
            Array.Empty<Entity>(),
            new[] { new QlhvEntityFullSyncTargetRow("A", "hash", true) });

        Assert.Equal(QlhvEntityFullSyncPlan.Empty, empty);
        Assert.Equal(QlhvEntityFullSyncPlan.Empty, deletedOnly);
    }

    [Fact]
    public void Empty_source_with_active_partition_is_blocked_instead_of_mass_soft_delete()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(
            Array.Empty<Entity>(),
            new[] { new QlhvEntityFullSyncTargetRow("A", "hash", false) }));

        Assert.Contains("0 dong", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target partition", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_source_or_target_identity_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Build(
            new[] { new Entity("A", "one"), new Entity(" a ", "two") },
            Array.Empty<QlhvEntityFullSyncTargetRow>()));
        Assert.Throws<InvalidOperationException>(() => Build(
            new[] { new Entity("A", "one") },
            new[]
            {
                new QlhvEntityFullSyncTargetRow("A", "one", false),
                new QlhvEntityFullSyncTargetRow(" a ", "two", true),
            }));
    }

    private static QlhvEntityFullSyncPlan Build(
        IReadOnlyList<Entity> source,
        IReadOnlyList<QlhvEntityFullSyncTargetRow> target)
        => QlhvEntityFullSyncPlanner.BuildPlan(
            source,
            target,
            row => row.Key,
            row => row.Hash,
            "KhoaHoc");

    private sealed record Entity(string Key, string Hash);
}
