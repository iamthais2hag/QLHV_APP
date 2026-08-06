SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeWorkerState', N'U') IS NULL
        THROW 527800, 'RT03_WORKER_STATE_MISSING', 1;

    IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState', N'CurrentCycleStartedAtUtc') IS NULL
        ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState
            ADD CurrentCycleStartedAtUtc datetime2(7) NULL;

    IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState', N'LastCycleFailedAtUtc') IS NULL
        ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState
            ADD LastCycleFailedAtUtc datetime2(7) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
