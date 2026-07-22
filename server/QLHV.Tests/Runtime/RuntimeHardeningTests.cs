using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QLHV.Api.Runtime;
using QLHV.Application.Auth;
using QLHV.Application.Runtime;
using QLHV.Infrastructure.Runtime;

namespace QLHV.Tests.Runtime;

public sealed class RuntimeHardeningTests
{
    [Fact]
    public async Task Missing_production_local_configuration_keeps_readiness_failed()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"qlhv-missing-{Guid.NewGuid():N}.json");
        var service = CreateReadiness(
            new RuntimeConfigurationState(missingPath, true, false, false),
            ReadyProbe());

        var status = await service.GetStatusAsync();

        Assert.False(status.IsReady);
        Assert.False(status.ConfigurationReady);
        Assert.Contains(
            status.Messages,
            message => message == $"Thiếu hoặc sai cấu hình QLHV_APP. Kiểm tra: {missingPath}");
    }

    [Fact]
    public async Task Wrong_database_name_keeps_readiness_failed()
    {
        var probe = ReadyProbe(databaseName: "master");

        var status = await CreateReadiness(ValidConfiguration(), probe).GetStatusAsync();

        Assert.False(status.IsReady);
        Assert.True(status.DatabaseConnected);
        Assert.Equal("master", status.DatabaseName);
        Assert.Contains("Database hiện tại không phải QLHV_APP.", status.Messages);
    }

    [Fact]
    public async Task Missing_required_schema_keeps_readiness_failed()
    {
        var probe = WithMessages(
            ReadyProbe(requiredSchemaReady: false),
            "Thiếu bảng bắt buộc: App_User.");

        var status = await CreateReadiness(ValidConfiguration(), probe).GetStatusAsync();

        Assert.False(status.IsReady);
        Assert.False(status.RequiredSchemaReady);
        Assert.Contains("Thiếu bảng bắt buộc: App_User.", status.Messages);
    }

    [Fact]
    public async Task Missing_active_admin_keeps_readiness_failed()
    {
        var probe = WithMessages(
            ReadyProbe(authenticationReady: false),
            "Chưa có tài khoản Admin đang hoạt động.");

        var status = await CreateReadiness(ValidConfiguration(), probe).GetStatusAsync();

        Assert.False(status.IsReady);
        Assert.False(status.AuthenticationReady);
        Assert.Contains("Chưa có tài khoản Admin đang hoạt động.", status.Messages);
    }

    [Fact]
    public async Task Complete_probe_reports_runtime_ready()
    {
        var status = await CreateReadiness(ValidConfiguration(), ReadyProbe()).GetStatusAsync();

        Assert.True(status.IsReady);
        Assert.True(status.ConfigurationReady);
        Assert.True(status.DatabaseConnected);
        Assert.True(status.RequiredSchemaReady);
        Assert.True(status.AuthenticationReady);
        Assert.True(status.BackupProfilesReady);
        Assert.True(status.BackupStorageReady);
        Assert.True(status.FileStorageReady);
        Assert.True(status.RuntimeStorageReady);
        Assert.DoesNotContain(status.Messages, message => message.Contains("Server=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Concurrent_readiness_requests_share_one_short_lived_probe()
    {
        var probe = new CountingProbe(ReadyProbe());
        var service = new RuntimeReadinessService(
            ValidConfiguration(),
            probe,
            ProductionEnvironment(),
            Options.Create(new QlhvRuntimeOptions { ReadinessCacheSeconds = 10 }),
            new RuntimeReadinessCache());

        var statuses = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => service.GetStatusAsync()));

        Assert.All(statuses, status => Assert.True(status.IsReady));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void Production_local_loader_reports_missing_and_malformed_files_without_throwing()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var missing = Path.Combine(root, "missing.json");
            var missingConfiguration = ConfigurationWithPath(missing);
            var missingState = ProductionLocalConfigurationLoader.Load(
                missingConfiguration,
                ProductionEnvironment(),
                []);
            Assert.False(missingState.IsReady);
            Assert.False(missingState.Exists);

            var malformed = Path.Combine(root, "malformed.json");
            File.WriteAllText(malformed, "{ not-json", System.Text.Encoding.UTF8);
            var malformedConfiguration = ConfigurationWithPath(malformed);
            var malformedState = ProductionLocalConfigurationLoader.Load(
                malformedConfiguration,
                ProductionEnvironment(),
                []);
            Assert.False(malformedState.IsReady);
            Assert.True(malformedState.Exists);
            Assert.False(malformedState.IsValid);

            var duplicateKey = Path.Combine(root, "duplicate-key.json");
            File.WriteAllText(
                duplicateKey,
                """{"ConnectionStrings":{"QLHV_APP":"first","QLHV_APP":"second"}}""",
                System.Text.Encoding.UTF8);
            var duplicateKeyState = ProductionLocalConfigurationLoader.Load(
                ConfigurationWithPath(duplicateKey),
                ProductionEnvironment(),
                []);
            Assert.False(duplicateKeyState.IsReady);
            Assert.True(duplicateKeyState.Exists);
            Assert.False(duplicateKeyState.IsValid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Production_local_loader_rejects_invalid_path_without_crashing()
    {
        var configuration = new ConfigurationManager();
        configuration["QlhvRuntime:ProductionLocalConfigPath"] = "invalid\0path.json";

        var state = ProductionLocalConfigurationLoader.Load(
            configuration,
            ProductionEnvironment(),
            []);

        Assert.False(state.IsReady);
        Assert.False(state.Exists);
        Assert.False(state.IsValid);
        Assert.Equal("[invalid Production.Local configuration path]", state.Path);
    }

    [Fact]
    public void Backup_readiness_uses_the_executor_path_on_each_bak_connection()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Runtime",
            "SqlServerRuntimeReadinessProbe.cs"));

        Assert.True(
            source.Split("QlhvBackupRefreshExecutor.BackupDirectory", StringSplitOptions.None).Length - 1 >= 2);
        Assert.Contains("CsdtOtoBak", source, StringComparison.Ordinal);
        Assert.Contains("CsdtMotoBak", source, StringComparison.Ordinal);
        Assert.Contains("xp_fileexist", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Environment_and_command_line_keep_precedence_over_production_local_json()
    {
        const string environmentName = "RuntimeHardeningPrecedence__Value";
        var previous = Environment.GetEnvironmentVariable(environmentName);
        var root = CreateTemporaryDirectory();
        try
        {
            var localPath = Path.Combine(root, "production-local.json");
            File.WriteAllText(
                localPath,
                """{"RuntimeHardeningPrecedence":{"Value":"file"}}""",
                System.Text.Encoding.UTF8);
            Environment.SetEnvironmentVariable(environmentName, "environment");

            var environmentConfiguration = ConfigurationWithPath(localPath);
            var environmentState = ProductionLocalConfigurationLoader.Load(
                environmentConfiguration,
                ProductionEnvironment(),
                []);
            Assert.True(environmentState.IsReady);
            Assert.Equal("environment", environmentConfiguration["RuntimeHardeningPrecedence:Value"]);

            var commandLineConfiguration = ConfigurationWithPath(localPath);
            var commandLineState = ProductionLocalConfigurationLoader.Load(
                commandLineConfiguration,
                ProductionEnvironment(),
                ["--RuntimeHardeningPrecedence:Value=command-line"]);
            Assert.True(commandLineState.IsReady);
            Assert.Equal("command-line", commandLineConfiguration["RuntimeHardeningPrecedence:Value"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rolling_logger_redacts_sensitive_values_and_bounds_retention()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var provider = new QlhvRollingFileLoggerProvider(
                root,
                maxFileSizeBytes: 64 * 1024,
                retainedFileCount: 2);
            var logger = provider.CreateLogger("QLHV.Tests.Runtime");
            var largeSafeMessage = new string('x', 70 * 1024);
            for (var index = 0; index < 5; index++)
            {
                logger.LogInformation("{Index} {Message}", index, largeSafeMessage);
            }

            logger.LogError(
                new InvalidOperationException("Server=secret;Password=hidden"),
                "Connection failed: Server=secret; Database=QLHV_APP; Password=a value with spaces");

            var files = Directory.GetFiles(root, "qlhv-api-*.log", SearchOption.TopDirectoryOnly);
            Assert.InRange(files.Length, 1, 2);
            var content = string.Join(Environment.NewLine, files.Select(File.ReadAllText));
            Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hidden", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("a value with spaces", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InvalidOperationException: Server", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[REDACTED SENSITIVE LOG MESSAGE]", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Health_and_runtime_status_distinguish_live_from_not_ready()
    {
        await using var factory = new RuntimeApiFactory(new StaticReadinessService(NotReadyStatus()));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);

        var runtimeResponse = await client.GetAsync("/api/system/runtime-status");
        Assert.Equal(HttpStatusCode.OK, runtimeResponse.StatusCode);
        var status = await runtimeResponse.Content.ReadFromJsonAsync<RuntimeStatusDto>();
        Assert.NotNull(status);
        Assert.False(status.IsReady);
    }

    [Fact]
    public async Task Login_returns_safe_503_with_correlation_id_when_runtime_is_not_ready()
    {
        await using var factory = new RuntimeApiFactory(new StaticReadinessService(NotReadyStatus()));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = "not-logged-or-checked",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Hệ thống chưa sẵn sàng. Vui lòng liên hệ quản trị viên.", problem.Detail);
        Assert.True(problem.Extensions.ContainsKey("correlationId"));
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.DoesNotContain("not-logged-or-checked", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authentication_store_exception_is_translated_to_safe_503()
    {
        await using var factory = new RuntimeApiFactory(
            new StaticReadinessService(ReadyStatus()),
            new ThrowingAuthService());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = "not-a-real-secret",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Server=private", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=private", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
    }

    private static RuntimeReadinessService CreateReadiness(
        RuntimeConfigurationState configuration,
        RuntimeReadinessProbeResult probe) => new(
            configuration,
            new StaticProbe(probe),
            ProductionEnvironment());

    private static RuntimeConfigurationState ValidConfiguration() =>
        new(@"D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json", true, true, true);

    private static RuntimeReadinessProbeResult ReadyProbe(
        string databaseName = "QLHV_APP",
        bool requiredSchemaReady = true,
        bool authenticationReady = true) => new()
    {
        DatabaseConnected = true,
        DatabaseName = databaseName,
        RequiredSchemaReady = requiredSchemaReady,
        AuthenticationReady = authenticationReady,
        BackupProfilesReady = true,
        BackupStorageReady = true,
        FileStorageReady = true,
        RuntimeStorageReady = true,
    };

    private static RuntimeReadinessProbeResult WithMessages(
        RuntimeReadinessProbeResult probe,
        params string[] messages) => new()
    {
        DatabaseConnected = probe.DatabaseConnected,
        DatabaseName = probe.DatabaseName,
        RequiredSchemaReady = probe.RequiredSchemaReady,
        AuthenticationReady = probe.AuthenticationReady,
        BackupProfilesReady = probe.BackupProfilesReady,
        BackupStorageReady = probe.BackupStorageReady,
        FileStorageReady = probe.FileStorageReady,
        RuntimeStorageReady = probe.RuntimeStorageReady,
        Messages = messages,
    };

    private static ConfigurationManager ConfigurationWithPath(string path)
    {
        var configuration = new ConfigurationManager();
        configuration["QlhvRuntime:ProductionLocalConfigPath"] = path;
        return configuration;
    }

    private static TestWebHostEnvironment ProductionEnvironment() => new()
    {
        EnvironmentName = Environments.Production,
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qlhv-runtime-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindWorkspaceFile(
        string firstPart,
        params string[] remainingParts)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(TestSourcePath())!);
        while (directory is not null)
        {
            var path = Path.Combine(
                new[] { directory.FullName, firstPart }.Concat(remainingParts).ToArray());
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.");
    }

    private static string TestSourcePath([CallerFilePath] string path = "") => path;

    private static RuntimeStatusDto NotReadyStatus() => new()
    {
        IsReady = false,
        Version = "test",
        Environment = "Testing",
        ConfigurationReady = false,
        CheckedAtUtc = DateTime.UtcNow,
        Messages = ["Thiếu hoặc sai cấu hình QLHV_APP. Kiểm tra: test-path"],
    };

    private static RuntimeStatusDto ReadyStatus() => new()
    {
        IsReady = true,
        Version = "test",
        Environment = "Testing",
        ConfigurationReady = true,
        DatabaseConnected = true,
        DatabaseName = "QLHV_APP",
        AuthenticationReady = true,
        RequiredSchemaReady = true,
        BackupProfilesReady = true,
        BackupStorageReady = true,
        FileStorageReady = true,
        RuntimeStorageReady = true,
        CheckedAtUtc = DateTime.UtcNow,
        Messages = ["Hệ thống sẵn sàng."],
    };

    private sealed class StaticProbe : IRuntimeReadinessProbe
    {
        private readonly RuntimeReadinessProbeResult _result;

        public StaticProbe(RuntimeReadinessProbeResult result)
        {
            _result = result;
        }

        public Task<RuntimeReadinessProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }

    private sealed class CountingProbe : IRuntimeReadinessProbe
    {
        private readonly RuntimeReadinessProbeResult _result;
        private int _calls;

        public CountingProbe(RuntimeReadinessProbeResult result)
        {
            _result = result;
        }

        public int Calls => Volatile.Read(ref _calls);

        public async Task<RuntimeReadinessProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(50, cancellationToken);
            return _result;
        }
    }

    private sealed class StaticReadinessService : IRuntimeReadinessService
    {
        private readonly RuntimeStatusDto _status;

        public StaticReadinessService(RuntimeStatusDto status)
        {
            _status = status;
        }

        public Task<RuntimeStatusDto> GetStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(_status);
    }

    private sealed class ThrowingAuthService : IAuthService
    {
        public Task<AuthLoginResult> AuthenticateAsync(
            LoginRequestDto request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Server=private;Database=QLHV_APP;Password=private; stack should stay private");
    }

    private sealed class RuntimeApiFactory : WebApplicationFactory<Program>
    {
        private readonly IRuntimeReadinessService _readiness;
        private readonly IAuthService? _auth;

        public RuntimeApiFactory(IRuntimeReadinessService readiness, IAuthService? auth = null)
        {
            _readiness = readiness;
            _auth = auth;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HttpsRedirection:Enabled"] = "false",
                    ["Sync:DryRun"] = "true",
                    ["SyncExecution:EnableTargetWrites"] = "false",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRuntimeReadinessService>();
                services.AddSingleton(_readiness);
                if (_auth is not null)
                {
                    services.RemoveAll<IAuthService>();
                    services.AddSingleton(_auth);
                }

                services.RemoveAll<IHostedService>();
            });
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "QLHV.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = Path.GetTempPath();

        public string EnvironmentName { get; set; } = Environments.Production;

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
