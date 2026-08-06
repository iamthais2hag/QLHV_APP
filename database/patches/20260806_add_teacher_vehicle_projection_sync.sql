SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
USE [QLHV_APP];

IF DB_NAME() <> N'QLHV_APP'
    THROW 532700, 'TVP_TARGET_DATABASE_REJECTED', 1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid=CONVERT(uniqueidentifier,N'9C44B304-8A84-4D0D-9A82-19C7233FF6BB')
)
    THROW 532701, 'TVP_TARGET_DATABASE_GUID_REJECTED', 1;
IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap',N'U') IS NULL OR
   OBJECT_ID(N'dbo.App_GiaoVien',N'U') IS NULL OR
   OBJECT_ID(N'dbo.App_XeTap',N'U') IS NULL OR
   OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien',N'U') IS NULL
    THROW 532702, 'TVP_TARGET_PREREQUISITE_MISSING', 1;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceProfileCode') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD SourceProfileCode nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceMaLichSD') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD SourceMaLichSD bigint NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceMaKhoaHoc') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD SourceMaKhoaHoc nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceBienSoXe') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD SourceBienSoXe nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceHash') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD SourceHash nvarchar(64) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceMaHocVien') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD SourceMaHocVien nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'DiaDiem') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD DiaDiem nvarchar(255) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'TenHocVien') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD TenHocVien nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'IsKhoaHocXeTap') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD IsKhoaHocXeTap bit NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'TrangThaiNguon') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD TrangThaiNguon bit NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'CreatedAtUtc') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD CreatedAtUtc datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'UpdatedAtUtc') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap ADD UpdatedAtUtc datetime2(7) NULL;

GO

IF OBJECT_ID(N'dbo.CK_App_KhoaHoc_XeTap_TvpSourceIdentity',N'C') IS NULL
    ALTER TABLE dbo.App_KhoaHoc_XeTap WITH CHECK ADD CONSTRAINT CK_App_KhoaHoc_XeTap_TvpSourceIdentity CHECK
    (
        (SourceProfileCode IS NULL AND SourceMaLichSD IS NULL AND SourceMaKhoaHoc IS NULL AND SourceBienSoXe IS NULL AND SourceHash IS NULL)
        OR
        (SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO') AND SourceMaLichSD>0 AND
         NULLIF(LTRIM(RTRIM(SourceMaKhoaHoc)),N'') IS NOT NULL AND
         NULLIF(LTRIM(RTRIM(SourceBienSoXe)),N'') IS NOT NULL AND
         SourceHash LIKE REPLICATE(N'[0-9a-f]',64))
    );

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc_XeTap') AND name=N'UX_App_KhoaHoc_XeTap_TvpSourceIdentity')
    CREATE UNIQUE INDEX UX_App_KhoaHoc_XeTap_TvpSourceIdentity
      ON dbo.App_KhoaHoc_XeTap(SourceProfileCode,SourceMaLichSD)
      WHERE SourceProfileCode IS NOT NULL AND SourceMaLichSD IS NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc_XeTap') AND name=N'IX_App_KhoaHoc_XeTap_TvpLookup')
    CREATE INDEX IX_App_KhoaHoc_XeTap_TvpLookup
      ON dbo.App_KhoaHoc_XeTap(SourceProfileCode,SourceMaKhoaHoc,SourceBienSoXe,IsDeleted)
      INCLUDE(SourceHash,TrangThaiNguon);

IF OBJECT_ID(N'dbo.App_TeacherVehicleProjectionCheckpoint',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_TeacherVehicleProjectionCheckpoint
    (
        SourceProfileCode nvarchar(50) NOT NULL,
        DomainName nvarchar(50) NOT NULL,
        ContractVersion nvarchar(20) NOT NULL,
        SourceDatabaseGuid uniqueidentifier NOT NULL,
        LastCtVersion bigint NOT NULL,
        MappingFingerprint char(64) NOT NULL,
        SourceSchemaFingerprint char(64) NOT NULL,
        State nvarchar(20) NOT NULL,
        LastCycleId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_App_TvpCheckpoint_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_TeacherVehicleProjectionCheckpoint PRIMARY KEY(SourceProfileCode,DomainName,ContractVersion),
        CONSTRAINT CK_App_TvpCheckpoint_Profile CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_TvpCheckpoint_Domain CHECK(DomainName IN(N'TEACHER',N'COURSE_TEACHER',N'COURSE_VEHICLE')),
        CONSTRAINT CK_App_TvpCheckpoint_State CHECK(State IN(N'ACTIVE',N'BLOCKED')),
        CONSTRAINT CK_App_TvpCheckpoint_Version CHECK(LastCtVersion>=0),
        CONSTRAINT CK_App_TvpCheckpoint_Hashes CHECK(
          MappingFingerprint LIKE REPLICATE('[0-9a-f]',64) AND
          SourceSchemaFingerprint LIKE REPLICATE('[0-9a-f]',64))
    );
END;

IF OBJECT_ID(N'dbo.App_TeacherVehicleProjectionCycle',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_TeacherVehicleProjectionCycle
    (
        CycleId uniqueidentifier NOT NULL,
        SourceProfileCode nvarchar(50) NOT NULL,
        DomainName nvarchar(50) NOT NULL,
        ContractVersion nvarchar(20) NOT NULL,
        SourceDatabaseGuid uniqueidentifier NOT NULL,
        FromCtVersion bigint NOT NULL,
        ToCtVersion bigint NOT NULL,
        MappingFingerprint char(64) NOT NULL,
        SourceSchemaFingerprint char(64) NOT NULL,
        SourceRows int NOT NULL,
        InsertedRows int NOT NULL,
        UpdatedRows int NOT NULL,
        InactiveRows int NOT NULL,
        NoChangeRows int NOT NULL,
        VerificationHash char(64) NOT NULL,
        ArtifactSha256 char(64) NULL,
        Outcome nvarchar(40) NOT NULL,
        StartedAtUtc datetime2(7) NOT NULL,
        CompletedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT PK_App_TeacherVehicleProjectionCycle PRIMARY KEY(CycleId,DomainName),
        CONSTRAINT CK_App_TvpCycle_Profile CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_TvpCycle_Domain CHECK(DomainName IN(N'TEACHER',N'VEHICLE',N'COURSE_TEACHER',N'COURSE_VEHICLE')),
        CONSTRAINT CK_App_TvpCycle_Versions CHECK(FromCtVersion>=0 AND ToCtVersion>=FromCtVersion),
        CONSTRAINT CK_App_TvpCycle_Counts CHECK(SourceRows>=0 AND InsertedRows>=0 AND UpdatedRows>=0 AND InactiveRows>=0 AND NoChangeRows>=0)
    );
    CREATE INDEX IX_App_TvpCycle_ProfileDomainVersion
      ON dbo.App_TeacherVehicleProjectionCycle(SourceProfileCode,DomainName,ToCtVersion DESC);
END;

COMMIT TRANSACTION;

IF COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceProfileCode') IS NULL OR
   COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceMaLichSD') IS NULL OR
   COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'SourceMaHocVien') IS NULL OR
   COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'DiaDiem') IS NULL OR
   COL_LENGTH(N'dbo.App_KhoaHoc_XeTap',N'TenHocVien') IS NULL OR
   OBJECT_ID(N'dbo.App_TeacherVehicleProjectionCheckpoint',N'U') IS NULL OR
   OBJECT_ID(N'dbo.App_TeacherVehicleProjectionCycle',N'U') IS NULL
    THROW 532703, 'TVP_TARGET_SCHEMA_VERIFY_FAILED', 1;

SELECT N'TEACHER_VEHICLE_PROJECTION_TARGET_SCHEMA_PASS' AS Marker;
