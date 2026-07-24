using System.Runtime.CompilerServices;

namespace QLHV.Tests.Auth;

public sealed class ClientAuthorizationSourceTests
{
    [Fact]
    public void Client_uses_cookie_credentials_without_persisting_password_or_token()
    {
        var apiFetch = ReadClientFile("api", "apiFetch.ts");
        var authApi = ReadClientFile("features", "auth", "api.ts");
        var authContext = ReadClientFile("features", "auth", "AuthContext.tsx");
        var loginPage = ReadClientFile("features", "auth", "LoginPage.tsx");
        var combined = string.Join('\n', authApi, authContext, loginPage);

        Assert.Contains("credentials: 'include'", apiFetch, StringComparison.Ordinal);
        Assert.Contains("response.status === 401", apiFetch, StringComparison.Ordinal);
        Assert.Contains("AUTH_SESSION_EXPIRED_EVENT", apiFetch, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(AUTH_SESSION_EXPIRED_EVENT", authContext, StringComparison.Ordinal);
        Assert.Contains("setUser(null)", authContext, StringComparison.Ordinal);
        Assert.Contains("/auth/login", authApi, StringComparison.Ordinal);
        Assert.Contains("/auth/logout", authApi, StringComparison.Ordinal);
        Assert.Contains("/auth/me", authApi, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", loginPage, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("console.log", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Header_displays_authenticated_identity_role_and_logout()
    {
        var header = ReadClientFile("layout", "Header.tsx");

        Assert.Contains("user?.displayName", header, StringComparison.Ordinal);
        Assert.Contains("user?.username", header, StringComparison.Ordinal);
        Assert.Contains("getRoleDisplayName(user?.role)", header, StringComparison.Ordinal);
        Assert.Contains("<ChangePasswordDialog", header, StringComparison.Ordinal);
        Assert.Contains("handleLogout", header, StringComparison.Ordinal);
        Assert.Contains("logout()", header, StringComparison.Ordinal);
        Assert.DoesNotContain(">Quản trị viên<", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Employee_and_viewer_have_explicit_business_permissions_while_admin_routes_stay_locked()
    {
        var app = ReadClientFile("App.tsx");
        var menu = ReadClientFile("navigation", "menu.ts");
        var types = ReadClientFile("features", "auth", "types.ts");
        var authApi = ReadClientFile("features", "auth", "api.ts");
        var permissions = ReadClientFile("features", "auth", "permissions.ts");
        var hocVien = ReadClientFile("features", "hoc-vien", "HocVienPage.tsx");
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");
        var autoSyncPanel = ReadClientFile("features", "qlhv-import", "AutoSyncPanel.tsx");

        Assert.Contains("export type AppUserRole = 'Admin' | 'Employee' | 'Viewer'", types, StringComparison.Ordinal);
        Assert.Contains("value.role !== 'Employee'", authApi, StringComparison.Ordinal);
        Assert.Contains("role === 'Employee'", permissions, StringComparison.Ordinal);
        Assert.Contains("permission === 'CanViewBusinessData' || permission === 'CanEditBusinessData'", permissions, StringComparison.Ordinal);
        Assert.Contains("<Route index element={<Dashboard />} />", app, StringComparison.Ordinal);
        Assert.Contains("path=\"/hoc-vien\" element={<HocVienPage />}", app, StringComparison.Ordinal);
        Assert.Contains("canOperateBusiness ? <HocVienCardPrintPage />", app, StringComparison.Ordinal);
        Assert.Contains("canSynchronize ? <MotoSyncPage />", app, StringComparison.Ordinal);
        Assert.Contains("path=\"/admin/users\"", app, StringComparison.Ordinal);
        Assert.Contains("canManageAccounts", app, StringComparison.Ordinal);
        Assert.Contains("path: '/admin/users'", menu, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: 'CanManageUsers'", menu, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: 'CanSynchronizeCSDT'", menu, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: 'CanEditBusinessData'", menu, StringComparison.Ordinal);
        Assert.Contains("canExportAndPrint", hocVien, StringComparison.Ordinal);
        Assert.Contains("const isAdmin = user?.role === 'Admin'", page, StringComparison.Ordinal);
        Assert.Contains(
            "Chỉ tài khoản Quản trị viên được phép thực hiện đồng bộ.",
            page,
            StringComparison.Ordinal);
        Assert.Contains("refreshReason={!isAdmin", page, StringComparison.Ordinal);
        Assert.Contains("executeReason={!isAdmin", page, StringComparison.Ordinal);
        Assert.Contains("if (!isAdmin)", page, StringComparison.Ordinal);
        Assert.Contains("if (!isAdmin) return 'Bạn không có quyền thực hiện: cần vai trò Admin.'", autoSyncPanel, StringComparison.Ordinal);
        Assert.Contains("disabled={disabledReason !== null}", autoSyncPanel, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_user_management_and_password_change_do_not_expose_secrets()
    {
        var app = ReadClientFile("App.tsx");
        var api = ReadClientFile("features", "admin-users", "api.ts");
        var page = ReadClientFile("features", "admin-users", "UserManagementPage.tsx");
        var changePassword = ReadClientFile("features", "auth", "ChangePasswordDialog.tsx");
        var authApi = ReadClientFile("features", "auth", "api.ts");
        var combined = string.Join('\n', api, page, changePassword, authApi);

        Assert.Contains("user.mustChangePassword", app, StringComparison.Ordinal);
        Assert.Contains("<ChangePasswordDialog required", app, StringComparison.Ordinal);
        Assert.Contains("/admin/users", api, StringComparison.Ordinal);
        Assert.Contains("/reset-password", api, StringComparison.Ordinal);
        Assert.Contains("'PUT', request", api, StringComparison.Ordinal);
        Assert.Contains("method: 'POST'", api, StringComparison.Ordinal);
        Assert.Contains("Employee", page, StringComparison.Ordinal);
        Assert.Contains("Viewer", page, StringComparison.Ordinal);
        Assert.Contains("Không thể tự khóa tài khoản đang đăng nhập", page, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", page, StringComparison.Ordinal);
        Assert.Contains("autoComplete=\"new-password\"", page, StringComparison.Ordinal);
        Assert.Contains("/auth/change-password", authApi, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", changePassword, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("console.log", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deleteManagedUser", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authenticated_admin_or_employee_ensures_fresh_without_blocking_or_accepting_force_fields()
    {
        var authContext = ReadClientFile("features", "auth", "AuthContext.tsx");
        var qlhvApi = ReadClientFile("features", "qlhv-import", "api.ts");
        var panel = ReadClientFile("features", "qlhv-import", "AutoSyncPanel.tsx");
        var ensureFresh = ExtractFunction(qlhvApi, "export async function ensureQlhvFresh");

        Assert.Contains("user.role === 'Viewer'", authContext, StringComparison.Ordinal);
        Assert.Contains("ensureQlhvFresh()", authContext, StringComparison.Ordinal);
        Assert.Contains(".catch(() => undefined)", authContext, StringComparison.Ordinal);
        Assert.Contains("ensuredFreshUserIdRef.current === user.id", authContext, StringComparison.Ordinal);
        Assert.Contains("/operations/ensure-fresh", ensureFresh, StringComparison.Ordinal);
        Assert.Contains("method: 'POST'", ensureFresh, StringComparison.Ordinal);
        Assert.DoesNotContain("body:", ensureFresh, StringComparison.Ordinal);
        Assert.DoesNotContain("force", ensureFresh, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceType", ensureFresh, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_APP_OPEN", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_direct_actions_keep_snapshot_and_busy_guards_without_key_or_confirmation()
    {
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");
        var logic = ReadClientFile("features", "qlhv-import", "logic.ts");
        var api = ReadClientFile("features", "qlhv-import", "api.ts");
        var combined = string.Join('\n', page, logic, api);

        Assert.Contains("refreshQlhvBackup(body)", page, StringComparison.Ordinal);
        Assert.Contains("executeQlhvImport(body)", page, StringComparison.Ordinal);
        Assert.Contains("expectedSnapshotToken: plan.data.backupSnapshotToken", logic, StringComparison.Ordinal);
        Assert.Contains("plan.data.blockers.length === 0", logic, StringComparison.Ordinal);
        Assert.Contains("plan.data.hocVienBlockers.length === 0", logic, StringComparison.Ordinal);
        Assert.Contains("plan.data.executableDomains.includes('HOC_VIEN')", logic, StringComparison.Ordinal);
        Assert.Contains("plan.data.sourceHocVienRows > 0", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("plan.data.relationConflicts === 0", logic, StringComparison.Ordinal);
        Assert.Contains("isOperationBusy(status)", logic, StringComparison.Ordinal);
        Assert.Contains("status.canSync", logic, StringComparison.Ordinal);
        Assert.Contains("aria-busy={state.refreshing}", page, StringComparison.Ordinal);
        Assert.Contains("aria-busy={state.executing}", page, StringComparison.Ordinal);
        Assert.Contains("'Đang làm mới BAK...'", page, StringComparison.Ordinal);
        Assert.Contains("'Đang đồng bộ...'", page, StringComparison.Ordinal);
        Assert.DoesNotContain("X-QLHV-Operations-Key", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operationsKey", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmText", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Login_classifies_runtime_auth_failures_and_waits_for_readiness()
    {
        var authApi = ReadClientFile("features", "auth", "api.ts");
        var loginPage = ReadClientFile("features", "auth", "LoginPage.tsx");

        Assert.Contains("response.status === 401", authApi, StringComparison.Ordinal);
        Assert.Contains("response.status === 423", authApi, StringComparison.Ordinal);
        Assert.Contains("response.status === 503", authApi, StringComparison.Ordinal);
        Assert.Contains("'invalid-credentials'", authApi, StringComparison.Ordinal);
        Assert.Contains("'locked'", authApi, StringComparison.Ordinal);
        Assert.Contains("'runtime-unavailable'", authApi, StringComparison.Ordinal);
        Assert.Contains("correlationId", authApi, StringComparison.Ordinal);
        Assert.Contains("getRuntimeStatus", loginPage, StringComparison.Ordinal);
        Assert.Contains("readiness !== 'ready'", loginPage, StringComparison.Ordinal);
        Assert.Contains("checkReadiness", loginPage, StringComparison.Ordinal);
        Assert.Contains("onRetry", loginPage, StringComparison.Ordinal);
        Assert.Contains("runtimeStatus?.version", loginPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_status_page_and_menu_are_admin_only()
    {
        var app = ReadClientFile("App.tsx");
        var menu = ReadClientFile("navigation", "menu.ts");
        var page = ReadClientFile("features", "runtime-status", "RuntimeStatusPage.tsx");

        Assert.Contains("/trang-thai-he-thong", app, StringComparison.Ordinal);
        Assert.Contains("user.role === 'Admin'", app, StringComparison.Ordinal);
        Assert.Contains("<RuntimeStatusPage", app, StringComparison.Ordinal);
        Assert.Contains("path: '/trang-thai-he-thong'", menu, StringComparison.Ordinal);
        Assert.Contains("requiredRole: 'Admin'", menu, StringComparison.Ordinal);
        Assert.Contains("getRuntimeStatus", page, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionString", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", page, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadClientFile(params string[] pathParts)
        => File.ReadAllText(FindWorkspaceFile(
            new[] { "client", "src" }.Concat(pathParts).ToArray()));

    private static string ExtractFunction(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {marker}");
        var next = source.IndexOf("\nexport async function ", start + marker.Length, StringComparison.Ordinal);
        var end = next < 0 ? source.Length : next;
        return source[start..end];
    }

    private static string FindWorkspaceFile(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}
