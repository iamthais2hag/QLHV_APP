USE [QLHV_APP];
GO

/* Run only after the production worker process is stopped. No learner rollback. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS
   (SELECT 1 FROM dbo.App_QlhvDirectRealtimeWorkerState
    WHERE WorkerStateId=1 AND CycleActive=1)
    THROW 527610, 'RT03_ACTIVE_CYCLE_BLOCKS_ROLLBACK.', 1;

BEGIN TRANSACTION;
UPDATE dbo.App_QlhvDirectRealtimeProfileState
SET Enabled=0, ActivatedAtUtc=NULL, UpdatedAtUtc=SYSUTCDATETIME()
WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO');

UPDATE dbo.App_QlhvDirectRealtimeFeatureState
SET EnableProductionRealtime=0, EnableProductionShadow=0,
    EnableProductionWrites=0, EnableProductionCanary=0,
    EnableControlledCutover=0, EnableProductionDeletes=0,
    UpdatedAtUtc=SYSUTCDATETIME(), UpdatedBy=N'RT03_PRODUCTION_ROLLBACK'
WHERE FeatureStateId=1;
COMMIT TRANSACTION;
GO

SELECT N'RT03_PRODUCTION_REGISTRATION_ROLLED_BACK_FLAGS_OFF' AS Evidence;
GO
