USE [QLHV_APP];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NULL
    BEGIN
        THROW 527410, 'dbo.App_QlhvSyncOperationHistory is required.', 1;
    END;

    IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL
    BEGIN
        THROW 527411, 'dbo.App_QlhvAutoSyncRun is required.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_QlhvSyncOperationHistory WITH (UPDLOCK, HOLDLOCK)
        WHERE Status NOT IN
        (
            N'QUEUED',
            N'RUNNING',
            N'SUCCEEDED',
            N'PARTIAL_SUCCESS',
            N'FAILED'
        )
    )
    BEGIN
        THROW 527412, 'Operation history contains an unsupported status.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_QlhvAutoSyncRun WITH (UPDLOCK, HOLDLOCK)
        WHERE Status NOT IN
        (
            N'QUEUED',
            N'RUNNING',
            N'SUCCEEDED',
            N'PARTIAL_SUCCESS',
            N'PARTIAL_FAILED',
            N'FAILED'
        )
    )
    BEGIN
        THROW 527413, 'Auto Sync history contains an unsupported status.', 1;
    END;

    IF OBJECT_ID(
        N'dbo.CK_App_QlhvSyncOperationHistory_StatusTimestamps',
        N'C') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncOperationHistory
            DROP CONSTRAINT CK_App_QlhvSyncOperationHistory_StatusTimestamps;
    END;

    IF OBJECT_ID(
        N'dbo.CK_App_QlhvSyncOperationHistory_Status',
        N'C') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncOperationHistory
            DROP CONSTRAINT CK_App_QlhvSyncOperationHistory_Status;
    END;

    ALTER TABLE dbo.App_QlhvSyncOperationHistory WITH CHECK
        ADD CONSTRAINT CK_App_QlhvSyncOperationHistory_Status
        CHECK
        (
            Status IN
            (
                N'QUEUED',
                N'RUNNING',
                N'SUCCEEDED',
                N'PARTIAL_SUCCESS',
                N'FAILED'
            )
        );

    ALTER TABLE dbo.App_QlhvSyncOperationHistory WITH CHECK
        ADD CONSTRAINT CK_App_QlhvSyncOperationHistory_StatusTimestamps
        CHECK
        (
            (Status = N'QUEUED'
                AND StartedAtUtc IS NULL
                AND CompletedAtUtc IS NULL)
            OR
            (Status = N'RUNNING'
                AND StartedAtUtc IS NOT NULL
                AND CompletedAtUtc IS NULL)
            OR
            (Status IN (N'SUCCEEDED', N'PARTIAL_SUCCESS', N'FAILED')
                AND CompletedAtUtc IS NOT NULL)
        );

    IF OBJECT_ID(
        N'dbo.CK_App_QlhvAutoSyncRun_StatusTimestamps',
        N'C') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            DROP CONSTRAINT CK_App_QlhvAutoSyncRun_StatusTimestamps;
    END;

    IF OBJECT_ID(
        N'dbo.CK_App_QlhvAutoSyncRun_Status',
        N'C') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            DROP CONSTRAINT CK_App_QlhvAutoSyncRun_Status;
    END;

    ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
        ADD CONSTRAINT CK_App_QlhvAutoSyncRun_Status
        CHECK
        (
            Status IN
            (
                N'QUEUED',
                N'RUNNING',
                N'SUCCEEDED',
                N'PARTIAL_SUCCESS',
                N'PARTIAL_FAILED',
                N'FAILED'
            )
        );

    ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
        ADD CONSTRAINT CK_App_QlhvAutoSyncRun_StatusTimestamps
        CHECK
        (
            (Status = N'QUEUED'
                AND StartedAtUtc IS NULL
                AND CompletedAtUtc IS NULL)
            OR
            (Status = N'RUNNING'
                AND StartedAtUtc IS NOT NULL
                AND CompletedAtUtc IS NULL)
            OR
            (Status IN
                (
                    N'SUCCEEDED',
                    N'PARTIAL_SUCCESS',
                    N'PARTIAL_FAILED',
                    N'FAILED'
                )
                AND CompletedAtUtc IS NOT NULL)
        );

    ALTER TABLE dbo.App_QlhvSyncOperationHistory
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvSyncOperationHistory_Status;
    ALTER TABLE dbo.App_QlhvSyncOperationHistory
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvSyncOperationHistory_StatusTimestamps;
    ALTER TABLE dbo.App_QlhvAutoSyncRun
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_Status;
    ALTER TABLE dbo.App_QlhvAutoSyncRun
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_StatusTimestamps;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id IN
        (
            OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U'),
            OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
        )
          AND name IN
          (
              N'CK_App_QlhvSyncOperationHistory_Status',
              N'CK_App_QlhvSyncOperationHistory_StatusTimestamps',
              N'CK_App_QlhvAutoSyncRun_Status',
              N'CK_App_QlhvAutoSyncRun_StatusTimestamps'
          )
          AND (is_disabled = 1 OR is_not_trusted = 1)
    )
    BEGIN
        THROW 527414, 'Partial-sync status constraints are disabled or untrusted.', 1;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO
