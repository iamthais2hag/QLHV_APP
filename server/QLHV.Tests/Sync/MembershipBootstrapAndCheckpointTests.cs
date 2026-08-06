using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Realtime;
using QLHV.Application.Sync.Realtime.ControlPlane;

namespace QLHV.Tests.Sync;

public sealed class GlobalCheckpointBehaviorTests
{
    [Fact]
    public async Task First_publish_is_durable_and_verified()
    {
        var store = new InMemoryCheckpointStore();
        var checkpoint = TestData.Checkpoint(
            TestData.Route(),
            version: 10,
            cycleId: Guid.NewGuid());

        await store.PublishAsync(checkpoint);

        Assert.Equal(checkpoint, await store.ReadAsync(
            checkpoint.SourceProfile,
            checkpoint.TargetProfile,
            checkpoint.StreamCode));
        Assert.True(await store.VerifyAsync(checkpoint));
    }

    [Fact]
    public async Task Same_cycle_and_version_replay_is_idempotent()
    {
        var store = new InMemoryCheckpointStore();
        var checkpoint = TestData.Checkpoint(
            TestData.Route(),
            version: 10,
            cycleId: Guid.NewGuid());

        await store.PublishAsync(checkpoint);
        await store.PublishAsync(checkpoint);

        Assert.Equal(2, store.PublishAttempts);
        Assert.Equal(checkpoint, store.Value);
    }

    [Fact]
    public async Task Lower_version_is_rejected_without_overwrite()
    {
        var store = new InMemoryCheckpointStore();
        var current = TestData.Checkpoint(
            TestData.Route(),
            version: 11,
            cycleId: Guid.NewGuid());
        await store.PublishAsync(current);

        var error = await Assert.ThrowsAsync<CsdtAtomicCycleException>(() =>
            store.PublishAsync(TestData.Checkpoint(
                TestData.Route(),
                version: 10,
                cycleId: Guid.NewGuid())));

        Assert.Equal(CsdtAtomicCycleErrorCodes.CheckpointStale, error.ErrorCode);
        Assert.Equal(current, store.Value);
    }

    [Fact]
    public async Task Same_version_different_cycle_marks_conflict()
    {
        var store = new InMemoryCheckpointStore();
        await store.PublishAsync(TestData.Checkpoint(
            TestData.Route(),
            version: 10,
            cycleId: Guid.NewGuid()));

        var error = await Assert.ThrowsAsync<CsdtAtomicCycleException>(() =>
            store.PublishAsync(TestData.Checkpoint(
                TestData.Route(),
                version: 10,
                cycleId: Guid.NewGuid())));

        Assert.Equal(CsdtAtomicCycleErrorCodes.CheckpointConflict, error.ErrorCode);
        Assert.Equal(CsdtCheckpointStatus.Conflict, store.Value!.Status);
    }

    [Theory]
    [InlineData(false, true, "TARGET_COMMIT_NOT_VERIFIED")]
    [InlineData(true, false, "COVERAGE_INCOMPLETE")]
    public async Task Target_commit_and_complete_coverage_are_mandatory(
        bool targetCommitted,
        bool coverageComplete,
        string expected)
    {
        var store = new InMemoryCheckpointStore
        {
            TargetCommitted = targetCommitted,
            CoverageComplete = coverageComplete,
        };

        var error = await Assert.ThrowsAsync<CsdtAtomicCycleException>(() =>
            store.PublishAsync(TestData.Checkpoint(
                TestData.Route(),
                version: 10,
                cycleId: Guid.NewGuid())));

        Assert.Equal(expected, error.ErrorCode);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task Recovery_publish_after_target_commit_succeeds()
    {
        var store = new InMemoryCheckpointStore
        {
            TargetCommitted = false,
        };
        var checkpoint = TestData.Checkpoint(
            TestData.Route(),
            version: 12,
            cycleId: Guid.NewGuid());
        await Assert.ThrowsAsync<CsdtAtomicCycleException>(() =>
            store.PublishAsync(checkpoint));

        store.TargetCommitted = true;
        await store.PublishAsync(checkpoint);

        Assert.True(await store.VerifyAsync(checkpoint));
    }

    [Fact]
    public async Task Concurrent_compare_and_set_has_one_authoritative_cycle()
    {
        var store = new InMemoryCheckpointStore();
        var candidates = Enumerable.Range(0, 12)
            .Select(_ => TestData.Checkpoint(
                TestData.Route(),
                version: 20,
                cycleId: Guid.NewGuid()))
            .ToArray();
        var successful = 0;
        var conflicts = 0;

        await Parallel.ForEachAsync(candidates, async (candidate, _) =>
        {
            try
            {
                await store.PublishAsync(candidate);
                Interlocked.Increment(ref successful);
            }
            catch (CsdtAtomicCycleException exception)
                when (exception.ErrorCode == CsdtAtomicCycleErrorCodes.CheckpointConflict)
            {
                Interlocked.Increment(ref conflicts);
            }
        });

        Assert.Equal(1, successful);
        Assert.Equal(candidates.Length - 1, conflicts);
        Assert.Equal(CsdtCheckpointStatus.Conflict, store.Value!.Status);
    }

    [Theory]
    [InlineData("OTO_V2_TO_V1", "OTO_V2", "OTO_V1")]
    [InlineData("MOTO_V2_TO_V1", "MOTO_V2", "MOTO_V1")]
    [InlineData("OTO_V2_TO_V1", "OTO_V2_BAK", "OTO_V1_BAK")]
    [InlineData("MOTO_V2_TO_V1", "MOTO_V2_BAK", "MOTO_V1_BAK")]
    public async Task Oto_moto_and_live_bak_checkpoints_are_isolated(
        string stream,
        string source,
        string target)
    {
        Assert.True(CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
            stream,
            source,
            target,
            out var route));
        var store = new InMemoryCheckpointStore();
        var checkpoint = TestData.Checkpoint(route, 7, Guid.NewGuid());

        await store.PublishAsync(checkpoint);

        Assert.Equal(source, store.Value!.SourceProfile);
        Assert.Equal(target, store.Value.TargetProfile);
        Assert.Equal(stream, store.Value.StreamCode);
    }

    [Fact]
    public void Fingerprint_or_schema_mismatch_is_a_conflict()
    {
        var route = TestData.Route();
        var current = TestData.Checkpoint(route, 9, Guid.NewGuid());
        var candidate = current with
        {
            SourceWatermark = 10,
            SourceSchemaFingerprint = TestData.Fingerprint("changed-schema"),
        };

        Assert.Equal(
            CsdtCheckpointCasDecision.Conflict,
            CsdtCheckpointPublicationRules.Evaluate(
                current,
                candidate,
                targetCommitted: true,
                coverageComplete: true));
    }

    private sealed class InMemoryCheckpointStore : ICsdtGlobalCheckpointStore
    {
        private readonly object _gate = new();

        internal bool TargetCommitted { get; set; } = true;
        internal bool CoverageComplete { get; set; } = true;
        internal int PublishAttempts { get; private set; }
        internal CsdtGlobalCheckpoint? Value { get; private set; }

        public Task PublishAsync(
            CsdtGlobalCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                PublishAttempts++;
                var decision = CsdtCheckpointPublicationRules.Evaluate(
                    Value,
                    checkpoint,
                    TargetCommitted,
                    CoverageComplete);
                switch (decision)
                {
                    case CsdtCheckpointCasDecision.FirstPublish:
                    case CsdtCheckpointCasDecision.Advance:
                    case CsdtCheckpointCasDecision.IdempotentReplay:
                        Value = checkpoint;
                        return Task.CompletedTask;
                    case CsdtCheckpointCasDecision.StaleRejected:
                        throw new CsdtAtomicCycleException(
                            CsdtAtomicCycleErrorCodes.CheckpointStale);
                    case CsdtCheckpointCasDecision.TargetCommitRequired:
                        throw new CsdtAtomicCycleException(
                            CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
                    case CsdtCheckpointCasDecision.CoverageRequired:
                        throw new CsdtAtomicCycleException(
                            CsdtAtomicCycleErrorCodes.CoverageIncomplete);
                    default:
                        if (Value is not null)
                        {
                            Value = Value with
                            {
                                Status = CsdtCheckpointStatus.Conflict,
                            };
                        }

                        throw new CsdtAtomicCycleException(
                            CsdtAtomicCycleErrorCodes.CheckpointConflict);
                }
            }
        }

        public Task<CsdtGlobalCheckpoint?> ReadAsync(
            string sourceProfile,
            string targetProfile,
            string streamCode,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(
                    Value is not null &&
                    Value.SourceProfile == sourceProfile &&
                    Value.TargetProfile == targetProfile &&
                    Value.StreamCode == streamCode
                        ? Value
                        : null);
            }
        }

        public Task<bool> VerifyAsync(
            CsdtGlobalCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(Value == checkpoint);
            }
        }

        public Task MarkConflictAsync(
            string sourceProfile,
            string targetProfile,
            string streamCode,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (Value is not null)
                {
                    Value = Value with { Status = CsdtCheckpointStatus.Conflict };
                }

                return Task.CompletedTask;
            }
        }
    }
}

public sealed class MembershipBootstrapBehaviorTests
{
    [Fact]
    public void Six_domains_plan_in_parent_before_child_order()
    {
        var staged = TestData.Stage();
        var plan = CsdtMembershipBootstrapPlanner.Plan(
            staged,
            TestData.Observations(staged, CsdtMembershipBootstrapState.Absent));

        Assert.True(plan.CanApply);
        Assert.Equal(CsdtAtomicCoreDomains.ApplyOrder, plan.Domains.Select(item => item.DomainName));
        Assert.All(plan.Domains, domain =>
        {
            Assert.Equal(1, domain.SourceCount);
            Assert.Equal(1, domain.MembershipCreateCount);
        });
    }

    [Fact]
    public void Existing_active_membership_and_claim_are_verified_without_duplicate()
    {
        var staged = TestData.Stage();
        var plan = CsdtMembershipBootstrapPlanner.Plan(
            staged,
            TestData.Observations(
                staged,
                CsdtMembershipBootstrapState.ActiveApplied,
                hasClaim: true,
                targetVerified: true));

        Assert.True(plan.CanApply);
        Assert.All(plan.Domains, domain =>
        {
            Assert.Equal(0, domain.MembershipCreateCount);
            Assert.Equal(1, domain.ExistingActiveCount);
        });
    }

    [Theory]
    [InlineData(CsdtMembershipBootstrapState.Conflict, "OWNERSHIP_CONFLICT")]
    [InlineData(CsdtMembershipBootstrapState.InactiveApplied, "REACTIVATION_CANDIDATE")]
    public void Conflict_or_inactive_membership_blocks_global_bootstrap(
        CsdtMembershipBootstrapState state,
        string expectedReason)
    {
        var staged = TestData.Stage();
        var observations = TestData.Observations(
            staged,
            CsdtMembershipBootstrapState.ActiveApplied,
            hasClaim: true,
            targetVerified: true).ToList();
        observations[3] = observations[3] with { State = state };

        var plan = CsdtMembershipBootstrapPlanner.Plan(staged, observations);

        Assert.False(plan.CanApply);
        Assert.Equal(expectedReason, plan.BlockingReason);
    }

    [Fact]
    public void Missing_parent_blocks_without_coverage_or_checkpoint_readiness()
    {
        var staged = TestData.Stage();
        var observations = TestData.Observations(
            staged,
            CsdtMembershipBootstrapState.ActiveApplied,
            hasClaim: true,
            targetVerified: true).ToList();
        observations[^1] = observations[^1] with { ParentMembershipReady = false };

        var plan = CsdtMembershipBootstrapPlanner.Plan(staged, observations);

        Assert.False(plan.CanApply);
        Assert.Equal("BOOTSTRAP_PARENT_MISSING", plan.BlockingReason);
    }

    [Theory]
    [InlineData(CsdtTargetOnlyDisposition.UnclassifiedTargetOnly)]
    [InlineData(CsdtTargetOnlyDisposition.OwnershipConflict)]
    public void Unsafe_target_only_classification_blocks_bootstrap(
        CsdtTargetOnlyDisposition disposition)
    {
        var staged = TestData.Stage();

        var plan = CsdtMembershipBootstrapPlanner.Plan(
            staged,
            TestData.Observations(staged, CsdtMembershipBootstrapState.Absent),
            [disposition]);

        Assert.False(plan.CanApply);
        Assert.Equal(1, plan.TargetOnlyCounts[disposition]);
    }

    [Fact]
    public void National_rows_outside_exact_route_are_target_native()
    {
        var route = TestData.MembershipRoute("DM_DonViGTVT");

        var disposition = CsdtTargetOnlyClassifier.Classify(
            route,
            new CsdtTargetOnlyClassificationInput(
                "DM_DonViGTVT",
                IsInsideMappedScope: false,
                IsExactRoutedUnit: false,
                HasV1History: false,
                OwnershipEvidence: null));

        Assert.Equal(CsdtTargetOnlyDisposition.TargetNative, disposition);
    }

    [Fact]
    public void Unowned_target_row_in_mapped_scope_is_never_auto_claimed()
    {
        var route = TestData.MembershipRoute("KhoaHoc");

        var disposition = CsdtTargetOnlyClassifier.Classify(
            route,
            new CsdtTargetOnlyClassificationInput(
                "KhoaHoc",
                IsInsideMappedScope: true,
                IsExactRoutedUnit: true,
                HasV1History: false,
                OwnershipEvidence: null));

        Assert.Equal(CsdtTargetOnlyDisposition.UnclassifiedTargetOnly, disposition);
    }

    [Fact]
    public void Coverage_requires_counts_claims_hashes_and_committed_results_for_six_of_six()
    {
        var staged = TestData.Stage();
        var committed = staged.Domains.Select(domain =>
            new CsdtCommittedMembershipDomain(
                domain.DomainName,
                domain.SourceRowCount,
                domain.SourceRowCount,
                ConflictCount: 0,
                UnclassifiedTargetOnlyCount: 0,
                domain.SourceKeySetHash,
                DomainResultCommitted: true));

        var coverage = CsdtCoverageProtocol.Verify(staged, committed);

        Assert.True(coverage.IsGlobalComplete);
        Assert.Equal(6, coverage.Domains.Count);
        Assert.All(coverage.Domains, marker =>
        {
            Assert.True(marker.IsComplete);
            Assert.Equal(staged.CycleId, marker.CompletedCycleId);
            Assert.Equal(staged.EndSourceVersion, marker.BaselineSourceVersion);
            Assert.True(marker.SourceSchemaFingerprint!.Equals(staged.SourceSchemaFingerprint));
        });
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    public void Incomplete_domain_never_gets_complete_coverage(
        long memberships,
        long claims,
        bool committedResult)
    {
        var staged = TestData.Stage();
        var committed = staged.Domains.Select((domain, index) =>
            new CsdtCommittedMembershipDomain(
                domain.DomainName,
                index == 2 ? memberships : domain.SourceRowCount,
                index == 2 ? claims : domain.SourceRowCount,
                0,
                0,
                domain.SourceKeySetHash,
                index == 2 ? committedResult : true));

        var coverage = CsdtCoverageProtocol.Verify(staged, committed);

        Assert.False(coverage.IsGlobalComplete);
        Assert.False(coverage.Domains[2].IsComplete);
    }

    [Fact]
    public void Dry_run_is_structured_and_contains_no_raw_business_keys()
    {
        var staged = TestData.Stage();
        var bootstrap = CsdtMembershipBootstrapPlanner.Plan(
            staged,
            TestData.Observations(staged, CsdtMembershipBootstrapState.Absent));
        var report = CsdtMembershipDryRunFactory.Create(staged, bootstrap);
        var diagnostic = report.ToString();

        Assert.Equal(6, report.Domains.Count);
        Assert.Contains("RawKeys=false", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("N1", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("K1", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Arbitrary_or_optional_domain_is_rejected()
    {
        var route = TestData.MembershipRoute("GiaoVien");

        Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtTargetOnlyClassifier.Classify(
                route,
                new CsdtTargetOnlyClassificationInput(
                    "GiaoVien",
                    true,
                    true,
                    false,
                    null)));
    }

    [Fact]
    public void Bootstrap_and_coverage_reject_an_extra_optional_domain()
    {
        var staged = TestData.Stage();
        var observations = TestData.Observations(
            staged,
            CsdtMembershipBootstrapState.Absent).Append(
            new CsdtMembershipBootstrapObservation(
                "GiaoVien",
                new CsdtProtectedMembershipKey(
                    CanonicalBusinessKeyEncoder.Encode(
                        1,
                        CanonicalKeyComponent.FromString("redacted"))),
                CsdtMembershipBootstrapState.Absent,
                HasTypedOwnershipClaim: false,
                ParentMembershipReady: true,
                TargetRowVerified: false));
        var committed = staged.Domains.Select(domain =>
            new CsdtCommittedMembershipDomain(
                domain.DomainName,
                domain.SourceRowCount,
                domain.SourceRowCount,
                0,
                0,
                domain.SourceKeySetHash,
                true)).Append(
            new CsdtCommittedMembershipDomain(
                "GiaoVien",
                0,
                0,
                0,
                0,
                TestData.Fingerprint("extra-domain"),
                true));

        Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtMembershipBootstrapPlanner.Plan(staged, observations));
        Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtCoverageProtocol.Verify(staged, committed));
    }

    [Fact]
    public void Failed_bootstrap_never_reports_checkpoint_ready()
    {
        var staged = TestData.Stage();
        var bootstrap = CsdtMembershipBootstrapPlanner.Plan(
            staged,
            TestData.Observations(staged, CsdtMembershipBootstrapState.Absent),
            [CsdtTargetOnlyDisposition.UnclassifiedTargetOnly]);
        var coverage = CsdtCoverageProtocol.Verify(
            staged,
            staged.Domains.Select(domain =>
                new CsdtCommittedMembershipDomain(
                    domain.DomainName,
                    domain.SourceRowCount,
                    domain.SourceRowCount,
                    0,
                    0,
                    domain.SourceKeySetHash,
                    true)));

        var report = CsdtMembershipDryRunFactory.Create(
            staged,
            bootstrap,
            coverage: coverage);

        Assert.True(coverage.IsGlobalComplete);
        Assert.False(bootstrap.CanApply);
        Assert.False(report.CheckpointReady);
    }
}

public sealed class MembershipReconcileBehaviorTests
{
    [Fact]
    public void Equal_source_and_active_membership_is_observed_active()
    {
        var data = Setup();

        var plan = Plan(data, [data.Active]);

        Assert.Single(plan.Items);
        Assert.Equal(
            CsdtMembershipReconcileOutcome.ObservedActive,
            plan.Items[0].Outcome);
        Assert.True(plan.CheckpointReady);
    }

    [Fact]
    public void Source_only_key_is_insert_candidate()
    {
        var data = Setup();

        var plan = Plan(data, []);

        Assert.Equal(
            CsdtMembershipReconcileOutcome.InsertOrReactivateCandidate,
            Assert.Single(plan.Items).Outcome);
    }

    [Fact]
    public void Inactive_same_key_with_newer_version_is_reactivation_candidate()
    {
        var data = Setup();
        var inactive = data.Active with
        {
            Status = SourceMembershipStatus.Inactive,
            IsApplied = true,
            LastObservedSourceVersion = 8,
            AppliedSourceVersion = 8,
            DeletedAtSourceVersion = 8,
        };

        var plan = Plan(data, [inactive], sourceVersion: 10);

        var item = Assert.Single(plan.Items);
        Assert.True(item.IsReactivation);
    }

    [Fact]
    public void Active_membership_absent_from_source_is_non_applied_absence_candidate()
    {
        var data = Setup();

        var plan = CsdtMembershipReconcilePlanner.Plan(
            data.Route,
            [],
            [data.Active],
            data.Coverage,
            10,
            TestData.Mapping,
            TestData.RouteFingerprint,
            TestData.SourceSchema,
            TestData.TargetSchema,
            deleteExecutionEnabled: false);

        Assert.Equal(
            CsdtMembershipReconcileOutcome.AbsenceCandidate,
            Assert.Single(plan.Items).Outcome);
        Assert.False(plan.CheckpointReady);
        Assert.Equal("DELETE_EXECUTION_NOT_ENABLED", plan.BlockingReason);
    }

    [Theory]
    [InlineData("coverage")]
    [InlineData("mapping")]
    [InlineData("route")]
    [InlineData("source-schema")]
    [InlineData("target-schema")]
    public void Missing_or_stale_coverage_blocks_reconcile(string mismatch)
    {
        var data = Setup();
        StreamCoverageState? coverage = mismatch == "coverage"
            ? null
            : data.Coverage with
            {
                MappingFingerprint = mismatch == "mapping"
                    ? TestData.Fingerprint("bad")
                    : data.Coverage.MappingFingerprint,
                RouteFingerprint = mismatch == "route"
                    ? TestData.Fingerprint("bad")
                    : data.Coverage.RouteFingerprint,
                SourceSchemaFingerprint = mismatch == "source-schema"
                    ? TestData.Fingerprint("bad")
                    : data.Coverage.SourceSchemaFingerprint,
                TargetSchemaFingerprint = mismatch == "target-schema"
                    ? TestData.Fingerprint("bad")
                    : data.Coverage.TargetSchemaFingerprint,
            };

        var plan = CsdtMembershipReconcilePlanner.Plan(
            data.Route,
            [data.Key.CopyCanonical()],
            [data.Active],
            coverage,
            10,
            TestData.Mapping,
            TestData.RouteFingerprint,
            TestData.SourceSchema,
            TestData.TargetSchema,
            false);

        Assert.False(plan.CoverageReady);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Reconcile_api_has_no_target_row_input_and_never_inspects_unrelated_rows()
    {
        var parameters = typeof(CsdtMembershipReconcilePlanner)
            .GetMethod(nameof(CsdtMembershipReconcilePlanner.Plan))!
            .GetParameters()
            .Select(parameter => parameter.Name)
            .ToArray();

        Assert.DoesNotContain(parameters, name =>
            name!.Contains("target", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("row", StringComparison.OrdinalIgnoreCase));
    }

    private static CsdtMembershipReconcilePlan Plan(
        ReconcileData data,
        IEnumerable<CsdtMembershipEvidence> memberships,
        long sourceVersion = 10)
        => CsdtMembershipReconcilePlanner.Plan(
            data.Route,
            [data.Key.CopyCanonical()],
            memberships,
            data.Coverage,
            sourceVersion,
            TestData.Mapping,
            TestData.RouteFingerprint,
            TestData.SourceSchema,
            TestData.TargetSchema,
            false);

    private static ReconcileData Setup()
    {
        var route = TestData.MembershipRoute("NguoiLX");
        var key = new CsdtProtectedMembershipKey(
            CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString("N1")));
        var active = TestData.Evidence(route, key, SourceMembershipStatus.Active, 9);
        var coverage = new StreamCoverageState(
            route,
            9,
            TestData.Mapping,
            TestData.RouteFingerprint,
            TestData.Fingerprint("keys"),
            1,
            true,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            TestData.SourceSchema,
            TestData.TargetSchema);
        return new ReconcileData(route, key, active, coverage);
    }

    private sealed record ReconcileData(
        MembershipRoute Route,
        CsdtProtectedMembershipKey Key,
        CsdtMembershipEvidence Active,
        StreamCoverageState Coverage);
}

public sealed class TombstoneOwnershipBehaviorTests
{
    [Fact]
    public void Exact_active_owner_resolves_to_immutable_delete_pending_plan()
    {
        var setup = Setup();

        var result = CsdtTombstoneOwnershipResolver.Resolve(
            setup.Request,
            [setup.Owner]);

        Assert.Equal(CsdtTombstoneOwnershipOutcome.ResolvedActiveOwner, result.Outcome);
        Assert.NotNull(result.Plan);
        Assert.Equal(SourceMembershipReasonCode.SourceDelete, result.Plan.ReasonCode);
    }

    [Fact]
    public void Unowned_key_blocks()
    {
        var setup = Setup();

        var result = CsdtTombstoneOwnershipResolver.Resolve(setup.Request, []);

        Assert.Equal(CsdtTombstoneOwnershipOutcome.UnownedDeleteKey, result.Outcome);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Multiple_owner_evidence_blocks()
    {
        var setup = Setup();

        var result = CsdtTombstoneOwnershipResolver.Resolve(
            setup.Request,
            [setup.Owner, setup.Owner with { MembershipId = 2 }]);

        Assert.Equal(
            CsdtTombstoneOwnershipOutcome.MultipleOrAmbiguousOwner,
            result.Outcome);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Old_or_duplicate_tombstone_is_stale_no_op(long version)
    {
        var setup = Setup(sourceVersion: version);

        var result = CsdtTombstoneOwnershipResolver.Resolve(
            setup.Request,
            [setup.Owner]);

        Assert.Equal(CsdtTombstoneOwnershipOutcome.StaleTombstone, result.Outcome);
        Assert.Null(result.Plan);
    }

    [Fact]
    public void Different_stream_owner_conflicts()
    {
        var setup = Setup();
        var different = setup.Owner with
        {
            Route = TestData.MembershipRoute("NguoiLX", TestData.MotoRoute()),
        };

        var result = CsdtTombstoneOwnershipResolver.Resolve(
            setup.Request,
            [different]);

        Assert.Equal(
            CsdtTombstoneOwnershipOutcome.StreamOwnershipConflict,
            result.Outcome);
    }

    [Theory]
    [InlineData("mapping")]
    [InlineData("route")]
    public void Fingerprint_mismatch_blocks(string kind)
    {
        var setup = Setup();
        var owner = setup.Owner with
        {
            MappingFingerprint = kind == "mapping"
                ? TestData.Fingerprint("bad")
                : setup.Owner.MappingFingerprint,
            RouteFingerprint = kind == "route"
                ? TestData.Fingerprint("bad")
                : setup.Owner.RouteFingerprint,
        };

        var result = CsdtTombstoneOwnershipResolver.Resolve(
            setup.Request,
            [owner]);

        Assert.Equal(
            kind == "mapping"
                ? CsdtTombstoneOwnershipOutcome.MappingFingerprintMismatch
                : CsdtTombstoneOwnershipOutcome.RouteFingerprintMismatch,
            result.Outcome);
    }

    [Fact]
    public void Composite_document_key_matches_exact_ordered_tuple()
    {
        var route = TestData.MembershipRoute("NguoiLXHS_GiayTo");
        var typed = TypedTargetKeyClaim.ForNguoiLxHsGiayTo(7, "N1");
        var key = new CsdtProtectedMembershipKey(
            CsdtTypedKeyCanonicalizer.Canonicalize(typed));
        var owner = TestData.Evidence(
            route,
            key,
            SourceMembershipStatus.Active,
            5,
            typed);
        var request = new CsdtTombstoneOwnershipRequest(
            route,
            typed,
            6,
            1,
            TestData.Mapping,
            TestData.RouteFingerprint);

        var result = CsdtTombstoneOwnershipResolver.Resolve(request, [owner]);

        Assert.Equal(CsdtTombstoneOwnershipOutcome.ResolvedActiveOwner, result.Outcome);
        Assert.DoesNotContain("N1", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("N1", result.Plan!.ToString(), StringComparison.Ordinal);
    }

    private static TombstoneData Setup(long sourceVersion = 6)
    {
        var route = TestData.MembershipRoute("NguoiLX");
        var typed = TypedTargetKeyClaim.ForNguoiLx("N1");
        var key = new CsdtProtectedMembershipKey(
            CsdtTypedKeyCanonicalizer.Canonicalize(typed));
        var owner = TestData.Evidence(
            route,
            key,
            SourceMembershipStatus.Active,
            5,
            typed);
        return new TombstoneData(
            new CsdtTombstoneOwnershipRequest(
                route,
                typed,
                sourceVersion,
                1,
                TestData.Mapping,
                TestData.RouteFingerprint),
            owner);
    }

    private sealed record TombstoneData(
        CsdtTombstoneOwnershipRequest Request,
        CsdtMembershipEvidence Owner);
}

public sealed class ReactivationPlanningBehaviorTests
{
    [Fact]
    public void Same_stream_newer_version_plans_parent_order_and_preservation()
    {
        var request = Request();

        var plan = CsdtReactivationPlanner.Plan(request);

        Assert.Equal(CsdtReactivationOutcome.Planned, plan.Outcome);
        Assert.True(plan.PreserveV1History);
        Assert.True(plan.ResyncV2OwnedColumns);
        Assert.False(plan.CreateDuplicateShell);
        Assert.False(plan.ResetCompletedLifecycle);
        Assert.Equal(
            ["TT_XuLy", "GhiChu", "GiayCNSK", "GiaiTrinh"],
            plan.ConditionalMergeColumns);
    }

    [Fact]
    public void Different_stream_is_rejected()
    {
        var request = Request();
        request = request with
        {
            Existing = request.Existing with
            {
                Route = TestData.MembershipRoute("NguoiLX_HoSo", TestData.MotoRoute()),
            },
        };

        var plan = CsdtReactivationPlanner.Plan(request);

        Assert.Equal(CsdtReactivationOutcome.DifferentStreamRejected, plan.Outcome);
    }

    [Fact]
    public void Missing_parent_blocks_child_reactivation()
    {
        var request = Request() with
        {
            ReadyParentDomains = ["DM_DonViGTVT", "KhoaHoc"],
        };

        var plan = CsdtReactivationPlanner.Plan(request);

        Assert.Equal(CsdtReactivationOutcome.ParentMissing, plan.Outcome);
    }

    [Fact]
    public void Old_version_and_active_replay_are_no_ops()
    {
        var stale = CsdtReactivationPlanner.Plan(Request() with { SourceVersion = 5 });
        var activeRequest = Request();
        var active = CsdtReactivationPlanner.Plan(activeRequest with
        {
            Existing = activeRequest.Existing with
            {
                Status = SourceMembershipStatus.Active,
            },
        });

        Assert.Equal(CsdtReactivationOutcome.StaleSourceVersion, stale.Outcome);
        Assert.Equal(CsdtReactivationOutcome.AlreadyActiveReplay, active.Outcome);
    }

    [Fact]
    public void Only_inactive_applied_membership_can_plan_reactivation()
    {
        var request = Request();
        var plan = CsdtReactivationPlanner.Plan(request with
        {
            Existing = request.Existing with
            {
                Status = SourceMembershipStatus.DeletePending,
            },
        });

        Assert.Equal(CsdtReactivationOutcome.OwnershipConflict, plan.Outcome);
        Assert.False(plan.ResyncV2OwnedColumns);
    }

    private static CsdtReactivationRequest Request()
    {
        var route = TestData.MembershipRoute("NguoiLX_HoSo");
        var typed = TypedTargetKeyClaim.ForNguoiLxHoSo("N1");
        var key = new CsdtProtectedMembershipKey(
            CsdtTypedKeyCanonicalizer.Canonicalize(typed));
        var evidence = TestData.Evidence(
            route,
            key,
            SourceMembershipStatus.Inactive,
            5,
            typed) with
        {
            DeletedAtSourceVersion = 5,
        };
        return new CsdtReactivationRequest(
            route,
            evidence,
            6,
            CsdtAtomicCoreDomains.ApplyOrder.Take(4).ToArray(),
            ExistingShellFound: true,
            CompletedLifecycle: true);
    }
}

public sealed class MembershipFoundationSafetyTests
{
    [Fact]
    public void Feature_defaults_are_all_false()
    {
        var options = new CsdtRealtimeSyncOptions();

        Assert.False(options.UseAtomicMappedTableCycle);
        Assert.False(options.EnableMembershipBootstrap);
        Assert.False(options.EnableMembershipReconcile);
        Assert.False(options.EnableDeleteExecution);
    }

    [Fact]
    public void Production_processor_and_di_remain_disconnected()
    {
        var processor = TestData.ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtRealtimeStreamProcessor.cs");
        var di = TestData.ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "DependencyInjection.cs");

        foreach (var name in new[]
                 {
                     "CsdtSqlGlobalCheckpointStore",
                     "CsdtMembershipBootstrapTransactionApplier",
                     "CsdtMembershipReconcilePlanner",
                     "CsdtTombstoneOwnershipResolver",
                     "CsdtReactivationPlanner",
                 })
        {
            Assert.DoesNotContain(name, processor, StringComparison.Ordinal);
            Assert.DoesNotContain(name, di, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Bootstrap_foundation_has_no_business_delete_or_inactive_apply()
    {
        var bootstrap = TestData.ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime", "ControlPlane",
            "CsdtMembershipBootstrapTransactionApplier.cs");

        Assert.DoesNotContain("DELETE FROM", bootstrap, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApplyInactiveAsync", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("HardDeleted", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void Durable_store_sql_is_parameterized_and_diagnostics_are_key_free()
    {
        var store = TestData.ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime", "ControlPlane",
            "CsdtSqlGlobalCheckpointStore.cs");

        Assert.Contains("@TargetProfile", store, StringComparison.Ordinal);
        Assert.Contains("@SourceProfile", store, StringComparison.Ordinal);
        Assert.Contains("@StreamCode", store, StringComparison.Ordinal);
        Assert.DoesNotContain("CanonicalBusinessKey", store, StringComparison.Ordinal);
        Assert.DoesNotContain("MaDK", store, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("20260726_add_csdt_control_plane_oto_v1.sql")]
    [InlineData("20260726_add_csdt_control_plane_moto_v1.sql")]
    [InlineData("20260726_add_csdt_control_plane_oto_v1_bak.sql")]
    [InlineData("20260726_add_csdt_control_plane_moto_v1_bak.sql")]
    public void Patches_define_exact_durable_checkpoint_and_do_not_change_database_options(
        string file)
    {
        var patch = TestData.ReadWorkspaceFile("database", "patches", file);

        Assert.Contains("CREATE TABLE dbo.QLHV_CsdtRealtimeCheckpoint", patch);
        Assert.Contains("UX_QLHV_CsdtRealtimeCheckpoint_Stream", patch);
        Assert.Contains("SourceSchemaFingerprint binary(32) NOT NULL", patch);
        Assert.Contains("TargetSchemaFingerprint binary(32) NOT NULL", patch);
        Assert.Contains("CHECKPOINT_CONFLICT", patch);
        Assert.Contains("BOOTSTRAP_PARENT_MISSING", patch);
        Assert.DoesNotContain("ALTER DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ENABLE_CHANGE_TRACKING", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER PROCEDURE", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Protected_development_configs_remain_byte_identical()
    {
        const string expected =
            "12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E";
        foreach (var parts in new[]
                 {
                     new[] { "server", "QLHV.Api", "appsettings.Development.json" },
                     new[] { "server", "QLHV.Worker", "appsettings.Development.json" },
                 })
        {
            var path = TestData.FindWorkspaceFile(parts);
            Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }

    [Fact]
    public void Control_plane_repository_never_commits_caller_transaction()
    {
        var repository = TestData.ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime", "ControlPlane",
            "CsdtRealtimeTargetControlPlaneRepository.cs");

        Assert.DoesNotContain("CommitAsync(", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("RollbackAsync(", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransaction", repository, StringComparison.Ordinal);
    }
}

internal static class TestData
{
    internal static readonly ControlPlaneFingerprint Mapping = Fingerprint("mapping");
    internal static readonly ControlPlaneFingerprint RouteFingerprint = Fingerprint("route");
    internal static readonly ControlPlaneFingerprint SourceSchema = Fingerprint("source-schema");
    internal static readonly ControlPlaneFingerprint TargetSchema = Fingerprint("target-schema");

    internal static CsdtRealtimeRouteDefinition Route()
        => CsdtRealtimeStreamCatalog.LiveRoutes[0];

    internal static CsdtRealtimeRouteDefinition MotoRoute()
        => CsdtRealtimeStreamCatalog.LiveRoutes[1];

    internal static MembershipRoute MembershipRoute(
        string table,
        CsdtRealtimeRouteDefinition? route = null)
    {
        route ??= Route();
        return new MembershipRoute(
            route.TargetProfileCode,
            route.SourceProfileCode,
            route.StreamCode,
            route.MaCSDT,
            table);
    }

    internal static CsdtGlobalCheckpoint Checkpoint(
        CsdtRealtimeRouteDefinition route,
        long version,
        Guid cycleId)
        => new(
            cycleId,
            route.SourceProfileCode,
            route.TargetProfileCode,
            route.StreamCode,
            version,
            Mapping,
            CsdtAtomicRouteFingerprint.Compute(route),
            Fingerprint($"keys-{route.TargetProfileCode}-{version}"),
            SourceSchema,
            TargetSchema);

    internal static CsdtStagedCycle Stage()
    {
        var route = Route();
        var domains = new[]
        {
            Domain("DM_DonViGTVT", Row(
                Key("66029"),
                ("MaDV", "66029"))),
            Domain("KhoaHoc", Row(
                Key("K1"),
                ("MaKH", "K1"),
                ("MaCSDT", "66029"))),
            Domain("BaoCaoI", Row(
                Key("B1"),
                ("MaBCI", "B1"),
                ("MaCSDT", "66029"),
                ("MaKH", "K1"))),
            Domain("NguoiLX", Row(
                Key("N1"),
                ("MaDK", "N1"),
                ("DonViNhanHSo", "66029"))),
            Domain("NguoiLX_HoSo", Row(
                Key("N1"),
                ("MaDK", "N1"),
                ("MaCSDT", "66029"),
                ("MaKhoaHoc", "K1"),
                ("MaBC1", "B1"),
                ("TT_XuLy", "03"))),
            Domain("NguoiLXHS_GiayTo", Row(
                CanonicalBusinessKeyEncoder.Encode(
                    1,
                    CanonicalKeyComponent.FromInt32(1),
                    CanonicalKeyComponent.FromString("N1")).ToArray(),
                ("MaGT", 1),
                ("MaDK", "N1"))),
        };
        return new CsdtStagedCycle(
            Guid.NewGuid(),
            route.SourceProfileCode,
            route.TargetProfileCode,
            route.StreamCode,
            route.MaCSDT,
            0,
            10,
            Mapping,
            CsdtAtomicRouteFingerprint.Compute(route),
            SourceSchema,
            TargetSchema,
            DateTimeOffset.UtcNow,
            1,
            TargetEqualityProof.ProofId,
            domains,
            CsdtAtomicStageFactory.ComputeCycleKeySetHash(domains));
    }

    internal static IEnumerable<CsdtMembershipBootstrapObservation> Observations(
        CsdtStagedCycle staged,
        CsdtMembershipBootstrapState state,
        bool hasClaim = false,
        bool targetVerified = false)
        => staged.Domains.Select(domain =>
            new CsdtMembershipBootstrapObservation(
                domain.DomainName,
                new CsdtProtectedMembershipKey(
                    CanonicalBusinessKey.FromEncoded(
                        Assert.Single(domain.CompleteKeys))),
                state,
                hasClaim,
                ParentMembershipReady: true,
                targetVerified));

    internal static CsdtMembershipEvidence Evidence(
        MembershipRoute route,
        CsdtProtectedMembershipKey key,
        SourceMembershipStatus status,
        long version,
        TypedTargetKeyClaim? typed = null)
        => new(
            MembershipId: 1,
            route,
            key,
            typed ?? Typed(route.TableName),
            status,
            IsApplied: true,
            OwnershipReserved: true,
            LastObservedSourceVersion: version,
            AppliedSourceVersion: version,
            DeletedAtSourceVersion: null,
            ReactivatedAtSourceVersion: null,
            Mapping,
            RouteFingerprint);

    internal static ControlPlaneFingerprint Fingerprint(string text)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string ReadWorkspaceFile(params string[] parts)
        => File.ReadAllText(FindWorkspaceFile(parts));

    internal static string FindWorkspaceFile(
        string[] parts,
        [CallerFilePath] string caller = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(caller)!);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.");
    }

    private static CsdtStagedDomain Domain(
        string name,
        CsdtStagedRow row)
        => CsdtAtomicStageFactory.CreateDomain(
            name,
            CsdtAtomicOperationMode.FullSnapshot,
            [row]);

    private static CsdtStagedRow Row(
        byte[] key,
        params (string Name, object? Value)[] values)
        => new(
            key,
            values.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal));

    private static byte[] Key(string value)
        => CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString(value)).ToArray();

    private static TypedTargetKeyClaim Typed(string table)
        => table switch
        {
            "DM_DonViGTVT" => TypedTargetKeyClaim.ForDmDonViGtvt("66029"),
            "KhoaHoc" => TypedTargetKeyClaim.ForKhoaHoc("K1"),
            "BaoCaoI" => TypedTargetKeyClaim.ForBaoCaoI("B1"),
            "NguoiLX" => TypedTargetKeyClaim.ForNguoiLx("N1"),
            "NguoiLX_HoSo" => TypedTargetKeyClaim.ForNguoiLxHoSo("N1"),
            "NguoiLXHS_GiayTo" =>
                TypedTargetKeyClaim.ForNguoiLxHsGiayTo(1, "N1"),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
}
