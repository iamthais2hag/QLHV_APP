using System.Runtime.CompilerServices;

namespace QLHV.Tests.Runtime;

public sealed class TimePolicyConsumerAlignmentTests
{
    [Theory]
    [InlineData("server/QLHV.Api/Controllers/SystemRuntimeController.cs")]
    [InlineData("server/QLHV.Api/Runtime/TimeAuthorityWriteGuardMiddleware.cs")]
    [InlineData("server/QLHV.Infrastructure/Runtime/RuntimeReadinessService.cs")]
    [InlineData("server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeWorker.cs")]
    [InlineData("server/QLHV.Infrastructure/Sync/Rt03/Rt03FullConvergenceRecoveryService.cs")]
    public void Api_worker_and_recovery_use_the_shared_mutation_gate(string path)
    {
        var source = ReadWorkspaceFile(path);

        Assert.Contains(
            "TimeAuthorityPolicy.IsMutationAllowed",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_preflight_accepts_only_healthy_or_exact_safe_warning()
    {
        var source = ReadWorkspaceFile(
            "ops/time-policy-v8/Invoke-TimePolicyV8StrictPreflight.ps1");

        Assert.Contains("TIME_HEALTHY", source, StringComparison.Ordinal);
        Assert.Contains("TIME_SAFE_WARNING", source, StringComparison.Ordinal);
        Assert.Contains(
            "TRANSIENT_W32TIME_DIAGNOSTIC",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "lastSyncError -ne 0",
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadWorkspaceFile(
        string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
