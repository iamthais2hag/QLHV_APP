using System.Text.RegularExpressions;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03V7WorkerSecurityTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Prerequisite_uses_a_dedicated_role_and_never_creates_a_principal()
    {
        var sql = Read(
            "database/patches/20260731_rt03_realtime_worker_permissions.sql");

        Assert.Contains(
            "CREATE ROLE [QLHV_RealtimeWorkerRole]",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER ROLE [QLHV_RealtimeWorkerRole]",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "DROP MEMBER [NT SERVICE\\QLHV_APP_RealtimeWorker]",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*CREATE\s+(LOGIN|USER)\b",
                RegexOptions.CultureInvariant),
            sql);
    }

    [Fact]
    public void Target_grants_are_object_scoped_without_delete_or_broad_database_dml()
    {
        var sql = Read(
            "database/patches/20260731_rt03_realtime_worker_permissions.sql");

        foreach (var target in new[]
                 {
                     "dbo.App_KhoaHoc",
                     "dbo.App_GiaoVien",
                     "dbo.App_XeTap",
                     "dbo.App_HocVien",
                     "dbo.App_QlhvDirectRealtimeApplyCheckpoint",
                     "dbo.App_QlhvDirectRealtimeApplyMarker",
                 })
        {
            Assert.Contains(
                $"OBJECT::{target}",
                sql,
                StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*GRANT\s+(CONTROL|ALTER)\b",
                RegexOptions.CultureInvariant),
            sql);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*GRANT\s+(INSERT|UPDATE|DELETE)\s+ON\s+(DATABASE|SCHEMA)::",
                RegexOptions.CultureInvariant),
            sql);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*GRANT\s+DELETE\b",
                RegexOptions.CultureInvariant),
            sql);
        Assert.DoesNotContain(
            "ADD MEMBER [db_owner]",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "ADD MEMBER [db_datawriter]",
            sql,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_contract_is_read_only_and_change_tracking_is_allowlisted()
    {
        var sql = Read(
            "database/patches/20260731_rt03_realtime_worker_permissions.sql");

        foreach (var table in new[]
                 {
                     "KhoaHoc",
                     "GiaoVien",
                     "XeTap",
                     "NguoiLX",
                     "NguoiLX_HoSo",
                     "DM_HangDT",
                     "DM_DVHC",
                     "KhoaHoc_GiaoVien",
                 })
        {
            Assert.Contains(
                $"GRANT SELECT ON OBJECT::dbo.{table}",
                sql,
                StringComparison.Ordinal);
            foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                Assert.DoesNotContain(
                    $"GRANT {verb} ON OBJECT::dbo.{table}",
                    sql,
                    StringComparison.Ordinal);
            }
        }

        Assert.Equal(
            10,
            Regex.Matches(
                sql,
                @"GRANT VIEW CHANGE TRACKING ON OBJECT::dbo\.",
                RegexOptions.CultureInvariant).Count);
    }

    [Fact]
    public void Recovery_state_is_accessed_through_exact_procedures()
    {
        var sql = Read(
            "database/patches/20260731_rt03_realtime_worker_permissions.sql");

        foreach (var procedure in new[]
                 {
                     "usp_App_Rt03BeginFullConvergence",
                     "usp_App_Rt03RecordFullConvergenceDomain",
                     "usp_App_Rt03VerifyFullConvergence",
                     "usp_App_Rt03FinalizeFullConvergence",
                 })
        {
            Assert.Contains(
                $"GRANT EXECUTE ON OBJECT::dbo.{procedure}",
                sql,
                StringComparison.Ordinal);
        }

        foreach (var table in new[]
                 {
                     "App_Rt03FullConvergenceSession",
                     "App_Rt03FullConvergenceDomain",
                     "App_Rt03FullConvergenceMarker",
                 })
        {
            Assert.Contains(
                $"GRANT VIEW DEFINITION ON OBJECT::dbo.{table}",
                sql,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"GRANT SELECT ON OBJECT::dbo.{table}",
                sql,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rollback_is_fail_closed_and_restores_only_the_v6_reader_membership()
    {
        var sql = Read(
            "database/patches/20260731_rollback_rt03_realtime_worker_permissions.sql");

        Assert.Contains(
            "RT03_V7_ROLLBACK_TARGET_STATE_UNSAFE",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT03_V7_ROLLBACK_OTO_STATE_UNSAFE",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT03_V7_ROLLBACK_MOTO_STATE_UNSAFE",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ALTER ROLE [db_datareader]",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ADD MEMBER [NT SERVICE\\QLHV_APP_RealtimeWorker]",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "REVOKE INSERT ON OBJECT::dbo.App_HocVien FROM " +
            "[NT SERVICE\\QLHV_APP_RealtimeWorker]",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "IF XACT_STATE()<>0 ROLLBACK TRANSACTION",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_is_read_only_and_checks_exact_members_grants_and_baseline_gap()
    {
        var script = Read(
            "ops/rt03-v7/Invoke-Rt03V7PermissionPreflight.ps1");

        Assert.Contains("readOnly = $true", script, StringComparison.Ordinal);
        Assert.Contains(
            "exact-worker-role-grants",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "exact-worker-role-members",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "baseline-course-update-gap-confirmed",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*(Invoke-)?Sqlcmd\b.*\b-i\b",
                RegexOptions.CultureInvariant),
            script);
    }

    [Fact]
    public void Behavioral_rehearsal_is_disposable_denies_source_dml_and_rolls_back()
    {
        var sql = Read(
            "database/patches/20260731_rehearse_rt03_realtime_worker_permissions.sql");

        Assert.Contains(
            "N'CSDLTTTC\\QLHVRT02'",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("COURSE_CT120_UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("COURSE_CT122_UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("COURSE_NO_CHANGE", sql, StringComparison.Ordinal);
        Assert.Contains("LEARNER_INSERT", sql, StringComparison.Ordinal);
        Assert.Contains("VEHICLE_UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains(
            "RECOVERY_CHECKPOINT_ADVANCED_BEFORE_ROLLBACK",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "UPDATE TOP(0) dbo.KhoaHoc",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "DELETE TOP(0) FROM dbo.KhoaHoc",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "ROLLBACK TRANSACTION",
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
        var endIndex = value.IndexOf(end, startIndex + start.Length,
            StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return value[startIndex..endIndex];
    }
}
