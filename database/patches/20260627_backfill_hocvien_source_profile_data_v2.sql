/*
    QLHV_APP - Controlled backfill for existing App_HocVien source identity.

    SAFETY:
    - Run on QLHV_APP_TEST first.
    - Take a backup/snapshot before running.
    - Do NOT run on production until reviewed and approved.
    - Use this script only when B3W9 source-attribution-diagnostics returns:
        recommendation = DATA_V2
        confidence     = High
    - This script does not change sync behavior and does not execute any sync job.

    Expected current state:
    - dbo.App_HocVien has exactly 1970 rows.
    - All 1970 rows have SourceProfileCode NULL/empty.
    - No row has MaDK NULL/empty.

    SourceVersion is intentionally left NULL. There is not yet an approved versioning convention.
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
        THROW 51000, 'Pre-check failed: dbo.App_HocVien does not exist.', 1;
    END;

    IF COL_LENGTH(N'dbo.App_HocVien', N'SourceProfileCode') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceMaDK') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceSystem') IS NULL
       OR COL_LENGTH(N'dbo.App_HocVien', N'SourceVersion') IS NULL
    BEGIN
        THROW 51001, 'Pre-check failed: source identity columns are missing.', 1;
    END;

    DECLARE @TotalRows int;
    DECLARE @EmptySourceRows int;
    DECLARE @ConfiguredSourceRows int;
    DECLARE @MissingMaDkRows int;

    SELECT
        @TotalRows = COUNT(1),
        @EmptySourceRows = SUM(CASE
            WHEN SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N'' THEN 1 ELSE 0 END),
        @ConfiguredSourceRows = SUM(CASE
            WHEN SourceProfileCode IS NOT NULL AND LTRIM(RTRIM(SourceProfileCode)) <> N'' THEN 1 ELSE 0 END),
        @MissingMaDkRows = SUM(CASE
            WHEN MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N'' THEN 1 ELSE 0 END)
    FROM dbo.App_HocVien;

    IF @TotalRows <> 1970
    BEGIN
        THROW 51002, 'Pre-check failed: dbo.App_HocVien total row count is not 1970.', 1;
    END;

    IF @EmptySourceRows <> 1970
    BEGIN
        THROW 51003, 'Pre-check failed: SourceProfileCode NULL/empty row count is not 1970.', 1;
    END;

    IF @ConfiguredSourceRows <> 0
    BEGIN
        THROW 51004, 'Pre-check failed: some rows already have SourceProfileCode configured.', 1;
    END;

    IF @MissingMaDkRows <> 0
    BEGIN
        THROW 51005, 'Pre-check failed: some rows have MaDK NULL/empty.', 1;
    END;

    UPDATE dbo.App_HocVien
    SET
        SourceProfileCode = N'DATA_V2',
        SourceMaDK = MaDK,
        SourceSystem = N'V2'
        -- SourceVersion intentionally remains NULL until a versioning convention is approved.
    WHERE SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N'';

    DECLARE @UpdatedRows int = @@ROWCOUNT;
    IF @UpdatedRows <> 1970
    BEGIN
        THROW 51006, 'Post-check failed: updated row count is not 1970.', 1;
    END;

    DECLARE @PostTotalRows int;
    DECLARE @DataV2Rows int;
    DECLARE @SourceMaDkMatches int;
    DECLARE @V2SystemRows int;
    DECLARE @PostEmptySourceRows int;

    SELECT
        @PostTotalRows = COUNT(1),
        @DataV2Rows = SUM(CASE WHEN SourceProfileCode = N'DATA_V2' THEN 1 ELSE 0 END),
        @SourceMaDkMatches = SUM(CASE WHEN SourceMaDK = MaDK THEN 1 ELSE 0 END),
        @V2SystemRows = SUM(CASE WHEN SourceSystem = N'V2' THEN 1 ELSE 0 END),
        @PostEmptySourceRows = SUM(CASE
            WHEN SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N'' THEN 1 ELSE 0 END)
    FROM dbo.App_HocVien;

    IF @PostTotalRows <> 1970
    BEGIN
        THROW 51007, 'Post-check failed: total row count changed.', 1;
    END;

    IF @DataV2Rows <> 1970
    BEGIN
        THROW 51008, 'Post-check failed: SourceProfileCode DATA_V2 row count is not 1970.', 1;
    END;

    IF @SourceMaDkMatches <> 1970
    BEGIN
        THROW 51009, 'Post-check failed: SourceMaDK does not match MaDK for all rows.', 1;
    END;

    IF @V2SystemRows <> 1970
    BEGIN
        THROW 51010, 'Post-check failed: SourceSystem V2 row count is not 1970.', 1;
    END;

    IF @PostEmptySourceRows <> 0
    BEGIN
        THROW 51011, 'Post-check failed: SourceProfileCode still has NULL/empty rows.', 1;
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
