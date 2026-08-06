SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeWorkerState', N'U') IS NULL
    THROW 527801, 'RT03_WORKER_STATE_MISSING', 1;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState', N'CurrentCycleStartedAtUtc') IS NULL
    THROW 527802, 'RT03_CYCLE_STARTED_UTC_MISSING', 1;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState', N'LastCycleFailedAtUtc') IS NULL
    THROW 527803, 'RT03_CYCLE_FAILED_UTC_MISSING', 1;

SELECT
    CONVERT(bit, 1) AS SimpleSqlUtcSchemaReady,
    SYSUTCDATETIME() AS VerifiedAtUtc;
