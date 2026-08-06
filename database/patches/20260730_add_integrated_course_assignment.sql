/*
    Integrated course / training-group / learner-assignment schema.

    PRODUCTION STATUS: NOT APPLIED BY THIS TASK.

    This patch creates only QLHV-owned data and metadata. It does not read from
    or write to CSDL_OTO/CSDL_MOTO, run Auto Sync, move a realtime checkpoint,
    or use a row count as a deployment guard.

    App_GiaoVien_hs.HoTenSearch is an application-normalized value. The
    application must derive it deterministically from HoTen using the versioned
    assignment text normalizer before every insert/update. SQL enforces a
    nonblank, uppercase, whitespace-normalized storage contract.
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

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @ExpectedProductionDatabaseGuid uniqueidentifier =
    '9C44B304-8A84-4D0D-9A82-19C7233FF6BB';
DECLARE @ObservedDatabaseGuid uniqueidentifier =
(
    SELECT database_guid
    FROM sys.database_recovery_status
    WHERE database_id = DB_ID()
);
DECLARE @IsIsolatedRehearsal bit =
    CASE WHEN DB_NAME() LIKE N'QLHV[_]ASSIGNMENT[_]REHEARSAL[_]%' THEN 1 ELSE 0 END;

IF DB_NAME() <> N'QLHV_APP' AND @IsIsolatedRehearsal = 0
    THROW 529300, 'Wrong database. Expected QLHV_APP or an explicitly named isolated assignment rehearsal database.', 1;

IF DB_NAME() = N'QLHV_APP'
   AND (@ObservedDatabaseGuid IS NULL
        OR @ObservedDatabaseGuid <> @ExpectedProductionDatabaseGuid)
    THROW 529301, 'QLHV_APP database identity mismatch.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
)
    THROW 529302, 'Target database must be ONLINE and read-write.', 1;

IF CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation'))
   <> N'SQL_Latin1_General_CP1_CI_AS'
    THROW 529303, 'Target database collation mismatch.', 1;
GO

IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_KhoaHoc', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_GiaoVien', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_XeTap', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_ImportBatch', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_AuditLog', N'U') IS NULL
    THROW 529304, 'Required QLHV_APP source/import tables are missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'App_HocVien', N'HocVienId'),
            (N'App_KhoaHoc', N'KhoaHocId'),
            (N'App_GiaoVien', N'GiaoVienId'),
            (N'App_XeTap', N'XeTapId'),
            (N'App_ImportBatch', N'ImportBatchId')
    ) AS required(TableName, ColumnName)
    OUTER APPLY
    (
        SELECT
            columnItem.system_type_id,
            columnItem.max_length,
            columnItem.precision,
            columnItem.scale,
            columnItem.is_nullable,
            columnItem.is_identity
        FROM sys.columns AS columnItem
        WHERE columnItem.object_id =
              OBJECT_ID(N'dbo.' + required.TableName, N'U')
          AND columnItem.name = required.ColumnName
    ) AS observed
    WHERE observed.system_type_id IS NULL
       OR observed.system_type_id <> 127       -- BIGINT
       OR observed.max_length <> 8
       OR observed.precision <> 19
       OR observed.scale <> 0
       OR observed.is_nullable <> 0
       OR observed.is_identity <> 1
)
    THROW 529305, 'Referenced primary-key type/identity precondition failed.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND name = N'EntityType'
      AND system_type_id = 231                 -- NVARCHAR
      AND max_length = 100                     -- NVARCHAR(50)
      AND is_nullable = 0
      AND collation_name = N'SQL_Latin1_General_CP1_CI_AS'
)
    THROW 529306, 'App_ImportBatch.EntityType must remain NVARCHAR(50) NOT NULL with the production collation.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM
    (
        VALUES
            (N'App_HocVien'),
            (N'App_KhoaHoc'),
            (N'App_GiaoVien'),
            (N'App_XeTap'),
            (N'App_ImportBatch')
    ) AS required(TableName)
    JOIN sys.columns AS columnItem
      ON columnItem.object_id =
         OBJECT_ID(N'dbo.' + required.TableName, N'U')
     AND columnItem.name = N'RowVersion'
     AND columnItem.system_type_id = 189        -- ROWVERSION/TIMESTAMP
     AND columnItem.max_length = 8
     AND columnItem.is_nullable = 0
) <> 5
    THROW 529307, 'Required source/import RowVersion contract failed.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
      AND name = N'MaKhoa'
      AND system_type_id = 231
      AND max_length = 100
      AND collation_name = N'SQL_Latin1_General_CP1_CI_AS'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
      AND name = N'SourceProfileCode'
      AND system_type_id = 231
      AND max_length = 100
      AND collation_name = N'SQL_Latin1_General_CP1_CI_AS'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc', N'U')
      AND name = N'MaKhoa'
      AND system_type_id = 231
      AND max_length = 100
      AND is_nullable = 0
      AND collation_name = N'SQL_Latin1_General_CP1_CI_AS'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc', N'U')
      AND name = N'SourceProfileCode'
      AND system_type_id = 231
      AND max_length = 100
      AND collation_name = N'SQL_Latin1_General_CP1_CI_AS'
)
    THROW 529308, 'Course/profile identity column precondition failed.', 1;

IF OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.App_AssignmentOperation', N'U') IS NOT NULL
    THROW 529309, 'One or more assignment-owned tables already exist; partial/repeat migration is rejected.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND name IN
      (
          N'FileSha256',
          N'TemplateVersion',
          N'NormalizationVersion',
          N'PreviewExpiresAtUtc',
          N'ConfirmedAtUtc',
          N'IdempotencyKey'
      )
)
    THROW 529310, 'One or more assignment import columns already exist; partial/repeat migration is rejected.', 1;

IF DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole') IS NOT NULL
    THROW 529312, 'QLHV_AssignmentApiRole already exists; partial/repeat migration is rejected.', 1;

IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
   AND EXISTS
(
    SELECT 1
    FROM sys.database_permissions
    WHERE grantee_principal_id =
          DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker')
      AND class = 1
      AND major_id = OBJECT_ID(N'dbo.App_ImportBatch', N'U')
      AND permission_name IN (N'INSERT', N'UPDATE', N'DELETE')
)
    THROW 529311, 'Realtime principal already has an explicit App_ImportBatch DML permission; exact reversible DENY cannot be established.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE ROLE [QLHV_AssignmentApiRole] AUTHORIZATION [dbo];

    CREATE TABLE dbo.App_GiaoVien_hs
    (
        GiaoVienHsId       BIGINT IDENTITY(1,1) NOT NULL,
        MaGiaoVienHs       NVARCHAR(50) NOT NULL,
        HoTen              NVARCHAR(255) NOT NULL,
        HoTenSearch        NVARCHAR(255) NOT NULL,
        NgaySinh           DATE NULL,
        SoCCCD             NVARCHAR(20) NULL,
        TrangThai          VARCHAR(20) NOT NULL
            CONSTRAINT DF_App_GiaoVien_hs_TrangThai DEFAULT ('ACTIVE'),
        GhiChu             NVARCHAR(1000) NULL,
        IsDeleted          BIT NOT NULL
            CONSTRAINT DF_App_GiaoVien_hs_IsDeleted DEFAULT (0),
        DeletedAt          DATETIME2(7) NULL,
        DeletedBy          NVARCHAR(100) NULL,
        DeleteReason       NVARCHAR(500) NULL,
        CreatedAt          DATETIME2(7) NOT NULL
            CONSTRAINT DF_App_GiaoVien_hs_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy          NVARCHAR(100) NOT NULL,
        UpdatedAt          DATETIME2(7) NULL,
        UpdatedBy          NVARCHAR(100) NULL,
        RowVersion         ROWVERSION NOT NULL,

        CONSTRAINT PK_App_GiaoVien_hs
            PRIMARY KEY CLUSTERED (GiaoVienHsId),
        CONSTRAINT UQ_App_GiaoVien_hs_MaGiaoVienHs
            UNIQUE NONCLUSTERED (MaGiaoVienHs),
        CONSTRAINT CK_App_GiaoVien_hs_CodeNameNormalization CHECK
        (
            LEN(LTRIM(RTRIM(MaGiaoVienHs))) > 0
            AND DATALENGTH(MaGiaoVienHs) =
                DATALENGTH(LTRIM(RTRIM(MaGiaoVienHs)))
            AND LEN(LTRIM(RTRIM(HoTen))) > 0
            AND DATALENGTH(HoTen) = DATALENGTH(LTRIM(RTRIM(HoTen)))
            AND LEN(LTRIM(RTRIM(HoTenSearch))) > 0
            AND DATALENGTH(HoTenSearch) =
                DATALENGTH(LTRIM(RTRIM(HoTenSearch)))
            AND HoTenSearch NOT LIKE N'%  %'
            AND HoTenSearch COLLATE Latin1_General_100_BIN2 =
                UPPER(HoTenSearch) COLLATE Latin1_General_100_BIN2
        ),
        CONSTRAINT CK_App_GiaoVien_hs_TrangThai CHECK
            (TrangThai IN ('ACTIVE', 'INACTIVE')),
        CONSTRAINT CK_App_GiaoVien_hs_SoCCCD CHECK
        (
            SoCCCD IS NULL
            OR
            (
                LEN(SoCCCD) IN (9, 12)
                AND DATALENGTH(SoCCCD) =
                    DATALENGTH(LTRIM(RTRIM(SoCCCD)))
                AND SoCCCD COLLATE Latin1_General_100_BIN2
                    NOT LIKE N'%[^0-9]%'
            )
        ),
        CONSTRAINT CK_App_GiaoVien_hs_SoftDelete CHECK
        (
            (
                IsDeleted = 0
                AND DeletedAt IS NULL
                AND DeletedBy IS NULL
                AND DeleteReason IS NULL
            )
            OR
            (
                IsDeleted = 1
                AND DeletedAt IS NOT NULL
                AND DeletedBy IS NOT NULL
                AND LEN(LTRIM(RTRIM(DeletedBy))) > 0
                AND DATALENGTH(DeletedBy) =
                    DATALENGTH(LTRIM(RTRIM(DeletedBy)))
                AND DeleteReason IS NOT NULL
                AND LEN(LTRIM(RTRIM(DeleteReason))) > 0
                AND DATALENGTH(DeleteReason) =
                    DATALENGTH(LTRIM(RTRIM(DeleteReason)))
            )
        ),
        CONSTRAINT CK_App_GiaoVien_hs_Audit CHECK
        (
            LEN(LTRIM(RTRIM(CreatedBy))) > 0
            AND DATALENGTH(CreatedBy) =
                DATALENGTH(LTRIM(RTRIM(CreatedBy)))
            AND
            (
                (UpdatedAt IS NULL AND UpdatedBy IS NULL)
                OR
                (
                    UpdatedAt IS NOT NULL
                    AND UpdatedBy IS NOT NULL
                    AND LEN(LTRIM(RTRIM(UpdatedBy))) > 0
                    AND DATALENGTH(UpdatedBy) =
                        DATALENGTH(LTRIM(RTRIM(UpdatedBy)))
                )
            )
        )
    );

    CREATE UNIQUE INDEX UX_App_GiaoVien_hs_SoCCCD
        ON dbo.App_GiaoVien_hs(SoCCCD)
        WHERE SoCCCD IS NOT NULL;

    CREATE INDEX IX_App_GiaoVien_hs_ActiveSearch
        ON dbo.App_GiaoVien_hs(HoTenSearch, MaGiaoVienHs)
        INCLUDE(HoTen, TrangThai, NgaySinh)
        WHERE IsDeleted = 0;

    CREATE TABLE dbo.App_KhoaHoc_NhomDaoTao
    (
        NhomDaoTaoId          BIGINT IDENTITY(1,1) NOT NULL,
        KhoaHocId             BIGINT NOT NULL,
        MaNhom                NVARCHAR(50) NOT NULL,
        TenNhom               NVARCHAR(255) NOT NULL,
        ThuTu                 INT NOT NULL
            CONSTRAINT DF_App_KHNDT_ThuTu DEFAULT (0),
        GiaoVienDungLopId     BIGINT NULL,
        XeTapId               BIGINT NULL,
        XeBaiSo10Id           BIGINT NULL,
        TrangThai             VARCHAR(20) NOT NULL
            CONSTRAINT DF_App_KHNDT_TrangThai DEFAULT ('ACTIVE'),
        GhiChu                NVARCHAR(1000) NULL,
        CreatedAt             DATETIME2(7) NOT NULL
            CONSTRAINT DF_App_KHNDT_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy             NVARCHAR(100) NOT NULL,
        UpdatedAt             DATETIME2(7) NULL,
        UpdatedBy             NVARCHAR(100) NULL,
        RowVersion            ROWVERSION NOT NULL,

        CONSTRAINT PK_App_KhoaHoc_NhomDaoTao
            PRIMARY KEY CLUSTERED (NhomDaoTaoId),
        CONSTRAINT UQ_App_KHNDT_KhoaHoc_MaNhom
            UNIQUE NONCLUSTERED (KhoaHocId, MaNhom),
        CONSTRAINT FK_App_KHNDT_KhoaHoc
            FOREIGN KEY (KhoaHocId)
            REFERENCES dbo.App_KhoaHoc(KhoaHocId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_KHNDT_GiaoVienDungLop
            FOREIGN KEY (GiaoVienDungLopId)
            REFERENCES dbo.App_GiaoVien(GiaoVienId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_KHNDT_XeTap
            FOREIGN KEY (XeTapId)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_KHNDT_XeBaiSo10
            FOREIGN KEY (XeBaiSo10Id)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT CK_App_KHNDT_CodeNameNormalization CHECK
        (
            LEN(LTRIM(RTRIM(MaNhom))) > 0
            AND DATALENGTH(MaNhom) = DATALENGTH(LTRIM(RTRIM(MaNhom)))
            AND LEN(LTRIM(RTRIM(TenNhom))) > 0
            AND DATALENGTH(TenNhom) = DATALENGTH(LTRIM(RTRIM(TenNhom)))
        ),
        CONSTRAINT CK_App_KHNDT_ThuTu CHECK (ThuTu >= 0),
        CONSTRAINT CK_App_KHNDT_TrangThai CHECK
            (TrangThai IN ('ACTIVE', 'INACTIVE')),
        CONSTRAINT CK_App_KHNDT_Audit CHECK
        (
            LEN(LTRIM(RTRIM(CreatedBy))) > 0
            AND DATALENGTH(CreatedBy) =
                DATALENGTH(LTRIM(RTRIM(CreatedBy)))
            AND
            (
                (UpdatedAt IS NULL AND UpdatedBy IS NULL)
                OR
                (
                    UpdatedAt IS NOT NULL
                    AND UpdatedBy IS NOT NULL
                    AND LEN(LTRIM(RTRIM(UpdatedBy))) > 0
                    AND DATALENGTH(UpdatedBy) =
                        DATALENGTH(LTRIM(RTRIM(UpdatedBy)))
                )
            )
        )
    );

    CREATE INDEX IX_App_KHNDT_DisplayOrder
        ON dbo.App_KhoaHoc_NhomDaoTao(KhoaHocId, ThuTu, MaNhom)
        INCLUDE(TenNhom, TrangThai, GiaoVienDungLopId, XeTapId, XeBaiSo10Id);

    ALTER TABLE dbo.App_ImportBatch ADD
        FileSha256 CHAR(64) NULL,
        TemplateVersion VARCHAR(40) NULL,
        NormalizationVersion VARCHAR(40) NULL,
        PreviewExpiresAtUtc DATETIME2(7) NULL,
        ConfirmedAtUtc DATETIME2(7) NULL,
        IdempotencyKey NVARCHAR(100) NULL;

    EXEC(N'
ALTER TABLE dbo.App_ImportBatch WITH CHECK ADD
        CONSTRAINT CK_App_ImportBatch_AssignmentFileSha256 CHECK
        (
            FileSha256 IS NULL
            OR
            (
                LEN(FileSha256) = 64
                AND FileSha256 COLLATE Latin1_General_100_BIN2
                    NOT LIKE ''%[^0-9A-F]%''
            )
        ),
        CONSTRAINT CK_App_ImportBatch_AssignmentVersions CHECK
        (
            (
                TemplateVersion IS NULL
                OR
                (
                    LEN(LTRIM(RTRIM(TemplateVersion))) > 0
                    AND DATALENGTH(TemplateVersion) =
                        DATALENGTH(LTRIM(RTRIM(TemplateVersion)))
                )
            )
            AND
            (
                NormalizationVersion IS NULL
                OR
                (
                    LEN(LTRIM(RTRIM(NormalizationVersion))) > 0
                    AND DATALENGTH(NormalizationVersion) =
                        DATALENGTH(LTRIM(RTRIM(NormalizationVersion)))
                )
            )
        ),
        CONSTRAINT CK_App_ImportBatch_AssignmentPreviewTimes CHECK
        (
            ConfirmedAtUtc IS NULL
            OR PreviewExpiresAtUtc IS NULL
            OR ConfirmedAtUtc <= PreviewExpiresAtUtc
        ),
        CONSTRAINT CK_App_ImportBatch_AssignmentIdempotency CHECK
        (
            IdempotencyKey IS NULL
            OR
            (
                LEN(LTRIM(RTRIM(IdempotencyKey))) > 0
                AND DATALENGTH(IdempotencyKey) =
                DATALENGTH(LTRIM(RTRIM(IdempotencyKey)))
            )
        );');

    EXEC(N'
CREATE UNIQUE INDEX UX_App_ImportBatch_AssignmentIdempotency
        ON dbo.App_ImportBatch(EntityType, IdempotencyKey)
        WHERE EntityType = N''HOCVIEN_ASSIGNMENT''
          AND IdempotencyKey IS NOT NULL;');

    CREATE TABLE dbo.App_HocVien_PhanCong
    (
        PhanCongId                       BIGINT IDENTITY(1,1) NOT NULL,
        HocVienId                        BIGINT NOT NULL,
        NhomDaoTaoId                     BIGINT NULL,
        GiaoVienHoSoId                   BIGINT NULL,
        GiaoVienDungLopId                BIGINT NULL,
        XeTapId                          BIGINT NULL,
        XeBaiSo10Id                      BIGINT NULL,
        IsGiaoVienDungLopOverride        BIT NOT NULL
            CONSTRAINT DF_App_HVPC_GVOverride DEFAULT (0),
        IsXeTapOverride                  BIT NOT NULL
            CONSTRAINT DF_App_HVPC_XeTapOverride DEFAULT (0),
        IsXeBaiSo10Override              BIT NOT NULL
            CONSTRAINT DF_App_HVPC_Xe10Override DEFAULT (0),
        NguonGan                         VARCHAR(20) NOT NULL,
        ImportSessionId                  BIGINT NULL,
        NgayHieuLuc                      DATETIME2(7) NOT NULL
            CONSTRAINT DF_App_HVPC_NgayHieuLuc DEFAULT (SYSUTCDATETIME()),
        NgayHetHieuLuc                   DATETIME2(7) NULL,
        IsCurrent                        BIT NOT NULL
            CONSTRAINT DF_App_HVPC_IsCurrent DEFAULT (1),
        GhiChu                           NVARCHAR(1000) NULL,
        CreatedAt                        DATETIME2(7) NOT NULL
            CONSTRAINT DF_App_HVPC_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CreatedBy                        NVARCHAR(100) NOT NULL,
        UpdatedAt                        DATETIME2(7) NULL,
        UpdatedBy                        NVARCHAR(100) NULL,
        RowVersion                       ROWVERSION NOT NULL,

        CONSTRAINT PK_App_HocVien_PhanCong
            PRIMARY KEY CLUSTERED (PhanCongId),
        CONSTRAINT FK_App_HVPC_HocVien
            FOREIGN KEY (HocVienId)
            REFERENCES dbo.App_HocVien(HocVienId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_HVPC_NhomDaoTao
            FOREIGN KEY (NhomDaoTaoId)
            REFERENCES dbo.App_KhoaHoc_NhomDaoTao(NhomDaoTaoId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_HVPC_GiaoVienHoSo
            FOREIGN KEY (GiaoVienHoSoId)
            REFERENCES dbo.App_GiaoVien_hs(GiaoVienHsId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_HVPC_GiaoVienDungLop
            FOREIGN KEY (GiaoVienDungLopId)
            REFERENCES dbo.App_GiaoVien(GiaoVienId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_HVPC_XeTap
            FOREIGN KEY (XeTapId)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_HVPC_XeBaiSo10
            FOREIGN KEY (XeBaiSo10Id)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_HVPC_ImportSession
            FOREIGN KEY (ImportSessionId)
            REFERENCES dbo.App_ImportBatch(ImportBatchId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT CK_App_HVPC_NguonGan CHECK
        (
            NguonGan IN ('MANUAL', 'EXCEL', 'BULK', 'GROUP')
            AND (NguonGan <> 'GROUP' OR NhomDaoTaoId IS NOT NULL)
        ),
        CONSTRAINT CK_App_HVPC_HasAssignment CHECK
        (
            NhomDaoTaoId IS NOT NULL
            OR GiaoVienHoSoId IS NOT NULL
            OR GiaoVienDungLopId IS NOT NULL
            OR XeTapId IS NOT NULL
            OR XeBaiSo10Id IS NOT NULL
        ),
        CONSTRAINT CK_App_HVPC_OverrideState CHECK
        (
            NhomDaoTaoId IS NOT NULL
            OR
            (
                IsGiaoVienDungLopOverride = 1
                AND IsXeTapOverride = 1
                AND IsXeBaiSo10Override = 1
            )
        ),
        CONSTRAINT CK_App_HVPC_EffectiveState CHECK
        (
            (IsCurrent = 1 AND NgayHetHieuLuc IS NULL)
            OR
            (
                IsCurrent = 0
                AND NgayHetHieuLuc IS NOT NULL
                AND NgayHetHieuLuc > NgayHieuLuc
            )
        ),
        CONSTRAINT CK_App_HVPC_ImportSource CHECK
        (
            (NguonGan = 'EXCEL' AND ImportSessionId IS NOT NULL)
            OR
            (NguonGan <> 'EXCEL' AND ImportSessionId IS NULL)
        ),
        CONSTRAINT CK_App_HVPC_Audit CHECK
        (
            LEN(LTRIM(RTRIM(CreatedBy))) > 0
            AND DATALENGTH(CreatedBy) =
                DATALENGTH(LTRIM(RTRIM(CreatedBy)))
            AND
            (
                (
                    IsCurrent = 1
                    AND UpdatedAt IS NULL
                    AND UpdatedBy IS NULL
                )
                OR
                (
                    IsCurrent = 0
                    AND UpdatedAt IS NOT NULL
                    AND UpdatedAt = NgayHetHieuLuc
                    AND UpdatedBy IS NOT NULL
                    AND LEN(LTRIM(RTRIM(UpdatedBy))) > 0
                    AND DATALENGTH(UpdatedBy) =
                        DATALENGTH(LTRIM(RTRIM(UpdatedBy)))
                )
            )
        )
    );

    CREATE UNIQUE INDEX UX_App_HVPC_OneCurrentPerHocVien
        ON dbo.App_HocVien_PhanCong(HocVienId)
        WHERE IsCurrent = 1;

    CREATE INDEX IX_App_HVPC_History
        ON dbo.App_HocVien_PhanCong
           (HocVienId, NgayHieuLuc DESC, PhanCongId DESC)
        INCLUDE
        (
            NgayHetHieuLuc,
            IsCurrent,
            NhomDaoTaoId,
            GiaoVienHoSoId,
            GiaoVienDungLopId,
            XeTapId,
            XeBaiSo10Id,
            NguonGan,
            ImportSessionId,
            IsGiaoVienDungLopOverride,
            IsXeTapOverride,
            IsXeBaiSo10Override
        );

    CREATE INDEX IX_App_HVPC_Current_Nhom
        ON dbo.App_HocVien_PhanCong(NhomDaoTaoId, HocVienId)
        INCLUDE
        (
            GiaoVienDungLopId,
            XeTapId,
            XeBaiSo10Id,
            IsGiaoVienDungLopOverride,
            IsXeTapOverride,
            IsXeBaiSo10Override
        )
        WHERE IsCurrent = 1 AND NhomDaoTaoId IS NOT NULL;

    CREATE INDEX IX_App_HVPC_Current_GiaoVienHoSo
        ON dbo.App_HocVien_PhanCong(GiaoVienHoSoId, HocVienId)
        WHERE IsCurrent = 1 AND GiaoVienHoSoId IS NOT NULL;

    CREATE INDEX IX_App_HVPC_Current_GiaoVienDungLop
        ON dbo.App_HocVien_PhanCong(GiaoVienDungLopId, HocVienId)
        WHERE IsCurrent = 1 AND GiaoVienDungLopId IS NOT NULL;

    CREATE INDEX IX_App_HVPC_Current_XeTap
        ON dbo.App_HocVien_PhanCong(XeTapId, HocVienId)
        WHERE IsCurrent = 1 AND XeTapId IS NOT NULL;

    CREATE INDEX IX_App_HVPC_Current_XeBaiSo10
        ON dbo.App_HocVien_PhanCong(XeBaiSo10Id, HocVienId)
        WHERE IsCurrent = 1 AND XeBaiSo10Id IS NOT NULL;

    CREATE INDEX IX_App_HVPC_ImportSession
        ON dbo.App_HocVien_PhanCong(ImportSessionId, PhanCongId)
        WHERE ImportSessionId IS NOT NULL;

    /*
        Durable idempotency ledger. The API inserts a COMPLETED row only in the
        same transaction as the business mutation. A failed transaction leaves
        neither mutation nor ledger row. IdempotencyKey is global across actors
        and operation types so it cannot be rebound to another scope/payload.

        RetainUntilUtc establishes the minimum 180-day replay window. The API
        has no UPDATE/DELETE permission. Any later DBA cleanup must delete only
        expired ledger rows and must never delete App_AuditLog, import sessions,
        or assignment history.
    */
    CREATE TABLE dbo.App_AssignmentOperation
    (
        AssignmentOperationId   BIGINT IDENTITY(1,1) NOT NULL,
        IdempotencyKeySha256    CHAR(64) NOT NULL,
        OperationType           VARCHAR(30) NOT NULL,
        CourseId                BIGINT NOT NULL,
        SourceProfileCode       NVARCHAR(50) NOT NULL,
        ScopeId                 BIGINT NULL,
        Actor                   NVARCHAR(100) NOT NULL,
        PayloadSha256           CHAR(64) NOT NULL,
        PreviewTokenSha256      CHAR(64) NOT NULL,
        OperationId             NVARCHAR(50) NOT NULL,
        ImportBatchId           BIGINT NULL,
        ChangedCount            INT NOT NULL,
        NoChangeCount           INT NOT NULL,
        RequiresBulkPermission  BIT NOT NULL
            CONSTRAINT DF_App_AssignmentOperation_Bulk DEFAULT (0),
        CompletedAtUtc          DATETIME2(7) NOT NULL,
        RetainUntilUtc          DATETIME2(7) NOT NULL,
        CreatedAt               DATETIME2(7) NOT NULL
            CONSTRAINT DF_App_AssignmentOperation_CreatedAt
            DEFAULT (SYSUTCDATETIME()),
        RowVersion              ROWVERSION NOT NULL,

        CONSTRAINT PK_App_AssignmentOperation
            PRIMARY KEY CLUSTERED (AssignmentOperationId),
        CONSTRAINT UQ_App_AssignmentOperation_IdempotencyKey
            UNIQUE NONCLUSTERED (IdempotencyKeySha256),
        CONSTRAINT FK_App_AssignmentOperation_Course
            FOREIGN KEY (CourseId)
            REFERENCES dbo.App_KhoaHoc(KhoaHocId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_AssignmentOperation_GroupScope
            FOREIGN KEY (ScopeId)
            REFERENCES dbo.App_KhoaHoc_NhomDaoTao(NhomDaoTaoId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT FK_App_AssignmentOperation_ImportBatch
            FOREIGN KEY (ImportBatchId)
            REFERENCES dbo.App_ImportBatch(ImportBatchId)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION,
        CONSTRAINT CK_App_AssignmentOperation_Identity CHECK
        (
            LEN(LTRIM(RTRIM(Actor))) > 0
            AND DATALENGTH(Actor) = DATALENGTH(LTRIM(RTRIM(Actor)))
            AND LEN(LTRIM(RTRIM(OperationId))) > 0
            AND DATALENGTH(OperationId) =
                DATALENGTH(LTRIM(RTRIM(OperationId)))
            AND SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
        ),
        CONSTRAINT CK_App_AssignmentOperation_Hashes CHECK
        (
            LEN(IdempotencyKeySha256) = 64
            AND IdempotencyKeySha256 COLLATE Latin1_General_100_BIN2
                NOT LIKE '%[^0-9A-F]%'
            AND LEN(PayloadSha256) = 64
            AND PayloadSha256 COLLATE Latin1_General_100_BIN2
                NOT LIKE '%[^0-9A-F]%'
            AND LEN(PreviewTokenSha256) = 64
            AND PreviewTokenSha256 COLLATE Latin1_General_100_BIN2
                NOT LIKE '%[^0-9A-F]%'
        ),
        CONSTRAINT CK_App_AssignmentOperation_Scope CHECK
        (
            (
                OperationType = 'ASSIGNMENT'
                AND ScopeId IS NULL
                AND ImportBatchId IS NULL
            )
            OR
            (
                OperationType = 'GROUP_DEFAULTS'
                AND ScopeId IS NOT NULL
                AND ImportBatchId IS NULL
            )
            OR
            (
                OperationType = 'IMPORT'
                AND ScopeId IS NULL
                AND ImportBatchId IS NOT NULL
            )
        ),
        CONSTRAINT CK_App_AssignmentOperation_Result CHECK
        (
            ChangedCount >= 0
            AND NoChangeCount >= 0
            AND RetainUntilUtc > CompletedAtUtc
            AND CreatedAt >= CompletedAtUtc
        )
    );

    CREATE INDEX IX_App_AssignmentOperation_Retention
        ON dbo.App_AssignmentOperation(RetainUntilUtc, AssignmentOperationId)
        INCLUDE(OperationType, CourseId);

    EXEC(N'
CREATE TRIGGER dbo.TR_App_GiaoVien_hs_BlockHardDelete
ON dbo.App_GiaoVien_hs
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 529330, ''App_GiaoVien_hs uses soft-delete; hard-delete is rejected.'', 1;
END;');

    EXEC(N'
CREATE TRIGGER dbo.TR_App_KHNDT_BlockHardDelete
ON dbo.App_KhoaHoc_NhomDaoTao
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 529331, ''Training groups use INACTIVE status; hard-delete is rejected.'', 1;
END;');

    EXEC(N'
CREATE TRIGGER dbo.TR_App_KHNDT_ImmutableCourse
ON dbo.App_KhoaHoc_NhomDaoTao
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS newRow
        INNER JOIN deleted AS oldRow
            ON oldRow.NhomDaoTaoId = newRow.NhomDaoTaoId
        WHERE newRow.KhoaHocId <> oldRow.KhoaHocId
    )
        THROW 529332, ''A training group cannot move to another course.'', 1;
END;');

    EXEC(N'
CREATE TRIGGER dbo.TR_App_HVPC_ValidateInsert
ON dbo.App_HocVien_PhanCong
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS assignmentRow
        INNER JOIN dbo.App_HocVien AS learner
            ON learner.HocVienId = assignmentRow.HocVienId
        INNER JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
            ON groupRow.NhomDaoTaoId = assignmentRow.NhomDaoTaoId
        INNER JOIN dbo.App_KhoaHoc AS course
            ON course.KhoaHocId = groupRow.KhoaHocId
        WHERE assignmentRow.NhomDaoTaoId IS NOT NULL
          AND
          (
              learner.MaKhoa IS NULL
              OR learner.SourceProfileCode IS NULL
              OR course.SourceProfileCode IS NULL
              OR learner.MaKhoa <> course.MaKhoa
              OR learner.SourceProfileCode COLLATE Latin1_General_100_BIN2
                 <> course.SourceProfileCode COLLATE Latin1_General_100_BIN2
          )
    )
        THROW 529333, ''Assignment group/course/profile mismatch.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS assignmentRow
        INNER JOIN dbo.App_KhoaHoc_NhomDaoTao AS groupRow
            ON groupRow.NhomDaoTaoId = assignmentRow.NhomDaoTaoId
        WHERE assignmentRow.NhomDaoTaoId IS NOT NULL
          AND
          (
              (
                  assignmentRow.IsGiaoVienDungLopOverride = 0
                  AND
                  (
                      assignmentRow.GiaoVienDungLopId <> groupRow.GiaoVienDungLopId
                      OR
                      (
                          assignmentRow.GiaoVienDungLopId IS NULL
                          AND groupRow.GiaoVienDungLopId IS NOT NULL
                      )
                      OR
                      (
                          assignmentRow.GiaoVienDungLopId IS NOT NULL
                          AND groupRow.GiaoVienDungLopId IS NULL
                      )
                  )
              )
              OR
              (
                  assignmentRow.IsXeTapOverride = 0
                  AND
                  (
                      assignmentRow.XeTapId <> groupRow.XeTapId
                      OR
                      (
                          assignmentRow.XeTapId IS NULL
                          AND groupRow.XeTapId IS NOT NULL
                      )
                      OR
                      (
                          assignmentRow.XeTapId IS NOT NULL
                          AND groupRow.XeTapId IS NULL
                      )
                  )
              )
              OR
              (
                  assignmentRow.IsXeBaiSo10Override = 0
                  AND
                  (
                      assignmentRow.XeBaiSo10Id <> groupRow.XeBaiSo10Id
                      OR
                      (
                          assignmentRow.XeBaiSo10Id IS NULL
                          AND groupRow.XeBaiSo10Id IS NOT NULL
                      )
                      OR
                      (
                          assignmentRow.XeBaiSo10Id IS NOT NULL
                          AND groupRow.XeBaiSo10Id IS NULL
                      )
                  )
              )
          )
    )
        THROW 529334, ''Inherited assignment values must equal the group defaults sealed for this snapshot.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS assignmentRow
        INNER JOIN dbo.App_ImportBatch AS importBatch
            ON importBatch.ImportBatchId = assignmentRow.ImportSessionId
        WHERE assignmentRow.NguonGan = ''EXCEL''
          AND importBatch.EntityType <> N''HOCVIEN_ASSIGNMENT''
    )
        THROW 529335, ''EXCEL assignment must reference an HOCVIEN_ASSIGNMENT import session.'', 1;
END;');

    EXEC(N'
CREATE TRIGGER dbo.TR_App_HVPC_ImmutableSnapshot
ON dbo.App_HocVien_PhanCong
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted AS newRow
        INNER JOIN deleted AS oldRow
            ON oldRow.PhanCongId = newRow.PhanCongId
        WHERE NOT
        (
            oldRow.IsCurrent = 1
            AND oldRow.NgayHetHieuLuc IS NULL
            AND newRow.IsCurrent = 0
            AND newRow.NgayHetHieuLuc IS NOT NULL
            AND newRow.NgayHetHieuLuc > newRow.NgayHieuLuc
            AND newRow.UpdatedAt IS NOT NULL
            AND newRow.UpdatedAt = newRow.NgayHetHieuLuc
            AND newRow.UpdatedBy IS NOT NULL
            AND LEN(LTRIM(RTRIM(newRow.UpdatedBy))) > 0
            AND EXISTS
            (
                SELECT
                    newRow.PhanCongId,
                    newRow.HocVienId,
                    newRow.NhomDaoTaoId,
                    newRow.GiaoVienHoSoId,
                    newRow.GiaoVienDungLopId,
                    newRow.XeTapId,
                    newRow.XeBaiSo10Id,
                    newRow.IsGiaoVienDungLopOverride,
                    newRow.IsXeTapOverride,
                    newRow.IsXeBaiSo10Override,
                    newRow.NguonGan,
                    newRow.ImportSessionId,
                    newRow.NgayHieuLuc,
                    newRow.GhiChu,
                    newRow.CreatedAt,
                    newRow.CreatedBy
                INTERSECT
                SELECT
                    oldRow.PhanCongId,
                    oldRow.HocVienId,
                    oldRow.NhomDaoTaoId,
                    oldRow.GiaoVienHoSoId,
                    oldRow.GiaoVienDungLopId,
                    oldRow.XeTapId,
                    oldRow.XeBaiSo10Id,
                    oldRow.IsGiaoVienDungLopOverride,
                    oldRow.IsXeTapOverride,
                    oldRow.IsXeBaiSo10Override,
                    oldRow.NguonGan,
                    oldRow.ImportSessionId,
                    oldRow.NgayHieuLuc,
                    oldRow.GhiChu,
                    oldRow.CreatedAt,
                    oldRow.CreatedBy
            )
        )
    )
        THROW 529336, ''Assignment snapshots are immutable; close the current row and insert a full replacement snapshot.'', 1;
END;');

    EXEC(N'
CREATE TRIGGER dbo.TR_App_HVPC_BlockHardDelete
ON dbo.App_HocVien_PhanCong
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 529337, ''Assignment history cannot be hard-deleted; close the current snapshot.'', 1;
END;');

    IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
    BEGIN
        DENY INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_GiaoVien_hs
            TO [NT SERVICE\QLHV_APP_RealtimeWorker];
        DENY INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_KhoaHoc_NhomDaoTao
            TO [NT SERVICE\QLHV_APP_RealtimeWorker];
        DENY INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_HocVien_PhanCong
            TO [NT SERVICE\QLHV_APP_RealtimeWorker];
        DENY INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_ImportBatch
            TO [NT SERVICE\QLHV_APP_RealtimeWorker];
        DENY INSERT, UPDATE, DELETE
            ON OBJECT::dbo.App_AssignmentOperation
            TO [NT SERVICE\QLHV_APP_RealtimeWorker];
    END;

    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.App_GiaoVien_hs
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.App_KhoaHoc_NhomDaoTao
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.App_HocVien_PhanCong
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.App_ImportBatch
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT, INSERT ON OBJECT::dbo.App_AssignmentOperation
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT ON OBJECT::dbo.App_KhoaHoc
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT ON OBJECT::dbo.App_HocVien
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT ON OBJECT::dbo.App_GiaoVien
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT ON OBJECT::dbo.App_XeTap
        TO [QLHV_AssignmentApiRole];
    GRANT SELECT, INSERT ON OBJECT::dbo.App_AuditLog
        TO [QLHV_AssignmentApiRole];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_AssignmentOperation', N'U') IS NULL
    THROW 529340, 'Post-migration table verification failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.foreign_keys
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
        OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U'),
        OBJECT_ID(N'dbo.App_AssignmentOperation', N'U')
    )
      AND delete_referential_action = 0
      AND update_referential_action = 0
) <> 14
    THROW 529341, 'Post-migration FK/NO ACTION verification failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.columns
    WHERE object_id IN
    (
        OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U'),
        OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
        OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U'),
        OBJECT_ID(N'dbo.App_AssignmentOperation', N'U')
    )
      AND name = N'RowVersion'
      AND system_type_id = 189
      AND max_length = 8
      AND is_nullable = 0
) <> 4
    THROW 529342, 'Post-migration RowVersion verification failed.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U')
      AND name = N'UX_App_HVPC_OneCurrentPerHocVien'
      AND is_unique = 1
      AND has_filter = 1
      AND CHARINDEX(N'[IsCurrent]=(1)', filter_definition) > 0
)
    THROW 529343, 'Post-migration one-current-snapshot index verification failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.triggers
    WHERE parent_id IN
    (
        OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U'),
        OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
        OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U')
    )
      AND is_disabled = 0
) <> 6
    THROW 529344, 'Post-migration history/consistency trigger verification failed.', 1;

IF DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
   AND
   (
       SELECT COUNT_BIG(*)
       FROM sys.database_permissions
       WHERE grantee_principal_id =
             DATABASE_PRINCIPAL_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker')
         AND state = N'D'
         AND class = 1
         AND major_id IN
         (
             OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U'),
             OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
             OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U'),
             OBJECT_ID(N'dbo.App_ImportBatch', N'U'),
             OBJECT_ID(N'dbo.App_AssignmentOperation', N'U')
         )
         AND permission_name IN (N'INSERT', N'UPDATE', N'DELETE')
   ) <> 15
    THROW 529345, 'Post-migration realtime DENY verification failed.', 1;

IF DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole') IS NULL
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.database_permissions
       WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
         AND state IN (N'G',N'W')
         AND class=1
         AND
         (
             (major_id IN
                 (OBJECT_ID(N'dbo.App_GiaoVien_hs',N'U'),
                  OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao',N'U'),
                  OBJECT_ID(N'dbo.App_HocVien_PhanCong',N'U'),
                  OBJECT_ID(N'dbo.App_ImportBatch',N'U'))
              AND permission_name IN (N'SELECT',N'INSERT',N'UPDATE'))
             OR
             (major_id=OBJECT_ID(N'dbo.App_AssignmentOperation',N'U')
              AND permission_name IN (N'SELECT',N'INSERT'))
             OR
             (major_id IN
                 (OBJECT_ID(N'dbo.App_KhoaHoc',N'U'),
                  OBJECT_ID(N'dbo.App_HocVien',N'U'),
                  OBJECT_ID(N'dbo.App_GiaoVien',N'U'),
                  OBJECT_ID(N'dbo.App_XeTap',N'U'))
              AND permission_name=N'SELECT')
             OR
             (major_id=OBJECT_ID(N'dbo.App_AuditLog',N'U')
              AND permission_name IN (N'SELECT',N'INSERT'))
         )
   ) <> 20
    THROW 529346, 'Post-migration assignment API role GRANT verification failed.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    CONVERT(varchar(36), recovery.database_guid) AS DatabaseGuid,
    CONVERT(bit, 1) AS MigrationVerified,
    (SELECT COUNT_BIG(*) FROM sys.foreign_keys
     WHERE parent_object_id IN
     (
         OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
         OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U'),
         OBJECT_ID(N'dbo.App_AssignmentOperation', N'U')
     )) AS AssignmentForeignKeyCount,
    (SELECT COUNT_BIG(*) FROM sys.triggers
     WHERE parent_id IN
     (
         OBJECT_ID(N'dbo.App_GiaoVien_hs', N'U'),
         OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao', N'U'),
         OBJECT_ID(N'dbo.App_HocVien_PhanCong', N'U')
     )
       AND is_disabled = 0) AS EnabledGuardTriggerCount
FROM sys.database_recovery_status AS recovery
WHERE recovery.database_id = DB_ID();
GO
