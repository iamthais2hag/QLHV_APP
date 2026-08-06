using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeClientSourceTests
{
    [Fact]
    public void Realtime_screen_replaces_the_legacy_route_and_has_the_required_labels()
    {
        var app = ReadClientFile("App.tsx");
        var menu = ReadClientFile("navigation", "menu.ts");
        var page = ReadClientFile(
            "features",
            "csdt-realtime",
            "CsdtRealtimeSyncPage.tsx");
        var card = ReadClientFile(
            "features",
            "csdt-realtime",
            "RealtimeStreamCard.tsx");
        var reverse = ReadClientFile(
            "features",
            "csdt-realtime",
            "ReverseSyncPanel.tsx");
        var logic = ReadClientFile("features", "csdt-realtime", "logic.ts");
        var combinedScreen = string.Join('\n', menu, page, card, reverse);

        Assert.Contains(
            "path=\"/dong-bo-v2\" element={<CsdtRealtimeSyncPage />}",
            app,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MotoSyncPage", app, StringComparison.Ordinal);
        Assert.Contains("Đồng bộ dữ liệu CSĐT V1 ↔ V2", menu, StringComparison.Ordinal);
        Assert.Contains("requiredPermission: 'CanViewBusinessData'", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDL_OTO", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("CSDL_MOTO", logic, StringComparison.Ordinal);
        Assert.Contains("Delete tombstone", page, StringComparison.Ordinal);
        Assert.Contains("Đồng bộ thủ công V1 → V2", reverse, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST DATABASE", combinedScreen, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Realtime_api_uses_fixed_routes_cookie_credentials_and_no_store()
    {
        var api = ReadClientFile("features", "csdt-realtime", "api.ts");
        var apiFetch = ReadClientFile("api", "apiFetch.ts");

        Assert.Contains(
            "`${API_BASE}/dong-bo-v2/csdt-realtime`",
            api,
            StringComparison.Ordinal);
        Assert.Contains("`${REALTIME_API}/streams`", api, StringComparison.Ordinal);
        Assert.Contains("/history?${query}", api, StringComparison.Ordinal);
        Assert.Contains("/tombstones?${query}", api, StringComparison.Ordinal);
        Assert.Contains("/reverse-plan?${query}", api, StringComparison.Ordinal);
        Assert.Contains("/reverse-execute", api, StringComparison.Ordinal);
        Assert.Contains("/enabled", api, StringComparison.Ordinal);
        Assert.Contains("/baseline", api, StringComparison.Ordinal);
        Assert.Contains("/retry", api, StringComparison.Ordinal);
        Assert.Contains("return await apiFetch", api, StringComparison.Ordinal);
        Assert.Contains("cache: 'no-store'", api, StringComparison.Ordinal);
        Assert.Contains("credentials: 'include'", api, StringComparison.Ordinal);
        Assert.Contains("credentials: 'include'", apiFetch, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_write_requests_cannot_select_database_or_profile()
    {
        var types = ReadClientFile("features", "csdt-realtime", "types.ts");
        var logic = ReadClientFile("features", "csdt-realtime", "logic.ts");
        var writeRequests = string.Join(
            '\n',
            ExtractInterface(types, "CsdtRealtimeEnableRequest"),
            ExtractInterface(types, "CsdtRealtimeBaselineRequest"),
            ExtractInterface(types, "CsdtRealtimeRetryRequest"),
            ExtractInterface(types, "CsdtReverseExecuteRequest"));

        Assert.DoesNotContain("database", writeRequests, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile", writeRequests, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vehicleType: CsdtRealtimeVehicleType", writeRequests, StringComparison.Ordinal);
        Assert.Contains("expectedStateToken", writeRequests, StringComparison.Ordinal);
        Assert.Contains("expectedPlanToken", writeRequests, StringComparison.Ordinal);
        Assert.Contains("'OTO_V2_TO_V1'", logic, StringComparison.Ordinal);
        Assert.Contains("'MOTO_V2_TO_V1'", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceProfileCode: 'OTO_V2'", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("targetProfileCode: 'OTO_V1'", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceProfileCode: 'MOTO_V2'", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("targetProfileCode: 'MOTO_V1'", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceDatabaseName:", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("targetDatabaseName:", logic, StringComparison.Ordinal);
        Assert.Contains("status.sourceProfileCode.length > 0", logic, StringComparison.Ordinal);
        Assert.Contains("status.sourceDatabaseName.length > 0", logic, StringComparison.Ordinal);
        Assert.Contains("hasExpectedStreamMapping", logic, StringComparison.Ordinal);
    }

    [Fact]
    public void Employee_and_Viewer_get_read_only_UI_while_Admin_actions_keep_tokens_and_double_click_guards()
    {
        var page = ReadClientFile(
            "features",
            "csdt-realtime",
            "CsdtRealtimeSyncPage.tsx");
        var card = ReadClientFile(
            "features",
            "csdt-realtime",
            "RealtimeStreamCard.tsx");
        var reverse = ReadClientFile(
            "features",
            "csdt-realtime",
            "ReverseSyncPanel.tsx");
        var logic = ReadClientFile("features", "csdt-realtime", "logic.ts");

        Assert.Contains("const isAdmin = user?.role === 'Admin'", page, StringComparison.Ordinal);
        Assert.Contains("Bạn đang ở chế độ chỉ xem.", page, StringComparison.Ordinal);
        Assert.Contains("{isAdmin ? (", card, StringComparison.Ordinal);
        Assert.Contains("Chỉ tài khoản Admin", card, StringComparison.Ordinal);
        Assert.Contains("{isAdmin ? (", reverse, StringComparison.Ordinal);
        Assert.Contains("Chỉ Admin được thực thi", reverse, StringComparison.Ordinal);
        Assert.Contains("expectedStateToken: status.stateToken", page, StringComparison.Ordinal);
        Assert.Contains("expectedPlanToken: plan.planToken", reverse, StringComparison.Ordinal);
        Assert.Contains("actionKeysRef.current.has(actionKey)", page, StringComparison.Ordinal);
        Assert.Contains("operationRef.current", reverse, StringComparison.Ordinal);
        Assert.Contains("status.writeAuthorized", logic, StringComparison.Ordinal);
        Assert.Contains("status.currentUserRole !== 'Admin'", logic, StringComparison.Ordinal);
    }

    [Fact]
    public void Realtime_status_polling_is_fast_only_while_active_and_refreshes_on_focus()
    {
        var page = ReadClientFile(
            "features",
            "csdt-realtime",
            "CsdtRealtimeSyncPage.tsx");
        var logic = ReadClientFile("features", "csdt-realtime", "logic.ts");

        Assert.Contains("ACTIVE_POLL_INTERVAL_MS = 2_500", page, StringComparison.Ordinal);
        Assert.Contains("IDLE_POLL_INTERVAL_MS = 10_000", page, StringComparison.Ordinal);
        Assert.Contains("shouldPollRealtimeFast(response.streams)", page, StringComparison.Ordinal);
        Assert.Contains("controller?.abort()", page, StringComparison.Ordinal);
        Assert.Contains("statusRequestIdRef", page, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('focus'", page, StringComparison.Ordinal);
        Assert.Contains("isRealtimeBusy", logic, StringComparison.Ordinal);
    }

    private static string ExtractInterface(string source, string interfaceName)
    {
        var marker = $"export interface {interfaceName}";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing interface: {interfaceName}");
        var next = source.IndexOf("\nexport interface ", start + marker.Length, StringComparison.Ordinal);
        var end = next < 0 ? source.Length : next;
        return source[start..end];
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

        throw new FileNotFoundException(
            "Cannot locate workspace file.",
            Path.Combine(pathParts));
    }
}
