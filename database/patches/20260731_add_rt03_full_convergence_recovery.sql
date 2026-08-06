USE [QLHV_APP];
GO
/*
    RT03 V5 durable recovery state machine.
    Production artifact only: build/tests never execute this file.
    It stores no source business identity or PII.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME()<>N'QLHV_APP'
    THROW 528500, 'RT03_RECOVERY_TARGET_DATABASE_NAME_REJECTED', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
)
    THROW 528501, 'RT03_RECOVERY_TARGET_DATABASE_GUID_REJECTED', 1;

IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint',N'U') IS NULL
    THROW 528502, 'RT03_RECOVERY_CHECKPOINT_TABLE_MISSING', 1;

IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker',N'U') IS NULL
    THROW 528503, 'RT03_RECOVERY_APPLY_MARKER_TABLE_MISSING', 1;
GO

IF OBJECT_ID(N'dbo.App_Rt03FullConvergenceSession',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Rt03FullConvergenceSession
    (
        RecoveryId uniqueidentifier NOT NULL,
        SourceProfileCode nvarchar(50) NOT NULL,
        SourceDatabaseGuid uniqueidentifier NOT NULL,
        CheckpointBefore bigint NOT NULL,
        AnchorVersion bigint NOT NULL,
        Status nvarchar(20) NOT NULL,
        VerificationPassed bit NOT NULL
            CONSTRAINT DF_App_Rt03Recovery_Verified DEFAULT(0),
        MappingFingerprint char(64) NOT NULL,
        SourceSchemaFingerprint char(64) NOT NULL,
        AttemptCount int NOT NULL
            CONSTRAINT DF_App_Rt03Recovery_Attempt DEFAULT(1),
        CreatedAtUtc datetime2(7) NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        CompletedAtUtc datetime2(7) NULL,
        LastErrorCode nvarchar(100) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_Rt03FullConvergenceSession
            PRIMARY KEY(RecoveryId),
        CONSTRAINT CK_App_Rt03Recovery_Profile
            CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_Rt03Recovery_Versions
            CHECK(CheckpointBefore>=0 AND AnchorVersion>=0),
        CONSTRAINT CK_App_Rt03Recovery_Status
            CHECK(Status IN(N'PREPARING',N'VERIFYING',N'COMPLETED',N'BLOCKED')),
        CONSTRAINT CK_App_Rt03Recovery_Completed
            CHECK
            (
                (Status=N'COMPLETED' AND VerificationPassed=1
                    AND CompletedAtUtc IS NOT NULL)
                OR
                (Status<>N'COMPLETED' AND CompletedAtUtc IS NULL)
            ),
        CONSTRAINT CK_App_Rt03Recovery_MappingHash
            CHECK(LEN(MappingFingerprint)=64
                AND MappingFingerprint NOT LIKE '%[^0-9A-Fa-f]%'),
        CONSTRAINT CK_App_Rt03Recovery_SchemaHash
            CHECK(LEN(SourceSchemaFingerprint)=64
                AND SourceSchemaFingerprint NOT LIKE '%[^0-9A-Fa-f]%')
    );

    CREATE UNIQUE INDEX UX_App_Rt03Recovery_OneOpenProfile
        ON dbo.App_Rt03FullConvergenceSession(SourceProfileCode)
        WHERE Status IN(N'PREPARING',N'VERIFYING');
END;
GO

IF OBJECT_ID(N'dbo.App_Rt03FullConvergenceDomain',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Rt03FullConvergenceDomain
    (
        RecoveryId uniqueidentifier NOT NULL,
        DomainCode nvarchar(20) NOT NULL,
        SequenceOrder tinyint NOT NULL,
        Status nvarchar(20) NOT NULL,
        AttemptCount int NOT NULL,
        SourceRows bigint NOT NULL,
        InsertedRows bigint NOT NULL,
        UpdatedRows bigint NOT NULL,
        InactiveRows bigint NOT NULL,
        MissingRows bigint NOT NULL,
        ManualReviewRows bigint NOT NULL,
        NoChangeRows bigint NOT NULL,
        VerificationHash char(64) NULL,
        StartedAtUtc datetime2(7) NOT NULL,
        CompletedAtUtc datetime2(7) NULL,
        LastErrorCode nvarchar(100) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_Rt03FullConvergenceDomain
            PRIMARY KEY(RecoveryId,DomainCode),
        CONSTRAINT FK_App_Rt03FullConvergenceDomain_Session
            FOREIGN KEY(RecoveryId)
            REFERENCES dbo.App_Rt03FullConvergenceSession(RecoveryId)
            ON DELETE NO ACTION,
        CONSTRAINT CK_App_Rt03RecoveryDomain_CodeOrder
            CHECK
            (
                (DomainCode=N'COURSE' AND SequenceOrder=1)
                OR (DomainCode=N'TEACHER' AND SequenceOrder=2)
                OR (DomainCode=N'VEHICLE' AND SequenceOrder=3)
                OR (DomainCode=N'LEARNER' AND SequenceOrder=4)
                OR (DomainCode=N'RELATION' AND SequenceOrder=5)
            ),
        CONSTRAINT CK_App_Rt03RecoveryDomain_Status
            CHECK(Status IN(N'RUNNING',N'COMPLETED',N'BLOCKED')),
        CONSTRAINT CK_App_Rt03RecoveryDomain_Counts
            CHECK
            (
                AttemptCount>0 AND SourceRows>=0 AND InsertedRows>=0
                AND UpdatedRows>=0 AND InactiveRows>=0 AND MissingRows>=0
                AND ManualReviewRows>=0 AND NoChangeRows>=0
            ),
        CONSTRAINT CK_App_Rt03RecoveryDomain_Completion
            CHECK
            (
                (Status=N'COMPLETED' AND CompletedAtUtc IS NOT NULL
                    AND VerificationHash IS NOT NULL)
                OR
                (Status<>N'COMPLETED' AND CompletedAtUtc IS NULL)
            )
    );
END;
GO

IF OBJECT_ID(N'dbo.App_Rt03FullConvergenceMarker',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Rt03FullConvergenceMarker
    (
        RecoveryId uniqueidentifier NOT NULL,
        SourceProfileCode nvarchar(50) NOT NULL,
        SourceDatabaseGuid uniqueidentifier NOT NULL,
        AnchorVersion bigint NOT NULL,
        VerificationHash char(64) NOT NULL,
        CommittedAtUtc datetime2(7) NOT NULL,
        CONSTRAINT PK_App_Rt03FullConvergenceMarker
            PRIMARY KEY(RecoveryId),
        CONSTRAINT UQ_App_Rt03FullConvergenceMarker_ProfileAnchor
            UNIQUE(SourceProfileCode,SourceDatabaseGuid,AnchorVersion),
        CONSTRAINT FK_App_Rt03FullConvergenceMarker_Session
            FOREIGN KEY(RecoveryId)
            REFERENCES dbo.App_Rt03FullConvergenceSession(RecoveryId)
            ON DELETE NO ACTION,
        CONSTRAINT CK_App_Rt03RecoveryMarker_Profile
            CHECK(SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_Rt03RecoveryMarker_Anchor
            CHECK(AnchorVersion>=0),
        CONSTRAINT CK_App_Rt03RecoveryMarker_Hash
            CHECK(LEN(VerificationHash)=64
                AND VerificationHash NOT LIKE '%[^0-9A-Fa-f]%')
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_App_Rt03BeginFullConvergence
    @RecoveryId uniqueidentifier,
    @SourceProfileCode nvarchar(50),
    @SourceDatabaseGuid uniqueidentifier,
    @CheckpointBefore bigint,
    @AnchorVersion bigint,
    @MappingFingerprint char(64),
    @SourceSchemaFingerprint char(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @SourceProfileCode NOT IN(N'CSDT_OTO',N'CSDT_MOTO')
       OR @CheckpointBefore<0 OR @AnchorVersion<0
        THROW 528510, 'RT03_RECOVERY_BEGIN_ARGUMENT_REJECTED', 1;

    BEGIN TRANSACTION;
    DECLARE @LockResult int,
            @LockResource nvarchar(255)=
                CONCAT(N'QLHV:RT03:RECOVERY_STATE:',@SourceProfileCode);
    EXEC @LockResult=sys.sp_getapplock
        @Resource=@LockResource,
        @LockMode=N'Exclusive',
        @LockOwner=N'Transaction',
        @LockTimeout=0;
    IF @LockResult<0
        THROW 528511, 'RT03_RECOVERY_PROFILE_LOCK_UNAVAILABLE', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.App_Rt03FullConvergenceSession WITH(UPDLOCK,HOLDLOCK)
        WHERE RecoveryId=@RecoveryId
          AND
          (
              SourceProfileCode<>@SourceProfileCode
              OR SourceDatabaseGuid<>@SourceDatabaseGuid
              OR CheckpointBefore<>@CheckpointBefore
              OR AnchorVersion>@AnchorVersion
              OR MappingFingerprint<>@MappingFingerprint
              OR SourceSchemaFingerprint<>@SourceSchemaFingerprint
              OR Status NOT IN(N'PREPARING',N'VERIFYING')
          )
    )
        THROW 528512, 'RT03_RECOVERY_RESUME_CONTRACT_MISMATCH', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_Rt03FullConvergenceSession WITH(UPDLOCK,HOLDLOCK)
        WHERE RecoveryId=@RecoveryId
    )
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM dbo.App_Rt03FullConvergenceSession WITH(UPDLOCK,HOLDLOCK)
            WHERE SourceProfileCode=@SourceProfileCode
              AND Status IN(N'PREPARING',N'VERIFYING')
        )
            THROW 528513, 'RT03_RECOVERY_OTHER_SESSION_ACTIVE', 1;

        INSERT dbo.App_Rt03FullConvergenceSession
        (
            RecoveryId,SourceProfileCode,SourceDatabaseGuid,
            CheckpointBefore,AnchorVersion,Status,VerificationPassed,
            MappingFingerprint,SourceSchemaFingerprint,
            CreatedAtUtc,UpdatedAtUtc
        )
        VALUES
        (
            @RecoveryId,@SourceProfileCode,@SourceDatabaseGuid,
            @CheckpointBefore,@AnchorVersion,N'PREPARING',0,
            @MappingFingerprint,@SourceSchemaFingerprint,
            SYSUTCDATETIME(),SYSUTCDATETIME()
        );
    END
    ELSE
    BEGIN
        /*
            A resumed attempt always replays every domain.  This prevents a
            crash after a target commit (or after VERIFYING) from treating a
            stale CT-OFF snapshot as current when the anchor has advanced.
        */
        UPDATE dbo.App_Rt03FullConvergenceDomain
        SET Status=N'BLOCKED',
            CompletedAtUtc=NULL,
            LastErrorCode=N'RT03_RECOVERY_REPLAY_REQUIRED'
        WHERE RecoveryId=@RecoveryId;

        UPDATE dbo.App_Rt03FullConvergenceSession
        SET AnchorVersion=@AnchorVersion,
            Status=N'PREPARING',
            VerificationPassed=0,
            AttemptCount=AttemptCount+1,
            UpdatedAtUtc=SYSUTCDATETIME(),
            LastErrorCode=NULL
        WHERE RecoveryId=@RecoveryId;
    END;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_App_Rt03RecordFullConvergenceDomain
    @RecoveryId uniqueidentifier,
    @DomainCode nvarchar(20),
    @SequenceOrder tinyint,
    @SourceRows bigint,
    @InsertedRows bigint,
    @UpdatedRows bigint,
    @InactiveRows bigint,
    @MissingRows bigint,
    @ManualReviewRows bigint,
    @NoChangeRows bigint,
    @VerificationHash char(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.App_Rt03FullConvergenceSession WITH(UPDLOCK,HOLDLOCK)
        WHERE RecoveryId=@RecoveryId AND Status=N'PREPARING'
    )
        THROW 528520, 'RT03_RECOVERY_DOMAIN_SESSION_NOT_PREPARING', 1;

    UPDATE dbo.App_Rt03FullConvergenceDomain WITH(UPDLOCK,HOLDLOCK)
    SET SequenceOrder=@SequenceOrder,Status=N'COMPLETED',
        AttemptCount=AttemptCount+1,SourceRows=@SourceRows,
        InsertedRows=@InsertedRows,UpdatedRows=@UpdatedRows,
        InactiveRows=@InactiveRows,MissingRows=@MissingRows,
        ManualReviewRows=@ManualReviewRows,NoChangeRows=@NoChangeRows,
        VerificationHash=@VerificationHash,
        CompletedAtUtc=SYSUTCDATETIME(),LastErrorCode=NULL
    WHERE RecoveryId=@RecoveryId AND DomainCode=@DomainCode;

    IF @@ROWCOUNT=0
        INSERT dbo.App_Rt03FullConvergenceDomain
        (
            RecoveryId,DomainCode,SequenceOrder,Status,AttemptCount,
            SourceRows,InsertedRows,UpdatedRows,InactiveRows,MissingRows,
            ManualReviewRows,NoChangeRows,VerificationHash,
            StartedAtUtc,CompletedAtUtc
        )
        VALUES
        (
            @RecoveryId,@DomainCode,@SequenceOrder,N'COMPLETED',1,
            @SourceRows,@InsertedRows,@UpdatedRows,@InactiveRows,@MissingRows,
            @ManualReviewRows,@NoChangeRows,@VerificationHash,
            SYSUTCDATETIME(),SYSUTCDATETIME()
        );

    UPDATE dbo.App_Rt03FullConvergenceSession
    SET UpdatedAtUtc=SYSUTCDATETIME()
    WHERE RecoveryId=@RecoveryId;
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_App_Rt03VerifyFullConvergence
    @RecoveryId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;

    IF
    (
        SELECT COUNT_BIG(*)
        FROM dbo.App_Rt03FullConvergenceDomain WITH(UPDLOCK,HOLDLOCK)
        WHERE RecoveryId=@RecoveryId AND Status=N'COMPLETED'
          AND VerificationHash IS NOT NULL
    )<>5
        THROW 528530, 'RT03_RECOVERY_DOMAIN_VERIFICATION_INCOMPLETE', 1;

    UPDATE dbo.App_Rt03FullConvergenceSession WITH(UPDLOCK,HOLDLOCK)
    SET Status=N'VERIFYING',VerificationPassed=1,
        UpdatedAtUtc=SYSUTCDATETIME()
    WHERE RecoveryId=@RecoveryId AND Status=N'PREPARING';
    IF @@ROWCOUNT<>1
        THROW 528531, 'RT03_RECOVERY_VERIFY_SESSION_STATE_REJECTED', 1;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_App_Rt03FinalizeFullConvergence
    @RecoveryId uniqueidentifier,
    @VerificationHash char(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;

    DECLARE @SourceProfileCode nvarchar(50),
            @SourceDatabaseGuid uniqueidentifier,
            @CheckpointBefore bigint,
            @AnchorVersion bigint;
    SELECT
        @SourceProfileCode=SourceProfileCode,
        @SourceDatabaseGuid=SourceDatabaseGuid,
        @CheckpointBefore=CheckpointBefore,
        @AnchorVersion=AnchorVersion
    FROM dbo.App_Rt03FullConvergenceSession WITH(UPDLOCK,HOLDLOCK)
    WHERE RecoveryId=@RecoveryId
      AND Status=N'VERIFYING'
      AND VerificationPassed=1;
    IF @SourceProfileCode IS NULL
        THROW 528540, 'RT03_RECOVERY_FINALIZE_SESSION_REJECTED', 1;

    IF
    (
        SELECT COUNT_BIG(*)
        FROM dbo.App_Rt03FullConvergenceDomain WITH(UPDLOCK,HOLDLOCK)
        WHERE RecoveryId=@RecoveryId AND Status=N'COMPLETED'
          AND VerificationHash IS NOT NULL
    )<>5
        THROW 528541, 'RT03_RECOVERY_FINALIZE_DOMAIN_REJECTED', 1;

    IF EXISTS
    (
        SELECT 1 FROM dbo.App_Rt03FullConvergenceMarker WITH(UPDLOCK,HOLDLOCK)
        WHERE RecoveryId=@RecoveryId
    )
        THROW 528542, 'RT03_RECOVERY_MARKER_ALREADY_EXISTS', 1;

    INSERT dbo.App_Rt03FullConvergenceMarker
    (
        RecoveryId,SourceProfileCode,SourceDatabaseGuid,
        AnchorVersion,VerificationHash,CommittedAtUtc
    )
    VALUES
    (
        @RecoveryId,@SourceProfileCode,@SourceDatabaseGuid,
        @AnchorVersion,@VerificationHash,SYSUTCDATETIME()
    );

    DECLARE @MarkerHash binary(32)=HASHBYTES
    (
        'SHA2_256',
        CONCAT
        (
            CONVERT(nvarchar(36),@RecoveryId),N'|',
            @SourceProfileCode,N'|',
            CONVERT(nvarchar(20),@AnchorVersion),N'|',
            @VerificationHash
        )
    );

    INSERT dbo.App_QlhvDirectRealtimeApplyMarker
    (
        CycleId,SourceProfileCode,PlanHash,MarkerHash,DispositionHash,
        SourceDatabaseGuid,SourceChangeTrackingVersion,
        InsertedRows,UpdatedRows,RetainedRows,PreservedQlhvOwnedHash,
        CommittedAtUtc
    )
    VALUES
    (
        @RecoveryId,@SourceProfileCode,@VerificationHash,@MarkerHash,
        @VerificationHash,@SourceDatabaseGuid,@AnchorVersion,
        0,0,0,@VerificationHash,SYSUTCDATETIME()
    );

    UPDATE dbo.App_QlhvDirectRealtimeApplyCheckpoint WITH(UPDLOCK,HOLDLOCK)
    SET SourceChangeTrackingVersion=@AnchorVersion,
        SourceDatabaseGuid=@SourceDatabaseGuid,
        CycleId=@RecoveryId,
        PlanHash=@VerificationHash,
        MarkerHash=@MarkerHash,
        PublishedAtUtc=SYSUTCDATETIME()
    WHERE Mode=N'DIRECT_REALTIME_APPLY'
      AND EnvironmentId=N'PRODUCTION'
      AND SourceProfileCode=@SourceProfileCode
      AND SourceDatabaseGuid=@SourceDatabaseGuid
      AND SourceChangeTrackingVersion=@CheckpointBefore;
    IF @@ROWCOUNT<>1
        THROW 528543, 'RT03_RECOVERY_CHECKPOINT_CAS_REJECTED', 1;

    UPDATE dbo.App_Rt03FullConvergenceSession
    SET Status=N'COMPLETED',CompletedAtUtc=SYSUTCDATETIME(),
        UpdatedAtUtc=SYSUTCDATETIME(),LastErrorCode=NULL
    WHERE RecoveryId=@RecoveryId AND Status=N'VERIFYING';
    IF @@ROWCOUNT<>1
        THROW 528544, 'RT03_RECOVERY_COMPLETION_REJECTED', 1;

    COMMIT TRANSACTION;
END;
GO

SELECT N'RT03_V5_FULL_CONVERGENCE_SCHEMA_READY_NOT_EXECUTED' AS Result;
GO
