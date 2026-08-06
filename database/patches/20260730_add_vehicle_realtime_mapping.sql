/*
    Vehicle realtime target schema only.
    IMPORTANT:
    - This file is a production migration artifact. It is NOT executed by build/test.
    - It does not backfill App_XeTap and deliberately creates no checkpoint row.
    - Incremental vehicle activation still requires a separately sealed
      baseline/checkpoint and source Change Tracking.
    - RT03 V5 expired-window recovery may use this target schema with a locked,
      set-based full snapshot while source dbo.XeTap Change Tracking is OFF; it
      does not create or advance a vehicle checkpoint.
    - Existing QLHV-owned values are never rewritten by this migration.
*/
USE [QLHV_APP];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME()<>N'QLHV_APP'
    THROW 527900, 'VEHICLE_TARGET_DATABASE_NAME_REJECTED', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
)
    THROW 527901, 'VEHICLE_TARGET_DATABASE_GUID_REJECTED', 1;

IF OBJECT_ID(N'dbo.App_XeTap',N'U') IS NULL
    THROW 527902, 'VEHICLE_TARGET_TABLE_MISSING', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.App_XeTap')
      AND c.name=N'XeTapId' AND t.name=N'bigint'
      AND c.is_identity=1 AND c.is_nullable=0
)
    THROW 527903, 'VEHICLE_TARGET_XETAPID_TYPE_MISMATCH', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.App_XeTap')
      AND c.name=N'BienSoXe' AND t.name=N'nvarchar'
      AND c.max_length=40 AND c.is_nullable=0
)
    THROW 527904, 'VEHICLE_TARGET_PLATE_TYPE_MISMATCH', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'dbo.App_XeTap')
      AND c.name=N'RowVersion' AND t.name=N'timestamp'
      AND c.max_length=8 AND c.is_nullable=0
)
    THROW 527905, 'VEHICLE_TARGET_ROWVERSION_TYPE_MISMATCH', 1;
GO

IF COL_LENGTH(N'dbo.App_XeTap',N'SourceProfileCode') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceProfileCode nvarchar(16) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceBienSoXe') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceBienSoXe nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'NormalizedBienSoXe') IS NULL
    ALTER TABLE dbo.App_XeTap ADD NormalizedBienSoXe nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'NormalizedSoDK') IS NULL
    ALTER TABLE dbo.App_XeTap ADD NormalizedSoDK nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'NormalizedSoKhung') IS NULL
    ALTER TABLE dbo.App_XeTap ADD NormalizedSoKhung nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'NormalizedSoDongCo') IS NULL
    ALTER TABLE dbo.App_XeTap ADD NormalizedSoDongCo nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'MaCSDT') IS NULL
    ALTER TABLE dbo.App_XeTap ADD MaCSDT nvarchar(6) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'MaSoGTVT') IS NULL
    ALTER TABLE dbo.App_XeTap ADD MaSoGTVT nvarchar(6) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceRowHash') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceRowHash char(64) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceTrangThai') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceTrangThai bit NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceLifecycle') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceLifecycle nvarchar(32) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceCtVersion') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceCtVersion bigint NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceLastSeenAt') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceLastSeenAt datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceMissingSince') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceMissingSince datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'ManualReviewCode') IS NULL
    ALTER TABLE dbo.App_XeTap ADD ManualReviewCode nvarchar(64) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'ManualReviewAt') IS NULL
    ALTER TABLE dbo.App_XeTap ADD ManualReviewAt datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceCreatedBy') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceCreatedBy nvarchar(60) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceUpdatedBy') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceUpdatedBy nvarchar(60) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceCreatedAt') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceCreatedAt datetime2(3) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceUpdatedAt') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceUpdatedAt datetime2(3) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceImagePathHash') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceImagePathHash char(64) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceMaFileTiepNhanXml') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceMaFileTiepNhanXml nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.App_XeTap',N'SourceThoiGianTiepNhanXml') IS NULL
    ALTER TABLE dbo.App_XeTap ADD SourceThoiGianTiepNhanXml datetime2(3) NULL;
GO

IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceIdentityPair',N'C') IS NULL
    ALTER TABLE dbo.App_XeTap WITH CHECK ADD CONSTRAINT CK_App_XeTap_SourceIdentityPair
    CHECK
    (
        (SourceProfileCode IS NULL AND SourceBienSoXe IS NULL
            AND NormalizedBienSoXe IS NULL)
        OR
        (SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')
            AND NULLIF(LTRIM(RTRIM(SourceBienSoXe)),N'') IS NOT NULL
            AND NULLIF(LTRIM(RTRIM(NormalizedBienSoXe)),N'') IS NOT NULL)
    );

IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceLifecycle',N'C') IS NULL
    ALTER TABLE dbo.App_XeTap WITH CHECK ADD CONSTRAINT CK_App_XeTap_SourceLifecycle
    CHECK
    (
        SourceLifecycle IS NULL OR
        SourceLifecycle IN
        (
            N'ACTIVE',N'SOURCE_INACTIVE',N'SOURCE_MISSING',N'MANUAL_REVIEW'
        )
    );

IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceRowHash',N'C') IS NULL
    ALTER TABLE dbo.App_XeTap WITH CHECK ADD CONSTRAINT CK_App_XeTap_SourceRowHash
    CHECK
    (
        SourceRowHash IS NULL OR
        (LEN(SourceRowHash)=64 AND SourceRowHash NOT LIKE '%[^0-9A-Fa-f]%')
    );

IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceImagePathHash',N'C') IS NULL
    ALTER TABLE dbo.App_XeTap WITH CHECK ADD CONSTRAINT CK_App_XeTap_SourceImagePathHash
    CHECK
    (
        SourceImagePathHash IS NULL OR
        (LEN(SourceImagePathHash)=64
            AND SourceImagePathHash NOT LIKE '%[^0-9A-Fa-f]%')
    );

IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceMissing',N'C') IS NULL
    ALTER TABLE dbo.App_XeTap WITH CHECK ADD CONSTRAINT CK_App_XeTap_SourceMissing
    CHECK
    (
        (SourceLifecycle=N'SOURCE_MISSING' AND SourceMissingSince IS NOT NULL)
        OR
        (ISNULL(SourceLifecycle,N'')<>N'SOURCE_MISSING'
            AND SourceMissingSince IS NULL)
    );

IF OBJECT_ID(N'dbo.CK_App_XeTap_ManualReviewPair',N'C') IS NULL
    ALTER TABLE dbo.App_XeTap WITH CHECK ADD CONSTRAINT CK_App_XeTap_ManualReviewPair
    CHECK
    (
        (ManualReviewCode IS NULL AND ManualReviewAt IS NULL)
        OR
        (NULLIF(LTRIM(RTRIM(ManualReviewCode)),N'') IS NOT NULL
            AND ManualReviewAt IS NOT NULL)
    );
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
      AND name=N'UX_App_XeTap_SourceIdentity'
)
    CREATE UNIQUE INDEX UX_App_XeTap_SourceIdentity
    ON dbo.App_XeTap(SourceProfileCode,SourceBienSoXe)
    WHERE SourceProfileCode IS NOT NULL AND SourceBienSoXe IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
      AND name=N'IX_App_XeTap_NormalizedBienSoXe'
)
    CREATE INDEX IX_App_XeTap_NormalizedBienSoXe
    ON dbo.App_XeTap(NormalizedBienSoXe,SourceProfileCode)
    INCLUDE(XeTapId,SourceBienSoXe,SourceLifecycle,IsDeleted)
    WHERE NormalizedBienSoXe IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
      AND name=N'IX_App_XeTap_SourceLifecycle'
)
    CREATE INDEX IX_App_XeTap_SourceLifecycle
    ON dbo.App_XeTap(SourceProfileCode,SourceLifecycle,IsDeleted)
    INCLUDE(XeTapId,BienSoXe,SourceBienSoXe,SourceTrangThai);
GO

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeCheckpoint',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_XeTap_RealtimeCheckpoint
    (
        SourceProfileCode nvarchar(16) NOT NULL,
        SourceDatabaseGuid uniqueidentifier NOT NULL,
        LastCtVersion bigint NOT NULL,
        MappingFingerprint char(64) NOT NULL,
        SourceSchemaFingerprint char(64) NOT NULL,
        State nvarchar(16) NOT NULL,
        LastCycleId uniqueidentifier NULL,
        LastPlanToken char(64) NULL,
        LastErrorCode nvarchar(64) NULL,
        LastErrorAt datetime2(7) NULL,
        UpdatedAt datetime2(7) NOT NULL
            CONSTRAINT DF_App_XeTap_RTCheckpoint_UpdatedAt
            DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_XeTap_RealtimeCheckpoint
            PRIMARY KEY(SourceProfileCode),
        CONSTRAINT CK_App_XeTap_RTCheckpoint_Profile
            CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_XeTap_RTCheckpoint_Version
            CHECK(LastCtVersion>=0),
        CONSTRAINT CK_App_XeTap_RTCheckpoint_State
            CHECK(State IN(N'READY',N'ACTIVE',N'BLOCKED')),
        CONSTRAINT CK_App_XeTap_RTCheckpoint_MappingHash
            CHECK(LEN(MappingFingerprint)=64
                AND MappingFingerprint NOT LIKE '%[^0-9A-Fa-f]%'),
        CONSTRAINT CK_App_XeTap_RTCheckpoint_SchemaHash
            CHECK(LEN(SourceSchemaFingerprint)=64
                AND SourceSchemaFingerprint NOT LIKE '%[^0-9A-Fa-f]%')
    );
END;
GO

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeEvent',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_XeTap_RealtimeEvent
    (
        VehicleRealtimeEventId bigint IDENTITY(1,1) NOT NULL,
        CycleId uniqueidentifier NOT NULL,
        SourceProfileCode nvarchar(16) NOT NULL,
        SourceCtVersion bigint NOT NULL,
        SourceBienSoXe nvarchar(20) NOT NULL,
        ChangeKind nvarchar(10) NOT NULL,
        Action nvarchar(40) NOT NULL,
        SourceRowHash char(64) NULL,
        TargetXeTapId bigint NULL,
        PlanToken char(64) NOT NULL,
        AppliedAt datetime2(7) NOT NULL
            CONSTRAINT DF_App_XeTap_RTEvent_AppliedAt
            DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_App_XeTap_RealtimeEvent
            PRIMARY KEY(VehicleRealtimeEventId),
        CONSTRAINT UQ_App_XeTap_RTEvent_SourceEvent
            UNIQUE(SourceProfileCode,SourceCtVersion,SourceBienSoXe),
        CONSTRAINT FK_App_XeTap_RTEvent_Target
            FOREIGN KEY(TargetXeTapId)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION,
        CONSTRAINT CK_App_XeTap_RTEvent_Profile
            CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_XeTap_RTEvent_Kind
            CHECK(ChangeKind IN(N'UPSERT',N'DELETE')),
        CONSTRAINT CK_App_XeTap_RTEvent_Version
            CHECK(SourceCtVersion>0)
    );
END;
GO

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeManualReview',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_XeTap_RealtimeManualReview
    (
        VehicleManualReviewId bigint IDENTITY(1,1) NOT NULL,
        CycleId uniqueidentifier NOT NULL,
        SourceProfileCode nvarchar(16) NOT NULL,
        SourceCtVersion bigint NOT NULL,
        SourceBienSoXe nvarchar(20) NOT NULL,
        ReviewCode nvarchar(64) NOT NULL,
        CollisionField nvarchar(32) NULL,
        TargetXeTapId bigint NULL,
        ConflictingXeTapId bigint NULL,
        SourceRowHash char(64) NULL,
        HasActiveAssignment bit NOT NULL,
        Status nvarchar(16) NOT NULL
            CONSTRAINT DF_App_XeTap_RTManual_Status DEFAULT N'OPEN',
        DetectedAt datetime2(7) NOT NULL
            CONSTRAINT DF_App_XeTap_RTManual_DetectedAt
            DEFAULT SYSUTCDATETIME(),
        ResolvedAt datetime2(7) NULL,
        ResolvedBy nvarchar(100) NULL,
        Resolution nvarchar(500) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_XeTap_RealtimeManualReview
            PRIMARY KEY(VehicleManualReviewId),
        CONSTRAINT UQ_App_XeTap_RTManual_SourceEvent
            UNIQUE(SourceProfileCode,SourceCtVersion,SourceBienSoXe),
        CONSTRAINT FK_App_XeTap_RTManual_Target
            FOREIGN KEY(TargetXeTapId)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION,
        CONSTRAINT FK_App_XeTap_RTManual_Conflict
            FOREIGN KEY(ConflictingXeTapId)
            REFERENCES dbo.App_XeTap(XeTapId)
            ON DELETE NO ACTION,
        CONSTRAINT CK_App_XeTap_RTManual_Profile
            CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_XeTap_RTManual_Status
            CHECK(Status IN(N'OPEN',N'RESOLVED',N'DISMISSED')),
        CONSTRAINT CK_App_XeTap_RTManual_Resolution
            CHECK
            (
                (Status=N'OPEN' AND ResolvedAt IS NULL AND ResolvedBy IS NULL)
                OR
                (Status<>N'OPEN' AND ResolvedAt IS NOT NULL
                    AND NULLIF(LTRIM(RTRIM(ResolvedBy)),N'') IS NOT NULL)
            )
    );
END;
GO

IF USER_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
BEGIN
    GRANT SELECT,INSERT,UPDATE ON dbo.App_XeTap
        TO [NT SERVICE\QLHV_APP_RealtimeWorker];
    DENY DELETE ON dbo.App_XeTap
        TO [NT SERVICE\QLHV_APP_RealtimeWorker];
    GRANT SELECT,INSERT,UPDATE ON dbo.App_XeTap_RealtimeCheckpoint
        TO [NT SERVICE\QLHV_APP_RealtimeWorker];
    GRANT SELECT,INSERT ON dbo.App_XeTap_RealtimeEvent
        TO [NT SERVICE\QLHV_APP_RealtimeWorker];
    GRANT SELECT,INSERT ON dbo.App_XeTap_RealtimeManualReview
        TO [NT SERVICE\QLHV_APP_RealtimeWorker];
END;
GO

SELECT N'VEHICLE_REALTIME_SCHEMA_READY_NO_BACKFILL_CHECKPOINT_NOT_CREATED' AS Result;
GO
