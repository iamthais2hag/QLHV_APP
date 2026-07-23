/*
    QLHV_APP - monotonic client data-version metadata.

    Safety:
    - Idempotent schema/control-row patch only.
    - Does not synchronize or modify learner/course/teacher/photo business rows.
    - Full-sync code updates the singleton row inside the same target transaction.
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

    IF OBJECT_ID(N'dbo.App_DataVersion', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_DataVersion
        (
            VersionId tinyint NOT NULL
                CONSTRAINT PK_App_DataVersion PRIMARY KEY CLUSTERED,
            HocVienVersion bigint NOT NULL
                CONSTRAINT DF_App_DataVersion_HocVienVersion DEFAULT 0,
            KhoaHocVersion bigint NOT NULL
                CONSTRAINT DF_App_DataVersion_KhoaHocVersion DEFAULT 0,
            GiaoVienVersion bigint NOT NULL
                CONSTRAINT DF_App_DataVersion_GiaoVienVersion DEFAULT 0,
            PhotoVersion bigint NOT NULL
                CONSTRAINT DF_App_DataVersion_PhotoVersion DEFAULT 0,
            LastSuccessfulSyncUtc datetime2(7) NULL,
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_DataVersion_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
            RowVersion rowversion NOT NULL,

            CONSTRAINT CK_App_DataVersion_Singleton
                CHECK (VersionId = 1),
            CONSTRAINT CK_App_DataVersion_NonNegative
                CHECK
                (
                    HocVienVersion >= 0
                    AND KhoaHocVersion >= 0
                    AND GiaoVienVersion >= 0
                    AND PhotoVersion >= 0
                )
        );
    END;

    IF OBJECT_ID(N'dbo.App_DataVersion', N'U') IS NULL
    BEGIN
        THROW 527300, 'Failed to create dbo.App_DataVersion.', 1;
    END;

    IF EXISTS
    (
        SELECT required.ColumnName
        FROM
        (
            VALUES
                (N'VersionId'),
                (N'HocVienVersion'),
                (N'KhoaHocVersion'),
                (N'GiaoVienVersion'),
                (N'PhotoVersion'),
                (N'LastSuccessfulSyncUtc'),
                (N'UpdatedAtUtc'),
                (N'RowVersion')
        ) AS required(ColumnName)
        WHERE COL_LENGTH(N'dbo.App_DataVersion', required.ColumnName) IS NULL
    )
    BEGIN
        THROW 527301, 'Existing dbo.App_DataVersion has an incompatible schema.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_DataVersion WITH (UPDLOCK, HOLDLOCK)
        WHERE VersionId = 1
    )
    BEGIN
        INSERT INTO dbo.App_DataVersion
        (
            VersionId,
            HocVienVersion,
            KhoaHocVersion,
            GiaoVienVersion,
            PhotoVersion,
            LastSuccessfulSyncUtc,
            UpdatedAtUtc
        )
        VALUES (1, 0, 0, 0, 0, NULL, SYSUTCDATETIME());
    END;

    IF (SELECT COUNT_BIG(1) FROM dbo.App_DataVersion) <> 1
    BEGIN
        THROW 527302, 'dbo.App_DataVersion must contain exactly one control row.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS kc
        INNER JOIN sys.index_columns AS ic
            ON ic.object_id = kc.parent_object_id
           AND ic.index_id = kc.unique_index_id
           AND ic.key_ordinal = 1
        INNER JOIN sys.columns AS c
            ON c.object_id = ic.object_id
           AND c.column_id = ic.column_id
        WHERE kc.parent_object_id = OBJECT_ID(N'dbo.App_DataVersion', N'U')
          AND kc.type = N'PK'
          AND c.name = N'VersionId'
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS extra
              WHERE extra.object_id = kc.parent_object_id
                AND extra.index_id = kc.unique_index_id
                AND extra.key_ordinal > 1
          )
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.key_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.App_DataVersion', N'U')
              AND type = N'PK'
        )
        BEGIN
            THROW 527303, 'dbo.App_DataVersion has an incompatible primary key.', 1;
        END;

        ALTER TABLE dbo.App_DataVersion
            ADD CONSTRAINT PK_App_DataVersion
                PRIMARY KEY CLUSTERED (VersionId);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_DataVersion', N'U')
          AND name = N'CK_App_DataVersion_Singleton'
    )
    BEGIN
        ALTER TABLE dbo.App_DataVersion
            DROP CONSTRAINT CK_App_DataVersion_Singleton;
    END;

    ALTER TABLE dbo.App_DataVersion WITH CHECK
        ADD CONSTRAINT CK_App_DataVersion_Singleton
            CHECK (VersionId = 1);

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.App_DataVersion', N'U')
          AND name = N'CK_App_DataVersion_NonNegative'
    )
    BEGIN
        ALTER TABLE dbo.App_DataVersion
            DROP CONSTRAINT CK_App_DataVersion_NonNegative;
    END;

    ALTER TABLE dbo.App_DataVersion WITH CHECK
        ADD CONSTRAINT CK_App_DataVersion_NonNegative
            CHECK
            (
                HocVienVersion >= 0
                AND KhoaHocVersion >= 0
                AND GiaoVienVersion >= 0
                AND PhotoVersion >= 0
            );

    ALTER TABLE dbo.App_DataVersion
        WITH CHECK CHECK CONSTRAINT CK_App_DataVersion_Singleton;
    ALTER TABLE dbo.App_DataVersion
        WITH CHECK CHECK CONSTRAINT CK_App_DataVersion_NonNegative;

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
