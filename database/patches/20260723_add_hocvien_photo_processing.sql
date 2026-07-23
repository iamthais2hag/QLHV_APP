USE [QLHV_APP];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_DataVersion', N'U') IS NULL
       OR COL_LENGTH(N'dbo.App_DataVersion', N'PhotoVersion') IS NULL
    BEGIN
        THROW 52100, N'Run the App_DataVersion patch before the photo-processing patch.', 1;
    END;

    IF OBJECT_ID(N'dbo.App_HocVienPhoto', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_HocVienPhoto
        (
            Id bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_HocVienPhoto PRIMARY KEY,
            SourceProfileCode nvarchar(32) NOT NULL,
            SourceMaDK nvarchar(128) NOT NULL,
            MaKhoa nvarchar(128) NULL,
            SourceImagePath nvarchar(1000) NULL,
            OutputImagePath nvarchar(1000) NULL,
            SourceFileHash char(64) NULL,
            SourcePathStatus nvarchar(32) NOT NULL,
            SourcePathKind nvarchar(32) NOT NULL,
            ProcessingStatus nvarchar(32) NOT NULL,
            ProcessingConfidence decimal(9,6) NULL,
            ProcessedAtUtc datetime2(3) NULL,
            ErrorMessage nvarchar(1000) NULL,
            ReviewRequired bit NOT NULL
                CONSTRAINT DF_App_HocVienPhoto_ReviewRequired DEFAULT (0),
            ApprovedAtUtc datetime2(3) NULL,
            ApprovedByUserId bigint NULL,
            CreatedAtUtc datetime2(3) NOT NULL
                CONSTRAINT DF_App_HocVienPhoto_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc datetime2(3) NOT NULL
                CONSTRAINT DF_App_HocVienPhoto_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT UQ_App_HocVienPhoto_SourceIdentity
                UNIQUE (SourceProfileCode, SourceMaDK),
            CONSTRAINT CK_App_HocVienPhoto_SourceProfileCode
                CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_HocVienPhoto_SourcePathStatus
                CHECK (SourcePathStatus IN (N'FOUND', N'MISSING', N'INVALID_PATH')),
            CONSTRAINT CK_App_HocVienPhoto_SourcePathKind
                CHECK (SourcePathKind IN (N'CURRENT_PATH', N'LEGACY_PATH', N'FALLBACK_PATH')),
            CONSTRAINT CK_App_HocVienPhoto_ProcessingStatus
                CHECK (ProcessingStatus IN
                    (N'PENDING', N'PROCESSING', N'SUCCEEDED',
                     N'REVIEW_REQUIRED', N'FAILED', N'APPROVED')),
            CONSTRAINT CK_App_HocVienPhoto_Confidence
                CHECK (ProcessingConfidence IS NULL OR
                       (ProcessingConfidence >= 0 AND ProcessingConfidence <= 1)),
            CONSTRAINT CK_App_HocVienPhoto_ApprovalPair
                CHECK
                (
                    (
                        ProcessingStatus = N'APPROVED'
                        AND ApprovedAtUtc IS NOT NULL
                        AND ApprovedByUserId IS NOT NULL
                        AND ReviewRequired = 0
                    )
                    OR
                    (
                        ProcessingStatus <> N'APPROVED'
                        AND ApprovedAtUtc IS NULL
                        AND ApprovedByUserId IS NULL
                    )
                )
        );
    END;

    IF OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_HocVienPhotoProcessingHistory
        (
            Id bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_HocVienPhotoProcessingHistory PRIMARY KEY,
            PhotoId bigint NOT NULL,
            SourceProfileCode nvarchar(32) NOT NULL,
            SourceMaDK nvarchar(128) NOT NULL,
            ProcessingStatus nvarchar(32) NOT NULL,
            ProcessingConfidence decimal(9,6) NULL,
            SourceFileHash char(64) NULL,
            OutputImagePath nvarchar(1000) NULL,
            ErrorMessage nvarchar(1000) NULL,
            Actor nvarchar(100) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL
                CONSTRAINT DF_App_HocVienPhotoHistory_CreatedAtUtc
                DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT FK_App_HocVienPhotoHistory_Photo
                FOREIGN KEY (PhotoId) REFERENCES dbo.App_HocVienPhoto(Id),
            CONSTRAINT CK_App_HocVienPhotoHistory_SourceProfileCode
                CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_HocVienPhotoHistory_ProcessingStatus
                CHECK (ProcessingStatus IN
                    (N'PENDING', N'PROCESSING', N'SUCCEEDED',
                     N'REVIEW_REQUIRED', N'FAILED', N'APPROVED')),
            CONSTRAINT CK_App_HocVienPhotoHistory_Confidence
                CHECK (ProcessingConfidence IS NULL OR
                       (ProcessingConfidence >= 0 AND ProcessingConfidence <= 1))
        );
    END;

    IF EXISTS
    (
        SELECT required.ColumnName
        FROM
        (
            VALUES
                (N'Id'),
                (N'SourceProfileCode'),
                (N'SourceMaDK'),
                (N'MaKhoa'),
                (N'SourceImagePath'),
                (N'OutputImagePath'),
                (N'SourceFileHash'),
                (N'SourcePathStatus'),
                (N'SourcePathKind'),
                (N'ProcessingStatus'),
                (N'ProcessingConfidence'),
                (N'ProcessedAtUtc'),
                (N'ErrorMessage'),
                (N'ReviewRequired'),
                (N'ApprovedAtUtc'),
                (N'ApprovedByUserId'),
                (N'CreatedAtUtc'),
                (N'UpdatedAtUtc')
        ) AS required(ColumnName)
        WHERE COL_LENGTH(N'dbo.App_HocVienPhoto', required.ColumnName) IS NULL
    )
       OR EXISTS
    (
        SELECT required.ColumnName
        FROM
        (
            VALUES
                (N'Id'),
                (N'PhotoId'),
                (N'SourceProfileCode'),
                (N'SourceMaDK'),
                (N'ProcessingStatus'),
                (N'ProcessingConfidence'),
                (N'SourceFileHash'),
                (N'OutputImagePath'),
                (N'ErrorMessage'),
                (N'Actor'),
                (N'CreatedAtUtc')
        ) AS required(ColumnName)
        WHERE COL_LENGTH(
            N'dbo.App_HocVienPhotoProcessingHistory',
            required.ColumnName) IS NULL
    )
    BEGIN
        THROW 52101, N'Existing photo-processing tables do not match the expected schema.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS photoPk
        INNER JOIN sys.index_columns AS photoKey
            ON photoKey.object_id = photoPk.parent_object_id
           AND photoKey.index_id = photoPk.unique_index_id
           AND photoKey.key_ordinal = 1
        INNER JOIN sys.columns AS photoColumn
            ON photoColumn.object_id = photoKey.object_id
           AND photoColumn.column_id = photoKey.column_id
        WHERE photoPk.parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND photoPk.type = N'PK'
          AND photoColumn.name = N'Id'
          AND NOT EXISTS
          (
              SELECT 1 FROM sys.index_columns AS extraPhotoKey
              WHERE extraPhotoKey.object_id = photoPk.parent_object_id
                AND extraPhotoKey.index_id = photoPk.unique_index_id
                AND extraPhotoKey.key_ordinal > 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1 FROM sys.key_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
              AND type = N'PK'
        )
            THROW 52102, N'dbo.App_HocVienPhoto has an incompatible primary key.', 1;

        ALTER TABLE dbo.App_HocVienPhoto
            ADD CONSTRAINT PK_App_HocVienPhoto PRIMARY KEY (Id);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS historyPk
        INNER JOIN sys.index_columns AS historyKey
            ON historyKey.object_id = historyPk.parent_object_id
           AND historyKey.index_id = historyPk.unique_index_id
           AND historyKey.key_ordinal = 1
        INNER JOIN sys.columns AS historyColumn
            ON historyColumn.object_id = historyKey.object_id
           AND historyColumn.column_id = historyKey.column_id
        WHERE historyPk.parent_object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND historyPk.type = N'PK'
          AND historyColumn.name = N'Id'
          AND NOT EXISTS
          (
              SELECT 1 FROM sys.index_columns AS extraHistoryKey
              WHERE extraHistoryKey.object_id = historyPk.parent_object_id
                AND extraHistoryKey.index_id = historyPk.unique_index_id
                AND extraHistoryKey.key_ordinal > 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1 FROM sys.key_constraints
            WHERE parent_object_id =
                    OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
              AND type = N'PK'
        )
            THROW 52103,
                N'dbo.App_HocVienPhotoProcessingHistory has an incompatible primary key.',
                1;

        ALTER TABLE dbo.App_HocVienPhotoProcessingHistory
            ADD CONSTRAINT PK_App_HocVienPhotoProcessingHistory PRIMARY KEY (Id);
    END;

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND name = N'CK_App_HocVienPhoto_SourceProfileCode'
    )
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT CK_App_HocVienPhoto_SourceProfileCode;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND name = N'CK_App_HocVienPhoto_SourcePathStatus'
    )
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT CK_App_HocVienPhoto_SourcePathStatus;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND name = N'CK_App_HocVienPhoto_SourcePathKind'
    )
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT CK_App_HocVienPhoto_SourcePathKind;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND name = N'CK_App_HocVienPhoto_ProcessingStatus'
    )
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT CK_App_HocVienPhoto_ProcessingStatus;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND name = N'CK_App_HocVienPhoto_Confidence'
    )
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT CK_App_HocVienPhoto_Confidence;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND name = N'CK_App_HocVienPhoto_ApprovalPair'
    )
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT CK_App_HocVienPhoto_ApprovalPair;

    ALTER TABLE dbo.App_HocVienPhoto WITH CHECK
        ADD CONSTRAINT CK_App_HocVienPhoto_SourceProfileCode
            CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_HocVienPhoto_SourcePathStatus
            CHECK (SourcePathStatus IN (N'FOUND', N'MISSING', N'INVALID_PATH')),
            CONSTRAINT CK_App_HocVienPhoto_SourcePathKind
            CHECK (SourcePathKind IN (N'CURRENT_PATH', N'LEGACY_PATH', N'FALLBACK_PATH')),
            CONSTRAINT CK_App_HocVienPhoto_ProcessingStatus
            CHECK
            (
                ProcessingStatus IN
                (
                    N'PENDING',
                    N'PROCESSING',
                    N'SUCCEEDED',
                    N'REVIEW_REQUIRED',
                    N'FAILED',
                    N'APPROVED'
                )
            ),
            CONSTRAINT CK_App_HocVienPhoto_Confidence
            CHECK
            (
                ProcessingConfidence IS NULL
                OR (ProcessingConfidence >= 0 AND ProcessingConfidence <= 1)
            ),
            CONSTRAINT CK_App_HocVienPhoto_ApprovalPair
            CHECK
            (
                (
                    ProcessingStatus = N'APPROVED'
                    AND ApprovedAtUtc IS NOT NULL
                    AND ApprovedByUserId IS NOT NULL
                    AND ReviewRequired = 0
                )
                OR
                (
                    ProcessingStatus <> N'APPROVED'
                    AND ApprovedAtUtc IS NULL
                    AND ApprovedByUserId IS NULL
                )
            );

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND name = N'CK_App_HocVienPhotoHistory_SourceProfileCode'
    )
        ALTER TABLE dbo.App_HocVienPhotoProcessingHistory
            DROP CONSTRAINT CK_App_HocVienPhotoHistory_SourceProfileCode;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND name = N'CK_App_HocVienPhotoHistory_ProcessingStatus'
    )
        ALTER TABLE dbo.App_HocVienPhotoProcessingHistory
            DROP CONSTRAINT CK_App_HocVienPhotoHistory_ProcessingStatus;
    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND name = N'CK_App_HocVienPhotoHistory_Confidence'
    )
        ALTER TABLE dbo.App_HocVienPhotoProcessingHistory
            DROP CONSTRAINT CK_App_HocVienPhotoHistory_Confidence;

    ALTER TABLE dbo.App_HocVienPhotoProcessingHistory WITH CHECK
        ADD CONSTRAINT CK_App_HocVienPhotoHistory_SourceProfileCode
            CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_HocVienPhotoHistory_ProcessingStatus
            CHECK
            (
                ProcessingStatus IN
                (
                    N'PENDING',
                    N'PROCESSING',
                    N'SUCCEEDED',
                    N'REVIEW_REQUIRED',
                    N'FAILED',
                    N'APPROVED'
                )
            ),
            CONSTRAINT CK_App_HocVienPhotoHistory_Confidence
            CHECK
            (
                ProcessingConfidence IS NULL
                OR (ProcessingConfidence >= 0 AND ProcessingConfidence <= 1)
            );

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS statusIndex
        WHERE statusIndex.object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND statusIndex.name = N'IX_App_HocVienPhoto_StatusReview'
          AND
          (
              statusIndex.is_disabled = 1
              OR INDEX_COL(N'dbo.App_HocVienPhoto', statusIndex.index_id, 1) <> N'ProcessingStatus'
              OR INDEX_COL(N'dbo.App_HocVienPhoto', statusIndex.index_id, 2) <> N'ReviewRequired'
              OR INDEX_COL(N'dbo.App_HocVienPhoto', statusIndex.index_id, 3) <> N'SourceProfileCode'
              OR INDEX_COL(N'dbo.App_HocVienPhoto', statusIndex.index_id, 4) <> N'UpdatedAtUtc'
              OR INDEX_COL(N'dbo.App_HocVienPhoto', statusIndex.index_id, 5) IS NOT NULL
          )
    )
    BEGIN
        DROP INDEX IX_App_HocVienPhoto_StatusReview
            ON dbo.App_HocVienPhoto;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS sourceIdentity
        INNER JOIN sys.index_columns AS profileKey
            ON profileKey.object_id = sourceIdentity.object_id
           AND profileKey.index_id = sourceIdentity.index_id
           AND profileKey.key_ordinal = 1
        INNER JOIN sys.columns AS profileColumn
            ON profileColumn.object_id = profileKey.object_id
           AND profileColumn.column_id = profileKey.column_id
        INNER JOIN sys.index_columns AS sourceKey
            ON sourceKey.object_id = sourceIdentity.object_id
           AND sourceKey.index_id = sourceIdentity.index_id
           AND sourceKey.key_ordinal = 2
        INNER JOIN sys.columns AS sourceColumn
            ON sourceColumn.object_id = sourceKey.object_id
           AND sourceColumn.column_id = sourceKey.column_id
        WHERE sourceIdentity.object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND sourceIdentity.is_unique = 1
          AND sourceIdentity.is_disabled = 0
          AND profileColumn.name = N'SourceProfileCode'
          AND sourceColumn.name = N'SourceMaDK'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS extraSourceKey
              WHERE extraSourceKey.object_id = sourceIdentity.object_id
                AND extraSourceKey.index_id = sourceIdentity.index_id
                AND extraSourceKey.key_ordinal > 2
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
              AND name = N'UX_App_HocVienPhoto_SourceIdentity'
        )
        BEGIN
            DROP INDEX UX_App_HocVienPhoto_SourceIdentity
                ON dbo.App_HocVienPhoto;
        END;

        CREATE UNIQUE INDEX UX_App_HocVienPhoto_SourceIdentity
            ON dbo.App_HocVienPhoto (SourceProfileCode, SourceMaDK);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS historyFk
        WHERE historyFk.parent_object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND historyFk.name = N'FK_App_HocVienPhotoHistory_Photo'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.foreign_key_columns AS mapping
              INNER JOIN sys.columns AS parentColumn
                  ON parentColumn.object_id = mapping.parent_object_id
                 AND parentColumn.column_id = mapping.parent_column_id
              INNER JOIN sys.columns AS referencedColumn
                  ON referencedColumn.object_id = mapping.referenced_object_id
                 AND referencedColumn.column_id = mapping.referenced_column_id
              WHERE mapping.constraint_object_id = historyFk.object_id
                AND mapping.referenced_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
                AND parentColumn.name = N'PhotoId'
                AND referencedColumn.name = N'Id'
          )
    )
    BEGIN
        ALTER TABLE dbo.App_HocVienPhotoProcessingHistory
            DROP CONSTRAINT FK_App_HocVienPhotoHistory_Photo;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.indexes AS historyIndex
        WHERE historyIndex.object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND historyIndex.name = N'IX_App_HocVienPhotoHistory_PhotoCreated'
          AND
          (
              historyIndex.is_disabled = 1
              OR INDEX_COL(
                    N'dbo.App_HocVienPhotoProcessingHistory',
                    historyIndex.index_id,
                    1) <> N'PhotoId'
              OR INDEX_COL(
                    N'dbo.App_HocVienPhotoProcessingHistory',
                    historyIndex.index_id,
                    2) <> N'CreatedAtUtc'
              OR INDEX_COL(
                    N'dbo.App_HocVienPhotoProcessingHistory',
                    historyIndex.index_id,
                    3) IS NOT NULL
          )
    )
    BEGIN
        DROP INDEX IX_App_HocVienPhotoHistory_PhotoCreated
            ON dbo.App_HocVienPhotoProcessingHistory;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id =
                OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory', N'U')
          AND name = N'FK_App_HocVienPhotoHistory_Photo'
    )
    BEGIN
        ALTER TABLE dbo.App_HocVienPhotoProcessingHistory WITH CHECK
            ADD CONSTRAINT FK_App_HocVienPhotoHistory_Photo
            FOREIGN KEY (PhotoId) REFERENCES dbo.App_HocVienPhoto(Id);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS approvedFk
        WHERE approvedFk.parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto', N'U')
          AND approvedFk.name = N'FK_App_HocVienPhoto_ApprovedByUser'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.foreign_key_columns AS mapping
              INNER JOIN sys.columns AS parentColumn
                  ON parentColumn.object_id = mapping.parent_object_id
                 AND parentColumn.column_id = mapping.parent_column_id
              INNER JOIN sys.columns AS referencedColumn
                  ON referencedColumn.object_id = mapping.referenced_object_id
                 AND referencedColumn.column_id = mapping.referenced_column_id
              WHERE mapping.constraint_object_id = approvedFk.object_id
                AND mapping.referenced_object_id = OBJECT_ID(N'dbo.App_User', N'U')
                AND parentColumn.name = N'ApprovedByUserId'
                AND referencedColumn.name = N'UserId'
          )
    )
    BEGIN
        ALTER TABLE dbo.App_HocVienPhoto
            DROP CONSTRAINT FK_App_HocVienPhoto_ApprovedByUser;
    END;

    IF OBJECT_ID(N'dbo.App_User', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.App_User', N'UserId') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVienPhoto')
             AND name = N'FK_App_HocVienPhoto_ApprovedByUser'
       )
    BEGIN
        ALTER TABLE dbo.App_HocVienPhoto WITH CHECK
            ADD CONSTRAINT FK_App_HocVienPhoto_ApprovedByUser
            FOREIGN KEY (ApprovedByUserId) REFERENCES dbo.App_User(UserId);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_HocVienPhoto')
          AND name = N'IX_App_HocVienPhoto_StatusReview'
    )
    BEGIN
        CREATE INDEX IX_App_HocVienPhoto_StatusReview
            ON dbo.App_HocVienPhoto
                (ProcessingStatus, ReviewRequired, SourceProfileCode, UpdatedAtUtc DESC);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_HocVienPhotoProcessingHistory')
          AND name = N'IX_App_HocVienPhotoHistory_PhotoCreated'
    )
    BEGIN
        CREATE INDEX IX_App_HocVienPhotoHistory_PhotoCreated
            ON dbo.App_HocVienPhotoProcessingHistory (PhotoId, CreatedAtUtc DESC);
    END;

    ALTER TABLE dbo.App_HocVienPhoto
        WITH CHECK CHECK CONSTRAINT ALL;
    ALTER TABLE dbo.App_HocVienPhotoProcessingHistory
        WITH CHECK CHECK CONSTRAINT ALL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
