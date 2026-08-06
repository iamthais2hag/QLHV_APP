using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Realtime;
using QLHV.Application.Sync.Realtime.ControlPlane;
using QLHV.Infrastructure.Sync.Realtime;
using QLHV.Infrastructure.Sync.Realtime.ControlPlane;

namespace QLHV.Tests.Sync;

public sealed class AtomicMappedTableCycleTests
{
    [Fact]
    public void Core_scope_and_apply_order_are_exact()
    {
        Assert.Equal(
            [
                "DM_DonViGTVT",
                "KhoaHoc",
                "BaoCaoI",
                "NguoiLX",
                "NguoiLX_HoSo",
                "NguoiLXHS_GiayTo",
            ],
            CsdtAtomicCoreDomains.ApplyOrder);
    }

    [Fact]
    public void Atomic_scope_rejects_an_optional_domain()
    {
        Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtAtomicCoreDomains.RequireExactScope(
                [.. CsdtAtomicCoreDomains.ApplyOrder, "GiaoVien"]));
    }

    [Fact]
    public void Atomic_feature_flag_defaults_false()
    {
        Assert.False(new CsdtRealtimeSyncOptions().UseAtomicMappedTableCycle);
    }

    [Fact]
    public async Task Feature_false_leaves_every_atomic_boundary_unused()
    {
        var fixture = Fixture(enabled: false);

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Failed, result.Outcome);
        Assert.Equal(CsdtAtomicCycleErrorCodes.FeatureDisabled, result.ErrorCode);
        Assert.Equal(0, fixture.Source.PreflightCount);
        Assert.Equal(0, fixture.Target.ApplyCount);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
    }

    [Fact]
    public void Production_processor_does_not_select_the_atomic_coordinator()
    {
        var processor = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtRealtimeStreamProcessor.cs");

        Assert.DoesNotContain(
            "CsdtAtomicMappedTableCycleCoordinator",
            processor,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UseAtomicMappedTableCycle",
            processor,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CsdtSourceCapabilityStatus.SnapshotNotEnabled, "SNAPSHOT_NOT_ENABLED")]
    [InlineData(CsdtSourceCapabilityStatus.CtNotEnabled, "CT_NOT_ENABLED")]
    [InlineData(CsdtSourceCapabilityStatus.CtTableNotTracked, "CT_TABLE_NOT_TRACKED")]
    [InlineData(CsdtSourceCapabilityStatus.CtCheckpointExpired, "CT_CHECKPOINT_EXPIRED")]
    [InlineData(CsdtSourceCapabilityStatus.SchemaMismatch, "SCHEMA_MISMATCH")]
    [InlineData(CsdtSourceCapabilityStatus.ProfileMismatch, "PROFILE_MISMATCH")]
    public async Task Source_preflight_failure_is_before_target_and_checkpoint(
        CsdtSourceCapabilityStatus status,
        string errorCode)
    {
        var fixture = Fixture();
        fixture.Source.Status = status;

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(0, fixture.Source.OpenCount);
        Assert.Equal(0, fixture.Journal.CreateCount);
        Assert.Equal(0, fixture.Target.ApplyCount);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
    }

    [Fact]
    public async Task One_snapshot_supplies_one_watermark_for_the_whole_cycle()
    {
        var fixture = Fixture();

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Complete, result.Outcome);
        Assert.Equal(1, fixture.Source.OpenCount);
        Assert.Equal(1, fixture.Source.Snapshot.StageCount);
        Assert.Equal(100, fixture.Journal.CreatedWatermark);
        Assert.Equal(100, result.PublishedWatermark);
        Assert.All(
            fixture.Source.Snapshot.Stage.Domains,
            domain => Assert.Same(
                fixture.Source.Snapshot.Stage,
                fixture.Source.Snapshot.Stage));
    }

    [Fact]
    public void Staged_rows_defensively_copy_keys_values_and_binary_payloads()
    {
        var key = new byte[] { 1, 2, 3 };
        var payload = new byte[] { 4, 5, 6 };
        var values = new Dictionary<string, object?>
        {
            ["MaDK"] = "66029-sensitive",
            ["Anh"] = payload,
        };
        var row = new CsdtStagedRow(key, values);
        key[0] = 9;
        payload[0] = 9;
        ((byte[])values["Anh"]!)[1] = 9;
        var copied = row.CopyValues();
        ((byte[])copied["Anh"]!)[2] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, row.CopyCanonicalKey());
        Assert.Equal(new byte[] { 4, 5, 6 }, row.ReadValue("Anh"));
    }

    [Fact]
    public void Stage_hashes_are_deterministic_under_input_reordering()
    {
        var first = Row("A", ("MaDV", "66029"), ("TrangThai", true));
        var second = Row("B", ("MaDV", "66030"), ("TrangThai", false));

        var left = CsdtAtomicStageFactory.CreateDomain(
            "DM_DonViGTVT",
            CsdtAtomicOperationMode.FullSnapshot,
            [first, second]);
        var right = CsdtAtomicStageFactory.CreateDomain(
            "DM_DonViGTVT",
            CsdtAtomicOperationMode.FullSnapshot,
            [second, first]);

        Assert.Equal(left.SourceKeySetHash, right.SourceKeySetHash);
        Assert.Equal(left.StageResultHash, right.StageResultHash);
    }

    [Fact]
    public void Sql_source_uses_one_snapshot_transaction_and_no_read_committed_fallback()
    {
        var source = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtSqlSourceCycleReader.cs");

        Assert.Contains("IsolationLevel.Snapshot", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "BeginTransactionAsync("));
        Assert.DoesNotContain("IsolationLevel.ReadCommitted", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER DATABASE", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_source_column_fails_before_target_transaction()
    {
        var fixture = Fixture();
        fixture.Source.Snapshot.Stage = ReplaceDomain(
            fixture.Source.Snapshot.Stage,
            "KhoaHoc",
            domain => CsdtAtomicStageFactory.CreateDomain(
                domain.DomainName,
                domain.OperationMode,
                domain.Rows,
                domain.Changes,
                domain.CompleteKeys,
                ["UnexpectedColumn"]));

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleErrorCodes.UnknownColumn, result.ErrorCode);
        Assert.Equal(0, fixture.Target.ApplyCount);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
    }

    [Fact]
    public async Task Target_collation_alias_in_stage_fails_before_target()
    {
        var fixture = Fixture();
        fixture.Source.Snapshot.Stage = ReplaceDomain(
            fixture.Source.Snapshot.Stage,
            "NguoiLX",
            domain => CsdtAtomicStageFactory.CreateDomain(
                domain.DomainName,
                domain.OperationMode,
                [
                    .. domain.Rows,
                    Row(
                        "learner-alias",
                        ("MaDK", "N1   "),
                        ("DonViNhanHSo", "66029")),
                ]));

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(0, fixture.Target.ApplyCount);
    }

    [Fact]
    public async Task Missing_parent_fails_the_whole_cycle()
    {
        var fixture = Fixture();
        fixture.Source.Snapshot.Stage = ReplaceDomain(
            fixture.Source.Snapshot.Stage,
            "BaoCaoI",
            domain => CsdtAtomicStageFactory.CreateDomain(
                domain.DomainName,
                domain.OperationMode,
                [
                    Row(
                        "report",
                        ("MaBCI", "B1"),
                        ("MaCSDT", "66029"),
                        ("MaKH", "MISSING")),
                ]));

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleErrorCodes.ParentMissing, result.ErrorCode);
        Assert.Equal(0, fixture.Target.ApplyCount);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("08")]
    [InlineData("15")]
    [InlineData("XX")]
    public async Task Unknown_dossier_state_fails_closed(string? state)
    {
        var fixture = Fixture();
        fixture.Source.Snapshot.Stage = ReplaceDomain(
            fixture.Source.Snapshot.Stage,
            "NguoiLX_HoSo",
            domain => CsdtAtomicStageFactory.CreateDomain(
                domain.DomainName,
                domain.OperationMode,
                [
                    Row(
                        "dossier",
                        ("MaDK", "N1"),
                        ("MaCSDT", "66029"),
                        ("MaKhoaHoc", "K1"),
                        ("MaBC1", "B1"),
                        ("TT_XuLy", state)),
                ]));

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleErrorCodes.InvalidTrainingState, result.ErrorCode);
        Assert.Equal(0, fixture.Target.ApplyCount);
    }

    [Fact]
    public async Task Staged_delete_fails_without_target_or_checkpoint()
    {
        var fixture = Fixture(operationMode: CsdtAtomicOperationMode.Incremental);
        fixture.Source.Snapshot.Stage = ReplaceDomain(
            fixture.Source.Snapshot.Stage,
            "NguoiLX",
            domain => CsdtAtomicStageFactory.CreateDomain(
                domain.DomainName,
                domain.OperationMode,
                domain.Rows,
                [
                    new CsdtStagedChange(
                        100,
                        CsdtStagedChangeOperation.Delete,
                        Key("deleted-learner")),
                ],
                domain.CompleteKeys));

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(
            CsdtAtomicCycleErrorCodes.DeleteExecutionNotEnabled,
            result.ErrorCode);
        Assert.Equal(0, fixture.Target.ApplyCount);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
        Assert.Equal(SyncCycleStatus.Failed, fixture.Journal.State);
    }

    [Fact]
    public async Task All_six_domains_success_produce_one_target_commit()
    {
        var fixture = Fixture();

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Complete, result.Outcome);
        Assert.Equal(1, fixture.Target.ApplyCount);
        Assert.Equal(1, fixture.Target.CommitCount);
        Assert.Equal(0, fixture.Target.RollbackCount);
    }

    [Fact]
    public async Task Target_domain_order_is_parent_before_child()
    {
        var fixture = Fixture();

        await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCoreDomains.ApplyOrder, fixture.Target.DomainOrder);
    }

    [Fact]
    public async Task Atomic_path_opens_one_logical_target_transaction()
    {
        var fixture = Fixture();

        await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(1, fixture.Target.TransactionCount);
        Assert.Equal(6, fixture.Target.DomainOrder.Count);
    }

    [Fact]
    public async Task Checkpoint_publish_happens_after_target_commit()
    {
        var events = new List<string>();
        var fixture = Fixture(events: events);

        await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.True(events.IndexOf("target-commit") < events.IndexOf("checkpoint-publish"));
        Assert.True(events.IndexOf("checkpoint-publish") < events.IndexOf("cycle-complete"));
    }

    [Fact]
    public async Task Global_checkpoint_contains_exactly_the_cycle_watermark()
    {
        var fixture = Fixture();

        await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(1, fixture.Checkpoint.PublishCount);
        Assert.Equal(100, fixture.Checkpoint.Value!.SourceWatermark);
        Assert.Equal(fixture.Request.CycleId, fixture.Checkpoint.Value.CycleId);
    }

    [Fact]
    public async Task Durable_cycle_flow_is_strict_and_complete()
    {
        var events = new List<string>();
        var fixture = Fixture(events: events);

        await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(
            [
                "preflight",
                "snapshot-open",
                "cycle-preparing",
                "source-stage",
                "cycle-staged",
                "cycle-validated",
                "target-committing",
                "target-commit",
                "checkpoint-publish",
                "checkpoint-published",
                "cycle-complete",
            ],
            events);
    }

    [Fact]
    public void State_machine_forbids_validation_before_staging()
    {
        Assert.False(SyncCycleStateMachine.CanTransition(
            SyncCycleStatus.Preparing,
            SyncCycleStatus.Validated));
        Assert.Throws<InvalidOperationException>(() =>
            SyncCycleStateMachine.Transition(
                SyncCycleStatus.Preparing,
                SyncCycleStatus.Validated));
    }

    [Fact]
    public void Marker_with_wrong_staged_hash_is_rejected()
    {
        var fixture = Fixture();
        var marker = Marker(fixture.Source.Snapshot.Stage) with
        {
            StagedKeySetHash = Fingerprint("wrong"),
        };

        var exception = Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtAtomicMappedTableCycleCoordinator.VerifyMarker(
                fixture.Source.Snapshot.Stage,
                marker));

        Assert.Equal(
            CsdtAtomicCycleErrorCodes.TargetCommitNotVerified,
            exception.ErrorCode);
    }

    [Fact]
    public void Reread_marker_with_changed_domain_result_hash_is_rejected()
    {
        var fixture = Fixture();
        var committed = Marker(fixture.Source.Snapshot.Stage);
        var changed = committed with
        {
            Domains =
            [
                committed.Domains[0] with { ResultHash = Fingerprint("changed") },
                .. committed.Domains.Skip(1),
            ],
        };

        var exception = Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtAtomicMappedTableCycleCoordinator.VerifyCommittedReplay(
                committed,
                changed));

        Assert.Equal(
            CsdtAtomicCycleErrorCodes.DomainResultHashMismatch,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Target_failure_rolls_back_and_never_publishes_checkpoint()
    {
        var fixture = Fixture();
        fixture.Target.FailAtDomain = "BaoCaoI";

        var result = await fixture.Coordinator.ExecuteAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Failed, result.Outcome);
        Assert.Equal(0, fixture.Target.CommitCount);
        Assert.Equal(1, fixture.Target.RollbackCount);
        Assert.Empty(fixture.Target.CommittedBusinessWrites);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
    }

    [Fact]
    public async Task Recovery_after_target_commit_publishes_without_replay()
    {
        var fixture = Fixture();
        fixture.Journal.Marker = Marker(fixture.Source.Snapshot.Stage);
        fixture.Journal.State = SyncCycleStatus.TargetCommitted;

        var result = await fixture.Coordinator.RecoverAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Complete, result.Outcome);
        Assert.Equal(0, fixture.Target.ApplyCount);
        Assert.Equal(1, fixture.Checkpoint.PublishCount);
    }

    [Fact]
    public async Task Recovery_after_checkpoint_marks_complete_idempotently()
    {
        var fixture = Fixture();
        var marker = Marker(fixture.Source.Snapshot.Stage) with
        {
            Status = SyncCycleStatus.CheckpointPublished,
        };
        fixture.Journal.Marker = marker;
        fixture.Journal.State = marker.Status;
        fixture.Checkpoint.Value = Checkpoint(marker);

        var result = await fixture.Coordinator.RecoverAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Complete, result.Outcome);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
        Assert.Equal(SyncCycleStatus.Complete, fixture.Journal.State);
    }

    [Fact]
    public async Task Recovery_of_complete_cycle_is_a_noop_after_verification()
    {
        var fixture = Fixture();
        var marker = Marker(fixture.Source.Snapshot.Stage) with
        {
            Status = SyncCycleStatus.Complete,
        };
        fixture.Journal.Marker = marker;
        fixture.Journal.State = marker.Status;
        fixture.Checkpoint.Value = Checkpoint(marker);

        var first = await fixture.Coordinator.RecoverAsync(fixture.Request);
        var second = await fixture.Coordinator.RecoverAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Complete, first.Outcome);
        Assert.Equal(CsdtAtomicCycleOutcome.Complete, second.Outcome);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
        Assert.Equal(0, fixture.Target.ApplyCount);
    }

    [Fact]
    public async Task Recovery_before_target_requires_rebuild_without_checkpoint()
    {
        var fixture = Fixture();
        fixture.Journal.Marker = Marker(
            fixture.Source.Snapshot.Stage,
            SyncCycleStatus.Staged,
            includeDomains: false);
        fixture.Journal.State = SyncCycleStatus.Staged;

        var result = await fixture.Coordinator.RecoverAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.RebuildRequired, result.Outcome);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
        Assert.Equal(0, fixture.Target.ApplyCount);
    }

    [Fact]
    public async Task Durable_stage_can_resume_the_same_cycle_and_watermark_after_rollback()
    {
        var fixture = Fixture();
        fixture.Journal.Marker = Marker(
            fixture.Source.Snapshot.Stage,
            SyncCycleStatus.Validated,
            includeDomains: false);
        fixture.Journal.State = SyncCycleStatus.Validated;

        var result = await fixture.Coordinator.ResumeStagedAsync(
            fixture.Request,
            fixture.Source.Snapshot.Stage);

        Assert.Equal(CsdtAtomicCycleOutcome.Complete, result.Outcome);
        Assert.Equal(0, fixture.Source.PreflightCount);
        Assert.Equal(1, fixture.Target.ApplyCount);
        Assert.Equal(100, result.PublishedWatermark);
    }

    [Fact]
    public async Task Recovery_with_fingerprint_drift_marks_conflict()
    {
        var fixture = Fixture();
        fixture.Journal.Marker = Marker(fixture.Source.Snapshot.Stage) with
        {
            MappingFingerprint = Fingerprint("drift"),
        };
        fixture.Journal.State = SyncCycleStatus.TargetCommitted;

        var result = await fixture.Coordinator.RecoverAsync(fixture.Request);

        Assert.Equal(CsdtAtomicCycleOutcome.Conflict, result.Outcome);
        Assert.Equal(0, fixture.Checkpoint.PublishCount);
    }

    [Fact]
    public async Task Recovery_rejects_an_external_checkpoint_mismatch()
    {
        var fixture = Fixture();
        var marker = Marker(fixture.Source.Snapshot.Stage) with
        {
            Status = SyncCycleStatus.CheckpointPublished,
        };
        fixture.Journal.Marker = marker;
        fixture.Journal.State = marker.Status;
        fixture.Checkpoint.Value = Checkpoint(marker) with
        {
            SourceWatermark = marker.EndSourceVersion - 1,
        };

        var exception = await Assert.ThrowsAsync<CsdtAtomicCycleException>(() =>
            fixture.Coordinator.RecoverAsync(fixture.Request));

        Assert.Equal(CsdtAtomicCycleErrorCodes.CheckpointMismatch, exception.ErrorCode);
    }

    [Fact]
    public void Raw_learner_key_is_redacted_from_stage_diagnostics()
    {
        const string raw = "66029-sensitive-learner";
        var row = Row("learner", ("MaDK", raw));
        var change = new CsdtStagedChange(
            1,
            CsdtStagedChangeOperation.Delete,
            Key(raw));

        Assert.DoesNotContain(raw, row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(raw, change.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Oto_and_moto_use_distinct_target_lock_resources()
    {
        var oto = CsdtAtomicTargetLockName.Build(
            "OTO_V1", "OTO_V2", "OTO_V2_TO_V1");
        var moto = CsdtAtomicTargetLockName.Build(
            "MOTO_V1", "MOTO_V2", "MOTO_V2_TO_V1");

        Assert.NotEqual(oto, moto);
    }

    [Fact]
    public void Live_and_backup_use_distinct_target_lock_resources()
    {
        var live = CsdtAtomicTargetLockName.Build(
            "OTO_V1", "OTO_V2", "OTO_V2_TO_V1");
        var backup = CsdtAtomicTargetLockName.Build(
            "OTO_V1_BAK", "OTO_V2_BAK", "OTO_V2_TO_V1");

        Assert.NotEqual(live, backup);
    }

    [Fact]
    public void Target_lock_resource_contains_only_profile_and_stream_scope()
    {
        const string raw = "66029-sensitive-learner";
        var resource = CsdtAtomicTargetLockName.Build(
            "OTO_V1", "OTO_V2", "OTO_V2_TO_V1");

        Assert.StartsWith(CsdtAtomicTargetLockName.Prefix, resource);
        Assert.DoesNotContain(raw, resource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BaoCaoII")]
    [InlineData("dbo.NguoiLX;DROP TABLE x")]
    public void Arbitrary_atomic_domain_identifier_is_rejected(string domain)
    {
        Assert.Throws<CsdtAtomicCycleException>(() =>
            CsdtAtomicCoreDomains.RequireExactScope(
                CsdtAtomicCoreDomains.ApplyOrder
                    .Take(5)
                    .Append(domain)));
    }

    [Fact]
    public void Source_change_queries_are_bounded_by_the_cycle_watermark()
    {
        var reader = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtRealtimeSourceReader.cs");
        var atomic = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtSqlSourceCycleReader.cs");

        Assert.Contains(
            "SYS_CHANGE_VERSION <= @ThroughVersion",
            reader,
            StringComparison.Ordinal);
        Assert.Contains(
            "_request.StartSourceVersion,\n                Watermark,",
            Normalize(atomic),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Atomic_target_path_contains_no_business_delete_operation()
    {
        var applier = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "ControlPlane", "CsdtTargetCycleApplier.cs");

        Assert.DoesNotContain("DELETE FROM dbo.", applier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApplyInactiveAsync", applier, StringComparison.Ordinal);
        Assert.DoesNotContain("HardDeleted", applier, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_writer_and_control_plane_accept_the_caller_transaction()
    {
        var writer = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtRealtimeTargetWriter.cs");
        var repository = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "ControlPlane", "CsdtRealtimeTargetControlPlaneRepository.cs");

        Assert.Contains("SqlTransaction transaction", writer, StringComparison.Ordinal);
        Assert.Contains("DbTransaction transaction", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransaction", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("CommitAsync(", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void All_control_plane_contracts_allowlist_delete_disabled_and_lock_timeout()
    {
        foreach (var file in new[]
                 {
                     "20260726_add_csdt_control_plane_oto_v1.sql",
                     "20260726_add_csdt_control_plane_moto_v1.sql",
                     "20260726_add_csdt_control_plane_oto_v1_bak.sql",
                     "20260726_add_csdt_control_plane_moto_v1_bak.sql",
                 })
        {
            var patch = ReadWorkspaceFile("database", "patches", file);
            Assert.Contains(
                "DELETE_EXECUTION_NOT_ENABLED",
                patch,
                StringComparison.Ordinal);
            Assert.Contains("TARGET_LOCK_TIMEOUT", patch, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Protected_development_configuration_hash_contract_is_unchanged()
    {
        foreach (var path in new[]
                 {
                     ("server", "QLHV.Api", "appsettings.Development.json"),
                     ("server", "QLHV.Worker", "appsettings.Development.json"),
                 })
        {
            var bytes = File.ReadAllBytes(FindWorkspaceFile(path.Item1, path.Item2, path.Item3));
            Assert.Equal(
                "12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E",
                Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }

    [Fact]
    public void Approved_dossier_state_sets_include_full_training_and_downstream_contract()
    {
        foreach (var state in new[]
                 {
                     "01", "02", "03", "04", "05", "06", "07", "09", "10",
                     "00", "11", "12", "13", "14", "16", "17", "18", "19", "90",
                 })
        {
            Assert.True(
                CsdtRealtimeForwardWritePlanner.IsAtomicTrainingState(state) ||
                CsdtRealtimeForwardWritePlanner.IsAtomicKnownDownstreamState(state));
        }

        Assert.False(CsdtRealtimeForwardWritePlanner.IsAtomicTrainingState("08"));
        Assert.False(CsdtRealtimeForwardWritePlanner.IsAtomicKnownDownstreamState("15"));
    }

    [Fact]
    public void Dossier_v2_fields_remain_direct_and_v1_fields_are_never_writable()
    {
        var table = new System.Data.DataTable();
        foreach (var name in new[]
                 {
                     "MaDK", "TT_XuLy", "TrangThai", "MaKhoaHoc", "MaBC1",
                     "GhiChu", "MaBC2",
                 })
        {
            table.Columns.Add(
                name,
                name == "TrangThai" ? typeof(bool) : typeof(string));
        }

        var source = table.NewRow();
        source["MaDK"] = "N1";
        source["TT_XuLy"] = "09";
        source["TrangThai"] = false;
        source["MaKhoaHoc"] = "KH-NEW";
        source["MaBC1"] = "BCI-NEW";
        source["GhiChu"] = "source";
        table.Rows.Add(source);
        var targetTable = table.Clone();
        var target = targetTable.NewRow();
        target["MaDK"] = "N1";
        target["TT_XuLy"] = "17";
        target["TrangThai"] = true;
        target["MaKhoaHoc"] = "KH-V1";
        target["MaBC1"] = "BCI-V1";
        target["GhiChu"] = "v1";
        target["MaBC2"] = "BCII";
        targetTable.Rows.Add(target);
        var domain = CsdtRealtimeDomainCatalog.GetRequired("NguoiLX_HoSo");
        var plan = CsdtRealtimeForwardWritePlanner.PlanRow(
            domain,
            source,
            target,
            relationshipLocked: true,
            parentExists: true,
            useAtomicMappedTableContract: true);
        var update = CsdtRealtimeForwardWritePlanner.ProjectUpdateValues(
            domain,
            plan.Row);

        Assert.Equal(false, update["TrangThai"]);
        Assert.Equal("KH-NEW", update["MaKhoaHoc"]);
        Assert.Equal("BCI-NEW", update["MaBC1"]);
        Assert.Equal("17", update["TT_XuLy"]);
        Assert.Equal("v1", update["GhiChu"]);

        var policy = CsdtRealtimeColumnOwnershipPolicy.GetRequired("NguoiLX_HoSo");

        foreach (var name in new[] { "MaBC2", "MaKySH", "SoBD", "TT_XuLy_Old" })
        {
            var rule = policy.GetRequired(name);
            Assert.Equal(CsdtRealtimeColumnOwner.V1, rule.Owner);
            Assert.False(rule.AllowInsert);
            Assert.False(rule.AllowUpdate);
        }
    }

    [Fact]
    public void Sql_stage_reads_exactly_the_six_core_domains()
    {
        var source = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtSqlSourceCycleReader.cs");

        Assert.Contains(
            "foreach (var name in CsdtAtomicCoreDomains.ApplyOrder)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"GiaoVien\"", Between(
            source,
            "StageCoreAsync(",
            "public async ValueTask DisposeAsync()"), StringComparison.Ordinal);
        Assert.DoesNotContain("\"KhoaHoc_GiaoVien\"", Between(
            source,
            "StageCoreAsync(",
            "public async ValueTask DisposeAsync()"), StringComparison.Ordinal);
    }

    [Fact]
    public void Full_snapshot_never_uses_a_target_global_anti_join()
    {
        var source = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "Realtime",
            "CsdtSqlSourceCycleReader.cs");

        Assert.DoesNotContain("NOT EXISTS", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TargetConnectionString", Between(
            source,
            "StageCoreAsync(",
            "public async ValueTask DisposeAsync()"), StringComparison.Ordinal);
    }

    private static TestFixture Fixture(
        bool enabled = true,
        CsdtAtomicOperationMode operationMode = CsdtAtomicOperationMode.FullSnapshot,
        List<string>? events = null)
    {
        var request = Request(operationMode);
        var stage = ValidStage(request);
        var source = new FakeSource(stage, events);
        var journal = new FakeJournal(events);
        var target = new FakeTarget(journal, events);
        var checkpoint = new FakeCheckpoint(events);
        var coordinator = new CsdtAtomicMappedTableCycleCoordinator(
            source,
            journal,
            target,
            checkpoint,
            new CsdtAtomicMappedTableCycleValidator(),
            Options.Create(new CsdtRealtimeSyncOptions
            {
                UseAtomicMappedTableCycle = enabled,
            }));
        return new TestFixture(
            request,
            source,
            journal,
            target,
            checkpoint,
            coordinator);
    }

    private static CsdtAtomicCycleRequest Request(CsdtAtomicOperationMode mode)
        => CsdtAtomicCycleRequest.ForCore(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            CsdtRealtimeStreamCatalog.LiveRoutes[0],
            90,
            mode,
            Fingerprint("mapping"),
            CsdtAtomicRouteFingerprint.Compute(
                CsdtRealtimeStreamCatalog.LiveRoutes[0]),
            Fingerprint("source-schema"),
            Fingerprint("target-schema"));

    private static CsdtStagedCycle ValidStage(CsdtAtomicCycleRequest request)
    {
        var domains = new[]
        {
            CsdtAtomicStageFactory.CreateDomain(
                "DM_DonViGTVT",
                request.OperationMode,
                [Row("unit", ("MaDV", "66029"))]),
            CsdtAtomicStageFactory.CreateDomain(
                "KhoaHoc",
                request.OperationMode,
                [
                    Row(
                        "course",
                        ("MaKH", "K1"),
                        ("MaCSDT", "66029")),
                ]),
            CsdtAtomicStageFactory.CreateDomain(
                "BaoCaoI",
                request.OperationMode,
                [
                    Row(
                        "report",
                        ("MaBCI", "B1"),
                        ("MaCSDT", "66029"),
                        ("MaKH", "K1")),
                ]),
            CsdtAtomicStageFactory.CreateDomain(
                "NguoiLX",
                request.OperationMode,
                [
                    Row(
                        "learner",
                        ("MaDK", "N1"),
                        ("DonViNhanHSo", "66029")),
                ]),
            CsdtAtomicStageFactory.CreateDomain(
                "NguoiLX_HoSo",
                request.OperationMode,
                [
                    Row(
                        "dossier",
                        ("MaDK", "N1"),
                        ("MaCSDT", "66029"),
                        ("MaKhoaHoc", "K1"),
                        ("MaBC1", "B1"),
                        ("TT_XuLy", "03")),
                ]),
            CsdtAtomicStageFactory.CreateDomain(
                "NguoiLXHS_GiayTo",
                request.OperationMode,
                [
                    Row(
                        "document",
                        ("MaGT", 1),
                        ("MaDK", "N1")),
                ]),
        };
        return new CsdtStagedCycle(
            request.CycleId,
            request.Route.SourceProfileCode,
            request.Route.TargetProfileCode,
            request.Route.StreamCode,
            request.Route.MaCSDT,
            request.StartSourceVersion,
            100,
            request.MappingFingerprint,
            request.RouteFingerprint,
            request.SourceSchemaFingerprint,
            request.TargetSchemaFingerprint,
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            1,
            TargetEqualityProof.ProofId,
            domains,
            CsdtAtomicStageFactory.ComputeCycleKeySetHash(domains));
    }

    private static CsdtStagedCycle ReplaceDomain(
        CsdtStagedCycle cycle,
        string name,
        Func<CsdtStagedDomain, CsdtStagedDomain> replace)
    {
        var domains = cycle.Domains
            .Select(domain =>
                string.Equals(domain.DomainName, name, StringComparison.Ordinal)
                    ? replace(domain)
                    : domain)
            .ToArray();
        return new CsdtStagedCycle(
            cycle.CycleId,
            cycle.SourceProfile,
            cycle.TargetProfile,
            cycle.StreamCode,
            cycle.MaCsdt,
            cycle.StartSourceVersion,
            cycle.EndSourceVersion,
            cycle.MappingFingerprint,
            cycle.RouteFingerprint,
            cycle.SourceSchemaFingerprint,
            cycle.TargetSchemaFingerprint,
            cycle.StageCreatedAtUtc,
            cycle.KeySchemaVersion,
            cycle.TargetEqualityProofId,
            domains,
            CsdtAtomicStageFactory.ComputeCycleKeySetHash(domains));
    }

    private static CsdtStagedRow Row(
        string keySeed,
        params (string Name, object? Value)[] values)
        => new(
            Key(keySeed),
            values.ToDictionary(
                item => item.Name,
                item => item.Value,
                StringComparer.Ordinal));

    private static byte[] Key(string value)
        => CanonicalBusinessKeyEncoder.Encode(
            1,
            CanonicalKeyComponent.FromString(value)).ToArray();

    private static ControlPlaneFingerprint Fingerprint(string value)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static CsdtTargetCycleCommitMarker Marker(
        CsdtStagedCycle stage,
        SyncCycleStatus status = SyncCycleStatus.TargetCommitted,
        bool includeDomains = true)
        => new(
            stage.CycleId,
            stage.SourceProfile,
            stage.TargetProfile,
            stage.StreamCode,
            stage.MaCsdt,
            stage.StartSourceVersion,
            stage.EndSourceVersion,
            status,
            CsdtAtomicCoreDomains.ApplyOrder.Count,
            stage.MappingFingerprint,
            stage.RouteFingerprint,
            stage.StagedKeySetHash,
            includeDomains
                ? stage.Domains.Select(domain =>
                    new CsdtAtomicDomainCommitResult(
                        domain.DomainName,
                        domain.SourceRowCount,
                        1,
                        0,
                        0,
                        domain.SourceKeySetHash,
                        domain.StageResultHash)).ToArray()
                : [],
            stage.SourceSchemaFingerprint,
            stage.TargetSchemaFingerprint);

    private static CsdtGlobalCheckpoint Checkpoint(
        CsdtTargetCycleCommitMarker marker)
        => new(
            marker.CycleId,
            marker.SourceProfile,
            marker.TargetProfile,
            marker.StreamCode,
            marker.EndSourceVersion,
            marker.MappingFingerprint,
            marker.RouteFingerprint,
            marker.StagedKeySetHash!,
            marker.SourceSchemaFingerprint,
            marker.TargetSchemaFingerprint);

    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int Count(string source, string value)
        => (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) /
           value.Length;

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string ReadWorkspaceFile(params string[] parts)
        => File.ReadAllText(FindWorkspaceFile(parts));

    private static string FindWorkspaceFile(
        string first,
        string second,
        string third)
        => FindWorkspaceFile([first, second, third]);

    private static string FindWorkspaceFile(
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

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(parts));
    }

    private sealed record TestFixture(
        CsdtAtomicCycleRequest Request,
        FakeSource Source,
        FakeJournal Journal,
        FakeTarget Target,
        FakeCheckpoint Checkpoint,
        CsdtAtomicMappedTableCycleCoordinator Coordinator);

    private sealed class FakeSource : ICsdtSourceCycleReader
    {
        private readonly List<string>? _events;

        internal FakeSource(CsdtStagedCycle stage, List<string>? events)
        {
            Snapshot = new FakeSnapshot(stage, events);
            _events = events;
        }

        internal CsdtSourceCapabilityStatus Status { get; set; } =
            CsdtSourceCapabilityStatus.Ready;

        internal int PreflightCount { get; private set; }

        internal int OpenCount { get; private set; }

        internal FakeSnapshot Snapshot { get; }

        public Task<CsdtSourceCapabilityResult> PreflightAsync(
            CsdtAtomicCycleRequest request,
            CancellationToken cancellationToken = default)
        {
            PreflightCount++;
            _events?.Add("preflight");
            return Task.FromResult(new CsdtSourceCapabilityResult(
                Status,
                100,
                CsdtAtomicCoreDomains.ApplyOrder.ToDictionary(
                    name => name,
                    _ => 1L,
                    StringComparer.Ordinal),
                request.SourceSchemaFingerprint));
        }

        public Task<ICsdtMappedTableSourceSnapshot> OpenSnapshotAsync(
            CsdtAtomicCycleRequest request,
            CsdtSourceCapabilityResult preflight,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            _events?.Add("snapshot-open");
            return Task.FromResult<ICsdtMappedTableSourceSnapshot>(Snapshot);
        }
    }

    private sealed class FakeSnapshot : ICsdtMappedTableSourceSnapshot
    {
        private readonly List<string>? _events;

        internal FakeSnapshot(CsdtStagedCycle stage, List<string>? events)
        {
            Stage = stage;
            _events = events;
        }

        public long Watermark => Stage.EndSourceVersion;

        internal CsdtStagedCycle Stage { get; set; }

        internal int StageCount { get; private set; }

        public Task<CsdtStagedCycle> StageCoreAsync(
            CancellationToken cancellationToken = default)
        {
            StageCount++;
            _events?.Add("source-stage");
            return Task.FromResult(Stage);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeJournal : ICsdtAtomicCycleJournal
    {
        private readonly List<string>? _events;

        internal FakeJournal(List<string>? events)
        {
            _events = events;
        }

        internal int CreateCount { get; private set; }

        internal long? CreatedWatermark { get; private set; }

        internal SyncCycleStatus? State { get; set; }

        internal CsdtTargetCycleCommitMarker? Marker { get; set; }

        public Task CreatePreparingAsync(
            CsdtAtomicCycleRequest request,
            long watermark,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            CreatedWatermark = watermark;
            State = SyncCycleStatus.Preparing;
            _events?.Add("cycle-preparing");
            Marker = new CsdtTargetCycleCommitMarker(
                request.CycleId,
                request.Route.SourceProfileCode,
                request.Route.TargetProfileCode,
                request.Route.StreamCode,
                request.Route.MaCSDT,
                request.StartSourceVersion,
                watermark,
                SyncCycleStatus.Preparing,
                6,
                request.MappingFingerprint,
                request.RouteFingerprint,
                null,
                [],
                request.SourceSchemaFingerprint,
                request.TargetSchemaFingerprint);
            return Task.CompletedTask;
        }

        public Task MarkStagedAsync(
            CsdtStagedCycle stagedCycle,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(SyncCycleStatus.Preparing, State);
            State = SyncCycleStatus.Staged;
            Marker = Marker! with
            {
                Status = State.Value,
                StagedKeySetHash = stagedCycle.StagedKeySetHash,
            };
            _events?.Add("cycle-staged");
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(
            Guid cycleId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(SyncCycleStatus.Staged, State);
            State = SyncCycleStatus.Validated;
            Marker = Marker! with { Status = State.Value };
            _events?.Add("cycle-validated");
            return Task.CompletedTask;
        }

        public Task MarkFailedOrConflictAsync(
            Guid cycleId,
            SyncCycleStatus status,
            string errorCode,
            CancellationToken cancellationToken = default)
        {
            State = status;
            if (Marker is not null)
            {
                Marker = Marker with { Status = status };
            }

            return Task.CompletedTask;
        }

        public Task<CsdtTargetCycleCommitMarker?> ReadMarkerAsync(
            Guid cycleId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Marker);

        public Task MarkCheckpointPublishedAsync(
            Guid cycleId,
            CancellationToken cancellationToken = default)
        {
            State = SyncCycleStatus.CheckpointPublished;
            Marker = Marker! with { Status = State.Value };
            _events?.Add("checkpoint-published");
            return Task.CompletedTask;
        }

        public Task MarkCompleteAsync(
            Guid cycleId,
            CancellationToken cancellationToken = default)
        {
            State = SyncCycleStatus.Complete;
            Marker = Marker! with { Status = State.Value };
            _events?.Add("cycle-complete");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTarget : ICsdtTargetCycleApplier
    {
        private readonly FakeJournal _journal;
        private readonly List<string>? _events;

        internal FakeTarget(FakeJournal journal, List<string>? events)
        {
            _journal = journal;
            _events = events;
        }

        internal int ApplyCount { get; private set; }

        internal int TransactionCount { get; private set; }

        internal int CommitCount { get; private set; }

        internal int RollbackCount { get; private set; }

        internal string? FailAtDomain { get; set; }

        internal List<string> DomainOrder { get; } = [];

        internal List<string> CommittedBusinessWrites { get; } = [];

        public Task<CsdtTargetCycleCommitMarker> ApplyAsync(
            CsdtStagedCycle stagedCycle,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            TransactionCount++;
            _events?.Add("target-committing");
            var pending = new List<string>();
            foreach (var domain in stagedCycle.Domains)
            {
                DomainOrder.Add(domain.DomainName);
                pending.Add(domain.DomainName);
                if (string.Equals(
                        domain.DomainName,
                        FailAtDomain,
                        StringComparison.Ordinal))
                {
                    RollbackCount++;
                    throw new CsdtAtomicCycleException(
                        CsdtAtomicCycleErrorCodes.ValidationFailed);
                }
            }

            CommitCount++;
            CommittedBusinessWrites.AddRange(pending);
            _events?.Add("target-commit");
            var marker = AtomicMappedTableCycleTests.Marker(stagedCycle);
            _journal.Marker = marker;
            _journal.State = SyncCycleStatus.TargetCommitted;
            return Task.FromResult(marker);
        }
    }

    private sealed class FakeCheckpoint : ICsdtGlobalCheckpointStore
    {
        private readonly List<string>? _events;

        internal FakeCheckpoint(List<string>? events)
        {
            _events = events;
        }

        internal int PublishCount { get; private set; }

        internal CsdtGlobalCheckpoint? Value { get; set; }

        public Task PublishAsync(
            CsdtGlobalCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
        {
            if (Value is not null && Value != checkpoint)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.CheckpointMismatch);
            }

            PublishCount++;
            Value = checkpoint;
            _events?.Add("checkpoint-publish");
            return Task.CompletedTask;
        }

        public Task<CsdtGlobalCheckpoint?> ReadAsync(
            string sourceProfile,
            string targetProfile,
            string streamCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Value);

        public Task<bool> VerifyAsync(
            CsdtGlobalCheckpoint checkpoint,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Value == checkpoint);

        public Task MarkConflictAsync(
            string sourceProfile,
            string targetProfile,
            string streamCode,
            CancellationToken cancellationToken = default)
        {
            if (Value is not null)
            {
                Value = Value with { Status = CsdtCheckpointStatus.Conflict };
            }

            return Task.CompletedTask;
        }
    }
}
