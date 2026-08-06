using System.Runtime.CompilerServices;
using QLHV.Application.Runtime;
using QLHV.Infrastructure.Runtime;

namespace QLHV.Tests.Runtime;

public sealed class DatabaseUtcHotfixTests
{
    [Fact]
    public void Database_clock_probe_is_exact_single_scalar_select()
    {
        Assert.Equal("SELECT SYSUTCDATETIME();",
            SqlDatabaseTimeAuthorityProbe.DatabaseUtcSql);
        Assert.DoesNotContain("FROM", SqlDatabaseTimeAuthorityProbe.DatabaseUtcSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.InRange(DatabaseTimeAuthorityContract.QueryTimeoutSeconds, 1, 2);
    }

    [Fact]
    public void Authorization_path_has_no_durable_or_history_probe()
    {
        var probe = Read("server/QLHV.Infrastructure/Runtime/SqlDatabaseTimeAuthorityProbe.cs");
        var service = Read("server/QLHV.Infrastructure/Runtime/TimeAuthorityService.cs");
        var middleware = Read("server/QLHV.Api/Runtime/TimeAuthorityWriteGuardMiddleware.cs");

        Assert.DoesNotContain("CycleHistory", probe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MAX(", probe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDurable", probe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDurable", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetWriteAuthorizationAsync", middleware,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Login_master_cycle_and_audit_timestamps_are_sql_owned()
    {
        var auth = Read("server/QLHV.Infrastructure/Auth/AppUserRepository.cs");
        var master = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03RealtimeControlStore.cs");
        var runtime = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRuntimeStateStore.cs");

        Assert.Contains("LastLoginAt = SYSUTCDATETIME()", auth,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UpdatedAtUtc=SYSUTCDATETIME()", master,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OccurredAtUtc", master, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SYSUTCDATETIME()", master, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CurrentCycleStartedAtUtc", runtime, StringComparison.Ordinal);
        Assert.Contains("LastCycleFailedAtUtc", runtime, StringComparison.Ordinal);
        Assert.Contains("@DatabaseUtcNow", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_contract_contains_no_durable_history_fields()
    {
        var types = Read("client/src/features/runtime-status/types.ts");
        var api = Read("client/src/features/runtime-status/api.ts");
        Assert.DoesNotContain("durable", types, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("durable", api, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("databaseClockAvailable", types, StringComparison.Ordinal);
    }

    private static string Read(
        string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
