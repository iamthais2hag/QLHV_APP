using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QLHV.Application;
using QLHV.Application.Sync.Rt03;
using QLHV.Infrastructure;
using QLHV.Infrastructure.Sync;
using QLHV.Infrastructure.Sync.Rt03;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03ProductionRegistrationTests
{
    [Fact]
    public void Default_source_configuration_does_not_register_writer_or_hosted_worker()
    {
        var configuration = Configuration(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructureCore(configuration);
        services.AddRt03ProductionRealtimeWorkerServices(configuration);

        Assert.DoesNotContain(services,
            item => item.ServiceType == typeof(IRt03ProductionRealtimeCycleProcessor));
        Assert.DoesNotContain(services,
            item => item.ServiceType == typeof(IHostedService) &&
                    item.ImplementationType == typeof(Rt03ProductionRealtimeWorker));
    }

    [Fact]
    public void Exact_controlled_cutover_configuration_registers_production_worker_once()
    {
        var configuration = Configuration(EnabledValues());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructureCore(configuration);
        services.AddRt03ProductionRealtimeWorkerServices(configuration);

        Assert.Single(services.Where(
            item => item.ServiceType == typeof(IRt03ProductionRealtimeCycleProcessor)));
        Assert.Single(services.Where(
            item => item.ServiceType == typeof(IHostedService) &&
                    item.ImplementationType == typeof(Rt03ProductionRealtimeWorker)));
        Assert.Single(services.Where(
            item => item.ServiceType == typeof(IQlhvDirectRealtimeGlobalLock)));
    }

    [Fact]
    public void Options_are_fail_closed_and_reject_delete_canary_or_moto_without_oto()
    {
        var validator = new Rt03ProductionOptionsValidator();
        Assert.True(validator.Validate(null, new Rt03ProductionOptions()).Succeeded);

        var valid = EnabledOptions();
        Assert.True(validator.Validate(null, valid).Succeeded);
        Assert.True(validator.Validate(null,
            Copy(valid, deletes: true)).Failed);
        Assert.True(validator.Validate(null,
            Copy(valid, canary: true)).Failed);
        Assert.True(validator.Validate(null,
            Copy(valid, oto: false, moto: true)).Failed);
    }

    [Fact]
    public void Startup_guard_rejects_wrong_identity_fingerprint_autosync_and_ct_window()
    {
        var options = EnabledOptions();
        var identities = ExactIdentities();
        var fingerprints = ExactFingerprints(options);
        var capabilities = ExactCapabilities();
        var exclusion = new Rt03AutoSyncExclusionSnapshot(
            false, false, false, 0, 0, 0, true, false);
        Rt03ProductionStartupGuard.Validate(
            options, identities, fingerprints, capabilities, exclusion);

        Assert.Equal(Rt03Errors.ProductionIdentityRejected,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03ProductionStartupGuard.Validate(
                    options,
                    identities.Select((item, index) => index == 0
                        ? item with { DatabaseGuid = Guid.NewGuid() }
                        : item).ToArray(),
                    fingerprints, capabilities, exclusion)).Code);
        Assert.Equal(Rt03Errors.SourceDrift,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03ProductionStartupGuard.Validate(
                    options, identities,
                    fingerprints with { MappingFingerprint = new string('0', 64) },
                    capabilities, exclusion)).Code);
        Assert.Equal(Rt03Errors.AutoSyncActive,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03ProductionStartupGuard.Validate(
                    options, identities, fingerprints, capabilities,
                    exclusion with { ActiveRunRows = 1 })).Code);
        Assert.Equal(Rt03Errors.ChangeTrackingWindowRejected,
            Assert.Throws<Rt03SafetyException>(() =>
                Rt03ProductionStartupGuard.Validate(
                    options, identities, fingerprints,
                    capabilities.Select((item, index) => index == 1
                        ? item with { ReadCommittedSnapshotEnabled = true }
                        : item).ToArray(), exclusion)).Code);
    }

    [Fact]
    public void Mutual_exclusion_uses_same_lifetime_lock_and_autosync_rejects_cutover()
    {
        Assert.Equal("QLHV:CSDT_AUTO_SYNC", QlhvSqlAutoSyncGlobalLock.LockResource);
        Assert.Equal(
            [
                "QLHV:CSDT_AUTO_SYNC",
            ],
            QlhvDirectRealtimeGlobalLock.LifetimeLockResources);
        Assert.Equal(
            "QLHV:CSDT_OPERATIONS:OTO",
            Rt03ProductionRealtimeWorker.ResolveOperationSource(
                Rt03Profiles.Oto).LockResource);
        Assert.Equal(
            "QLHV:CSDT_OPERATIONS:MOTO",
            Rt03ProductionRealtimeWorker.ResolveOperationSource(
                Rt03Profiles.Moto).LockResource);
        Assert.NotEqual(
            Rt03ProductionRealtimeWorker.ResolveOperationSource(
                Rt03Profiles.Oto).LockResource,
            Rt03ProductionRealtimeWorker.ResolveOperationSource(
                Rt03Profiles.Moto).LockResource);
        Assert.Contains("@LockTimeout = 0", QlhvSqlAutoSyncGlobalLock.AcquireSql);
        Assert.Contains("@LockOwner=N'Session'", QlhvDirectRealtimeGlobalLock.ReleaseSql);
        Assert.Contains("EnableControlledCutover = 1",
            QlhvSqlAutoSyncGlobalLock.RejectWhenDirectRealtimeActiveSql);
        Assert.Contains("App_QlhvDirectRealtimeFeatureState",
            QlhvSqlAutoSyncGlobalLock.DirectRealtimeFeatureTablePresentSql);
        Assert.Contains("App_QlhvAutoSyncRun", QlhvDirectRealtimeGlobalLock.ActiveAutoSyncSql);
    }

    [Fact]
    public void Writer_lock_timeout_defers_cycle_for_retry_without_treating_a_lease_as_timeout()
    {
        Assert.True(
            Rt03ProductionRealtimeWorker.ShouldDeferForUnavailableProfileLease(null));
        Assert.False(
            Rt03ProductionRealtimeWorker.ShouldDeferForUnavailableProfileLease(
                new NoopAsyncDisposable()));
    }

    [Fact]
    public void Non_contiguous_change_versions_select_the_next_existing_version_not_checkpoint_plus_one()
    {
        Assert.Contains(
            "SELECT MIN(ChangeVersion) AS Value",
            Rt03ProductionRealtimeCycleProcessor.NextChangeBatchSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE changeRow.ChangeVersion = nextVersion.Value",
            Rt03ProductionRealtimeCycleProcessor.NextChangeBatchSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CheckpointVersion + 1",
            Rt03ProductionRealtimeCycleProcessor.NextChangeBatchSql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Direct_realtime_capability_window_excludes_projection_owned_tables()
    {
        var sql = Rt03ProductionRealtimeCycleProcessor.SourceCapabilitySql;

        Assert.Contains("OBJECT_ID(N'dbo.NguoiLX')", sql, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.NguoiLX_HoSo')", sql, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.KhoaHoc')", sql, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.DM_HangDT')", sql, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.DM_DVHC')", sql, StringComparison.Ordinal);

        Assert.DoesNotContain("OBJECT_ID(N'dbo.GiaoVien')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OBJECT_ID(N'dbo.XeTap')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OBJECT_ID(N'dbo.KhoaHoc_GiaoVien')", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OBJECT_ID(N'dbo.KhoaHoc_XeTap')", sql, StringComparison.Ordinal);
        Assert.Contains("sys.change_tracking_tables", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_patches_enforce_oto_then_moto_no_delete_and_exact_rollback()
    {
        var root = RepositoryRoot();
        var registration = Read(root,
            "database/patches/20260727_rt03_add_production_registration_state.sql");
        var oto = Read(root,
            "database/patches/20260727_rt03_activate_oto_controlled_cutover.sql");
        var motoCheckpoint = Read(root,
            "database/patches/20260727_rt03_initialize_moto_checkpoint.sql");
        var moto = Read(root,
            "database/patches/20260727_rt03_activate_moto_controlled_cutover.sql");
        var rollback = Read(root,
            "database/patches/20260727_rt03_rollback_production_registration.sql");

        Assert.Contains("VALUES (1,N'UNREGISTERED',N'STOPPED',0)", registration);
        Assert.Contains("WHERE Enabled<>0", registration);
        Assert.Contains("RT03_OTO_CANARY_PROOF_MISSING", oto);
        Assert.Contains("CSDT_OTO' THEN 1 ELSE 0", oto);
        Assert.DoesNotContain("App_HocVien", motoCheckpoint, StringComparison.Ordinal);
        Assert.Contains("InsertedRows,UpdatedRows", motoCheckpoint);
        Assert.Contains("HEALTHY_NO_CHANGE", moto);
        Assert.Contains("CSDT_MOTO", moto);
        Assert.DoesNotContain("App_HocVien", rollback, StringComparison.Ordinal);
        Assert.Contains("CycleActive=1", rollback);
    }

    [Fact]
    public void Production_apply_surface_has_no_delete_deactivate_or_whole_row_update()
    {
        var apply = string.Join("\n", Rt03ProductionSql.ApplyCommands);
        Assert.DoesNotContain("DELETE FROM dbo.App_HocVien", apply,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET IsDeleted", apply,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", apply,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET HoTen = @DesiredHoTen", apply);
        Assert.Contains("Rt03DirectRealtimeWorker", apply);
    }

    [Fact]
    public void Idempotent_delete_replay_has_a_distinct_zero_mutation_marker_path()
    {
        var processor = Read(
            RepositoryRoot(),
            "server/QLHV.Infrastructure/Sync/Rt03/" +
            "Rt03ProductionRealtimeCycleProcessor.cs");

        Assert.Contains(
            "Rt03LearnerReplayDisposition.IdempotentDeleteAlreadyAbsent",
            processor,
            StringComparison.Ordinal);
        Assert.Contains(
            "PlannedOperationKind.AdvanceIdempotentDeleteNoChange",
            processor,
            StringComparison.Ordinal);
        Assert.Contains(
            "IDEMPOTENT_DELETE_ALREADY_ABSENT",
            processor,
            StringComparison.Ordinal);
        Assert.Contains(
            "plan.DispositionHash",
            processor,
            StringComparison.Ordinal);

        var apply = string.Join("\n", Rt03ProductionSql.ApplyCommands);
        Assert.DoesNotContain(
            "DELETE FROM dbo.App_HocVien",
            apply,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "SET IsDeleted",
            apply,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Checkpoint_namespaces_and_runtime_order_are_profile_isolated()
    {
        Assert.Equal(new[] { Rt03Profiles.Oto, Rt03Profiles.Moto }, Rt03Profiles.Ordered);
        Assert.Contains(
            "CONVERT(int, SequenceOrder) AS SequenceOrder",
            Rt03ProductionRuntimeStateStore.ProfileStateSql,
            StringComparison.Ordinal);
        var oto = new QLHV.Application.Sync.QlhvDirectRealtime.QlhvDirectRealtimeApplyCheckpointKey(
            Rt03Profiles.Oto, "DIRECT_REALTIME_APPLY", "map", "PRODUCTION");
        var moto = oto with { SourceProfile = Rt03Profiles.Moto };
        Assert.NotEqual(oto, moto);
    }

    private static Rt03ProductionOptions EnabledOptions() => new()
    {
        EnableRt03ProductionRealtime = true,
        EnableRt03ProductionShadow = true,
        EnableRt03ProductionWrites = true,
        EnableRt03ProductionCanary = false,
        EnableRt03ControlledCutover = true,
        EnableRt03ProductionDeletes = false,
        ValidationOnly = false,
        EnableOto = true,
        EnableMoto = true,
        PollIntervalSeconds = 2,
    };

    private static Rt03ProductionOptions Copy(
        Rt03ProductionOptions source,
        bool? deletes = null,
        bool? canary = null,
        bool? oto = null,
        bool? moto = null) => new()
    {
        EnableRt03ProductionRealtime = source.EnableRt03ProductionRealtime,
        EnableRt03ProductionShadow = source.EnableRt03ProductionShadow,
        EnableRt03ProductionWrites = source.EnableRt03ProductionWrites,
        EnableRt03ProductionCanary = canary ?? source.EnableRt03ProductionCanary,
        EnableRt03ControlledCutover = source.EnableRt03ControlledCutover,
        EnableRt03ProductionDeletes = deletes ?? source.EnableRt03ProductionDeletes,
        ValidationOnly = source.ValidationOnly,
        EnableOto = oto ?? source.EnableOto,
        EnableMoto = moto ?? source.EnableMoto,
        PollIntervalSeconds = source.PollIntervalSeconds,
        ExpectedMappingFingerprint = source.ExpectedMappingFingerprint,
        ExpectedOtoSourceSchemaFingerprint = source.ExpectedOtoSourceSchemaFingerprint,
        ExpectedMotoSourceSchemaFingerprint = source.ExpectedMotoSourceSchemaFingerprint,
        ExpectedTargetSchemaFingerprint = source.ExpectedTargetSchemaFingerprint,
    };

    private static IReadOnlyList<Rt03ObservedDatabase> ExactIdentities()
        => Rt03ProductionCatalog.RequiredDatabases.Select(item => new Rt03ObservedDatabase(
            item.Role,
            Rt03ProductionCatalog.ServerIdentity,
            item.DatabaseName,
            item.DatabaseName,
            item.DatabaseId,
            item.DatabaseGuid,
            $"Server=CSDLTTTC;Database={item.DatabaseName}",
            true,
            false,
            false)).ToArray();

    private static Rt03ProductionFingerprintSnapshot ExactFingerprints(
        Rt03ProductionOptions options) => new(
            options.ExpectedMappingFingerprint,
            options.ExpectedOtoSourceSchemaFingerprint,
            options.ExpectedMotoSourceSchemaFingerprint,
            options.ExpectedTargetSchemaFingerprint);

    private static IReadOnlyList<Rt03SourceCapabilitySnapshot> ExactCapabilities()
        =>
        [
            new(Rt03Profiles.Oto,
                Rt03ProductionCatalog.RequiredDatabases[0].DatabaseGuid,
                0, 0, 5, true, false),
            new(Rt03Profiles.Moto,
                Rt03ProductionCatalog.RequiredDatabases[1].DatabaseGuid,
                0, 0, 5, true, false),
        ];

    private static IConfiguration Configuration(
        IEnumerable<KeyValuePair<string, string?>> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> EnabledValues() => new()
    {
        [$"{Rt03ProductionOptions.SectionName}:EnableRt03ProductionRealtime"] = "true",
        [$"{Rt03ProductionOptions.SectionName}:EnableRt03ProductionShadow"] = "true",
        [$"{Rt03ProductionOptions.SectionName}:EnableRt03ProductionWrites"] = "true",
        [$"{Rt03ProductionOptions.SectionName}:EnableRt03ControlledCutover"] = "true",
        [$"{Rt03ProductionOptions.SectionName}:ValidationOnly"] = "false",
        [$"{Rt03ProductionOptions.SectionName}:EnableOto"] = "true",
        [$"{Rt03ProductionOptions.SectionName}:EnableMoto"] = "true",
    };

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    private static string Read(string root, string relative)
        => File.ReadAllText(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar)));

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
