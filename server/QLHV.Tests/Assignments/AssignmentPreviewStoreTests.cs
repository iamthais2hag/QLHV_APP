using QLHV.Application.Assignments;

namespace QLHV.Tests.Assignments;

public sealed class AssignmentPreviewStoreTests
{
    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Preview_is_bound_to_exact_kind_and_actor()
    {
        var time = new ManualTimeProvider();
        var store = new AssignmentPreviewStore(time);
        var payload = new Payload("sealed");
        var (token, expiresAtUtc) = store.Put("ASSIGNMENT", "operator-a", payload);

        Assert.Equal(time.GetUtcNow().UtcDateTime.Add(AssignmentRules.PreviewTtl), expiresAtUtc);
        Assert.Same(payload, store.Get<Payload>(token, "ASSIGNMENT", "OPERATOR-A"));

        var actorError = Assert.Throws<AssignmentDomainException>(() =>
            store.Get<Payload>(token, "ASSIGNMENT", "operator-b"));
        Assert.Equal(409, actorError.StatusCode);
        Assert.Equal("CONFLICT", actorError.Code);

        var kindError = Assert.Throws<AssignmentDomainException>(() =>
            store.Get<Payload>(token, "IMPORT", "operator-a"));
        Assert.Equal(409, kindError.StatusCode);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Expired_preview_is_rejected_and_removed()
    {
        var time = new ManualTimeProvider();
        var store = new AssignmentPreviewStore(time);
        var payload = new Payload("expired");
        var (token, _) = store.Put("IMPORT", "operator-a", payload);
        time.AdvanceMonotonic(AssignmentRules.PreviewTtl);

        var first = Assert.Throws<AssignmentDomainException>(() =>
            store.Get<Payload>(token, "IMPORT", "operator-a"));
        var second = Assert.Throws<AssignmentDomainException>(() =>
            store.Get<Payload>(token, "IMPORT", "operator-a"));

        Assert.Equal("CONFLICT", first.Code);
        Assert.Equal("CONFLICT", second.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [Trait("Category", "AssignmentFocused")]
    public void Client_or_server_wall_clock_jump_does_not_change_in_process_preview_ttl(
        int days)
    {
        var time = new ManualTimeProvider();
        var store = new AssignmentPreviewStore(time);
        var payload = new Payload("monotonic");
        var (token, _) = store.Put("IMPORT", "operator-a", payload);

        time.AdvanceWall(TimeSpan.FromDays(days));

        Assert.Same(payload, store.Get<Payload>(token, "IMPORT", "operator-a"));

        time.AdvanceMonotonic(AssignmentRules.PreviewTtl);
        Assert.Throws<AssignmentDomainException>(() =>
            store.Get<Payload>(token, "IMPORT", "operator-a"));
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Completion_is_one_time_and_idempotency_is_actor_scoped()
    {
        var store = new AssignmentPreviewStore();
        var (token, _) = store.Put("ASSIGNMENT", " Operator-A ", new Payload("sealed"));
        var firstResult = new Result("operation-1");
        store.Complete(token, "operator-a", " retry-001 ", firstResult);

        Assert.True(store.TryGetCompleted<Result>("OPERATOR-A", "retry-001", out var replay));
        Assert.Same(firstResult, replay);
        Assert.False(store.TryGetCompleted<Result>("operator-b", "retry-001", out _));
        Assert.Throws<AssignmentDomainException>(() =>
            store.Get<Payload>(token, "ASSIGNMENT", "operator-a"));

        store.Complete(token, "operator-a", "retry-001", new Result("operation-2"));
        Assert.True(store.TryGetCompleted<Result>("operator-a", "retry-001", out replay));
        Assert.Equal("operation-1", replay!.OperationId);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public void Idempotency_key_is_required_and_bounded()
    {
        var store = new AssignmentPreviewStore();

        Assert.Throws<AssignmentDomainException>(() =>
            store.TryGetCompleted<Result>("operator-a", " ", out _));
        Assert.Throws<AssignmentDomainException>(() =>
            store.TryGetCompleted<Result>("operator-a", new string('x', 101), out _));
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public async Task Concurrent_retries_execute_once_and_key_cannot_be_rebound_to_another_preview()
    {
        var store = new AssignmentPreviewStore();
        var (token, _) = store.Put("ASSIGNMENT", "operator-a", new Payload("first"));
        var calls = 0;

        async Task<Result> Confirm()
        {
            return await store.RunIdempotentAsync(token, "operator-a", "retry-002", async () =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(25);
                return new Result("operation-1");
            });
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Confirm()));

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Equal("operation-1", result.OperationId));

        var (otherToken, _) = store.Put("ASSIGNMENT", "operator-a", new Payload("second"));
        var rebound = await Assert.ThrowsAsync<AssignmentDomainException>(() =>
            store.RunIdempotentAsync(
                otherToken,
                "operator-a",
                "retry-002",
                () => Task.FromResult(new Result("must-not-run"))));
        Assert.Equal("CONFLICT", rebound.Code);
        Assert.Equal(409, rebound.StatusCode);
    }

    [Fact]
    [Trait("Category", "AssignmentFocused")]
    public async Task Concurrent_confirms_with_same_actor_and_key_but_different_tokens_conflict()
    {
        var store = new AssignmentPreviewStore();
        var (firstToken, _) = store.Put("ASSIGNMENT", "operator-a", new Payload("first"));
        var (secondToken, _) = store.Put("ASSIGNMENT", "operator-a", new Payload("second"));
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var first = store.RunIdempotentAsync(firstToken, "operator-a", "retry-003", async () =>
        {
            Interlocked.Increment(ref calls);
            firstEntered.SetResult(true);
            await releaseFirst.Task;
            return new Result("operation-first");
        });
        await firstEntered.Task;

        var second = store.RunIdempotentAsync(secondToken, "operator-a", "retry-003", () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new Result("operation-second"));
        });
        releaseFirst.SetResult(true);

        Assert.Equal("operation-first", (await first).OperationId);
        var conflict = await Assert.ThrowsAsync<AssignmentDomainException>(() => second);
        Assert.Equal("CONFLICT", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(1, calls);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow =
            new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void AdvanceWall(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }

        public void AdvanceMonotonic(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
        }
    }

    private sealed record Payload(string Value);
    private sealed record Result(string OperationId);
}
