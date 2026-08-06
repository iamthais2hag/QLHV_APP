using QLHV.Application.Runtime;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03FullConvergenceRecoveryTests
{
    [Fact]
    public void Checkpoint_equal_minimum_valid_is_incremental_valid()
    {
        var audit = Audit(checkpoint: 70, minimum: 70);

        Assert.Equal(
            Rt03RecoveryClassifications.IncrementalValid,
            Rt03ChangeTrackingRecoveryClassifier.Classify(audit));
    }

    [Fact]
    public void Checkpoint_below_minimum_valid_requires_full_convergence()
    {
        var audit = Audit(checkpoint: 25, minimum: 70);

        Assert.Equal(
            Rt03RecoveryClassifications.ExpiredRequiresFullConvergence,
            Rt03ChangeTrackingRecoveryClassifier.Classify(audit));
    }

    [Fact]
    public void Ct_disabled_requires_snapshot_and_never_checkpoint_skip()
    {
        var audit = Audit(
            checkpoint: 25,
            minimum: null,
            trackingEnabled: false);

        Assert.Equal(
            Rt03RecoveryClassifications.CtDisabledRequiresSnapshot,
            Rt03ChangeTrackingRecoveryClassifier.Classify(audit));
    }

    [Fact]
    public void Unverified_delete_contract_fails_closed()
    {
        var audit = Audit(
            checkpoint: 25,
            minimum: 70,
            deleteContractVerified: false);

        Assert.Equal(
            Rt03RecoveryClassifications.UnsafeDeleteContract,
            Rt03ChangeTrackingRecoveryClassifier.Classify(audit));
    }

    [Fact]
    public void Lock_order_is_global_profile_writer_then_domains()
    {
        var locks = Rt03FullConvergenceLocks.ForProfile(Rt03Profiles.Oto);

        Assert.Equal("QLHV:CSDT_AUTO_SYNC", locks[0]);
        Assert.Equal("QLHV:RT03:RECOVERY:CSDT_OTO", locks[1]);
        Assert.Equal("QLHV:CSDT_OPERATIONS:OTO", locks[2]);
        Assert.Equal(
            [
                "QLHV:RT03:RECOVERY:CSDT_OTO:COURSE",
                "QLHV:RT03:RECOVERY:CSDT_OTO:TEACHER",
                "QLHV:RT03:RECOVERY:CSDT_OTO:VEHICLE",
                "QLHV:RT03:RECOVERY:CSDT_OTO:LEARNER",
                "QLHV:RT03:RECOVERY:CSDT_OTO:RELATION",
            ],
            locks.Skip(3));
    }

    [Fact]
    public void Full_convergence_orders_course_before_learner()
    {
        var ordered = Rt03FullConvergenceDomains.Ordered.ToList();
        Assert.True(
            ordered.IndexOf(
                Rt03FullConvergenceDomains.Course) <
            ordered.IndexOf(
                Rt03FullConvergenceDomains.Learner));
        Assert.Equal(
            [
                "COURSE",
                "TEACHER",
                "VEHICLE",
                "LEARNER",
                "RELATION",
            ],
            Rt03FullConvergenceDomains.Ordered);
    }

    [Fact]
    public void External_full_sync_exact_target_becomes_no_change()
    {
        var plan = Plan(
            source: [Source("A", "hash-1")],
            target: [Target(1, "A", "hash-1", "qlhv")]);

        Assert.Equal(Rt03FullConvergenceActions.NoChange, Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void External_full_sync_stale_target_updates_only_source_owned()
    {
        var plan = Plan(
            source: [Source("A", "hash-new")],
            target: [Target(1, "A", "hash-old", "qlhv")]);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(Rt03FullConvergenceActions.UpdateSourceOwned, row.Action);
        Assert.Equal("qlhv", row.ExpectedQlhvOwnedHash);
    }

    [Fact]
    public void Duplicate_target_is_blocked_ambiguous()
    {
        var plan = Plan(
            source: [Source("A", "hash")],
            target:
            [
                Target(1, "A", "hash", "q1"),
                Target(2, "A", "hash", "q2"),
            ]);

        Assert.False(plan.IsSafe);
        Assert.Equal(
            Rt03FullConvergenceActions.BlockedAmbiguous,
            Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Missing_source_without_lifecycle_contract_is_blocked()
    {
        var plan = Plan(
            source: [],
            target: [Target(1, "A", "hash", "qlhv")],
            missingSourceLifecycleVerified: false);

        Assert.Equal(
            Rt03FullConvergenceActions.BlockedDeleteContract,
            Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Source_inactive_uses_exact_lifecycle_action()
    {
        var plan = Plan(
            source: [Source("A", "hash", active: false)],
            target: [Target(1, "A", "hash", "qlhv")]);

        Assert.Equal(
            Rt03FullConvergenceActions.MarkSourceInactive,
            Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Assigned_vehicle_is_never_hard_deleted()
    {
        var plan = Plan(
            Rt03FullConvergenceDomains.Vehicle,
            source: [],
            target: [Target(1, "A", "hash", "assignment", assigned: true)]);

        Assert.Equal(
            Rt03FullConvergenceActions.ManualReview,
            Assert.Single(plan.Rows).Action);
    }

    [Fact]
    public void Oto_and_moto_exact_identities_do_not_collide()
    {
        var allTargets = new[]
        {
            Target(1, "A", "oto", "q", profile: Rt03Profiles.Oto),
            Target(2, "A", "moto", "q", profile: Rt03Profiles.Moto),
        };

        var oto = Plan(
            source: [Source("A", "oto")],
            target: allTargets);
        var moto = Rt03FullConvergencePlanner.Plan(
            Rt03Profiles.Moto,
            Rt03FullConvergenceDomains.Course,
            [Source("A", "moto", profile: Rt03Profiles.Moto)],
            allTargets,
            missingSourceLifecycleVerified: true);

        Assert.Equal(Rt03FullConvergenceActions.NoChange, Assert.Single(oto.Rows).Action);
        Assert.Equal(Rt03FullConvergenceActions.NoChange, Assert.Single(moto.Rows).Action);
    }

    [Fact]
    public void Qlhv_owned_assignment_hash_must_be_preserved()
    {
        var plan = Plan(
            source: [Source("A", "new")],
            target: [Target(1, "A", "old", "assignment-hash")]);

        var error = Assert.Throws<Rt03SafetyException>(() =>
            Rt03FullConvergencePlanner.VerifyQlhvOwnedPreserved(
                plan,
                new Dictionary<long, string> { [1] = "changed" }));

        Assert.Equal(Rt03Errors.OwnershipProofRejected, error.Code);
    }

    [Fact]
    public void Five_thousand_rows_are_planned_in_one_set_without_regression()
    {
        var source = Enumerable.Range(1, 5_000)
            .Select(index => Source($"K{index:D5}", $"H{index:D5}"))
            .ToArray();
        var target = Enumerable.Range(1, 5_000)
            .Select(index => Target(
                index,
                $"K{index:D5}",
                $"H{index:D5}",
                $"Q{index:D5}"))
            .ToArray();

        var plan = Plan(source: source, target: target);

        Assert.True(plan.IsSafe);
        Assert.Equal(5_000, plan.Rows.Count);
        Assert.All(plan.Rows, row =>
            Assert.Equal(Rt03FullConvergenceActions.NoChange, row.Action));
    }

    [Fact]
    public void Failure_after_domain_commit_resumes_by_idempotent_domain_replay()
    {
        var session = Session(
            committedDomains:
                new HashSet<string>([Rt03FullConvergenceDomains.Course], StringComparer.Ordinal));

        Assert.Equal(
            Rt03RecoveryNextActions.ExecuteOrReplayDomains,
            Rt03RecoveryStateMachine.Next(
                session,
                TimeHealthStatuses.Healthy,
                allDomainsClassified: true));
    }

    [Fact]
    public void Verification_is_required_before_atomic_finalize()
    {
        var all = Rt03FullConvergenceDomains.Ordered.ToHashSet(StringComparer.Ordinal);
        var session = Session(committedDomains: all, verificationPassed: false);

        Assert.Equal(
            Rt03RecoveryNextActions.Verify,
            Rt03RecoveryStateMachine.Next(
                session,
                TimeHealthStatuses.Healthy,
                allDomainsClassified: true));
    }

    [Fact]
    public void Marker_and_checkpoint_cannot_be_partially_published()
    {
        var all = Rt03FullConvergenceDomains.Ordered.ToHashSet(StringComparer.Ordinal);
        var session = Session(
            committedDomains: all,
            verificationPassed: true,
            markerExists: true);

        Assert.Equal(
            Rt03RecoveryNextActions.Blocked,
            Rt03RecoveryStateMachine.Next(
                session,
                TimeHealthStatuses.Healthy,
                allDomainsClassified: true));
    }

    [Fact]
    public void Completed_recovery_with_marker_and_anchor_checkpoint_replays_after_anchor()
    {
        var all = Rt03FullConvergenceDomains.Ordered.ToHashSet(StringComparer.Ordinal);
        var session = Session(
            committedDomains: all,
            verificationPassed: true,
            markerExists: true) with
        {
            Status = Rt03RecoverySessionStatuses.Completed,
            CurrentCheckpoint = 84,
        };

        Assert.Equal(
            Rt03RecoveryNextActions.ReplayAfterAnchor,
            Rt03RecoveryStateMachine.Next(
                session,
                TimeHealthStatuses.Healthy,
                allDomainsClassified: true));
    }

    [Theory]
    [InlineData("BLOCKED")]
    [InlineData("WARNING")]
    public void Non_healthy_time_blocks_recovery(string timeHealth)
    {
        Assert.Equal(
            Rt03RecoveryNextActions.Blocked,
            Rt03RecoveryStateMachine.Next(
                Session(),
                timeHealth,
                allDomainsClassified: true));
    }

    [Fact]
    public void Healthy_time_allows_preflight_to_continue()
    {
        Assert.Equal(
            Rt03RecoveryNextActions.ExecuteOrReplayDomains,
            Rt03RecoveryStateMachine.Next(
                Session(),
                TimeHealthStatuses.Healthy,
                allDomainsClassified: true));
    }

    [Fact]
    public void Error_two_is_stale_only_with_timestamped_newer_success()
    {
        var error = DateTimeOffset.Parse("2026-07-31T01:00:00Z");
        var evidence = StableTimeEvidence(
            lastErrorCode: 2,
            lastErrorAt: error,
            lastSuccessAt: error.AddMinutes(1));

        Assert.Equal(
            TimeSyncDiagnosticClassifications.VerifiedStaleAfterFreshSuccess,
            TimeSyncDiagnosticClassifier.Classify(
                evidence,
                "time.windows.com,0x9"));
    }

    [Fact]
    public void Error_two_without_error_timestamp_is_current_failure()
    {
        var evidence = StableTimeEvidence(
            lastErrorCode: 2,
            lastErrorAt: null,
            lastSuccessAt: DateTimeOffset.Parse("2026-07-31T01:01:00Z"));

        Assert.Equal(
            TimeSyncDiagnosticClassifications.CurrentFailure,
            TimeSyncDiagnosticClassifier.Classify(
                evidence,
                "time.windows.com,0x9"));
    }

    [Fact]
    public void Error_two_with_unstable_phase_is_not_stale_safe()
    {
        var error = DateTimeOffset.Parse("2026-07-31T01:00:00Z");
        var evidence = StableTimeEvidence(
            lastErrorCode: 2,
            lastErrorAt: error,
            lastSuccessAt: error.AddMinutes(1)) with
        {
            PhaseOffsetMilliseconds = [250, 310, 31_000],
        };

        Assert.Equal(
            TimeSyncDiagnosticClassifications.CurrentFailure,
            TimeSyncDiagnosticClassifier.Classify(
                evidence,
                "time.windows.com,0x9"));
    }

    private static Rt03TrackedTableAudit Audit(
        long checkpoint,
        long? minimum,
        bool trackingEnabled = true,
        bool deleteContractVerified = true)
        => new(
            Rt03Profiles.Oto,
            "dbo.KhoaHoc",
            true,
            trackingEnabled,
            minimum,
            checkpoint,
            deleteContractVerified);

    private static Rt03FullConvergenceDomainPlan Plan(
        IReadOnlyCollection<Rt03FullConvergenceSourceRow> source,
        IReadOnlyCollection<Rt03FullConvergenceTargetRow> target,
        bool missingSourceLifecycleVerified = true)
        => Plan(
            Rt03FullConvergenceDomains.Course,
            source,
            target,
            missingSourceLifecycleVerified);

    private static Rt03FullConvergenceDomainPlan Plan(
        string domain,
        IReadOnlyCollection<Rt03FullConvergenceSourceRow> source,
        IReadOnlyCollection<Rt03FullConvergenceTargetRow> target,
        bool missingSourceLifecycleVerified = true)
        => Rt03FullConvergencePlanner.Plan(
            Rt03Profiles.Oto,
            domain,
            source.Select(row => row with { Domain = domain }).ToArray(),
            target.Select(row => row with { Domain = domain }).ToArray(),
            missingSourceLifecycleVerified);

    private static Rt03FullConvergenceSourceRow Source(
        string identity,
        string sourceHash,
        bool active = true,
        string profile = Rt03Profiles.Oto)
        => new(
            profile,
            Rt03FullConvergenceDomains.Course,
            identity,
            sourceHash,
            active);

    private static Rt03FullConvergenceTargetRow Target(
        long id,
        string identity,
        string sourceHash,
        string qlhvHash,
        bool assigned = false,
        string profile = Rt03Profiles.Oto)
        => new(
            id,
            profile,
            Rt03FullConvergenceDomains.Course,
            identity,
            sourceHash,
            qlhvHash,
            IsDeleted: false,
            HasActiveAssignment: assigned,
            IsManualHold: false);

    private static Rt03RecoverySessionSnapshot Session(
        IReadOnlySet<string>? committedDomains = null,
        bool verificationPassed = false,
        bool markerExists = false)
        => new(
            Guid.NewGuid(),
            Rt03Profiles.Oto,
            Guid.NewGuid(),
            CheckpointBefore: 25,
            AnchorVersion: 84,
            Rt03RecoverySessionStatuses.Preparing,
            committedDomains ?? new HashSet<string>(StringComparer.Ordinal),
            verificationPassed,
            markerExists,
            CurrentCheckpoint: 25);

    private static TimeSyncDiagnosticEvidence StableTimeEvidence(
        int lastErrorCode,
        DateTimeOffset? lastErrorAt,
        DateTimeOffset? lastSuccessAt)
        => new(
            lastErrorCode,
            lastErrorAt,
            lastSuccessAt,
            "time.windows.com,0x9",
            Stratum: 5,
            PhaseOffsetMilliseconds: [250, 260, 255],
            ApiSqlSkewMilliseconds: [300, 280, 310],
            ConsecutiveStableSamples: 3);
}
