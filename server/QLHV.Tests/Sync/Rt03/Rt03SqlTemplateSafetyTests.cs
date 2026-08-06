using System.Text.RegularExpressions;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03SqlTemplateSafetyTests
{
    [Fact]
    public void Every_rt03_sql_starts_with_an_exact_database_context()
    {
        foreach (var path in Rt03SqlFiles())
        {
            var sql = File.ReadAllText(path);
            Assert.Matches(new Regex(
                @"\AUSE \[(QLHV_APP|CSDL_OTO|CSDL_MOTO|\$\(Rt03TargetDatabase\))\];\r?\nGO\r?\n",
                RegexOptions.CultureInvariant), sql);
            Assert.DoesNotContain("EXACT_DATABASE_NAME", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Read_only_proofs_have_no_write_or_dynamic_execution_statement()
    {
        foreach (var name in new[]
        {
            "20260727_rt03_production_identity_discovery_read_only.sql",
            "20260727_rt03_production_readiness_read_only.sql",
            "20260727_rt03_task2_preflight_read_only.sql",
            "20260727_rt03_post_canary_read_only.sql",
        })
        {
            var sql = File.ReadAllText(Path.Combine(Root(), "database", "proofs", name));
            Assert.DoesNotMatch(new Regex(
                @"(?im)^\s*(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC(?:UTE)?|BACKUP|RESTORE)\b",
                RegexOptions.CultureInvariant), sql);
        }
    }

    [Fact]
    public void Ct_snapshot_templates_use_exact_live_allowlist_and_never_enable_rcsi()
    {
        foreach (var source in new[] { "oto", "moto" })
        {
            var enable = File.ReadAllText(Path.Combine(
                Root(), "database", "patches",
                $"20260727_rt03_enable_ct_snapshot_{source}_production.sql"));
            var disable = File.ReadAllText(Path.Combine(
                Root(), "database", "patches",
                $"20260727_rt03_disable_ct_snapshot_{source}_production.sql"));
            foreach (var table in new[]
            {
                "NguoiLX", "NguoiLX_HoSo", "KhoaHoc", "DM_HangDT", "DM_DVHC",
            })
            {
                Assert.Contains($"dbo.{table}", enable, StringComparison.Ordinal);
                Assert.Contains($"dbo.{table}", disable, StringComparison.Ordinal);
            }
            Assert.Equal(5, Regex.Matches(enable,
                @"ALTER TABLE dbo\.[A-Za-z_]+ ENABLE CHANGE_TRACKING").Count);
            Assert.DoesNotContain("READ_COMMITTED_SNAPSHOT ON", enable,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("is_read_committed_snapshot_on", enable,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("_BAK", enable, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("_V1", enable, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Moto_feature_and_ct_paths_require_oto_pass_first()
    {
        var moto = File.ReadAllText(Path.Combine(
            Root(), "database", "patches",
            "20260727_rt03_enable_ct_snapshot_moto_production.sql"));
        var cutover = File.ReadAllText(Path.Combine(
            Root(), "database", "patches",
            "20260727_rt03_feature_enable_controlled_cutover.sql"));
        Assert.Contains("RT03_OTO_CANARY_RESULT", moto, StringComparison.Ordinal);
        Assert.Contains("RT03_OTO_MUST_PASS_FIRST", moto, StringComparison.Ordinal);
        Assert.Contains("RT03_OTO_CANARY_RESULT", cutover, StringComparison.Ordinal);
        Assert.Contains("RT03_OTO_CANARY_PROOF_MISSING", cutover, StringComparison.Ordinal);
    }

    [Fact]
    public void Control_plane_seeds_flags_false_and_has_no_business_row_mutation()
    {
        var sql = File.ReadAllText(Path.Combine(
            Root(), "database", "patches",
            "20260727_rt03_add_direct_realtime_control_plane.sql"));
        Assert.Contains("VALUES (1, 0, 0, 0, 0, 0, 0",
            sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(
            @"(?i)(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+dbo\.App_HocVien\b"), sql);
        Assert.Contains("CHECK (EnableProductionDeletes = 0)",
            sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Feature_scripts_enforce_autosync_exclusion_and_delete_false()
    {
        var enable = File.ReadAllText(Path.Combine(
            Root(), "database", "patches",
            "20260727_rt03_feature_enable_canary.sql"));
        var disable = File.ReadAllText(Path.Combine(
            Root(), "database", "patches",
            "20260727_rt03_feature_disable_all.sql"));
        Assert.Contains("RT03_AUTOSYNC_ACTIVE", enable, StringComparison.Ordinal);
        Assert.Contains("RT03_AUTOSYNC_POLLING_NOT_DISABLED", enable,
            StringComparison.Ordinal);
        Assert.Contains("EnableProductionDeletes = 0", enable,
            StringComparison.Ordinal);
        Assert.DoesNotContain("QlhvAutoSyncRun\nSET", enable,
            StringComparison.OrdinalIgnoreCase);
        foreach (var flag in new[]
        {
            "EnableProductionRealtime",
            "EnableProductionShadow",
            "EnableProductionWrites",
            "EnableProductionCanary",
            "EnableControlledCutover",
            "EnableProductionDeletes",
        })
        {
            Assert.Contains($"{flag} = 0", disable, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Current_plan_is_observation_only_with_empty_allowlist()
    {
        var json = File.ReadAllText(Path.Combine(
            Root(), "ops", "rt03", "rt03-production-observation-plan.json"));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("OBSERVATION_ONLY", root.GetProperty("mode").GetString());
        Assert.Equal(0, root.GetProperty("candidates").GetArrayLength());
        Assert.False(root.GetProperty("checkpointPublished").GetBoolean());
        Assert.Equal(0, root.GetProperty("businessDataWrites").GetInt32());
        Assert.False(root.GetProperty("existingAutoSyncTouched").GetBoolean());
    }

    [Fact]
    public void KhoaHoc_identity_patch_removes_global_key_and_fixed_cycle_counts()
    {
        var sql = File.ReadAllText(Path.Combine(
            Root(),
            "database",
            "patches",
            "20260730_rt03_support_khoahoc_business_identity.sql"));
        Assert.Contains(
            "DROP CONSTRAINT UQ_App_KhoaHoc_MaKhoa",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "UX_App_KhoaHoc_SourceIdentity",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "InsertedRows >= 0",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InsertedRows BETWEEN 0 AND 1",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Corrected_KhoaHoc_scripts_set_and_assert_the_full_session_contract()
    {
        foreach (var name in new[]
        {
            "20260730_rt03_support_khoahoc_business_identity.sql",
            "20260730_rollback_rt03_khoahoc_business_identity.sql",
        })
        {
            var sql = File.ReadAllText(Path.Combine(
                Root(),
                "database",
                "patches",
                name));
            foreach (var option in new[]
            {
                "SET ANSI_NULLS ON;",
                "SET ANSI_PADDING ON;",
                "SET ANSI_WARNINGS ON;",
                "SET ARITHABORT ON;",
                "SET CONCAT_NULL_YIELDS_NULL ON;",
                "SET QUOTED_IDENTIFIER ON;",
                "SET NUMERIC_ROUNDABORT OFF;",
                "SET NOCOUNT ON;",
                "SET XACT_ABORT ON;",
            })
            {
                Assert.Contains(option, sql, StringComparison.Ordinal);
            }

            foreach (var option in new[]
            {
                "SESSIONPROPERTY(N'ANSI_NULLS')",
                "SESSIONPROPERTY(N'ANSI_PADDING')",
                "SESSIONPROPERTY(N'ANSI_WARNINGS')",
                "SESSIONPROPERTY(N'ARITHABORT')",
                "SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL')",
                "SESSIONPROPERTY(N'QUOTED_IDENTIFIER')",
                "SESSIONPROPERTY(N'NUMERIC_ROUNDABORT')",
            })
            {
                Assert.Contains(option, sql, StringComparison.Ordinal);
            }

            Assert.True(
                sql.IndexOf("SESSIONPROPERTY", StringComparison.Ordinal) <
                sql.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal));
            Assert.Contains(
                "BLOCKED - RT03 SCHEMA DRIFT DETECTED",
                sql,
                StringComparison.Ordinal);
            Assert.Contains(
                "$(Rt03ExpectedDatabaseGuid)",
                sql,
                StringComparison.Ordinal);
            Assert.Contains(
                "$(Rt03ExecutionMode)",
                sql,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Corrected_prerequisite_has_exact_lookup_index_and_transaction_fault_probe()
    {
        var sql = File.ReadAllText(Path.Combine(
            Root(),
            "database",
            "patches",
            "20260730_rt03_support_khoahoc_business_identity.sql"));
        var createIndex = new Regex(
            @"CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_SourceProfile_MaKhoa\s+" +
            @"ON dbo\.App_KhoaHoc\(SourceProfileCode, MaKhoa\)\s+" +
            @"INCLUDE\s+\(\s*SourceMaKhoaHoc,\s*SourceHash,\s*IsDeleted,\s*" +
            @"TrangThaiNguon\s*\);",
            RegexOptions.CultureInvariant).Match(sql);
        Assert.True(createIndex.Success);
        Assert.DoesNotContain(
            "WHERE",
            createIndex.Value,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "RT03_REHEARSAL_FORCED_FAILURE_AFTER_INDEX",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT03_KHOAHOC_SCHEMA_PREREQUISITE_ALREADY_APPLIED_EXACT",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Corrected_rollback_reverses_only_manifest_objects_in_safe_order()
    {
        var sql = File.ReadAllText(Path.Combine(
            Root(),
            "database",
            "patches",
            "20260730_rollback_rt03_khoahoc_business_identity.sql"));
        var checkDrop = sql.IndexOf(
            "DROP CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Mutations",
            StringComparison.Ordinal);
        var lookupDrop = sql.IndexOf(
            "DROP INDEX IX_App_KhoaHoc_SourceProfile_MaKhoa",
            StringComparison.Ordinal);
        var uniqueRestore = sql.IndexOf(
            "ADD CONSTRAINT UQ_App_KhoaHoc_MaKhoa UNIQUE",
            StringComparison.Ordinal);
        Assert.True(checkDrop > 0);
        Assert.True(lookupDrop > checkDrop);
        Assert.True(uniqueRestore > lookupDrop);
        Assert.Contains(
            "RT03_KHOAHOC_ROLLBACK_BLOCKED_CROSS_PROFILE_MAKHOA_EXISTS",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT03_KHOAHOC_ROLLBACK_BLOCKED_MULTIROW_CYCLE_HISTORY_EXISTS",
            sql,
            StringComparison.Ordinal);
    }

    private static IEnumerable<string> Rt03SqlFiles()
        => Directory.EnumerateFiles(
                Path.Combine(Root(), "database"), "*rt03*.sql",
                SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase);

    private static string Root()
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
