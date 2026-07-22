using System.Runtime.CompilerServices;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvBackupRefreshSafetyTests
{
    [Theory]
    [InlineData("CSDL_OTO")]
    [InlineData("CSDL_OTO_BAK")]
    [InlineData("CSDL_MOTO")]
    [InlineData("CSDL_MOTO_BAK")]
    public void Backup_sql_is_allowlisted_compressed_checked_and_parameterized(string databaseName)
    {
        var sql = QlhvBackupRefreshExecutor.BuildBackupSql(databaseName);

        Assert.Contains($"BACKUP DATABASE [{databaseName}]", sql, StringComparison.Ordinal);
        Assert.Contains("TO DISK = @BackupPath", sql, StringComparison.Ordinal);
        Assert.Contains("COPY_ONLY", sql, StringComparison.Ordinal);
        Assert.Contains("CHECKSUM", sql, StringComparison.Ordinal);
        Assert.Contains("COMPRESSION", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(QlhvBackupRefreshExecutor.BackupDirectory, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Backup_and_restore_reject_arbitrary_database_identifiers()
    {
        Assert.Throws<InvalidOperationException>(() =>
            QlhvBackupRefreshExecutor.BuildBackupSql("CSDL_CUSTOM"));
        Assert.Throws<InvalidOperationException>(() =>
            QlhvBackupRefreshExecutor.BuildRestoreSql(
                "CSDL_CUSTOM",
                new[] { new QlhvBackupRefreshExecutor.RestoreFileMapping("data", @"D:\data.mdf") }));
    }

    [Fact]
    public void Restore_sql_preserves_trusted_target_paths_and_required_guards()
    {
        var sql = QlhvBackupRefreshExecutor.BuildRestoreSql(
            "CSDL_OTO_BAK",
            new[]
            {
                new QlhvBackupRefreshExecutor.RestoreFileMapping("CSDL_OTO", @"D:\SQL_DATA\CSDL_OTO_BAK.mdf"),
                new QlhvBackupRefreshExecutor.RestoreFileMapping("CSDL_OTO_log", @"D:\SQL_DATA\CSDL_OTO_BAK_log.ldf"),
            });

        Assert.Contains("RESTORE DATABASE [CSDL_OTO_BAK] FROM DISK = @BackupPath", sql, StringComparison.Ordinal);
        Assert.Contains("WITH REPLACE, RECOVERY, CHECKSUM", sql, StringComparison.Ordinal);
        Assert.Contains("MOVE N'CSDL_OTO' TO N'D:\\SQL_DATA\\CSDL_OTO_BAK.mdf'", sql, StringComparison.Ordinal);
        Assert.Contains("MOVE N'CSDL_OTO_log' TO N'D:\\SQL_DATA\\CSDL_OTO_BAK_log.ldf'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Restore_file_mapping_matches_data_and_log_by_file_order()
    {
        var mappings = QlhvBackupRefreshExecutor.MapRestoreFiles(
            new[]
            {
                new QlhvBackupRefreshExecutor.BackupFileRow { LogicalName = "live_log", Type = "L", FileId = 3 },
                new QlhvBackupRefreshExecutor.BackupFileRow { LogicalName = "live_data_2", Type = "D", FileId = 2 },
                new QlhvBackupRefreshExecutor.BackupFileRow { LogicalName = "live_data_1", Type = "D", FileId = 1 },
            },
            new[]
            {
                new QlhvBackupRefreshExecutor.PhysicalFileRow { FileId = 2, Type = 0, PhysicalName = @"D:\bak2.ndf" },
                new QlhvBackupRefreshExecutor.PhysicalFileRow { FileId = 1, Type = 0, PhysicalName = @"D:\bak1.mdf" },
                new QlhvBackupRefreshExecutor.PhysicalFileRow { FileId = 3, Type = 1, PhysicalName = @"D:\bak.ldf" },
            });

        Assert.Equal(
            new[]
            {
                new QlhvBackupRefreshExecutor.RestoreFileMapping("live_data_1", @"D:\bak1.mdf"),
                new QlhvBackupRefreshExecutor.RestoreFileMapping("live_data_2", @"D:\bak2.ndf"),
                new QlhvBackupRefreshExecutor.RestoreFileMapping("live_log", @"D:\bak.ldf"),
            },
            mappings);
    }

    [Fact]
    public void Restore_file_mapping_rejects_unsupported_or_mismatched_files()
    {
        Assert.Throws<InvalidOperationException>(() => QlhvBackupRefreshExecutor.MapRestoreFiles(
            new[]
            {
                new QlhvBackupRefreshExecutor.BackupFileRow { LogicalName = "filestream", Type = "S", FileId = 1 },
            },
            Array.Empty<QlhvBackupRefreshExecutor.PhysicalFileRow>()));

        Assert.Throws<InvalidOperationException>(() => QlhvBackupRefreshExecutor.MapRestoreFiles(
            new[]
            {
                new QlhvBackupRefreshExecutor.BackupFileRow { LogicalName = "data", Type = "D", FileId = 1 },
                new QlhvBackupRefreshExecutor.BackupFileRow { LogicalName = "log", Type = "L", FileId = 2 },
            },
            new[]
            {
                new QlhvBackupRefreshExecutor.PhysicalFileRow { FileId = 1, Type = 0, PhysicalName = @"D:\bak.mdf" },
            }));
    }

    [Fact]
    public void Source_keeps_restore_recovery_and_session_lock_safety_contracts()
    {
        var executor = ReadWorkspaceFile("server", "QLHV.Infrastructure", "Sync", "QlhvBackupRefreshExecutor.cs");
        var worker = ReadWorkspaceFile("server", "QLHV.Infrastructure", "Sync", "QlhvRefreshBackupWorker.cs");
        var operationLock = ReadWorkspaceFile("server", "QLHV.Infrastructure", "Sync", "QlhvSqlSourceOperationLock.cs");

        Assert.Contains("RESTORE VERIFYONLY FROM DISK = @BackupPath WITH CHECKSUM", executor, StringComparison.Ordinal);
        Assert.Contains("RESTORE FILELISTONLY FROM DISK = @BackupPath", executor, StringComparison.Ordinal);
        Assert.Contains("_pre_refresh_", executor, StringComparison.Ordinal);
        Assert.Contains("SET ONLINE", executor, StringComparison.Ordinal);
        Assert.Contains("SET MULTI_USER WITH ROLLBACK IMMEDIATE", executor, StringComparison.Ordinal);
        Assert.Contains("TryRecoverDatabaseAccessAsync", worker, StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer", worker, StringComparison.Ordinal);
        Assert.Contains("sys.sp_getapplock", operationLock, StringComparison.Ordinal);
        Assert.Contains("@LockOwner = N'Session'", operationLock, StringComparison.Ordinal);
        Assert.Contains("@LockTimeout = 0", operationLock, StringComparison.Ordinal);
        Assert.Contains("sys.sp_releaseapplock", operationLock, StringComparison.Ordinal);
        Assert.Contains("SqlConnection.ClearPool", operationLock, StringComparison.Ordinal);
    }

    private static string ReadWorkspaceFile(string firstPart, params string[] remainingParts)
        => ReadWorkspaceFileFromCaller(new[] { firstPart }.Concat(remainingParts).ToArray());

    private static string ReadWorkspaceFileFromCaller(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}
