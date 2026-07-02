/*
Task 5 B3W89-B3W96 - Moto TEST sync run history.

WARNING:
- Review before running.
- Run on QLHV_APP_TEST first only.
- Do not run on production without owner approval.
- This script creates only the app-side audit table for Moto TEST sync attempts.
- It must not store database passwords, connection strings, or other secrets.
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

IF OBJECT_ID(N'dbo.App_MotoSyncRunHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_MotoSyncRunHistory
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_App_MotoSyncRunHistory PRIMARY KEY,
        CreatedAt datetime2(7) NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_CreatedAt DEFAULT SYSUTCDATETIME(),
        Direction nvarchar(20) NOT NULL,
        SyncMode nvarchar(30) NOT NULL,
        SourceProfileCode nvarchar(50) NOT NULL,
        TargetProfileCode nvarchar(50) NOT NULL,
        MaKhoaHoc nvarchar(50) NULL,
        ConfirmTextMatched bit NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_ConfirmTextMatched DEFAULT 0,
        Executed bit NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_Executed DEFAULT 0,
        Status nvarchar(50) NOT NULL,
        Message nvarchar(1000) NULL,
        InsertedKhoaHoc bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_InsertedKhoaHoc DEFAULT 0,
        InsertedBaoCaoI bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_InsertedBaoCaoI DEFAULT 0,
        InsertedNguoiLX bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_InsertedNguoiLX DEFAULT 0,
        InsertedNguoiLXGPLX bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_InsertedNguoiLXGPLX DEFAULT 0,
        InsertedNguoiLXHoSo bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_InsertedNguoiLXHoSo DEFAULT 0,
        InsertedGiayTo bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_InsertedGiayTo DEFAULT 0,
        UpdatedNguoiLX bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_UpdatedNguoiLX DEFAULT 0,
        UpdatedNguoiLXHoSo bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_UpdatedNguoiLXHoSo DEFAULT 0,
        UpdatedRows bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_UpdatedRows DEFAULT 0,
        DeletedRows bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_DeletedRows DEFAULT 0,
        DurationMs bigint NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_DurationMs DEFAULT 0,
        StartedAt datetime2(7) NOT NULL,
        EndedAt datetime2(7) NOT NULL,
        HasRemainingWork bit NOT NULL
            CONSTRAINT DF_App_MotoSyncRunHistory_HasRemainingWork DEFAULT 0,
        BeforePlanJson nvarchar(max) NULL,
        AfterPlanJson nvarchar(max) NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.App_MotoSyncRunHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.App_MotoSyncRunHistory', N'U')
         AND name = N'IX_App_MotoSyncRunHistory_StartedAt'
   )
BEGIN
    CREATE INDEX IX_App_MotoSyncRunHistory_StartedAt
        ON dbo.App_MotoSyncRunHistory (StartedAt DESC, Id DESC);
END;
GO

IF OBJECT_ID(N'dbo.App_MotoSyncRunHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.App_MotoSyncRunHistory', N'U')
         AND name = N'IX_App_MotoSyncRunHistory_Filter'
   )
BEGIN
    CREATE INDEX IX_App_MotoSyncRunHistory_Filter
        ON dbo.App_MotoSyncRunHistory
        (
            MaKhoaHoc,
            Direction,
            SyncMode,
            StartedAt DESC,
            Id DESC
        );
END;
GO
