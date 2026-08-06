namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03FullConvergenceRuntimeSafetyTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Recovery_is_explicit_one_shot_not_a_hosted_service()
    {
        var program = Read("server/QLHV.Worker/Program.cs");
        var registrations = Read("server/QLHV.Infrastructure/DependencyInjection.cs");

        Assert.Contains(
            "--rt03-v5-full-convergence-recovery",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddRt03FullConvergenceRecoveryServices",
            program,
            StringComparison.Ordinal);
        var method = Slice(
            registrations,
            "AddRt03FullConvergenceRecoveryServices",
            "\n    }\n}");
        Assert.DoesNotContain("AddHostedService", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_barrier_seals_anchor_then_locks_all_integrated_tables()
    {
        var source = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceSourceBarrier.cs");

        var anchorIndex = source.IndexOf("IdentitySql", StringComparison.Ordinal);
        var barrierIndex = source.IndexOf(
            "AcquireReadBarrierSql",
            anchorIndex,
            StringComparison.Ordinal);
        Assert.True(anchorIndex >= 0 && barrierIndex > anchorIndex);
        foreach (var table in new[]
                 {
                     "dbo.KhoaHoc",
                     "dbo.GiaoVien",
                     "dbo.XeTap",
                     "dbo.NguoiLX",
                     "dbo.NguoiLX_HoSo",
                     "dbo.DM_HangDT",
                     "dbo.DM_DVHC",
                     "dbo.KhoaHoc_GiaoVien",
                 })
        {
            Assert.Contains(
                $"FROM {table} WITH(TABLOCK,HOLDLOCK)",
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Runtime_executes_course_teacher_vehicle_learner_relation_order()
    {
        var source = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs");
        var course = source.IndexOf("sequenceOrder: 1", StringComparison.Ordinal);
        var teacher = source.IndexOf("sequenceOrder: 2", StringComparison.Ordinal);
        var vehicle = source.IndexOf("SequenceOrder: 3", StringComparison.Ordinal);
        var learner = source.IndexOf("sequenceOrder: 4", StringComparison.Ordinal);
        var relation = source.IndexOf("sequenceOrder: 5", StringComparison.Ordinal);

        Assert.True(
            course >= 0 &&
            course < teacher &&
            teacher < vehicle &&
            vehicle < learner &&
            learner < relation);
    }

    [Fact]
    public void Runtime_holds_profile_and_ordered_domain_leases_around_recovery()
    {
        var service = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs");
        var locks = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceLockFactory.cs");

        var global = service.IndexOf("globalLease", StringComparison.Ordinal);
        var profile = service.IndexOf("recoveryLease", global, StringComparison.Ordinal);
        var writer = service.IndexOf("sourceLease", profile, StringComparison.Ordinal);
        var domains = service.IndexOf(
            "TryAcquireDomainsAsync",
            writer,
            StringComparison.Ordinal);
        var barrier = service.IndexOf("_barriers.AcquireAsync", domains, StringComparison.Ordinal);
        Assert.True(
            global >= 0 &&
            global < profile &&
            profile < writer &&
            writer < domains &&
            domains < barrier);
        Assert.Contains(
            "Rt03FullConvergenceLocks.ForProfile",
            locks,
            StringComparison.Ordinal);
        Assert.Contains(
            "@LockTimeout=0",
            locks,
            StringComparison.Ordinal);
        Assert.Contains(
            "for (var index = _acquired.Count - 1; index >= 0; index--)",
            locks,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_does_not_publish_external_full_sync_snapshot_state()
    {
        var service = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs");
        Assert.Contains(
            "BackupSnapshotToken: string.Empty",
            service,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_requires_shared_mutation_safe_time_and_fresh_checkpoint()
    {
        var source = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs");

        Assert.Contains(
            "TimeAuthorityPolicy.IsMutationAllowed(time)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "preflight.CheckpointVersion != request.ExpectedCheckpoint",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "preflight.AutoSyncInactive",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "preflight.FullSyncInactive",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Event_after_anchor_remains_pending_after_atomic_publication()
    {
        var source = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs");
        var finalize = source.IndexOf("_state.FinalizeAsync", StringComparison.Ordinal);
        var current = source.IndexOf(
            "ReadCurrentVersionAsync",
            finalize,
            StringComparison.Ordinal);

        Assert.True(finalize >= 0 && current > finalize);
        Assert.Contains(
            "currentVersion - barrier.AnchorVersion",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicle_recovery_writer_never_updates_qlhv_owned_assignment_columns()
    {
        var source = Read(
            "server/QLHV.Infrastructure/Sync/VehicleRealtime/SqlVehicleFullConvergenceTargetStore.cs");

        foreach (var forbidden in new[]
                 {
                     "GVQuanLyMa=",
                     "GVQuanLyTen=",
                     "GhiChuNoiBo=",
                     "App_HocVien_PhanCong SET",
                     "App_KhoaHoc_NhomDaoTao SET",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("DELETE FROM dbo.App_XeTap", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "MarkMissingSql",
            source,
            StringComparison.Ordinal);
    }

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(
            Root,
            relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Slice(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }
}
