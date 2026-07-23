using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
        Assert.Contains("WHERE ActiveSlot = 1", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE ActiveSlot IS NOT NULL", patch, StringComparison.Ordinal);
        Assert.Contains("WITH (TABLOCKX, HOLDLOCK)", patch, StringComparison.Ordinal);
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
    public void Auto_sync_patch_finishes_added_column_batches_before_referencing_them()
    {
        var patch = ReadPatch("20260723_add_qlhv_auto_sync.sql");
        var batches = SplitSqlBatches(patch);

        var actorAddBatch = FindSingleBatch(
            batches,
            @"\bADD\s+Actor\s+nvarchar\(100\)\s+NOT\s+NULL\b");
        var actorVerifyBatch = FindSingleBatch(
            batches,
            @"THROW\s+527327,\s*'Failed to add dbo\.App_QlhvSyncOperationHistory\.Actor\.'");
        var actorReferenceBatch = FindFirstBatch(
            batches,
            @"\bActor\s+IN\s*\(");

        Assert.Contains(
            "COL_LENGTH(N'dbo.App_QlhvSyncOperationHistory', N'Actor') IS NULL",
            batches[actorAddBatch],
            StringComparison.Ordinal);
        Assert.True(actorAddBatch < actorVerifyBatch);
        Assert.True(actorVerifyBatch < actorReferenceBatch);
        AssertColumnAddBatchHasNoCompiledReferences(batches[actorAddBatch], "Actor");

        var currentStageAddBatch = FindSingleBatch(
            batches,
            @"\bADD\s+CurrentStage\s+nvarchar\(32\)\s+NULL\b");
        var currentStageVerifyBatch = FindSingleBatch(
            batches,
            @"THROW\s+527328,\s*'Failed to add dbo\.App_QlhvAutoSyncRun\.CurrentStage\.'");
        var currentStageUpdateBatch = FindSingleBatch(
            batches,
            @"UPDATE\s+dbo\.App_QlhvAutoSyncRun\s+SET\s+CurrentStage");

        Assert.Contains(
            "COL_LENGTH(N'dbo.App_QlhvAutoSyncRun', N'CurrentStage') IS NULL",
            batches[currentStageAddBatch],
            StringComparison.Ordinal);
        Assert.True(currentStageAddBatch < currentStageVerifyBatch);
        Assert.True(currentStageVerifyBatch < currentStageUpdateBatch);
        AssertColumnAddBatchHasNoCompiledReferences(
            batches[currentStageAddBatch],
            "CurrentStage");
    }

    [Fact]
    public void Auto_sync_patch_guards_column_adds_for_first_run_and_rerun()
    {
        var patch = ReadPatch("20260723_add_qlhv_auto_sync.sql");
        var batches = SplitSqlBatches(patch);

        Assert.Single(
            Regex.Matches(
                patch,
                @"\bADD\s+Actor\s+nvarchar\(100\)\s+NOT\s+NULL\b",
                RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Single(
            Regex.Matches(
                patch,
                @"\bADD\s+CurrentStage\s+nvarchar\(32\)\s+NULL\b",
                RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Matches(
            @"IF\s+COL_LENGTH\(N'dbo\.App_QlhvSyncOperationHistory',\s*N'Actor'\)\s+IS\s+NULL[\s\S]*?\bADD\s+Actor\b",
            patch);
        Assert.Matches(
            @"IF\s+COL_LENGTH\(N'dbo\.App_QlhvAutoSyncRun',\s*N'CurrentStage'\)\s+IS\s+NULL[\s\S]*?\bADD\s+CurrentStage\b",
            patch);
        Assert.Contains(
            "currentStageColumn.name = N'CurrentStage'",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "currentStageDefault.parent_column_id",
            patch,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\bDELETE\b", patch);

        var alteredColumns = Regex.Matches(
                patch,
                @"(?is)\bALTER\s+TABLE\b[^;]*?\bADD\s+(?!CONSTRAINT\b)(?<column>\w+)")
            .Cast<Match>()
            .Select(match => match.Groups["column"].Value)
            .ToArray();

        Assert.Equal(new[] { "Actor", "CurrentStage", "ActiveSlot" }, alteredColumns);

        foreach (var transactionalBatch in batches.Where(
                     batch => batch.Contains(
                         "BEGIN TRANSACTION;",
                         StringComparison.Ordinal)))
        {
            Assert.Contains("BEGIN TRY", transactionalBatch, StringComparison.Ordinal);
            Assert.Contains("COMMIT TRANSACTION;", transactionalBatch, StringComparison.Ordinal);
            Assert.Contains("BEGIN CATCH", transactionalBatch, StringComparison.Ordinal);
            Assert.Contains("ROLLBACK TRANSACTION;", transactionalBatch, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Active_slot_patch_migrates_computed_or_missing_columns_to_nullable_tinyint()
    {
        var patch = ReadPatch("20260723_add_qlhv_auto_sync.sql");
        var batches = SplitSqlBatches(patch);

        Assert.DoesNotMatch(@"(?is)\bActiveSlot\s+AS\s*\(", patch);
        Assert.Contains("ActiveSlot tinyint NULL", patch, StringComparison.Ordinal);
        Assert.Contains("'IsComputed') = 1", patch, StringComparison.Ordinal);
        Assert.Contains("DROP COLUMN ActiveSlot;", patch, StringComparison.Ordinal);
        Assert.Contains(
            "COL_LENGTH(N'dbo.App_QlhvAutoSyncRun', N'ActiveSlot') IS NULL",
            patch,
            StringComparison.Ordinal);
        Assert.Contains("activeSlotColumn.is_computed = 0", patch, StringComparison.Ordinal);
        Assert.Contains("activeSlotColumn.is_nullable = 1", patch, StringComparison.Ordinal);
        Assert.Contains("activeSlotType.name = N'tinyint'", patch, StringComparison.Ordinal);
        Assert.Contains("activeStatistic.user_created = 1", patch, StringComparison.Ordinal);
        Assert.Contains("sys.fulltext_index_columns", patch, StringComparison.Ordinal);
        Assert.Contains("activeExpression.is_schema_bound_reference = 1", patch, StringComparison.Ordinal);
        Assert.Contains("is_unique_constraint = 1", patch, StringComparison.Ordinal);

        var dropBatch = FindSingleBatch(batches, @"DROP\s+COLUMN\s+ActiveSlot");
        var addBatch = FindSingleBatch(
            batches,
            @"\bADD\s+ActiveSlot\s+tinyint\s+NULL\b");
        var verifyBatch = FindSingleBatch(
            batches,
            @"THROW\s+527337,\s*'dbo\.App_QlhvAutoSyncRun\.ActiveSlot must be a nullable tinyint column\.'");
        var backfillBatch = FindSingleBatch(
            batches,
            @"SET\s+ActiveSlot\s*=\s*CASE");

        Assert.True(dropBatch < addBatch);
        Assert.True(addBatch < verifyBatch);
        Assert.True(verifyBatch < backfillBatch);
        AssertColumnAddBatchHasNoCompiledReferences(batches[addBatch], "ActiveSlot");

        var migrationBatch = batches[dropBatch];
        var earlyDuplicateGuard = migrationBatch.IndexOf("THROW 527338", StringComparison.Ordinal);
        var computedColumnDrop = migrationBatch.IndexOf(
            "DROP COLUMN ActiveSlot;",
            StringComparison.Ordinal);
        Assert.True(earlyDuplicateGuard >= 0);
        Assert.True(earlyDuplicateGuard < computedColumnDrop);
    }

    [Fact]
    public void Active_slot_constraint_and_unique_index_enforce_one_global_active_run()
    {
        var patch = ReadPatch("20260723_add_qlhv_auto_sync.sql");

        Assert.Contains(
            "CHECK (ActiveSlot IS NULL OR ActiveSlot = 1)",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_ActiveSlot",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE UNIQUE NONCLUSTERED INDEX UX_App_QlhvAutoSyncRun_ActiveSlot",
            patch,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON dbo.App_QlhvAutoSyncRun (ActiveSlot)",
            patch,
            StringComparison.Ordinal);
        Assert.Contains("WHERE ActiveSlot = 1", patch, StringComparison.Ordinal);
        Assert.Contains("activeIndex.type <> 2", patch, StringComparison.Ordinal);
        Assert.Contains("activeIndex.is_hypothetical = 1", patch, StringComparison.Ordinal);
        Assert.Contains("includedColumn.is_included_column = 1", patch, StringComparison.Ordinal);
        Assert.Contains("activeIndex.filter_definition IS NULL", patch, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            @"(?is)CREATE\s+UNIQUE\s+NONCLUSTERED\s+INDEX\s+UX_App_QlhvAutoSyncRun_ActiveSlot[^;]*?\bINCLUDE\b",
            patch);
        Assert.DoesNotMatch(
            @"(?is)CREATE\s+UNIQUE(?:\s+NONCLUSTERED)?\s+INDEX\b[^;]*?\bON\s+dbo\.App_QlhvAutoSyncRun\s*\(\s*Status\s*\)",
            patch);

        Assert.Matches(
            @"(?is)COUNT_BIG\(\*\).*?Status\s+IN\s*\(N'QUEUED',\s*N'RUNNING'\).*?>\s*1.*?THROW\s+527338",
            patch);
        Assert.Matches(
            @"(?is)SET\s+ActiveSlot\s*=\s*CASE\s+WHEN\s+Status\s+IN\s*\(N'QUEUED',\s*N'RUNNING'\)\s+THEN\s+CONVERT\(tinyint,\s*1\)\s+ELSE\s+NULL\s+END",
            patch);
        Assert.Matches(
            @"(?is)COUNT_BIG\(\*\).*?WHERE\s+ActiveSlot\s*=\s*1.*?>\s*1.*?THROW\s+527339",
            patch);
    }

    [Fact]
    public void Auto_sync_repository_sets_and_releases_the_durable_active_slot_atomically()
    {
        var repository = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "QlhvAutoSyncRunRepository.cs"));

        Assert.Contains("WHERE ActiveSlot = 1", repository, StringComparison.Ordinal);
        Assert.Matches(
            @"(?is)INSERT\s+INTO\s+dbo\.App_QlhvAutoSyncRun.*?CurrentSourceType,\s*CurrentStage,\s*ActiveSlot.*?NULL,\s*N'CONNECTING',\s*1,",
            repository);
        Assert.Matches(
            @"(?is)SET\s+Status\s*=\s*N'RUNNING',\s*ActiveSlot\s*=\s*1",
            repository);
        Assert.Matches(
            @"(?is)SET\s+Status\s*=\s*@Status,\s*ActiveSlot\s*=\s*NULL",
            repository);
        Assert.Matches(
            @"(?is)SET\s+Status\s*=\s*N'QUEUED',\s*ActiveSlot\s*=\s*1",
            repository);
        Assert.Contains(
            "AND Status IN (N'QUEUED', N'RUNNING')",
            repository,
            StringComparison.Ordinal);
        Assert.Contains(
            "AND ActiveSlot = 1;",
            repository,
            StringComparison.Ordinal);
        Assert.Contains(
            "QlhvAutoSyncConstants.PartialFailed",
            repository,
            StringComparison.Ordinal);
        Assert.Contains(
            "Auto Sync chi duoc hoan tat bang trang thai terminal.",
            repository,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SET ActiveSlot = NULL WHERE",
            repository,
            StringComparison.OrdinalIgnoreCase);
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

    private static string[] SplitSqlBatches(string patch)
        => Regex.Split(
                patch,
                @"^\s*GO\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static int FindSingleBatch(string[] batches, string pattern)
    {
        var matches = batches
            .Select((batch, index) => new { batch, index })
            .Where(item => Regex.IsMatch(
                item.batch,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            .Select(item => item.index)
            .ToArray();

        return Assert.Single(matches);
    }

    private static int FindFirstBatch(string[] batches, string pattern)
    {
        var index = Array.FindIndex(
            batches,
            batch => Regex.IsMatch(
                batch,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline));

        Assert.True(index >= 0, $"No SQL batch matched pattern: {pattern}");
        return index;
    }

    private static void AssertColumnAddBatchHasNoCompiledReferences(
        string batch,
        string columnName)
    {
        Assert.DoesNotMatch(@"(?i)\bSELECT\b", batch);
        Assert.DoesNotMatch(@"(?i)\bUPDATE\b", batch);
        Assert.DoesNotMatch(@"(?i)\bINSERT\b", batch);
        Assert.DoesNotMatch(@"(?i)\bMERGE\b", batch);
        Assert.DoesNotMatch(@"(?i)\bCHECK\s*\(", batch);
        Assert.DoesNotMatch(
            $@"(?i)\bCREATE\s+(?:INDEX|VIEW|PROCEDURE|FUNCTION)\b[\s\S]*?\b{Regex.Escape(columnName)}\b",
            batch);
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
