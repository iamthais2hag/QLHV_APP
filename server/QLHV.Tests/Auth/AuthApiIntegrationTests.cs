using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Tests.Auth;

public sealed class AuthApiIntegrationTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public AuthApiIntegrationTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_write_requests_return_401()
    {
        using var client = CreateClient();

        var import = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/import-execute",
            ImportRequest());
        var refresh = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/refresh-backup",
            new QlhvRefreshBackupRequest { SourceType = "OTO" });

        Assert.Equal(HttpStatusCode.Unauthorized, import.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Viewer_write_requests_return_403()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer", AuthApiFactory.ViewerPassword);

        var import = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/import-execute",
            ImportRequest());
        var refresh = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/refresh-backup",
            new QlhvRefreshBackupRequest { SourceType = "OTO" });
        var saveProfile = await client.PutAsJsonAsync(
            "/api/csdt-connection-profiles/CSDT_OTO",
            new { displayName = "must-not-reach-service" });
        var testProfile = await client.PostAsJsonAsync(
            "/api/csdt-connection-profiles/CSDT_OTO/test",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saveProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, testProfile.StatusCode);
    }

    [Fact]
    public async Task Viewer_can_reach_required_read_only_qlhv_endpoints()
    {
        using var client = CreateClient();
        await LoginAsync(client, "viewer", AuthApiFactory.ViewerPassword);

        var responses = new[]
        {
            await client.GetAsync(
                "/api/dong-bo-v2/qlhv/import-plan?sourceProfileCode=INVALID&maCSDT=invalid&maKhoaHoc="),
            await client.GetAsync(
                "/api/dong-bo-v2/qlhv/import-diagnostics?sourceProfileCode=INVALID&maCSDT=invalid&maKhoaHoc="),
            await client.GetAsync("/api/dong-bo-v2/qlhv/operations/status?sourceType=INVALID"),
            await client.GetAsync("/api/dong-bo-v2/qlhv/operations/history?sourceType=INVALID"),
        };

        Assert.All(responses, response =>
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        });
    }

    [Fact]
    public async Task Admin_reaches_existing_dry_run_guards_without_touching_database()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin", AuthApiFactory.AdminPassword);

        var importResponse = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/import-execute",
            ImportRequest());
        var importResult = await importResponse.Content.ReadFromJsonAsync<QlhvImportExecuteResultDto>();

        Assert.Equal(HttpStatusCode.Conflict, importResponse.StatusCode);
        Assert.NotNull(importResult);
        Assert.False(importResult.Executed);
        Assert.Contains(
            importResult.Plan.Blockers,
            blocker => blocker.Contains("Sync:DryRun = true", StringComparison.Ordinal));

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/refresh-backup",
            new QlhvRefreshBackupRequest { SourceType = "OTO" });
        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<QlhvRefreshBackupResultDto>();

        Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);
        Assert.NotNull(refreshResult);
        Assert.False(refreshResult.Accepted);
        Assert.Contains("Sync:DryRun = true", refreshResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_invalidates_the_cookie_session()
    {
        using var client = CreateClient();
        await LoginAsync(client, "admin", AuthApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_issuing_a_session()
    {
        using var client = CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = "wrong-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Inactive_user_is_rejected_without_issuing_a_session()
    {
        using var client = CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "inactive",
            Password = AuthApiFactory.InactivePassword,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Current_role_is_reloaded_before_authorizing_each_request()
    {
        using var client = CreateClient();
        await LoginAsync(client, "role-admin", AuthApiFactory.SessionPassword);
        _factory.SetUserState("role-admin", roles: [AppRoles.Viewer]);

        var write = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/import-execute",
            ImportRequest());
        var me = await client.GetFromJsonAsync<AuthSessionDto>("/api/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.NotNull(me);
        Assert.Equal(AppRoles.Viewer, me.Role);
        Assert.DoesNotContain(AppRoles.Admin, me.Roles);
    }

    [Theory]
    [InlineData("disabled-session", false, false)]
    [InlineData("deleted-session", true, true)]
    public async Task Disabled_or_deleted_account_loses_existing_cookie_session(
        string username,
        bool isActive,
        bool isDeleted)
    {
        using var client = CreateClient();
        await LoginAsync(client, username, AuthApiFactory.SessionPassword);
        _factory.SetUserState(username, isActive, isDeleted);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_temporarily_lock_the_account()
    {
        using var client = CreateClient();
        for (var attempt = 0; attempt < AuthService.MaxFailedLoginAttempts; attempt++)
        {
            var rejected = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "lockout",
                Password = "wrong-password",
            });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        var correctWhileLocked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "lockout",
            Password = AuthApiFactory.SessionPassword,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, correctWhileLocked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = username,
            Password = password,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static QlhvImportExecuteRequest ImportRequest() => new()
    {
        SourceProfileCode = "CSDT_OTO",
        MaCSDT = "66029",
        ExpectedSnapshotToken = "test-snapshot",
    };
}

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "admin-password-for-tests";
    public const string ViewerPassword = "viewer-password-for-tests";
    public const string InactivePassword = "inactive-password-for-tests";
    public const string SessionPassword = "session-password-for-tests";

    private readonly InMemoryAppUserRepository _users = new();

    public void SetUserState(
        string username,
        bool? isActive = null,
        bool? isDeleted = null,
        IReadOnlyList<string>? roles = null) =>
        _users.SetUserState(username, isActive, isDeleted, roles);

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
                ["ConnectionStrings:QLHV_APP"] =
                    "Server=__TEST_SERVER__;Database=__TEST_DATABASE__;Integrated Security=True;",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAppUserRepository>();
            services.AddSingleton<IAppUserRepository>(_users);
            services.RemoveAll<IHostedService>();
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
        });
    }

    private sealed class InMemoryAppUserRepository : IAppUserRepository
    {
        private readonly Dictionary<string, AppUserCredential> _users;
        private readonly object _gate = new();

        public InMemoryAppUserRepository()
        {
            var hasher = new PasswordHasher<AppUserCredential>();
            _users = new[]
                {
                    CreateUser(hasher, 1, "admin", AdminPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 2, "viewer", ViewerPassword, true, AppRoles.Viewer),
                    CreateUser(hasher, 3, "inactive", InactivePassword, false, AppRoles.Admin),
                    CreateUser(hasher, 4, "role-admin", SessionPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 5, "disabled-session", SessionPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 6, "deleted-session", SessionPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 7, "lockout", SessionPassword, true, AppRoles.Admin),
                }
                .ToDictionary(user => user.Username, StringComparer.OrdinalIgnoreCase);
        }

        public Task<AppUserCredential?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_users.GetValueOrDefault(username));
            }
        }

        public Task<AppUserCredential?> FindByIdAsync(
            long userId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_users.Values.FirstOrDefault(user => user.Id == userId));
            }
        }

        public Task RecordSuccessfulLoginAsync(
            long userId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = FindRequired(userId);
                _users[user.Username] = Copy(user, failedLoginCount: 0, updatedAtUtc: DateTime.UtcNow);
            }

            return Task.CompletedTask;
        }

        public Task RecordFailedLoginAsync(
            long userId,
            DateTime failedAtUtc,
            DateTime resetCutoffUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = FindRequired(userId);
                var failedLoginCount = user.UpdatedAtUtc is null || user.UpdatedAtUtc < resetCutoffUtc
                    ? 1
                    : user.FailedLoginCount == int.MaxValue
                        ? int.MaxValue
                        : user.FailedLoginCount + 1;
                _users[user.Username] = Copy(user, failedLoginCount: failedLoginCount, updatedAtUtc: failedAtUtc);
            }

            return Task.CompletedTask;
        }

        public Task UpdatePasswordHashAsync(
            long userId,
            string passwordHash,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FirstAdminCreateResult> TryCreateFirstAdminAsync(
            string username,
            string displayName,
            string passwordHash,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new FirstAdminCreateResult(FirstAdminCreateStatus.AdminAlreadyExists, null));

        public void SetUserState(
            string username,
            bool? isActive,
            bool? isDeleted,
            IReadOnlyList<string>? roles)
        {
            lock (_gate)
            {
                var user = _users[username];
                _users[username] = Copy(
                    user,
                    isActive: isActive,
                    isDeleted: isDeleted,
                    roles: roles);
            }
        }

        private AppUserCredential FindRequired(long userId) =>
            _users.Values.Single(user => user.Id == userId);

        private static AppUserCredential Copy(
            AppUserCredential user,
            bool? isActive = null,
            bool? isDeleted = null,
            IReadOnlyList<string>? roles = null,
            int? failedLoginCount = null,
            DateTime? updatedAtUtc = null) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            PasswordHash = user.PasswordHash,
            IsActive = isActive ?? user.IsActive,
            IsDeleted = isDeleted ?? user.IsDeleted,
            Roles = roles ?? user.Roles,
            FailedLoginCount = failedLoginCount ?? user.FailedLoginCount,
            UpdatedAtUtc = updatedAtUtc ?? user.UpdatedAtUtc,
        };

        private static AppUserCredential CreateUser(
            IPasswordHasher<AppUserCredential> hasher,
            long id,
            string username,
            string password,
            bool isActive,
            string role)
        {
            var user = new AppUserCredential
            {
                Id = id,
                Username = username,
                DisplayName = $"{username} test user",
                IsActive = isActive,
                Roles = [role],
            };
            return new AppUserCredential
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                PasswordHash = hasher.HashPassword(user, password),
                IsActive = user.IsActive,
                Roles = user.Roles,
            };
        }
    }
}
