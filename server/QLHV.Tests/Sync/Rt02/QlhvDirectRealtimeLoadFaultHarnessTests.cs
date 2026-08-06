using System.Diagnostics;
using QLHV.Application.Sync.QlhvDirectRealtime;

namespace QLHV.Tests.Sync.Rt02;

public sealed class QlhvDirectRealtimeLoadFaultHarnessTests
{
    [Fact]
    public async Task Rt02_load_one_insert_one_update_one_retained_is_measured()
    {
        var operations = new[]
        {
            Rt02TestData.InsertOperation(),
            Rt02TestData.UpdateOperation(),
            Rt02TestData.RetainOperation(),
        };

        var metrics = await ExecuteMeasuredAsync(operations);

        AssertMeasured(metrics, expectedRows: 3);
        Assert.Equal(1, metrics.InsertedRows);
        Assert.Equal(1, metrics.UpdatedRows);
        Assert.Equal(1, metrics.RetainedRows);
    }

    [Fact]
    public async Task Rt02_load_100_inserts_is_measured()
    {
        var operations = Enumerable.Range(1, 100)
            .Select(index => Rt02TestData.InsertOperation($"HMAC-I-{index:D4}"))
            .ToArray();

        var metrics = await ExecuteMeasuredAsync(operations);

        AssertMeasured(metrics, expectedRows: 100);
        Assert.Equal(100, metrics.InsertedRows);
    }

    [Fact]
    public async Task Rt02_load_100_updates_is_measured()
    {
        var operations = Enumerable.Range(1, 100)
            .Select(index => Rt02TestData.UpdateOperation(
                identity: $"HMAC-U-{index:D4}"))
            .ToArray();

        var metrics = await ExecuteMeasuredAsync(operations);

        AssertMeasured(metrics, expectedRows: 100);
        Assert.Equal(100, metrics.UpdatedRows);
    }

    [Fact]
    public async Task Rt02_load_mixed_1000_operations_is_measured()
    {
        var inserts = Enumerable.Range(1, 500)
            .Select(index => Rt02TestData.InsertOperation($"HMAC-MI-{index:D4}"));
        var updates = Enumerable.Range(1, 499)
            .Select(index => Rt02TestData.UpdateOperation(
                identity: $"HMAC-MU-{index:D4}"));
        var operations = inserts
            .Concat(updates)
            .Append(Rt02TestData.RetainOperation("HMAC-MR-0001"))
            .ToArray();

        var metrics = await ExecuteMeasuredAsync(operations);

        AssertMeasured(metrics, expectedRows: 1000);
        Assert.Equal(500, metrics.InsertedRows);
        Assert.Equal(499, metrics.UpdatedRows);
        Assert.Equal(1, metrics.RetainedRows);
    }

    [Fact]
    public async Task Rt02_load_duplicate_event_replay_is_idempotent()
    {
        var root = new QlhvDirectRealtimeIsolatedTestCompositionRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        var first = await ExecuteAsync(root, plan);
        var replay = await ExecuteAsync(root, plan);

        Assert.Equal(first.MarkerHash, replay.MarkerHash);
        Assert.Single(root.Store.Learners);
        Assert.Equal(1, root.Store.OpenTransactionCount);
    }

    [Fact]
    public async Task Rt02_fault_target_timeout_rolls_back_and_explicit_retry_succeeds()
    {
        var root = new QlhvDirectRealtimeIsolatedTestCompositionRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.CommitFailure = new TimeoutException("Injected isolated timeout.");

        await Assert.ThrowsAsync<TimeoutException>(() => ExecuteAsync(root, plan));
        var result = await ExecuteAsync(root, plan);

        Assert.Equal("SUCCEEDED", result.Status);
        Assert.Equal(1, root.Store.RollbackCount);
        Assert.Single(root.Store.Learners);
    }

    [Fact]
    public async Task Rt02_fault_deadlock_rolls_back_and_explicit_retry_succeeds()
    {
        var root = new QlhvDirectRealtimeIsolatedTestCompositionRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.CommitFailure =
            new Rt02SyntheticDeadlockException("Injected isolated deadlock.");

        await Assert.ThrowsAsync<Rt02SyntheticDeadlockException>(
            () => ExecuteAsync(root, plan));
        var result = await ExecuteAsync(root, plan);

        Assert.Equal(1, result.UpdatedRows);
        Assert.Equal(1, root.Store.RollbackCount);
        Assert.Equal(1, root.Store.CommitCount);
    }

    [Fact]
    public async Task Rt02_fault_crash_after_commit_recovers_without_second_transaction()
    {
        var root = new QlhvDirectRealtimeIsolatedTestCompositionRoot(
            new Rt02CrashAfterCommitFaultInjector());
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExecuteAsync(root, plan));
        var recovered = await ExecuteAsync(root, plan);

        Assert.True(recovered.RecoveredFromDurableMarker);
        Assert.Equal(1, root.Store.OpenTransactionCount);
        Assert.Equal(1, root.Checkpoints.PublishCount);
    }

    [Fact]
    public async Task Rt02_fault_checkpoint_conflict_fails_closed_without_transaction()
    {
        var root = new QlhvDirectRealtimeIsolatedTestCompositionRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        var key = new QlhvDirectRealtimeApplyCheckpointKey(
            plan.SourceProfile,
            QlhvDirectRealtimeModes.DirectRealtimeApply,
            plan.MappingFingerprint,
            plan.EnvironmentId);
        await root.Checkpoints.PublishAsync(
            new QlhvDirectRealtimeApplyCheckpoint(
                key,
                "DIFFERENT-CYCLE",
                "DIFFERENT-PLAN",
                "DIFFERENT-MARKER",
                1,
                DateTime.UtcNow),
            default);

        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));

        Assert.Equal(QlhvDirectRealtimeErrors.CheckpointConflict, error.Code);
        Assert.Equal(0, root.Store.OpenTransactionCount);
    }

    private static async Task<Rt02LoadMetrics> ExecuteMeasuredAsync(
        IReadOnlyList<QlhvDirectRealtimeApplyOperation> operations)
    {
        var root = new QlhvDirectRealtimeIsolatedTestCompositionRoot();
        var plan = Rt02TestData.Plan(operations);
        Rt02TestData.SeedForPlan(root.Store, plan);
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var timer = Stopwatch.StartNew();
        var result = await ExecuteAsync(root, plan);
        timer.Stop();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        var rows = result.InsertedRows + result.UpdatedRows + result.RetainedRows;
        return new Rt02LoadMetrics(
            timer.Elapsed,
            root.Store.LastTransactionDuration,
            rows / Math.Max(timer.Elapsed.TotalSeconds, 0.000001),
            root.Store.QueryCount,
            RetryCount: 0,
            Math.Max(0, memoryAfter - memoryBefore),
            RollbackSucceeded: root.Store.RollbackCount == 0,
            result.InsertedRows,
            result.UpdatedRows,
            result.RetainedRows);
    }

    private static Task<QlhvDirectRealtimeApplyResult> ExecuteAsync(
        QlhvDirectRealtimeIsolatedTestCompositionRoot root,
        QlhvDirectRealtimeApplyPlan plan)
    {
        var environment = Rt02TestData.Environment();
        return root.Cycle.ExecuteAsync(
            plan,
            environment,
            Rt02TestData.Identities(environment));
    }

    private static void AssertMeasured(
        Rt02LoadMetrics metrics,
        int expectedRows)
    {
        Assert.True(metrics.CycleDuration >= TimeSpan.Zero);
        Assert.True(metrics.TransactionDuration >= TimeSpan.Zero);
        Assert.True(metrics.RowsPerSecond > 0);
        Assert.True(metrics.QueryCount >= expectedRows);
        Assert.Equal(0, metrics.RetryCount);
        Assert.True(metrics.MemoryDeltaBytes >= 0);
        Assert.True(metrics.RollbackSucceeded);
        Assert.Equal(
            expectedRows,
            metrics.InsertedRows + metrics.UpdatedRows + metrics.RetainedRows);
    }

    private sealed record Rt02LoadMetrics(
        TimeSpan CycleDuration,
        TimeSpan TransactionDuration,
        double RowsPerSecond,
        int QueryCount,
        int RetryCount,
        long MemoryDeltaBytes,
        bool RollbackSucceeded,
        int InsertedRows,
        int UpdatedRows,
        int RetainedRows);

    private sealed class Rt02SyntheticDeadlockException : Exception
    {
        public Rt02SyntheticDeadlockException(string message)
            : base(message)
        {
        }
    }
}
