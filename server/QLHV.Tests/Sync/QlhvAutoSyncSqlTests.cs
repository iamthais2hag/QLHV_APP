using System.Runtime.CompilerServices;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvAutoSyncSqlTests
{
    [Fact]
    public void Auto_sync_patch_is_transactional_idempotent_and_has_global_active_slot()
    {
        var patch = ReadPatch("20260723_add_qlhv_auto_sync.sql");

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRY", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL", patch, StringComparison.Ordinal);
        Assert.Contains("Actor", patch, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_AUTO_SYNC", patch, StringComparison.Ordinal);
        Assert.Contains("SYSTEM_SESSION_START", patch, StringComparison.Ordinal);
        Assert.Contains("SESSION_START", patch, StringComparison.Ordinal);
        Assert.Contains("CurrentStage", patch, StringComparison.Ordinal);
        Assert.Contains("UX_App_QlhvAutoSyncRun_ActiveSlot", patch, StringComparison.Ordinal);
        Assert.Contains("WHERE ActiveSlot IS NOT NULL", patch, StringComparison.Ordinal);
        Assert.Contains("activeKey.key_ordinal = 1", patch, StringComparison.Ordinal);
        Assert.Contains("activeColumn.name = N'ActiveSlot'", patch, StringComparison.Ordinal);
        Assert.Contains("extraKey.key_ordinal > 1", patch, StringComparison.Ordinal);
        Assert.Contains("UX_App_QlhvAutoSyncRun_RunId", patch, StringComparison.Ordinal);
        Assert.Contains("PK_App_QlhvSyncPartitionState", patch, StringComparison.Ordinal);
        Assert.Contains("partitionColumn.name = N'SourceType'", patch, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT CK_App_QlhvSyncPartitionState_Source", patch, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT CK_App_QlhvSyncPartitionState_Rows", patch, StringComparison.Ordinal);
        Assert.Contains("SourceOrderJson", patch, StringComparison.Ordinal);
        Assert.Contains("OtoResultJson", patch, StringComparison.Ordinal);
        Assert.Contains("MotoResultJson", patch, StringComparison.Ordinal);

        Assert.DoesNotContain("BACKUP DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Data_version_patch_creates_singleton_monotonic_versions_without_business_updates()
    {
        var patch = ReadPatch("20260723_add_app_data_version.sql");

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("VersionId = 1", patch, StringComparison.Ordinal);
        Assert.Contains("HocVienVersion bigint", patch, StringComparison.Ordinal);
        Assert.Contains("KhoaHocVersion bigint", patch, StringComparison.Ordinal);
        Assert.Contains("GiaoVienVersion bigint", patch, StringComparison.Ordinal);
        Assert.Contains("PhotoVersion bigint", patch, StringComparison.Ordinal);
        Assert.Contains("LastSuccessfulSyncUtc", patch, StringComparison.Ordinal);
        Assert.Contains("CK_App_DataVersion_NonNegative", patch, StringComparison.Ordinal);

        Assert.DoesNotContain("UPDATE dbo.App_HocVien", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.App_KhoaHoc", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.App_GiaoVien", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BACKUP DATABASE", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE DATABASE", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Version_increment_is_after_all_merges_and_before_transaction_commit()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "QlhvHocVienTargetRepository.cs"));

        var course = source.IndexOf("MergeKhoaHoc", StringComparison.Ordinal);
        var teacher = source.IndexOf("MergeGiaoVien", StringComparison.Ordinal);
        var relation = source.IndexOf("MergeRelation", StringComparison.Ordinal);
        var student = source.IndexOf("QlhvFullSnapshotSyncSql.Merge", StringComparison.Ordinal);
        var version = source.IndexOf(
            "QlhvDataVersionSql.IncrementAfterSuccessfulFullSync",
            StringComparison.Ordinal);
        var commit = source.IndexOf("CommitAsync", version, StringComparison.Ordinal);

        Assert.True(course >= 0 && course < teacher);
        Assert.True(teacher < relation);
        Assert.True(relation < student);
        Assert.True(student < version);
        Assert.True(version < commit);
        Assert.Contains("transaction: transaction", source[version..commit], StringComparison.Ordinal);
    }

    [Fact]
    public void Version_sql_increments_sync_entities_but_not_photo_version()
    {
        var sql = QlhvDataVersionSql.IncrementAfterSuccessfulFullSync;

        Assert.Contains("HocVienVersion = HocVienVersion + 1", sql, StringComparison.Ordinal);
        Assert.Contains("KhoaHocVersion = KhoaHocVersion + 1", sql, StringComparison.Ordinal);
        Assert.Contains("GiaoVienVersion = GiaoVienVersion + 1", sql, StringComparison.Ordinal);
        Assert.Contains("LastSuccessfulSyncUtc = SYSUTCDATETIME()", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("PhotoVersion =", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_lock_uses_session_owned_sp_getapplock()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "QlhvSqlAutoSyncGlobalLock.cs"));

        Assert.Contains("sp_getapplock", source, StringComparison.Ordinal);
        Assert.Contains("QLHV:CSDT_AUTO_SYNC", source, StringComparison.Ordinal);
        Assert.Contains("@LockOwner = N'Session'", source, StringComparison.Ordinal);
        Assert.Contains("@LockTimeout = 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_dedupe_and_api_reads_prevent_duplicate_or_stale_sessions()
    {
        var repository = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "QlhvAutoSyncRunRepository.cs"));
        var program = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Api",
            "Program.cs"));
        var clientFetch = File.ReadAllText(FindWorkspaceFile(
            "client",
            "src",
            "api",
            "apiFetch.ts"));

        Assert.Contains("@DedupeNotBeforeUtc", repository, StringComparison.Ordinal);
        Assert.Contains("TriggerType = @TriggerType", repository, StringComparison.Ordinal);
        Assert.Contains("CreatedAtUtc >= @DedupeNotBeforeUtc", repository, StringComparison.Ordinal);
        Assert.Contains("no-store, no-cache, must-revalidate", program, StringComparison.Ordinal);
        Assert.Contains("cache: init.cache ?? 'no-store'", clientFetch, StringComparison.Ordinal);
    }

    private static string ReadPatch(string fileName)
        => File.ReadAllText(FindWorkspaceFile("database", "patches", fileName));

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
