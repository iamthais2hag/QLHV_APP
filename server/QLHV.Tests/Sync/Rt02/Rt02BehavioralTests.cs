using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.QlhvDirectRealtime;
using QLHV.Application.Sync.Rt01;

namespace QLHV.Tests.Sync.Rt02;

public sealed class Rt02BehavioralTests
{
    [Fact]
    public void Rt02_Requirement_01_production_source_name_is_rejected()
    {
        var environment = Rt02TestData.Environment(otoDatabase: "CSDL_OTO");

        var error = Assert.Throws<QlhvDirectRealtimeSafetyException>(() =>
            QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
                environment,
                Rt02TestData.Identities(environment),
                DateTime.UtcNow));

        Assert.Equal(
            QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
            error.Code);
    }

    [Fact]
    public void Rt02_Requirement_02_production_target_name_is_rejected()
    {
        var environment = Rt02TestData.Environment(targetDatabase: "QLHV_APP");

        var error = Assert.Throws<QlhvDirectRealtimeSafetyException>(() =>
            QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
                environment,
                Rt02TestData.Identities(environment),
                DateTime.UtcNow));

        Assert.Equal(
            QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
            error.Code);
    }

    [Fact]
    public void Rt02_Requirement_03_alias_to_production_is_rejected()
    {
        var environment = Rt02TestData.Environment();
        var identities = Rt02TestData.Identities(environment).ToArray();
        identities[2] = identities[2] with { IsAliasOfProduction = true };

        AssertRejected(environment, identities);
    }

    [Fact]
    public void Rt02_Requirement_04_missing_test_marker_is_rejected()
    {
        var environment = Rt02TestData.Environment();
        var identities = Rt02TestData.Identities(environment).ToArray();
        identities[0] = identities[0] with { EnvironmentMarker = string.Empty };

        AssertRejected(environment, identities);
    }

    [Fact]
    public void Rt02_Requirement_05_wrong_server_is_rejected()
    {
        var environment = Rt02TestData.Environment();
        var identities = Rt02TestData.Identities(environment).ToArray();
        identities[1] = identities[1] with { ServerIdentity = "PRODUCTION-SQL" };

        AssertRejected(environment, identities);
    }

    [Fact]
    public void Rt02_Requirement_06_exact_isolated_identity_is_accepted()
    {
        var environment = Rt02TestData.Environment();

        QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
            environment,
            Rt02TestData.Identities(environment),
            DateTime.UtcNow);
    }

    [Fact]
    public async Task Rt02_Requirement_07_approved_source_only_insert_succeeds()
    {
        var (root, plan, result) = await ExecuteAsync(
            [Rt02TestData.InsertOperation()]);

        Assert.Equal("SUCCEEDED", result.Status);
        Assert.Equal(1, result.InsertedRows);
        Assert.True(root.Store.Learners.ContainsKey(Rt02TestData.InsertIdentity));
    }

    [Fact]
    public async Task Rt02_Requirement_08_insert_creates_exactly_one_active_target()
    {
        var (root, _, _) = await ExecuteAsync(
            [Rt02TestData.InsertOperation()]);

        Assert.Single(root.Store.Learners);
        var learner = root.Store.Learners[Rt02TestData.InsertIdentity];
        Assert.True(learner.Active);
        Assert.False(learner.SoftDeleted);
    }

    [Fact]
    public async Task Rt02_Requirement_09_existing_target_blocks_insert()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.Learners[Rt02TestData.InsertIdentity] = ExistingInsertIdentity();

        await AssertTargetChangedAsync(root, plan);

        Assert.Equal(0, root.Store.CommitCount);
    }

    [Fact]
    public async Task Rt02_Requirement_10_soft_deleted_counterpart_blocks_plain_insert()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        var learner = ExistingInsertIdentity();
        learner.Active = false;
        learner.SoftDeleted = true;
        root.Store.Learners[Rt02TestData.InsertIdentity] = learner;

        await AssertTargetChangedAsync(root, plan);
    }

    [Fact]
    public async Task Rt02_Requirement_11_alias_identity_blocks_insert()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.AliasIdentities.Add(Rt02TestData.InsertIdentity);

        await AssertTargetChangedAsync(root, plan);
    }

    [Fact]
    public async Task Rt02_Requirement_12_profile_conflict_blocks_insert()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.ProfileConflictIdentities.Add(Rt02TestData.InsertIdentity);

        await AssertTargetChangedAsync(root, plan);
    }

    [Fact]
    public async Task Rt02_Requirement_13_concurrent_target_creation_rolls_back()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.CreateTargetBeforeInsert = true;

        await AssertTargetChangedAsync(root, plan);

        Assert.Empty(root.Store.Learners);
        Assert.Equal(1, root.Store.RollbackCount);
    }

    [Fact]
    public async Task Rt02_Requirement_14_insert_retry_does_not_duplicate()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await ExecuteAsync(root, plan);
        await ExecuteAsync(root, plan);

        Assert.Single(root.Store.Learners);
        Assert.Equal(1, root.Store.CommitCount);
    }

    [Fact]
    public async Task Rt02_Requirement_15_approved_HoTen_update_succeeds()
    {
        var (root, _, result) = await ExecuteAsync(
            [Rt02TestData.UpdateOperation()]);

        Assert.Equal(1, result.UpdatedRows);
        Assert.Equal(
            "SYNTHETIC LEARNER UPDATED",
            root.Store.Learners[Rt02TestData.UpdateIdentity].HoTen);
    }

    [Fact]
    public async Task Rt02_Requirement_16_only_HoTen_and_import_hash_change()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        var before = root.Store.Learners[Rt02TestData.UpdateIdentity].Clone();

        await ExecuteAsync(root, plan);

        var after = root.Store.Learners[Rt02TestData.UpdateIdentity];
        Assert.NotEqual(before.HoTen, after.HoTen);
        Assert.NotEqual(before.MappedHash, after.MappedHash);
        Assert.Equal(before.SourceProfile, after.SourceProfile);
        Assert.Equal(before.Active, after.Active);
        Assert.Equal(before.SoftDeleted, after.SoftDeleted);
    }

    [Fact]
    public async Task Rt02_Requirement_17_QLHV_owned_fields_are_preserved()
    {
        var (root, _, _) = await ExecuteAsync(
            [Rt02TestData.UpdateOperation()]);

        Assert.Equal(
            Rt02TestData.QlhvOwnedHash,
            root.Store.Learners[Rt02TestData.UpdateIdentity].QlhvOwnedHash);
    }

    [Fact]
    public async Task Rt02_Requirement_18_unknown_field_is_rejected()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(
            [Rt02TestData.UpdateOperation(["UnknownField"])]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));

        Assert.Equal(QlhvDirectRealtimeErrors.PlanFingerprintConflict, error.Code);
    }

    [Fact]
    public async Task Rt02_Requirement_19_non_source_owned_field_is_rejected()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(
            [Rt02TestData.UpdateOperation(["ManualNotes"])]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));
    }

    [Fact]
    public async Task Rt02_Requirement_20_target_changed_since_shadow_rolls_back()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.ChangeTargetBeforeUpdate = true;

        await AssertTargetChangedAsync(root, plan);

        Assert.Equal(
            Rt02TestData.OldMappedHash,
            root.Store.Learners[Rt02TestData.UpdateIdentity].MappedHash);
    }

    [Fact]
    public async Task Rt02_Requirement_21_source_changed_since_shadow_blocks_apply()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.CurrentSourceHashes[Rt02TestData.UpdateIdentity] = "CHANGED";

        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));

        Assert.Equal(QlhvDirectRealtimeErrors.SourceChangedSinceShadow, error.Code);
    }

    [Fact]
    public async Task Rt02_Requirement_22_update_row_count_not_one_rolls_back()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.ForcedUpdateRowCount = 0;

        await AssertTargetChangedAsync(root, plan);

        Assert.Equal(1, root.Store.RollbackCount);
    }

    [Fact]
    public async Task Rt02_Requirement_23_update_retry_is_idempotent()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await ExecuteAsync(root, plan);
        await ExecuteAsync(root, plan);

        Assert.Equal(1, root.Store.CommitCount);
        Assert.Equal(1, root.Checkpoints.PublishCount);
    }

    [Fact]
    public async Task Rt02_Requirement_24_target_only_is_retained_active()
    {
        var (root, _, result) = await ExecuteAsync(
            [Rt02TestData.RetainOperation()]);

        Assert.Equal(1, result.RetainedRows);
        Assert.True(root.Store.Learners[Rt02TestData.RetainIdentity].Active);
    }

    [Fact]
    public async Task Rt02_Requirement_25_manual_review_record_is_produced()
    {
        var (root, _, _) = await ExecuteAsync(
            [Rt02TestData.RetainOperation()]);

        var evidence = Assert.Single(root.Store.ReviewEvidence);
        Assert.Equal(
            QlhvDirectRealtimeDispositions.ManualReviewRequired,
            evidence.Disposition);
        Assert.False(evidence.TargetMutated);
    }

    [Fact]
    public void Rt02_Requirement_26_no_delete_SQL_exists()
    {
        var sql = string.Join("\n", QlhvDirectRealtimeApplySql.ReviewOnlyCommands);

        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rt02_Requirement_27_no_soft_delete_SQL_exists()
    {
        var sql = string.Join("\n", QlhvDirectRealtimeApplySql.ReviewOnlyCommands);

        Assert.DoesNotContain("IsDeleted =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deactiv", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rt02_Requirement_28_target_only_does_not_change_profile()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.RetainOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        var profile = root.Store.Learners[Rt02TestData.RetainIdentity].SourceProfile;

        await ExecuteAsync(root, plan);

        Assert.Equal(
            profile,
            root.Store.Learners[Rt02TestData.RetainIdentity].SourceProfile);
    }

    [Fact]
    public void Rt02_Requirement_29_no_ownership_transfer_command_exists()
    {
        var sql = string.Join("\n", QlhvDirectRealtimeApplySql.ReviewOnlyCommands);

        Assert.DoesNotContain("Owner", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "SET SourceProfileCode",
            sql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rt02_Requirement_30_insert_success_then_update_failure_rolls_back_insert()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(
            [Rt02TestData.InsertOperation(), Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        root.Store.FailUpdate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExecuteAsync(root, plan));

        Assert.False(root.Store.Learners.ContainsKey(Rt02TestData.InsertIdentity));
        Assert.Equal(0, root.Store.CommitCount);
        Assert.Equal(1, root.Store.RollbackCount);
    }

    [Fact]
    public async Task Rt02_Requirement_31_one_transaction_is_used_per_cycle()
    {
        var (root, _, _) = await ExecuteAsync(
            [Rt02TestData.InsertOperation(), Rt02TestData.RetainOperation()]);

        Assert.Equal(1, root.Store.OpenTransactionCount);
        Assert.Equal(1, root.Store.CommitCount);
    }

    [Fact]
    public async Task Rt02_Requirement_32_repository_does_not_self_commit()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await using (var transaction =
                     await root.TransactionFactory.OpenAsync(default))
        {
            await transaction.InsertAsync(plan.Operations[0], default);
            Assert.Equal(0, root.Store.CommitCount);
            Assert.Empty(root.Store.Learners);
        }

        Assert.Empty(root.Store.Learners);
    }

    [Fact]
    public async Task Rt02_Requirement_33_target_marker_is_written_in_transaction()
    {
        var fault = new Rt02CrashAfterCommitFaultInjector();
        var root = NewRoot(fault);
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExecuteAsync(root, plan));

        Assert.True(root.Store.Markers.ContainsKey(plan.CycleId));
        Assert.Equal(1, root.Store.CommitCount);
        Assert.Equal(0, root.Checkpoints.PublishCount);
    }

    [Fact]
    public async Task Rt02_Requirement_34_crash_after_commit_recovers_checkpoint()
    {
        var fault = new Rt02CrashAfterCommitFaultInjector();
        var root = NewRoot(fault);
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExecuteAsync(root, plan));

        var result = await ExecuteAsync(root, plan);

        Assert.True(result.RecoveredFromDurableMarker);
        Assert.True(result.CheckpointPublished);
        Assert.Equal(1, root.Store.CommitCount);
    }

    [Fact]
    public async Task Rt02_Requirement_35_same_cycle_replay_is_idempotent()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(
            [Rt02TestData.InsertOperation(), Rt02TestData.UpdateOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        var first = await ExecuteAsync(root, plan);
        var replay = await ExecuteAsync(root, plan);

        Assert.Equal(first.MarkerHash, replay.MarkerHash);
        Assert.Equal(1, root.Store.CommitCount);
        Assert.Equal(1, root.Checkpoints.PublishCount);
    }

    [Fact]
    public async Task Rt02_Requirement_36_fingerprint_drift_conflicts()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(
            [Rt02TestData.InsertOperation()],
            mappingFingerprint: "DRIFT-MAPPING");
        Rt02TestData.SeedForPlan(root.Store, plan);

        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));

        Assert.Equal(QlhvDirectRealtimeErrors.PlanFingerprintConflict, error.Code);
    }

    [Fact]
    public async Task Rt02_Requirement_37_checkpoint_isolated_by_environment_and_profile()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan([Rt02TestData.InsertOperation()]);
        Rt02TestData.SeedForPlan(root.Store, plan);
        await ExecuteAsync(root, plan);

        var key = Assert.Single(root.Checkpoints.Checkpoints).Key;
        Assert.Equal(Rt02TestData.EnvironmentId, key.EnvironmentId);
        Assert.Equal(Rt02TestData.SourceProfile, key.SourceProfile);
        Assert.Equal(QlhvDirectRealtimeModes.DirectRealtimeApply, key.Mode);
    }

    [Fact]
    public void Rt02_Requirement_38_diagnostics_do_not_require_raw_MaDK_or_PII()
    {
        var operation = Rt02TestData.InsertOperation();
        var serializedContract =
            $"{operation.OperationId}|{operation.IdentityHmac}|{operation.SourceRowHash}";

        Assert.DoesNotContain("MaDK", serializedContract, StringComparison.Ordinal);
        Assert.StartsWith("HMAC-", operation.IdentityHmac, StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_Requirement_39_HMAC_is_purpose_and_version_bound()
    {
        const string secret = "unit-test-secret-not-production";
        var first = QlhvDirectRealtimeHash.KeyedDiagnosticHmac(
            secret,
            "RT02-IDENTITY",
            "V1",
            "synthetic-key");
        var changedPurpose = QlhvDirectRealtimeHash.KeyedDiagnosticHmac(
            secret,
            "OTHER",
            "V1",
            "synthetic-key");
        var changedVersion = QlhvDirectRealtimeHash.KeyedDiagnosticHmac(
            secret,
            "RT02-IDENTITY",
            "V2",
            "synthetic-key");

        Assert.NotEqual(first, changedPurpose);
        Assert.NotEqual(first, changedVersion);
    }

    [Fact]
    public async Task Rt02_Requirement_40_arbitrary_column_is_rejected()
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(
            [Rt02TestData.UpdateOperation(["HoTen", "IsDeleted"])]);
        Rt02TestData.SeedForPlan(root.Store, plan);

        await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));
    }

    [Fact]
    public void Rt02_Requirement_41_apply_SQL_values_are_parameterized()
    {
        foreach (var sql in QlhvDirectRealtimeApplySql.ReviewOnlyCommands)
        {
            Assert.Contains("@", sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "EXEC(",
            string.Join("\n", QlhvDirectRealtimeApplySql.ReviewOnlyCommands),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rt02_Requirement_42_lock_name_contains_no_learner_key()
    {
        var (root, _, _) = await ExecuteAsync(
            [Rt02TestData.InsertOperation()]);

        Assert.NotNull(root.Store.LastLockName);
        Assert.DoesNotContain(
            Rt02TestData.InsertIdentity,
            root.Store.LastLockName!,
            StringComparison.Ordinal);
        Assert.Contains(Rt02TestData.SourceProfile, root.Store.LastLockName!);
    }

    [Fact]
    public void Rt02_Requirement_43_RT01_worker_remains_disabled()
    {
        Assert.False(new Rt01ShadowOptions().Enabled);
        var applicationDi = ReadWorkspaceFile(
            "server",
            "QLHV.Application",
            "DependencyInjection.cs");
        var infrastructureDi = ReadWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "DependencyInjection.cs");
        Assert.DoesNotContain("Rt01ShadowWorker", applicationDi, StringComparison.Ordinal);
        Assert.DoesNotContain("Rt01ShadowWorker", infrastructureDi, StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_Requirement_44_all_direct_realtime_flags_default_false()
    {
        var options = new QlhvDirectRealtimeOptions();

        Assert.False(options.EnableQlhvDirectRealtime);
        Assert.False(options.EnableQlhvDirectRealtimeShadow);
        Assert.False(options.EnableQlhvDirectRealtimeWrites);
        Assert.False(options.EnableQlhvDirectRealtimeDeletes);
        Assert.False(options.EnableQlhvDirectRealtimeIsolatedApply);
    }

    [Fact]
    public void Rt02_Requirement_45_existing_Auto_Sync_is_not_referenced_by_new_apply_path()
    {
        var source = ReadDirectRealtimeApplyPathSources();

        Assert.DoesNotContain("QlhvAutoSync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDL_OTO_BAK", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDL_MOTO_BAK", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_Requirement_46_existing_Auto_Sync_final_RunId_baseline_is_documented()
    {
        var report = ReadWorkspaceFile(
            "docs",
            "analysis",
            "CSDT_AUTO_SYNC_PARTIAL_SUCCESS_ROOT_CAUSE_AND_FIX.md");

        Assert.Contains(
            "182ddbfa-b47f-47ec-b5f9-01830f74ad26",
            report,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rt02_Requirement_47_isolated_cycle_has_no_production_run_counter()
    {
        var (root, _, _) = await ExecuteAsync(
            [Rt02TestData.InsertOperation()]);

        Assert.Equal(1, root.Store.CommitCount);
        Assert.Single(root.Store.Markers);
        Assert.Single(root.Checkpoints.Checkpoints);
        Assert.DoesNotContain(
            "AutoSync",
            root.Store.LastLockName!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rt02_Requirement_48_V2_to_V1_namespace_is_not_referenced()
    {
        var source = ReadDirectRealtimeApplicationSources();

        Assert.DoesNotContain(
            "QLHV.Application.Sync.Realtime",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CsdtRealtime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("V2_TO_V1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_Requirement_49_protected_config_hashes_match_entry_baseline()
    {
        const string expected =
            "12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E";

        Assert.Equal(
            expected,
            Sha256WorkspaceFile(
                "server",
                "QLHV.Api",
                "appsettings.Development.json"));
        Assert.Equal(
            expected,
            Sha256WorkspaceFile(
                "server",
                "QLHV.Worker",
                "appsettings.Development.json"));
    }

    [Fact]
    public void Rt02_Requirement_50_deterministic_fixture_contract_is_complete()
    {
        Assert.Equal(150, QlhvDirectRealtimeFixtureContract.OtoNoChangeRows);
        Assert.Equal(1, QlhvDirectRealtimeFixtureContract.OtoInsertCandidates);
        Assert.Equal(1, QlhvDirectRealtimeFixtureContract.OtoHoTenUpdateCandidates);
        Assert.Equal(1, QlhvDirectRealtimeFixtureContract.OtoTargetOnlyRetainedRows);
        Assert.Equal(3, QlhvDirectRealtimeFixtureContract.OtoExistingSoftDeletedRows);
        Assert.Equal(5, QlhvDirectRealtimeFixtureContract.MotoNoChangeRows);
        Assert.Equal(0, QlhvDirectRealtimeFixtureContract.DuplicateActiveIdentityGroups);
    }

    private static QlhvDirectRealtimeIsolatedTestCompositionRoot NewRoot(
        IQlhvDirectRealtimeFaultInjector? faultInjector = null)
        => new(faultInjector);

    private static async Task<(
        QlhvDirectRealtimeIsolatedTestCompositionRoot Root,
        QlhvDirectRealtimeApplyPlan Plan,
        QlhvDirectRealtimeApplyResult Result)> ExecuteAsync(
        IReadOnlyList<QlhvDirectRealtimeApplyOperation> operations)
    {
        var root = NewRoot();
        var plan = Rt02TestData.Plan(operations);
        Rt02TestData.SeedForPlan(root.Store, plan);
        var result = await ExecuteAsync(root, plan);
        return (root, plan, result);
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

    private static async Task AssertTargetChangedAsync(
        QlhvDirectRealtimeIsolatedTestCompositionRoot root,
        QlhvDirectRealtimeApplyPlan plan)
    {
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan));
        Assert.Equal(QlhvDirectRealtimeErrors.TargetChangedSinceShadow, error.Code);
    }

    private static void AssertRejected(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities)
    {
        var error = Assert.Throws<QlhvDirectRealtimeSafetyException>(() =>
            QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
                environment,
                identities,
                DateTime.UtcNow));
        Assert.Equal(
            QlhvDirectRealtimeErrors.IsolatedDatabaseIdentityRejected,
            error.Code);
    }

    private static Rt02TestLearner ExistingInsertIdentity()
        => new()
        {
            IdentityHmac = Rt02TestData.InsertIdentity,
            SourceProfile = Rt02TestData.SourceProfile,
            HoTen = "SYNTHETIC EXISTING",
            MappedHash = "EXISTING-HASH",
            QlhvOwnedHash = "QLHV-EXISTING",
        };

    private static string ReadDirectRealtimeApplicationSources()
    {
        var directory = FindWorkspaceDirectory(
            "server",
            "QLHV.Application",
            "Sync",
            "QlhvDirectRealtime");
        return string.Join(
            "\n",
            Directory.GetFiles(directory, "*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadDirectRealtimeApplyPathSources()
    {
        var directory = FindWorkspaceDirectory(
            "server",
            "QLHV.Application",
            "Sync",
            "QlhvDirectRealtime");
        return string.Join(
            "\n",
            new[]
            {
                "QlhvDirectRealtimeApplyCycle.cs",
                "QlhvDirectRealtimeApplySql.cs",
            }.Select(fileName => File.ReadAllText(Path.Combine(directory, fileName))));
    }

    private static string Sha256WorkspaceFile(params string[] pathParts)
        => Convert.ToHexString(
            SHA256.HashData(
                File.ReadAllBytes(FindWorkspacePath(pathParts))));

    private static string ReadWorkspaceFile(params string[] pathParts)
        => File.ReadAllText(FindWorkspacePath(pathParts));

    private static string FindWorkspaceDirectory(params string[] pathParts)
    {
        var path = FindWorkspacePath(pathParts);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        return path;
    }

    private static string FindWorkspacePath(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Cannot locate workspace artifact.",
            Path.Combine(pathParts));
    }
}
