/*
    QLHV_APP durable state for near-realtime CSDT V2 -> V1 synchronization.

    This patch stores only synchronization metadata. It does not read or change
    CSDT business data, execute a baseline, or enable Change Tracking elsewhere.
    Checkpoints are intentionally split by stream and domain so application code
    can advance each checkpoint only after the corresponding V1 transaction commits.
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

IF DB_NAME() <> N'QLHV_APP'
    THROW 527480, 'Wrong database. Expected QLHV_APP.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
)
    THROW 527481, 'QLHV_APP must be ONLINE and read-write.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeStream', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeStream
        (
            StreamId bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_CsdtRealtimeStream PRIMARY KEY CLUSTERED,
            StreamCode nvarchar(40) NOT NULL,
            VehicleType nvarchar(10) NOT NULL,
            SourceProfileCode nvarchar(50) NOT NULL,
            TargetProfileCode nvarchar(50) NOT NULL,
            MaCSDT nvarchar(10) NOT NULL,
            IsEnabled bit NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeStream_IsEnabled DEFAULT (1),
            StreamStatus nvarchar(30) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeStream_StreamStatus DEFAULT (N'BASELINE_PENDING'),
            BaselineStatus nvarchar(20) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeStream_BaselineStatus DEFAULT (N'NOT_STARTED'),
            BaselineVersion bigint NULL,
            LastSuccessfulVersion bigint NULL,
            CurrentSourceVersion bigint NULL,
            MinimumValidVersion bigint NULL,
            LagVersions bigint NULL,
            LastStartedAtUtc datetime2(7) NULL,
            LastCompletedAtUtc datetime2(7) NULL,
            LastSuccessAtUtc datetime2(7) NULL,
            LastReconciledAtUtc datetime2(7) NULL,
            RetryCount int NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeStream_RetryCount DEFAULT (0),
            NextRetryAtUtc datetime2(7) NULL,
            LastErrorCode nvarchar(100) NULL,
            LastErrorMessage nvarchar(2000) NULL,
            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeStream_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeStream_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            RowVersion rowversion NOT NULL,

            CONSTRAINT UQ_App_CsdtRealtimeStream_StreamCode UNIQUE NONCLUSTERED (StreamCode),
            CONSTRAINT CK_App_CsdtRealtimeStream_Catalog CHECK
            (
                (
                    StreamCode = N'OTO_V2_TO_V1'
                    AND VehicleType = N'OTO'
                    AND
                    (
                        (SourceProfileCode = N'OTO_V2' AND TargetProfileCode = N'OTO_V1')
                        OR
                        (SourceProfileCode = N'OTO_V2_BAK' AND TargetProfileCode = N'OTO_V1_BAK')
                    )
                    AND MaCSDT = N'66029'
                )
                OR
                (
                    StreamCode = N'MOTO_V2_TO_V1'
                    AND VehicleType = N'MOTO'
                    AND
                    (
                        (SourceProfileCode = N'MOTO_V2' AND TargetProfileCode = N'MOTO_V1')
                        OR
                        (SourceProfileCode = N'MOTO_V2_BAK' AND TargetProfileCode = N'MOTO_V1_BAK')
                    )
                    AND MaCSDT = N'66030'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeStream_Status CHECK
            (
                StreamStatus IN
                (
                    N'DISABLED',
                    N'BASELINE_PENDING',
                    N'BASELINING',
                    N'CATCHING_UP',
                    N'RUNNING',
                    N'ERROR'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeStream_BaselineStatus CHECK
            (
                BaselineStatus IN (N'NOT_STARTED', N'RUNNING', N'COMPLETED', N'FAILED')
            ),
            CONSTRAINT CK_App_CsdtRealtimeStream_Versions CHECK
            (
                (BaselineVersion IS NULL OR BaselineVersion >= 0)
                AND (LastSuccessfulVersion IS NULL OR LastSuccessfulVersion >= 0)
                AND (CurrentSourceVersion IS NULL OR CurrentSourceVersion >= 0)
                AND (MinimumValidVersion IS NULL OR MinimumValidVersion >= 0)
                AND (LagVersions IS NULL OR LagVersions >= 0)
                AND RetryCount >= 0
            ),
            CONSTRAINT CK_App_CsdtRealtimeStream_EnableStatus CHECK
            (
                IsEnabled = 1 OR StreamStatus = N'DISABLED'
            )
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeDomainState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeDomainState
        (
            StreamId bigint NOT NULL,
            DomainCode nvarchar(50) NOT NULL,
            DomainOrder tinyint NOT NULL,
            DomainStatus nvarchar(20) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_DomainStatus DEFAULT (N'PENDING'),
            IsOptional bit NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_IsOptional DEFAULT (0),
            BaselineStatus nvarchar(20) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_BaselineStatus DEFAULT (N'NOT_STARTED'),
            BaselineVersion bigint NULL,
            LastSuccessfulVersion bigint NULL,
            CurrentSourceVersion bigint NULL,
            MinimumValidVersion bigint NULL,
            LagVersions bigint NULL,
            SourceRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_SourceRows DEFAULT (0),
            TargetRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_TargetRows DEFAULT (0),
            BaselineRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_BaselineRows DEFAULT (0),
            InsertedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_InsertedRows DEFAULT (0),
            UpdatedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_UpdatedRows DEFAULT (0),
            SkippedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_SkippedRows DEFAULT (0),
            ErrorRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_ErrorRows DEFAULT (0),
            TombstoneRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_TombstoneRows DEFAULT (0),
            ReconciledRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_ReconciledRows DEFAULT (0),
            LastStartedAtUtc datetime2(7) NULL,
            LastCompletedAtUtc datetime2(7) NULL,
            LastSuccessAtUtc datetime2(7) NULL,
            LastReconciledAtUtc datetime2(7) NULL,
            RetryCount int NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_RetryCount DEFAULT (0),
            NextRetryAtUtc datetime2(7) NULL,
            LastErrorCode nvarchar(100) NULL,
            LastErrorMessage nvarchar(2000) NULL,
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeDomainState_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
            RowVersion rowversion NOT NULL,

            CONSTRAINT PK_App_CsdtRealtimeDomainState
                PRIMARY KEY CLUSTERED (StreamId, DomainCode),
            CONSTRAINT FK_App_CsdtRealtimeDomainState_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT UQ_App_CsdtRealtimeDomainState_Order
                UNIQUE NONCLUSTERED (StreamId, DomainOrder),
            CONSTRAINT CK_App_CsdtRealtimeDomainState_Domain CHECK
            (
                DomainCode IN
                (
                    N'DM_DonViGTVT',
                    N'GiaoVien',
                    N'KhoaHoc',
                    N'KhoaHoc_GiaoVien',
                    N'BaoCaoI',
                    N'NguoiLX',
                    N'NguoiLX_HoSo',
                    N'NguoiLX_GPLX',
                    N'NguoiLXHS_GiayTo'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeDomainState_Status CHECK
            (
                DomainStatus IN (N'PENDING', N'BASELINING', N'CATCHING_UP', N'RUNNING', N'SKIPPED', N'ERROR')
            ),
            CONSTRAINT CK_App_CsdtRealtimeDomainState_BaselineStatus CHECK
            (
                BaselineStatus IN (N'NOT_STARTED', N'RUNNING', N'COMPLETED', N'FAILED', N'SKIPPED')
            ),
            CONSTRAINT CK_App_CsdtRealtimeDomainState_NonNegative CHECK
            (
                (BaselineVersion IS NULL OR BaselineVersion >= 0)
                AND (LastSuccessfulVersion IS NULL OR LastSuccessfulVersion >= 0)
                AND (CurrentSourceVersion IS NULL OR CurrentSourceVersion >= 0)
                AND (MinimumValidVersion IS NULL OR MinimumValidVersion >= 0)
                AND (LagVersions IS NULL OR LagVersions >= 0)
                AND SourceRows >= 0
                AND TargetRows >= 0
                AND BaselineRows >= 0
                AND InsertedRows >= 0
                AND UpdatedRows >= 0
                AND SkippedRows >= 0
                AND ErrorRows >= 0
                AND TombstoneRows >= 0
                AND ReconciledRows >= 0
                AND RetryCount >= 0
            )
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeRun', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeRun
        (
            RunId uniqueidentifier NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_RunId DEFAULT (NEWSEQUENTIALID()),
            StreamId bigint NOT NULL,
            RunType nvarchar(20) NOT NULL,
            RunStatus nvarchar(20) NOT NULL,
            ActiveSlot bit NULL,
            FromVersion bigint NULL,
            ToVersion bigint NULL,
            MinimumValidVersion bigint NULL,
            StartedAtUtc datetime2(7) NULL,
            CompletedAtUtc datetime2(7) NULL,
            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
            Actor nvarchar(100) NOT NULL,
            InsertedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_InsertedRows DEFAULT (0),
            UpdatedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_UpdatedRows DEFAULT (0),
            SkippedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_SkippedRows DEFAULT (0),
            ErrorRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_ErrorRows DEFAULT (0),
            TombstoneRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRun_TombstoneRows DEFAULT (0),
            ErrorCode nvarchar(100) NULL,
            ErrorMessage nvarchar(2000) NULL,
            DetailJson nvarchar(max) NULL,

            CONSTRAINT PK_App_CsdtRealtimeRun PRIMARY KEY CLUSTERED (RunId),
            CONSTRAINT FK_App_CsdtRealtimeRun_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT CK_App_CsdtRealtimeRun_Type
                CHECK (RunType IN (N'BASELINE', N'INCREMENTAL', N'RECONCILE', N'RETRY', N'REVERSE')),
            CONSTRAINT CK_App_CsdtRealtimeRun_Status
                CHECK (RunStatus IN (N'QUEUED', N'RUNNING', N'SUCCEEDED', N'PARTIAL', N'FAILED', N'CANCELLED')),
            CONSTRAINT CK_App_CsdtRealtimeRun_ActiveSlot CHECK
            (
                (
                    RunStatus IN (N'QUEUED', N'RUNNING')
                    AND ActiveSlot = 1
                    AND CompletedAtUtc IS NULL
                )
                OR
                (
                    RunStatus IN (N'SUCCEEDED', N'PARTIAL', N'FAILED', N'CANCELLED')
                    AND ActiveSlot IS NULL
                    AND CompletedAtUtc IS NOT NULL
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeRun_Timestamps CHECK
            (
                (StartedAtUtc IS NULL OR StartedAtUtc >= CreatedAtUtc)
                AND (CompletedAtUtc IS NULL OR CompletedAtUtc >= COALESCE(StartedAtUtc, CreatedAtUtc))
            ),
            CONSTRAINT CK_App_CsdtRealtimeRun_NonNegative CHECK
            (
                (FromVersion IS NULL OR FromVersion >= 0)
                AND (ToVersion IS NULL OR ToVersion >= 0)
                AND (MinimumValidVersion IS NULL OR MinimumValidVersion >= 0)
                AND InsertedRows >= 0
                AND UpdatedRows >= 0
                AND SkippedRows >= 0
                AND ErrorRows >= 0
                AND TombstoneRows >= 0
            ),
            CONSTRAINT CK_App_CsdtRealtimeRun_DetailJson
                CHECK (DetailJson IS NULL OR ISJSON(DetailJson) = 1)
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeRunDomain', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeRunDomain
        (
            RunId uniqueidentifier NOT NULL,
            DomainCode nvarchar(50) NOT NULL,
            DomainStatus nvarchar(20) NOT NULL,
            FromVersion bigint NULL,
            ToVersion bigint NULL,
            StartedAtUtc datetime2(7) NULL,
            CompletedAtUtc datetime2(7) NULL,
            SourceRows bigint NULL,
            InsertedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_InsertedRows DEFAULT (0),
            UpdatedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_UpdatedRows DEFAULT (0),
            SkippedRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_SkippedRows DEFAULT (0),
            ErrorRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_ErrorRows DEFAULT (0),
            TombstoneRows bigint NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_TombstoneRows DEFAULT (0),
            AttemptCount int NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_AttemptCount DEFAULT (0),
            LastAttemptAtUtc datetime2(7) NULL,
            SucceededAtUtc datetime2(7) NULL,
            ErrorCode nvarchar(100) NULL,
            ErrorMessage nvarchar(2000) NULL,
            DetailJson nvarchar(max) NULL,

            CONSTRAINT PK_App_CsdtRealtimeRunDomain
                PRIMARY KEY CLUSTERED (RunId, DomainCode),
            CONSTRAINT FK_App_CsdtRealtimeRunDomain_Run
                FOREIGN KEY (RunId) REFERENCES dbo.App_CsdtRealtimeRun (RunId),
            CONSTRAINT CK_App_CsdtRealtimeRunDomain_Domain CHECK
            (
                DomainCode IN
                (
                    N'DM_DonViGTVT',
                    N'GiaoVien',
                    N'KhoaHoc',
                    N'KhoaHoc_GiaoVien',
                    N'BaoCaoI',
                    N'NguoiLX',
                    N'NguoiLX_HoSo',
                    N'NguoiLX_GPLX',
                    N'NguoiLXHS_GiayTo'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeRunDomain_Status
                CHECK (DomainStatus IN (N'PENDING', N'RUNNING', N'SUCCEEDED', N'SKIPPED', N'FAILED')),
            CONSTRAINT CK_App_CsdtRealtimeRunDomain_NonNegative CHECK
            (
                (FromVersion IS NULL OR FromVersion >= 0)
                AND (ToVersion IS NULL OR ToVersion >= 0)
                AND (SourceRows IS NULL OR SourceRows >= 0)
                AND InsertedRows >= 0
                AND UpdatedRows >= 0
                AND SkippedRows >= 0
                AND ErrorRows >= 0
                AND TombstoneRows >= 0
                AND AttemptCount >= 0
            ),
            CONSTRAINT CK_App_CsdtRealtimeRunDomain_DetailJson
                CHECK (DetailJson IS NULL OR ISJSON(DetailJson) = 1)
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeCommand', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeCommand
        (
            CommandId uniqueidentifier NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeCommand_CommandId DEFAULT (NEWSEQUENTIALID()),
            StreamId bigint NOT NULL,
            CommandType nvarchar(30) NOT NULL,
            CommandStatus nvarchar(20) NOT NULL,
            ActiveSlot bit NULL,
            RequestedBy nvarchar(100) NOT NULL,
            RequestedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeCommand_RequestedAtUtc DEFAULT (SYSUTCDATETIME()),
            StartedAtUtc datetime2(7) NULL,
            CompletedAtUtc datetime2(7) NULL,
            ExpectedRowVersion binary(8) NULL,
            RunId uniqueidentifier NULL,
            ErrorCode nvarchar(100) NULL,
            ErrorMessage nvarchar(2000) NULL,
            RequestJson nvarchar(max) NULL,

            CONSTRAINT PK_App_CsdtRealtimeCommand PRIMARY KEY CLUSTERED (CommandId),
            CONSTRAINT FK_App_CsdtRealtimeCommand_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT FK_App_CsdtRealtimeCommand_Run
                FOREIGN KEY (RunId) REFERENCES dbo.App_CsdtRealtimeRun (RunId),
            CONSTRAINT CK_App_CsdtRealtimeCommand_Type CHECK
            (
                CommandType IN
                (
                    N'SET_ENABLED',
                    N'BASELINE',
                    N'RETRY',
                    N'V1_TO_V2_PLAN',
                    N'V1_TO_V2_EXECUTE'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeCommand_Status
                CHECK (CommandStatus IN (N'QUEUED', N'RUNNING', N'SUCCEEDED', N'PARTIAL', N'FAILED', N'CANCELLED')),
            CONSTRAINT CK_App_CsdtRealtimeCommand_ActiveSlot CHECK
            (
                (
                    CommandStatus IN (N'QUEUED', N'RUNNING')
                    AND ActiveSlot = 1
                    AND CompletedAtUtc IS NULL
                )
                OR
                (
                    CommandStatus IN (N'SUCCEEDED', N'PARTIAL', N'FAILED', N'CANCELLED')
                    AND ActiveSlot IS NULL
                    AND CompletedAtUtc IS NOT NULL
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeCommand_Timestamps CHECK
            (
                (StartedAtUtc IS NULL OR StartedAtUtc >= RequestedAtUtc)
                AND (CompletedAtUtc IS NULL OR CompletedAtUtc >= COALESCE(StartedAtUtc, RequestedAtUtc))
            ),
            CONSTRAINT CK_App_CsdtRealtimeCommand_RequestJson
                CHECK (RequestJson IS NULL OR ISJSON(RequestJson) = 1)
        );
    END;

    IF COL_LENGTH(N'dbo.App_CsdtRealtimeRunDomain', N'AttemptCount') IS NULL
    BEGIN
        ALTER TABLE dbo.App_CsdtRealtimeRunDomain
            ADD AttemptCount int NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeRunDomain_AttemptCount DEFAULT (0) WITH VALUES;
    END;

    IF COL_LENGTH(N'dbo.App_CsdtRealtimeRunDomain', N'LastAttemptAtUtc') IS NULL
        ALTER TABLE dbo.App_CsdtRealtimeRunDomain ADD LastAttemptAtUtc datetime2(7) NULL;

    IF COL_LENGTH(N'dbo.App_CsdtRealtimeRunDomain', N'SucceededAtUtc') IS NULL
        ALTER TABLE dbo.App_CsdtRealtimeRunDomain ADD SucceededAtUtc datetime2(7) NULL;

    IF OBJECT_ID(N'dbo.CK_App_CsdtRealtimeRunDomain_AttemptCount', N'C') IS NULL
    BEGIN
        ALTER TABLE dbo.App_CsdtRealtimeRunDomain
            ADD CONSTRAINT CK_App_CsdtRealtimeRunDomain_AttemptCount
                CHECK (AttemptCount >= 0);
    END;

    IF OBJECT_ID(N'dbo.CK_App_CsdtRealtimeCommand_Status', N'C') IS NOT NULL
        ALTER TABLE dbo.App_CsdtRealtimeCommand
            DROP CONSTRAINT CK_App_CsdtRealtimeCommand_Status;

    ALTER TABLE dbo.App_CsdtRealtimeCommand
        ADD CONSTRAINT CK_App_CsdtRealtimeCommand_Status
            CHECK (CommandStatus IN (N'QUEUED', N'RUNNING', N'SUCCEEDED', N'PARTIAL', N'FAILED', N'CANCELLED'));

    IF OBJECT_ID(N'dbo.CK_App_CsdtRealtimeCommand_ActiveSlot', N'C') IS NOT NULL
        ALTER TABLE dbo.App_CsdtRealtimeCommand
            DROP CONSTRAINT CK_App_CsdtRealtimeCommand_ActiveSlot;

    ALTER TABLE dbo.App_CsdtRealtimeCommand
        ADD CONSTRAINT CK_App_CsdtRealtimeCommand_ActiveSlot CHECK
        (
            (
                CommandStatus IN (N'QUEUED', N'RUNNING')
                AND ActiveSlot = 1
                AND CompletedAtUtc IS NULL
            )
            OR
            (
                CommandStatus IN (N'SUCCEEDED', N'PARTIAL', N'FAILED', N'CANCELLED')
                AND ActiveSlot IS NULL
                AND CompletedAtUtc IS NOT NULL
            )
        );

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeTombstone', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeTombstone
        (
            TombstoneId bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_CsdtRealtimeTombstone PRIMARY KEY CLUSTERED,
            StreamId bigint NOT NULL,
            DomainCode nvarchar(50) NOT NULL,
            EntityKeyHash binary(32) NOT NULL,
            EntityKey nvarchar(450) NOT NULL,
            SourceVersion bigint NOT NULL,
            TombstoneStatus nvarchar(20) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeTombstone_Status DEFAULT (N'PENDING'),
            FirstSeenAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeTombstone_FirstSeenAtUtc DEFAULT (SYSUTCDATETIME()),
            LastSeenAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeTombstone_LastSeenAtUtc DEFAULT (SYSUTCDATETIME()),
            ReviewedAtUtc datetime2(7) NULL,
            ReviewedBy nvarchar(100) NULL,
            SourceKeyJson nvarchar(max) NULL,
            Note nvarchar(1000) NULL,

            CONSTRAINT FK_App_CsdtRealtimeTombstone_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT UQ_App_CsdtRealtimeTombstone_Identity
                UNIQUE NONCLUSTERED (StreamId, DomainCode, EntityKeyHash, SourceVersion),
            CONSTRAINT CK_App_CsdtRealtimeTombstone_Domain CHECK
            (
                DomainCode IN
                (
                    N'DM_DonViGTVT',
                    N'GiaoVien',
                    N'KhoaHoc',
                    N'KhoaHoc_GiaoVien',
                    N'BaoCaoI',
                    N'NguoiLX',
                    N'NguoiLX_HoSo',
                    N'NguoiLX_GPLX',
                    N'NguoiLXHS_GiayTo'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeTombstone_Status
                CHECK (TombstoneStatus IN (N'PENDING', N'REVIEWED', N'IGNORED', N'RESOLVED')),
            CONSTRAINT CK_App_CsdtRealtimeTombstone_Version
                CHECK (SourceVersion >= 0),
            CONSTRAINT CK_App_CsdtRealtimeTombstone_SourceKeyJson
                CHECK (SourceKeyJson IS NULL OR ISJSON(SourceKeyJson) = 1)
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeConflict', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeConflict
        (
            ConflictId bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_CsdtRealtimeConflict PRIMARY KEY CLUSTERED,
            StreamId bigint NOT NULL,
            RunId uniqueidentifier NULL,
            Direction nvarchar(20) NOT NULL,
            DomainCode nvarchar(50) NOT NULL,
            EntityKeyHash binary(32) NOT NULL,
            EntityKey nvarchar(450) NOT NULL,
            ConflictCode nvarchar(100) NOT NULL,
            ConflictStatus nvarchar(20) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeConflict_Status DEFAULT (N'PENDING'),
            SourceVersion bigint NULL,
            DetectedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeConflict_DetectedAtUtc DEFAULT (SYSUTCDATETIME()),
            ReviewedAtUtc datetime2(7) NULL,
            ReviewedBy nvarchar(100) NULL,
            ResolvedAtUtc datetime2(7) NULL,
            Resolution nvarchar(1000) NULL,
            DetailJson nvarchar(max) NULL,

            CONSTRAINT FK_App_CsdtRealtimeConflict_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT FK_App_CsdtRealtimeConflict_Run
                FOREIGN KEY (RunId) REFERENCES dbo.App_CsdtRealtimeRun (RunId),
            CONSTRAINT CK_App_CsdtRealtimeConflict_Direction
                CHECK (Direction IN (N'V2_TO_V1', N'V1_TO_V2')),
            CONSTRAINT CK_App_CsdtRealtimeConflict_Domain CHECK
            (
                DomainCode IN
                (
                    N'DM_DonViGTVT',
                    N'GiaoVien',
                    N'KhoaHoc',
                    N'KhoaHoc_GiaoVien',
                    N'BaoCaoI',
                    N'NguoiLX',
                    N'NguoiLX_HoSo',
                    N'NguoiLX_GPLX',
                    N'NguoiLXHS_GiayTo'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeConflict_Status
                CHECK (ConflictStatus IN (N'PENDING', N'REVIEWED', N'IGNORED', N'RESOLVED')),
            CONSTRAINT CK_App_CsdtRealtimeConflict_Version
                CHECK (SourceVersion IS NULL OR SourceVersion >= 0),
            CONSTRAINT CK_App_CsdtRealtimeConflict_DetailJson
                CHECK (DetailJson IS NULL OR ISJSON(DetailJson) = 1)
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeEntityState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeEntityState
        (
            StreamId bigint NOT NULL,
            DomainCode nvarchar(50) NOT NULL,
            EntityKeyHash binary(32) NOT NULL,
            EntityKey nvarchar(450) NOT NULL,
            SourceVersion bigint NULL,
            TargetVersion bigint NULL,
            SourceHash binary(32) NULL,
            TargetHash binary(32) NULL,
            LastAction nvarchar(20) NOT NULL,
            LastSynchronizedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeEntityState_LastSynchronizedAtUtc DEFAULT (SYSUTCDATETIME()),
            LastVerifiedAtUtc datetime2(7) NULL,
            LastRunId uniqueidentifier NULL,

            CONSTRAINT PK_App_CsdtRealtimeEntityState
                PRIMARY KEY CLUSTERED (StreamId, DomainCode, EntityKeyHash),
            CONSTRAINT FK_App_CsdtRealtimeEntityState_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT FK_App_CsdtRealtimeEntityState_Run
                FOREIGN KEY (LastRunId) REFERENCES dbo.App_CsdtRealtimeRun (RunId),
            CONSTRAINT CK_App_CsdtRealtimeEntityState_Domain CHECK
            (
                DomainCode IN
                (
                    N'DM_DonViGTVT',
                    N'GiaoVien',
                    N'KhoaHoc',
                    N'KhoaHoc_GiaoVien',
                    N'BaoCaoI',
                    N'NguoiLX',
                    N'NguoiLX_HoSo',
                    N'NguoiLX_GPLX',
                    N'NguoiLXHS_GiayTo'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeEntityState_Action
                CHECK (LastAction IN (N'INSERT', N'UPDATE', N'SKIP', N'RECONCILE', N'CONFLICT')),
            CONSTRAINT CK_App_CsdtRealtimeEntityState_Versions CHECK
            (
                (SourceVersion IS NULL OR SourceVersion >= 0)
                AND (TargetVersion IS NULL OR TargetVersion >= 0)
            )
        );
    END;

    IF OBJECT_ID(N'dbo.App_CsdtRealtimeSourceIdentity', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_CsdtRealtimeSourceIdentity
        (
            StreamId bigint NOT NULL,
            DomainCode nvarchar(50) NOT NULL,
            SourceIdentityHash binary(32) NOT NULL,
            SourceIdentity nvarchar(450) NOT NULL,
            IdentityStatus nvarchar(20) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeSourceIdentity_Status DEFAULT (N'PRESENT'),
            FirstObservedVersion bigint NOT NULL,
            FirstObservedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeSourceIdentity_FirstObservedAtUtc DEFAULT (SYSUTCDATETIME()),
            LastSeenVersion bigint NOT NULL,
            LastSeenAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeSourceIdentity_LastSeenAtUtc DEFAULT (SYSUTCDATETIME()),
            MissingSinceVersion bigint NULL,
            MissingSinceAtUtc datetime2(7) NULL,
            LastRunId uniqueidentifier NULL,
            UpdatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_App_CsdtRealtimeSourceIdentity_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),

            CONSTRAINT PK_App_CsdtRealtimeSourceIdentity
                PRIMARY KEY CLUSTERED (StreamId, DomainCode, SourceIdentityHash),
            CONSTRAINT FK_App_CsdtRealtimeSourceIdentity_Stream
                FOREIGN KEY (StreamId) REFERENCES dbo.App_CsdtRealtimeStream (StreamId),
            CONSTRAINT FK_App_CsdtRealtimeSourceIdentity_Run
                FOREIGN KEY (LastRunId) REFERENCES dbo.App_CsdtRealtimeRun (RunId),
            CONSTRAINT CK_App_CsdtRealtimeSourceIdentity_Domain CHECK
            (
                DomainCode IN
                (
                    N'DM_DonViGTVT',
                    N'GiaoVien',
                    N'KhoaHoc',
                    N'KhoaHoc_GiaoVien',
                    N'BaoCaoI',
                    N'NguoiLX',
                    N'NguoiLX_HoSo',
                    N'NguoiLX_GPLX',
                    N'NguoiLXHS_GiayTo'
                )
            ),
            CONSTRAINT CK_App_CsdtRealtimeSourceIdentity_Status
                CHECK (IdentityStatus IN (N'PRESENT', N'MISSING')),
            CONSTRAINT CK_App_CsdtRealtimeSourceIdentity_Versions CHECK
            (
                FirstObservedVersion >= 0
                AND LastSeenVersion >= FirstObservedVersion
                AND (MissingSinceVersion IS NULL OR MissingSinceVersion >= LastSeenVersion)
            ),
            CONSTRAINT CK_App_CsdtRealtimeSourceIdentity_MissingState CHECK
            (
                (IdentityStatus = N'PRESENT' AND MissingSinceVersion IS NULL AND MissingSinceAtUtc IS NULL)
                OR
                (IdentityStatus = N'MISSING' AND MissingSinceVersion IS NOT NULL AND MissingSinceAtUtc IS NOT NULL)
            ),
            CONSTRAINT CK_App_CsdtRealtimeSourceIdentity_Json
                CHECK (ISJSON(SourceIdentity) = 1)
        );
    END;

    INSERT INTO dbo.App_CsdtRealtimeSourceIdentity
    (
        StreamId, DomainCode, SourceIdentityHash, SourceIdentity,
        IdentityStatus, FirstObservedVersion, FirstObservedAtUtc,
        LastSeenVersion, LastSeenAtUtc, MissingSinceVersion,
        MissingSinceAtUtc, LastRunId, UpdatedAtUtc
    )
    SELECT
        entity.StreamId,
        entity.DomainCode,
        entity.EntityKeyHash,
        entity.EntityKey,
        CASE
            WHEN deleted.SourceVersion >= ISNULL(entity.SourceVersion, 0) THEN N'MISSING'
            ELSE N'PRESENT'
        END,
        ISNULL(entity.SourceVersion, 0),
        entity.LastSynchronizedAtUtc,
        ISNULL(entity.SourceVersion, 0),
        entity.LastSynchronizedAtUtc,
        CASE
            WHEN deleted.SourceVersion >= ISNULL(entity.SourceVersion, 0) THEN deleted.SourceVersion
            ELSE NULL
        END,
        CASE
            WHEN deleted.SourceVersion >= ISNULL(entity.SourceVersion, 0) THEN deleted.LastSeenAtUtc
            ELSE NULL
        END,
        entity.LastRunId,
        SYSUTCDATETIME()
    FROM dbo.App_CsdtRealtimeEntityState AS entity
    OUTER APPLY
    (
        SELECT TOP (1) tombstone.SourceVersion, tombstone.LastSeenAtUtc
        FROM dbo.App_CsdtRealtimeTombstone AS tombstone
        WHERE tombstone.StreamId = entity.StreamId
          AND tombstone.DomainCode = entity.DomainCode
          AND tombstone.EntityKeyHash = entity.EntityKeyHash
        ORDER BY tombstone.SourceVersion DESC, tombstone.TombstoneId DESC
    ) AS deleted
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_CsdtRealtimeSourceIdentity AS existing
        WHERE existing.StreamId = entity.StreamId
          AND existing.DomainCode = entity.DomainCode
          AND existing.SourceIdentityHash = entity.EntityKeyHash
    );

    IF EXISTS
    (
        SELECT required.ObjectName
        FROM
        (
            VALUES
                (N'App_CsdtRealtimeStream'),
                (N'App_CsdtRealtimeDomainState'),
                (N'App_CsdtRealtimeRun'),
                (N'App_CsdtRealtimeRunDomain'),
                (N'App_CsdtRealtimeCommand'),
                (N'App_CsdtRealtimeTombstone'),
                (N'App_CsdtRealtimeConflict'),
                (N'App_CsdtRealtimeEntityState'),
                (N'App_CsdtRealtimeSourceIdentity')
        ) required(ObjectName)
        WHERE OBJECT_ID(N'dbo.' + required.ObjectName, N'U') IS NULL
    )
        THROW 527482, 'A required CSDT realtime state table is missing.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeRun', N'U')
          AND name = N'UX_App_CsdtRealtimeRun_Active'
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_CsdtRealtimeRun_Active
            ON dbo.App_CsdtRealtimeRun (StreamId, ActiveSlot)
            INCLUDE (RunType, RunStatus, StartedAtUtc)
            WHERE ActiveSlot = 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeRun', N'U')
          AND name = N'IX_App_CsdtRealtimeRun_History'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_CsdtRealtimeRun_History
            ON dbo.App_CsdtRealtimeRun (StreamId, CreatedAtUtc DESC)
            INCLUDE
            (
                RunId,
                RunType,
                RunStatus,
                FromVersion,
                ToVersion,
                StartedAtUtc,
                CompletedAtUtc,
                InsertedRows,
                UpdatedRows,
                SkippedRows,
                ErrorRows,
                TombstoneRows
            );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeCommand', N'U')
          AND name = N'UX_App_CsdtRealtimeCommand_Active'
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_CsdtRealtimeCommand_Active
            ON dbo.App_CsdtRealtimeCommand (StreamId, ActiveSlot)
            INCLUDE (CommandType, CommandStatus, RequestedAtUtc)
            WHERE ActiveSlot = 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeCommand', N'U')
          AND name = N'IX_App_CsdtRealtimeCommand_History'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_CsdtRealtimeCommand_History
            ON dbo.App_CsdtRealtimeCommand (StreamId, RequestedAtUtc DESC)
            INCLUDE (CommandId, CommandType, CommandStatus, RequestedBy, StartedAtUtc, CompletedAtUtc, RunId);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeTombstone', N'U')
          AND name = N'IX_App_CsdtRealtimeTombstone_Status'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_CsdtRealtimeTombstone_Status
            ON dbo.App_CsdtRealtimeTombstone (StreamId, TombstoneStatus, LastSeenAtUtc DESC)
            INCLUDE (DomainCode, EntityKeyHash, EntityKey, SourceVersion);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeSourceIdentity', N'U')
          AND name = N'IX_App_CsdtRealtimeSourceIdentity_Status'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_CsdtRealtimeSourceIdentity_Status
            ON dbo.App_CsdtRealtimeSourceIdentity
            (
                StreamId,
                DomainCode,
                IdentityStatus,
                LastSeenVersion
            )
            INCLUDE (SourceIdentity, MissingSinceVersion, LastSeenAtUtc);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.App_CsdtRealtimeConflict', N'U')
          AND name = N'IX_App_CsdtRealtimeConflict_Status'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_App_CsdtRealtimeConflict_Status
            ON dbo.App_CsdtRealtimeConflict (StreamId, ConflictStatus, DetectedAtUtc DESC)
            INCLUDE (DomainCode, EntityKeyHash, EntityKey, ConflictCode, Direction, RunId);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_CsdtRealtimeStream
        WHERE
            (
                StreamCode = N'OTO_V2_TO_V1'
                AND
                (
                    VehicleType <> N'OTO'
                    OR
                    (
                        NOT
                        (
                            (SourceProfileCode = N'OTO_V2' AND TargetProfileCode = N'OTO_V1')
                            OR
                            (SourceProfileCode = N'OTO_V2_BAK' AND TargetProfileCode = N'OTO_V1_BAK')
                        )
                    )
                    OR MaCSDT <> N'66029'
                )
            )
            OR
            (
                StreamCode = N'MOTO_V2_TO_V1'
                AND
                (
                    VehicleType <> N'MOTO'
                    OR
                    (
                        NOT
                        (
                            (SourceProfileCode = N'MOTO_V2' AND TargetProfileCode = N'MOTO_V1')
                            OR
                            (SourceProfileCode = N'MOTO_V2_BAK' AND TargetProfileCode = N'MOTO_V1_BAK')
                        )
                    )
                    OR MaCSDT <> N'66030'
                )
            )
    )
        THROW 527483, 'An existing realtime stream has an incompatible fixed catalog mapping.', 1;

    INSERT INTO dbo.App_CsdtRealtimeStream
    (
        StreamCode,
        VehicleType,
        SourceProfileCode,
        TargetProfileCode,
        MaCSDT,
        IsEnabled,
        StreamStatus,
        BaselineStatus
    )
    SELECT
        seed.StreamCode,
        seed.VehicleType,
        seed.SourceProfileCode,
        seed.TargetProfileCode,
        seed.MaCSDT,
        1,
        N'BASELINE_PENDING',
        N'NOT_STARTED'
    FROM
    (
        VALUES
            (N'OTO_V2_TO_V1', N'OTO', N'OTO_V2', N'OTO_V1', N'66029'),
            (N'MOTO_V2_TO_V1', N'MOTO', N'MOTO_V2', N'MOTO_V1', N'66030')
    ) seed(StreamCode, VehicleType, SourceProfileCode, TargetProfileCode, MaCSDT)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_CsdtRealtimeStream existing
        WHERE existing.StreamCode = seed.StreamCode
    );

    INSERT INTO dbo.App_CsdtRealtimeDomainState
    (
        StreamId,
        DomainCode,
        DomainOrder,
        DomainStatus,
        IsOptional,
        BaselineStatus
    )
    SELECT
        stream.StreamId,
        domain.DomainCode,
        domain.DomainOrder,
        N'PENDING',
        domain.IsOptional,
        N'NOT_STARTED'
    FROM dbo.App_CsdtRealtimeStream stream
    CROSS JOIN
    (
        VALUES
            (N'DM_DonViGTVT',      CONVERT(tinyint, 10), CONVERT(bit, 0)),
            (N'GiaoVien',           CONVERT(tinyint, 20), CONVERT(bit, 1)),
            (N'KhoaHoc',            CONVERT(tinyint, 30), CONVERT(bit, 0)),
            (N'KhoaHoc_GiaoVien',  CONVERT(tinyint, 40), CONVERT(bit, 1)),
            (N'BaoCaoI',            CONVERT(tinyint, 50), CONVERT(bit, 0)),
            (N'NguoiLX',            CONVERT(tinyint, 60), CONVERT(bit, 0)),
            (N'NguoiLX_HoSo',       CONVERT(tinyint, 70), CONVERT(bit, 0)),
            (N'NguoiLX_GPLX',       CONVERT(tinyint, 80), CONVERT(bit, 0)),
            (N'NguoiLXHS_GiayTo',  CONVERT(tinyint, 90), CONVERT(bit, 0))
    ) domain(DomainCode, DomainOrder, IsOptional)
    WHERE stream.StreamCode IN (N'OTO_V2_TO_V1', N'MOTO_V2_TO_V1')
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.App_CsdtRealtimeDomainState existing
          WHERE existing.StreamId = stream.StreamId
            AND existing.DomainCode = domain.DomainCode
      );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
