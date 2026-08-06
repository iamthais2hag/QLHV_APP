USE [QLHV_APP];
GO

/* RT-03 Task 2 post-canary proof. SELECT-only and aggregate/privacy-safe. */
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
   OR DB_ID() <> 12
   OR NOT EXISTS
      (
          SELECT 1 FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
      )
    THROW 527580, 'RT03_PRODUCTION_IDENTITY_REJECTED: post-canary proof.', 1;

IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeFeatureState', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview', N'U') IS NULL
    THROW 527581, 'RT03_CONTROL_PLANE_MISSING: post-canary proof.', 1;

SELECT
    N'RT03_POST_CANARY_FEATURE_STATE' AS Evidence,
    EnableProductionRealtime,
    EnableProductionShadow,
    EnableProductionWrites,
    EnableProductionCanary,
    EnableControlledCutover,
    EnableProductionDeletes,
    CONVERT(varchar(33), UpdatedAtUtc, 126) AS UpdatedAtUtc,
    UpdatedBy
FROM dbo.App_QlhvDirectRealtimeFeatureState
WHERE FeatureStateId = 1;

SELECT
    N'RT03_POST_CANARY_AUTOSYNC_EXCLUSION' AS Evidence,
    (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun
        WHERE Status IN (N'QUEUED', N'RUNNING')) AS ActiveAutoSyncRows,
    (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun
        WHERE ActiveSlot = 1) AS ActiveAutoSyncSlotRows,
    (SELECT COUNT_BIG(*) FROM dbo.App_QlhvSyncOperationHistory
        WHERE Status IN (N'QUEUED', N'RUNNING')) AS ActiveOperationRows;

SELECT
    N'RT03_POST_CANARY_MARKER_CHECKPOINT' AS Evidence,
    marker.SourceProfileCode,
    COUNT_BIG(*) AS MarkerRows,
    SUM(CASE WHEN checkpointItem.CycleId IS NULL THEN CONVERT(bigint, 0)
             ELSE CONVERT(bigint, 1) END) AS MatchingCheckpointRows,
    MIN(marker.InsertedRows) AS MinimumInsertedRows,
    MAX(marker.InsertedRows) AS MaximumInsertedRows,
    MIN(marker.UpdatedRows) AS MinimumUpdatedRows,
    MAX(marker.UpdatedRows) AS MaximumUpdatedRows,
    MIN(marker.RetainedRows) AS MinimumRetainedRows,
    MAX(marker.RetainedRows) AS MaximumRetainedRows,
    MIN(CASE WHEN checkpointItem.PublishedAtUtc >= marker.CommittedAtUtc THEN 1 ELSE 0 END)
        AS CheckpointAfterCommit
FROM dbo.App_QlhvDirectRealtimeApplyMarker AS marker
LEFT JOIN dbo.App_QlhvDirectRealtimeApplyCheckpoint AS checkpointItem
    ON checkpointItem.CycleId = marker.CycleId
   AND checkpointItem.PlanHash = marker.PlanHash
   AND checkpointItem.MarkerHash = marker.MarkerHash
   AND checkpointItem.SourceChangeTrackingVersion = marker.SourceChangeTrackingVersion
WHERE marker.PlanHash = N'$(RT03_PLAN_HASH)'
GROUP BY marker.SourceProfileCode
ORDER BY marker.SourceProfileCode;

SELECT
    N'RT03_POST_CANARY_MANUAL_REVIEW' AS Evidence,
    SourceProfileCode,
    COUNT_BIG(*) AS ManualReviewRows,
    MIN(CONVERT(int, TargetRetainedActive)) AS AllTargetsRetainedActive,
    MAX(CONVERT(int, TargetMutated)) AS AnyTargetMutated
FROM dbo.App_QlhvDirectRealtimeManualReview
WHERE PlanHash = N'$(RT03_PLAN_HASH)'
GROUP BY SourceProfileCode
ORDER BY SourceProfileCode;

SELECT
    N'RT03_POST_CANARY_PROFILE_COUNTS' AS Evidence,
    SourceProfileCode,
    SUM(CASE WHEN IsDeleted = 0 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS ActiveRows,
    SUM(CASE WHEN IsDeleted = 1 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS SoftDeletedRows,
    COUNT_BIG(*) AS TotalRows
FROM dbo.App_HocVien
WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
GROUP BY SourceProfileCode
ORDER BY SourceProfileCode;

SELECT
    N'RT03_POST_CANARY_DUPLICATE_ACTIVE' AS Evidence,
    profile.SourceProfileCode,
    COUNT_BIG(duplicateGroup.SourceIdentity) AS DuplicateActiveIdentityGroups
FROM (VALUES (N'CSDT_OTO'), (N'CSDT_MOTO')) AS profile(SourceProfileCode)
LEFT JOIN
(
    SELECT SourceProfileCode, LTRIM(RTRIM(SourceMaDK)) AS SourceIdentity
    FROM dbo.App_HocVien
    WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
      AND IsDeleted = 0
    GROUP BY SourceProfileCode, LTRIM(RTRIM(SourceMaDK))
    HAVING COUNT_BIG(*) > 1
) AS duplicateGroup
    ON duplicateGroup.SourceProfileCode = profile.SourceProfileCode
GROUP BY profile.SourceProfileCode
ORDER BY profile.SourceProfileCode;

WITH qlhvOwned AS
(
    SELECT
        SourceProfileCode,
        SourceMaDK,
        HocVienId,
        CONCAT(
            COALESCE(CONVERT(nvarchar(20), HocVienId), N'<NULL>'), N'|', IsDeleted, N'|',
            COALESCE(GhiChuNoiBo, N'<NULL>'), N'|', DaDoiChieuCCCD, N'|',
            DaInThe, N'|', DaTaoXML, N'|', COALESCE(CreatedBy, N'<NULL>'), N'|',
            COALESCE(UpdatedBy, N'<NULL>'), N'|', COALESCE(DeletedBy, N'<NULL>'), N'|',
            COALESCE(DeleteReason, N'<NULL>')) AS Canonical
    FROM dbo.App_HocVien
    WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
)
SELECT
    N'RT03_POST_CANARY_QLHV_OWNED_FINGERPRINT' AS Evidence,
    profile.SourceProfileCode,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(
        (SELECT STRING_AGG(CONVERT(nvarchar(max), Canonical), NCHAR(10))
         WITHIN GROUP (ORDER BY SourceMaDK, HocVienId)
         FROM qlhvOwned AS rowItem
         WHERE rowItem.SourceProfileCode = profile.SourceProfileCode), N'')), 2)
        AS QlhvOwnedFingerprint
FROM (VALUES (N'CSDT_OTO'), (N'CSDT_MOTO')) AS profile(SourceProfileCode)
ORDER BY profile.SourceProfileCode;

SELECT
    N'RT03_POST_CANARY_SOURCE_CAPABILITY' AS Evidence,
    databaseItem.name AS DatabaseName,
    CASE WHEN tracking.database_id IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END
        AS ChangeTrackingEnabled,
    databaseItem.snapshot_isolation_state_desc AS SnapshotIsolationState,
    databaseItem.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn
FROM sys.databases AS databaseItem
LEFT JOIN sys.change_tracking_databases AS tracking
    ON tracking.database_id = databaseItem.database_id
WHERE databaseItem.name IN (N'CSDL_OTO', N'CSDL_MOTO')
ORDER BY databaseItem.name;
GO
