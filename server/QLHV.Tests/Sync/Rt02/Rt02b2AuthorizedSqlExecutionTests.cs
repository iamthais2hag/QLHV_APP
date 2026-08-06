using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using QLHV.Application.Sync.QlhvDirectRealtime;

namespace QLHV.Tests.Sync.Rt02;

public sealed class Rt02b2AuthorizedSqlExecutionTests
{
    private const string RequiredOptInVariable = "QLHV_RT02B2_APPROVAL_ID";
    private const string ResultsVariable = "QLHV_RT02B2_RESULTS_PATH";
    private const string ReadOnlyPreflightVariable =
        "QLHV_RT02B2_READ_ONLY_PREFLIGHT_APPROVAL_ID";
    private const string ReadOnlyPreflightResultsVariable =
        "QLHV_RT02B2_READ_ONLY_PREFLIGHT_RESULTS_PATH";
    private const string ProcessHelperTokenVariable =
        "QLHV_RT02B2_PROCESS_HELPER_TOKEN";
    private const string ProcessHelperInputVariable =
        "QLHV_RT02B2_PROCESS_HELPER_INPUT_PATH";
    private const string ProcessHelperSignalVariable =
        "QLHV_RT02B2_PROCESS_HELPER_SIGNAL_PATH";
    private const string ProcessHelperModeVariable =
        "QLHV_RT02B2_PROCESS_HELPER_MODE";
    private const string ProcessHelperToken =
        "RT02B2-CONTROLLED-PROCESS-TERMINATION-20260727-01";
    private const string ProcessHelperInsideTransaction =
        "INSIDE_TARGET_TRANSACTION";
    private const string ProcessHelperAfterCommit = "AFTER_TARGET_COMMIT";
    private static readonly IReadOnlyList<string> EvidenceLimitations =
    [
        "Timeout and deadlock faults are injected immediately before SQL commit.",
        "Plan-hash tamper uses an injected committed-marker read result.",
    ];

    [Fact]
    [Trait("Category", "RT02B2-Isolated-Sql-Read-Only-Preflight")]
    public async Task Authorized_isolated_SQL_read_only_preflight_passes_all_gates()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ReadOnlyPreflightVariable),
                Rt02b2SqlRoute.ApprovalId,
                StringComparison.Ordinal))
        {
            return;
        }

        var resultsPath = Environment.GetEnvironmentVariable(
            ReadOnlyPreflightResultsVariable);
        Assert.False(string.IsNullOrWhiteSpace(resultsPath));

        var identities = await LoadIdentitiesAsync();
        var state = await LoadEnvironmentStateAsync();
        var environment = BuildEnvironment(state);
        QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
            environment,
            identities,
            DateTime.UtcNow);
        await AssertMetadataGatesAsync();

        var before = await ReadIntegritySnapshotAsync();
        Assert.Equal(150, before.OtoNoChange);
        Assert.Equal(1, before.OtoInsertCandidates);
        Assert.Equal(1, before.OtoUpdateCandidates);
        Assert.Equal(1, before.OtoTargetOnlyActive);
        Assert.Equal(3, before.OtoSoftDeletedBaseline);
        Assert.Equal(5, before.MotoNoChange);
        Assert.Equal(0, before.DuplicateActiveGroups);
        Assert.Equal(0, before.NonCoreInactiveOrDeletedRows);
        Assert.Equal(0, before.PiiLikeRows);
        Assert.Equal(0, before.MarkerCount);
        Assert.Equal(0, before.CheckpointCount);
        Assert.Equal(0, await CountAllManualReviewAsync());
        await AssertBusinessRowTotalsAsync(
            otoRows: 152,
            otoProfileRows: 152,
            motoRows: 5,
            motoProfileRows: 5,
            targetRows: 160,
            activeTargetRows: 157,
            softDeletedTargetRows: 3);

        await WriteJsonEvidenceAsync(
            resultsPath!,
            new
            {
                Status = "VERIFIED_READ_ONLY_PREFLIGHT",
                EnvironmentId = Rt02b2SqlRoute.EnvironmentId,
                ApprovalId = Rt02b2SqlRoute.ApprovalId,
                DatasetFingerprint = state.DatasetFingerprint,
                DatabaseIdentityCount = identities.Count,
                Snapshot = before,
            });
    }

    [Fact]
    [Trait("Category", "RT02B2-Isolated-Sql")]
    public async Task Authorized_isolated_SQL_apply_harness_passes_all_gates()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RequiredOptInVariable),
                Rt02b2SqlRoute.ApprovalId,
                StringComparison.Ordinal))
        {
            return;
        }

        var resultsPath = Environment.GetEnvironmentVariable(ResultsVariable);
        Assert.False(string.IsNullOrWhiteSpace(resultsPath));

        var startedAtUtc = DateTime.UtcNow;
        var scenarios = new List<Rt02b2ScenarioResult>();
        var currentStep = "progress_evidence_initialization";
        try
        {
            await WriteProgressEvidenceAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                "RUNNING",
                currentStep);

            currentStep = "identity_and_metadata_preflight";
            var identities = await LoadIdentitiesAsync();
            var state = await LoadEnvironmentStateAsync();
            var environment = BuildEnvironment(state);
            QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
                environment,
                identities,
                DateTime.UtcNow);
            await AssertMetadataGatesAsync();

            var before = await ReadIntegritySnapshotAsync();
            Assert.Equal(150, before.OtoNoChange);
            Assert.Equal(1, before.OtoInsertCandidates);
            Assert.Equal(1, before.OtoUpdateCandidates);
            Assert.Equal(1, before.OtoTargetOnlyActive);
            Assert.Equal(3, before.OtoSoftDeletedBaseline);
            Assert.Equal(5, before.MotoNoChange);
            Assert.Equal(0, before.DuplicateActiveGroups);
            Assert.Equal(0, before.PiiLikeRows);
            Assert.Equal(0, before.MarkerCount);
            Assert.Equal(0, before.CheckpointCount);
            Assert.Equal(0, await CountAllManualReviewAsync());
            await AssertBusinessRowTotalsAsync(
                otoRows: 152,
                otoProfileRows: 152,
                motoRows: 5,
                motoProfileRows: 5,
                targetRows: 160,
                activeTargetRows: 157,
                softDeletedTargetRows: 3);

            currentStep = "minimal_insert_update_retained";
            var coreQlhvOwnedBefore = await ReadCoreQlhvOwnedHashAsync();
            var coreOperations = await LoadCoreOperationsAsync();
            var coreRoot = NewRoot("CSDT_OTO");
            var corePlan = await BuildPlanAsync(
                "RT02B2-CORE-MINIMAL",
                "CSDT_OTO",
                Rt02b2SqlRoute.OtoDatabase,
                coreOperations,
                state);
            var coreMeasured = await ExecuteMeasuredAsync(
                "minimal_insert_update_retained",
                coreRoot,
                corePlan,
                environment,
                identities,
                retryCount: 0);
            Assert.Equal(1, coreMeasured.InsertedRows);
            Assert.Equal(1, coreMeasured.UpdatedRows);
            Assert.Equal(1, coreMeasured.RetainedRows);
            Assert.True(coreMeasured.TransactionCommitted);
            Assert.True(coreMeasured.CheckpointPublished);
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                coreMeasured);

            Assert.Equal(
                coreQlhvOwnedBefore,
                await ReadCoreQlhvOwnedHashAsync());
            Assert.Equal(1, await CountTargetRoleAsync("SOURCE_ONLY_NEW_ROW"));
            Assert.Equal(1, await CountCoreUpdatedNameAsync());
            Assert.Equal(1, await CountCoreRetainedActiveAsync());
            Assert.Equal(1, await CountManualReviewAsync("RT02B2-CORE-MINIMAL"));

            currentStep = "moto_five_no_change";
            var motoRoot = NewRoot(
                "CSDT_MOTO",
                Rt02b2SqlRoute.MotoDatabase);
            var motoPlan = await BuildPlanAsync(
                "RT02B2-MOTO-NOCHANGE",
                "CSDT_MOTO",
                Rt02b2SqlRoute.MotoDatabase,
                [],
                state);
            var motoMeasured = await ExecuteMeasuredAsync(
                "moto_five_no_change",
                motoRoot,
                motoPlan,
                environment,
                identities,
                retryCount: 0);
            Assert.Equal(0, motoMeasured.InsertedRows);
            Assert.Equal(0, motoMeasured.UpdatedRows);
            Assert.Equal(0, motoMeasured.RetainedRows);
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                motoMeasured);

            currentStep = "update_failure_rolls_back_insert";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyUpdateFailureRollsBackInsertAsync(
                    environment,
                    identities,
                    state));
            currentStep = "final_verification_failure_rolls_back_transaction";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyFinalVerificationFailureRollsBackAsync(
                    environment,
                    identities,
                    state));
            currentStep = "second_session_target_creation_blocks_apply_transaction";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyConcurrentTargetRollsBackAsync(
                    environment,
                    identities,
                    state));
            currentStep = "second_session_target_change_blocks_stale_apply";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyTargetChangedRollsBackAsync(
                    environment,
                    identities,
                    state));
            currentStep = "source_changed_since_shadow_blocks_apply";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifySourceChangedBlocksApplyAsync(
                    environment,
                    identities,
                    state));
            currentStep = "cancellation_before_transaction_no_mutation";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyCancellationBeforeTransactionAsync(
                    environment,
                    identities,
                    state));
            currentStep = "checkpoint_conflict_before_transaction";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyCheckpointConflictBeforeTransactionAsync(
                    environment,
                    identities,
                    state));
            currentStep =
                "controlled_process_termination_inside_transaction_rolls_back";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyControlledProcessTerminationInsideTransactionAsync(
                    resultsPath!,
                    environment,
                    identities,
                    state));
            currentStep =
                "controlled_process_termination_after_commit_recovers_checkpoint";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyControlledProcessTerminationAfterCommitAsync(
                    resultsPath!,
                    environment,
                    identities,
                    state));
            currentStep = "duplicate_event_replay_idempotent";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyReplayIsIdempotentAsync(
                    environment,
                    identities,
                    state));
            currentStep = "target_timeout_explicit_retry";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyTimeoutRollbackAndRetryAsync(
                    environment,
                    identities,
                    state));
            currentStep = "deadlock_explicit_retry";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyDeadlockRollbackAndRetryAsync(
                    environment,
                    identities,
                    state));
            currentStep = "mapping_fingerprint_drift_fail_closed";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyMappingFingerprintDriftFailsClosedAsync(
                    environment,
                    identities,
                    state));
            currentStep = "source_schema_fingerprint_drift_fail_closed";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifySourceSchemaFingerprintDriftFailsClosedAsync(
                    environment,
                    identities,
                    state));
            currentStep = "target_schema_fingerprint_drift_fail_closed";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyTargetSchemaFingerprintDriftFailsClosedAsync(
                    environment,
                    identities,
                    state));
            currentStep = "incomplete_immutable_plan_fails_before_transaction";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyIncompletePlanFailsClosedAsync(
                    environment,
                    identities,
                    state));
            currentStep = "injected_committed_marker_plan_hash_tamper_fails_closed";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await VerifyInjectedMarkerPlanHashTamperFailsClosedAsync(
                    environment,
                    identities,
                    state));
            currentStep = "load_100_inserts";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await ExecuteLoadInsert100Async(
                    environment,
                    identities,
                    state));
            currentStep = "load_100_updates";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await ExecuteLoadUpdate100Async(
                    environment,
                    identities,
                    state));
            currentStep = "load_mixed_1000_operations";
            await RecordScenarioAsync(
                resultsPath!,
                startedAtUtc,
                scenarios,
                await ExecuteLoadMixed1000Async(
                    environment,
                    identities,
                    state));

            currentStep = "final_integrity_and_metadata";
            var after = await ReadIntegritySnapshotAsync();
            Assert.Equal(3, after.OtoSoftDeletedBaseline);
            Assert.Equal(0, after.DuplicateActiveGroups);
            Assert.Equal(0, after.NonCoreInactiveOrDeletedRows);
            Assert.Equal(0, after.PiiLikeRows);
            Assert.Equal(1, await CountCoreRetainedActiveAsync());
            Assert.Equal(
                coreQlhvOwnedBefore,
                await ReadCoreQlhvOwnedHashAsync());
            Assert.Equal(10, after.MarkerCount);
            Assert.Equal(10, after.CheckpointCount);
            Assert.Equal(2, await CountAllManualReviewAsync());
            Assert.Equal(
                1,
                await CountManualReviewAsync("RT02B2-LOAD-MIXED-1000"));
            await AssertBusinessRowTotalsAsync(
                otoRows: 1370,
                otoProfileRows: 1370,
                motoRows: 5,
                motoProfileRows: 5,
                targetRows: 1372,
                activeTargetRows: 1369,
                softDeletedTargetRows: 3);
            await AssertMarkerCheckpointConsistencyAsync();
            Assert.Equal(22, scenarios.Count);
            Assert.True(after.ChangeTrackingRows > 0);
            await AssertMetadataGatesAsync();

            var report = new Rt02b2ExecutionResult(
                Status: "VERIFIED",
                EnvironmentId: Rt02b2SqlRoute.EnvironmentId,
                ApprovalId: Rt02b2SqlRoute.ApprovalId,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: DateTime.UtcNow,
                ServerIdentity: Rt02b2SqlRoute.ServerIdentity,
                DatabaseIdentities:
                [
                    .. identities.Select(identity => new Rt02b2DatabaseResult(
                        identity.Role,
                        identity.ActualDatabaseName,
                        identity.DatabaseId,
                        identity.DatabaseGuid,
                        identity.RecoveryModel,
                        identity.IsReadWrite)),
                ],
                DatasetFingerprint: state.DatasetFingerprint,
                MappingFingerprint: state.MappingFingerprint,
                SourceSchemaFingerprint: state.SourceSchemaFingerprint,
                TargetSchemaFingerprint: state.TargetSchemaFingerprint,
                IdentityNormalizationVersion: state.IdentityNormalizationVersion,
                EvidenceLimitations: EvidenceLimitations,
                Before: before,
                After: after,
                CoreQlhvOwnedHashBefore: coreQlhvOwnedBefore,
                CoreQlhvOwnedHashAfter: await ReadCoreQlhvOwnedHashAsync(),
                Scenarios: scenarios);

            currentStep = "verified_evidence_write";
            await WriteJsonEvidenceAsync(resultsPath!, report);
        }
        catch (Exception error)
        {
            try
            {
                await WriteProgressEvidenceAsync(
                    resultsPath!,
                    startedAtUtc,
                    scenarios,
                    "BLOCKED",
                    currentStep,
                    error);
            }
            catch
            {
                // Preserve the original assertion/safety exception.
            }

            throw;
        }
    }

    [Fact]
    [Trait("Category", "RT02B2-Process-Termination-Helper")]
    public async Task Controlled_process_termination_helper()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ProcessHelperTokenVariable),
                ProcessHelperToken,
                StringComparison.Ordinal))
        {
            return;
        }

        Assert.Equal(
            Rt02b2SqlRoute.ApprovalId,
            Environment.GetEnvironmentVariable(RequiredOptInVariable));
        var inputPath =
            Environment.GetEnvironmentVariable(ProcessHelperInputVariable);
        var signalPath =
            Environment.GetEnvironmentVariable(ProcessHelperSignalVariable);
        var mode = Environment.GetEnvironmentVariable(ProcessHelperModeVariable);
        Assert.False(string.IsNullOrWhiteSpace(inputPath));
        Assert.False(string.IsNullOrWhiteSpace(signalPath));
        Assert.True(Path.IsPathFullyQualified(inputPath!));
        Assert.True(Path.IsPathFullyQualified(signalPath!));
        Assert.Equal(
            Path.GetDirectoryName(inputPath),
            Path.GetDirectoryName(signalPath));
        Assert.True(File.Exists(inputPath));
        Assert.False(File.Exists(signalPath));
        Assert.True(
            mode is ProcessHelperInsideTransaction or ProcessHelperAfterCommit);

        var input = JsonSerializer.Deserialize<Rt02b2ProcessHelperInput>(
            await File.ReadAllTextAsync(inputPath!));
        Assert.NotNull(input);
        var identities = await LoadIdentitiesAsync();
        var state = await LoadEnvironmentStateAsync();
        var environment = BuildEnvironment(state);
        QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
            environment,
            identities,
            DateTime.UtcNow);
        await AssertMetadataGatesAsync();

        QlhvDirectRealtimeSqlIsolatedTestCompositionRoot root;
        if (string.Equals(
                mode,
                ProcessHelperInsideTransaction,
                StringComparison.Ordinal))
        {
            root = NewRoot(input!.Plan.SourceProfile, input.SourceDatabase);
            root.TransactionFactory.BeforeVerificationProcessTerminationSignalPath =
                signalPath;
        }
        else
        {
            root = NewRoot(
                input!.Plan.SourceProfile,
                input.SourceDatabase,
                faultInjector:
                    new Rt02BlockAfterCommitProcessTerminationFaultInjector(
                        signalPath!));
        }

        await ExecuteAsync(root, input.Plan, environment, identities);
        Assert.Fail(
            "The controlled process-termination helper returned without " +
            "being terminated by its parent.");
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyUpdateFailureRollsBackInsertAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_ROLLBACK";
        var insert = Assert.Single(
            await SeedInsertOperationsAsync(profile, "ROLLBACK-I", 1));
        var update = Assert.Single(
            await SeedUpdateOperationsAsync(profile, "ROLLBACK-U", 1));
        var mappedBefore = await ReadMappedHashAsync(update.IdentityHmac);
        var root = NewRoot(profile);
        root.TransactionFactory.FailUpdate = true;
        var plan = await BuildPlanAsync(
            "RT02B2-ROLLBACK-INSERT-UPDATE",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [insert, update],
            state);
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(0, await CountTargetIdentityAsync(insert.IdentityHmac));
        Assert.Equal(mappedBefore, await ReadMappedHashAsync(update.IdentityHmac));
        Assert.Equal(0, root.Metrics.CommitCount);
        Assert.Equal(1, root.Metrics.RollbackCount);
        return FailureMetric(
            "update_failure_rolls_back_insert",
            "EXPECTED_ROLLBACK",
            "INJECTED_UPDATE_FAILURE",
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyFinalVerificationFailureRollsBackAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_VERIFYFAIL";
        var insert = Assert.Single(
            await SeedInsertOperationsAsync(profile, "VERIFYFAIL-I", 1));
        var update = Assert.Single(
            await SeedUpdateOperationsAsync(profile, "VERIFYFAIL-U", 1));
        var mappedBefore = await ReadMappedHashAsync(update.IdentityHmac);
        var qlhvOwnedBefore = await ReadQlhvOwnedTupleHashAsync(
            update.IdentityHmac);
        var root = NewRoot(profile);
        root.TransactionFactory.FailVerification = true;
        var plan = await BuildPlanAsync(
            "RT02B2-VERIFY-FAILURE",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [insert, update],
            state);
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(0, await CountTargetIdentityAsync(insert.IdentityHmac));
        Assert.Equal(mappedBefore, await ReadMappedHashAsync(update.IdentityHmac));
        Assert.Equal(
            qlhvOwnedBefore,
            await ReadQlhvOwnedTupleHashAsync(update.IdentityHmac));
        Assert.Equal(0, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        Assert.Equal(0, root.Metrics.CommitCount);
        Assert.Equal(1, root.Metrics.RollbackCount);
        return FailureMetric(
            "final_verification_failure_rolls_back_transaction",
            "EXPECTED_ROLLBACK",
            "INJECTED_FINAL_VERIFICATION_FAILURE",
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyConcurrentTargetRollsBackAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_CONCURRENT";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "CONCURRENT-I", 1));
        var root = NewRoot(profile);
        root.TransactionFactory.CreateTargetBeforeInsert = true;
        var plan = await BuildPlanAsync(
            "RT02B2-CONCURRENT-TARGET",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(
            QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
            error.Code);
        Assert.Equal(1, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(
            QlhvDirectRealtimeHash.Sha256("RT02B2-CONCURRENT-MAPPED"),
            await ReadMappedHashAsync(operation.IdentityHmac));
        Assert.Equal(1, root.Metrics.SecondSessionWriteCount);
        Assert.Equal(1, root.Metrics.RollbackCount);
        Assert.Equal(0, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        return FailureMetric(
            "second_session_target_creation_blocks_apply_transaction",
            "EXPECTED_ROLLBACK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyTargetChangedRollsBackAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_TARGETCHG";
        var operation = Assert.Single(
            await SeedUpdateOperationsAsync(profile, "TARGETCHG-U", 1));
        var qlhvOwnedBefore = await ReadQlhvOwnedTupleHashAsync(
            operation.IdentityHmac);
        var root = NewRoot(profile);
        root.TransactionFactory.ChangeTargetBeforeUpdate = true;
        var plan = await BuildPlanAsync(
            "RT02B2-TARGET-CHANGED",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(
            QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
            error.Code);
        Assert.Equal(
            QlhvDirectRealtimeHash.Sha256("RT02B2-CONCURRENT-TARGET-CHANGE"),
            await ReadMappedHashAsync(operation.IdentityHmac));
        Assert.Equal(
            qlhvOwnedBefore,
            await ReadQlhvOwnedTupleHashAsync(operation.IdentityHmac));
        Assert.Equal(1, root.Metrics.SecondSessionWriteCount);
        Assert.Equal(1, root.Metrics.RollbackCount);
        Assert.Equal(0, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        return FailureMetric(
            "second_session_target_change_blocks_stale_apply",
            "EXPECTED_ROLLBACK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifySourceChangedBlocksApplyAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_SOURCECHG";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "SOURCECHG-I", 1));
        await ChangeSourceHashAsync(
            operation.IdentityHmac,
            QlhvDirectRealtimeHash.Sha256("RT02B2-SOURCE-CHANGED"));
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-SOURCE-CHANGED",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(
            QlhvDirectRealtimeErrors.SourceChangedSinceShadow,
            error.Code);
        Assert.Equal(0, await CountTargetIdentityAsync(operation.IdentityHmac));
        return FailureMetric(
            "source_changed_since_shadow_blocks_apply",
            "EXPECTED_BLOCK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyCancellationBeforeTransactionAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_PRECANCEL";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "PRECANCEL-I", 1));
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-CANCEL-BEFORE-TX",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExecuteAsync(
                root,
                plan,
                environment,
                identities,
                cancellation.Token));
        timer.Stop();
        Assert.Equal(0, root.Metrics.OpenTransactionCount);
        Assert.Equal(0, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(0, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        return FailureMetric(
            "cancellation_before_transaction_no_mutation",
            "EXPECTED_BLOCK",
            "CANCELLED_BEFORE_TRANSACTION",
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyCheckpointConflictBeforeTransactionAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_CPCONFLICT";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "CPCONFLICT-I", 1));
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-CHECKPOINT-CONFLICT-ATTEMPT",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var priorMarker = new QlhvDirectRealtimeApplyMarker(
            "RT02B2-CHECKPOINT-CONFLICT-PRIOR",
            QlhvDirectRealtimeHash.Sha256("RT02B2-PRIOR-PLAN"),
            QlhvDirectRealtimeHash.Sha256("RT02B2-PRIOR-DISPOSITION"),
            0,
            0,
            0,
            QlhvDirectRealtimeHash.Sha256("RT02B2-PRIOR-QLHV-OWNED"),
            DateTime.UtcNow);
        await SeedApplyMarkerAsync(priorMarker);
        var key = new QlhvDirectRealtimeApplyCheckpointKey(
            profile,
            QlhvDirectRealtimeModes.DirectRealtimeApply,
            plan.MappingFingerprint,
            Rt02b2SqlRoute.EnvironmentId);
        await root.Checkpoints.PublishAsync(
            new QlhvDirectRealtimeApplyCheckpoint(
                key,
                priorMarker.CycleId,
                priorMarker.PlanHash,
                priorMarker.MarkerHash,
                plan.SourceWatermark,
                DateTime.UtcNow),
            default);
        Assert.Equal(1, await CountMarkerAsync(priorMarker.CycleId));
        Assert.Equal(1, await CountCheckpointAsync(profile));

        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(QlhvDirectRealtimeErrors.CheckpointConflict, error.Code);
        Assert.Equal(0, root.Metrics.OpenTransactionCount);
        Assert.Equal(0, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(1, await CountMarkerAsync(priorMarker.CycleId));
        Assert.Equal(1, await CountCheckpointAsync(profile));
        return FailureMetric(
            "checkpoint_conflict_before_transaction",
            "EXPECTED_BLOCK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyControlledProcessTerminationInsideTransactionAsync(
            string resultsPath,
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_KILL_INTX";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "KILL-INTX-I", 1));
        var plan = await BuildPlanAsync(
            "RT02B2-KILL-INSIDE-TRANSACTION",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        var duration = await RunControlledProcessHelperAsync(
            resultsPath,
            "kill-inside-transaction",
            ProcessHelperInsideTransaction,
            new Rt02b2ProcessHelperInput(
                Rt02b2SqlRoute.OtoDatabase,
                plan));
        await WaitForConditionAsync(
            async () =>
                await CountTargetIdentityAsync(operation.IdentityHmac) == 0 &&
                await CountMarkerAsync(plan.CycleId) == 0 &&
                await CountCheckpointAsync(profile) == 0,
            "The killed in-transaction child did not roll back completely.");
        return new Rt02b2ScenarioResult(
            "controlled_process_termination_inside_transaction_rolls_back",
            "EXPECTED_PROCESS_TERMINATION_ROLLBACK",
            "CHILD_PROCESS_TERMINATED_INSIDE_TRANSACTION",
            duration.TotalMilliseconds,
            0,
            0,
            0,
            0,
            Math.Max(
                0,
                Process.GetCurrentProcess().WorkingSet64 - memoryBefore),
            true,
            0,
            0,
            0,
            false,
            false)
        {
            ExternalEvidencePrefix =
                $"{resultsPath}.kill-inside-transaction",
        };
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyControlledProcessTerminationAfterCommitAsync(
            string resultsPath,
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_KILL_POST";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "KILL-POST-I", 1));
        var plan = await BuildPlanAsync(
            "RT02B2-KILL-AFTER-COMMIT",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        var timer = Stopwatch.StartNew();
        await RunControlledProcessHelperAsync(
            resultsPath,
            "kill-after-commit",
            ProcessHelperAfterCommit,
            new Rt02b2ProcessHelperInput(
                Rt02b2SqlRoute.OtoDatabase,
                plan));
        await WaitForConditionAsync(
            async () =>
                await CountTargetIdentityAsync(operation.IdentityHmac) == 1 &&
                await CountMarkerAsync(plan.CycleId) == 1 &&
                await CountCheckpointAsync(profile) == 0,
            "The killed post-commit child did not leave the exact durable marker state.");

        var root = NewRoot(profile);
        var queryBefore = root.Metrics.QueryCount;
        var recovered = await ExecuteAsync(root, plan, environment, identities);
        timer.Stop();
        Assert.True(recovered.RecoveredFromDurableMarker);
        Assert.True(recovered.CheckpointPublished);
        Assert.Equal(0, root.Metrics.OpenTransactionCount);
        Assert.Equal(1, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(1, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(1, await CountCheckpointAsync(profile));
        return new Rt02b2ScenarioResult(
            "controlled_process_termination_after_commit_recovers_checkpoint",
            "SUCCEEDED_RECOVERED",
            "CHILD_PROCESS_TERMINATED_AFTER_COMMIT",
            timer.Elapsed.TotalMilliseconds,
            root.Metrics.LastTransactionDuration.TotalMilliseconds,
            1 / Math.Max(timer.Elapsed.TotalSeconds, 0.000001),
            root.Metrics.QueryCount - queryBefore,
            1,
            Math.Max(
                0,
                Process.GetCurrentProcess().WorkingSet64 - memoryBefore),
            root.Metrics.RollbackCount == 0,
            1,
            0,
            0,
            true,
            true)
        {
            ExternalEvidencePrefix = $"{resultsPath}.kill-after-commit",
        };
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyReplayIsIdempotentAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_REPLAY";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "REPLAY-I", 1));
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-IDEMPOTENT-REPLAY",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var first = await ExecuteAsync(root, plan, environment, identities);
        var replay = await ExecuteAsync(root, plan, environment, identities);
        timer.Stop();
        Assert.Equal(first.MarkerHash, replay.MarkerHash);
        Assert.Equal("SUCCEEDED_IDEMPOTENT_REPLAY", replay.Status);
        Assert.Equal(1, root.Metrics.OpenTransactionCount);
        Assert.Equal(1, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(1, await CountCheckpointAsync(profile));
        return new Rt02b2ScenarioResult(
            "duplicate_event_replay_idempotent",
            replay.Status,
            null,
            timer.Elapsed.TotalMilliseconds,
            root.Metrics.LastTransactionDuration.TotalMilliseconds,
            1 / Math.Max(timer.Elapsed.TotalSeconds, 0.000001),
            root.Metrics.QueryCount - queryBefore,
            1,
            Math.Max(
                0,
                Process.GetCurrentProcess().WorkingSet64 - memoryBefore),
            true,
            1,
            0,
            0,
            true,
            true);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyTimeoutRollbackAndRetryAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_TIMEOUT";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "TIMEOUT-I", 1));
        var fault = new Rt02SqlCommitFaultController
        {
            Mode = Rt02SqlCommitFaultMode.TimeoutOnce,
        };
        var root = NewRoot(profile, commitFault: fault);
        var plan = await BuildPlanAsync(
            "RT02B2-TARGET-TIMEOUT",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        await Assert.ThrowsAsync<TimeoutException>(
            () => ExecuteAsync(root, plan, environment, identities));
        Assert.Equal(0, await CountTargetIdentityAsync(operation.IdentityHmac));
        var result = await ExecuteAsync(root, plan, environment, identities);
        timer.Stop();
        Assert.Equal("SUCCEEDED", result.Status);
        Assert.Equal(1, root.Metrics.RollbackCount);
        Assert.Equal(1, root.Metrics.CommitCount);
        Assert.Equal(1, await CountTargetIdentityAsync(operation.IdentityHmac));
        return new Rt02b2ScenarioResult(
            "target_timeout_explicit_retry",
            result.Status,
            "INJECTED_TIMEOUT",
            timer.Elapsed.TotalMilliseconds,
            root.Metrics.LastTransactionDuration.TotalMilliseconds,
            1 / Math.Max(timer.Elapsed.TotalSeconds, 0.000001),
            root.Metrics.QueryCount - queryBefore,
            1,
            Math.Max(
                0,
                Process.GetCurrentProcess().WorkingSet64 - memoryBefore),
            true,
            1,
            0,
            0,
            true,
            true);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyDeadlockRollbackAndRetryAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_DEADLOCK";
        var operation = Assert.Single(
            await SeedUpdateOperationsAsync(profile, "DEADLOCK-U", 1));
        var qlhvBefore = await ReadQlhvOwnedTupleHashAsync(
            operation.IdentityHmac);
        var fault = new Rt02SqlCommitFaultController
        {
            Mode = Rt02SqlCommitFaultMode.DeadlockOnce,
        };
        var root = NewRoot(profile, commitFault: fault);
        var plan = await BuildPlanAsync(
            "RT02B2-DEADLOCK",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        await Assert.ThrowsAsync<Rt02InjectedDeadlockException>(
            () => ExecuteAsync(root, plan, environment, identities));
        Assert.Equal(
            operation.StagedTargetMappedHash,
            await ReadMappedHashAsync(operation.IdentityHmac));
        var result = await ExecuteAsync(root, plan, environment, identities);
        timer.Stop();
        Assert.Equal("SUCCEEDED", result.Status);
        Assert.Equal(
            operation.SourceRowHash,
            await ReadMappedHashAsync(operation.IdentityHmac));
        Assert.Equal(
            qlhvBefore,
            await ReadQlhvOwnedTupleHashAsync(operation.IdentityHmac));
        return new Rt02b2ScenarioResult(
            "deadlock_explicit_retry",
            result.Status,
            "INJECTED_DEADLOCK",
            timer.Elapsed.TotalMilliseconds,
            root.Metrics.LastTransactionDuration.TotalMilliseconds,
            1 / Math.Max(timer.Elapsed.TotalSeconds, 0.000001),
            root.Metrics.QueryCount - queryBefore,
            1,
            Math.Max(
                0,
                Process.GetCurrentProcess().WorkingSet64 - memoryBefore),
            root.Metrics.RollbackCount == 1,
            0,
            1,
            0,
            true,
            true);
    }

    private static Task<Rt02b2ScenarioResult>
        VerifyMappingFingerprintDriftFailsClosedAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
        => VerifyFingerprintDriftFailsClosedAsync(
            "mapping_fingerprint_drift_fail_closed",
            "RT02_DRIFT_MAP",
            "DRIFT-MAPPING-U",
            "RT02B2-MAPPING-FINGERPRINT-DRIFT",
            environment,
            identities,
            state,
            mappingFingerprint:
                QlhvDirectRealtimeHash.Sha256("RT02B2-DRIFT-MAPPING"));

    private static Task<Rt02b2ScenarioResult>
        VerifySourceSchemaFingerprintDriftFailsClosedAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
        => VerifyFingerprintDriftFailsClosedAsync(
            "source_schema_fingerprint_drift_fail_closed",
            "RT02_DRIFT_SRC",
            "DRIFT-SOURCE-SCHEMA-U",
            "RT02B2-SOURCE-SCHEMA-FINGERPRINT-DRIFT",
            environment,
            identities,
            state,
            sourceSchemaFingerprint:
                QlhvDirectRealtimeHash.Sha256("RT02B2-DRIFT-SOURCE-SCHEMA"));

    private static Task<Rt02b2ScenarioResult>
        VerifyTargetSchemaFingerprintDriftFailsClosedAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
        => VerifyFingerprintDriftFailsClosedAsync(
            "target_schema_fingerprint_drift_fail_closed",
            "RT02_DRIFT_TGT",
            "DRIFT-TARGET-SCHEMA-U",
            "RT02B2-TARGET-SCHEMA-FINGERPRINT-DRIFT",
            environment,
            identities,
            state,
            targetSchemaFingerprint:
                QlhvDirectRealtimeHash.Sha256("RT02B2-DRIFT-TARGET-SCHEMA"));

    private static async Task<Rt02b2ScenarioResult>
        VerifyFingerprintDriftFailsClosedAsync(
            string resultName,
            string profile,
            string scenario,
            string cycleId,
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state,
            string? mappingFingerprint = null,
            string? sourceSchemaFingerprint = null,
            string? targetSchemaFingerprint = null)
    {
        var operation = Assert.Single(
            await SeedUpdateOperationsAsync(profile, scenario, 1));
        var before = await ReadMappedHashAsync(operation.IdentityHmac);
        var qlhvOwnedBefore = await ReadQlhvOwnedTupleHashAsync(
            operation.IdentityHmac);
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            cycleId,
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state,
            mappingFingerprint,
            sourceSchemaFingerprint,
            targetSchemaFingerprint);
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(
            QlhvDirectRealtimeErrors.PlanFingerprintConflict,
            error.Code);
        Assert.Equal(before, await ReadMappedHashAsync(operation.IdentityHmac));
        Assert.Equal(
            qlhvOwnedBefore,
            await ReadQlhvOwnedTupleHashAsync(operation.IdentityHmac));
        Assert.Equal(0, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        Assert.Equal(0, root.Metrics.CommitCount);
        Assert.Equal(1, root.Metrics.RollbackCount);
        return FailureMetric(
            resultName,
            "EXPECTED_BLOCK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyIncompletePlanFailsClosedAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_PLANEMPTY";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "PLANEMPTY-I", 1));
        var root = NewRoot(profile);
        var completePlan = await BuildPlanAsync(
            "RT02B2-INCOMPLETE-PLAN",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var incompletePlan = completePlan with
        {
            StageHash = string.Empty,
        };
        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, incompletePlan, environment, identities));
        timer.Stop();
        Assert.Equal(
            QlhvDirectRealtimeErrors.PlanFingerprintConflict,
            error.Code);
        Assert.Equal(0, root.Metrics.OpenTransactionCount);
        Assert.Equal(0, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(0, await CountMarkerAsync(incompletePlan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        return FailureMetric(
            "incomplete_immutable_plan_fails_before_transaction",
            "EXPECTED_BLOCK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult>
        VerifyInjectedMarkerPlanHashTamperFailsClosedAsync(
            QlhvDirectRealtimeIsolatedEnvironment environment,
            IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
            Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_PLANTAMPER";
        var operation = Assert.Single(
            await SeedInsertOperationsAsync(profile, "PLANTAMPER-I", 1));
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-DURABLE-MARKER-PLAN-TAMPER",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            [operation],
            state);
        var tamperedMarker = new QlhvDirectRealtimeApplyMarker(
            plan.CycleId,
            QlhvDirectRealtimeHash.Sha256("RT02B2-TAMPERED-PLAN-HASH"),
            plan.DispositionHash,
            0,
            0,
            0,
            QlhvDirectRealtimeHash.Sha256("RT02B2-TAMPERED-QLHV-OWNED"),
            DateTime.UtcNow);
        root.TransactionFactory.CommittedMarkerOverride = tamperedMarker;

        var timer = Stopwatch.StartNew();
        var queryBefore = root.Metrics.QueryCount;
        var error = await Assert.ThrowsAsync<QlhvDirectRealtimeSafetyException>(
            () => ExecuteAsync(root, plan, environment, identities));
        timer.Stop();
        Assert.Equal(
            QlhvDirectRealtimeErrors.PlanFingerprintConflict,
            error.Code);
        Assert.Equal(0, root.Metrics.OpenTransactionCount);
        Assert.Equal(0, await CountTargetIdentityAsync(operation.IdentityHmac));
        Assert.Equal(0, await CountMarkerAsync(plan.CycleId));
        Assert.Equal(0, await CountCheckpointAsync(profile));
        return FailureMetric(
            "injected_committed_marker_plan_hash_tamper_fails_closed",
            "EXPECTED_BLOCK",
            error.Code,
            timer.Elapsed,
            root,
            queryBefore);
    }

    private static async Task<Rt02b2ScenarioResult> ExecuteLoadInsert100Async(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_LOAD_I100";
        var operations = await SeedInsertOperationsAsync(
            profile,
            "LOAD-I100",
            100);
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-LOAD-INSERT-100",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            operations,
            state);
        var measured = await ExecuteMeasuredAsync(
            "load_100_inserts",
            root,
            plan,
            environment,
            identities,
            0);
        Assert.Equal(100, measured.InsertedRows);
        return measured;
    }

    private static async Task<Rt02b2ScenarioResult> ExecuteLoadUpdate100Async(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_LOAD_U100";
        var operations = await SeedUpdateOperationsAsync(
            profile,
            "LOAD-U100",
            100);
        var qlhvBefore = await ReadProfileQlhvOwnedHashAsync(profile);
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-LOAD-UPDATE-100",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            operations,
            state);
        var measured = await ExecuteMeasuredAsync(
            "load_100_updates",
            root,
            plan,
            environment,
            identities,
            0);
        Assert.Equal(100, measured.UpdatedRows);
        Assert.Equal(
            qlhvBefore,
            await ReadProfileQlhvOwnedHashAsync(profile));
        return measured;
    }

    private static async Task<Rt02b2ScenarioResult> ExecuteLoadMixed1000Async(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        Rt02b2EnvironmentState state)
    {
        const string profile = "RT02_LOAD_M1000";
        var inserts = await SeedInsertOperationsAsync(
            profile,
            "LOAD-M1000-I",
            500);
        var updates = await SeedUpdateOperationsAsync(
            profile,
            "LOAD-M1000-U",
            499);
        var retained = await SeedRetainOperationAsync(
            profile,
            "LOAD-M1000-R");
        var operations = inserts
            .Concat(updates)
            .Append(retained)
            .ToArray();
        var qlhvBefore = await ReadProfileQlhvOwnedHashAsync(profile);
        var root = NewRoot(profile);
        var plan = await BuildPlanAsync(
            "RT02B2-LOAD-MIXED-1000",
            profile,
            Rt02b2SqlRoute.OtoDatabase,
            operations,
            state);
        var measured = await ExecuteMeasuredAsync(
            "load_mixed_1000_operations",
            root,
            plan,
            environment,
            identities,
            0);
        Assert.Equal(500, measured.InsertedRows);
        Assert.Equal(499, measured.UpdatedRows);
        Assert.Equal(1, measured.RetainedRows);
        Assert.True(await CountProfileQlhvOwnedPrefixPreservedAsync(
            profile,
            qlhvBefore));
        return measured;
    }

    private static QlhvDirectRealtimeSqlIsolatedTestCompositionRoot NewRoot(
        string sourceProfile,
        string sourceDatabase = Rt02b2SqlRoute.OtoDatabase,
        Rt02SqlCommitFaultController? commitFault = null,
        IQlhvDirectRealtimeFaultInjector? faultInjector = null)
        => new(
            sourceProfile,
            sourceDatabase,
            commitFault,
            faultInjector);

    private static Task<QlhvDirectRealtimeApplyResult> ExecuteAsync(
        QlhvDirectRealtimeSqlIsolatedTestCompositionRoot root,
        QlhvDirectRealtimeApplyPlan plan,
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        CancellationToken cancellationToken = default)
        => root.Cycle.ExecuteAsync(
            plan,
            environment,
            identities,
            cancellationToken);

    private static async Task<Rt02b2ScenarioResult> ExecuteMeasuredAsync(
        string name,
        QlhvDirectRealtimeSqlIsolatedTestCompositionRoot root,
        QlhvDirectRealtimeApplyPlan plan,
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        int retryCount)
    {
        var memoryBefore = Process.GetCurrentProcess().WorkingSet64;
        var queryBefore = root.Metrics.QueryCount;
        var timer = Stopwatch.StartNew();
        var result = await ExecuteAsync(root, plan, environment, identities);
        timer.Stop();
        var memoryAfter = Process.GetCurrentProcess().WorkingSet64;
        var rows = result.InsertedRows + result.UpdatedRows + result.RetainedRows;
        return new Rt02b2ScenarioResult(
            name,
            result.Status,
            null,
            timer.Elapsed.TotalMilliseconds,
            root.Metrics.LastTransactionDuration.TotalMilliseconds,
            rows / Math.Max(timer.Elapsed.TotalSeconds, 0.000001),
            root.Metrics.QueryCount - queryBefore,
            retryCount,
            Math.Max(0, memoryAfter - memoryBefore),
            root.Metrics.RollbackCount == 0,
            result.InsertedRows,
            result.UpdatedRows,
            result.RetainedRows,
            result.TransactionCommitted,
            result.CheckpointPublished,
            root.Metrics.SecondSessionWriteCount);
    }

    private static Rt02b2ScenarioResult FailureMetric(
        string name,
        string status,
        string errorCode,
        TimeSpan duration,
        QlhvDirectRealtimeSqlIsolatedTestCompositionRoot root,
        int queryBefore)
        => new(
            name,
            status,
            errorCode,
            duration.TotalMilliseconds,
            root.Metrics.LastTransactionDuration.TotalMilliseconds,
            0,
            root.Metrics.QueryCount - queryBefore,
            0,
            0,
            root.Metrics.RollbackCount > 0,
            0,
            0,
            0,
            false,
            false,
            root.Metrics.SecondSessionWriteCount);

    private static async Task<QlhvDirectRealtimeApplyPlan> BuildPlanAsync(
        string cycleId,
        string sourceProfile,
        string sourceDatabase,
        IReadOnlyList<QlhvDirectRealtimeApplyOperation> operations,
        Rt02b2EnvironmentState state,
        string? mappingFingerprint = null,
        string? sourceSchemaFingerprint = null,
        string? targetSchemaFingerprint = null)
    {
        var watermark = await ReadChangeTrackingVersionAsync(sourceDatabase);
        return new QlhvDirectRealtimeApplyPlan(
            cycleId,
            Rt02b2SqlRoute.EnvironmentId,
            sourceProfile,
            mappingFingerprint ?? state.MappingFingerprint,
            sourceSchemaFingerprint ?? state.SourceSchemaFingerprint,
            targetSchemaFingerprint ?? state.TargetSchemaFingerprint,
            watermark,
            state.IdentityNormalizationVersion,
            QlhvDirectRealtimeHash.Sha256(
                $"STAGE|{cycleId}|" +
                string.Join("|", operations.Select(operation =>
                    $"{operation.IdentityHmac}:{operation.SourceRowHash}"))),
            QlhvDirectRealtimeHash.Sha256(
                $"COMPARE|{cycleId}|" +
                string.Join("|", operations.Select(operation =>
                    operation.StagedTargetMappedHash))),
            QlhvDirectRealtimeHash.Sha256(
                $"DISPOSITION|{cycleId}|" +
                string.Join("|", operations.Select(operation =>
                    operation.Disposition))),
            operations);
    }

    private static async Task<long> ReadChangeTrackingVersionAsync(
        string database)
    {
        await using var connection = await OpenAsync(database);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION();";
        var value = await command.ExecuteScalarAsync();
        Assert.NotNull(value);
        return Convert.ToInt64(value);
    }

    private static async Task<IReadOnlyList<QlhvDirectRealtimeApplyOperation>>
        LoadCoreOperationsAsync()
    {
        var operations = new List<QlhvDirectRealtimeApplyOperation>();
        await using (var source = await OpenAsync(Rt02b2SqlRoute.OtoDatabase))
        {
            await using var insert = source.CreateCommand();
            insert.CommandText = """
SELECT IdentityHmac, HoTen, SourceRowHash
FROM dbo.NguoiLX
WHERE ScenarioCode = 'CORE'
  AND DatasetRole = 'SOURCE_ONLY_NEW_ROW';
""";
            await using var reader = await insert.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            operations.Add(new QlhvDirectRealtimeApplyOperation(
                "OP-CORE-INSERT",
                QlhvDirectRealtimeApplyOperationKind.Insert,
                QlhvDirectRealtimeDispositions.WouldInsertSafeAfterApproval,
                reader.GetString(0),
                reader.GetString(2),
                string.Empty,
                string.Empty,
                [],
                reader.GetString(1)));
        }

        await using (var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase))
        {
            await using var update = connection.CreateCommand();
            update.CommandText = """
SELECT
    source.IdentityHmac,
    source.HoTen,
    source.SourceRowHash,
    target.MappedHash,
    target.QlhvOwnedHash
FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS source
INNER JOIN dbo.Rt02Learner AS target
    ON target.IdentityHmac = source.IdentityHmac
WHERE source.ScenarioCode = 'CORE'
  AND source.DatasetRole = 'STALE_IMPORTED_VALUE'
  AND target.ScenarioCode = 'CORE';
""";
            await using (var reader = await update.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                operations.Add(new QlhvDirectRealtimeApplyOperation(
                    "OP-CORE-UPDATE",
                    QlhvDirectRealtimeApplyOperationKind.Update,
                    QlhvDirectRealtimeDispositions.StaleImportedValue,
                    reader.GetString(0),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    ["HoTen"],
                    reader.GetString(1)));
            }

            await using var retain = connection.CreateCommand();
            retain.CommandText = """
SELECT IdentityHmac, MappedHash, QlhvOwnedHash
FROM dbo.Rt02Learner
WHERE ScenarioCode = 'CORE'
  AND DatasetRole = 'SOURCE_ROW_REMOVED';
""";
            await using var retainedReader = await retain.ExecuteReaderAsync();
            Assert.True(await retainedReader.ReadAsync());
            operations.Add(new QlhvDirectRealtimeApplyOperation(
                "OP-CORE-RETAIN",
                QlhvDirectRealtimeApplyOperationKind.RetainForManualReview,
                QlhvDirectRealtimeDispositions.ManualReviewRequired,
                retainedReader.GetString(0),
                string.Empty,
                retainedReader.GetString(1),
                retainedReader.GetString(2),
                []));
        }

        return operations;
    }

    private static async Task<IReadOnlyList<QlhvDirectRealtimeApplyOperation>>
        SeedInsertOperationsAsync(
            string sourceProfile,
            string scenario,
            int count)
    {
        var operations = new List<QlhvDirectRealtimeApplyOperation>(count);
        await using var connection = await OpenAsync(Rt02b2SqlRoute.OtoDatabase);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        for (var index = 1; index <= count; index++)
        {
            var identity = IdentityHmac($"{scenario}|{index:D4}");
            var desiredName = $"SYNTHETIC {scenario} {index:D4}";
            var sourceHash = QlhvDirectRealtimeHash.Sha256(
                $"RT02B2|SOURCE|{identity}|{desiredName}");
            await using (var command = new SqlCommand("""
INSERT dbo.NguoiLX
(
    IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash
)
VALUES
(
    @IdentityHmac, @ScenarioCode, 'SOURCE_ONLY_NEW_ROW',
    @HoTen, @SourceRowHash
);
INSERT dbo.NguoiLX_HoSo(IdentityHmac, PayloadHash)
VALUES(@IdentityHmac, @PayloadHash);
""", connection, transaction))
            {
                AddIdentity(command, identity);
                command.Parameters.Add("@ScenarioCode", SqlDbType.VarChar, 40).Value =
                    scenario;
                command.Parameters.Add("@HoTen", SqlDbType.NVarChar, 200).Value =
                    desiredName;
                command.Parameters.Add("@SourceRowHash", SqlDbType.Char, 64).Value =
                    sourceHash;
                command.Parameters.Add("@PayloadHash", SqlDbType.Char, 64).Value =
                    QlhvDirectRealtimeHash.Sha256($"RT02B2|HOSO|{identity}");
                Assert.Equal(2, await command.ExecuteNonQueryAsync());
            }

            operations.Add(new QlhvDirectRealtimeApplyOperation(
                $"OP-{scenario}-INSERT-{index:D4}",
                QlhvDirectRealtimeApplyOperationKind.Insert,
                QlhvDirectRealtimeDispositions.WouldInsertSafeAfterApproval,
                identity,
                sourceHash,
                string.Empty,
                string.Empty,
                [],
                desiredName));
        }

        await transaction.CommitAsync();
        return operations;
    }

    private static async Task<IReadOnlyList<QlhvDirectRealtimeApplyOperation>>
        SeedUpdateOperationsAsync(
            string sourceProfile,
            string scenario,
            int count)
    {
        var operations = new List<QlhvDirectRealtimeApplyOperation>(count);
        await using var source = await OpenAsync(Rt02b2SqlRoute.OtoDatabase);
        await using var target = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var sourceTransaction = (SqlTransaction)
            await source.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var targetTransaction = (SqlTransaction)
            await target.BeginTransactionAsync(IsolationLevel.Serializable);
        for (var index = 1; index <= count; index++)
        {
            var identity = IdentityHmac($"{scenario}|{index:D4}");
            var oldName = $"SYNTHETIC {scenario} OLD {index:D4}";
            var desiredName = $"SYNTHETIC {scenario} NEW {index:D4}";
            var oldMapped = QlhvDirectRealtimeHash.Sha256(
                $"RT02B2|SOURCE|{identity}|{oldName}");
            var sourceHash = QlhvDirectRealtimeHash.Sha256(
                $"RT02B2|SOURCE|{identity}|{desiredName}");
            var qlhvHash = QlhvDirectRealtimeHash.Sha256(
                $"RT02B2|QLHV|{identity}|READY|NOTES|PHOTO_DISABLED");

            await using (var sourceCommand = new SqlCommand("""
INSERT dbo.NguoiLX
(
    IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash
)
VALUES
(
    @IdentityHmac, @ScenarioCode, 'STALE_IMPORTED_VALUE',
    @HoTen, @SourceRowHash
);
INSERT dbo.NguoiLX_HoSo(IdentityHmac, PayloadHash)
VALUES(@IdentityHmac, @PayloadHash);
""", source, sourceTransaction))
            {
                AddIdentity(sourceCommand, identity);
                sourceCommand.Parameters.Add("@ScenarioCode", SqlDbType.VarChar, 40)
                    .Value = scenario;
                sourceCommand.Parameters.Add("@HoTen", SqlDbType.NVarChar, 200)
                    .Value = desiredName;
                sourceCommand.Parameters.Add("@SourceRowHash", SqlDbType.Char, 64)
                    .Value = sourceHash;
                sourceCommand.Parameters.Add("@PayloadHash", SqlDbType.Char, 64)
                    .Value = QlhvDirectRealtimeHash.Sha256(
                        $"RT02B2|HOSO|{identity}");
                Assert.Equal(2, await sourceCommand.ExecuteNonQueryAsync());
            }

            await using (var targetCommand = new SqlCommand("""
INSERT dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @IdentityHmac, @SourceProfile, @ScenarioCode,
    'STALE_IMPORTED_VALUE', @OldName, @OldMappedHash,
    @QlhvOwnedHash, 'READY', @NotesHash, 'PHOTO_DISABLED', 1, 0
);
""", target, targetTransaction))
            {
                AddIdentity(targetCommand, identity);
                targetCommand.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20)
                    .Value = sourceProfile;
                targetCommand.Parameters.Add("@ScenarioCode", SqlDbType.VarChar, 40)
                    .Value = scenario;
                targetCommand.Parameters.Add("@OldName", SqlDbType.NVarChar, 200)
                    .Value = oldName;
                targetCommand.Parameters.Add("@OldMappedHash", SqlDbType.Char, 64)
                    .Value = oldMapped;
                targetCommand.Parameters.Add("@QlhvOwnedHash", SqlDbType.Char, 64)
                    .Value = qlhvHash;
                targetCommand.Parameters.Add("@NotesHash", SqlDbType.Char, 64)
                    .Value = QlhvDirectRealtimeHash.Sha256(
                        $"RT02B2|NOTES|{identity}");
                Assert.Equal(1, await targetCommand.ExecuteNonQueryAsync());
            }

            operations.Add(new QlhvDirectRealtimeApplyOperation(
                $"OP-{scenario}-UPDATE-{index:D4}",
                QlhvDirectRealtimeApplyOperationKind.Update,
                QlhvDirectRealtimeDispositions.StaleImportedValue,
                identity,
                sourceHash,
                oldMapped,
                qlhvHash,
                ["HoTen"],
                desiredName));
        }

        await sourceTransaction.CommitAsync();
        await targetTransaction.CommitAsync();
        return operations;
    }

    private static async Task<QlhvDirectRealtimeApplyOperation>
        SeedRetainOperationAsync(
            string sourceProfile,
            string scenario)
    {
        var identity = IdentityHmac($"{scenario}|0001");
        var mappedHash = QlhvDirectRealtimeHash.Sha256(
            $"RT02B2|TARGETONLY|{identity}");
        var qlhvHash = QlhvDirectRealtimeHash.Sha256(
            $"RT02B2|QLHV|{identity}|READY|NOTES|PHOTO_DISABLED");
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @IdentityHmac, @SourceProfile, @ScenarioCode, 'SOURCE_ROW_REMOVED',
    N'SYNTHETIC TARGET ONLY', @MappedHash, @QlhvOwnedHash, 'READY',
    @NotesHash, 'PHOTO_DISABLED', 1, 0
);
""";
        AddIdentity(command, identity);
        command.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
            sourceProfile;
        command.Parameters.Add("@ScenarioCode", SqlDbType.VarChar, 40).Value =
            scenario;
        command.Parameters.Add("@MappedHash", SqlDbType.Char, 64).Value =
            mappedHash;
        command.Parameters.Add("@QlhvOwnedHash", SqlDbType.Char, 64).Value =
            qlhvHash;
        command.Parameters.Add("@NotesHash", SqlDbType.Char, 64).Value =
            QlhvDirectRealtimeHash.Sha256($"RT02B2|NOTES|{identity}");
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return new QlhvDirectRealtimeApplyOperation(
            $"OP-{scenario}-RETAIN",
            QlhvDirectRealtimeApplyOperationKind.RetainForManualReview,
            QlhvDirectRealtimeDispositions.ManualReviewRequired,
            identity,
            string.Empty,
            mappedHash,
            qlhvHash,
            []);
    }

    private static async Task ChangeSourceHashAsync(
        string identity,
        string changedHash)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.OtoDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE dbo.NguoiLX
SET SourceRowHash = @ChangedHash
WHERE IdentityHmac = @IdentityHmac;
""";
        AddIdentity(command, identity);
        command.Parameters.Add("@ChangedHash", SqlDbType.Char, 64).Value =
            changedHash;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<IReadOnlyList<QlhvDirectRealtimeDatabaseIdentity>>
        LoadIdentitiesAsync()
    {
        return
        [
            await LoadIdentityAsync(
                "OTO",
                Rt02b2SqlRoute.OtoDatabase,
                5,
                Guid.Parse("FEE7CD94-A717-4E73-89F0-0FBFF71D1789")),
            await LoadIdentityAsync(
                "MOTO",
                Rt02b2SqlRoute.MotoDatabase,
                6,
                Guid.Parse("6D8101F9-07AB-4F0F-B378-29ED084F7B2A")),
            await LoadIdentityAsync(
                "TARGET",
                Rt02b2SqlRoute.TargetDatabase,
                7,
                Guid.Parse("F7BAC56F-8329-47AB-A17C-A0D592ADD484")),
        ];
    }

    private static async Task<QlhvDirectRealtimeDatabaseIdentity>
        LoadIdentityAsync(
            string role,
            string database,
            int expectedId,
            Guid expectedGuid)
    {
        await using var connection = await OpenAsync(database);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    DB_NAME(),
    CONVERT(int, DB_ID()),
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')),
    recovery.database_guid,
    databaseItem.state_desc,
    databaseItem.is_read_only,
    databaseItem.recovery_model_desc,
    databaseItem.source_database_id,
    CONVERT(nvarchar(128),
        (
            SELECT value FROM sys.extended_properties
            WHERE class = 0 AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value FROM sys.extended_properties
            WHERE class = 0 AND name = N'RT02_OWNER_APPROVAL_ID'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value FROM sys.extended_properties
            WHERE class = 0 AND name = N'RT02_DATASET_MODE'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value FROM sys.extended_properties
            WHERE class = 0 AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
        )),
    CONVERT(nvarchar(128),
        (
            SELECT value FROM sys.extended_properties
            WHERE class = 0 AND name = N'RT02_EXPIRES_AT_UTC'
        ))
FROM sys.databases AS databaseItem
INNER JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
WHERE databaseItem.database_id = DB_ID();
""";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(database, reader.GetString(0));
        Assert.Equal(expectedId, reader.GetInt32(1));
        Assert.Equal(Rt02b2SqlRoute.ServerIdentity, reader.GetString(2));
        Assert.Equal(expectedGuid, reader.GetGuid(3));
        Assert.Equal("ONLINE", reader.GetString(4));
        Assert.False(reader.GetBoolean(5));
        Assert.True(reader.IsDBNull(7));
        Assert.Equal(Rt02b2SqlRoute.EnvironmentId, reader.GetString(8));
        Assert.Equal(Rt02b2SqlRoute.ApprovalId, reader.GetString(9));
        Assert.Equal("SYNTHETIC", reader.GetString(10));
        Assert.Equal("FALSE", reader.GetString(11));
        Assert.Equal(Rt02b2SqlRoute.ExpiryUtc, reader.GetString(12));
        return new QlhvDirectRealtimeDatabaseIdentity(
            role,
            database,
            database,
            expectedId,
            Rt02b2SqlRoute.ServerIdentity,
            expectedGuid,
            IsReadWrite: true,
            RecoveryModel: reader.GetString(6),
            ConnectionRoute: $"SharedMemory/{Rt02b2SqlRoute.ServerIdentity}/{database}",
            EnvironmentMarker: Rt02b2SqlRoute.EnvironmentId,
            IsAliasOfProduction: false,
            MatchesProductionIdentity: false);
    }

    private static QlhvDirectRealtimeIsolatedEnvironment BuildEnvironment(
        Rt02b2EnvironmentState state)
        => new(
            Rt02b2SqlRoute.OtoDatabase,
            Rt02b2SqlRoute.MotoDatabase,
            Rt02b2SqlRoute.TargetDatabase,
            Rt02b2SqlRoute.ServerIdentity,
            Rt02b2SqlRoute.EnvironmentId,
            state.DatasetFingerprint,
            "SYNTHETIC:RT02B2-SQL-V1",
            state.CreatedAtUtc,
            DateTime.Parse(
                Rt02b2SqlRoute.ExpiryUtc,
                null,
                System.Globalization.DateTimeStyles.AdjustToUniversal),
            Rt02b2SqlRoute.ApprovalId);

    private static async Task<Rt02b2EnvironmentState>
        LoadEnvironmentStateAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    DatasetFingerprint,
    MappingFingerprint,
    SourceSchemaFingerprint,
    TargetSchemaFingerprint,
    IdentityNormalizationVersion,
    DatasetMode,
    PiiRows,
    CreatedAtUtc
FROM dbo.Rt02EnvironmentState
WHERE EnvironmentId = @EnvironmentId;
""";
        command.Parameters.Add("@EnvironmentId", SqlDbType.VarChar, 128).Value =
            Rt02b2SqlRoute.EnvironmentId;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("SYNTHETIC", reader.GetString(5));
        Assert.Equal(0, reader.GetInt32(6));
        return new Rt02b2EnvironmentState(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc));
    }

    private static async Task AssertMetadataGatesAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    (SELECT COUNT_BIG(*) FROM sys.servers WHERE is_linked = 1),
    (SELECT is_read_committed_snapshot_on
        FROM sys.databases WHERE name = N'QLHV_RT02_OTO_TEST'),
    (SELECT is_read_committed_snapshot_on
        FROM sys.databases WHERE name = N'QLHV_RT02_MOTO_TEST'),
    (SELECT is_read_committed_snapshot_on
        FROM sys.databases WHERE name = N'QLHV_RT02_TARGET_TEST'),
    (SELECT snapshot_isolation_state
        FROM sys.databases WHERE name = N'QLHV_RT02_OTO_TEST'),
    (SELECT snapshot_isolation_state
        FROM sys.databases WHERE name = N'QLHV_RT02_MOTO_TEST'),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables),
    (SELECT COUNT_BIG(*) FROM sys.synonyms);
""";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.False(reader.GetBoolean(1));
        Assert.False(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.Equal(1, reader.GetByte(4));
        Assert.Equal(1, reader.GetByte(5));
        Assert.Equal(2L, reader.GetInt64(6));
        Assert.Equal(2L, reader.GetInt64(7));
        Assert.Equal(0L, reader.GetInt64(8));
    }

    private static async Task<Rt02b2IntegritySnapshot>
        ReadIntegritySnapshotAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
WITH DuplicateActive AS
(
    SELECT SourceProfile, IdentityHmac
    FROM dbo.Rt02Learner
    WHERE Active = 1 AND SoftDeleted = 0
    GROUP BY SourceProfile, IdentityHmac
    HAVING COUNT_BIG(*) > 1
)
SELECT
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'NO_CHANGE'),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'SOURCE_ONLY_NEW_ROW'),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'STALE_IMPORTED_VALUE'),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'SOURCE_ROW_REMOVED'
          AND Active = 1 AND SoftDeleted = 0),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'SOFT_DELETED_BASELINE'
          AND Active = 0 AND SoftDeleted = 1),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'NO_CHANGE'),
    (SELECT COUNT_BIG(*) FROM DuplicateActive),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode <> 'CORE' AND (Active = 0 OR SoftDeleted = 1)),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE HoTen NOT LIKE N'SYNTHETIC %'),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyMarker),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyCheckpoint);
""";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var changeTrackingRows = await CountOtoChangeTrackingRowsAsync();
        return new Rt02b2IntegritySnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8),
            reader.GetInt64(9),
            reader.GetInt64(10),
            changeTrackingRows);
    }

    private static async Task<string> ReadCoreQlhvOwnedHashAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT QlhvOwnedHash, WorkflowState, NotesHash, PhotoState, Active, SoftDeleted
FROM dbo.Rt02Learner
WHERE ScenarioCode = 'CORE'
ORDER BY IdentityHmac;
""";
        await using var reader = await command.ExecuteReaderAsync();
        var parts = new List<string>();
        while (await reader.ReadAsync())
        {
            parts.Add(
                $"{reader.GetString(0)}|{reader.GetString(1)}|" +
                $"{reader.GetString(2)}|{reader.GetString(3)}|" +
                $"{reader.GetBoolean(4)}|{reader.GetBoolean(5)}");
        }

        return QlhvDirectRealtimeHash.Sha256(string.Join(";", parts));
    }

    private static async Task<string> ReadQlhvOwnedTupleHashAsync(
        string identity)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT QlhvOwnedHash, WorkflowState, NotesHash, PhotoState, Active, SoftDeleted
FROM dbo.Rt02Learner
WHERE IdentityHmac = @IdentityHmac;
""";
        AddIdentity(command, identity);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return QlhvDirectRealtimeHash.Sha256(
            $"{reader.GetString(0)}|{reader.GetString(1)}|" +
            $"{reader.GetString(2)}|{reader.GetString(3)}|" +
            $"{reader.GetBoolean(4)}|{reader.GetBoolean(5)}");
    }

    private static async Task<string> ReadProfileQlhvOwnedHashAsync(
        string profile)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT IdentityHmac, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState
FROM dbo.Rt02Learner
WHERE SourceProfile = @SourceProfile
ORDER BY IdentityHmac;
""";
        command.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
            profile;
        await using var reader = await command.ExecuteReaderAsync();
        var parts = new List<string>();
        while (await reader.ReadAsync())
        {
            parts.Add(
                $"{reader.GetString(0)}|{reader.GetString(1)}|" +
                $"{reader.GetString(2)}|{reader.GetString(3)}|" +
                $"{reader.GetString(4)}");
        }

        return QlhvDirectRealtimeHash.Sha256(string.Join(";", parts));
    }

    private static async Task<bool> CountProfileQlhvOwnedPrefixPreservedAsync(
        string profile,
        string beforeHash)
    {
        var current = await ReadProfileQlhvOwnedHashAsync(profile);
        if (string.Equals(current, beforeHash, StringComparison.Ordinal))
        {
            return true;
        }

        // Mixed load adds new insert rows during apply. Existing update/retain
        // rows are checked independently by excluding rows created by apply.
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT IdentityHmac, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState
FROM dbo.Rt02Learner
WHERE SourceProfile = @SourceProfile
  AND DatasetRole <> 'SOURCE_ONLY_NEW_ROW'
ORDER BY IdentityHmac;
""";
        command.Parameters.Add("@SourceProfile", SqlDbType.VarChar, 20).Value =
            profile;
        await using var reader = await command.ExecuteReaderAsync();
        var parts = new List<string>();
        while (await reader.ReadAsync())
        {
            parts.Add(
                $"{reader.GetString(0)}|{reader.GetString(1)}|" +
                $"{reader.GetString(2)}|{reader.GetString(3)}|" +
                $"{reader.GetString(4)}");
        }

        return parts.Count == 500 &&
            string.Equals(
                QlhvDirectRealtimeHash.Sha256(string.Join(";", parts)),
                beforeHash,
                StringComparison.Ordinal);
    }

    private static async Task<long> CountOtoChangeTrackingRowsAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.OtoDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COUNT_BIG(*)
FROM CHANGETABLE(CHANGES dbo.NguoiLX, 0) AS changes;
""";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadMappedHashAsync(string identity)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT MappedHash
FROM dbo.Rt02Learner
WHERE IdentityHmac = @IdentityHmac;
""";
        AddIdentity(command, identity);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountTargetIdentityAsync(string identity)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT COUNT_BIG(*)
FROM dbo.Rt02Learner
WHERE IdentityHmac = @IdentityHmac;
""";
        AddIdentity(command, identity);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static Task<long> CountTargetRoleAsync(string role)
        => CountScalarAsync(
            """
SELECT COUNT_BIG(*)
FROM dbo.Rt02Learner
WHERE DatasetRole = @Value;
""",
            role);

    private static Task<long> CountCoreUpdatedNameAsync()
        => CountScalarAsync(
            """
SELECT COUNT_BIG(*)
FROM dbo.Rt02Learner
WHERE ScenarioCode = 'CORE'
  AND DatasetRole = 'STALE_IMPORTED_VALUE'
  AND HoTen = N'SYNTHETIC OTO UPDATED'
  AND Active = 1
  AND SoftDeleted = 0;
""",
            null);

    private static Task<long> CountCoreRetainedActiveAsync()
        => CountScalarAsync(
            """
SELECT COUNT_BIG(*)
FROM dbo.Rt02Learner
WHERE ScenarioCode = 'CORE'
  AND DatasetRole = 'SOURCE_ROW_REMOVED'
  AND Active = 1
  AND SoftDeleted = 0;
""",
            null);

    private static Task<long> CountManualReviewAsync(string cycleId)
        => CountScalarAsync(
            """
SELECT COUNT_BIG(*)
FROM dbo.Rt02ManualReviewEvidence
WHERE CycleId = @Value
  AND Disposition = 'MANUAL_REVIEW_REQUIRED'
  AND TargetRetainedActive = 1
  AND TargetMutated = 0;
""",
            cycleId);

    private static Task<long> CountAllManualReviewAsync()
        => CountScalarAsync(
            "SELECT COUNT_BIG(*) FROM dbo.Rt02ManualReviewEvidence;",
            null);

    private static Task<long> CountMarkerAsync(string cycleId)
        => CountScalarAsync(
            "SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyMarker WHERE CycleId = @Value;",
            cycleId);

    private static Task<long> CountCheckpointAsync(string profile)
        => CountScalarAsync(
            """
SELECT COUNT_BIG(*)
FROM dbo.Rt02ApplyCheckpoint
WHERE SourceProfile = @Value
  AND Mode = 'DIRECT_REALTIME_APPLY';
""",
            profile);

    private static async Task SeedApplyMarkerAsync(
        QlhvDirectRealtimeApplyMarker marker)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
INSERT dbo.Rt02ApplyMarker
(
    CycleId, PlanHash, DispositionHash, InsertedRows, UpdatedRows,
    RetainedRows, PreservedQlhvOwnedHash, CommittedAtUtc
)
VALUES
(
    @CycleId, @PlanHash, @DispositionHash, @InsertedRows, @UpdatedRows,
    @RetainedRows, @PreservedQlhvOwnedHash, @CommittedAtUtc
);
""";
        command.Parameters.Add("@CycleId", SqlDbType.VarChar, 120).Value =
            marker.CycleId;
        command.Parameters.Add("@PlanHash", SqlDbType.Char, 64).Value =
            marker.PlanHash;
        command.Parameters.Add("@DispositionHash", SqlDbType.Char, 64).Value =
            marker.DispositionHash;
        command.Parameters.Add("@InsertedRows", SqlDbType.Int).Value =
            marker.InsertedRows;
        command.Parameters.Add("@UpdatedRows", SqlDbType.Int).Value =
            marker.UpdatedRows;
        command.Parameters.Add("@RetainedRows", SqlDbType.Int).Value =
            marker.RetainedRows;
        command.Parameters.Add("@PreservedQlhvOwnedHash", SqlDbType.Char, 64).Value =
            marker.PreservedQlhvOwnedHash;
        command.Parameters.Add("@CommittedAtUtc", SqlDbType.DateTime2).Value =
            marker.CommittedAtUtc;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertMarkerCheckpointConsistencyAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    CycleId,
    PlanHash,
    DispositionHash,
    InsertedRows,
    UpdatedRows,
    RetainedRows,
    PreservedQlhvOwnedHash,
    CommittedAtUtc
FROM dbo.Rt02ApplyMarker
ORDER BY CycleId;

SELECT CycleId, PlanHash, MarkerHash
FROM dbo.Rt02ApplyCheckpoint
ORDER BY CycleId;
""";
        var markers = new Dictionary<
            string,
            (string PlanHash, string MarkerHash)>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var marker = new QlhvDirectRealtimeApplyMarker(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetDateTime(7));
            Assert.True(markers.TryAdd(
                marker.CycleId,
                (marker.PlanHash, marker.MarkerHash)));
        }

        Assert.True(await reader.NextResultAsync());
        var checkpointCycles = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            var cycleId = reader.GetString(0);
            Assert.True(checkpointCycles.Add(cycleId));
            Assert.True(markers.TryGetValue(cycleId, out var marker));
            Assert.Equal(marker.PlanHash, reader.GetString(1));
            Assert.Equal(marker.MarkerHash, reader.GetString(2));
        }

        Assert.Equal(markers.Count, checkpointCycles.Count);
        Assert.Equal(
            markers.Keys.Order(StringComparer.Ordinal),
            checkpointCycles.Order(StringComparer.Ordinal));
    }

    private static async Task AssertBusinessRowTotalsAsync(
        long otoRows,
        long otoProfileRows,
        long motoRows,
        long motoProfileRows,
        long targetRows,
        long activeTargetRows,
        long softDeletedTargetRows)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX_HoSo),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX),
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX_HoSo),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE Active = 1 AND SoftDeleted = 0),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE Active = 0 AND SoftDeleted = 1);
""";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(otoRows, reader.GetInt64(0));
        Assert.Equal(otoProfileRows, reader.GetInt64(1));
        Assert.Equal(motoRows, reader.GetInt64(2));
        Assert.Equal(motoProfileRows, reader.GetInt64(3));
        Assert.Equal(targetRows, reader.GetInt64(4));
        Assert.Equal(activeTargetRows, reader.GetInt64(5));
        Assert.Equal(softDeletedTargetRows, reader.GetInt64(6));
    }

    private static async Task<long> CountScalarAsync(
        string sql,
        string? value)
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (value is not null)
        {
            command.Parameters.Add("@Value", SqlDbType.VarChar, 120).Value =
                value;
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<TimeSpan> RunControlledProcessHelperAsync(
        string resultsPath,
        string artifactName,
        string mode,
        Rt02b2ProcessHelperInput input)
    {
        var artifactPrefix = $"{resultsPath}.{artifactName}";
        var inputPath = $"{artifactPrefix}.input.json";
        var signalPath = $"{artifactPrefix}.signal.txt";
        var stdoutPath = $"{artifactPrefix}.stdout.log";
        var stderrPath = $"{artifactPrefix}.stderr.log";
        foreach (var path in new[]
                 {
                     inputPath,
                     signalPath,
                     $"{signalPath}.tmp",
                     stdoutPath,
                     stderrPath,
                 })
        {
            Assert.False(
                File.Exists(path),
                $"Controlled-process evidence already exists: {path}");
        }

        await WriteJsonEvidenceAsync(inputPath, input);
        var projectPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "QLHV.Tests.csproj"));
        Assert.True(File.Exists(projectPath));
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "test",
                     projectPath,
                     "-c",
                     "Release",
                     "--no-build",
                     "--filter",
                     "FullyQualifiedName=" +
                     "QLHV.Tests.Sync.Rt02.Rt02b2AuthorizedSqlExecutionTests." +
                     "Controlled_process_termination_helper",
                     "--logger",
                     "console;verbosity=minimal",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[ProcessHelperTokenVariable] = ProcessHelperToken;
        startInfo.Environment[ProcessHelperInputVariable] = inputPath;
        startInfo.Environment[ProcessHelperSignalVariable] = signalPath;
        startInfo.Environment[ProcessHelperModeVariable] = mode;
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process
        {
            StartInfo = startInfo,
        };
        var timer = Stopwatch.StartNew();
        Assert.True(process.Start());
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Exception? failure = null;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(signalPath))
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        "The controlled-process helper exited before signaling. " +
                        $"ExitCode={process.ExitCode}.");
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        "The controlled-process helper did not signal within 30 seconds.");
                }

                await Task.Delay(100);
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            timer.Stop();
            await File.WriteAllTextAsync(stdoutPath, await stdoutTask);
            await File.WriteAllTextAsync(stderrPath, await stderrTask);
        }

        if (failure is not null)
        {
            throw failure;
        }

        Assert.True(File.Exists(signalPath));
        var signal = await File.ReadAllTextAsync(signalPath);
        Assert.Contains(
            $"Mode={mode}",
            signal,
            StringComparison.Ordinal);
        if (string.Equals(mode, ProcessHelperAfterCommit, StringComparison.Ordinal))
        {
            Assert.Contains(
                $"CycleId={input.Plan.CycleId}",
                signal,
                StringComparison.Ordinal);
        }

        return timer.Elapsed;
    }

    private static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail(failureMessage);
    }

    private static async Task RecordScenarioAsync(
        string resultsPath,
        DateTime startedAtUtc,
        List<Rt02b2ScenarioResult> scenarios,
        Rt02b2ScenarioResult scenario)
    {
        var evidenceCounts = await ReadScenarioEvidenceCountsAsync();
        Assert.Equal(0, evidenceCounts.DuplicateActiveCount);
        Assert.Equal(evidenceCounts.MarkerCount, evidenceCounts.CheckpointCount);
        scenario = scenario with
        {
            DuplicateActiveCount = evidenceCounts.DuplicateActiveCount,
            MarkerCount = evidenceCounts.MarkerCount,
            CheckpointCount = evidenceCounts.CheckpointCount,
            ManualReviewCount = evidenceCounts.ManualReviewCount,
        };
        scenarios.Add(scenario);
        await WriteProgressEvidenceAsync(
            resultsPath,
            startedAtUtc,
            scenarios,
            "RUNNING",
            scenario.Name);
    }

    private static async Task<Rt02b2ScenarioEvidenceCounts>
        ReadScenarioEvidenceCountsAsync()
    {
        await using var connection = await OpenAsync(Rt02b2SqlRoute.TargetDatabase);
        await using var command = connection.CreateCommand();
        command.CommandText = """
WITH DuplicateActive AS
(
    SELECT SourceProfile, IdentityHmac
    FROM dbo.Rt02Learner
    WHERE Active = 1 AND SoftDeleted = 0
    GROUP BY SourceProfile, IdentityHmac
    HAVING COUNT_BIG(*) > 1
)
SELECT
    (SELECT COUNT_BIG(*) FROM DuplicateActive),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyMarker),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyCheckpoint),
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ManualReviewEvidence);
""";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Rt02b2ScenarioEvidenceCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static Task WriteProgressEvidenceAsync(
        string resultsPath,
        DateTime startedAtUtc,
        IReadOnlyList<Rt02b2ScenarioResult> scenarios,
        string status,
        string currentStep,
        Exception? error = null)
        => WriteJsonEvidenceAsync(
            resultsPath,
            new Rt02b2ProgressEvidence(
                status,
                Rt02b2SqlRoute.EnvironmentId,
                Rt02b2SqlRoute.ApprovalId,
                startedAtUtc,
                DateTime.UtcNow,
                currentStep,
                error?.GetType().FullName,
                error?.Message,
                EvidenceLimitations,
                scenarios));

    private static async Task WriteJsonEvidenceAsync(
        string resultsPath,
        object evidence)
    {
        var json = JsonSerializer.Serialize(
            evidence,
            evidence.GetType(),
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        var temporaryPath = $"{resultsPath}.tmp";
        await File.WriteAllTextAsync(temporaryPath, json);
        File.Move(temporaryPath, resultsPath, overwrite: true);
    }

    private static string IdentityHmac(string value)
        => QlhvDirectRealtimeHash.KeyedDiagnosticHmac(
            "RT02B2-SYNTHETIC-ONLY-KEY",
            "RT02B2-ISOLATED-HARNESS",
            "V1",
            value);

    private static async Task<SqlConnection> OpenAsync(string database)
        => await Rt02b2SqlRoute.OpenConnectionAsync(database);

    private static void AddIdentity(SqlCommand command, string identity)
        => command.Parameters.Add("@IdentityHmac", SqlDbType.Char, 64).Value =
            identity;

    private sealed class Rt02BlockAfterCommitProcessTerminationFaultInjector :
        IQlhvDirectRealtimeFaultInjector
    {
        private readonly string _signalPath;

        public Rt02BlockAfterCommitProcessTerminationFaultInjector(
            string signalPath)
        {
            _signalPath = signalPath;
        }

        public Task AfterTargetCommitAsync(
            QlhvDirectRealtimeApplyMarker marker,
            CancellationToken cancellationToken)
            => Rt02ProcessTerminationSignal.WriteAndBlockAsync(
                _signalPath,
                ProcessHelperAfterCommit,
                marker);
    }

    private sealed record Rt02b2ProcessHelperInput(
        string SourceDatabase,
        QlhvDirectRealtimeApplyPlan Plan);

    private sealed record Rt02b2EnvironmentState(
        string DatasetFingerprint,
        string MappingFingerprint,
        string SourceSchemaFingerprint,
        string TargetSchemaFingerprint,
        string IdentityNormalizationVersion,
        DateTime CreatedAtUtc);

    private sealed record Rt02b2ScenarioEvidenceCounts(
        long DuplicateActiveCount,
        long MarkerCount,
        long CheckpointCount,
        long ManualReviewCount);
}

internal sealed record Rt02b2ExecutionResult(
    string Status,
    string EnvironmentId,
    string ApprovalId,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string ServerIdentity,
    IReadOnlyList<Rt02b2DatabaseResult> DatabaseIdentities,
    string DatasetFingerprint,
    string MappingFingerprint,
    string SourceSchemaFingerprint,
    string TargetSchemaFingerprint,
    string IdentityNormalizationVersion,
    IReadOnlyList<string> EvidenceLimitations,
    Rt02b2IntegritySnapshot Before,
    Rt02b2IntegritySnapshot After,
    string CoreQlhvOwnedHashBefore,
    string CoreQlhvOwnedHashAfter,
    IReadOnlyList<Rt02b2ScenarioResult> Scenarios);

internal sealed record Rt02b2ProgressEvidence(
    string Status,
    string EnvironmentId,
    string ApprovalId,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc,
    string CurrentStep,
    string? ErrorType,
    string? ErrorMessage,
    IReadOnlyList<string> EvidenceLimitations,
    IReadOnlyList<Rt02b2ScenarioResult> Scenarios);

internal sealed record Rt02b2DatabaseResult(
    string Role,
    string DatabaseName,
    int DatabaseId,
    Guid DatabaseGuid,
    string RecoveryModel,
    bool IsReadWrite);

internal sealed record Rt02b2IntegritySnapshot(
    long OtoNoChange,
    long OtoInsertCandidates,
    long OtoUpdateCandidates,
    long OtoTargetOnlyActive,
    long OtoSoftDeletedBaseline,
    long MotoNoChange,
    long DuplicateActiveGroups,
    long NonCoreInactiveOrDeletedRows,
    long PiiLikeRows,
    long MarkerCount,
    long CheckpointCount,
    long ChangeTrackingRows);

internal sealed record Rt02b2ScenarioResult(
    string Name,
    string Status,
    string? ErrorCode,
    double CycleDurationMs,
    double TransactionDurationMs,
    double RowsPerSecond,
    int QueryCount,
    int RetryCount,
    long MemoryDeltaBytes,
    bool RollbackSucceeded,
    int InsertedRows,
    int UpdatedRows,
    int RetainedRows,
    bool TransactionCommitted,
    bool CheckpointPublished,
    int SecondSessionWriteCount = 0,
    long DuplicateActiveCount = 0,
    long MarkerCount = 0,
    long CheckpointCount = 0,
    long ManualReviewCount = 0,
    string? ExternalEvidencePrefix = null);
