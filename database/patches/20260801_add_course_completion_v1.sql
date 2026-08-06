USE [$(CourseCompletionTargetDatabase)];
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
GO

IF OBJECT_ID(N'dbo.App_KhoaHoc',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_AuditLog',N'U') IS NULL
    THROW 52100, 'COURSE_COMPLETION_V1_PREREQUISITE_MISSING', 1;
GO

DECLARE @Existing int =
    IIF(OBJECT_ID(N'dbo.App_CourseCompletion',N'U') IS NULL,0,1) +
    IIF(OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot',N'U') IS NULL,0,1) +
    IIF(OBJECT_ID(N'dbo.App_CourseCompletionOperation',N'U') IS NULL,0,1);

IF @Existing NOT IN (0,3)
    THROW 52101, 'COURSE_COMPLETION_V1_PARTIAL_SCHEMA_DRIFT', 1;

IF @Existing = 0
BEGIN
    BEGIN TRANSACTION;

    CREATE TABLE dbo.App_CourseCompletion
    (
        CourseCompletionId bigint IDENTITY(1,1) NOT NULL,
        KhoaHocId bigint NOT NULL,
        SourceProfileCode nvarchar(20) NOT NULL,
        SourceCourseKey nvarchar(50) NOT NULL,
        ContractVersion nvarchar(20) NOT NULL,
        Status nvarchar(20) NOT NULL,
        CompletionBusinessDate date NOT NULL,
        SourceSnapshotHash char(64) NOT NULL,
        LearnerCount int NOT NULL,
        CompletedAtUtc datetime2(7) NOT NULL,
        CompletedBy nvarchar(100) NOT NULL,
        CompletionReason nvarchar(500) NOT NULL,
        CreatedOperationId uniqueidentifier NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_CourseCompletion PRIMARY KEY (CourseCompletionId),
        CONSTRAINT FK_App_CourseCompletion_KhoaHoc FOREIGN KEY (KhoaHocId)
            REFERENCES dbo.App_KhoaHoc(KhoaHocId) ON DELETE NO ACTION,
        CONSTRAINT UQ_App_CourseCompletion_SourceIdentity
            UNIQUE (SourceProfileCode,SourceCourseKey),
        CONSTRAINT UQ_App_CourseCompletion_CreatedOperation UNIQUE (CreatedOperationId),
        CONSTRAINT CK_App_CourseCompletion_Profile
            CHECK (SourceProfileCode IN (N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_CourseCompletion_Status CHECK (Status=N'COMPLETED'),
        CONSTRAINT CK_App_CourseCompletion_Contract CHECK (ContractVersion=N'1.0'),
        CONSTRAINT CK_App_CourseCompletion_Hash
            CHECK (SourceSnapshotHash NOT LIKE '%[^0-9A-F]%' AND LEN(SourceSnapshotHash)=64),
        CONSTRAINT CK_App_CourseCompletion_LearnerCount CHECK (LearnerCount>0),
        CONSTRAINT CK_App_CourseCompletion_Reason
            CHECK (LEN(LTRIM(RTRIM(CompletionReason))) BETWEEN 1 AND 500),
        CONSTRAINT CK_App_CourseCompletion_Actor
            CHECK (LEN(LTRIM(RTRIM(CompletedBy))) BETWEEN 1 AND 100)
    );

    CREATE UNIQUE INDEX UX_App_CourseCompletion_KhoaHocId
        ON dbo.App_CourseCompletion(KhoaHocId);

    CREATE TABLE dbo.App_CourseCompletionLearnerSnapshot
    (
        SnapshotId bigint IDENTITY(1,1) NOT NULL,
        CourseCompletionId bigint NOT NULL,
        SourceProfileCode nvarchar(20) NOT NULL,
        SourceCourseKey nvarchar(50) NOT NULL,
        ProtectedLearnerIdentity char(64) NOT NULL,
        MaKhoaHoc nvarchar(50) NOT NULL,
        TT_XuLy nvarchar(10) NOT NULL,
        LearnerClassification nvarchar(40) NOT NULL,
        ResultCompletenessClassification nvarchar(40) NOT NULL,
        DownstreamClassification nvarchar(40) NOT NULL,
        CanonicalLearnerRowHash char(64) NOT NULL,
        SnapshotAtUtc datetime2(7) NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_CourseCompletionLearnerSnapshot PRIMARY KEY (SnapshotId),
        CONSTRAINT FK_App_CourseCompletionLearnerSnapshot_Completion
            FOREIGN KEY (CourseCompletionId)
            REFERENCES dbo.App_CourseCompletion(CourseCompletionId) ON DELETE NO ACTION,
        CONSTRAINT UQ_App_CourseCompletionLearnerSnapshot_Identity
            UNIQUE (CourseCompletionId,ProtectedLearnerIdentity),
        CONSTRAINT CK_App_CourseCompletionLearnerSnapshot_Profile
            CHECK (SourceProfileCode IN (N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_CourseCompletionLearnerSnapshot_Hashes
            CHECK (ProtectedLearnerIdentity NOT LIKE '%[^0-9A-F]%'
               AND CanonicalLearnerRowHash NOT LIKE '%[^0-9A-F]%'
               AND LEN(ProtectedLearnerIdentity)=64
               AND LEN(CanonicalLearnerRowHash)=64),
        CONSTRAINT CK_App_CourseCompletionLearnerSnapshot_Status
            CHECK (TT_XuLy IN (N'09',N'10',N'11',N'12',N'13',N'14',N'15',N'16',N'17',N'18',N'19')),
        CONSTRAINT CK_App_CourseCompletionLearnerSnapshot_Class
            CHECK (LearnerClassification IN (N'PASSED',N'FAILED',N'DOWNSTREAM')),
        CONSTRAINT CK_App_CourseCompletionLearnerSnapshot_Result
            CHECK (ResultCompletenessClassification IN (N'COMPLETE',N'READ_ONLY_DOWNSTREAM'))
    );

    CREATE INDEX IX_App_CourseCompletionLearnerSnapshot_Completion
        ON dbo.App_CourseCompletionLearnerSnapshot(CourseCompletionId)
        INCLUDE (ProtectedLearnerIdentity,CanonicalLearnerRowHash,TT_XuLy);

    CREATE TABLE dbo.App_CourseCompletionOperation
    (
        OperationId uniqueidentifier NOT NULL,
        SourceProfileCode nvarchar(20) NOT NULL,
        SourceCourseKey nvarchar(50) NOT NULL,
        ActorId nvarchar(100) NOT NULL,
        IdempotencyKeyHash binary(32) NOT NULL,
        RequestFingerprint char(64) NOT NULL,
        PreviewSnapshotHash char(64) NOT NULL,
        ResultCode nvarchar(40) NOT NULL,
        CourseCompletionId bigint NULL,
        CreatedAtUtc datetime2(7) NOT NULL,
        CompletedAtUtc datetime2(7) NULL,
        ErrorCode nvarchar(80) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT PK_App_CourseCompletionOperation PRIMARY KEY (OperationId),
        CONSTRAINT FK_App_CourseCompletionOperation_Completion
            FOREIGN KEY (CourseCompletionId)
            REFERENCES dbo.App_CourseCompletion(CourseCompletionId) ON DELETE NO ACTION,
        CONSTRAINT UQ_App_CourseCompletionOperation_Idempotency
            UNIQUE (SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash),
        CONSTRAINT CK_App_CourseCompletionOperation_Profile
            CHECK (SourceProfileCode IN (N'CSDT_OTO',N'CSDT_MOTO')),
        CONSTRAINT CK_App_CourseCompletionOperation_Result
            CHECK (ResultCode IN (N'COMPLETED',N'NO_CHANGE')),
        CONSTRAINT CK_App_CourseCompletionOperation_Hashes
            CHECK (RequestFingerprint NOT LIKE '%[^0-9A-F]%'
               AND PreviewSnapshotHash NOT LIKE '%[^0-9A-F]%'
               AND LEN(RequestFingerprint)=64 AND LEN(PreviewSnapshotHash)=64),
        CONSTRAINT CK_App_CourseCompletionOperation_Actor
            CHECK (LEN(LTRIM(RTRIM(ActorId))) BETWEEN 1 AND 100)
    );

    COMMIT TRANSACTION;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api') IS NULL
    CREATE ROLE qlhv_course_completion_api AUTHORIZATION dbo;
GO
GRANT SELECT,INSERT ON OBJECT::dbo.App_CourseCompletion TO qlhv_course_completion_api;
GRANT SELECT,INSERT ON OBJECT::dbo.App_CourseCompletionLearnerSnapshot TO qlhv_course_completion_api;
GRANT SELECT,INSERT ON OBJECT::dbo.App_CourseCompletionOperation TO qlhv_course_completion_api;
GRANT SELECT ON OBJECT::dbo.App_KhoaHoc TO qlhv_course_completion_api;
GRANT INSERT ON OBJECT::dbo.App_AuditLog TO qlhv_course_completion_api;
GO

:r .\20260801_verify_course_completion_v1.sql
