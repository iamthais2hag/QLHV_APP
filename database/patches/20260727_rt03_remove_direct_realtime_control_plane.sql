USE [QLHV_APP];
GO

/* RT-03 control-plane schema rollback. Task 2 only; requires zero durable state. */
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
    THROW 527562, 'RT03_PRODUCTION_IDENTITY_REJECTED: QLHV_APP rollback.', 1;

IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint)
    THROW 527563, 'RT03_CONTROL_PLANE_ROLLBACK_BLOCKED: checkpoint exists.', 1;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyMarker)
    THROW 527564, 'RT03_CONTROL_PLANE_ROLLBACK_BLOCKED: marker exists.', 1;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.App_QlhvDirectRealtimeManualReview)
    THROW 527565, 'RT03_CONTROL_PLANE_ROLLBACK_BLOCKED: manual review exists.', 1;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeFeatureState', N'U') IS NOT NULL
   AND EXISTS
      (
          SELECT 1 FROM dbo.App_QlhvDirectRealtimeFeatureState
          WHERE EnableProductionRealtime <> 0
             OR EnableProductionShadow <> 0
             OR EnableProductionWrites <> 0
             OR EnableProductionCanary <> 0
             OR EnableControlledCutover <> 0
             OR EnableProductionDeletes <> 0
      )
    THROW 527566, 'RT03_CONTROL_PLANE_ROLLBACK_BLOCKED: feature enabled.', 1;
GO

BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview', N'U') IS NOT NULL
    DROP TABLE dbo.App_QlhvDirectRealtimeManualReview;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint', N'U') IS NOT NULL
    DROP TABLE dbo.App_QlhvDirectRealtimeApplyCheckpoint;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker', N'U') IS NOT NULL
    DROP TABLE dbo.App_QlhvDirectRealtimeApplyMarker;
IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeFeatureState', N'U') IS NOT NULL
    DROP TABLE dbo.App_QlhvDirectRealtimeFeatureState;
COMMIT TRANSACTION;
GO

SELECT N'RT03_CONTROL_PLANE_REMOVED' AS Evidence;
GO
