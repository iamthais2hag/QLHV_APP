USE [QLHV_APP];
GO

/* Fail-safe RT-03 feature disable. Does not pause or resume Existing Auto Sync. */
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
    THROW 527571, 'RT03_PRODUCTION_IDENTITY_REJECTED: feature disable.', 1;
GO

BEGIN TRANSACTION;
UPDATE dbo.App_QlhvDirectRealtimeFeatureState WITH (UPDLOCK, HOLDLOCK)
SET EnableProductionRealtime = 0,
    EnableProductionShadow = 0,
    EnableProductionWrites = 0,
    EnableProductionCanary = 0,
    EnableControlledCutover = 0,
    EnableProductionDeletes = 0,
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = N'RT03_TASK2_FAILSAFE'
WHERE FeatureStateId = 1;
COMMIT TRANSACTION;
GO

SELECT N'RT03_ALL_FEATURES_DISABLED' AS Evidence;
GO
