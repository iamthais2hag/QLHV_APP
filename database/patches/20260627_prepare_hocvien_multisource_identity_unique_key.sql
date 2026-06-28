/*
    Task 5 B3W16 - DRAFT ONLY.

    Purpose:
    - Prepare dbo.App_HocVien for multi-source sync identity.
    - Runtime write identity is SourceProfileCode + SourceMaDK.
    - Existing UQ_App_HocVien_MaDK can block valid DATA_V1/DATA_V2 rows that share MaDK.

    Safety:
    - DO NOT run on production without manual SQL review and backup.
    - Run on QLHV_APP_TEST first only after read-only diagnostics pass.
    - This script intentionally stops before dropping the MaDK-only unique constraint unless a human changes
      @AllowDropMaDkUniqueConstraint to 1 after review.
    - This script does not update, delete, truncate, or merge data.
*/

USE [QLHV_APP_TEST];
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

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @AllowDropMaDkUniqueConstraint bit = 0;

    IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL
    BEGIN
        THROW 51200, 'Pre-check failed: dbo.App_HocVien does not exist.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_HocVien', N'SourceProfileCode') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceMaDK') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceSystem') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceVersion') IS NULL
    BEGIN
        THROW 51201, 'Pre-check failed: source identity columns are missing.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM dbo.App_HocVien
        WHERE IsDeleted = 0
          AND (
              SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N''
              OR SourceMaDK IS NULL OR LTRIM(RTRIM(SourceMaDK)) = N''
          )
    )
    BEGIN
        THROW 51202, 'Pre-check failed: active HocVien rows still have missing SourceProfileCode or SourceMaDK.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM dbo.App_HocVien
        WHERE IsDeleted = 0
        GROUP BY SourceProfileCode, SourceMaDK
        HAVING COUNT(1) > 1
    )
    BEGIN
        THROW 51203, 'Pre-check failed: duplicate active SourceProfileCode + SourceMaDK rows exist.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_App_HocVien_SourceProfileCode_SourceMaDK_Active'
          AND object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_HocVien_SourceProfileCode_SourceMaDK_Active
        ON dbo.App_HocVien(SourceProfileCode, SourceMaDK)
        WHERE IsDeleted = 0
          AND SourceProfileCode IS NOT NULL
          AND SourceMaDK IS NOT NULL;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.key_constraints
        WHERE name = N'UQ_App_HocVien_MaDK'
          AND parent_object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
    )
    BEGIN
        IF @AllowDropMaDkUniqueConstraint <> 1
        BEGIN
            THROW 51204, 'Manual review required: UQ_App_HocVien_MaDK still exists. Set @AllowDropMaDkUniqueConstraint = 1 only after approval.', 1;
        END;

        ALTER TABLE dbo.App_HocVien DROP CONSTRAINT UQ_App_HocVien_MaDK;
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
