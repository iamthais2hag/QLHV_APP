using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QLHV.Application.Auth;

namespace QLHV.Tests.Hosting;

public sealed class SingleProcessHostingTests
{
    [Fact]
    public async Task Production_host_serves_frontend_assets_and_direct_react_routes()
    {
        using var factory = new LanHostFactory();
        using var client = factory.CreateClient(ClientOptions());

        foreach (var route in new[] { "/", "/login", "/qlhv-import" })
        {
            using var response = await client.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains(LanHostFactory.SpaMarker, body, StringComparison.Ordinal);
        }

        using var asset = await client.GetAsync("/assets/lan-host-test.css");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains(
            "css",
            asset.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lan-host-static-asset", await asset.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spa_fallback_does_not_intercept_api_swagger_health_or_missing_static_files()
    {
        using var factory = new LanHostFactory();
        using var client = factory.CreateClient(ClientOptions());

        using var missingApi = await client.GetAsync("/api/route-that-does-not-exist");
        using var authApi = await client.GetAsync("/api/auth/me");
        using var swagger = await client.GetAsync("/swagger/route-that-does-not-exist");
        using var missingStatic = await client.GetAsync("/assets/missing-file.js");
        using var health = await client.GetAsync("/health");
        using var healthChild = await client.GetAsync("/health/route-that-does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, authApi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingStatic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, healthChild.StatusCode);

        foreach (var response in new[] { missingApi, authApi, swagger, missingStatic, health, healthChild })
        {
            Assert.DoesNotContain(
                LanHostFactory.SpaMarker,
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Login_cookie_works_on_same_origin_http_and_keeps_browser_safety_attributes()
    {
        using var factory = new LanHostFactory();
        using var client = factory.CreateClient(ClientOptions());

        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = LanHostFactory.Username,
            Password = LanHostFactory.Password,
        });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        var attributes = setCookie
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .ToArray();
        Assert.Contains(attributes, value => value.Equals("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(attributes, value => value.Equals("samesite=lax", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(attributes, value => value.Equals("secure", StringComparison.OrdinalIgnoreCase));

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public void Client_api_sources_are_same_origin_and_do_not_require_a_production_api_environment_variable()
    {
        var clientSource = FindWorkspaceDirectory("client", "src");
        var apiSources = Directory.GetFiles(clientSource, "api.ts", SearchOption.AllDirectories);
        var apiBaseSource = File.ReadAllText(Path.Combine(clientSource, "api", "apiBase.ts"));

        Assert.NotEmpty(apiSources);
        Assert.Contains("export const API_BASE = '/api'", apiBaseSource, StringComparison.Ordinal);
        Assert.DoesNotContain("VITE_API_BASE_URL", apiBaseSource, StringComparison.Ordinal);
        foreach (var apiSourcePath in apiSources)
        {
            var source = File.ReadAllText(apiSourcePath);
            Assert.Contains("API_BASE", source, StringComparison.Ordinal);
            Assert.DoesNotContain("http://localhost", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://localhost", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", source, StringComparison.OrdinalIgnoreCase);
        }

        var productionSources = string.Join(
            '\n',
            Directory.GetFiles(clientSource, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        Assert.DoesNotContain("localhost:5130", productionSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", productionSources, StringComparison.OrdinalIgnoreCase);

        var distDirectory = Path.Combine(FindWorkspaceRoot(), "client", "dist");
        if (Directory.Exists(distDirectory))
        {
            var productionBundle = string.Join(
                '\n',
                Directory.GetFiles(distDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));
            Assert.DoesNotContain("http://localhost", productionBundle, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://localhost", productionBundle, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", productionBundle, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Api_project_build_contract_embeds_client_dist_and_excludes_development_settings()
    {
        var project = File.ReadAllText(FindWorkspaceFile("server", "QLHV.Api", "QLHV.Api.csproj"));

        Assert.Contains("npm", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run build", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dist", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wwwroot", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appsettings.Development.json", project, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            project.Contains("CopyToPublishDirectory=\"Never\"", StringComparison.OrdinalIgnoreCase) ||
            project.Contains("<CopyToPublishDirectory>Never</CopyToPublishDirectory>", StringComparison.OrdinalIgnoreCase),
            "The API publish must exclude appsettings.Development.json.");
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("http://localhost"),
    };

    private static string FindWorkspaceFile(params string[] pathParts)
    {
        var path = Path.Combine(new[] { FindWorkspaceRoot() }.Concat(pathParts).ToArray());
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Cannot locate workspace file.", path);
        }

        return path;
    }

    private static string FindWorkspaceDirectory(params string[] pathParts)
    {
        var path = Path.Combine(new[] { FindWorkspaceRoot() }.Concat(pathParts).ToArray());
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        return path;
    }

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

    private sealed class LanHostFactory : WebApplicationFactory<Program>
    {
        public const string SpaMarker = "qlhv-lan-spa-fixture";
        public const string Username = "lan-admin";
        public const string Password = "lan-test-password";

        private readonly string _webRoot = Path.Combine(
            Path.GetTempPath(),
            $"qlhv-lan-host-tests-{Guid.NewGuid():N}");
        private readonly TestUserRepository _users = new();

        public LanHostFactory()
        {
            Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
            File.WriteAllText(
                Path.Combine(_webRoot, "index.html"),
                $"<!doctype html><html><body><div id=\"{SpaMarker}\"></div></body></html>");
            File.WriteAllText(
                Path.Combine(_webRoot, "assets", "lan-host-test.css"),
                "/* lan-host-static-asset */ body { color: black; }");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseWebRoot(_webRoot);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HttpsRedirection:Enabled"] = "false",
                    ["Authentication:Cookie:SecurePolicy"] = "SameAsRequest",
                    ["Sync:DryRun"] = "true",
                    ["SyncExecution:EnableTargetWrites"] = "false",
                    ["ConnectionStrings:QLHV_APP"] =
                        "Server=__TEST_SERVER__;Database=__TEST_DATABASE__;Integrated Security=True;",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthService>();
                services.RemoveAll<IAppUserRepository>();
                services.AddSingleton<IAuthService, TestAuthService>();
                services.AddSingleton<IAppUserRepository>(_users);
                services.RemoveAll<IHostedService>();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            try
            {
                Directory.Delete(_webRoot, recursive: true);
            }
            catch (IOException)
            {
                // TestServer can release static-file handles just after factory disposal.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup only; the unique folder is under the OS temp path.
            }
        }
    }

    private sealed class TestAuthService : IAuthService
    {
        public Task<AuthLoginResult> AuthenticateAsync(
            LoginRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var valid = request.Username == LanHostFactory.Username &&
                        request.Password == LanHostFactory.Password;
            return Task.FromResult(valid
                ? new AuthLoginResult
                {
                    Succeeded = true,
                    Session = new AuthSessionDto
                    {
                        Id = 100,
                        Username = LanHostFactory.Username,
                        DisplayName = "LAN test administrator",
                        Role = AppRoles.Admin,
                        Roles = [AppRoles.Admin],
                    },
                }
                : AuthLoginResult.InvalidCredentials());
        }
    }

    private sealed class TestUserRepository : IAppUserRepository
    {
        private readonly AppUserCredential _user;

        public TestUserRepository()
        {
            var prototype = new AppUserCredential
            {
                Id = 100,
                Username = LanHostFactory.Username,
                DisplayName = "LAN test administrator",
                IsActive = true,
                Roles = [AppRoles.Admin],
            };
            _user = new AppUserCredential
            {
                Id = prototype.Id,
                Username = prototype.Username,
                DisplayName = prototype.DisplayName,
                IsActive = prototype.IsActive,
                Roles = prototype.Roles,
                PasswordHash = new PasswordHasher<AppUserCredential>()
                    .HashPassword(prototype, LanHostFactory.Password),
            };
        }

        public Task<AppUserCredential?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppUserCredential?>(
                username.Equals(_user.Username, StringComparison.OrdinalIgnoreCase) ? _user : null);

        public Task<AppUserCredential?> FindByIdAsync(
            long userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppUserCredential?>(userId == _user.Id ? _user : null);

        public Task RecordSuccessfulLoginAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecordFailedLoginAsync(
            long userId,
            DateTime failedAtUtc,
            DateTime resetCutoffUtc,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdatePasswordHashAsync(
            long userId,
            string passwordHash,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FirstAdminCreateResult> TryCreateFirstAdminAsync(
            string username,
            string displayName,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FirstAdminCreateResult(
                FirstAdminCreateStatus.AdminAlreadyExists,
                _user.Id));
    }
}
