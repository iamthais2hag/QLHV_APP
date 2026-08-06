using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QLHV.Application;
using QLHV.Application.Sync.Rt03;
using QLHV.Infrastructure;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03ProductionSafetyTests
{
    [Fact]
    public void Production_identity_accepts_only_the_pinned_live_to_qlhv_route()
    {
        Rt03ProductionIdentityValidator.Validate(ObservedIdentities());

        foreach (var mutation in new Func<List<Rt03ObservedDatabase>, List<Rt03ObservedDatabase>>[]
        {
            rows => Replace(rows, "SOURCE_OTO", item => item with { DatabaseId = 99 }),
            rows => Replace(rows, "SOURCE_MOTO", item => item with { DatabaseGuid = Guid.NewGuid() }),
            rows => Replace(rows, "TARGET", item => item with { ServerIdentity = "OTHER" }),
            rows => Replace(rows, "SOURCE_OTO", item => item with
            {
                ActualDatabaseName = "CSDL_OTO_BAK",
                ConnectionRoute = "Server=CSDLTTTC;Database=CSDL_OTO_BAK",
            }),
            rows => Replace(rows, "SOURCE_MOTO", item => item with { IsAlias = true }),
            rows => Replace(rows, "TARGET", item => item with { IsLinkedOrExternal = true }),
        })
        {
            var error = Assert.Throws<Rt03SafetyException>(() =>
                Rt03ProductionIdentityValidator.Validate(mutation(ObservedIdentities())));
            Assert.Equal(Rt03Errors.ProductionIdentityRejected, error.Code);
        }
    }

    [Fact]
    public void Observation_only_requires_an_empty_allowlist_and_no_invented_checkpoint()
    {
        Rt03CanaryPlanValidator.Validate(Plan(Rt03Modes.ObservationOnly, []));

        var error = Assert.Throws<Rt03SafetyException>(() =>
            Rt03CanaryPlanValidator.Validate(Plan(
                Rt03Modes.ObservationOnly,
                [Candidate("OTO-I", Rt03Profiles.Oto, Rt03CandidateKind.Insert)])));
        Assert.Equal(Rt03Errors.AllowlistRejected, error.Code);

        error = Assert.Throws<Rt03SafetyException>(() =>
            Rt03CanaryPlanValidator.Validate(Plan(
                Rt03Modes.ObservationOnly,
                [],
                otoVersion: 1)));
        Assert.Equal(Rt03Errors.AllowlistRejected, error.Code);
    }

    [Fact]
    public void Canary_allowlist_is_exact_bounded_and_contains_no_wildcard()
    {
        var valid = Plan(Rt03Modes.Canary,
        [
            Candidate("OTO-I", Rt03Profiles.Oto, Rt03CandidateKind.Insert),
            Candidate("OTO-U", Rt03Profiles.Oto, Rt03CandidateKind.UpdateSourceOwnedFields),
            Candidate("OTO-R", Rt03Profiles.Oto, Rt03CandidateKind.RetainForManualReview),
        ]);
        Rt03CanaryPlanValidator.Validate(valid);

        var tooMany = valid with
        {
            Candidates =
            [
                .. valid.Candidates,
                Candidate("OTO-I-2", Rt03Profiles.Oto, Rt03CandidateKind.Insert),
            ],
        };
        Assert.Equal(Rt03Errors.AllowlistRejected,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03CanaryPlanValidator.Validate(tooMany)).Code);

        var wildcard = valid with { PlanId = "RT03-*" };
        Assert.Equal(Rt03Errors.AllowlistRejected,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03CanaryPlanValidator.Validate(wildcard)).Code);
    }

    [Fact]
    public void Update_allowlist_rejects_every_field_except_HoTen()
    {
        var update = Candidate("OTO-U", Rt03Profiles.Oto,
            Rt03CandidateKind.UpdateSourceOwnedFields);
        Rt03CanaryPlanValidator.Validate(Plan(Rt03Modes.Canary, [update]));

        foreach (var fields in new[]
        {
            new[] { "GhiChuNoiBo" },
            new[] { "HoTen", "DaInThe" },
            Array.Empty<string>(),
        })
        {
            var rejected = update with { RequestedFields = fields };
            Assert.Equal(Rt03Errors.AllowlistRejected,
                Assert.Throws<Rt03SafetyException>(() =>
                    Rt03CanaryPlanValidator.Validate(
                        Plan(Rt03Modes.Canary, [rejected]))).Code);
        }
    }

    [Fact]
    public void Moto_is_blocked_until_a_verified_oto_pass()
    {
        var moto = Candidate("MOTO-I", Rt03Profiles.Moto, Rt03CandidateKind.Insert);
        var blocked = Plan(Rt03Modes.Canary, [moto]);
        Assert.Equal(Rt03Errors.OtoMustPassFirst,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03CanaryPlanValidator.Validate(blocked)).Code);

        Rt03CanaryPlanValidator.Validate(blocked with { OtoCanaryResult = "PASSED" });
    }

    [Fact]
    public void Task1_flags_default_false_and_writer_is_not_registered()
    {
        var defaults = new Rt03ProductionOptions();
        Rt03ExecutionGate.ValidateTask1DisabledState(defaults, 0);
        Assert.True(defaults.ValidationOnly);
        Assert.False(defaults.EnableRt03ProductionRealtime);
        Assert.False(defaults.EnableRt03ProductionShadow);
        Assert.False(defaults.EnableRt03ProductionWrites);
        Assert.False(defaults.EnableRt03ProductionCanary);
        Assert.False(defaults.EnableRt03ControlledCutover);
        Assert.False(defaults.EnableRt03ProductionDeletes);

        var services = new ServiceCollection();
        var configuration = new ConfigurationManager();
        services.AddApplication();
        services.AddInfrastructureCore(configuration);
        services.AddCsdtRealtimeWorkerServices(configuration);
        services.AddRt03ProductionRealtimeWorkerServices(configuration);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IRt03ProductionRealtimeCycleProcessor) ||
            descriptor.ImplementationType == typeof(
                QLHV.Infrastructure.Sync.Rt03.Rt03ProductionRealtimeWorker));
    }

    [Fact]
    public void Active_autosync_polling_run_slot_operation_or_realtime_blocks_canary()
    {
        var plan = Plan(Rt03Modes.Canary,
            [Candidate("OTO-I", Rt03Profiles.Oto, Rt03CandidateKind.Insert)]);
        var options = EnabledCanaryOptions();
        var safe = new Rt03AutoSyncExclusionSnapshot(
            ConfigurationEnabled: false,
            PollingEnabled: false,
            IsPolling: false,
            ActiveRunRows: 0,
            ActiveSlotRows: 0,
            ActiveOperationRows: 0,
            ExistingAutoSyncGlobalLockAcquired: true,
            ExistingRealtimeRunActive: false);
        Rt03ExecutionGate.ValidateMutationCanary(
            options, plan, safe, Current(plan), EmptyCheckpoint());

        var unsafeStates = new[]
        {
            safe with { ConfigurationEnabled = true },
            safe with { PollingEnabled = true },
            safe with { IsPolling = true },
            safe with { ActiveRunRows = 1 },
            safe with { ActiveSlotRows = 1 },
            safe with { ActiveOperationRows = 1 },
            safe with { ExistingAutoSyncGlobalLockAcquired = false },
            safe with { ExistingRealtimeRunActive = true },
        };
        foreach (var state in unsafeStates)
        {
            Assert.Equal(Rt03Errors.AutoSyncActive,
                Assert.Throws<Rt03SafetyException>(() =>
                    Rt03ExecutionGate.ValidateMutationCanary(
                        options, plan, state, Current(plan), EmptyCheckpoint())).Code);
        }
    }

    [Fact]
    public void Source_schema_mapping_and_stage_drift_block_canary()
    {
        var plan = Plan(Rt03Modes.Canary,
            [Candidate("OTO-I", Rt03Profiles.Oto, Rt03CandidateKind.Insert)]);
        var current = Current(plan);
        var safe = SafeAutoSync();
        foreach (var drift in new[]
        {
            current with { MappingFingerprint = "changed" },
            current with { OtoSourceSchemaFingerprint = "changed" },
            current with { MotoSourceSchemaFingerprint = "changed" },
            current with { TargetSchemaFingerprint = "changed" },
            current with { OtoStageHash = "changed" },
            current with { MotoStageHash = "changed" },
        })
        {
            Assert.Equal(Rt03Errors.SourceDrift,
                Assert.Throws<Rt03SafetyException>(() =>
                    Rt03ExecutionGate.ValidateMutationCanary(
                        EnabledCanaryOptions(), plan, safe, drift, EmptyCheckpoint())).Code);
        }
    }

    [Fact]
    public void Target_qlhv_owned_duplicate_delete_and_deactivation_drift_block_canary()
    {
        var plan = Plan(Rt03Modes.Canary,
            [Candidate("OTO-U", Rt03Profiles.Oto,
                Rt03CandidateKind.UpdateSourceOwnedFields)]);
        var current = Current(plan);
        foreach (var drift in new[]
        {
            current with { OtoTargetComparisonHash = "changed" },
            current with { MotoTargetComparisonHash = "changed" },
            current with { DuplicateActiveTarget = true },
            current with { QlhvOwnedFieldDrift = true },
            current with { UnexpectedDeleteOrDeactivation = true },
        })
        {
            Assert.Equal(Rt03Errors.TargetDrift,
                Assert.Throws<Rt03SafetyException>(() =>
                    Rt03ExecutionGate.ValidateMutationCanary(
                        EnabledCanaryOptions(), plan, SafeAutoSync(), drift,
                        EmptyCheckpoint())).Code);
        }
    }

    [Fact]
    public void Conflicting_checkpoint_blocks_before_target_work()
    {
        var plan = Plan(Rt03Modes.Canary,
            [Candidate("OTO-I", Rt03Profiles.Oto, Rt03CandidateKind.Insert)]);
        var conflict = new Rt03CheckpointState(
            Exists: true,
            CycleId: "other",
            PlanHash: "other-plan",
            MarkerHash: "marker",
            SourceVersion: 11,
            ExpectedVersion: 10);

        Assert.Equal(Rt03Errors.CheckpointConflict,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03ExecutionGate.ValidateMutationCanary(
                    EnabledCanaryOptions(), plan, SafeAutoSync(), Current(plan),
                    conflict)).Code);
    }

    [Fact]
    public void Rollback_is_exact_and_cannot_escape_the_allowlist()
    {
        var candidates = new[]
        {
            Candidate("OTO-I", Rt03Profiles.Oto, Rt03CandidateKind.Insert),
            Candidate("OTO-U", Rt03Profiles.Oto, Rt03CandidateKind.UpdateSourceOwnedFields),
            Candidate("OTO-R", Rt03Profiles.Oto, Rt03CandidateKind.RetainForManualReview),
        };
        var plan = Plan(Rt03Modes.Canary, candidates);
        var actions = new[]
        {
            Rollback(candidates[0], Rt03RollbackKind.DeleteExactCanaryInsert, []),
            Rollback(candidates[1], Rt03RollbackKind.RestoreExactSourceOwnedFields, ["HoTen"]),
            Rollback(candidates[2], Rt03RollbackKind.NoMutationManualReview, []),
        };
        Rt03RollbackValidator.Validate(plan, actions);

        var outside = actions[0] with { CandidateId = "OUTSIDE" };
        Assert.Equal(Rt03Errors.RollbackRejected,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03RollbackValidator.Validate(plan, [outside, actions[1], actions[2]])).Code);
        var downstreamUse = actions[0] with { DownstreamReferenceCount = 1 };
        Assert.Equal(Rt03Errors.RollbackRejected,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03RollbackValidator.Validate(plan,
                    [downstreamUse, actions[1], actions[2]])).Code);
    }

    [Fact]
    public void Apply_sql_has_no_delete_deactivate_wildcard_or_dynamic_execution()
    {
        var apply = string.Join("\n", Rt03ProductionSql.ApplyCommands);
        Assert.DoesNotMatch(new Regex(@"\bDELETE\s+FROM\b",
            RegexOptions.IgnoreCase), apply);
        Assert.DoesNotMatch(new Regex(@"\bTRUNCATE\b|\bMERGE\b|\bDROP\b",
            RegexOptions.IgnoreCase), apply);
        Assert.DoesNotMatch(new Regex(@"\bSET\s+IsDeleted\b|\bSET\s+IsActive\b",
            RegexOptions.IgnoreCase), apply);
        Assert.DoesNotContain("sp_executesql", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC(", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIKE @", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET HoTen = @DesiredHoTen", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Rollback_sql_targets_exact_row_hash_and_zero_downstream_use()
    {
        Assert.Contains("HocVienId = @ExactInsertedHocVienId",
            Rt03ProductionSql.RollbackExactCanaryInsert, StringComparison.Ordinal);
        Assert.Contains("@DownstreamReferenceCount <> 0",
            Rt03ProductionSql.RollbackExactCanaryInsert, StringComparison.Ordinal);
        Assert.Contains("V2RowHash = @ExpectedCurrentSourceOwnedHash",
            Rt03ProductionSql.RollbackExactCanaryInsert, StringComparison.Ordinal);
        Assert.Contains("HocVienId = @TargetHocVienId",
            Rt03ProductionSql.RollbackExactHoTen, StringComparison.Ordinal);
        Assert.DoesNotContain("GhiChuNoiBo =", Rt03ProductionSql.RollbackExactHoTen,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Crash_before_transaction_stops_without_marker_or_checkpoint()
    {
        var disposition = Rt03RecoveryPlanner.Decide(new Rt03RecoverySnapshot(
            TransactionStarted: false,
            CommitConfirmed: false,
            CommitAmbiguous: false,
            MarkerExists: false,
            MarkerMatchesPlan: false,
            IntegrityVerified: false,
            CheckpointExists: false,
            CheckpointMatchesMarker: false));

        Assert.Equal(Rt03RecoveryDisposition.StopWithoutMutation, disposition);
    }

    [Fact]
    public void Crash_inside_uncommitted_transaction_requires_sql_rollback()
    {
        var disposition = Rt03RecoveryPlanner.Decide(new Rt03RecoverySnapshot(
            TransactionStarted: true,
            CommitConfirmed: false,
            CommitAmbiguous: false,
            MarkerExists: false,
            MarkerMatchesPlan: false,
            IntegrityVerified: false,
            CheckpointExists: false,
            CheckpointMatchesMarker: false));

        Assert.Equal(Rt03RecoveryDisposition.RollbackOpenTransaction, disposition);
    }

    [Fact]
    public void Crash_after_commit_recovers_checkpoint_only_from_verified_marker()
    {
        var verified = new Rt03RecoverySnapshot(
            TransactionStarted: true,
            CommitConfirmed: true,
            CommitAmbiguous: false,
            MarkerExists: true,
            MarkerMatchesPlan: true,
            IntegrityVerified: true,
            CheckpointExists: false,
            CheckpointMatchesMarker: false);

        Assert.Equal(Rt03RecoveryDisposition.VerifyMarkerThenPublishCheckpoint,
            Rt03RecoveryPlanner.Decide(verified));
        Assert.Equal(Rt03RecoveryDisposition.AlreadyCompleted,
            Rt03RecoveryPlanner.Decide(verified with
            {
                CheckpointExists = true,
                CheckpointMatchesMarker = true,
            }));
        Assert.Equal(Rt03RecoveryDisposition.BlockAsAmbiguous,
            Rt03RecoveryPlanner.Decide(verified with { CommitAmbiguous = true }));
        Assert.Equal(Rt03RecoveryDisposition.BlockAsAmbiguous,
            Rt03RecoveryPlanner.Decide(verified with { MarkerMatchesPlan = false }));
    }

    [Fact]
    public void Protected_development_configs_keep_the_handoff_hash()
    {
        var root = FindRepositoryRoot();
        const string expected =
            "12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E";
        foreach (var relative in new[]
        {
            @"server\QLHV.Api\appsettings.Development.json",
            @"server\QLHV.Worker\appsettings.Development.json",
        })
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, relative));
            Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }

    private static List<Rt03ObservedDatabase> ObservedIdentities()
        => Rt03ProductionCatalog.RequiredDatabases.Select(expected =>
            new Rt03ObservedDatabase(
                expected.Role,
                Rt03ProductionCatalog.ServerIdentity,
                expected.DatabaseName,
                expected.DatabaseName,
                expected.DatabaseId,
                expected.DatabaseGuid,
                $"Server=CSDLTTTC;Database={expected.DatabaseName}",
                IsOnline: true,
                IsAlias: false,
                IsLinkedOrExternal: false)).ToList();

    private static List<Rt03ObservedDatabase> Replace(
        List<Rt03ObservedDatabase> rows,
        string role,
        Func<Rt03ObservedDatabase, Rt03ObservedDatabase> replace)
        => rows.Select(row => row.Role == role ? replace(row) : row).ToList();

    private static Rt03CanaryCandidate Candidate(
        string id,
        string profile,
        Rt03CandidateKind kind)
        => new(
            id,
            profile,
            kind,
            Rt03Hash.DiagnosticHmac("test-secret-at-least-32-bytes-long", "identity", id),
            kind switch
            {
                Rt03CandidateKind.Insert => "SOURCE_ONLY_NEW_ROW",
                Rt03CandidateKind.UpdateSourceOwnedFields => "STALE_IMPORTED_VALUE",
                _ => "SOURCE_ROW_REMOVED",
            },
            "before-source",
            "before-qlhv",
            kind == Rt03CandidateKind.RetainForManualReview ? "NONE" : kind.ToString(),
            "QLHV_OWNED_UNCHANGED",
            "rollback-image",
            kind == Rt03CandidateKind.UpdateSourceOwnedFields ? ["HoTen"] : [],
            []);

    private static Rt03CanaryPlan Plan(
        string mode,
        IReadOnlyList<Rt03CanaryCandidate> candidates,
        long? otoVersion = null)
        => new(
            "RT03-PLAN-001",
            mode,
            "PRODUCTION",
            "mapping",
            "oto-schema",
            "moto-schema",
            "target-schema",
            "oto-stage",
            "moto-stage",
            "oto-target",
            "moto-target",
            otoVersion,
            null,
            "NOT_RUN",
            candidates);

    private static Rt03ProductionOptions EnabledCanaryOptions()
        => new()
        {
            ValidationOnly = false,
            EnableRt03ProductionRealtime = true,
            EnableRt03ProductionShadow = true,
            EnableRt03ProductionWrites = true,
            EnableRt03ProductionCanary = true,
            EnableRt03ControlledCutover = false,
            EnableRt03ProductionDeletes = false,
        };

    private static Rt03AutoSyncExclusionSnapshot SafeAutoSync()
        => new(false, false, false, 0, 0, 0, true, false);

    private static Rt03RevalidationSnapshot Current(Rt03CanaryPlan plan)
        => new(
            plan.MappingFingerprint,
            plan.OtoSourceSchemaFingerprint,
            plan.MotoSourceSchemaFingerprint,
            plan.TargetSchemaFingerprint,
            plan.OtoStageHash,
            plan.MotoStageHash,
            plan.OtoTargetComparisonHash,
            plan.MotoTargetComparisonHash,
            DuplicateActiveTarget: false,
            QlhvOwnedFieldDrift: false,
            UnexpectedDeleteOrDeactivation: false);

    private static Rt03CheckpointState EmptyCheckpoint()
        => new(false, null, null, null, null, null);

    private static Rt03RollbackAction Rollback(
        Rt03CanaryCandidate candidate,
        Rt03RollbackKind kind,
        IReadOnlyList<string> restoredFields)
        => new(
            candidate.CandidateId,
            candidate.SourceProfile,
            kind,
            candidate.IdentityHmac,
            candidate.RollbackImageHash,
            "expected-current-source",
            "expected-current-qlhv",
            restoredFields,
            DownstreamReferenceCount: 0,
            TargetStillInExactAllowlist: true);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "server", "QLHV.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate QLHV_APP.sln.");
    }
}
