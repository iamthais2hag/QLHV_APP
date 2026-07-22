using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class QlhvImportClientSafetySourceTests
{
    [Fact]
    public void Fixed_source_catalog_and_refresh_contract_do_not_accept_database_input()
    {
        var logic = ReadClientFile("logic.ts");
        var types = ReadClientFile("types.ts");
        var api = ReadClientFile("api.ts");
        var refreshRequest = ExtractBlock(types, "export interface QlhvRefreshBackupRequest");

        Assert.Contains("liveDatabaseName: 'CSDL_OTO'", logic, StringComparison.Ordinal);
        Assert.Contains("backupDatabaseName: 'CSDL_OTO_BAK'", logic, StringComparison.Ordinal);
        Assert.Contains("sourceProfileCode: 'CSDT_OTO'", logic, StringComparison.Ordinal);
        Assert.Contains("maCSDT: '66029'", logic, StringComparison.Ordinal);
        Assert.Contains("liveDatabaseName: 'CSDL_MOTO'", logic, StringComparison.Ordinal);
        Assert.Contains("backupDatabaseName: 'CSDL_MOTO_BAK'", logic, StringComparison.Ordinal);
        Assert.Contains("sourceProfileCode: 'CSDT_MOTO'", logic, StringComparison.Ordinal);
        Assert.Contains("maCSDT: '66030'", logic, StringComparison.Ordinal);

        Assert.Contains("sourceType: QlhvImportSourceKind", refreshRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmText", refreshRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database", refreshRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server", refreshRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Path", refreshRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maCSDT", refreshRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceProfileCode", refreshRequest, StringComparison.Ordinal);
        Assert.Contains("body: JSON.stringify(request)", api, StringComparison.Ordinal);
        Assert.DoesNotContain("REFRESH CSDL BAK", logic, StringComparison.Ordinal);
    }

    [Fact]
    public void Qlhv_client_has_no_operations_key_or_typed_confirmation_flow()
    {
        var api = ReadClientFile("api.ts");
        var logic = ReadClientFile("logic.ts");
        var page = ReadClientFile("QlhvImportPage.tsx");
        var types = ReadClientFile("types.ts");
        var combined = string.Join('\n', api, logic, page, types);

        Assert.DoesNotContain("X-QLHV-Operations-Key", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operationsKey", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmText", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OperationsKeyField", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmationField", page, StringComparison.Ordinal);
        Assert.DoesNotContain("REFRESH CSDL BAK", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IMPORT QLHV CSĐT", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("console.log", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refreshQlhvBackup(body)", page, StringComparison.Ordinal);
        Assert.Contains("executeQlhvImport(body)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_snapshot_ui_has_no_course_filter_and_sends_snapshot_token()
    {
        var api = ReadClientFile("api.ts");
        var logic = ReadClientFile("logic.ts");
        var page = ReadClientFile("QlhvImportPage.tsx");
        var types = ReadClientFile("types.ts");
        var buildQuery = ExtractFunction(api, "function buildQuery");

        Assert.DoesNotContain("maKhoaHoc", page, StringComparison.Ordinal);
        Assert.DoesNotContain("maKhoaHoc", buildQuery, StringComparison.Ordinal);
        Assert.Contains("maKhoaHoc: null", logic, StringComparison.Ordinal);
        Assert.Contains("backupSnapshotToken: string", types, StringComparison.Ordinal);
        Assert.Contains("generatedAtUtc: string", types, StringComparison.Ordinal);
        Assert.Contains("expectedSnapshotToken: string", types, StringComparison.Ordinal);
        Assert.Contains("expectedSnapshotToken: plan.data.backupSnapshotToken", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmText", logic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stale_busy_pending_and_polling_guards_are_visible_in_source()
    {
        var logic = ReadClientFile("logic.ts");
        var page = ReadClientFile("QlhvImportPage.tsx");

        Assert.Contains("isPlanSnapshotCurrent", logic, StringComparison.Ordinal);
        Assert.Contains("plan.backupSnapshotToken === status.backupSnapshotToken", logic, StringComparison.Ordinal);
        Assert.Contains("isOperationBusy(status)", logic, StringComparison.Ordinal);
        Assert.Contains("plan.data.sourceHocVienRows > 0", logic, StringComparison.Ordinal);
        Assert.Contains("plan.data.blockers.length === 0", logic, StringComparison.Ordinal);
        Assert.Contains("status.canSync", logic, StringComparison.Ordinal);
        Assert.Contains("pendingOperationId", page, StringComparison.Ordinal);
        Assert.Contains("!!state.pendingOperationId", page, StringComparison.Ordinal);
        Assert.Contains("window.setInterval", page, StringComparison.Ordinal);
        Assert.Contains("POLL_INTERVAL_MS", page, StringComparison.Ordinal);
        Assert.Contains("plan: null", page, StringComparison.Ordinal);
        Assert.Contains(".jp2", page, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadClientFile(string fileName)
        => File.ReadAllText(FindWorkspaceFile(
            "client", "src", "features", "qlhv-import", fileName));

    private static string ExtractBlock(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {marker}");
        var end = source.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing closing brace after: {marker}");
        return source[start..(end + 2)];
    }

    private static string ExtractFunction(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {marker}");
        var nextFunction = source.IndexOf("\nfunction ", start + marker.Length, StringComparison.Ordinal);
        var nextAsyncFunction = source.IndexOf("\nasync function ", start + marker.Length, StringComparison.Ordinal);
        var candidates = new[] { nextFunction, nextAsyncFunction }.Where(index => index > start).ToArray();
        var end = candidates.Length == 0 ? source.Length : candidates.Min();
        return source[start..end];
    }

    private static string FindWorkspaceFile(
        string firstPathPart,
        params string[] remainingPathParts)
        => FindWorkspaceFileFromCaller(
            new[] { firstPathPart }.Concat(remainingPathParts).ToArray());

    private static string FindWorkspaceFileFromCaller(
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
