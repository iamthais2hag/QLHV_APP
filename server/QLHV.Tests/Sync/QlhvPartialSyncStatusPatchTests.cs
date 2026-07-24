using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class QlhvPartialSyncStatusPatchTests
{
    [Fact]
    public void Patch_is_transactional_rerunnable_and_schema_only()
    {
        var patch = ReadPatch();

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRY", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN CATCH", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains(
            "IF OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NULL",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL",
            patch,
            StringComparison.Ordinal);

        Assert.DoesNotContain("BACKUP DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Patch_enables_partial_success_for_history_and_auto_sync_with_trusted_checks()
    {
        var patch = ReadPatch();

        Assert.Contains("N'PARTIAL_SUCCESS'", patch, StringComparison.Ordinal);
        Assert.Contains(
            "CK_App_QlhvSyncOperationHistory_Status",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "CK_App_QlhvSyncOperationHistory_StatusTimestamps",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "CK_App_QlhvAutoSyncRun_Status",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "CK_App_QlhvAutoSyncRun_StatusTimestamps",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "WITH CHECK CHECK CONSTRAINT CK_App_QlhvSyncOperationHistory_Status",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_Status",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "AND (is_disabled = 1 OR is_not_trusted = 1)",
            patch,
            StringComparison.Ordinal);
    }

    private static string ReadPatch()
        => File.ReadAllText(FindWorkspaceFile(
            "database",
            "patches",
            "20260724_allow_qlhv_partial_sync_status.sql"));

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
