USE [QLHV_APP];
GO

/* Activate OTO only from the verified canary checkpoint. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName'))<>N'CSDLTTTC'
   OR DB_ID()<>12
   OR NOT EXISTS
      (SELECT 1 FROM sys.database_recovery_status WHERE database_id=DB_ID()
       AND database_guid='9C44B304-8A84-4D0D-9A82-19C7233FF6BB')
    THROW 527594, 'RT03_PRODUCTION_IDENTITY_REJECTED: OTO cutover.', 1;
IF N'$(RT03_AUTOSYNC_POLLING_STATE)'<>N'DISABLED_VERIFIED'
    THROW 527595, 'RT03_AUTOSYNC_POLLING_NOT_DISABLED.', 1;
IF EXISTS (SELECT 1 FROM dbo.App_QlhvAutoSyncRun WHERE Status IN(N'QUEUED',N'RUNNING') OR ActiveSlot=1)
   OR EXISTS (SELECT 1 FROM dbo.App_QlhvSyncOperationHistory WHERE Status IN(N'QUEUED',N'RUNNING'))
    THROW 527596, 'RT03_AUTOSYNC_ACTIVE.', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.App_QlhvDirectRealtimeApplyMarker marker
    INNER JOIN dbo.App_QlhvDirectRealtimeApplyCheckpoint checkpointItem
      ON checkpointItem.CycleId=marker.CycleId
     AND checkpointItem.PlanHash=marker.PlanHash
     AND checkpointItem.MarkerHash=marker.MarkerHash
    WHERE marker.SourceProfileCode=N'CSDT_OTO'
      AND marker.PlanHash=N'$(RT03_CANARY_PLAN_HASH)'
      AND checkpointItem.MappingFingerprint=
          '7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9'
      AND checkpointItem.PublishedAtUtc>=marker.CommittedAtUtc
)
    THROW 527597, 'RT03_OTO_CANARY_PROOF_MISSING.', 1;
GO

BEGIN TRANSACTION;
UPDATE dbo.App_QlhvDirectRealtimeFeatureState WITH(UPDLOCK,HOLDLOCK)
SET EnableProductionRealtime=1, EnableProductionShadow=1,
    EnableProductionWrites=1, EnableProductionCanary=0,
    EnableControlledCutover=1, EnableProductionDeletes=0,
    UpdatedAtUtc=SYSUTCDATETIME(), UpdatedBy=N'RT03_PRODUCTION_REGISTRATION'
WHERE FeatureStateId=1
  AND EnableProductionRealtime=0 AND EnableProductionShadow=0
  AND EnableProductionWrites=0 AND EnableProductionCanary=0
  AND EnableControlledCutover=0 AND EnableProductionDeletes=0;
IF @@ROWCOUNT<>1
BEGIN
    ROLLBACK TRANSACTION;
    THROW 527598, 'RT03_FEATURE_STATE_CONFLICT.', 1;
END;

UPDATE dbo.App_QlhvDirectRealtimeProfileState WITH(UPDLOCK,HOLDLOCK)
SET Enabled=CASE WHEN SourceProfileCode=N'CSDT_OTO' THEN 1 ELSE 0 END,
    ActivatedAtUtc=CASE WHEN SourceProfileCode=N'CSDT_OTO' THEN SYSUTCDATETIME() ELSE NULL END,
    LastStatus=NULL, UpdatedAtUtc=SYSUTCDATETIME()
WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO');
IF @@ROWCOUNT<>2
BEGIN
    ROLLBACK TRANSACTION;
    THROW 527599, 'RT03_PROFILE_STATE_CONFLICT.', 1;
END;
COMMIT TRANSACTION;
GO

SELECT N'RT03_OTO_CONTROLLED_CUTOVER_ACTIVATED' AS Evidence;
GO
