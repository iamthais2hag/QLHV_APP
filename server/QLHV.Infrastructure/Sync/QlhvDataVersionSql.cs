namespace QLHV.Infrastructure.Sync;

public static class QlhvDataVersionSql
{
    // Executed by QlhvHocVienTargetRepository on the same SqlConnection/SqlTransaction
    // as course, teacher, relation and student merges. Visibility therefore changes only
    // when that full-sync transaction commits.
    public const string UpsertPartitionStateAfterSuccessfulFullSync = @"
DECLARE @AppliedAtUtc datetime2(7) = SYSUTCDATETIME();

UPDATE dbo.App_QlhvSyncPartitionState WITH (UPDLOCK, HOLDLOCK)
SET SourceProfileCode = @SourceProfileCode,
    AppliedBackupSnapshotToken = @AppliedBackupSnapshotToken,
    HocVienRows = @HocVienRows,
    KhoaHocRows = @KhoaHocRows,
    GiaoVienRows = @GiaoVienRows,
    KhoaHocGiaoVienRows = @KhoaHocGiaoVienRows,
    AppliedAtUtc = @AppliedAtUtc,
    UpdatedAtUtc = @AppliedAtUtc
WHERE SourceType = @SourceType;

IF @@ROWCOUNT = 0
BEGIN
    INSERT dbo.App_QlhvSyncPartitionState
    (
        SourceType,
        SourceProfileCode,
        AppliedBackupSnapshotToken,
        HocVienRows,
        KhoaHocRows,
        GiaoVienRows,
        KhoaHocGiaoVienRows,
        AppliedAtUtc,
        UpdatedAtUtc
    )
    VALUES
    (
        @SourceType,
        @SourceProfileCode,
        @AppliedBackupSnapshotToken,
        @HocVienRows,
        @KhoaHocRows,
        @GiaoVienRows,
        @KhoaHocGiaoVienRows,
        @AppliedAtUtc,
        @AppliedAtUtc
    );
END;";

    public const string IncrementAfterSuccessfulFullSync = @"
UPDATE dbo.App_DataVersion WITH (UPDLOCK)
SET HocVienVersion = HocVienVersion + 1,
    KhoaHocVersion = KhoaHocVersion + 1,
    GiaoVienVersion = GiaoVienVersion + 1,
    LastSuccessfulSyncUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE VersionId = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 527310, 'dbo.App_DataVersion singleton row is missing.', 1;
END;";
}
