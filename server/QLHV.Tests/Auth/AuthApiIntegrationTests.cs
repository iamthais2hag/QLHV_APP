using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QLHV.Application.Auth;
using QLHV.Application.HocVien;
using QLHV.Application.HocVien.Dtos;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;
using QLHV.Shared.Paging;

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
        var autoSync = await client.PostAsync(
            "/api/dong-bo-v2/qlhv/operations/auto-sync",
            null);
        var ensureFresh = await client.PostAsync(
            "/api/dong-bo-v2/qlhv/operations/ensure-fresh",
            null);
        var sessionStart = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/session-start-sync",
            new QlhvSessionStartSyncRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, import.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, autoSync.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ensureFresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sessionStart.StatusCode);
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
        var autoSync = await client.PostAsync(
            "/api/dong-bo-v2/qlhv/operations/auto-sync",
            null);
        var ensureFresh = await client.PostAsync(
            "/api/dong-bo-v2/qlhv/operations/ensure-fresh",
            null);
        var sessionStart = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/session-start-sync",
            new QlhvSessionStartSyncRequest());

        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saveProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, testProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, autoSync.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ensureFresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sessionStart.StatusCode);
    }

    [Theory]
    [InlineData("admin", AuthApiFactory.AdminPassword)]
    [InlineData("employee", AuthApiFactory.EmployeePassword)]
    public async Task Admin_and_Employee_can_request_app_open_ensure_fresh(
        string username,
        string password)
    {
        var autoSync = new RecordingAutoSyncService();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IQlhvAutoSyncService>();
                services.AddSingleton<IQlhvAutoSyncService>(autoSync);
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        await LoginAsync(client, username, password);

        var response = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/ensure-fresh" +
            "?force=true&sourceType=MOTO&actor=ATTACKER&cooldownSeconds=0",
            new
            {
                force = true,
                source = "MOTO",
                actor = "ATTACKER",
                cooldownSeconds = 0,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, autoSync.EnsureFreshCalls);
        Assert.Equal(0, autoSync.ManualQueueCalls);
    }

    [Fact]
    public async Task Employee_can_login_and_me_reports_the_Employee_role()
    {
        using var factory = new AuthApiFactory();
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "employee",
            Password = AuthApiFactory.EmployeePassword,
        });
        var loginSession = await login.Content.ReadFromJsonAsync<AuthSessionDto>();
        var me = await client.GetFromJsonAsync<AuthSessionDto>("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotNull(loginSession);
        Assert.Equal(AppRoles.Employee, loginSession.Role);
        Assert.Equal([AppRoles.Employee], loginSession.Roles);
        Assert.False(loginSession.MustChangePassword);
        Assert.NotNull(me);
        Assert.Equal("employee", me.Username);
        Assert.Equal(AppRoles.Employee, me.Role);
        Assert.Equal([AppRoles.Employee], me.Roles);
        Assert.False(me.MustChangePassword);
    }

    [Fact]
    public async Task Employee_can_read_export_and_print_but_cannot_use_Admin_operations()
    {
        using var factory = new AuthApiFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "employee", AuthApiFactory.EmployeePassword);

        var businessRead = await client.GetAsync("/api/hoc-vien?page=1&pageSize=20");
        var export = await client.GetAsync("/api/hoc-vien/export-excel?page=1&pageSize=20");
        var printPreview = await client.PostAsJsonAsync(
            "/api/hoc-vien/the-hoc-vien/print-preview",
            new HocVienCardPrintRequest
            {
                Mode = "selected",
                HocVienIds = [1],
            });

        var import = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/import-execute",
            ImportRequest());
        var refresh = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/refresh-backup",
            new QlhvRefreshBackupRequest { SourceType = "OTO" });
        var manualAutoSync = await client.PostAsync(
            "/api/dong-bo-v2/qlhv/operations/auto-sync",
            null);
        var sessionStart = await client.PostAsJsonAsync(
            "/api/dong-bo-v2/qlhv/operations/session-start-sync",
            new QlhvSessionStartSyncRequest());
        var users = await client.GetAsync("/api/admin/users");
        var saveProfile = await client.PutAsJsonAsync(
            "/api/csdt-connection-profiles/CSDT_OTO",
            new { displayName = "must-not-reach-service" });
        var testProfile = await client.PostAsJsonAsync(
            "/api/csdt-connection-profiles/CSDT_OTO/test",
            new { });

        Assert.Equal(HttpStatusCode.OK, businessRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(HttpStatusCode.OK, printPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, import.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, manualAutoSync.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sessionStart.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saveProfile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, testProfile.StatusCode);
    }

    [Fact]
    public async Task Must_change_password_user_can_only_use_session_endpoints_until_password_changes()
    {
        using var factory = new AuthApiFactory();
        using var client = CreateClient(factory);
        var replacementPassword = "must-change-replacement-password";

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "must-change",
            Password = AuthApiFactory.MustChangePassword,
        });
        var loginSession = await login.Content.ReadFromJsonAsync<AuthSessionDto>();

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotNull(loginSession);
        Assert.True(loginSession.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/hoc-vien")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync(
                "/api/dong-bo-v2/qlhv/operations/ensure-fresh",
                null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/users")).StatusCode);

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangeOwnPasswordRequestDto
            {
                CurrentPassword = AuthApiFactory.MustChangePassword,
                NewPassword = replacementPassword,
            });
        var me = await client.GetFromJsonAsync<AuthSessionDto>("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.NotNull(me);
        Assert.False(me.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/hoc-vien")).StatusCode);

        using var oldPasswordClient = CreateClient(factory);
        using var newPasswordClient = CreateClient(factory);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await oldPasswordClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "must-change",
                Password = AuthApiFactory.MustChangePassword,
            })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await newPasswordClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "must-change",
                Password = replacementPassword,
            })).StatusCode);
    }

    [Fact]
    public async Task Must_change_password_user_can_logout()
    {
        using var factory = new AuthApiFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "must-change", AuthApiFactory.MustChangePassword);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_user_api_supports_Employee_account_lifecycle_without_exposing_secrets()
    {
        using var factory = new AuthApiFactory();
        using var adminClient = CreateClient(factory);
        await LoginAsync(adminClient, "admin", AuthApiFactory.AdminPassword);
        var username = $"employee-{Guid.NewGuid():N}";
        var temporaryPassword = "temporary-employee-password";
        var replacementPassword = "replacement-employee-password";

        var create = await adminClient.PostAsJsonAsync(
            "/api/admin/users",
            new CreateAppUserRequestDto
            {
                Username = username,
                DisplayName = "New Employee",
                Role = AppRoles.Employee,
                TemporaryPassword = temporaryPassword,
                IsActive = true,
                MustChangePassword = false,
            });
        var createJson = await create.Content.ReadAsStringAsync();
        var created = await create.Content.ReadFromJsonAsync<AppUserListItemDto>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(created);
        Assert.Equal(AppRoles.Employee, created.Role);
        Assert.True(created.IsActive);
        Assert.False(created.MustChangePassword);
        Assert.DoesNotContain("passwordHash", createJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordSalt", createJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporaryPassword", createJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temporaryPassword, createJson, StringComparison.Ordinal);

        var duplicate = await adminClient.PostAsJsonAsync(
            "/api/admin/users",
            new CreateAppUserRequestDto
            {
                Username = username.ToUpperInvariant(),
                DisplayName = "Duplicate Employee",
                Role = AppRoles.Employee,
                TemporaryPassword = temporaryPassword,
                IsActive = true,
                MustChangePassword = false,
            });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var employeeSession = CreateClient(factory);
        await LoginAsync(employeeSession, username, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, (await employeeSession.GetAsync("/api/auth/me")).StatusCode);

        var locked = await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{created.Id}",
            new UpdateAppUserRequestDto
            {
                DisplayName = created.DisplayName,
                Role = AppRoles.Employee,
                IsActive = false,
                MustChangePassword = false,
            });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await employeeSession.GetAsync("/api/auth/me")).StatusCode);

        using var lockedLoginClient = CreateClient(factory);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await lockedLoginClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = username,
                Password = temporaryPassword,
            })).StatusCode);

        var unlocked = await adminClient.PutAsJsonAsync(
            $"/api/admin/users/{created.Id}",
            new UpdateAppUserRequestDto
            {
                DisplayName = created.DisplayName,
                Role = AppRoles.Employee,
                IsActive = true,
                MustChangePassword = false,
            });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);

        using var activeEmployeeSession = CreateClient(factory);
        await LoginAsync(activeEmployeeSession, username, temporaryPassword);
        Assert.Equal(
            HttpStatusCode.OK,
            (await activeEmployeeSession.GetAsync("/api/auth/me")).StatusCode);

        var reset = await adminClient.PostAsJsonAsync(
            $"/api/admin/users/{created.Id}/reset-password",
            new ResetAppUserPasswordRequestDto
            {
                TemporaryPassword = replacementPassword,
                MustChangePassword = false,
            });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await activeEmployeeSession.GetAsync("/api/auth/me")).StatusCode);

        using var oldPasswordClient = CreateClient(factory);
        using var newPasswordClient = CreateClient(factory);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await oldPasswordClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = username,
                Password = temporaryPassword,
            })).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await newPasswordClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = username,
                Password = replacementPassword,
            })).StatusCode);

        var listJson = await (await adminClient.GetAsync("/api/admin/users"))
            .Content
            .ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordSalt", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporaryPassword", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", listJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_own_account()
    {
        using var factory = new AuthApiFactory();
        using var client = CreateClient(factory);
        await LoginAsync(client, "admin", AuthApiFactory.AdminPassword);

        var response = await client.PutAsJsonAsync(
            "/api/admin/users/1",
            new UpdateAppUserRequestDto
            {
                DisplayName = "admin test user",
                Role = AppRoles.Admin,
                IsActive = false,
                MustChangePassword = false,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_Admin_demotions_leave_at_least_one_active_Admin()
    {
        using var factory = new AuthApiFactory();
        factory.SetUserState("lockout", roles: [AppRoles.Viewer]);
        factory.SetUserState("disabled-session", roles: [AppRoles.Viewer]);
        factory.SetUserState("deleted-session", roles: [AppRoles.Viewer]);
        using var start = new ManualResetEventSlim(false);

        static async Task<AppUserManagementResult> DemoteAsync(
            AuthApiFactory testFactory,
            ManualResetEventSlim startSignal,
            long targetUserId,
            long actorUserId,
            string actorUsername)
        {
            startSignal.Wait();
            using var scope = testFactory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAppUserManagementService>();
            return await service.UpdateAsync(
                targetUserId,
                new UpdateAppUserRequestDto
                {
                    DisplayName = targetUserId == 1
                        ? "admin test user"
                        : "role-admin test user",
                    Role = AppRoles.Employee,
                    IsActive = true,
                    MustChangePassword = false,
                },
                actorUserId,
                actorUsername);
        }

        var first = Task.Run(() => DemoteAsync(factory, start, 5, 1, "admin"));
        var second = Task.Run(() => DemoteAsync(factory, start, 1, 5, "role-admin"));
        start.Set();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Status == AppUserManagementStatus.Success);
        Assert.Single(
            results,
            result => result.Status == AppUserManagementStatus.LastActiveAdminDenied);
        Assert.Equal(1, factory.CountActiveAdmins());
    }

    [Fact]
    public async Task Malformed_Admin_credential_does_not_satisfy_last_Admin_guard()
    {
        using var factory = new AuthApiFactory();
        factory.SetUserState("lockout", roles: [AppRoles.Viewer]);
        factory.SetUserState("disabled-session", roles: [AppRoles.Viewer]);
        factory.SetUserState("deleted-session", roles: [AppRoles.Viewer]);
        factory.SetPasswordHash("role-admin", "malformed-not-an-identity-hash");
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAppUserManagementService>();

        var result = await service.UpdateAsync(
            1,
            new UpdateAppUserRequestDto
            {
                DisplayName = "admin test user",
                Role = AppRoles.Employee,
                IsActive = true,
                MustChangePassword = false,
            },
            1,
            "admin");

        Assert.Equal(AppUserManagementStatus.LastActiveAdminDenied, result.Status);
        Assert.Equal(1, factory.CountActiveAdmins());
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
    public async Task Operations_status_applies_the_current_Admin_or_Viewer_authorization()
    {
        using var adminClient = CreateClient();
        using var viewerClient = CreateClient();
        await LoginAsync(adminClient, "admin", AuthApiFactory.AdminPassword);
        await LoginAsync(viewerClient, "viewer", AuthApiFactory.ViewerPassword);

        var admin = await adminClient.GetFromJsonAsync<QlhvOperationsStatusDto>(
            "/api/dong-bo-v2/qlhv/operations/status?sourceType=OTO");
        var viewer = await viewerClient.GetFromJsonAsync<QlhvOperationsStatusDto>(
            "/api/dong-bo-v2/qlhv/operations/status?sourceType=OTO");

        Assert.NotNull(admin);
        Assert.Equal(AppRoles.Admin, admin.CurrentUserRole);
        Assert.True(admin.WriteAuthorized);
        Assert.DoesNotContain("Bạn không có quyền Admin.", admin.RefreshBlockers);
        Assert.True(admin.DryRun);
        Assert.False(admin.TargetWritesEnabled);
        Assert.False(admin.CanRefresh);
        Assert.False(admin.CanSync);
        Assert.Contains("Chế độ DryRun đang bật.", admin.RefreshBlockers);
        Assert.Contains("Quyền ghi dữ liệu đang tắt.", admin.SyncBlockers);

        Assert.NotNull(viewer);
        Assert.Equal(AppRoles.Viewer, viewer.CurrentUserRole);
        Assert.False(viewer.WriteAuthorized);
        Assert.False(viewer.CanRefresh);
        Assert.False(viewer.CanSync);
        Assert.Contains("Bạn không có quyền Admin.", viewer.RefreshBlockers);
        Assert.Contains("Bạn không có quyền Admin.", viewer.SyncBlockers);
    }

    [Fact]
    public async Task Admin_with_enabled_runtime_write_flags_can_refresh_when_idle()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Sync:DryRun"] = "false",
                    ["SyncExecution:EnableTargetWrites"] = "true",
                })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        await LoginAsync(client, "admin", AuthApiFactory.AdminPassword);

        var status = await client.GetFromJsonAsync<QlhvOperationsStatusDto>(
            "/api/dong-bo-v2/qlhv/operations/status?sourceType=OTO");

        Assert.NotNull(status);
        Assert.Equal(AppRoles.Admin, status.CurrentUserRole);
        Assert.True(status.WriteAuthorized);
        Assert.False(status.DryRun);
        Assert.True(status.TargetWritesEnabled);
        Assert.True(status.CanRefresh);
        Assert.True(status.CanSync);
        Assert.Empty(status.RefreshBlockers);
        Assert.Empty(status.SyncBlockers);
    }

    [Fact]
    public async Task Failed_auto_sync_status_does_not_block_login_or_application_shell()
    {
        var webRoot = Path.Combine(
            Path.GetTempPath(),
            $"qlhv-auth-auto-sync-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        File.WriteAllText(
            Path.Combine(webRoot, "index.html"),
            "<!doctype html><html><body><div id=\"auto-sync-failure-shell\"></div></body></html>");
        try
        {
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(webRoot);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IQlhvAutoSyncService>();
                    services.AddSingleton<IQlhvAutoSyncService, FailedAutoSyncService>();
                });
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });

            await LoginAsync(client, "admin", AuthApiFactory.AdminPassword);
            var me = await client.GetAsync("/api/auth/me");
            var autoSync = await client.GetFromJsonAsync<QlhvAutoSyncStatusDto>(
                "/api/dong-bo-v2/qlhv/operations/auto-sync/status");
            var application = await client.GetAsync("/qlhv-import");

            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.NotNull(autoSync);
            Assert.Equal("failed", autoSync.State);
            Assert.Equal("Auto Sync test failure.", autoSync.LastError);
            Assert.Equal(HttpStatusCode.OK, application.StatusCode);
            Assert.Contains(
                "auto-sync-failure-shell",
                await application.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(webRoot, recursive: true);
            }
            catch (IOException)
            {
                // TestServer can release static-file handles just after disposal.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of the unique OS temp fixture.
            }
        }
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
        var problem = await login.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Tên đăng nhập hoặc mật khẩu không đúng.", problem.Detail);
        Assert.True(problem.Extensions.ContainsKey("correlationId"));
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

        Assert.Equal((HttpStatusCode)423, correctWhileLocked.StatusCode);
        var lockedProblem = await correctWhileLocked.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(lockedProblem);
        Assert.Contains("tạm khóa", lockedProblem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(lockedProblem.Extensions.ContainsKey("correlationId"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static HttpClient CreateClient(AuthApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
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

    private sealed class FailedAutoSyncService : IQlhvAutoSyncService
    {
        public Task<QlhvAutoSyncQueueResultDto> QueueAsync(
            string triggerType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new QlhvAutoSyncQueueResultDto
            {
                Status = QlhvAutoSyncConstants.Failed,
                Message = "Auto Sync test failure.",
            });

        public Task<QlhvAutoSyncQueueResultDto> QueueSessionStartAsync(
            bool serverStartedByLauncher,
            CancellationToken cancellationToken = default) =>
            QueueAsync(QlhvAutoSyncConstants.SessionStartTrigger, cancellationToken);

        public Task<QlhvAutoSyncQueueResultDto> QueueEnsureFreshAsync(
            CancellationToken cancellationToken = default) =>
            QueueAsync(QlhvAutoSyncConstants.AppOpenTrigger, cancellationToken);

        public Task<QlhvSessionStartStatusDto> GetSessionStartStatusAsync(
            bool serverStartedByLauncher,
            Guid? runId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new QlhvSessionStartStatusDto
            {
                Found = true,
                ServerReady = true,
                State = "failed",
                IsTerminal = true,
                LastError = "Auto Sync test failure.",
                Message = "Auto Sync test failure.",
            });

        public Task<QlhvAutoSyncStatusDto> GetStatusAsync(
            Guid? runId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new QlhvAutoSyncStatusDto
            {
                Found = true,
                Enabled = true,
                RunOnServerStartup = true,
                RefreshBackupBeforeSync = true,
                State = "failed",
                LastError = "Auto Sync test failure.",
            });
    }

    private sealed class RecordingAutoSyncService : IQlhvAutoSyncService
    {
        public int EnsureFreshCalls { get; private set; }

        public int ManualQueueCalls { get; private set; }

        public Task<QlhvAutoSyncQueueResultDto> QueueEnsureFreshAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureFreshCalls++;
            return Task.FromResult(new QlhvAutoSyncQueueResultDto
            {
                Accepted = true,
                Status = "UP_TO_DATE",
                Message = "up to date",
            });
        }

        public Task<QlhvAutoSyncQueueResultDto> QueueAsync(
            string triggerType,
            CancellationToken cancellationToken = default)
        {
            ManualQueueCalls++;
            throw new NotSupportedException();
        }

        public Task<QlhvAutoSyncQueueResultDto> QueueSessionStartAsync(
            bool serverStartedByLauncher,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvSessionStartStatusDto> GetSessionStartStatusAsync(
            bool serverStartedByLauncher,
            Guid? runId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvAutoSyncStatusDto> GetStatusAsync(
            Guid? runId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "admin-password-for-tests";
    public const string EmployeePassword = "employee-password-for-tests";
    public const string MustChangePassword = "must-change-password-for-tests";
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

    public int CountActiveAdmins() => _users.CountActiveAdmins();

    public void SetPasswordHash(string username, string passwordHash) =>
        _users.SetPasswordHash(username, passwordHash);

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
            services.RemoveAll<IAppUserManagementRepository>();
            services.AddSingleton<IAppUserManagementRepository>(_users);
            services.RemoveAll<IHocVienService>();
            services.AddSingleton<IHocVienService, FakeHocVienService>();
            services.RemoveAll<IRuntimeReadinessService>();
            services.AddSingleton<IRuntimeReadinessService>(new ReadyRuntimeReadinessService());
            services.RemoveAll<IQlhvOperationsRepository>();
            services.AddSingleton<IQlhvOperationsRepository>(new ReadyOperationsRepository());
            services.RemoveAll<IQlhvOperationHistoryRepository>();
            services.AddSingleton<IQlhvOperationHistoryRepository>(new EmptyOperationHistoryRepository());
            services.RemoveAll<IHostedService>();
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
        });
    }

    private sealed class ReadyRuntimeReadinessService : IRuntimeReadinessService
    {
        public Task<RuntimeStatusDto> GetStatusAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(new RuntimeStatusDto
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
            Messages = ["ready"],
        });
    }

    private sealed class ReadyOperationsRepository : IQlhvOperationsRepository
    {
        public Task<QlhvOperationDataSnapshot> ReadStatusSnapshotAsync(
            QlhvOperationSourceDefinition source,
            CancellationToken cancellationToken = default) => Task.FromResult(new QlhvOperationDataSnapshot(
                new QlhvOperationRowCountsDto { NguoiLX = 10 },
                new QlhvOperationRowCountsDto { NguoiLX = 10 },
                10,
                "test-snapshot-token"));
    }

    private sealed class EmptyOperationHistoryRepository : IQlhvOperationHistoryRepository
    {
        public Task<bool> TryCreateAsync(
            QlhvOperationHistoryCreate entry,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task MarkRunningAsync(Guid operationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompleteAsync(
            QlhvOperationHistoryCompletion completion,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<QlhvOperationHistoryDto>> SearchAsync(
            string sourceType,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QlhvOperationHistoryDto>>(Array.Empty<QlhvOperationHistoryDto>());

        public Task<QlhvOperationHistoryDto?> GetActiveAsync(
            string sourceType,
            CancellationToken cancellationToken = default) => Task.FromResult<QlhvOperationHistoryDto?>(null);

        public Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
            string sourceType,
            string operationType,
            CancellationToken cancellationToken = default) => Task.FromResult<QlhvOperationHistoryDto?>(null);
    }

    private sealed class FakeHocVienService : IHocVienService
    {
        public Task<PagedResult<HocVienListItemDto>> SearchAsync(
            HocVienSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PagedResult<HocVienListItemDto>.Empty(1, 20));

        public Task<HocVienPhotoPreviewDto?> GetPhotoPreviewAsync(
            int hocVienId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<HocVienPhotoPreviewDto?>(null);

        public Task<HocVienExportFileDto> PrintCardsAsync(
            HocVienCardPrintRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HocVienExportFileDto
            {
                FileName = "cards.pdf",
                ContentType = "application/pdf",
                Content = [1],
            });

        public Task<HocVienCardPrintPreviewDto> PreviewPrintCardsAsync(
            HocVienCardPrintRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HocVienCardPrintPreviewDto
            {
                TotalStudents = 1,
                TotalPages = 1,
                CardsPerPage = 12,
            });

        public Task<HocVienPhotoAuditResultDto> AuditPhotosAsync(
            HocVienPhotoAuditRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HocVienPhotoAuditResultDto
            {
                Page = 1,
                PageSize = 20,
            });

        public Task<HocVienExportFileDto> ExportExcelAsync(
            HocVienSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HocVienExportFileDto
            {
                FileName = "students.xlsx",
                Content = [1],
            });

        public Task<IReadOnlyList<HocVienKhoaLookupDto>> SearchKhoaLookupsAsync(
            string? keyword,
            string? maHangDT,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HocVienKhoaLookupDto>>([]);

        public Task<IReadOnlyList<HocVienHangHocLookupDto>> SearchHangHocLookupsAsync(
            string? keyword,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HocVienHangHocLookupDto>>([]);
    }

    private sealed class InMemoryAppUserRepository :
        IAppUserRepository,
        IAppUserManagementRepository
    {
        private readonly Dictionary<string, AppUserCredential> _users;
        private readonly Dictionary<long, DateTime> _createdAtUtc = [];
        private readonly Dictionary<long, string?> _createdBy = [];
        private readonly Dictionary<long, DateTime?> _lastLoginAtUtc = [];
        private readonly object _gate = new();
        private long _nextId;

        public InMemoryAppUserRepository()
        {
            var hasher = new PasswordHasher<AppUserCredential>();
            _users = new[]
                {
                    CreateUser(hasher, 1, "admin", AdminPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 2, "employee", EmployeePassword, true, AppRoles.Employee),
                    CreateUser(hasher, 3, "viewer", ViewerPassword, true, AppRoles.Viewer),
                    CreateUser(hasher, 4, "inactive", InactivePassword, false, AppRoles.Admin),
                    CreateUser(hasher, 5, "role-admin", SessionPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 6, "disabled-session", SessionPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 7, "deleted-session", SessionPassword, true, AppRoles.Admin),
                    CreateUser(hasher, 8, "lockout", SessionPassword, true, AppRoles.Admin),
                    CreateUser(
                        hasher,
                        9,
                        "must-change",
                        MustChangePassword,
                        true,
                        AppRoles.Employee,
                        mustChangePassword: true),
                }
                .ToDictionary(user => user.Username, StringComparer.OrdinalIgnoreCase);
            _nextId = _users.Values.Max(user => user.Id);
            foreach (var user in _users.Values)
            {
                _createdAtUtc[user.Id] = DateTime.UnixEpoch.AddMinutes(user.Id);
                _createdBy[user.Id] = "test-fixture";
                _lastLoginAtUtc[user.Id] = null;
            }
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

        public Task<bool> TryRecordSuccessfulLoginAsync(
            long userId,
            string expectedPasswordHash,
            Guid expectedSecurityStamp,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = FindRequired(userId);
                if (!user.IsActive ||
                    user.IsDeleted ||
                    !string.Equals(
                        user.PasswordHash,
                        expectedPasswordHash,
                        StringComparison.Ordinal) ||
                    user.SecurityStamp != expectedSecurityStamp)
                {
                    return Task.FromResult(false);
                }

                _users[user.Username] = Copy(
                    user,
                    failedLoginCount: 0,
                    lastFailedLoginAtUtc: null,
                    updatedAtUtc: DateTime.UtcNow);
                _lastLoginAtUtc[userId] = DateTime.UtcNow;
            }

            return Task.FromResult(true);
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
                var failedLoginCount =
                    user.LastFailedLoginAtUtc is null ||
                    user.LastFailedLoginAtUtc < resetCutoffUtc
                    ? 1
                    : user.FailedLoginCount == int.MaxValue
                        ? int.MaxValue
                        : user.FailedLoginCount + 1;
                _users[user.Username] = Copy(
                    user,
                    failedLoginCount: failedLoginCount,
                    lastFailedLoginAtUtc: failedAtUtc,
                    updatedAtUtc: failedAtUtc);
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryUpdatePasswordHashAsync(
            long userId,
            string expectedPasswordHash,
            string passwordHash,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = _users.Values.FirstOrDefault(candidate => candidate.Id == userId);
                if (user is null ||
                    !string.Equals(
                        user.PasswordHash,
                        expectedPasswordHash,
                        StringComparison.Ordinal))
                {
                    return Task.FromResult(false);
                }

                _users[user.Username] = Copy(
                    user,
                    passwordHash: passwordHash,
                    updatedAtUtc: DateTime.UtcNow);
                return Task.FromResult(true);
            }
        }

        public Task<IReadOnlyList<AppUserListItemDto>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                IReadOnlyList<AppUserListItemDto> result = _users.Values
                    .Where(user => !user.IsDeleted)
                    .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
                    .Select(ToListItem)
                    .ToArray();
                return Task.FromResult(result);
            }
        }

        public Task<AppUserManagementResult> CreateAsync(
            AppUserCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_users.ContainsKey(command.Username) ||
                    _users.Values.Any(user =>
                        string.Equals(
                            AppUserManagementService.NormalizeUsername(user.Username),
                            command.NormalizedUsername,
                            StringComparison.Ordinal)))
                {
                    return Task.FromResult(AppUserManagementResult.Failure(
                        AppUserManagementStatus.UsernameExists,
                        "Username already exists."));
                }

                var id = ++_nextId;
                var user = new AppUserCredential
                {
                    Id = id,
                    Username = command.Username,
                    DisplayName = command.DisplayName,
                    PasswordHash = command.PasswordHash,
                    SecurityStamp = Guid.NewGuid(),
                    IsActive = command.IsActive,
                    IsDeleted = false,
                    FailedLoginCount = 0,
                    Roles = [command.Role],
                    MustChangePassword = command.MustChangePassword,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                _users.Add(user.Username, user);
                _createdAtUtc[id] = DateTime.UtcNow;
                _createdBy[id] = command.ActorUsername;
                _lastLoginAtUtc[id] = null;
                return Task.FromResult(AppUserManagementResult.Success(ToListItem(user)));
            }
        }

        public Task<AppUserManagementResult> UpdateAsync(
            AppUserUpdateCommand command,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = _users.Values.FirstOrDefault(
                    candidate => candidate.Id == command.UserId && !candidate.IsDeleted);
                if (user is null)
                {
                    return Task.FromResult(AppUserManagementResult.Failure(
                        AppUserManagementStatus.NotFound,
                        "User not found."));
                }

                if (command.ActorUserId == command.UserId && !command.IsActive)
                {
                    return Task.FromResult(AppUserManagementResult.Failure(
                        AppUserManagementStatus.SelfDeactivationDenied,
                        "Cannot deactivate the current account."));
                }

                var removesActiveAdmin =
                    user.IsActive &&
                    user.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal) &&
                    (!command.IsActive ||
                     !string.Equals(command.Role, AppRoles.Admin, StringComparison.Ordinal));
                if (removesActiveAdmin &&
                    _users.Values.Count(candidate =>
                        candidate.Id != command.UserId &&
                        candidate.IsActive &&
                        !candidate.IsDeleted &&
                        AppPasswordHashFormat.IsSupported(candidate.PasswordHash) &&
                        candidate.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal)) == 0)
                {
                    return Task.FromResult(AppUserManagementResult.Failure(
                        AppUserManagementStatus.LastActiveAdminDenied,
                        "Cannot remove the last active Admin."));
                }

                var updated = Copy(
                    user,
                    displayName: command.DisplayName,
                    securityStamp:
                        user.IsActive != command.IsActive ||
                        user.MustChangePassword != command.MustChangePassword ||
                        !user.Roles.Contains(command.Role, StringComparer.Ordinal)
                            ? Guid.NewGuid()
                            : user.SecurityStamp,
                    isActive: command.IsActive,
                    roles: [command.Role],
                    mustChangePassword: command.MustChangePassword,
                    failedLoginCount: command.IsActive ? 0 : user.FailedLoginCount,
                    lastFailedLoginAtUtc: command.IsActive
                        ? null
                        : user.LastFailedLoginAtUtc,
                    updatedAtUtc: DateTime.UtcNow);
                _users[user.Username] = updated;
                return Task.FromResult(AppUserManagementResult.Success(ToListItem(updated)));
            }
        }

        public Task<AppUserManagementResult> ResetPasswordAsync(
            AppUserPasswordResetCommand command,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = _users.Values.FirstOrDefault(
                    candidate => candidate.Id == command.UserId && !candidate.IsDeleted);
                if (user is null)
                {
                    return Task.FromResult(AppUserManagementResult.Failure(
                        AppUserManagementStatus.NotFound,
                        "User not found."));
                }

                _users[user.Username] = Copy(
                    user,
                    passwordHash: command.PasswordHash,
                    securityStamp: Guid.NewGuid(),
                    mustChangePassword: command.MustChangePassword,
                    failedLoginCount: 0,
                    lastFailedLoginAtUtc: null,
                    updatedAtUtc: DateTime.UtcNow);
                return Task.FromResult(AppUserManagementResult.Success());
            }
        }

        public Task<AppUserManagementResult> ChangeOwnPasswordAsync(
            AppUserOwnPasswordChangeCommand command,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var user = _users.Values.FirstOrDefault(
                    candidate =>
                        candidate.Id == command.UserId &&
                        candidate.IsActive &&
                        !candidate.IsDeleted);
                if (user is null ||
                    !string.Equals(
                        user.PasswordHash,
                        command.ExpectedPasswordHash,
                        StringComparison.Ordinal))
                {
                    return Task.FromResult(AppUserManagementResult.Failure(
                        AppUserManagementStatus.Conflict,
                        "User changed."));
                }

                var securityStamp = Guid.NewGuid();
                _users[user.Username] = Copy(
                    user,
                    passwordHash: command.PasswordHash,
                    securityStamp: securityStamp,
                    mustChangePassword: false,
                    failedLoginCount: 0,
                    lastFailedLoginAtUtc: null,
                    updatedAtUtc: DateTime.UtcNow);
                return Task.FromResult(
                    AppUserManagementResult.Success(securityStamp: securityStamp));
            }
        }

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

        public int CountActiveAdmins()
        {
            lock (_gate)
            {
                return _users.Values.Count(user =>
                    user.IsActive &&
                    !user.IsDeleted &&
                    AppPasswordHashFormat.IsSupported(user.PasswordHash) &&
                    user.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal));
            }
        }

        public void SetPasswordHash(string username, string passwordHash)
        {
            lock (_gate)
            {
                var user = _users[username];
                _users[username] = Copy(user, passwordHash: passwordHash);
            }
        }

        private AppUserCredential FindRequired(long userId) =>
            _users.Values.Single(user => user.Id == userId);

        private AppUserListItemDto ToListItem(AppUserCredential user) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = AppRoles.SelectPrimary(user.Roles) ?? string.Empty,
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            LastLoginAtUtc = _lastLoginAtUtc.GetValueOrDefault(user.Id),
            CreatedAtUtc = _createdAtUtc.GetValueOrDefault(user.Id, DateTime.UnixEpoch),
            CreatedBy = _createdBy.GetValueOrDefault(user.Id),
        };

        private static AppUserCredential Copy(
            AppUserCredential user,
            string? displayName = null,
            string? passwordHash = null,
            Guid? securityStamp = null,
            bool? isActive = null,
            bool? isDeleted = null,
            IReadOnlyList<string>? roles = null,
            bool? mustChangePassword = null,
            int? failedLoginCount = null,
            DateTime? lastFailedLoginAtUtc = null,
            DateTime? updatedAtUtc = null) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = displayName ?? user.DisplayName,
            PasswordHash = passwordHash ?? user.PasswordHash,
            SecurityStamp = securityStamp ?? user.SecurityStamp,
            IsActive = isActive ?? user.IsActive,
            IsDeleted = isDeleted ?? user.IsDeleted,
            Roles = roles ?? user.Roles,
            MustChangePassword = mustChangePassword ?? user.MustChangePassword,
            FailedLoginCount = failedLoginCount ?? user.FailedLoginCount,
            LastFailedLoginAtUtc = failedLoginCount == 0
                ? null
                : lastFailedLoginAtUtc ?? user.LastFailedLoginAtUtc,
            UpdatedAtUtc = updatedAtUtc ?? user.UpdatedAtUtc,
        };

        private static AppUserCredential CreateUser(
            IPasswordHasher<AppUserCredential> hasher,
            long id,
            string username,
            string password,
            bool isActive,
            string role,
            bool mustChangePassword = false)
        {
            var user = new AppUserCredential
            {
                Id = id,
                Username = username,
                DisplayName = $"{username} test user",
                SecurityStamp = Guid.NewGuid(),
                IsActive = isActive,
                Roles = [role],
                MustChangePassword = mustChangePassword,
            };
            return new AppUserCredential
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                PasswordHash = hasher.HashPassword(user, password),
                SecurityStamp = user.SecurityStamp,
                IsActive = user.IsActive,
                Roles = user.Roles,
                MustChangePassword = user.MustChangePassword,
            };
        }
    }
}
