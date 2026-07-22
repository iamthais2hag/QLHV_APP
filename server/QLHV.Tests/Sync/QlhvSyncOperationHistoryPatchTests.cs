using System.Runtime.CompilerServices;

namespace QLHV.Tests.Sync;

public sealed class QlhvSyncOperationHistoryPatchTests
{
    [Fact]
    public void Patch_is_transactional_idempotent_and_schema_only()
    {
        var patch = ReadPatch();

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("GO", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NULL", patch, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", patch, StringComparison.Ordinal);

        Assert.DoesNotContain("BACKUP DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QlhvOperations__AdminKey", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("X-QLHV-Operations-Key", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void Patch_covers_operation_history_contract_and_partition_lock_index()
    {
        var patch = ReadPatch();

        var requiredColumns = new[]
        {
            "OperationId",
            "SourceType",
            "OperationType",
            "Status",
            "LiveDatabaseName",
            "BackupDatabaseName",
            "MaCSDT",
            "SourceProfileCode",
            "CreatedAtUtc",
            "StartedAtUtc",
            "CompletedAtUtc",
            "UpdatedAtUtc",
            "LiveRows",
            "BackupRows",
            "TargetActiveRows",
            "SourceRows",
            "InsertedRows",
            "UpdatedRows",
            "ReactivatedRows",
            "SoftDeletedRows",
            "SkippedRows",
            "SnapshotToken",
            "ErrorMessage",
            "DetailJson",
        };
        foreach (var column in requiredColumns)
        {
            Assert.Contains(column, patch, StringComparison.Ordinal);
        }

        Assert.Contains("SourceType IN (N'OTO', N'MOTO')", patch, StringComparison.Ordinal);
        Assert.Contains("OperationType IN (N'REFRESH_BACKUP', N'FULL_SYNC')", patch, StringComparison.Ordinal);
        Assert.Contains(
            "Status IN (N'QUEUED', N'RUNNING', N'SUCCEEDED', N'FAILED')",
            patch,
            StringComparison.Ordinal);
        Assert.Contains("ISJSON(DetailJson) = 1", patch, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE UNIQUE NONCLUSTERED INDEX UX_App_QlhvSyncOperationHistory_ActiveSource",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON dbo.App_QlhvSyncOperationHistory (SourceType)",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE Status IN (N'QUEUED', N'RUNNING')",
            patch,
            StringComparison.Ordinal);
    }

    private static string ReadPatch()
        => File.ReadAllText(FindWorkspaceFile(
            "database",
            "patches",
            "20260722_add_qlhv_sync_operation_history.sql"));

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
