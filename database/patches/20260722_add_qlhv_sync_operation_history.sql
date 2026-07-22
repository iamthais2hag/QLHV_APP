/*
    QLHV_APP - in-app CSDT backup refresh and full-sync operation history.

    Safety:
    - Schema-only, idempotent patch. It does not back up, restore, or synchronize data.
    - Stores operation metadata only; never store an operations key, password,
      connection string, or other secret in ErrorMessage/DetailJson.
    - The filtered unique index permits at most one QUEUED/RUNNING operation per
      source partition, including across multiple API processes.
*/

USE [QLHV_APP];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvSyncOperationHistory
        (
            Id bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_QlhvSyncOperationHistory PRIMARY KEY CLUSTERED,
            OperationId uniqueidentifier NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_OperationId DEFAULT NEWSEQUENTIALID(),
            SourceType nvarchar(10) NOT NULL,
            OperationType nvarchar(30) NOT NULL,
            Status nvarchar(20) NOT NULL,

            LiveDatabaseName sysname NOT NULL,
            BackupDatabaseName sysname NOT NULL,
            MaCSDT nvarchar(10) NOT NULL,
            SourceProfileCode nvarchar(50) NOT NULL,

            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
            StartedAtUtc datetime2(7) NULL,
            CompletedAtUtc datetime2(7) NULL,
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),

            LiveRows bigint NULL,
            BackupRows bigint NULL,
            TargetActiveRows bigint NULL,
            SourceRows bigint NULL,
            InsertedRows bigint NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_InsertedRows DEFAULT 0,
            UpdatedRows bigint NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_UpdatedRows DEFAULT 0,
            ReactivatedRows bigint NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_ReactivatedRows DEFAULT 0,
            SoftDeletedRows bigint NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_SoftDeletedRows DEFAULT 0,
            SkippedRows bigint NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_SkippedRows DEFAULT 0,

            SnapshotToken nvarchar(512) NULL,
            ErrorMessage nvarchar(2000) NULL,
            DetailJson nvarchar(max) NULL,

            CONSTRAINT UQ_App_QlhvSyncOperationHistory_OperationId UNIQUE NONCLUSTERED (OperationId),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_SourceType
                CHECK (SourceType IN (N'OTO', N'MOTO')),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_OperationType
                CHECK (OperationType IN (N'REFRESH_BACKUP', N'FULL_SYNC')),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_Status
                CHECK (Status IN (N'QUEUED', N'RUNNING', N'SUCCEEDED', N'FAILED')),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_StatusTimestamps
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
                    (Status IN (N'SUCCEEDED', N'FAILED')
                        AND CompletedAtUtc IS NOT NULL)
                ),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_TimestampOrder
                CHECK
                (
                    (StartedAtUtc IS NULL OR StartedAtUtc >= CreatedAtUtc)
                    AND
                    (CompletedAtUtc IS NULL OR CompletedAtUtc >= COALESCE(StartedAtUtc, CreatedAtUtc))
                    AND UpdatedAtUtc >= CreatedAtUtc
                ),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_NonNegativeCounts
                CHECK
                (
                    (LiveRows IS NULL OR LiveRows >= 0)
                    AND (BackupRows IS NULL OR BackupRows >= 0)
                    AND (TargetActiveRows IS NULL OR TargetActiveRows >= 0)
                    AND (SourceRows IS NULL OR SourceRows >= 0)
                    AND InsertedRows >= 0
                    AND UpdatedRows >= 0
                    AND ReactivatedRows >= 0
                    AND SoftDeletedRows >= 0
                    AND SkippedRows >= 0
                ),
            CONSTRAINT CK_App_QlhvSyncOperationHistory_DetailJson
                CHECK (DetailJson IS NULL OR ISJSON(DetailJson) = 1)
        );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NULL
    BEGIN
        THROW 527240, 'Failed to create dbo.App_QlhvSyncOperationHistory.', 1;
    END;

    IF EXISTS
    (
        SELECT required.ColumnName
        FROM
        (
            VALUES
                (N'Id'),
                (N'OperationId'),
                (N'SourceType'),
                (N'OperationType'),
                (N'Status'),
                (N'LiveDatabaseName'),
                (N'BackupDatabaseName'),
                (N'MaCSDT'),
                (N'SourceProfileCode'),
                (N'CreatedAtUtc'),
                (N'StartedAtUtc'),
                (N'CompletedAtUtc'),
                (N'UpdatedAtUtc'),
                (N'LiveRows'),
                (N'BackupRows'),
                (N'TargetActiveRows'),
                (N'SourceRows'),
                (N'InsertedRows'),
                (N'UpdatedRows'),
                (N'ReactivatedRows'),
                (N'SoftDeletedRows'),
                (N'SkippedRows'),
                (N'SnapshotToken'),
                (N'ErrorMessage'),
                (N'DetailJson')
        ) AS required(ColumnName)
        WHERE COL_LENGTH(N'dbo.App_QlhvSyncOperationHistory', required.ColumnName) IS NULL
    )
    BEGIN
        THROW 527241, 'Existing dbo.App_QlhvSyncOperationHistory has an incompatible schema.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
          AND name = N'UX_App_QlhvSyncOperationHistory_ActiveSource'
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_QlhvSyncOperationHistory_ActiveSource
            ON dbo.App_QlhvSyncOperationHistory (SourceType)
            INCLUDE (OperationId, OperationType, Status, StartedAtUtc, UpdatedAtUtc)
            WHERE Status IN (N'QUEUED', N'RUNNING');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
          AND name = N'IX_App_QlhvSyncOperationHistory_History'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_QlhvSyncOperationHistory_History
            ON dbo.App_QlhvSyncOperationHistory
            (
                SourceType,
                CreatedAtUtc DESC,
                Id DESC
            )
            INCLUDE
            (
                OperationId,
                OperationType,
                Status,
                StartedAtUtc,
                CompletedAtUtc,
                SourceRows,
                InsertedRows,
                UpdatedRows,
                ReactivatedRows,
                SoftDeletedRows,
                SkippedRows,
                SnapshotToken
            );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
          AND name = N'IX_App_QlhvSyncOperationHistory_LatestByType'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_QlhvSyncOperationHistory_LatestByType
            ON dbo.App_QlhvSyncOperationHistory
            (
                SourceType,
                OperationType,
                Status,
                CompletedAtUtc DESC,
                Id DESC
            )
            INCLUDE
            (
                OperationId,
                LiveRows,
                BackupRows,
                TargetActiveRows,
                SourceRows,
                SnapshotToken,
                ErrorMessage
            );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
GO
