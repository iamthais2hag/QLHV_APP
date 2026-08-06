namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03FullConvergenceSchemaTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Recovery_schema_has_persistent_resumable_state_machine()
    {
        var sql = Read("database/patches/20260731_add_rt03_full_convergence_recovery.sql");

        Assert.Contains("App_Rt03FullConvergenceSession", sql, StringComparison.Ordinal);
        Assert.Contains("App_Rt03FullConvergenceDomain", sql, StringComparison.Ordinal);
        Assert.Contains("App_Rt03FullConvergenceMarker", sql, StringComparison.Ordinal);
        Assert.Contains("PREPARING", sql, StringComparison.Ordinal);
        Assert.Contains("VERIFYING", sql, StringComparison.Ordinal);
        Assert.Contains("COMPLETED", sql, StringComparison.Ordinal);
        Assert.Contains("AttemptCount=AttemptCount+1", sql, StringComparison.Ordinal);
        Assert.Contains(
            "RT03_RECOVERY_REPLAY_REQUIRED",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Marker_checkpoint_and_completion_are_one_atomic_transaction()
    {
        var sql = Read("database/patches/20260731_add_rt03_full_convergence_recovery.sql");
        var procedure = Slice(
            sql,
            "CREATE OR ALTER PROCEDURE dbo.usp_App_Rt03FinalizeFullConvergence",
            "SELECT N'RT03_V5_FULL_CONVERGENCE_SCHEMA_READY_NOT_EXECUTED'");

        Assert.Contains("BEGIN TRANSACTION", procedure, StringComparison.Ordinal);
        Assert.Contains(
            "INSERT dbo.App_Rt03FullConvergenceMarker",
            procedure,
            StringComparison.Ordinal);
        Assert.Contains(
            "UPDATE dbo.App_QlhvDirectRealtimeApplyCheckpoint",
            procedure,
            StringComparison.Ordinal);
        Assert.Contains(
            "INSERT dbo.App_QlhvDirectRealtimeApplyMarker",
            procedure,
            StringComparison.Ordinal);
        Assert.Contains(
            "PlanHash=@VerificationHash",
            procedure,
            StringComparison.Ordinal);
        Assert.Contains(
            "MarkerHash=@MarkerHash",
            procedure,
            StringComparison.Ordinal);
        Assert.Contains(
            "SET Status=N'COMPLETED'",
            procedure,
            StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION", procedure, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SET SourceChangeTrackingVersion=CHANGE_TRACKING_MIN_VALID_VERSION",
            procedure,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Finalize_uses_checkpoint_compare_and_swap()
    {
        var sql = Read("database/patches/20260731_add_rt03_full_convergence_recovery.sql");

        Assert.Contains(
            "AND SourceChangeTrackingVersion=@CheckpointBefore",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT03_RECOVERY_CHECKPOINT_CAS_REJECTED",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "SELECT COUNT_BIG(*)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("<>5", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_artifacts_never_touch_assignment_or_bao_cao_i()
    {
        foreach (var relative in new[]
                 {
                     "database/patches/20260731_add_rt03_full_convergence_recovery.sql",
                     "database/patches/20260731_rollback_rt03_full_convergence_recovery.sql",
                 })
        {
            var sql = Read(relative);
            Assert.DoesNotContain(
                "App_HocVien_PhanCong",
                sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "BaoCaoI",
                sql,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Rollback_refuses_to_remove_durable_recovery_evidence()
    {
        var sql = Read(
            "database/patches/20260731_rollback_rt03_full_convergence_recovery.sql");

        Assert.Contains(
            "ROLLBACK_REFUSED_NONEMPTY_SESSION",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ROLLBACK_REFUSED_NONEMPTY_DOMAIN",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ROLLBACK_REFUSED_NONEMPTY_MARKER",
            sql,
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
