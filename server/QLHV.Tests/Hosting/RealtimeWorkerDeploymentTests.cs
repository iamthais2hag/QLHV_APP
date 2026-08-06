using System.Text.RegularExpressions;

namespace QLHV.Tests.Hosting;

public sealed class RealtimeWorkerDeploymentTests
{
    [Fact]
    public void Worker_is_a_production_only_non_web_windows_service_host()
    {
        var program = ReadWorkspaceFile("server", "QLHV.Worker", "Program.cs");
        var project = ReadWorkspaceFile("server", "QLHV.Worker", "QLHV.Worker.csproj");

        Assert.Contains("EnvironmentName = Environments.Production", program, StringComparison.Ordinal);
        Assert.Contains("ContentRootPath = AppContext.BaseDirectory", program, StringComparison.Ordinal);
        Assert.Contains("Environment.CurrentDirectory = AppContext.BaseDirectory", program, StringComparison.Ordinal);
        Assert.Contains("ProductionLocalHostConfiguration.Load", program, StringComparison.Ordinal);
        Assert.Contains("AddWindowsService", program, StringComparison.Ordinal);
        Assert.Contains("QLHV_APP_RealtimeWorker", program, StringComparison.Ordinal);
        Assert.Contains("AddInfrastructureCore", program, StringComparison.Ordinal);
        Assert.Contains("AddCsdtRealtimeWorkerServices", program, StringComparison.Ordinal);
        Assert.DoesNotContain("WebApplication", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Kestrel", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UseUrls", program, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Microsoft.Extensions.Hosting.WindowsServices", project, StringComparison.Ordinal);
        Assert.Contains("appsettings.Development.json", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CopyToPublishDirectory>Never", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CopyToOutputDirectory>Never", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Worker_service_contract_is_fixed_automatic_recoverable_and_path_validated()
    {
        var service = ReadScript("RealtimeWorkerService.ps1");

        Assert.Contains("QLHV_APP_RealtimeWorker", service, StringComparison.Ordinal);
        Assert.Contains(@"app\worker\QLHV.Worker.exe", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sc.exe create", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delayed-auto", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"NT SERVICE\QLHV_APP_RealtimeWorker", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalSystem", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rt03Production__EnableRt03ProductionRealtime=true", service, StringComparison.Ordinal);
        Assert.Contains("QlhvAutoSync__Enabled=false", service, StringComparison.Ordinal);
        Assert.Contains("REG_MULTI_SZ", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Win32_Service", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact approved worker path", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'failure', $script:QlhvRealtimeWorkerServiceName", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart/5000/restart/15000/restart/60000", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start-Service", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop-Service", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invoke-QlhvSc @('delete'", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taskkill", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(@"Stop-Process\s+[^\r\n]*-Name", RegexOptions.IgnoreCase),
            service);
    }

    [Theory]
    [InlineData("Install-QLHV-App.ps1")]
    [InlineData("Update-QLHV-App.ps1")]
    public void Deployment_stages_worker_and_starts_service_only_after_api_smoke_succeeds(
        string scriptName)
    {
        var script = ReadScript(scriptName);

        Assert.Contains("QLHV.Worker.csproj", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$StageWorker", script, StringComparison.Ordinal);
        Assert.Contains("worker\\QLHV.Worker.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Install-QlhvRealtimeWorkerService", script, StringComparison.Ordinal);
        Assert.Contains("Start-QlhvRealtimeWorkerService", script, StringComparison.Ordinal);
        Assert.Contains("Stop-QLHV-App.ps1", script, StringComparison.OrdinalIgnoreCase);

        var stopBeforeTransition = script.IndexOf(
            "& $StopScript -Quiet",
            StringComparison.Ordinal);
        var activateRuntime = script.LastIndexOf(
            "Move-Item -LiteralPath $StageApp -Destination $AppDirectory",
            StringComparison.Ordinal);
        var smoke = script.LastIndexOf(
            "Invoke-ReadOnlySmokeTest",
            StringComparison.Ordinal);
        var workerStart = script.IndexOf(
            "Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot",
            smoke,
            StringComparison.Ordinal);

        Assert.True(
            stopBeforeTransition >= 0 && activateRuntime > stopBeforeTransition,
            "The exact running service/API must stop before the runtime directory is replaced.");
        Assert.True(
            smoke >= 0 && workerStart > smoke,
            "The Worker service must start only after read-only API smoke checks pass.");
    }

    [Fact]
    public void Stop_and_uninstall_manage_the_exact_worker_service_before_runtime_files()
    {
        var stop = ReadScript("Stop-QLHV-App.ps1");
        var uninstall = ReadScript("Uninstall-QLHV-App.ps1");

        Assert.Contains("RealtimeWorkerService.ps1", stop, StringComparison.Ordinal);
        Assert.Contains("Get-QlhvRealtimeWorkerServiceSnapshot", stop, StringComparison.Ordinal);
        Assert.Contains("Stop-QlhvRealtimeWorkerService", stop, StringComparison.Ordinal);
        Assert.Contains("RealtimeWorkerService.ps1", uninstall, StringComparison.Ordinal);
        Assert.Contains("Remove-QlhvRealtimeWorkerService", uninstall, StringComparison.Ordinal);

        var stopService = stop.IndexOf(
            "Stop-QlhvRealtimeWorkerService",
            StringComparison.Ordinal);
        var stopApi = stop.IndexOf(
            "Stop-VerifiedQlhvProcess",
            stopService,
            StringComparison.Ordinal);
        Assert.True(stopService >= 0 && stopApi > stopService);

        var removeService = uninstall.IndexOf(
            "Remove-QlhvRealtimeWorkerService",
            StringComparison.Ordinal);
        var removeRuntime = uninstall.IndexOf(
            "Remove-Item -LiteralPath $RuntimeRoot -Recurse",
            StringComparison.Ordinal);
        Assert.True(removeService >= 0 && removeRuntime > removeService);
    }

    [Fact]
    public void Readme_documents_the_separate_non_web_worker_lifecycle()
    {
        var readme = ReadScript("README.md");

        Assert.Contains("QLHV_APP_RealtimeWorker", readme, StringComparison.Ordinal);
        Assert.Contains(@"D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-web Windows service", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only HTTP listener on port 8088", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1-second poll interval", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5-minute reconciliation", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UseBackupProfiles=false", readme, StringComparison.Ordinal);
    }

    private static string ReadScript(string fileName) =>
        File.ReadAllText(Path.Combine(FindWorkspaceRoot(), "scripts", "windows", "qlhv-lan", fileName));

    private static string ReadWorkspaceFile(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindWorkspaceRoot() }.Concat(path).ToArray()));

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate the QLHV workspace root.");
    }
}
