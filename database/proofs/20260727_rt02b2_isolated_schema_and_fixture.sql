USE [master];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC\QLHVRT02'
    THROW 527400, 'ISOLATED_DATABASE_IDENTITY_REJECTED: exact server identity.', 1;

IF CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE N'%Developer%'
    THROW 527401, 'ISOLATED_DATABASE_IDENTITY_REJECTED: SQL edition/version.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
      AND source_database_id IS NULL
) <> 3
    THROW 527402, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database state.', 1;
GO

USE [QLHV_RT02_OTO_TEST];
GO
IF DB_ID() <> 5
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789')
    THROW 527403, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO identity.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B-OPERATOR-APPROVAL-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_DATASET_MODE'
      AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
      AND CONVERT(nvarchar(128), value) = N'FALSE'
)
    THROW 527404, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO markers.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NOT NULL
    THROW 527405, 'RT02 OTO test schema already exists.', 1;

CREATE TABLE dbo.NguoiLX
(
    IdentityHmac char(64) NOT NULL
        CONSTRAINT PK_RT02_OTO_NguoiLX PRIMARY KEY,
    ScenarioCode varchar(40) NOT NULL,
    DatasetRole varchar(40) NOT NULL,
    HoTen nvarchar(200) NOT NULL,
    SourceRowHash char(64) NOT NULL,
    IsActive bit NOT NULL
        CONSTRAINT DF_RT02_OTO_NguoiLX_IsActive DEFAULT (1),
    CreatedAtUtc datetime2(7) NOT NULL
        CONSTRAINT DF_RT02_OTO_NguoiLX_CreatedAtUtc DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.NguoiLX_HoSo
(
    IdentityHmac char(64) NOT NULL
        CONSTRAINT PK_RT02_OTO_NguoiLX_HoSo PRIMARY KEY,
    PayloadHash char(64) NOT NULL,
    CONSTRAINT FK_RT02_OTO_NguoiLX_HoSo_NguoiLX
        FOREIGN KEY (IdentityHmac) REFERENCES dbo.NguoiLX(IdentityHmac)
);
GO

USE [QLHV_RT02_MOTO_TEST];
GO
IF DB_ID() <> 6
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A')
    THROW 527406, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO identity.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B-OPERATOR-APPROVAL-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_DATASET_MODE'
      AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
      AND CONVERT(nvarchar(128), value) = N'FALSE'
)
    THROW 527407, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO markers.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NOT NULL
   OR OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NOT NULL
    THROW 527408, 'RT02 MOTO test schema already exists.', 1;

CREATE TABLE dbo.NguoiLX
(
    IdentityHmac char(64) NOT NULL
        CONSTRAINT PK_RT02_MOTO_NguoiLX PRIMARY KEY,
    ScenarioCode varchar(40) NOT NULL,
    DatasetRole varchar(40) NOT NULL,
    HoTen nvarchar(200) NOT NULL,
    SourceRowHash char(64) NOT NULL,
    IsActive bit NOT NULL
        CONSTRAINT DF_RT02_MOTO_NguoiLX_IsActive DEFAULT (1),
    CreatedAtUtc datetime2(7) NOT NULL
        CONSTRAINT DF_RT02_MOTO_NguoiLX_CreatedAtUtc DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.NguoiLX_HoSo
(
    IdentityHmac char(64) NOT NULL
        CONSTRAINT PK_RT02_MOTO_NguoiLX_HoSo PRIMARY KEY,
    PayloadHash char(64) NOT NULL,
    CONSTRAINT FK_RT02_MOTO_NguoiLX_HoSo_NguoiLX
        FOREIGN KEY (IdentityHmac) REFERENCES dbo.NguoiLX(IdentityHmac)
);
GO

USE [QLHV_RT02_TARGET_TEST];
GO
IF DB_ID() <> 7
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
    THROW 527409, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target identity.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B-OPERATOR-APPROVAL-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_DATASET_MODE'
      AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
      AND CONVERT(nvarchar(128), value) = N'FALSE'
)
    THROW 527410, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target markers.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.tables
    WHERE is_ms_shipped = 0
)
    THROW 527411, 'RT02 target test schema already exists.', 1;

CREATE TABLE dbo.Rt02Learner
(
    IdentityHmac char(64) NOT NULL
        CONSTRAINT PK_Rt02Learner PRIMARY KEY,
    SourceProfile varchar(20) NOT NULL,
    ScenarioCode varchar(40) NOT NULL,
    DatasetRole varchar(40) NOT NULL,
    HoTen nvarchar(200) NOT NULL,
    MappedHash char(64) NOT NULL,
    QlhvOwnedHash char(64) NOT NULL,
    WorkflowState varchar(40) NOT NULL,
    NotesHash char(64) NOT NULL,
    PhotoState varchar(40) NOT NULL,
    Active bit NOT NULL,
    SoftDeleted bit NOT NULL,
    CreatedAtUtc datetime2(7) NOT NULL
        CONSTRAINT DF_Rt02Learner_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc datetime2(7) NOT NULL
        CONSTRAINT DF_Rt02Learner_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Rt02Learner_DeleteState
        CHECK
        (
            (Active = 1 AND SoftDeleted = 0)
            OR (Active = 0 AND SoftDeleted = 1)
        )
);
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF ISNULL(CONVERT(int, SESSIONPROPERTY(N'ANSI_NULLS')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'ANSI_PADDING')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'ANSI_WARNINGS')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'ARITHABORT')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'QUOTED_IDENTIFIER')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'NUMERIC_ROUNDABORT')), -1) <> 0
    THROW 527419, 'RT02 filtered-index canonical SET options are unresolved.', 1;

CREATE UNIQUE INDEX UX_Rt02Learner_ActiveIdentity
    ON dbo.Rt02Learner(SourceProfile, IdentityHmac)
    WHERE Active = 1 AND SoftDeleted = 0;

CREATE TABLE dbo.Rt02ManualReviewEvidence
(
    CycleId varchar(120) NOT NULL,
    OperationId varchar(160) NOT NULL,
    IdentityHmac char(64) NOT NULL,
    Disposition varchar(60) NOT NULL,
    DispositionHash char(64) NOT NULL,
    TargetRetainedActive bit NOT NULL,
    TargetMutated bit NOT NULL,
    CreatedAtUtc datetime2(7) NOT NULL
        CONSTRAINT DF_Rt02ManualReviewEvidence_CreatedAtUtc
        DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Rt02ManualReviewEvidence
        PRIMARY KEY (CycleId, OperationId),
    CONSTRAINT CK_Rt02ManualReviewEvidence_NoMutation
        CHECK (TargetRetainedActive = 1 AND TargetMutated = 0)
);

CREATE TABLE dbo.Rt02ApplyMarker
(
    CycleId varchar(120) NOT NULL
        CONSTRAINT PK_Rt02ApplyMarker PRIMARY KEY,
    PlanHash char(64) NOT NULL,
    DispositionHash char(64) NOT NULL,
    InsertedRows int NOT NULL,
    UpdatedRows int NOT NULL,
    RetainedRows int NOT NULL,
    PreservedQlhvOwnedHash char(64) NOT NULL,
    CommittedAtUtc datetime2(7) NOT NULL
);

CREATE TABLE dbo.Rt02ApplyCheckpoint
(
    SourceProfile varchar(20) NOT NULL,
    Mode varchar(40) NOT NULL,
    MappingFingerprint char(64) NOT NULL,
    EnvironmentId varchar(128) NOT NULL,
    CycleId varchar(120) NOT NULL,
    PlanHash char(64) NOT NULL,
    MarkerHash char(64) NOT NULL,
    SourceWatermark bigint NOT NULL,
    PublishedAtUtc datetime2(7) NOT NULL,
    CONSTRAINT PK_Rt02ApplyCheckpoint
        PRIMARY KEY
        (
            SourceProfile,
            Mode,
            MappingFingerprint,
            EnvironmentId
        )
);

CREATE TABLE dbo.Rt02EnvironmentState
(
    EnvironmentId varchar(128) NOT NULL
        CONSTRAINT PK_Rt02EnvironmentState PRIMARY KEY,
    DatasetFingerprint char(64) NOT NULL,
    MappingFingerprint char(64) NOT NULL,
    SourceSchemaFingerprint char(64) NOT NULL,
    TargetSchemaFingerprint char(64) NOT NULL,
    IdentityNormalizationVersion varchar(60) NOT NULL,
    DatasetMode varchar(20) NOT NULL,
    PiiRows int NOT NULL,
    CreatedAtUtc datetime2(7) NOT NULL,
    CONSTRAINT CK_Rt02EnvironmentState_Synthetic
        CHECK (DatasetMode = 'SYNTHETIC' AND PiiRows = 0)
);
GO

USE [QLHV_RT02_OTO_TEST];
GO
DECLARE @Index int = 1;
DECLARE @Identity char(64);
DECLARE @HoTen nvarchar(200);
DECLARE @SourceHash char(64);

WHILE @Index <= 150
BEGIN
    SET @Identity = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2-KEY|HMAC|OTO|NOCHANGE|', FORMAT(@Index, N'D4'))), 2);
    SET @HoTen = CONCAT(N'SYNTHETIC OTO NOCHANGE ', FORMAT(@Index, N'D4'));
    SET @SourceHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|SOURCE|', @Identity, N'|', @HoTen)), 2);

    INSERT dbo.NguoiLX
    (
        IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash
    )
    VALUES
    (
        @Identity, 'CORE', 'NO_CHANGE', @HoTen, @SourceHash
    );

    INSERT dbo.NguoiLX_HoSo(IdentityHmac, PayloadHash)
    VALUES
    (
        @Identity,
        CONVERT(char(64), HASHBYTES(
            N'SHA2_256',
            CONCAT(N'RT02B2|HOSO|', @Identity)), 2)
    );

    SET @Index += 1;
END;

DECLARE @InsertIdentity char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256', N'RT02B2-KEY|HMAC|OTO|INSERT'), 2);
DECLARE @InsertName nvarchar(200) = N'SYNTHETIC OTO INSERT';
DECLARE @InsertHash char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256',
    CONCAT(N'RT02B2|SOURCE|', @InsertIdentity, N'|', @InsertName)), 2);
INSERT dbo.NguoiLX
(
    IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash
)
VALUES
(
    @InsertIdentity, 'CORE', 'SOURCE_ONLY_NEW_ROW', @InsertName, @InsertHash
);
INSERT dbo.NguoiLX_HoSo(IdentityHmac, PayloadHash)
VALUES
(
    @InsertIdentity,
    CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|HOSO|', @InsertIdentity)), 2)
);

DECLARE @UpdateIdentity char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256', N'RT02B2-KEY|HMAC|OTO|UPDATE'), 2);
DECLARE @UpdateName nvarchar(200) = N'SYNTHETIC OTO UPDATED';
DECLARE @UpdateHash char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256',
    CONCAT(N'RT02B2|SOURCE|', @UpdateIdentity, N'|', @UpdateName)), 2);
INSERT dbo.NguoiLX
(
    IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash
)
VALUES
(
    @UpdateIdentity, 'CORE', 'STALE_IMPORTED_VALUE', @UpdateName, @UpdateHash
);
INSERT dbo.NguoiLX_HoSo(IdentityHmac, PayloadHash)
VALUES
(
    @UpdateIdentity,
    CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|HOSO|', @UpdateIdentity)), 2)
);
GO

USE [QLHV_RT02_MOTO_TEST];
GO
DECLARE @Index int = 1;
DECLARE @Identity char(64);
DECLARE @HoTen nvarchar(200);
DECLARE @SourceHash char(64);

WHILE @Index <= 5
BEGIN
    SET @Identity = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2-KEY|HMAC|MOTO|NOCHANGE|', FORMAT(@Index, N'D4'))), 2);
    SET @HoTen = CONCAT(N'SYNTHETIC MOTO NOCHANGE ', FORMAT(@Index, N'D4'));
    SET @SourceHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|SOURCE|', @Identity, N'|', @HoTen)), 2);

    INSERT dbo.NguoiLX
    (
        IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash
    )
    VALUES
    (
        @Identity, 'CORE', 'NO_CHANGE', @HoTen, @SourceHash
    );

    INSERT dbo.NguoiLX_HoSo(IdentityHmac, PayloadHash)
    VALUES
    (
        @Identity,
        CONVERT(char(64), HASHBYTES(
            N'SHA2_256',
            CONCAT(N'RT02B2|HOSO|', @Identity)), 2)
    );

    SET @Index += 1;
END;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
DECLARE @Index int = 1;
DECLARE @Identity char(64);
DECLARE @HoTen nvarchar(200);
DECLARE @MappedHash char(64);
DECLARE @QlhvHash char(64);
DECLARE @NotesHash char(64);

WHILE @Index <= 150
BEGIN
    SET @Identity = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2-KEY|HMAC|OTO|NOCHANGE|', FORMAT(@Index, N'D4'))), 2);
    SET @HoTen = CONCAT(N'SYNTHETIC OTO NOCHANGE ', FORMAT(@Index, N'D4'));
    SET @MappedHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|SOURCE|', @Identity, N'|', @HoTen)), 2);
    SET @QlhvHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|QLHV|', @Identity, N'|READY|NOTES|PHOTO_DISABLED')), 2);
    SET @NotesHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|NOTES|', @Identity)), 2);

    INSERT dbo.Rt02Learner
    (
        IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
        MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
        Active, SoftDeleted
    )
    VALUES
    (
        @Identity, 'CSDT_OTO', 'CORE', 'NO_CHANGE', @HoTen,
        @MappedHash, @QlhvHash, 'READY', @NotesHash, 'PHOTO_DISABLED',
        1, 0
    );

    SET @Index += 1;
END;

DECLARE @UpdateIdentity char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256', N'RT02B2-KEY|HMAC|OTO|UPDATE'), 2);
DECLARE @UpdateOldName nvarchar(200) = N'SYNTHETIC OTO OLD';
DECLARE @UpdateOldMapped char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256',
    CONCAT(N'RT02B2|SOURCE|', @UpdateIdentity, N'|', @UpdateOldName)), 2);
DECLARE @UpdateQlhv char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256',
    CONCAT(N'RT02B2|QLHV|', @UpdateIdentity, N'|READY|NOTES|PHOTO_DISABLED')), 2);
INSERT dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @UpdateIdentity, 'CSDT_OTO', 'CORE', 'STALE_IMPORTED_VALUE',
    @UpdateOldName, @UpdateOldMapped, @UpdateQlhv, 'READY',
    CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|NOTES|', @UpdateIdentity)), 2),
    'PHOTO_DISABLED', 1, 0
);

DECLARE @RetainIdentity char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256', N'RT02B2-KEY|HMAC|OTO|TARGETONLY'), 2);
DECLARE @RetainMapped char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256',
    CONCAT(N'RT02B2|TARGETONLY|', @RetainIdentity)), 2);
DECLARE @RetainQlhv char(64) = CONVERT(char(64), HASHBYTES(
    N'SHA2_256',
    CONCAT(N'RT02B2|QLHV|', @RetainIdentity, N'|READY|NOTES|PHOTO_DISABLED')), 2);
INSERT dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @RetainIdentity, 'CSDT_OTO', 'CORE', 'SOURCE_ROW_REMOVED',
    N'SYNTHETIC OTO TARGET ONLY', @RetainMapped, @RetainQlhv, 'READY',
    CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|NOTES|', @RetainIdentity)), 2),
    'PHOTO_DISABLED', 1, 0
);

SET @Index = 1;
WHILE @Index <= 3
BEGIN
    SET @Identity = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2-KEY|HMAC|OTO|SOFTDELETED|', FORMAT(@Index, N'D2'))), 2);
    SET @QlhvHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|QLHV|', @Identity, N'|CLOSED|NOTES|PHOTO_DISABLED')), 2);
    INSERT dbo.Rt02Learner
    (
        IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
        MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
        Active, SoftDeleted
    )
    VALUES
    (
        @Identity, 'CSDT_OTO', 'CORE', 'SOFT_DELETED_BASELINE',
        CONCAT(N'SYNTHETIC OTO SOFTDELETED ', FORMAT(@Index, N'D2')),
        CONVERT(char(64), HASHBYTES(
            N'SHA2_256',
            CONCAT(N'RT02B2|SOFTDELETED|', @Identity)), 2),
        @QlhvHash, 'CLOSED',
        CONVERT(char(64), HASHBYTES(
            N'SHA2_256',
            CONCAT(N'RT02B2|NOTES|', @Identity)), 2),
        'PHOTO_DISABLED', 0, 1
    );
    SET @Index += 1;
END;

SET @Index = 1;
WHILE @Index <= 5
BEGIN
    SET @Identity = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2-KEY|HMAC|MOTO|NOCHANGE|', FORMAT(@Index, N'D4'))), 2);
    SET @HoTen = CONCAT(N'SYNTHETIC MOTO NOCHANGE ', FORMAT(@Index, N'D4'));
    SET @MappedHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|SOURCE|', @Identity, N'|', @HoTen)), 2);
    SET @QlhvHash = CONVERT(char(64), HASHBYTES(
        N'SHA2_256',
        CONCAT(N'RT02B2|QLHV|', @Identity, N'|READY|NOTES|PHOTO_DISABLED')), 2);
    INSERT dbo.Rt02Learner
    (
        IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
        MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
        Active, SoftDeleted
    )
    VALUES
    (
        @Identity, 'CSDT_MOTO', 'CORE', 'NO_CHANGE', @HoTen,
        @MappedHash, @QlhvHash, 'READY',
        CONVERT(char(64), HASHBYTES(
            N'SHA2_256',
            CONCAT(N'RT02B2|NOTES|', @Identity)), 2),
        'PHOTO_DISABLED', 1, 0
    );
    SET @Index += 1;
END;

DECLARE @Canonical nvarchar(max);
SELECT @Canonical = CONCAT(
    N'ENV=RT02B0-CSDLTTTC-QLHVRT02-20260727-01',
    N'|MODE=SYNTHETIC|GENERATOR=RT02B2-SQL-V1',
    N'|OTO_NO_CHANGE=150|OTO_INSERT=1|OTO_UPDATE=1',
    N'|OTO_TARGET_ONLY=1|OTO_SOFT_DELETED=3|MOTO_NO_CHANGE=5',
    N'|DUPLICATE_ACTIVE=0|PII_ROWS=0|',
    STRING_AGG(
        CONVERT(nvarchar(max),
            CONCAT(
                IdentityHmac, N':', SourceProfile, N':', DatasetRole,
                N':', MappedHash, N':', QlhvOwnedHash, N':',
                CONVERT(varchar(1), Active), N':',
                CONVERT(varchar(1), SoftDeleted))),
        N'|'
    ) WITHIN GROUP (ORDER BY SourceProfile, IdentityHmac)
)
FROM dbo.Rt02Learner
WHERE ScenarioCode = 'CORE';

DECLARE @DatasetFingerprint char(64) = CONVERT(
    char(64), HASHBYTES(N'SHA2_256', @Canonical), 2);
DECLARE @MappingFingerprint char(64) = CONVERT(
    char(64), HASHBYTES(N'SHA2_256', N'RT02B2-MAPPING-V1-HOTEN-ONLY'), 2);
DECLARE @SourceSchemaFingerprint char(64) = CONVERT(
    char(64), HASHBYTES(
        N'SHA2_256',
        N'RT02B2-SOURCE-V1|NguoiLX|NguoiLX_HoSo'), 2);
DECLARE @TargetSchemaFingerprint char(64) = CONVERT(
    char(64), HASHBYTES(
        N'SHA2_256',
        N'RT02B2-TARGET-V1|Learner|Review|Marker|Checkpoint'), 2);

INSERT dbo.Rt02EnvironmentState
(
    EnvironmentId, DatasetFingerprint, MappingFingerprint,
    SourceSchemaFingerprint, TargetSchemaFingerprint,
    IdentityNormalizationVersion, DatasetMode, PiiRows, CreatedAtUtc
)
VALUES
(
    'RT02B0-CSDLTTTC-QLHVRT02-20260727-01',
    @DatasetFingerprint,
    @MappingFingerprint,
    @SourceSchemaFingerprint,
    @TargetSchemaFingerprint,
    'RT02B2-HMAC-SHA256-V1',
    'SYNTHETIC',
    0,
    SYSUTCDATETIME()
);
GO

USE [QLHV_RT02_TARGET_TEST];
GO
WITH DuplicateActive AS
(
    SELECT SourceProfile, IdentityHmac
    FROM dbo.Rt02Learner
    WHERE Active = 1 AND SoftDeleted = 0
    GROUP BY SourceProfile, IdentityHmac
    HAVING COUNT_BIG(*) > 1
)
SELECT
    N'RT02B2_CORE_FIXTURE' AS Evidence,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'NO_CHANGE') AS OtoNoChange,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'SOURCE_ONLY_NEW_ROW')
        AS OtoInsertCandidate,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'STALE_IMPORTED_VALUE')
        AS OtoUpdateCandidate,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'SOURCE_ROW_REMOVED'
          AND Active = 1 AND SoftDeleted = 0) AS OtoTargetOnlyActive,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'SOFT_DELETED_BASELINE'
          AND Active = 0 AND SoftDeleted = 1) AS OtoSoftDeletedBaseline,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
        WHERE ScenarioCode = 'CORE' AND DatasetRole = 'NO_CHANGE') AS MotoNoChange,
    (SELECT COUNT_BIG(*) FROM DuplicateActive) AS DuplicateActiveGroups,
    (SELECT DatasetFingerprint FROM dbo.Rt02EnvironmentState) AS DatasetFingerprint;
GO
