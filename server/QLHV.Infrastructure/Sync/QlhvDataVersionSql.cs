namespace QLHV.Infrastructure.Sync;

public static class QlhvDataVersionSql
{
    // Executed by QlhvHocVienTargetRepository in the successful HocVien transaction.
    // Applied flags preserve the last committed count for optional domains that were
    // skipped or failed, while still recording the HocVien snapshot token/count.
    public const string UpsertPartitionStateAfterSuccessfulFullSync = @"
DECLARE @AppliedAtUtc datetime2(7) = SYSUTCDATETIME();

UPDATE dbo.App_QlhvSyncPartitionState WITH (UPDLOCK, HOLDLOCK)
SET SourceProfileCode = @SourceProfileCode,
    AppliedBackupSnapshotToken = @AppliedBackupSnapshotToken,
    HocVienRows = @HocVienRows,
    KhoaHocRows = CASE WHEN @KhoaHocApplied = 1 THEN @KhoaHocRows ELSE KhoaHocRows END,
    GiaoVienRows = CASE WHEN @GiaoVienApplied = 1 THEN @GiaoVienRows ELSE GiaoVienRows END,
    KhoaHocGiaoVienRows =
        CASE WHEN @RelationApplied = 1 THEN @KhoaHocGiaoVienRows ELSE KhoaHocGiaoVienRows END,
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
        CASE WHEN @KhoaHocApplied = 1 THEN @KhoaHocRows ELSE 0 END,
        CASE WHEN @GiaoVienApplied = 1 THEN @GiaoVienRows ELSE 0 END,
        CASE WHEN @RelationApplied = 1 THEN @KhoaHocGiaoVienRows ELSE 0 END,
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

    public const string IncrementAfterKhoaHocCommit = @"
UPDATE dbo.App_DataVersion WITH (UPDLOCK)
SET KhoaHocVersion = KhoaHocVersion + 1,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE VersionId = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 527310, 'dbo.App_DataVersion singleton row is missing.', 1;
END;";

    public const string IncrementAfterGiaoVienCommit = @"
UPDATE dbo.App_DataVersion WITH (UPDLOCK)
SET GiaoVienVersion = GiaoVienVersion + 1,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE VersionId = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 527310, 'dbo.App_DataVersion singleton row is missing.', 1;
END;";

    public const string IncrementAfterRelationCommit = @"
UPDATE dbo.App_DataVersion WITH (UPDLOCK)
SET KhoaHocVersion = KhoaHocVersion + 1,
    GiaoVienVersion = GiaoVienVersion + 1,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE VersionId = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 527310, 'dbo.App_DataVersion singleton row is missing.', 1;
END;";

    public const string IncrementAfterHocVienCommit = @"
UPDATE dbo.App_DataVersion WITH (UPDLOCK)
SET HocVienVersion = HocVienVersion + 1,
    LastSuccessfulSyncUtc = SYSUTCDATETIME(),
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE VersionId = 1;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 527310, 'dbo.App_DataVersion singleton row is missing.', 1;
END;";
}
