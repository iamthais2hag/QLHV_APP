USE [$(Rt03TargetDatabase)];
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

CREATE TABLE dbo.App_KhoaHoc
(
    KhoaHocId bigint IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_App_KhoaHoc PRIMARY KEY,
    MaKhoa nvarchar(50) NOT NULL,
    SourceProfileCode nvarchar(50) NULL,
    SourceMaKhoaHoc nvarchar(50) NULL,
    SourceHash nvarchar(64) NULL,
    IsDeleted bit NOT NULL
        CONSTRAINT DF_App_KhoaHoc_IsDeleted DEFAULT (0),
    TrangThaiNguon bit NULL,
    CONSTRAINT UQ_App_KhoaHoc_MaKhoa UNIQUE (MaKhoa)
);

CREATE UNIQUE NONCLUSTERED INDEX UX_App_KhoaHoc_SourceIdentity
    ON dbo.App_KhoaHoc(SourceProfileCode, SourceMaKhoaHoc)
    WHERE SourceProfileCode IS NOT NULL
      AND SourceMaKhoaHoc IS NOT NULL;

CREATE TABLE dbo.App_QlhvDirectRealtimeCycleHistory
(
    CycleId uniqueidentifier NOT NULL
        CONSTRAINT PK_App_QlhvDirectRealtimeCycleHistory PRIMARY KEY,
    InsertedRows int NOT NULL,
    UpdatedRows int NOT NULL,
    DeletedOrDeactivatedRows int NOT NULL,
    DuplicateActiveRows int NOT NULL,
    CheckpointBefore bigint NOT NULL,
    CheckpointAfter bigint NOT NULL,
    CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Mutations
        CHECK
        (
            InsertedRows BETWEEN 0 AND 1
            AND UpdatedRows BETWEEN 0 AND 1
            AND InsertedRows + UpdatedRows <= 1
            AND DeletedOrDeactivatedRows = 0
            AND DuplicateActiveRows = 0
            AND CheckpointAfter >= CheckpointBefore
        )
);

SELECT N'RT03_KHOAHOC_REHEARSAL_BASELINE_CREATED' AS Evidence;
