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
        Assert.Contains("Auto Sync đang chạy; không thể gửi yêu cầu lần hai.", panel, StringComparison.Ordinal);
        Assert.Contains("disabled={disabledReason !== null}", panel, StringComparison.Ordinal);
        Assert.Contains("aria-busy={starting || running}", panel, StringComparison.Ordinal);
        Assert.Contains("startingRef.current", panel, StringComparison.Ordinal);
        Assert.Contains("Chạy Auto Sync ngay", panel, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_AUTO_SYNC", panel, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_SESSION_START", panel, StringComparison.Ordinal);
        Assert.Contains("sessionStartRunId", panel, StringComparison.Ordinal);
        Assert.Contains("trackedSessionRunId", panel, StringComparison.Ordinal);
        Assert.Contains("manualRunId", panel, StringComparison.Ordinal);
        Assert.Contains("setManualRunId(result.runId)", panel, StringComparison.Ordinal);
        Assert.Contains("const trackedRunId = manualRunId ?? trackedSessionRunId", panel, StringComparison.Ordinal);
        Assert.Contains("currentStage", panel, StringComparison.Ordinal);
        Assert.Contains("waitingForTrackedRun", panel, StringComparison.Ordinal);
        Assert.Contains("const shouldPoll = running || waitingForTrackedRun", panel, StringComparison.Ordinal);
        Assert.Contains("shouldPoll ? POLL_INTERVAL_MS : IDLE_POLL_INTERVAL_MS", panel, StringComparison.Ordinal);
        Assert.Contains("notifiedTerminalRunRef.current = trackedRunId", panel, StringComparison.Ordinal);
        Assert.Contains("getQlhvAutoSyncStatus(requestedRunId)", panel, StringComparison.Ordinal);
        Assert.Contains("parameters.set('runId', runId)", api, StringComparison.Ordinal);
        Assert.Contains("trackedStatus.found", panel, StringComparison.Ordinal);
        Assert.Contains("statusQueryRunId === trackedRunId", panel, StringComparison.Ordinal);
        Assert.Contains("requestId !== loadRequestIdRef.current", panel, StringComparison.Ordinal);
        Assert.Contains("onBusyChange?.(busy)", panel, StringComparison.Ordinal);
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
        Assert.Contains("Viewer chỉ được xem ảnh", panel, StringComparison.Ordinal);
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
