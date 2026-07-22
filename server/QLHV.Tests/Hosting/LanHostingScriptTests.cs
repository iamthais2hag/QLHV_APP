using System.Text.RegularExpressions;

namespace QLHV.Tests.Hosting;

public sealed class LanHostingScriptTests
{
    private static readonly string[] RequiredFiles =
    [
        "Install-QLHV-App.ps1",
        "Start-QLHV-App.ps1",
        "Start-QLHV-App.cmd",
        "Stop-QLHV-App.ps1",
        "Update-QLHV-App.ps1",
        "Uninstall-QLHV-App.ps1",
        "README.md",
    ];

    [Fact]
    public void Deployment_package_contains_the_documented_single_click_tooling()
    {
        var directory = FindScriptsDirectory();

        foreach (var fileName in RequiredFiles)
        {
            Assert.True(File.Exists(Path.Combine(directory, fileName)), $"Missing LAN hosting file: {fileName}");
        }

        var commandLauncher = Read("Start-QLHV-App.cmd");
        Assert.Contains("Start-QLHV-App.ps1", commandLauncher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm", commandLauncher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vite", commandLauncher, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_builds_publish_output_only_and_excludes_local_or_external_data()
    {
        var installer = Read("Install-QLHV-App.ps1");

        Assert.Contains("Administrator", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("npm.cmd", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server\\QLHV.sln", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D:\\QLHV_APP_RUNTIME", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appsettings.Development", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IM_GPLX", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New-NetFirewallRule", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-NetFirewallRule", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Private", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"-Profile\s+[^\r\n]*Public", RegexOptions.IgnoreCase), installer);
        Assert.Contains("QLHV Th", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start-QLHV-App.cmd", installer, StringComparison.OrdinalIgnoreCase);

        AssertExclusionIsEnforced(installer, "appsettings.Development");
        AssertExclusionIsEnforced(installer, "IM_GPLX");
    }

    [Fact]
    public void Launcher_is_singleton_hidden_logged_and_health_checked_before_opening_browser()
    {
        var launcher = Read("Start-QLHV-App.ps1");

        Assert.Contains("8088", launcher, StringComparison.Ordinal);
        Assert.Contains("Get-NetTCPConnection", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-Process", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System.Threading.Mutex", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pid", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ASPNETCORE_ENVIRONMENT", launcher, StringComparison.Ordinal);
        Assert.Contains("Production", launcher, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS", launcher, StringComparison.Ordinal);
        Assert.Contains("http://0.0.0.0:8088", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HttpsRedirection__Enabled", launcher, StringComparison.Ordinal);
        Assert.Contains("Authentication__Cookie__SecurePolicy", launcher, StringComparison.Ordinal);
        Assert.Contains("SameAsRequest", launcher, StringComparison.Ordinal);
        Assert.Contains("Start-Process", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hidden", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RedirectStandardOutput", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RedirectStandardError", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/health", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://localhost:8088", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("npm", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vite", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taskkill", launcher, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stop_targets_the_recorded_pid_and_verifies_runtime_executable_path()
    {
        var stop = Read("Stop-QLHV-App.ps1");

        Assert.Contains("pid", stop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-Process", stop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QLHV.Api", stop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D:\\QLHV_APP_RUNTIME", stop, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            stop.Contains(".Path", StringComparison.OrdinalIgnoreCase) ||
            stop.Contains("MainModule", StringComparison.OrdinalIgnoreCase),
            "Stop script must verify the executable path before stopping the PID.");
        Assert.Matches(new Regex(@"Stop-Process\s+[^\r\n]*-Id", RegexOptions.IgnoreCase), stop);
        Assert.DoesNotContain("taskkill", stop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"Get-Process\s+(dotnet|node)(\s|$)", RegexOptions.IgnoreCase), stop);
        Assert.DoesNotMatch(new Regex(@"Stop-Process\s+[^\r\n]*-Name", RegexOptions.IgnoreCase), stop);
    }

    [Fact]
    public void Update_is_staged_health_checked_and_rolls_back_before_restarting_old_runtime()
    {
        var update = Read("Update-QLHV-App.ps1");

        Assert.Contains("npm.cmd", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stage", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("backup", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop-QLHV-App.ps1", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start-QLHV-App.ps1", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/health", update, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            update.Contains("rollback", StringComparison.OrdinalIgnoreCase) ||
            update.Contains("khôi phục", StringComparison.OrdinalIgnoreCase) ||
            update.Contains("restore", StringComparison.OrdinalIgnoreCase),
            "Update script must contain an explicit rollback path.");
    }

    [Fact]
    public void Deployment_scripts_never_run_sql_backup_restore_refresh_or_sync_operations()
    {
        var scripts = Directory.GetFiles(FindScriptsDirectory(), "*.ps1")
            .Concat(Directory.GetFiles(FindScriptsDirectory(), "*.cmd"))
            .ToArray();
        var executableSource = string.Join('\n', scripts.Select(File.ReadAllText));

        Assert.DoesNotMatch(new Regex(@"\bsqlcmd(?:\.exe)?\b", RegexOptions.IgnoreCase), executableSource);
        Assert.DoesNotMatch(new Regex(@"\bInvoke-Sqlcmd\b", RegexOptions.IgnoreCase), executableSource);
        Assert.DoesNotContain("refresh-backup", executableSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("import-execute", executableSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"Invoke-(RestMethod|WebRequest)[^\r\n]*-Method\s+Post", RegexOptions.IgnoreCase), executableSource);
        Assert.DoesNotMatch(new Regex(@"\b(BACKUP|RESTORE)\s+DATABASE\b", RegexOptions.IgnoreCase), executableSource);
    }

    [Fact]
    public void Readme_describes_server_shortcut_client_url_and_no_client_tooling_requirement()
    {
        var readme = Read("README.md");

        Assert.Contains("QLHV Th", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://localhost:8088", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("http://192.168.100.101:8088", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D:\\QLHV_APP_RUNTIME", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Node.js", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET SDK", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PowerShell", readme, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertExclusionIsEnforced(string installer, string marker)
    {
        var markerIndex = installer.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        Assert.True(markerIndex >= 0, $"Installer does not mention required exclusion: {marker}");

        var contractWindow = installer.Substring(
            markerIndex,
            Math.Min(400, installer.Length - markerIndex));
        Assert.True(
            contractWindow.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            contractWindow.Contains("Exclude", StringComparison.OrdinalIgnoreCase) ||
            contractWindow.Contains("throw", StringComparison.OrdinalIgnoreCase) ||
            contractWindow.Contains("Test-Path", StringComparison.OrdinalIgnoreCase),
            $"Installer mentions {marker} but does not enforce its exclusion.");
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(FindScriptsDirectory(), fileName));

    private static string FindScriptsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "scripts",
                "windows",
                "qlhv-lan");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate scripts/windows/qlhv-lan.");
    }
}
