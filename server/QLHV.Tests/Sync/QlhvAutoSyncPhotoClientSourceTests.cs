using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class QlhvAutoSyncPhotoClientSourceTests
{
    [Fact]
    public void Auto_sync_ui_uses_cookie_api_and_preserves_admin_busy_guards()
    {
        var api = ReadClientFile("features", "qlhv-import", "api.ts");
        var panel = ReadClientFile("features", "qlhv-import", "AutoSyncPanel.tsx");
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");
        var combined = string.Join('\n', api, panel, page);

        Assert.Contains("/operations/auto-sync/status", api, StringComparison.Ordinal);
        Assert.Contains("/operations/auto-sync`", api, StringComparison.Ordinal);
        Assert.Contains("method: 'POST'", api, StringComparison.Ordinal);
        Assert.Contains("isAdmin", panel, StringComparison.Ordinal);
        Assert.Contains("status.configuration.manualRunAllowed", panel, StringComparison.Ordinal);
        Assert.Contains("disabled={disabledReason !== null}", panel, StringComparison.Ordinal);
        Assert.Contains("Chạy Auto Sync dự phòng", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStartRunId", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("manualRunId", panel, StringComparison.Ordinal);
        Assert.Contains("status.autoSyncRuntime.isRunActive", panel, StringComparison.Ordinal);
        Assert.Contains("UI_REFRESH_INTERVAL_MS", panel, StringComparison.Ordinal);
        Assert.Contains("getQlhvAutoSyncStatus()", panel, StringComparison.Ordinal);
        Assert.Contains("parameters.set('runId', runId)", api, StringComparison.Ordinal);
        Assert.Contains("id !== requestId.current", panel, StringComparison.Ordinal);
        Assert.Contains("onBusyChange?.(active || starting)", panel, StringComparison.Ordinal);
        Assert.Contains("status.history", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("operationHistory", panel, StringComparison.Ordinal);
        Assert.Contains("AUTO_SYNC_BUSY_MESSAGE", page, StringComparison.Ordinal);
        Assert.Contains("onBusyChange={handleAutoSyncBusyChange}", page, StringComparison.Ordinal);
        Assert.Contains("if (autoSyncBusy)", page, StringComparison.Ordinal);
        Assert.Contains("await dataVersion.reload()", page, StringComparison.Ordinal);
        Assert.Contains(
            "writeBlockedReason={autoSyncBusy ? AUTO_SYNC_BUSY_MESSAGE : null}",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("setStatus(null)", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations-Key", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmText", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auto_sync_screen_labels_both_pipelines_to_qlhv_app_and_uses_durable_history()
    {
        var logic = ReadClientFile("features", "qlhv-import", "logic.ts");
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");
        var panel = ReadClientFile("features", "qlhv-import", "AutoSyncPanel.tsx");

        Assert.Equal(2, CountOccurrences(logic, "targetDatabaseName: 'QLHV_APP'"));
        Assert.Contains("<code>{source.targetDatabaseName}</code>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<code>{source.sourceProfileCode}</code>", page, StringComparison.Ordinal);
        Assert.Contains("value.targetDatabaseName === 'QLHV_APP'", ReadClientFile(
            "features", "qlhv-import", "api.ts"), StringComparison.Ordinal);
        Assert.Contains("status.history", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("operationHistory", panel, StringComparison.Ordinal);
        Assert.Contains("Realtime trực tiếp — đường chính", panel, StringComparison.Ordinal);
        Assert.Contains("Auto Sync dự phòng", panel, StringComparison.Ordinal);
        Assert.Contains("CSDL_OTO / CSDL_MOTO → QLHV_APP", panel, StringComparison.Ordinal);
        Assert.Contains("status.realtime.profiles", panel, StringComparison.Ordinal);
        Assert.Contains("Lịch sử stale — không hoạt động", panel, StringComparison.Ordinal);
        Assert.Contains("Windows Service", panel, StringComparison.Ordinal);
        Assert.Contains("HEALTHY_NO_CHANGE", panel, StringComparison.Ordinal);
        Assert.Contains("Writer chính", panel, StringComparison.Ordinal);
        Assert.Contains("QLHV Realtime", panel, StringComparison.Ordinal);
        Assert.Contains("Đang bảo vệ", panel, StringComparison.Ordinal);
        Assert.Contains("RunOnServerStartup", panel, StringComparison.Ordinal);
        Assert.Contains("Active run / slot / operation", panel, StringComparison.Ordinal);
        Assert.Contains("Bị khóa bởi Realtime", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_sync_screen_and_build_emit_runtime_identity_without_a_service_worker()
    {
        var panel = ReadClientFile("features", "qlhv-import", "AutoSyncPanel.tsx");
        var api = ReadClientFile("features", "qlhv-import", "api.ts");
        var buildIdentity = ReadClientFile("buildIdentity.ts");
        var vite = File.ReadAllText(FindWorkspaceFile(["client", "vite.config.ts"]));
        var program = File.ReadAllText(FindWorkspaceFile(
            ["server", "QLHV.Api", "Program.cs"]));

        Assert.Contains("FRONTEND_BUILD_ID", panel, StringComparison.Ordinal);
        Assert.Contains("status.runtime.apiBuildId", panel, StringComparison.Ordinal);
        Assert.Contains("status.runtime.workerBuildId", panel, StringComparison.Ordinal);
        Assert.Contains("lastRefresh", panel, StringComparison.Ordinal);
        Assert.Contains("isRuntimeBuildIdentity", api, StringComparison.Ordinal);
        Assert.Contains("__QLHV_FRONTEND_BUILD_ID__", buildIdentity, StringComparison.Ordinal);
        Assert.Contains("build-info.json", vite, StringComparison.Ordinal);
        Assert.Contains("X-QLHV-API-Build", program, StringComparison.Ordinal);
        Assert.Contains("no-cache, no-store, must-revalidate", program, StringComparison.Ordinal);
        var fallback = program[program.IndexOf("app.MapMethods", StringComparison.Ordinal)..];
        Assert.Contains("context.Response.Headers.CacheControl", fallback, StringComparison.Ordinal);
        Assert.Contains("context.Response.SendFileAsync(indexFile)", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("navigator.serviceWorker", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("registerSW", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_plan_message_is_not_presented_as_an_auto_sync_blocker()
    {
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");

        Assert.Contains("<strong>Full sync ", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>Full sync:</strong>", page, StringComparison.Ordinal);
        Assert.Contains("plan: null", page, StringComparison.Ordinal);
        Assert.Contains("isPlanSnapshotCurrent", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Auto_sync_data_gap_probe_is_read_only_and_documents_count_scopes()
    {
        var controller = File.ReadAllText(FindWorkspaceFile(
            ["server", "QLHV.Api", "Controllers", "QlhvImportController.cs"]));
        var dtos = File.ReadAllText(FindWorkspaceFile(
            ["server", "QLHV.Application", "Sync", "Dtos", "QlhvAutoSyncDtos.cs"]));

        Assert.Contains("operations/auto-sync/diagnostics", controller, StringComparison.Ordinal);
        Assert.Contains("CSDT_AUTO_SYNC_DATA_GAP_V1", dtos, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly", dtos, StringComparison.Ordinal);
        Assert.Contains("LiveRows/BackupRows", controller, StringComparison.Ordinal);
        Assert.Contains("TargetActiveRows", controller, StringComparison.Ordinal);
        Assert.Contains("ContentToken", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpPost(\"operations/auto-sync/diagnostics", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_version_hook_polls_refetches_on_focus_and_supports_manual_reload()
    {
        var api = ReadClientFile("features", "data-version", "api.ts");
        var hook = ReadClientFile("features", "data-version", "useDataVersionRefresh.ts");
        var importPage = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");
        var hocVien = ReadClientFile("features", "hoc-vien", "HocVienPage.tsx");
        var print = ReadClientFile("features", "hoc-vien", "HocVienCardPrintPage.tsx");
        var module = ReadClientFile("pages", "ModulePage.tsx");

        Assert.Contains("/system/data-version", api, StringComparison.Ordinal);
        Assert.Contains("window.setInterval", hook, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('focus'", hook, StringComparison.Ordinal);
        Assert.Contains("resourcesRef.current.some", hook, StringComparison.Ordinal);
        Assert.Contains("onVersionChanged", hook, StringComparison.Ordinal);
        Assert.Contains("refreshPendingRef.current", hook, StringComparison.Ordinal);
        Assert.Contains("const changed = previous === null", hook, StringComparison.Ordinal);
        var callbackIndex = hook.IndexOf(
            "await callbackRef.current(next, previous);",
            StringComparison.Ordinal);
        var versionCommitIndex = hook.IndexOf(
            "versionRef.current = next;",
            StringComparison.Ordinal);
        Assert.True(callbackIndex >= 0);
        Assert.True(versionCommitIndex > callbackIndex);
        Assert.Contains("getHocVienPhotoCacheVersion", hocVien, StringComparison.Ordinal);
        Assert.Contains("dataVersion.version?.hocVienVersion", hocVien, StringComparison.Ordinal);
        Assert.Contains("dataVersion.version?.photoVersion", hocVien, StringComparison.Ordinal);
        Assert.Contains("getHocVienPhotoCacheVersion", print, StringComparison.Ordinal);
        Assert.Contains("dataVersion.version?.hocVienVersion", print, StringComparison.Ordinal);
        Assert.Contains("dataVersion.version?.photoVersion", print, StringComparison.Ordinal);
        Assert.Contains("Tải lại dữ liệu", importPage, StringComparison.Ordinal);
        Assert.Contains("Tải lại dữ liệu", hocVien, StringComparison.Ordinal);
        Assert.Contains("Tải lại dữ liệu", print, StringComparison.Ordinal);
        Assert.Contains("khoaHocVersion", module, StringComparison.Ordinal);
        Assert.Contains("giaoVienVersion", module, StringComparison.Ordinal);
        Assert.Contains("Tải lại dữ liệu", module, StringComparison.Ordinal);
    }

    [Fact]
    public void Photo_management_compares_images_and_keeps_admin_review_controls()
    {
        var panel = ReadClientFile("features", "qlhv-import", "PhotoProcessingPanel.tsx");
        var api = ReadClientFile("features", "qlhv-import", "api.ts");

        Assert.Contains("Ảnh gốc", panel, StringComparison.Ordinal);
        Assert.Contains("Ảnh nền xanh", panel, StringComparison.Ordinal);
        Assert.Contains("Confidence", panel, StringComparison.Ordinal);
        Assert.Contains("Đường dẫn gốc", panel, StringComparison.Ordinal);
        Assert.Contains("INVALID_PATH", panel, StringComparison.Ordinal);
        Assert.Contains("LEGACY_PATH", panel, StringComparison.Ordinal);
        Assert.Contains("item.errorMessage", panel, StringComparison.Ordinal);
        Assert.Contains("Chấp nhận", panel, StringComparison.Ordinal);
        Assert.Contains("Xử lý lại", panel, StringComparison.Ordinal);
        Assert.Contains("Chỉ xem “Cần kiểm tra”", panel, StringComparison.Ordinal);
        Assert.Contains("Thành công", panel, StringComparison.Ordinal);
        Assert.Contains("Thất bại", panel, StringComparison.Ordinal);
        Assert.Contains("Cần kiểm tra", panel, StringComparison.Ordinal);
        Assert.Contains("Tài khoản hiện tại chỉ được xem ảnh", panel, StringComparison.Ordinal);
        Assert.Contains("writeBlockedReason", panel, StringComparison.Ordinal);
        Assert.Contains(
            "disabled={!isAdmin || !!writeBlockedReason || pending",
            panel,
            StringComparison.Ordinal);
        Assert.Contains("!data.engineReady || !canApprove(item)", panel, StringComparison.Ordinal);
        Assert.Contains("pendingIds.has(item.id)", panel, StringComparison.Ordinal);
        Assert.Contains("pendingIdsRef.current.has(item.id)", panel, StringComparison.Ordinal);
        Assert.Contains("runPhotoAction(id, 'approve'", api, StringComparison.Ordinal);
        Assert.Contains("runPhotoAction(id, 'reprocess'", api, StringComparison.Ordinal);
        Assert.Contains("/photos/${id}/${action}", api, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_page_shows_auto_sync_photo_plan_and_photo_management_sections()
    {
        var page = ReadClientFile("features", "qlhv-import", "QlhvImportPage.tsx");
        var types = ReadClientFile("features", "qlhv-import", "types.ts");

        Assert.Contains("<AutoSyncPanel", page, StringComparison.Ordinal);
        Assert.Contains("<PhotoProcessingPanel", page, StringComparison.Ordinal);
        Assert.Contains("<ImportPhotoCounts", page, StringComparison.Ordinal);
        Assert.Contains("Tìm thấy", page, StringComparison.Ordinal);
        Assert.Contains("Cần xử lý lại", page, StringComparison.Ordinal);
        Assert.Contains("reviewRequired", types, StringComparison.Ordinal);
        Assert.Contains("sourcePreviewUrl", types, StringComparison.Ordinal);
        Assert.Contains("outputPreviewUrl", types, StringComparison.Ordinal);
    }

    private static string ReadClientFile(params string[] pathParts)
        => File.ReadAllText(FindWorkspaceFile(
            new[] { "client", "src" }.Concat(pathParts).ToArray()));

    private static int CountOccurrences(string value, string fragment)
        => value.Split(fragment, StringSplitOptions.None).Length - 1;

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
