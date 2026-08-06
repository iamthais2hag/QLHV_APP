using System.Security.Cryptography;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Tests.Sync;

public sealed class SyncCycleStateMachineTests
{
    [Fact]
    public void Cycle_follows_the_only_publishable_happy_path()
    {
        var status = SyncCycleStatus.Preparing;
        foreach (var next in new[]
                 {
                     SyncCycleStatus.Staged,
                     SyncCycleStatus.Validated,
                     SyncCycleStatus.TargetCommitting,
                     SyncCycleStatus.TargetCommitted,
                     SyncCycleStatus.CheckpointPublished,
                     SyncCycleStatus.Complete,
                 })
        {
            status = SyncCycleStateMachine.Transition(status, next);
        }

        Assert.Equal(SyncCycleStatus.Complete, status);
    }

    [Fact]
    public void Every_non_contract_cycle_transition_is_rejected()
    {
        var statuses = Enum.GetValues<SyncCycleStatus>();
        var valid = new HashSet<(SyncCycleStatus, SyncCycleStatus)>
        {
            (SyncCycleStatus.Preparing, SyncCycleStatus.Staged),
            (SyncCycleStatus.Staged, SyncCycleStatus.Validated),
            (SyncCycleStatus.Validated, SyncCycleStatus.TargetCommitting),
            (SyncCycleStatus.TargetCommitting, SyncCycleStatus.TargetCommitted),
            (SyncCycleStatus.TargetCommitted, SyncCycleStatus.CheckpointPublished),
            (SyncCycleStatus.CheckpointPublished, SyncCycleStatus.Complete),
        };

        foreach (var status in statuses)
        {
            valid.Add((status, status));
            if (status is not (
                    SyncCycleStatus.Complete or
                    SyncCycleStatus.Failed or
                    SyncCycleStatus.Conflict))
            {
                valid.Add((status, SyncCycleStatus.Failed));
                valid.Add((status, SyncCycleStatus.Conflict));
            }
        }

        foreach (var before in statuses)
        {
            foreach (var after in statuses)
            {
                Assert.Equal(
                    valid.Contains((before, after)),
                    SyncCycleStateMachine.CanTransition(before, after));
            }
        }
    }

    [Fact]
    public void Checkpoint_cannot_be_published_before_target_commit()
        => Assert.Throws<InvalidOperationException>(() =>
            SyncCycleStateMachine.Transition(
                SyncCycleStatus.Validated,
                SyncCycleStatus.CheckpointPublished));

    [Fact]
    public void Incomplete_coverage_is_fail_closed()
    {
        var mapping = Fingerprint(1);
        var route = Fingerprint(2);
        var coverage = new StreamCoverageState(
            new MembershipRoute(
                "OTO_V1",
                "OTO_V2",
                "OTO_V2_TO_V1",
                "66029",
                "NguoiLX"),
            10,
            mapping,
            route,
            Fingerprint(3),
            100,
            IsComplete: false,
            CompletedCycleId: null,
            CompletedAtUtc: null);

        Assert.False(coverage.AllowsDeleteReconciliation(mapping, route));
    }

    [Fact]
    public void Complete_coverage_still_fails_closed_on_fingerprint_mismatch()
    {
        var mapping = Fingerprint(1);
        var route = Fingerprint(2);
        var coverage = new StreamCoverageState(
            new MembershipRoute(
                "OTO_V1",
                "OTO_V2",
                "OTO_V2_TO_V1",
                "66029",
                "NguoiLX"),
            10,
            mapping,
            route,
            Fingerprint(3),
            100,
            IsComplete: true,
            CompletedCycleId: Guid.NewGuid(),
            CompletedAtUtc: DateTimeOffset.UtcNow);

        Assert.False(
            coverage.AllowsDeleteReconciliation(Fingerprint(99), route));
        Assert.False(
            coverage.AllowsDeleteReconciliation(mapping, Fingerprint(99)));
    }

    private static ControlPlaneFingerprint Fingerprint(byte value)
        => new(Enumerable.Repeat(value, SHA256.HashSizeInBytes).ToArray());
}
