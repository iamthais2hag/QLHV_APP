using System.Runtime.CompilerServices;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvCourseTeacherFullSnapshotSyncSqlTests
{
    [Fact]
    public void Import_fallback_snapshot_token_changes_with_teacher_or_relation_row_counts()
    {
        var rows = new QlhvOperationRowCountsDto { NguoiLX = 1, NguoiLXHoSo = 1, KhoaHoc = 1 };
        var createdAt = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc);

        var baseline = QlhvBackupSnapshotToken.CreateImportMetadataFallback(
            "CSDL_OTO_BAK", createdAt, rows, 0, 0);
        var teacherChanged = QlhvBackupSnapshotToken.CreateImportMetadataFallback(
            "CSDL_OTO_BAK", createdAt, rows, 1, 0);
        var relationChanged = QlhvBackupSnapshotToken.CreateImportMetadataFallback(
            "CSDL_OTO_BAK", createdAt, rows, 0, 1);

        Assert.NotEqual(baseline, teacherChanged);
        Assert.NotEqual(baseline, relationChanged);
    }

    [Fact]
    public void Staging_and_merges_use_source_partition_identities_and_preserve_relation_links()
    {
        var create = QlhvCourseTeacherFullSnapshotSyncSql.CreateStagingTables;

        Assert.Contains("PRIMARY KEY (SourceProfileCode, SourceMaKhoaHoc)", create, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (SourceProfileCode, SourceMaGV)", create, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (SourceProfileCode, SourceMaLichLV)", create, StringComparison.Ordinal);
        Assert.Contains("SourceMaKhoaHoc NVARCHAR(50) NOT NULL", create, StringComparison.Ordinal);
        Assert.Contains("IsKhoaHocGiaoVien BIT NOT NULL", create, StringComparison.Ordinal);
        Assert.Contains("HangGPLX NVARCHAR(100) NULL", create, StringComparison.Ordinal);

        Assert.Contains(
            "target.SourceMaKhoaHoc = source.SourceMaKhoaHoc",
            QlhvCourseTeacherFullSnapshotSyncSql.MergeKhoaHoc,
            StringComparison.Ordinal);
        Assert.Contains(
            "target.SourceMaGV = source.SourceMaGV",
            QlhvCourseTeacherFullSnapshotSyncSql.MergeGiaoVien,
            StringComparison.Ordinal);
        Assert.Contains(
            "target.SourceMaLichLV = source.SourceMaLichLV",
            QlhvCourseTeacherFullSnapshotSyncSql.MergeRelation,
            StringComparison.Ordinal);
        Assert.Contains("target.IsKhoaHocGiaoVien", QlhvCourseTeacherFullSnapshotSyncSql.MergeRelation, StringComparison.Ordinal);
    }

    [Fact]
    public void Per_domain_guards_preserve_empty_duplicate_relation_and_natural_key_protection()
    {
        var course = QlhvCourseTeacherFullSnapshotSyncSql.KhoaHocAtomicGuard;
        var teacher = QlhvCourseTeacherFullSnapshotSyncSql.GiaoVienAtomicGuard;
        var relation = QlhvCourseTeacherFullSnapshotSyncSql.RelationAtomicGuard;

        foreach (var guard in new[] { course, teacher, relation })
        {
            Assert.Contains("InvalidSourceProfileRows", guard, StringComparison.Ordinal);
            Assert.Contains("InvalidTargetIdentityRows", guard, StringComparison.Ordinal);
            Assert.Contains("DuplicateTargetIdentityRows", guard, StringComparison.Ordinal);
            Assert.Contains("EmptyPartitionRiskGroups", guard, StringComparison.Ordinal);
            Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", guard, StringComparison.Ordinal);
        }

        Assert.Contains("NaturalKeyConflicts", course, StringComparison.Ordinal);
        Assert.Contains("NaturalKeyConflicts", teacher, StringComparison.Ordinal);
        Assert.Contains("RelationConflicts", relation, StringComparison.Ordinal);
        Assert.Contains("dbo.App_KhoaHoc AS course", relation, StringComparison.Ordinal);
        Assert.Contains("dbo.App_GiaoVien AS teacher", relation, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.App_HocVien", course, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.App_HocVien", teacher, StringComparison.Ordinal);
        Assert.DoesNotContain("dbo.App_HocVien", relation, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_soft_delete_is_profile_scoped_and_no_sql_physically_deletes_rows()
    {
        var softDeletes = new[]
        {
            QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteKhoaHoc,
            QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteGiaoVien,
            QlhvCourseTeacherFullSnapshotSyncSql.SoftDeleteRelation,
            QlhvFullSnapshotSyncSql.SoftDeleteMissing,
        };
        foreach (var sql in softDeletes)
        {
            Assert.Contains("target.SourceProfileCode = @SourceProfileCode", sql, StringComparison.Ordinal);
            Assert.Contains("IsDeleted", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("THEN DELETE", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Patch_is_idempotent_transactional_schema_only_and_trusts_constraints()
    {
        var patch = File.ReadAllText(FindWorkspaceFile(
            "database", "patches", "20260723_add_qlhv_course_teacher_full_sync.sql"));

        Assert.Contains("USE [QLHV_APP];", patch, StringComparison.Ordinal);
        Assert.Contains("SET ANSI_NULLS ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET QUOTED_IDENTIFIER ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET ANSI_PADDING ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET ANSI_WARNINGS ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET ARITHABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET CONCAT_NULL_YIELDS_NULL ON;", patch, StringComparison.Ordinal);
        Assert.Contains("SET NUMERIC_ROUNDABORT OFF;", patch, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON;", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRY", patch, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION;", patch, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH", patch, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", patch, StringComparison.Ordinal);
        Assert.Contains("WITH CHECK CHECK CONSTRAINT CK_App_KhoaHoc_SourceIdentity", patch, StringComparison.Ordinal);
        Assert.Contains("WITH CHECK CHECK CONSTRAINT CK_App_GiaoVien_SourceIdentity", patch, StringComparison.Ordinal);
        Assert.Contains("WITH CHECK CHECK CONSTRAINT CK_App_KhoaHoc_GiaoVien_SourceIdentity", patch, StringComparison.Ordinal);
        Assert.Contains("UX_App_KhoaHoc_SourceIdentity", patch, StringComparison.Ordinal);
        Assert.Contains("UX_App_GiaoVien_SourceIdentity", patch, StringComparison.Ordinal);
        Assert.Contains("UX_App_KhoaHoc_GiaoVien_SourceIdentity", patch, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN HangGPLX NVARCHAR(100) NULL", patch, StringComparison.Ordinal);
        Assert.Contains("max_length <> -1", patch, StringComparison.Ordinal);
        Assert.Contains("IsKhoaHocGiaoVien", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER TABLE dbo.App_KhoaHoc WITH CHECK CHECK CONSTRAINT", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER TABLE dbo.App_GiaoVien WITH CHECK CHECK CONSTRAINT", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER TABLE dbo.App_KhoaHoc_GiaoVien WITH CHECK CHECK CONSTRAINT", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER INDEX UX_App_KhoaHoc_SourceIdentity ON dbo.App_KhoaHoc REBUILD", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER INDEX UX_App_GiaoVien_SourceIdentity ON dbo.App_GiaoVien REBUILD", patch, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql N'ALTER INDEX UX_App_KhoaHoc_GiaoVien_SourceIdentity ON dbo.App_KhoaHoc_GiaoVien REBUILD", patch, StringComparison.Ordinal);
        Assert.Contains("sourceprofilecodeisnotnullandsourcemakhoahocisnotnull", patch, StringComparison.Ordinal);
        Assert.Contains("sourceprofilecodeisnotnullandsourcemagvisnotnull", patch, StringComparison.Ordinal);
        Assert.Contains("sourceprofilecodeisnotnullandsourcemalichlvisnotnull", patch, StringComparison.Ordinal);
        Assert.Contains("targetColumn.is_computed <> 0", patch, StringComparison.Ordinal);
        Assert.Contains("targetConstraint.is_not_trusted <> 0", patch, StringComparison.Ordinal);
        Assert.Contains("sai dinh nghia hoac khong hoat dong", patch, StringComparison.Ordinal);

        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.App_", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.App_", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Patch_compiles_post_add_constraints_and_filtered_indexes_in_dynamic_batches()
    {
        var patch = File.ReadAllText(FindWorkspaceFile(
                "database", "patches", "20260723_add_qlhv_course_teacher_full_sync.sql"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (var tableName in new[]
                 {
                     "App_KhoaHoc",
                     "App_GiaoVien",
                     "App_KhoaHoc_GiaoVien",
                 })
        {
            Assert.Contains(
                $"EXEC sys.sp_executesql N'\n        ALTER TABLE dbo.{tableName} WITH CHECK",
                patch,
                StringComparison.Ordinal);
            Assert.Contains(
                $"EXEC sys.sp_executesql N'\n            CREATE UNIQUE NONCLUSTERED INDEX UX_{tableName}_SourceIdentity",
                patch,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "\n        ALTER TABLE dbo.App_KhoaHoc WITH CHECK ADD CONSTRAINT",
            patch,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\n        CREATE UNIQUE NONCLUSTERED INDEX UX_App_KhoaHoc_SourceIdentity",
            patch,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_runs_selected_groups_in_order_using_independent_serializable_transactions()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvHocVienTargetRepository.cs"));

        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        var course = source.IndexOf("QlhvImportDomains.KhoaHoc,", StringComparison.Ordinal);
        var teacher = source.IndexOf("QlhvImportDomains.GiaoVien,", StringComparison.Ordinal);
        var relation = source.IndexOf("QlhvImportDomains.Relation,", StringComparison.Ordinal);
        var student = source.IndexOf("FullSyncHocVienDomainCoreAsync(", StringComparison.Ordinal);

        Assert.True(course >= 0 && course < teacher);
        Assert.True(teacher < relation);
        Assert.True(relation < student);
        Assert.Contains("FullSyncEntityDomainCoreAsync(", source, StringComparison.Ordinal);
        Assert.Contains("FullSyncHocVienDomainCoreAsync(", source, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(CancellationToken.None)", source, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, "BeginTransactionAsync(") >= 3,
            "Repository must expose separate transaction scopes instead of one four-domain transaction.");
    }

    [Fact]
    public void Skipped_or_empty_optional_domains_return_before_any_domain_sql_or_soft_delete()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvHocVienTargetRepository.cs"));
        var optionalStart = source.IndexOf(
            "private async Task<DomainTransactionResult> ExecuteOptionalDomainAsync",
            StringComparison.Ordinal);
        var sqlStart = source.IndexOf(
            "private async Task<DomainTransactionResult> FullSyncEntityDomainCoreAsync",
            StringComparison.Ordinal);
        Assert.True(optionalStart >= 0 && sqlStart > optionalStart);

        var optionalMethod = source[optionalStart..sqlStart];
        Assert.Contains("if (!selectedDomains.Contains(domain))", optionalMethod, StringComparison.Ordinal);
        Assert.Contains("if (sourceRows == 0)", optionalMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlConnection", optionalMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("softDeleteSql", optionalMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SoftDelete", optionalMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_versions_are_updated_inside_the_successful_domain_transaction_only()
    {
        var sql = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvDataVersionSql.cs"));
        var repository = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvHocVienTargetRepository.cs"));

        Assert.Contains("IncrementAfterKhoaHocCommit", sql, StringComparison.Ordinal);
        Assert.Contains("IncrementAfterGiaoVienCommit", sql, StringComparison.Ordinal);
        Assert.Contains("IncrementAfterRelationCommit", sql, StringComparison.Ordinal);
        Assert.Contains("IncrementAfterHocVienCommit", sql, StringComparison.Ordinal);
        Assert.Contains("QlhvDataVersionSql.IncrementAfterKhoaHocCommit", repository, StringComparison.Ordinal);
        Assert.Contains("QlhvDataVersionSql.IncrementAfterGiaoVienCommit", repository, StringComparison.Ordinal);
        Assert.Contains("QlhvDataVersionSql.IncrementAfterRelationCommit", repository, StringComparison.Ordinal);
        Assert.Contains("QlhvDataVersionSql.IncrementAfterHocVienCommit", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_sync_records_the_hoc_vien_snapshot_and_preserves_unapplied_optional_counts()
    {
        var sql = QlhvDataVersionSql.UpsertPartitionStateAfterSuccessfulFullSync;
        var repository = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvHocVienTargetRepository.cs"));

        Assert.Contains("AppliedBackupSnapshotToken = @AppliedBackupSnapshotToken", sql, StringComparison.Ordinal);
        Assert.Contains("HocVienRows = @HocVienRows", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CASE WHEN @KhoaHocApplied = 1 THEN @KhoaHocRows ELSE KhoaHocRows END",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CASE WHEN @GiaoVienApplied = 1 THEN @GiaoVienRows ELSE GiaoVienRows END",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CASE WHEN @RelationApplied = 1 THEN @KhoaHocGiaoVienRows ELSE KhoaHocGiaoVienRows END",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("KhoaHocApplied =", repository, StringComparison.Ordinal);
        Assert.Contains("GiaoVienApplied =", repository, StringComparison.Ordinal);
        Assert.Contains("RelationApplied =", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void Independent_full_sync_transactions_are_not_retried_after_an_ambiguous_commit()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvHocVienTargetRepository.cs"));
        var fullSyncStart = source.IndexOf(
            "private async Task<QlhvImportFullSyncWriteResult> FullSyncDomainsAsync",
            StringComparison.Ordinal);
        var upsertStart = source.IndexOf(
            "private async Task<UpsertCounts> UpsertBatchCoreAsync",
            StringComparison.Ordinal);

        Assert.True(fullSyncStart >= 0 && upsertStart > fullSyncStart);
        Assert.DoesNotContain(
            "SyncRetryPolicyFactory.CreateDefault",
            source[fullSyncStart..upsertStart],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_and_write_result_expose_domain_selection_skip_reasons_and_outcomes()
    {
        var payload = new QlhvImportFullSyncPayload(
            Array.Empty<QLHV.Application.Sync.Mapping.QlhvImportKhoaHocWriteModel>(),
            Array.Empty<QLHV.Application.Sync.Mapping.QlhvImportGiaoVienWriteModel>(),
            Array.Empty<QLHV.Application.Sync.Mapping.QlhvImportKhoaHocGiaoVienWriteModel>(),
            Array.Empty<QLHV.Application.Sync.Mapping.QlhvImportHocVienWriteModel>(),
            ExecutableDomains: [QlhvImportDomains.HocVien],
            SkippedDomainReasons: new Dictionary<string, string>
            {
                [QlhvImportDomains.GiaoVien] = "schema not ready",
            });

        Assert.Equal([QlhvImportDomains.HocVien], payload.DomainsToExecute);
        Assert.Equal("schema not ready", payload.DomainSkipReasons[QlhvImportDomains.GiaoVien]);

        var result = new QlhvImportFullSyncWriteResult(0, 0, 0, 0, 0, 0, 0, 0)
        {
            DomainResults =
            [
                new QlhvDomainWriteResult(
                    QlhvImportDomains.HocVien,
                    QlhvImportDomainStatuses.Succeeded,
                    null,
                    QlhvEntityWriteCounts.Empty),
                new QlhvDomainWriteResult(
                    QlhvImportDomains.GiaoVien,
                    QlhvImportDomainStatuses.SkippedNotRequested,
                    "schema not ready",
                    QlhvEntityWriteCounts.Empty),
            ],
        };

        Assert.False(result.RequiredDomainFailed);
        Assert.False(result.HasConflicts);
        Assert.Equal(2, result.DomainResults.Count);
        var notRequested = Assert.Single(
            result.DomainResults,
            item => item.Status == QlhvImportDomainStatuses.SkippedNotRequested);
        Assert.False(notRequested.Requested);
        Assert.False(notRequested.Attempted);
        Assert.False(notRequested.Committed);
        Assert.False(notRequested.ContributesToPartial);

        var requiredFailure = result with
        {
            DomainResults =
            [
                new QlhvDomainWriteResult(
                    QlhvImportDomains.HocVien,
                    QlhvImportDomainStatuses.Failed,
                    "student guard failed",
                    QlhvEntityWriteCounts.Empty),
            ],
        };

        Assert.True(requiredFailure.RequiredDomainFailed);
        Assert.True(requiredFailure.HasConflicts);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += pattern.Length;
        }

        return count;
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
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}
