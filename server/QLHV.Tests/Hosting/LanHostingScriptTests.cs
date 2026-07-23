using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
        Assert.Contains("launcher.lock", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FileShare]::None", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Enter-CrossSessionLauncherLock", launcher, StringComparison.Ordinal);
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
        Assert.Contains("Get-QlhvRuntimeProcessIds", stop, StringComparison.Ordinal);
        Assert.Contains("Get-CimInstance -ClassName Win32_Process", stop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stale/missing PID file", stop, StringComparison.OrdinalIgnoreCase);
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
    public void Installer_creates_only_an_allowlisted_external_production_configuration()
    {
        var installer = Read("Install-QLHV-App.ps1");

        Assert.Contains(
            @"D:\QLHV_APP_RUNTIME",
            installer,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "appsettings.Production.Local.json",
            installer,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$allowedSections", installer, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings", installer, StringComparison.Ordinal);
        Assert.Contains("ConnectionProfileEncryption", installer, StringComparison.Ordinal);
        Assert.Contains("DataProtection", installer, StringComparison.Ordinal);
        Assert.Contains("FileStorage", installer, StringComparison.Ordinal);
        Assert.Contains("SyncExecution", installer, StringComparison.Ordinal);
        Assert.Contains("Authentication", installer, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConvertFrom-Json", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsPathRooted", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetFullPath", installer, StringComparison.OrdinalIgnoreCase);

        var allowlistStart = installer.IndexOf("$allowedSections", StringComparison.Ordinal);
        var allowlistEnd = installer.IndexOf("$target =", allowlistStart, StringComparison.Ordinal);
        Assert.True(allowlistStart >= 0 && allowlistEnd > allowlistStart);
        var allowlist = installer[allowlistStart..allowlistEnd];
        Assert.DoesNotContain("Logging", allowlist, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cors", allowlist, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(@"Copy-Item[^\r\n]*appsettings\.Development", RegexOptions.IgnoreCase),
            installer);
        Assert.DoesNotMatch(
            new Regex(@"Copy-Item[^\r\n]*\$DevelopmentSettings", RegexOptions.IgnoreCase),
            installer);
    }

    [Fact]
    public void Installer_preserves_existing_local_values_except_operational_flags_and_restricts_acl()
    {
        var installer = Read("Install-QLHV-App.ps1");

        Assert.Contains("Set-QlhvProductionWriteFlags", installer, StringComparison.Ordinal);
        Assert.Contains("operational write flags were normalized", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[IO.File]::Replace", installer, StringComparison.Ordinal);
        Assert.Contains("SetAccessRuleProtection", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S-1-5-18", installer, StringComparison.Ordinal);
        Assert.Contains("S-1-5-32-544", installer, StringComparison.Ordinal);
        Assert.Contains("RuntimeAccount", installer, StringComparison.Ordinal);
        Assert.Contains("ReadAndExecute", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("icacls.exe", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-Access Modify", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-FileHash", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA256", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(@"Write-(Host|Output|Verbose|Debug)[^\r\n]*\$(json|source|target|configuration)", RegexOptions.IgnoreCase),
            installer);

        var restrictDirectory = installer.IndexOf("Restrict the directory before", StringComparison.Ordinal);
        var writeSecretFile = installer.IndexOf("WriteAllText($temporaryConfig", StringComparison.Ordinal);
        Assert.True(restrictDirectory >= 0 && writeSecretFile > restrictDirectory,
            "The config directory must be restricted before a secret-bearing temp file is written.");
    }

    [Fact]
    public void Launcher_validates_config_then_waits_for_live_and_ready_before_browser()
    {
        var launcher = Read("Start-QLHV-App.ps1");

        Assert.Contains("appsettings.Production.Local.json", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Thiếu hoặc sai cấu hình QLHV_APP", launcher, StringComparison.Ordinal);
        Assert.Contains("QlhvRuntime__ProductionLocalConfigPath", launcher, StringComparison.Ordinal);
        Assert.Contains("QlhvRuntime__Root", launcher, StringComparison.Ordinal);
        Assert.Contains("/health/live", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/health/ready", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/system/runtime-status", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop-QlhvProcessById", launcher, StringComparison.Ordinal);
        Assert.Contains("TCP port 8088 is already used by another process", launcher, StringComparison.Ordinal);
        Assert.Contains("StartedThisRun", launcher, StringComparison.Ordinal);
        Assert.Contains("-TimeoutSeconds 45", launcher, StringComparison.Ordinal);

        var liveWait = launcher.LastIndexOf("-Url $LiveUrl", StringComparison.Ordinal);
        var readyWait = launcher.LastIndexOf("-Url $ReadyUrl", StringComparison.Ordinal);
        var browserOpen = launcher.LastIndexOf("Start-Process $ApplicationUrl", StringComparison.Ordinal);
        Assert.True(liveWait >= 0 && readyWait > liveWait, "Liveness must be checked before readiness.");
        Assert.True(browserOpen > readyWait, "The browser must open only after readiness succeeds.");
    }

    [Fact]
    public void Launcher_handles_missing_http_error_details_under_strict_mode()
    {
        var launcher = Read("Start-QLHV-App.ps1");
        var allScripts = string.Join('\n',
            Directory.GetFiles(FindScriptsDirectory(), "*.ps1").Select(File.ReadAllText));

        Assert.Contains("$errorDetailsProperty = $_.PSObject.Properties['ErrorDetails']", launcher, StringComparison.Ordinal);
        Assert.Contains("$null -ne $errorDetailsProperty.Value", launcher, StringComparison.Ordinal);
        Assert.Contains("PSObject.Properties['Message']", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("$_.ErrorDetails.Message", allScripts, StringComparison.Ordinal);
    }

    [Fact]
    public void Launcher_reconciles_orphaned_exact_runtime_processes_without_broad_kills()
    {
        var launcher = Read("Start-QLHV-App.ps1");

        Assert.Contains("Get-QlhvRuntimeProcessIds", launcher, StringComparison.Ordinal);
        Assert.Contains("Get-CimInstance -ClassName Win32_Process", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orphanedRuntimeId", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stop-QlhvProcessById", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"Stop-Process[^\r\n]*-Name", RegexOptions.IgnoreCase), launcher);
    }

    [Fact]
    public void Rollback_supports_legacy_health_and_installer_does_not_leave_elevated_api_running()
    {
        var launcher = Read("Start-QLHV-App.ps1");
        var installer = Read("Install-QLHV-App.ps1");
        var update = Read("Update-QLHV-App.ps1");

        Assert.Contains("AllowLegacyRollback", launcher, StringComparison.Ordinal);
        Assert.Contains("Wait-ForLegacyRollbackHealth", launcher, StringComparison.Ordinal);
        Assert.Contains("$LegacyHealthUrl", launcher, StringComparison.Ordinal);
        Assert.Contains("$legacyReady = $legacyProbe.Success", launcher, StringComparison.Ordinal);
        Assert.Contains("-AllowLegacyRollback", installer, StringComparison.Ordinal);
        Assert.Contains("-AllowLegacyRollback", update, StringComparison.Ordinal);
        Assert.Contains("legacy-runtime.marker", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test-Path -LiteralPath $LegacyRuntimeMarker", launcher, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $LegacyRuntimeMarker", installer, StringComparison.Ordinal);
        Assert.Contains("Set-Content -LiteralPath $LegacyRuntimeMarker", update, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $LegacyRuntimeMarker", installer, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $LegacyRuntimeMarker", update, StringComparison.Ordinal);
        Assert.Contains("Logging__Console__LogLevel__Default", launcher, StringComparison.Ordinal);
        Assert.Contains("None", launcher, StringComparison.Ordinal);

        var smoke = installer.LastIndexOf("Invoke-ReadOnlySmokeTest", StringComparison.Ordinal);
        var stopAfterSmoke = installer.IndexOf("& $StopScript -Quiet", smoke, StringComparison.Ordinal);
        var shortcut = installer.LastIndexOf("Install-DesktopShortcut", StringComparison.Ordinal);
        Assert.True(smoke >= 0 && stopAfterSmoke > smoke && shortcut > stopAfterSmoke,
            "The elevated installer must stop its smoke-test process before completing the shortcut install.");
        Assert.Contains("firewallExistedBefore", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shortcutExistedBefore", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_smoke_tests_get_only_and_rolls_runtime_back_on_failure()
    {
        var installer = Read("Install-QLHV-App.ps1");

        Assert.Contains("Invoke-StartRuntime", installer, StringComparison.Ordinal);
        Assert.Contains("Invoke-ReadOnlySmokeTest", installer, StringComparison.Ordinal);
        Assert.Contains("/health/live", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/health/ready", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/system/runtime-status", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/auth/me", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Expected = 401", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/qlhv-import", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InstallBackup", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("previous runtime was restored", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(@"Invoke-(RestMethod|WebRequest)[^\r\n]*-Method\s+Post", RegexOptions.IgnoreCase),
            installer);

        var smokeCall = installer.LastIndexOf("Invoke-ReadOnlySmokeTest", StringComparison.Ordinal);
        var deleteBackup = installer.LastIndexOf("Remove-Item -LiteralPath $InstallBackup", StringComparison.Ordinal);
        Assert.True(deleteBackup > smokeCall, "The old runtime backup must survive until smoke tests pass.");
    }

    [Fact]
    public void Firewall_install_uses_explicit_supported_parameters_without_pipeline_binding()
    {
        var installer = Read("Install-QLHV-App.ps1");

        Assert.Contains("Set-NetFirewallRule", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-Name 'QLHV-App-LAN-TCP-8088-Private'", installer, StringComparison.Ordinal);
        Assert.Contains("-NewDisplayName $FirewallDisplayName", installer, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-LocalPort 8088", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("foreach ($duplicateRule in $displayNameRules)", installer, StringComparison.Ordinal);
        Assert.Contains("Remove-NetFirewallRule -Name", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-NetFirewallPortFilter", installer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(@"\|\s*(Set-NetFirewallRule|Set-NetFirewallPortFilter|Remove-NetFirewallRule)", RegexOptions.IgnoreCase),
            installer);

        var calls = InvokeFirewallInstallerWithMocks();
        Assert.Contains("SET|QLHV-App-LAN-TCP-8088-Private|TCP|8088", calls, StringComparison.Ordinal);
        Assert.Contains("REMOVE|QLHV-App-LAN-Old", calls, StringComparison.Ordinal);
        Assert.DoesNotContain("REMOVE|QLHV-App-LAN-TCP-8088-Private", calls, StringComparison.Ordinal);
        Assert.DoesNotContain("NEW|", calls, StringComparison.Ordinal);
    }

    [Fact]
    public void Updater_normalizes_write_flags_then_protects_the_result_during_ready_rollback()
    {
        var update = Read("Update-QLHV-App.ps1");

        Assert.Contains("appsettings.Production.Local.json", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-FileHash", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set-QlhvProductionWriteFlags", update, StringComparison.Ordinal);
        Assert.Contains("[IO.File]::Replace", update, StringComparison.Ordinal);
        Assert.Contains("Assert-ConfigurationUnchanged", update, StringComparison.Ordinal);
        Assert.Contains("/health/live", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/health/ready", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No SQL patch was run", update, StringComparison.OrdinalIgnoreCase);
        var normalize = update.LastIndexOf("[void](Set-QlhvProductionWriteFlags -Path $ProductionConfig)", StringComparison.Ordinal);
        var protectedHash = update.IndexOf("$productionConfigHash =", normalize, StringComparison.Ordinal);
        Assert.True(normalize >= 0 && protectedHash > normalize,
            "Updater must take its protected hash after the intentional flag normalization.");
    }

    [Theory]
    [InlineData("Install-QLHV-App.ps1")]
    [InlineData("Update-QLHV-App.ps1")]
    public void Production_flag_normalizer_changes_only_two_flags_and_does_not_emit_config_values(string scriptName)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "qlhv-flags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var configurationPath = Path.Combine(temporaryDirectory, "appsettings.Production.Local.json");
        const string sentinel = "do-not-log-or-change-this-value";
        try
        {
            File.WriteAllText(configurationPath, $$"""
                {
                  "ConnectionStrings": { "QLHV_APP": "{{sentinel}}" },
                  "Sync": { "DryRun": true, "BatchSize": 321 },
                  "SyncExecution": { "EnableTargetWrites": false, "RequireManualConfirmation": true },
                  "CustomLocal": { "Sentinel": "{{sentinel}}", "Enabled": true }
                }
                """);

            var firstOutput = InvokeProductionFlagNormalizer(scriptName, configurationPath);
            var hashAfterFirstRun = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(configurationPath)));
            var secondOutput = InvokeProductionFlagNormalizer(scriptName, configurationPath);
            var hashAfterSecondRun = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(configurationPath)));

            Assert.DoesNotContain(sentinel, firstOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, secondOutput, StringComparison.Ordinal);
            Assert.Equal(hashAfterFirstRun, hashAfterSecondRun);

            using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
            var root = document.RootElement;
            Assert.False(root.GetProperty("Sync").GetProperty("DryRun").GetBoolean());
            Assert.Equal(321, root.GetProperty("Sync").GetProperty("BatchSize").GetInt32());
            Assert.True(root.GetProperty("SyncExecution").GetProperty("EnableTargetWrites").GetBoolean());
            Assert.True(root.GetProperty("SyncExecution").GetProperty("RequireManualConfirmation").GetBoolean());
            Assert.Equal(sentinel, root.GetProperty("ConnectionStrings").GetProperty("QLHV_APP").GetString());
            Assert.Equal(sentinel, root.GetProperty("CustomLocal").GetProperty("Sentinel").GetString());
            Assert.True(root.GetProperty("CustomLocal").GetProperty("Enabled").GetBoolean());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void Installer_and_updater_log_original_failures_safely_before_rollback()
    {
        var installer = Read("Install-QLHV-App.ps1");
        var updater = Read("Update-QLHV-App.ps1");

        foreach (var (script, prefix) in new[] { (installer, "installer-"), (updater, "updater-") })
        {
            Assert.Contains("Protect-DeploymentLogMessage", script, StringComparison.Ordinal);
            Assert.Contains("Write-SafeDeploymentFailure", script, StringComparison.Ordinal);
            Assert.Contains(prefix, script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".error.log", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[REDACTED]", script, StringComparison.Ordinal);
            Assert.Contains("1MB", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AddDays(-30)", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$index -ge 14", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".Exception.Message", script, StringComparison.Ordinal);
            Assert.DoesNotContain("ScriptStackTrace", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(
                new Regex(@"Write-SafeDeploymentFailure[^\r\n]*\$(configuration|json|source|target)", RegexOptions.IgnoreCase),
                script);
        }

        var installerLog = installer.IndexOf("Persist the original failure before rollback", StringComparison.Ordinal);
        var installerRollback = installer.IndexOf("if ($newRuntimeInstalled)", installerLog, StringComparison.Ordinal);
        Assert.True(installerLog >= 0 && installerRollback > installerLog,
            "Installer must persist the original failure before runtime rollback.");

        var updaterLog = updater.IndexOf("Persist the original activation/smoke failure", StringComparison.Ordinal);
        var updaterRollback = updater.IndexOf("& $StopScript -Quiet", updaterLog, StringComparison.Ordinal);
        Assert.True(updaterLog >= 0 && updaterRollback > updaterLog,
            "Updater must persist the original failure before runtime rollback.");
    }

    [Theory]
    [InlineData("Install-QLHV-App.ps1", "Password=alpha beta trailing words")]
    [InlineData("Install-QLHV-App.ps1", "ApiToken=red blue trailing words")]
    [InlineData("Update-QLHV-App.ps1", "Password=alpha beta trailing words")]
    [InlineData("Update-QLHV-App.ps1", "ApiToken=red blue trailing words")]
    public void Deployment_log_sanitizer_fails_closed_for_multiword_sensitive_values(
        string scriptName,
        string unsafeMessage)
    {
        var sanitized = InvokeDeploymentSanitizer(scriptName, unsafeMessage);

        Assert.Equal("Sensitive deployment failure details were omitted.", sanitized);
        Assert.DoesNotContain("alpha", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("beta", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trailing", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("red", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blue", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Install-QLHV-App.ps1")]
    [InlineData("Update-QLHV-App.ps1")]
    public void Deployment_log_sanitizer_preserves_a_safe_operational_cause(string scriptName)
    {
        const string message = "Private firewall rule creation failed with exit code 5.";

        Assert.Equal(message, InvokeDeploymentSanitizer(scriptName, message));
    }

    [Fact]
    public void Installer_normal_rollback_is_excluded_from_transition_recovery()
    {
        var installer = Read("Install-QLHV-App.ps1");

        Assert.Contains("$script:RollbackPathEntered = $false", installer, StringComparison.Ordinal);
        Assert.Contains("$script:RollbackPathEntered = $true", installer, StringComparison.Ordinal);
        Assert.Contains("-not $script:RollbackPathEntered", installer, StringComparison.Ordinal);

        var restore = installer.IndexOf("Move-Item -LiteralPath $InstallBackup -Destination $AppDirectory", StringComparison.Ordinal);
        var enterRollback = installer.IndexOf("$script:RollbackPathEntered = $true", restore, StringComparison.Ordinal);
        var verifyRollback = installer.IndexOf("Invoke-StartRuntime -AllowLegacyRollback", enterRollback, StringComparison.Ordinal);
        var transitionGuard = installer.IndexOf("-not $script:RollbackPathEntered", verifyRollback, StringComparison.Ordinal);
        Assert.True(restore >= 0 && enterRollback > restore && verifyRollback > enterRollback && transitionGuard > verifyRollback,
            "Normal rollback must set its path flag before verification and the later transition branch must exclude it.");
    }

    [Fact]
    public void Stop_to_backup_transition_failure_health_checks_prior_runtime_and_leaves_it_non_elevated()
    {
        var installer = Read("Install-QLHV-App.ps1");
        var updater = Read("Update-QLHV-App.ps1");

        foreach (var script in new[] { installer, updater })
        {
            Assert.Contains("ExistingRuntimeWasStopped", script, StringComparison.Ordinal);
            Assert.Contains("-AllowLegacyRollback", script, StringComparison.Ordinal);
            Assert.Contains("legacy-health-compatible", script, StringComparison.Ordinal);
            Assert.Contains("remains installed, was health-checked, and is stopped", script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Start it from the shortcut", script, StringComparison.OrdinalIgnoreCase);
        }

        var updaterTransitionGuard = updater.IndexOf("-not $script:RollbackPathEntered", StringComparison.Ordinal);
        var updaterTransitionStart = updater.IndexOf("Invoke-StartRuntime -AllowLegacyRollback", updaterTransitionGuard, StringComparison.Ordinal);
        var updaterTransitionStop = updater.IndexOf("& $StopScript -Quiet", updaterTransitionStart, StringComparison.Ordinal);
        Assert.True(updaterTransitionGuard >= 0 && updaterTransitionStart > updaterTransitionGuard && updaterTransitionStop > updaterTransitionStart);
    }

    [Fact]
    public void Launcher_runs_the_api_directly_and_prunes_bounded_startup_logs()
    {
        var launcher = Read("Start-QLHV-App.ps1");

        Assert.Contains("QLHV.Api.exe", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QLHV.Api.dll", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RedirectStandardOutput", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RedirectStandardError", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AddDays(-30)", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Run-QLHV-App.ps1", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(@"Get-Process\s+(dotnet|node)(\s|$)", RegexOptions.IgnoreCase),
            launcher);
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

    private static string InvokeDeploymentSanitizer(string scriptName, string message)
    {
        var scriptPath = Path.Combine(FindScriptsDirectory(), scriptName)
            .Replace("'", "''", StringComparison.Ordinal);
        var escapedMessage = message.Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{scriptPath}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count -gt 0) { throw 'Cannot parse deployment script.' }
            $function = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -eq 'Protect-DeploymentLogMessage'
            }, $true)
            if ($null -eq $function) { throw 'Sanitizer function was not found.' }
            Invoke-Expression $function.Extent.Text
            [Console]::Out.Write((Protect-DeploymentLogMessage -Message '{{escapedMessage}}'))
            """;
        return InvokePowerShell(command);
    }

    private static string InvokeProductionFlagNormalizer(string scriptName, string configurationPath)
    {
        var scriptPath = Path.Combine(FindScriptsDirectory(), scriptName)
            .Replace("'", "''", StringComparison.Ordinal);
        var escapedConfigurationPath = configurationPath.Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{scriptPath}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count -gt 0) { throw 'Cannot parse deployment script.' }
            $function = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -eq 'Set-QlhvProductionWriteFlags'
            }, $true)
            if ($null -eq $function) { throw 'Production flag normalizer was not found.' }
            Invoke-Expression $function.Extent.Text
            $aclBefore = (Get-Acl -LiteralPath '{{escapedConfigurationPath}}').Sddl
            [void](Set-QlhvProductionWriteFlags -Path '{{escapedConfigurationPath}}')
            $aclAfter = (Get-Acl -LiteralPath '{{escapedConfigurationPath}}').Sddl
            if ($aclAfter -cne $aclBefore) { throw 'Production configuration ACL changed.' }
            [Console]::Out.Write('normalized')
            """;
        return InvokePowerShell(command);
    }

    private static string InvokeFirewallInstallerWithMocks()
    {
        var scriptPath = Path.Combine(FindScriptsDirectory(), "Install-QLHV-App.ps1")
            .Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{scriptPath}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count -gt 0) { throw 'Cannot parse installer.' }
            $function = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -eq 'Install-FirewallRule'
            }, $true)
            if ($null -eq $function) { throw 'Firewall function was not found.' }
            Invoke-Expression $function.Extent.Text

            $FirewallDisplayName = 'QLHV App LAN - TCP 8088 (Private)'
            $script:calls = [System.Collections.Generic.List[string]]::new()
            function Get-NetFirewallRule {
                [CmdletBinding()]
                param([string]$Name, [string]$DisplayName)
                if ($PSBoundParameters.ContainsKey('Name')) {
                    return [pscustomobject]@{ Name = 'QLHV-App-LAN-TCP-8088-Private' }
                }
                return @(
                    [pscustomobject]@{ Name = 'QLHV-App-LAN-TCP-8088-Private' },
                    [pscustomobject]@{ Name = 'QLHV-App-LAN-Old' }
                )
            }
            function Set-NetFirewallRule {
                [CmdletBinding()]
                param(
                    [string]$Name, [string]$NewDisplayName, [string]$Description,
                    $Enabled, $Direction, $Action, $Profile,
                    [string]$Protocol, [string[]]$LocalPort
                )
                [void]$script:calls.Add("SET|$Name|$Protocol|$($LocalPort -join ',')")
            }
            function New-NetFirewallRule {
                [CmdletBinding()]
                param(
                    [string]$Name, [string]$DisplayName, [string]$Description,
                    $Enabled, $Direction, $Action, $Profile,
                    [string]$Protocol, [string[]]$LocalPort
                )
                [void]$script:calls.Add("NEW|$Name")
            }
            function Remove-NetFirewallRule {
                [CmdletBinding()]
                param([string[]]$Name)
                foreach ($item in $Name) { [void]$script:calls.Add("REMOVE|$item") }
            }

            Install-FirewallRule
            [Console]::Out.Write(($script:calls -join "`n"))
            """;
        return InvokePowerShell(command);
    }

    private static string InvokePowerShell(string command)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell sanitizer test process.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PowerShell sanitizer test timed out.");
        }

        Assert.True(process.ExitCode == 0, $"PowerShell sanitizer test failed: {stderr}");
        return stdout.Trim();
    }

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
