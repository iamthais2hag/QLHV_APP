/*
    Patch: Add MaHangDT to dbo.App_HocVien
    Safety:
    - Do not run against production unless reviewed and approved.
    - This script is idempotent.
    - This file is documentation/preparation only; Codex must not execute it.
*/

IF COL_LENGTH('dbo.App_HocVien', 'MaHangDT') IS NULL
BEGIN
    ALTER TABLE dbo.App_HocVien
        ADD MaHangDT NVARCHAR(20) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_HocVien_MaHangDT'
      AND object_id = OBJECT_ID(N'dbo.App_HocVien')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_HocVien_MaHangDT
        ON dbo.App_HocVien(MaHangDT)
        WHERE IsDeleted = 0;
END
GO
