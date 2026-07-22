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
        Assert.Contains("<small>{user?.role}</small>", header, StringComparison.Ordinal);
        Assert.Contains("handleLogout", header, StringComparison.Ordinal);
        Assert.Contains("logout()", header, StringComparison.Ordinal);
        Assert.DoesNotContain(">Quản trị viên<", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Viewer_is_limited_to_qlhv_read_page_and_write_buttons_are_locked()
    {
        var app = ReadClientFile("App.tsx");
        var menu = ReadClientFile("navigation", "menu.ts");
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");

        Assert.Contains("return item.path === '/qlhv-import'", menu, StringComparison.Ordinal);
        Assert.Contains("user.role === 'Admin' ? <Dashboard /> : viewerRedirect", app, StringComparison.Ordinal);
        Assert.Contains("Navigate to=\"/qlhv-import\"", app, StringComparison.Ordinal);
        Assert.Contains("const isAdmin = user?.role === 'Admin'", page, StringComparison.Ordinal);
        Assert.Contains("Bạn không có quyền thực hiện", page, StringComparison.Ordinal);
        Assert.Contains("refreshReason={!isAdmin", page, StringComparison.Ordinal);
        Assert.Contains("executeReason={!isAdmin", page, StringComparison.Ordinal);
        Assert.Contains("if (!isAdmin)", page, StringComparison.Ordinal);
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
        Assert.Contains("plan.data.sourceHocVienRows > 0", logic, StringComparison.Ordinal);
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

    private static string ReadClientFile(params string[] pathParts)
        => File.ReadAllText(FindWorkspaceFile(
            new[] { "client", "src" }.Concat(pathParts).ToArray()));

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
