USE [QLHV_APP];
GO

/* RT-03 Task 2 only. OTO must be PASS; Existing Auto Sync remains paused. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
   OR DB_ID() <> 12
   OR NOT EXISTS
      (
          SELECT 1 FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
      )
    THROW 527572, 'RT03_PRODUCTION_IDENTITY_REJECTED: controlled cutover.', 1;

IF N'$(RT03_OTO_CANARY_RESULT)' <> N'PASSED'
    THROW 527573, 'RT03_OTO_MUST_PASS_FIRST.', 1;
IF N'$(RT03_AUTOSYNC_POLLING_STATE)' <> N'DISABLED_VERIFIED'
    THROW 527574, 'RT03_AUTOSYNC_POLLING_NOT_DISABLED.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.App_QlhvAutoSyncRun
    WHERE Status IN (N'QUEUED', N'RUNNING') OR ActiveSlot = 1
)
OR EXISTS
(
    SELECT 1 FROM dbo.App_QlhvSyncOperationHistory
    WHERE Status IN (N'QUEUED', N'RUNNING')
)
    THROW 527575, 'RT03_AUTOSYNC_ACTIVE.', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.App_QlhvDirectRealtimeApplyMarker AS marker
    INNER JOIN dbo.App_QlhvDirectRealtimeApplyCheckpoint AS checkpoint
        ON checkpoint.CycleId = marker.CycleId
       AND checkpoint.PlanHash = marker.PlanHash
       AND checkpoint.MarkerHash = marker.MarkerHash
    WHERE marker.SourceProfileCode = N'CSDT_OTO'
      AND marker.PlanHash = N'$(RT03_PLAN_HASH)'
      AND checkpoint.PublishedAtUtc >= marker.CommittedAtUtc
)
    THROW 527576, 'RT03_OTO_CANARY_PROOF_MISSING.', 1;
GO

BEGIN TRANSACTION;
UPDATE dbo.App_QlhvDirectRealtimeFeatureState WITH (UPDLOCK, HOLDLOCK)
SET EnableProductionRealtime = 1,
    EnableProductionShadow = 1,
    EnableProductionWrites = 1,
    EnableProductionCanary = 0,
    EnableControlledCutover = 1,
    EnableProductionDeletes = 0,
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = N'RT03_TASK2_OPERATOR'
WHERE FeatureStateId = 1
  AND EnableProductionRealtime = 1
  AND EnableProductionShadow = 1
  AND EnableProductionWrites = 1
  AND EnableProductionCanary = 1
  AND EnableControlledCutover = 0
  AND EnableProductionDeletes = 0;

IF @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 527577, 'RT03_FEATURE_STATE_CONFLICT.', 1;
END;
COMMIT TRANSACTION;
GO

SELECT N'RT03_CONTROLLED_CUTOVER_ENABLED' AS Evidence;
GO
