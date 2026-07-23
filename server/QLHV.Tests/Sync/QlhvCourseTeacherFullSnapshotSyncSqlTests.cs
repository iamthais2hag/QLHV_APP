using System.Runtime.CompilerServices;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;
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
    public void Guards_cover_empty_groups_relations_duplicates_and_cross_partition_natural_keys()
    {
        var guard = QlhvCourseTeacherFullSnapshotSyncSql.AtomicGuard;

        Assert.Contains("InvalidSourceProfileRows", guard, StringComparison.Ordinal);
        Assert.Contains("InvalidTargetIdentityRows", guard, StringComparison.Ordinal);
        Assert.Contains("DuplicateTargetIdentityRows", guard, StringComparison.Ordinal);
        Assert.Contains("RelationConflicts", guard, StringComparison.Ordinal);
        Assert.Contains("EmptyPartitionRiskGroups", guard, StringComparison.Ordinal);
        Assert.Contains("NaturalKeyConflicts", guard, StringComparison.Ordinal);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", guard, StringComparison.Ordinal);
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
        Assert.Contains("IsKhoaHocGiaoVien", patch, StringComparison.Ordinal);
        Assert.Contains("ALTER INDEX UX_App_KhoaHoc_SourceIdentity ON dbo.App_KhoaHoc REBUILD", patch, StringComparison.Ordinal);
        Assert.Contains("ALTER INDEX UX_App_GiaoVien_SourceIdentity ON dbo.App_GiaoVien REBUILD", patch, StringComparison.Ordinal);
        Assert.Contains("ALTER INDEX UX_App_KhoaHoc_GiaoVien_SourceIdentity ON dbo.App_KhoaHoc_GiaoVien REBUILD", patch, StringComparison.Ordinal);
        Assert.Contains("sai dinh nghia hoac khong hoat dong", patch, StringComparison.Ordinal);

        Assert.DoesNotContain("DELETE FROM", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.App_", patch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.App_", patch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repository_runs_all_groups_in_required_order_inside_one_serializable_transaction()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "server", "QLHV.Infrastructure", "Sync", "QlhvHocVienTargetRepository.cs"));

        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        var course = source.IndexOf("MergeKhoaHoc", StringComparison.Ordinal);
        var teacher = source.IndexOf("MergeGiaoVien", StringComparison.Ordinal);
        var relation = source.IndexOf("MergeRelation", StringComparison.Ordinal);
        var student = source.IndexOf("QlhvFullSnapshotSyncSql.Merge", StringComparison.Ordinal);
        var commit = source.IndexOf("CommitAsync", StringComparison.Ordinal);

        Assert.True(course >= 0 && course < teacher);
        Assert.True(teacher < relation);
        Assert.True(relation < student);
        Assert.True(student < commit);
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
