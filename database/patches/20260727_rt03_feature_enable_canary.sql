USE [QLHV_APP];
GO

/* RT-03 Task 2 only. Existing Auto Sync must already be paused by the operator. */
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
    THROW 527567, 'RT03_PRODUCTION_IDENTITY_REJECTED: feature enable.', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.App_QlhvAutoSyncRun WITH (UPDLOCK, HOLDLOCK)
    WHERE Status IN (N'QUEUED', N'RUNNING') OR ActiveSlot = 1
)
OR EXISTS
(
    SELECT 1 FROM dbo.App_QlhvSyncOperationHistory WITH (UPDLOCK, HOLDLOCK)
    WHERE Status IN (N'QUEUED', N'RUNNING')
)
    THROW 527568, 'RT03_AUTOSYNC_ACTIVE.', 1;

IF N'$(RT03_AUTOSYNC_POLLING_STATE)' <> N'DISABLED_VERIFIED'
    THROW 527569, 'RT03_AUTOSYNC_POLLING_NOT_DISABLED.', 1;
GO

BEGIN TRANSACTION;
UPDATE dbo.App_QlhvDirectRealtimeFeatureState WITH (UPDLOCK, HOLDLOCK)
SET EnableProductionRealtime = 1,
    EnableProductionShadow = 1,
    EnableProductionWrites = 1,
    EnableProductionCanary = 1,
    EnableControlledCutover = 0,
    EnableProductionDeletes = 0,
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = N'RT03_TASK2_OPERATOR'
WHERE FeatureStateId = 1
  AND EnableProductionRealtime = 0
  AND EnableProductionShadow = 0
  AND EnableProductionWrites = 0
  AND EnableProductionCanary = 0
  AND EnableControlledCutover = 0
  AND EnableProductionDeletes = 0;

IF @@ROWCOUNT <> 1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 527570, 'RT03_FEATURE_STATE_CONFLICT.', 1;
END;
COMMIT TRANSACTION;
GO

SELECT N'RT03_CANARY_FEATURE_ENABLED' AS Evidence;
GO
