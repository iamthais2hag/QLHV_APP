/*
    QLHV_APP - durable Auto Sync run state and operation actor attribution.

    Safety:
    - Idempotent schema-only patch.
    - Does not execute refresh, restore, full sync, or any business-data write.
    - The unique active-slot index prevents duplicate Auto Sync runs across processes.
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
        THROW 527320, 'Run 20260722_add_qlhv_sync_operation_history.sql first.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_QlhvSyncOperationHistory', N'Actor') IS NULL
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncOperationHistory
            ADD Actor nvarchar(100) NOT NULL
                CONSTRAINT DF_App_QlhvSyncOperationHistory_Actor
                DEFAULT N'MANUAL_ADMIN' WITH VALUES;
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

IF COL_LENGTH(N'dbo.App_QlhvSyncOperationHistory', N'Actor') IS NULL
BEGIN
    THROW 527327, 'Failed to add dbo.App_QlhvSyncOperationHistory.Actor.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns AS actorColumn
    INNER JOIN sys.types AS actorType
        ON actorType.user_type_id = actorColumn.user_type_id
    WHERE actorColumn.object_id =
            OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
      AND actorColumn.name = N'Actor'
      AND actorType.name = N'nvarchar'
      AND actorColumn.max_length = 200
      AND actorColumn.is_nullable = 0
)
BEGIN
    THROW 527329, 'dbo.App_QlhvSyncOperationHistory.Actor has an incompatible schema.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
          AND name = N'CK_App_QlhvSyncOperationHistory_Actor'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncOperationHistory
            DROP CONSTRAINT CK_App_QlhvSyncOperationHistory_Actor;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U')
          AND name = N'CK_App_QlhvSyncOperationHistory_Actor'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncOperationHistory WITH CHECK
            ADD CONSTRAINT CK_App_QlhvSyncOperationHistory_Actor
            CHECK
            (
                Actor IN
                (
                    N'MANUAL_ADMIN',
                    N'SYSTEM_AUTO_SYNC',
                    N'SYSTEM_SESSION_START'
                )
            );
    END;

    ALTER TABLE dbo.App_QlhvSyncOperationHistory
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvSyncOperationHistory_Actor;

    IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvAutoSyncRun
        (
            Id bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_QlhvAutoSyncRun PRIMARY KEY CLUSTERED,
            RunId uniqueidentifier NOT NULL
                CONSTRAINT DF_App_QlhvAutoSyncRun_RunId DEFAULT NEWSEQUENTIALID(),
            TriggerType nvarchar(20) NOT NULL,
            Actor nvarchar(100) NOT NULL,
            Status nvarchar(20) NOT NULL,
            SourceOrderJson nvarchar(200) NOT NULL,
            CurrentSourceType nvarchar(10) NULL,
            CurrentStage nvarchar(32) NOT NULL
                CONSTRAINT DF_App_QlhvAutoSyncRun_CurrentStage DEFAULT N'CONNECTING',

            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_QlhvAutoSyncRun_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
            StartedAtUtc datetime2(7) NULL,
            CompletedAtUtc datetime2(7) NULL,
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_QlhvAutoSyncRun_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),

            OtoResultJson nvarchar(max) NULL,
            MotoResultJson nvarchar(max) NULL,
            ErrorMessage nvarchar(2000) NULL,

            ActiveSlot AS
            (
                CASE
                    WHEN Status IN (N'QUEUED', N'RUNNING')
                    THEN CONVERT(tinyint, 1)
                    ELSE NULL
                END
            ) PERSISTED,

            CONSTRAINT UQ_App_QlhvAutoSyncRun_RunId
                UNIQUE NONCLUSTERED (RunId),
            CONSTRAINT CK_App_QlhvAutoSyncRun_TriggerType
                CHECK (TriggerType IN (N'STARTUP', N'MANUAL', N'SESSION_START')),
            CONSTRAINT CK_App_QlhvAutoSyncRun_Actor
                CHECK
                (
                    Actor IN
                    (
                        N'MANUAL_ADMIN',
                        N'SYSTEM_AUTO_SYNC',
                        N'SYSTEM_SESSION_START'
                    )
                ),
            CONSTRAINT CK_App_QlhvAutoSyncRun_Status
                CHECK
                (
                    Status IN
                    (
                        N'QUEUED',
                        N'RUNNING',
                        N'SUCCEEDED',
                        N'PARTIAL_FAILED',
                        N'FAILED'
                    )
                ),
            CONSTRAINT CK_App_QlhvAutoSyncRun_CurrentSource
                CHECK (CurrentSourceType IS NULL OR CurrentSourceType IN (N'OTO', N'MOTO')),
            CONSTRAINT CK_App_QlhvAutoSyncRun_CurrentStage
                CHECK
                (
                    CurrentStage IN
                    (
                        N'CONNECTING',
                        N'REFRESH_OTO',
                        N'SYNC_OTO',
                        N'REFRESH_MOTO',
                        N'SYNC_MOTO',
                        N'LOADING_DATA',
                        N'COMPLETED',
                        N'FAILED'
                    )
                ),
            CONSTRAINT CK_App_QlhvAutoSyncRun_SourceOrderJson
                CHECK (ISJSON(SourceOrderJson) = 1),
            CONSTRAINT CK_App_QlhvAutoSyncRun_OtoResultJson
                CHECK (OtoResultJson IS NULL OR ISJSON(OtoResultJson) = 1),
            CONSTRAINT CK_App_QlhvAutoSyncRun_MotoResultJson
                CHECK (MotoResultJson IS NULL OR ISJSON(MotoResultJson) = 1),
            CONSTRAINT CK_App_QlhvAutoSyncRun_StatusTimestamps
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
                    (Status IN (N'SUCCEEDED', N'PARTIAL_FAILED', N'FAILED')
                        AND CompletedAtUtc IS NOT NULL)
                ),
            CONSTRAINT CK_App_QlhvAutoSyncRun_TimestampOrder
                CHECK
                (
                    (StartedAtUtc IS NULL OR StartedAtUtc >= CreatedAtUtc)
                    AND
                    (CompletedAtUtc IS NULL OR CompletedAtUtc >= COALESCE(StartedAtUtc, CreatedAtUtc))
                    AND UpdatedAtUtc >= CreatedAtUtc
                )
        );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL
    BEGIN
        THROW 527321, 'Failed to create dbo.App_QlhvAutoSyncRun.', 1;
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

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL
    BEGIN
        THROW 527321, 'Failed to create dbo.App_QlhvAutoSyncRun.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_QlhvAutoSyncRun', N'CurrentStage') IS NULL
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            ADD CurrentStage nvarchar(32) NULL;
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

IF COL_LENGTH(N'dbo.App_QlhvAutoSyncRun', N'CurrentStage') IS NULL
BEGIN
    THROW 527328, 'Failed to add dbo.App_QlhvAutoSyncRun.CurrentStage.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns AS currentStageColumn
    INNER JOIN sys.types AS currentStageType
        ON currentStageType.user_type_id = currentStageColumn.user_type_id
    WHERE currentStageColumn.object_id =
            OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
      AND currentStageColumn.name = N'CurrentStage'
      AND currentStageType.name = N'nvarchar'
      AND currentStageColumn.max_length = 64
)
BEGIN
    THROW 527330, 'dbo.App_QlhvAutoSyncRun.CurrentStage has an incompatible schema.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE dbo.App_QlhvAutoSyncRun
    SET CurrentStage = CASE
        WHEN Status = N'SUCCEEDED' THEN N'COMPLETED'
        WHEN Status IN (N'PARTIAL_FAILED', N'FAILED') THEN N'FAILED'
        ELSE N'CONNECTING'
    END
    WHERE CurrentStage IS NULL;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CurrentStage'
          AND is_nullable = 1
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            ALTER COLUMN CurrentStage nvarchar(32) NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS currentStageDefault
        INNER JOIN sys.columns AS currentStageColumn
            ON currentStageColumn.object_id = currentStageDefault.parent_object_id
           AND currentStageColumn.column_id = currentStageDefault.parent_column_id
        WHERE currentStageDefault.parent_object_id =
                OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND currentStageColumn.name = N'CurrentStage'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            ADD CONSTRAINT DF_App_QlhvAutoSyncRun_CurrentStage
                DEFAULT N'CONNECTING' FOR CurrentStage;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CurrentStage'
          AND is_nullable = 1
    )
    BEGIN
        THROW 527331, 'Failed to make dbo.App_QlhvAutoSyncRun.CurrentStage NOT NULL.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CK_App_QlhvAutoSyncRun_TriggerType'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            DROP CONSTRAINT CK_App_QlhvAutoSyncRun_TriggerType;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CK_App_QlhvAutoSyncRun_CurrentStage'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            DROP CONSTRAINT CK_App_QlhvAutoSyncRun_CurrentStage;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CK_App_QlhvAutoSyncRun_TriggerType'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
            ADD CONSTRAINT CK_App_QlhvAutoSyncRun_TriggerType
            CHECK (TriggerType IN (N'STARTUP', N'MANUAL', N'SESSION_START'));
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CK_App_QlhvAutoSyncRun_Actor'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun
            DROP CONSTRAINT CK_App_QlhvAutoSyncRun_Actor;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CK_App_QlhvAutoSyncRun_Actor'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
            ADD CONSTRAINT CK_App_QlhvAutoSyncRun_Actor
            CHECK
            (
                Actor IN
                (
                    N'MANUAL_ADMIN',
                    N'SYSTEM_AUTO_SYNC',
                    N'SYSTEM_SESSION_START'
                )
            );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'CK_App_QlhvAutoSyncRun_CurrentStage'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvAutoSyncRun WITH CHECK
            ADD CONSTRAINT CK_App_QlhvAutoSyncRun_CurrentStage
            CHECK
            (
                CurrentStage IN
                (
                    N'CONNECTING',
                    N'REFRESH_OTO',
                    N'SYNC_OTO',
                    N'REFRESH_MOTO',
                    N'SYNC_MOTO',
                    N'LOADING_DATA',
                    N'COMPLETED',
                    N'FAILED'
                )
            );
    END;

    ALTER TABLE dbo.App_QlhvAutoSyncRun
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_TriggerType;
    ALTER TABLE dbo.App_QlhvAutoSyncRun
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_Actor;
    ALTER TABLE dbo.App_QlhvAutoSyncRun
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvAutoSyncRun_CurrentStage;

    IF EXISTS
    (
        SELECT required.ColumnName
        FROM
        (
            VALUES
                (N'Id'),
                (N'RunId'),
                (N'TriggerType'),
                (N'Actor'),
                (N'Status'),
                (N'SourceOrderJson'),
                (N'CurrentSourceType'),
                (N'CurrentStage'),
                (N'CreatedAtUtc'),
                (N'StartedAtUtc'),
                (N'CompletedAtUtc'),
                (N'UpdatedAtUtc'),
                (N'OtoResultJson'),
                (N'MotoResultJson'),
                (N'ErrorMessage'),
                (N'ActiveSlot')
        ) AS required(ColumnName)
        WHERE COL_LENGTH(N'dbo.App_QlhvAutoSyncRun', required.ColumnName) IS NULL
    )
    BEGIN
        THROW 527322, 'Existing dbo.App_QlhvAutoSyncRun has an incompatible schema.', 1;
    END;

    IF COLUMNPROPERTY(
            OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U'),
            N'ActiveSlot',
            'IsComputed') <> 1
       OR
       (
           SELECT cc.definition
           FROM sys.computed_columns AS cc
           WHERE cc.object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
             AND cc.name = N'ActiveSlot'
       ) NOT LIKE N'%QUEUED%RUNNING%'
    BEGIN
        THROW 527323, 'dbo.App_QlhvAutoSyncRun.ActiveSlot is incompatible.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS activeIndex
        WHERE activeIndex.object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND activeIndex.name = N'UX_App_QlhvAutoSyncRun_ActiveSlot'
          AND
          (
              activeIndex.is_unique <> 1
              OR activeIndex.is_disabled = 1
              OR activeIndex.filter_definition NOT LIKE N'%ActiveSlot%IS NOT NULL%'
              OR NOT EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS activeKey
                  INNER JOIN sys.columns AS activeColumn
                      ON activeColumn.object_id = activeKey.object_id
                     AND activeColumn.column_id = activeKey.column_id
                  WHERE activeKey.object_id = activeIndex.object_id
                    AND activeKey.index_id = activeIndex.index_id
                    AND activeKey.key_ordinal = 1
                    AND activeColumn.name = N'ActiveSlot'
              )
              OR EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS extraKey
                  WHERE extraKey.object_id = activeIndex.object_id
                    AND extraKey.index_id = activeIndex.index_id
                    AND extraKey.key_ordinal > 1
              )
          )
    )
    BEGIN
        DROP INDEX UX_App_QlhvAutoSyncRun_ActiveSlot
            ON dbo.App_QlhvAutoSyncRun;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'UX_App_QlhvAutoSyncRun_ActiveSlot'
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_QlhvAutoSyncRun_ActiveSlot
            ON dbo.App_QlhvAutoSyncRun (ActiveSlot)
            INCLUDE (RunId, Status, CurrentSourceType, CurrentStage, UpdatedAtUtc)
            WHERE ActiveSlot IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS runIdIndex
        INNER JOIN sys.index_columns AS runIdKey
            ON runIdKey.object_id = runIdIndex.object_id
           AND runIdKey.index_id = runIdIndex.index_id
           AND runIdKey.key_ordinal = 1
        INNER JOIN sys.columns AS runIdColumn
            ON runIdColumn.object_id = runIdKey.object_id
           AND runIdColumn.column_id = runIdKey.column_id
        WHERE runIdIndex.object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND runIdIndex.is_unique = 1
          AND runIdIndex.is_disabled = 0
          AND runIdColumn.name = N'RunId'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS extraRunIdKey
              WHERE extraRunIdKey.object_id = runIdIndex.object_id
                AND extraRunIdKey.index_id = runIdIndex.index_id
                AND extraRunIdKey.key_ordinal > 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND name = N'UX_App_QlhvAutoSyncRun_RunId'
              AND is_unique_constraint = 1
        )
        BEGIN
            THROW 527326, 'Named RunId index is an incompatible unique constraint.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
              AND name = N'UX_App_QlhvAutoSyncRun_RunId'
        )
        BEGIN
            DROP INDEX UX_App_QlhvAutoSyncRun_RunId
                ON dbo.App_QlhvAutoSyncRun;
        END;

        CREATE UNIQUE NONCLUSTERED INDEX UX_App_QlhvAutoSyncRun_RunId
            ON dbo.App_QlhvAutoSyncRun (RunId);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS historyIndex
        WHERE historyIndex.object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND historyIndex.name = N'IX_App_QlhvAutoSyncRun_History'
          AND
          (
              historyIndex.is_disabled = 1
              OR NOT EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS historyKey
                  INNER JOIN sys.columns AS historyColumn
                      ON historyColumn.object_id = historyKey.object_id
                     AND historyColumn.column_id = historyKey.column_id
                  WHERE historyKey.object_id = historyIndex.object_id
                    AND historyKey.index_id = historyIndex.index_id
                    AND historyKey.key_ordinal = 1
                    AND historyColumn.name = N'CreatedAtUtc'
                    AND historyKey.is_descending_key = 1
              )
              OR NOT EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS historyKey
                  INNER JOIN sys.columns AS historyColumn
                      ON historyColumn.object_id = historyKey.object_id
                     AND historyColumn.column_id = historyKey.column_id
                  WHERE historyKey.object_id = historyIndex.object_id
                    AND historyKey.index_id = historyIndex.index_id
                    AND historyKey.key_ordinal = 2
                    AND historyColumn.name = N'Id'
                    AND historyKey.is_descending_key = 1
              )
              OR EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS extraHistoryKey
                  WHERE extraHistoryKey.object_id = historyIndex.object_id
                    AND extraHistoryKey.index_id = historyIndex.index_id
                    AND extraHistoryKey.key_ordinal > 2
              )
          )
    )
    BEGIN
        DROP INDEX IX_App_QlhvAutoSyncRun_History
            ON dbo.App_QlhvAutoSyncRun;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U')
          AND name = N'IX_App_QlhvAutoSyncRun_History'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_QlhvAutoSyncRun_History
            ON dbo.App_QlhvAutoSyncRun (CreatedAtUtc DESC, Id DESC)
            INCLUDE
            (
                RunId,
                TriggerType,
                Actor,
                Status,
                CurrentSourceType,
                StartedAtUtc,
                CompletedAtUtc,
                ErrorMessage
            );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvSyncPartitionState
        (
            SourceType nvarchar(10) NOT NULL
                CONSTRAINT PK_App_QlhvSyncPartitionState PRIMARY KEY,
            SourceProfileCode nvarchar(64) NOT NULL,
            AppliedBackupSnapshotToken nvarchar(512) NOT NULL,
            HocVienRows bigint NOT NULL,
            KhoaHocRows bigint NOT NULL,
            GiaoVienRows bigint NOT NULL,
            KhoaHocGiaoVienRows bigint NOT NULL,
            AppliedAtUtc datetime2(7) NOT NULL,
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_QlhvSyncPartitionState_UpdatedAtUtc
                DEFAULT SYSUTCDATETIME(),
            CONSTRAINT CK_App_QlhvSyncPartitionState_Source
                CHECK
                (
                    (SourceType = N'OTO' AND SourceProfileCode = N'CSDT_OTO')
                    OR
                    (SourceType = N'MOTO' AND SourceProfileCode = N'CSDT_MOTO')
                ),
            CONSTRAINT CK_App_QlhvSyncPartitionState_Rows
                CHECK
                (
                    HocVienRows >= 0
                    AND KhoaHocRows >= 0
                    AND GiaoVienRows >= 0
                    AND KhoaHocGiaoVienRows >= 0
                )
        );
    END;

    IF EXISTS
    (
        SELECT required.ColumnName
        FROM
        (
            VALUES
                (N'SourceType'),
                (N'SourceProfileCode'),
                (N'AppliedBackupSnapshotToken'),
                (N'HocVienRows'),
                (N'KhoaHocRows'),
                (N'GiaoVienRows'),
                (N'KhoaHocGiaoVienRows'),
                (N'AppliedAtUtc'),
                (N'UpdatedAtUtc')
        ) AS required(ColumnName)
        WHERE COL_LENGTH(N'dbo.App_QlhvSyncPartitionState', required.ColumnName) IS NULL
    )
    BEGIN
        THROW 527324, 'Existing dbo.App_QlhvSyncPartitionState has an incompatible schema.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS partitionPk
        INNER JOIN sys.index_columns AS partitionKey
            ON partitionKey.object_id = partitionPk.parent_object_id
           AND partitionKey.index_id = partitionPk.unique_index_id
           AND partitionKey.key_ordinal = 1
        INNER JOIN sys.columns AS partitionColumn
            ON partitionColumn.object_id = partitionKey.object_id
           AND partitionColumn.column_id = partitionKey.column_id
        WHERE partitionPk.parent_object_id =
                OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U')
          AND partitionPk.type = N'PK'
          AND partitionColumn.name = N'SourceType'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS extraPartitionKey
              WHERE extraPartitionKey.object_id = partitionPk.parent_object_id
                AND extraPartitionKey.index_id = partitionPk.unique_index_id
                AND extraPartitionKey.key_ordinal > 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.key_constraints
            WHERE parent_object_id =
                    OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U')
              AND type = N'PK'
        )
        BEGIN
            THROW 527325, 'dbo.App_QlhvSyncPartitionState has an incompatible primary key.', 1;
        END;

        ALTER TABLE dbo.App_QlhvSyncPartitionState
            ADD CONSTRAINT PK_App_QlhvSyncPartitionState
                PRIMARY KEY (SourceType);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U')
          AND name = N'CK_App_QlhvSyncPartitionState_Source'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncPartitionState
            DROP CONSTRAINT CK_App_QlhvSyncPartitionState_Source;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U')
          AND name = N'CK_App_QlhvSyncPartitionState_Source'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncPartitionState WITH CHECK
            ADD CONSTRAINT CK_App_QlhvSyncPartitionState_Source
            CHECK
            (
                (SourceType = N'OTO' AND SourceProfileCode = N'CSDT_OTO')
                OR
                (SourceType = N'MOTO' AND SourceProfileCode = N'CSDT_MOTO')
            );
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U')
          AND name = N'CK_App_QlhvSyncPartitionState_Rows'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncPartitionState
            DROP CONSTRAINT CK_App_QlhvSyncPartitionState_Rows;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_QlhvSyncPartitionState', N'U')
          AND name = N'CK_App_QlhvSyncPartitionState_Rows'
    )
    BEGIN
        ALTER TABLE dbo.App_QlhvSyncPartitionState WITH CHECK
            ADD CONSTRAINT CK_App_QlhvSyncPartitionState_Rows
            CHECK
            (
                HocVienRows >= 0
                AND KhoaHocRows >= 0
                AND GiaoVienRows >= 0
                AND KhoaHocGiaoVienRows >= 0
            );
    END;

    ALTER TABLE dbo.App_QlhvSyncPartitionState
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvSyncPartitionState_Source;
    ALTER TABLE dbo.App_QlhvSyncPartitionState
        WITH CHECK CHECK CONSTRAINT CK_App_QlhvSyncPartitionState_Rows;

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
