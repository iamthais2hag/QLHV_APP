/*
    QLHV_APP - Rollback controlled DATA_V2 source identity backfill.

    SAFETY:
    - Run on QLHV_APP_TEST first.
    - Take a backup/snapshot before running.
    - Do NOT run on production until reviewed and approved.
    - Use only to roll back database/patches/20260627_backfill_hocvien_source_profile_data_v2.sql.

    This rollback refuses to run unless all 1970 rows are exactly in the expected backfilled state:
    - SourceProfileCode = DATA_V2
    - SourceMaDK = MaDK
    - SourceSystem = V2

    It then clears SourceProfileCode, SourceMaDK, SourceSystem, and SourceVersion.
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

    IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL
    BEGIN
        THROW 51100, 'Rollback pre-check failed: dbo.App_HocVien does not exist.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_HocVien', N'SourceProfileCode') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceMaDK') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceSystem') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceVersion') IS NULL
    BEGIN
        THROW 51101, 'Rollback pre-check failed: source identity columns are missing.', 1;
    END;

    DECLARE @TotalRows int;
    DECLARE @DataV2Rows int;
    DECLARE @SourceMaDkMatches int;
    DECLARE @V2SystemRows int;
    DECLARE @UnexpectedRows int;

    SELECT
        @TotalRows = COUNT(1),
        @DataV2Rows = SUM(CASE WHEN SourceProfileCode = N'DATA_V2' THEN 1 ELSE 0 END),
        @SourceMaDkMatches = SUM(CASE WHEN SourceMaDK = MaDK THEN 1 ELSE 0 END),
        @V2SystemRows = SUM(CASE WHEN SourceSystem = N'V2' THEN 1 ELSE 0 END),
        @UnexpectedRows = SUM(CASE
            WHEN SourceProfileCode = N'DATA_V2'
             AND SourceMaDK = MaDK
             AND SourceSystem = N'V2' THEN 0 ELSE 1 END)
    FROM dbo.App_HocVien;

    IF @TotalRows <> 1970
    BEGIN
        THROW 51102, 'Rollback pre-check failed: dbo.App_HocVien total row count is not 1970.', 1;
    END;

    IF @DataV2Rows <> 1970
    BEGIN
        THROW 51103, 'Rollback pre-check failed: SourceProfileCode DATA_V2 row count is not 1970.', 1;
    END;

    IF @SourceMaDkMatches <> 1970
    BEGIN
        THROW 51104, 'Rollback pre-check failed: SourceMaDK does not match MaDK for all rows.', 1;
    END;

    IF @V2SystemRows <> 1970
    BEGIN
        THROW 51105, 'Rollback pre-check failed: SourceSystem V2 row count is not 1970.', 1;
    END;

    IF @UnexpectedRows <> 0
    BEGIN
        THROW 51106, 'Rollback pre-check failed: rows are not exactly in the expected DATA_V2 backfilled state.', 1;
    END;

    UPDATE dbo.App_HocVien
    SET
        SourceProfileCode = NULL,
        SourceMaDK = NULL,
        SourceSystem = NULL,
        SourceVersion = NULL
    WHERE SourceProfileCode = N'DATA_V2'
      AND SourceMaDK = MaDK
      AND SourceSystem = N'V2';

    DECLARE @UpdatedRows int = @@ROWCOUNT;
    IF @UpdatedRows <> 1970
    BEGIN
        THROW 51107, 'Rollback post-check failed: cleared row count is not 1970.', 1;
    END;

    DECLARE @ClearedRows int;
    SELECT
        @ClearedRows = SUM(CASE
            WHEN SourceProfileCode IS NULL
             AND SourceMaDK IS NULL
             AND SourceSystem IS NULL
             AND SourceVersion IS NULL THEN 1 ELSE 0 END)
    FROM dbo.App_HocVien;

    IF @ClearedRows <> 1970
    BEGIN
        THROW 51108, 'Rollback post-check failed: source identity fields were not cleared for all rows.', 1;
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
