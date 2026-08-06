USE [QLHV_APP];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF N'$(RT03_AUTOSYNC_POLLING_STATE)'<>N'DISABLED_VERIFIED'
    THROW 527604, 'RT03_AUTOSYNC_POLLING_NOT_DISABLED.', 1;
IF EXISTS (SELECT 1 FROM dbo.App_QlhvAutoSyncRun WHERE Status IN(N'QUEUED',N'RUNNING') OR ActiveSlot=1)
   OR EXISTS (SELECT 1 FROM dbo.App_QlhvSyncOperationHistory WHERE Status IN(N'QUEUED',N'RUNNING'))
    THROW 527605, 'RT03_AUTOSYNC_ACTIVE.', 1;
IF NOT EXISTS
   (SELECT 1 FROM dbo.App_QlhvDirectRealtimeFeatureState
    WHERE FeatureStateId=1 AND EnableProductionRealtime=1
      AND EnableProductionShadow=1 AND EnableProductionWrites=1
      AND EnableProductionCanary=0 AND EnableControlledCutover=1
      AND EnableProductionDeletes=0)
    THROW 527606, 'RT03_CONTROLLED_CUTOVER_STATE_REJECTED.', 1;
IF NOT EXISTS
   (SELECT 1 FROM dbo.App_QlhvDirectRealtimeProfileState
    WHERE SourceProfileCode=N'CSDT_OTO' AND Enabled=1
      AND LastStatus=N'HEALTHY_NO_CHANGE')
    THROW 527607, 'RT03_OTO_HEALTH_PROOF_MISSING.', 1;
IF NOT EXISTS
   (SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
    WHERE SourceProfileCode=N'CSDT_MOTO' AND Mode=N'DIRECT_REALTIME_APPLY'
      AND MappingFingerprint=
          '7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9'
      AND EnvironmentId=N'PRODUCTION'
      AND SourceDatabaseGuid='308BDDA8-80F3-4ACB-9836-578D80A9E98E')
    THROW 527608, 'RT03_MOTO_CHECKPOINT_MISSING.', 1;
GO

UPDATE dbo.App_QlhvDirectRealtimeProfileState WITH(UPDLOCK,HOLDLOCK)
SET Enabled=1, ActivatedAtUtc=SYSUTCDATETIME(), LastStatus=NULL,
    UpdatedAtUtc=SYSUTCDATETIME()
WHERE SourceProfileCode=N'CSDT_MOTO' AND Enabled=0;
IF @@ROWCOUNT<>1 THROW 527609, 'RT03_MOTO_PROFILE_STATE_CONFLICT.', 1;
GO

SELECT N'RT03_MOTO_CONTROLLED_CUTOVER_ACTIVATED' AS Evidence;
GO
