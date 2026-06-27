/*
    QLHV_APP - App_HocVien multi-source identity design
    Task 5 B3W6

    This patch is phase 1 only and must be reviewed before manual execution.

    Safety:
    - Does not delete data.
    - Does not update/backfill existing rows.
    - Does not drop existing UQ_App_HocVien_MaDK.
    - Does not change current sync behavior.
    - Keeps new source identity columns nullable until backfill/code migration is approved.
*/

IF COL_LENGTH(N'dbo.App_HocVien', N'SourceProfileCode') IS NULL
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD SourceProfileCode NVARCHAR(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.App_HocVien', N'SourceMaDK') IS NULL
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD SourceMaDK NVARCHAR(50) NULL;
END;
GO

IF COL_LENGTH(N'dbo.App_HocVien', N'SourceSystem') IS NULL
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD SourceSystem NVARCHAR(30) NULL;
END;
GO

IF COL_LENGTH(N'dbo.App_HocVien', N'SourceVersion') IS NULL
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD SourceVersion NVARCHAR(30) NULL;
END;
GO

IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_App_HocVien_SourceProfileCode'
      AND parent_object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
)
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD CONSTRAINT CK_App_HocVien_SourceProfileCode
            CHECK (
                SourceProfileCode IS NULL
                OR SourceProfileCode IN (N'DATA_V1', N'DATA_V2')
            );
END;
GO

IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_App_HocVien_SourceSystem'
      AND parent_object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
)
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD CONSTRAINT CK_App_HocVien_SourceSystem
            CHECK (
                SourceSystem IS NULL
                OR SourceSystem IN (N'V1', N'V2')
            );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_HocVien_SourceProfileCode_MaDK'
      AND object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_HocVien_SourceProfileCode_MaDK
    ON dbo.App_HocVien(SourceProfileCode, MaDK)
    INCLUDE (HocVienId, SourceMaDK, SourceSystem, V2RowHash, LastSyncStatus)
    WHERE IsDeleted = 0 AND SourceProfileCode IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_HocVien_SourceProfileCode_SourceMaDK'
      AND object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_HocVien_SourceProfileCode_SourceMaDK
    ON dbo.App_HocVien(SourceProfileCode, SourceMaDK)
    INCLUDE (HocVienId, MaDK, SourceSystem, V2RowHash, LastSyncStatus)
    WHERE IsDeleted = 0 AND SourceProfileCode IS NOT NULL AND SourceMaDK IS NOT NULL;
END;
GO

/*
    Phase 2 TODO after backfill and code migration:
    - Backfill SourceProfileCode/SourceMaDK/SourceSystem for existing rows after source ownership is confirmed.
    - Update sync staging and MERGE to match by SourceProfileCode + MaDK or SourceProfileCode + SourceMaDK.
    - Review/drop or replace UQ_App_HocVien_MaDK if duplicate MaDK across DATA_V1/DATA_V2 must be allowed.
    - Add unique composite index only after data is clean, for example:
        UNIQUE (SourceProfileCode, MaDK) WHERE IsDeleted = 0 AND SourceProfileCode IS NOT NULL
      or:
        UNIQUE (SourceProfileCode, SourceMaDK) WHERE IsDeleted = 0 AND SourceProfileCode IS NOT NULL AND SourceMaDK IS NOT NULL
*/
