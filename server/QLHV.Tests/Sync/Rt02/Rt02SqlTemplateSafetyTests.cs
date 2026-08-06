using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using QLHV.Application.Sync.QlhvDirectRealtime;

namespace QLHV.Tests.Sync.Rt02;

public sealed class Rt02SqlTemplateSafetyTests
{
    private static readonly string[] EnablePatches =
    [
        "20260727_rt02_enable_ct_snapshot_oto_test.sql",
        "20260727_rt02_enable_ct_snapshot_moto_test.sql",
    ];

    private static readonly string[] DisablePatches =
    [
        "20260727_rt02_disable_ct_snapshot_oto_test.sql",
        "20260727_rt02_disable_ct_snapshot_moto_test.sql",
    ];

    [Theory]
    [MemberData(nameof(AllPatchNames))]
    public void Rt02_SQL_template_starts_with_unresolved_exact_test_database(
        string fileName)
    {
        var patch = ReadPatch(fileName).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.StartsWith("USE [EXACT_TEST_DB];\nGO\n", patch, StringComparison.Ordinal);
        Assert.Contains("REVIEW TEMPLATE ONLY", patch, StringComparison.Ordinal);
        Assert.Contains("__RT02_SQL_SERVER_INSTANCE__", patch, StringComparison.Ordinal);
        Assert.Contains("__RT02_ENVIRONMENT_ID__", patch, StringComparison.Ordinal);
        Assert.Contains("__RT02_OWNER_APPROVAL_ID__", patch, StringComparison.Ordinal);
        Assert.Contains("__RT02_DATABASE_GUID__", patch, StringComparison.Ordinal);
        Assert.Contains("DB_NAME()", patch, StringComparison.Ordinal);
        Assert.Contains("database_id", patch, StringComparison.Ordinal);
        Assert.Contains("database_guid", patch, StringComparison.Ordinal);
        Assert.Contains("SERVERPROPERTY", patch, StringComparison.Ordinal);
        Assert.Contains("ISOLATED_DATABASE_IDENTITY_REJECTED", patch, StringComparison.Ordinal);
        Assert.Contains("RT02_ISOLATED_ENVIRONMENT_ID", patch, StringComparison.Ordinal);
        Assert.Contains("RT02_OWNER_APPROVAL_ID", patch, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllPatchNames))]
    public void Rt02_SQL_template_refuses_all_named_production_databases(
        string fileName)
    {
        var patch = ReadPatch(fileName);
        foreach (var productionName in
                 QlhvDirectRealtimeIsolatedEnvironmentValidator.ProductionDatabaseNames)
        {
            Assert.Contains(
                $"N'{productionName}'",
                patch,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [MemberData(nameof(EnablePatchNames))]
    public void Rt02_enable_template_is_idempotent_allowlisted_and_does_not_enable_RCSI(
        string fileName)
    {
        var patch = ReadPatch(fileName);

        Assert.Contains("CHANGE_RETENTION = 2 DAYS", patch, StringComparison.Ordinal);
        Assert.Contains("AUTO_CLEANUP = ON", patch, StringComparison.Ordinal);
        Assert.Contains("SET ALLOW_SNAPSHOT_ISOLATION ON", patch, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SET READ_COMMITTED_SNAPSHOT ON",
            patch,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Count(patch, "ENABLE CHANGE_TRACKING WITH"));
        Assert.Contains("OBJECT_ID(N'dbo.NguoiLX'", patch, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'dbo.NguoiLX_HoSo'", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("sp_executesql", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EXEC(", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE dbo.", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(DisablePatchNames))]
    public void Rt02_rollback_template_only_disables_allowlisted_CT_and_snapshot(
        string fileName)
    {
        var patch = ReadPatch(fileName);

        Assert.Equal(2, Count(patch, "DISABLE CHANGE_TRACKING"));
        Assert.Contains("SET CHANGE_TRACKING = OFF", patch, StringComparison.Ordinal);
        Assert.Contains("SET ALLOW_SNAPSHOT_ISOLATION OFF", patch, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SET READ_COMMITTED_SNAPSHOT",
            patch,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QlhvDirectRealtime_static_apply_SQL_safety_scan_has_no_P0_pattern()
    {
        var sql = string.Join("\n", QlhvDirectRealtimeApplySql.ReviewOnlyCommands);
        var forbidden = new[]
        {
            @"\bDELETE\s+FROM\b",
            @"\bTRUNCATE\b",
            @"\bMERGE\b",
            @"\bSET\s+IsDeleted\b",
            @"\bSET\s+SourceProfileCode\b",
            @"\bSET\s+\w*Owner\w*\b",
            @"\bEXEC\s*\(",
            @"\bsp_executesql\b",
        };
        foreach (var pattern in forbidden)
        {
            Assert.DoesNotMatch(
                new Regex(
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                sql);
        }

        Assert.Contains("UPDATE dbo.HocVien", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE Id = @TargetId", sql, StringComparison.Ordinal);
        Assert.Contains("AND V2RowHash = @ExpectedMappedHash", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_composition_roots_only_register_direct_realtime_writer_behind_rt03_flag()
    {
        var application = ReadWorkspaceFile(
            "server", "QLHV.Application", "DependencyInjection.cs");
        var infrastructure = ReadWorkspaceFile(
            "server", "QLHV.Infrastructure", "DependencyInjection.cs");
        var api = ReadWorkspaceFile("server", "QLHV.Api", "Program.cs");
        var worker = ReadWorkspaceFile("server", "QLHV.Worker", "Program.cs");
        var productionComposition = string.Join("\n",
            application, infrastructure, api, worker);

        Assert.DoesNotContain(
            "QlhvDirectRealtime",
            application,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddRt03ProductionRealtimeWorkerServices",
            api,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddRt03ProductionRealtimeWorkerServices",
            worker,
            StringComparison.Ordinal);
        Assert.Contains(
            "nameof(Rt03ProductionOptions.EnableRt03ProductionRealtime)",
            infrastructure,
            StringComparison.Ordinal);
        Assert.Contains(
            "return services;",
            infrastructure,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IQlhvDirectRealtimeTargetTransactionFactory",
            productionComposition,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_complete_fixture_loader_is_hash_pinned_transactional_and_has_no_schema_DDL()
    {
        var loader = ReadWorkspaceFile(
            "database",
            "proofs",
            "20260727_rt02_complete_fixture_loader.ps1");

        Assert.Contains(
            @"lpc:CSDLTTTC\QLHVRT02",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT02B-OPERATOR-APPROVAL-20260727-01",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "[Data.IsolationLevel]::Serializable",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT02-COMPLETE-FIXTURE-LOAD",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "HMACSHA256",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "ARTIFACT_FIXTURE_LOADER",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "ParameterSetName = 'Verify'",
            loader,
            StringComparison.Ordinal);
        Assert.Contains(
            "StableReadCount = 2",
            loader,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*(CREATE|ALTER|DROP|TRUNCATE|MERGE|DELETE)\b",
                RegexOptions.CultureInvariant),
            loader);
        Assert.DoesNotContain(
            "Data Source=lpc:CSDLTTTC;",
            loader,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("20260727_rt02_complete_features_on_read_only.sql")]
    [InlineData("20260727_rt02_complete_features_off_read_only.sql")]
    [InlineData("20260727_rt02_complete_final_integrity_read_only.sql")]
    public void Rt02_complete_feature_proofs_are_read_only(string fileName)
    {
        var proof = ReadWorkspaceFile("database", "proofs", fileName);

        Assert.Contains(
            @"N'CSDLTTTC\QLHVRT02'",
            proof,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*(CREATE|ALTER|DROP|TRUNCATE|MERGE|INSERT|UPDATE|DELETE|EXEC)\b",
                RegexOptions.CultureInvariant),
            proof);
    }

    [Fact]
    public void Rt02_real_deadlock_probe_uses_two_read_only_row_lock_sessions()
    {
        var probe = ReadWorkspaceFile(
            "database",
            "proofs",
            "20260727_rt02_complete_real_deadlock_probe.ps1");

        Assert.Contains(
            @"lpc:CSDLTTTC\QLHVRT02",
            probe,
            StringComparison.Ordinal);
        Assert.Contains(
            "WITH (UPDLOCK, HOLDLOCK)",
            probe,
            StringComparison.Ordinal);
        Assert.Contains(
            "[Data.IsolationLevel]::Serializable",
            probe,
            StringComparison.Ordinal);
        Assert.Contains(
            "SqlException 1205",
            probe,
            StringComparison.Ordinal);
        Assert.Contains(
            "two distinct SQL sessions",
            probe,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(
                @"(?im)^\s*(CREATE|ALTER|DROP|TRUNCATE|MERGE|INSERT|UPDATE|DELETE)\b",
                RegexOptions.CultureInvariant),
            probe);
        Assert.DoesNotContain(
            "Data Source=lpc:CSDLTTTC;",
            probe,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rt02_complete_runner_is_one_attempt_hash_pinned_and_never_runs_schema_gate_DDL()
    {
        var runner = ReadWorkspaceFile(
            "database",
            "proofs",
            "20260727_rt02_complete_execution_runner.ps1");

        Assert.Contains(
            "RT02_COMPLETE_EXECUTION_STARTED.txt",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "VALIDATED_NO_SQL_EXECUTED",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "Clear-LiveOptIns",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "phase_f_disable_moto",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "phase_f_disable_oto",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "Authorized_isolated_SQL_apply_harness_passes_all_gates",
            runner,
            StringComparison.Ordinal);
        Assert.Contains(
            "expectedProductionOutputHash",
            runner,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "20260727_rt02b2_isolated_schema_and_fixture.sql",
            runner,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "20260727_rt02b2_schema_set_options_hotfix.sql",
            runner,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "20260727_rt02b2_run_one_schema_gate_retry.ps1",
            runner,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rt02_authorized_harness_database_identity_query_binds_catalog_aliases()
    {
        var harness = ReadWorkspaceFile(
            "server",
            "QLHV.Tests",
            "Sync",
            "Rt02",
            "Rt02b2AuthorizedSqlExecutionTests.cs");

        Assert.Contains(
            "FROM sys.databases AS databaseItem",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONVERT(int, DB_ID())",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "INNER JOIN sys.database_recovery_status AS recovery",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE databaseItem.database_id = DB_ID();",
            harness,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_authorized_harness_uses_fixture_creation_time_for_approval_window()
    {
        var harness = ReadWorkspaceFile(
            "server",
            "QLHV.Tests",
            "Sync",
            "Rt02",
            "Rt02b2AuthorizedSqlExecutionTests.cs");

        Assert.Contains(
            "PiiRows,\n    CreatedAtUtc",
            harness.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(
            "DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)",
            harness,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.CreatedAtUtc",
            harness,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "2026-07-27T00:00:00Z",
            harness,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rt02_SQL_connections_apply_and_assert_filtered_index_SET_vector()
    {
        var composition = ReadWorkspaceFile(
            "server",
            "QLHV.Tests",
            "Sync",
            "Rt02",
            "QlhvDirectRealtimeSqlIsolatedTestCompositionRoot.cs");
        var harness = ReadWorkspaceFile(
            "server",
            "QLHV.Tests",
            "Sync",
            "Rt02",
            "Rt02b2AuthorizedSqlExecutionTests.cs");

        foreach (var requiredSet in new[]
                 {
                     "SET ANSI_NULLS ON;",
                     "SET ANSI_PADDING ON;",
                     "SET ANSI_WARNINGS ON;",
                     "SET ARITHABORT ON;",
                     "SET CONCAT_NULL_YIELDS_NULL ON;",
                     "SET QUOTED_IDENTIFIER ON;",
                     "SET NUMERIC_ROUNDABORT OFF;",
                 })
        {
            Assert.Contains(requiredSet, composition, StringComparison.Ordinal);
        }

        Assert.Contains(
            "SESSIONPROPERTY('ARITHABORT')",
            composition,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONVERT(int, DB_ID())",
            composition,
            StringComparison.Ordinal);
        Assert.Contains(
            "RT02 filtered-index session SET vector rejected.",
            composition,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(composition, "new SqlConnection("));
        Assert.DoesNotContain(
            "new SqlConnection(",
            harness,
            StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> AllPatchNames()
        => EnablePatches.Concat(DisablePatches).Select(name => new object[] { name });

    public static IEnumerable<object[]> EnablePatchNames()
        => EnablePatches.Select(name => new object[] { name });

    public static IEnumerable<object[]> DisablePatchNames()
        => DisablePatches.Select(name => new object[] { name });

    private static int Count(string source, string value)
        => Regex.Matches(
            source,
            Regex.Escape(value),
            RegexOptions.CultureInvariant).Count;

    private static string ReadPatch(string fileName)
        => ReadWorkspaceFile("database", "patches", fileName);

    private static string ReadWorkspaceFile(params string[] pathParts)
        => File.ReadAllText(FindWorkspacePath(pathParts));

    private static string FindWorkspacePath(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate RT02 artifact.", Path.Combine(pathParts));
    }
}
