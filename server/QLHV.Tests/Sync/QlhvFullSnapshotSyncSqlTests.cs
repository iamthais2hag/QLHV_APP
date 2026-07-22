using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvFullSnapshotSyncSqlTests
{
    [Fact]
    public void Staging_and_merge_use_composite_source_identity()
    {
        Assert.Contains(
            "PRIMARY KEY (SourceProfileCode, SourceMaDK)",
            QlhvFullSnapshotSyncSql.CreateStagingTable,
            StringComparison.Ordinal);
        Assert.Contains(
            "ON target.SourceProfileCode = source.SourceProfileCode",
            QlhvFullSnapshotSyncSql.Merge,
            StringComparison.Ordinal);
        Assert.Contains(
            "AND target.SourceMaDK = source.SourceMaDK",
            QlhvFullSnapshotSyncSql.Merge,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ON target.MaDK = source.MaDK",
            QlhvFullSnapshotSyncSql.Merge,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_updates_photo_fields_and_reactivates_deleted_rows()
    {
        Assert.Contains("target.AnhRelativePath", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("target.ChatLuongAnh", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("target.NgayThuNhanAnh", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("target.NguoiThuNhanAnh", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("target.IsDeleted = 1", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("target.IsDeleted        = 0", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("target.DeletedAt        = NULL", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
        Assert.Contains("N'REACTIVATE'", QlhvFullSnapshotSyncSql.Merge, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_rows_are_soft_deleted_only_inside_requested_profile()
    {
        Assert.Contains(
            "WHERE target.SourceProfileCode = @SourceProfileCode",
            QlhvFullSnapshotSyncSql.SoftDeleteMissing,
            StringComparison.Ordinal);
        Assert.Contains(
            "source.SourceProfileCode = target.SourceProfileCode",
            QlhvFullSnapshotSyncSql.SoftDeleteMissing,
            StringComparison.Ordinal);
        Assert.Contains(
            "source.SourceMaDK = target.SourceMaDK",
            QlhvFullSnapshotSyncSql.SoftDeleteMissing,
            StringComparison.Ordinal);
        Assert.Contains("target.IsDeleted = 0", QlhvFullSnapshotSyncSql.SoftDeleteMissing, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_snapshot_sql_contains_no_physical_delete()
    {
        var allSql = string.Join(
            Environment.NewLine,
            QlhvFullSnapshotSyncSql.AtomicGuard,
            QlhvFullSnapshotSyncSql.Merge,
            QlhvFullSnapshotSyncSql.SoftDeleteMissing);

        Assert.DoesNotContain("DELETE FROM", allSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("THEN DELETE", allSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transaction_guard_rechecks_partition_and_target_identity_integrity()
    {
        Assert.Contains("InvalidSourceProfileRows", QlhvFullSnapshotSyncSql.AtomicGuard, StringComparison.Ordinal);
        Assert.Contains("InvalidTargetIdentityRows", QlhvFullSnapshotSyncSql.AtomicGuard, StringComparison.Ordinal);
        Assert.Contains("DuplicateTargetIdentityRows", QlhvFullSnapshotSyncSql.AtomicGuard, StringComparison.Ordinal);
        Assert.Contains(
            "target.SourceProfileCode = @SourceProfileCode",
            QlhvFullSnapshotSyncSql.AtomicGuard,
            StringComparison.Ordinal);
    }
}
