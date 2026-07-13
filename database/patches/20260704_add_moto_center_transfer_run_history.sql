/*
Task 5 B3W145-B3W152 - Moto center-transfer run history.

WARNING:
- Review before running.
- Run on QLHV_APP_TEST first only.
- Do not run on production without owner approval.
- This script creates only the app-side audit table for Moto center-transfer TEST attempts.
- It must not store raw confirm text, database passwords, connection strings, or other secrets.
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

IF OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_MotoCenterTransferRunHistory
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_App_MotoCenterTransferRunHistory PRIMARY KEY,
        SourceProfileCode nvarchar(50) NOT NULL,
        TargetProfileCode nvarchar(50) NOT NULL,
        MaKhoaHocCu nvarchar(50) NOT NULL,
        MaKhoaHocMoi nvarchar(50) NULL,
        MaCSDTCu nvarchar(20) NOT NULL,
        MaCSDTMoi nvarchar(20) NOT NULL,
        MaSoGTVTMoi nvarchar(20) NULL,
        ConfirmTextMatched bit NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_ConfirmTextMatched DEFAULT 0,
        Executed bit NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_Executed DEFAULT 0,
        Status nvarchar(50) NOT NULL,
        Message nvarchar(1000) NULL,
        CopiedKhoaHoc bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_CopiedKhoaHoc DEFAULT 0,
        CopiedBaoCaoI bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_CopiedBaoCaoI DEFAULT 0,
        CopiedNguoiLX bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_CopiedNguoiLX DEFAULT 0,
        CopiedNguoiLXHoSo bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_CopiedNguoiLXHoSo DEFAULT 0,
        CopiedNguoiLXHSGiayTo bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_CopiedNguoiLXHSGiayTo DEFAULT 0,
        UpdatedNguoiLXHoSo bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_UpdatedNguoiLXHoSo DEFAULT 0,
        UpdatedNguoiLX bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_UpdatedNguoiLX DEFAULT 0,
        UpdatedKhoaHoc bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_UpdatedKhoaHoc DEFAULT 0,
        UpdatedBaoCaoI bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_UpdatedBaoCaoI DEFAULT 0,
        UpdatedNguoiLXHSGiayTo bigint NOT NULL
            CONSTRAINT DF_App_MotoCenterTransferRunHistory_UpdatedNguoiLXHSGiayTo DEFAULT 0,
        TargetKhoaHocMoiCountAfter bigint NULL,
        TargetBaoCaoIMoiCountAfter bigint NULL,
        TargetNguoiLXHoSoMoiCountAfter bigint NULL,
        TargetNguoiLXHSGiayToMoiCountAfter bigint NULL,
        TargetNguoiLXMoiCountAfter bigint NULL,
        DurationMs bigint NULL,
        StartedAt datetime2(7) NOT NULL,
        EndedAt datetime2(7) NULL,
        PlanJson nvarchar(max) NULL,
        SummaryJson nvarchar(max) NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U')
         AND name = N'IX_App_MotoCenterTransferRunHistory_StartedAt'
   )
BEGIN
    CREATE INDEX IX_App_MotoCenterTransferRunHistory_StartedAt
        ON dbo.App_MotoCenterTransferRunHistory (StartedAt DESC, Id DESC);
END;
GO

IF OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U')
         AND name = N'IX_App_MotoCenterTransferRunHistory_MaKhoaHocCu'
   )
BEGIN
    CREATE INDEX IX_App_MotoCenterTransferRunHistory_MaKhoaHocCu
        ON dbo.App_MotoCenterTransferRunHistory (MaKhoaHocCu, StartedAt DESC, Id DESC);
END;
GO

IF OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U')
         AND name = N'IX_App_MotoCenterTransferRunHistory_MaKhoaHocMoi'
   )
BEGIN
    CREATE INDEX IX_App_MotoCenterTransferRunHistory_MaKhoaHocMoi
        ON dbo.App_MotoCenterTransferRunHistory (MaKhoaHocMoi, StartedAt DESC, Id DESC);
END;
GO

IF OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.App_MotoCenterTransferRunHistory', N'U')
         AND name = N'IX_App_MotoCenterTransferRunHistory_MaCSDT'
   )
BEGIN
    CREATE INDEX IX_App_MotoCenterTransferRunHistory_MaCSDT
        ON dbo.App_MotoCenterTransferRunHistory (MaCSDTCu, MaCSDTMoi, StartedAt DESC, Id DESC);
END;
GO
