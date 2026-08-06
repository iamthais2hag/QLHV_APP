USE [QLHV_APP];
GO

/*
RT-03 Task 2 control-plane schema. DO NOT execute during Task 1.
Creates only RT-03 feature/marker/checkpoint/manual-review metadata. It does not
modify dbo.App_HocVien and seeds every production feature flag as false.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
   OR DB_ID() <> 12
   OR NOT EXISTS
      (
          SELECT 1 FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
      )
    THROW 527560, 'RT03_PRODUCTION_IDENTITY_REJECTED: QLHV_APP control plane.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeFeatureState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeFeatureState
        (
            FeatureStateId tinyint NOT NULL
                CONSTRAINT PK_App_QlhvDirectRealtimeFeatureState PRIMARY KEY,
            EnableProductionRealtime bit NOT NULL,
            EnableProductionShadow bit NOT NULL,
            EnableProductionWrites bit NOT NULL,
            EnableProductionCanary bit NOT NULL,
            EnableControlledCutover bit NOT NULL,
            EnableProductionDeletes bit NOT NULL,
            UpdatedAtUtc datetime2(7) NOT NULL,
            UpdatedBy nvarchar(100) NOT NULL,
            Version rowversion NOT NULL,
            CONSTRAINT CK_App_QlhvDirectRealtimeFeatureState_Singleton
                CHECK (FeatureStateId = 1),
            CONSTRAINT CK_App_QlhvDirectRealtimeFeatureState_DeleteForbidden
                CHECK (EnableProductionDeletes = 0)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.App_QlhvDirectRealtimeFeatureState
        WHERE FeatureStateId = 1
    )
    BEGIN
        INSERT INTO dbo.App_QlhvDirectRealtimeFeatureState
        (
            FeatureStateId, EnableProductionRealtime, EnableProductionShadow,
            EnableProductionWrites, EnableProductionCanary,
            EnableControlledCutover, EnableProductionDeletes,
            UpdatedAtUtc, UpdatedBy
        )
        VALUES (1, 0, 0, 0, 0, 0, 0, SYSUTCDATETIME(), N'RT03_SCHEMA_DEFAULT');
    END;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeApplyMarker
        (
            CycleId uniqueidentifier NOT NULL
                CONSTRAINT PK_App_QlhvDirectRealtimeApplyMarker PRIMARY KEY,
            SourceProfileCode nvarchar(50) NOT NULL,
            PlanHash char(64) NOT NULL,
            MarkerHash binary(32) NOT NULL,
            DispositionHash char(64) NOT NULL,
            SourceDatabaseGuid uniqueidentifier NOT NULL,
            SourceChangeTrackingVersion bigint NOT NULL,
            InsertedRows int NOT NULL,
            UpdatedRows int NOT NULL,
            RetainedRows int NOT NULL,
            PreservedQlhvOwnedHash char(64) NOT NULL,
            CommittedAtUtc datetime2(7) NOT NULL,
            CONSTRAINT CK_App_QlhvDirectRealtimeApplyMarker_Profile
                CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_QlhvDirectRealtimeApplyMarker_Counts
                CHECK
                (
                    InsertedRows BETWEEN 0 AND 1
                    AND UpdatedRows BETWEEN 0 AND 1
                    AND RetainedRows BETWEEN 0 AND 1
                )
        );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeApplyCheckpoint
        (
            SourceProfileCode nvarchar(50) NOT NULL,
            Mode nvarchar(40) NOT NULL,
            MappingFingerprint char(64) NOT NULL,
            EnvironmentId nvarchar(40) NOT NULL,
            SourceDatabaseGuid uniqueidentifier NOT NULL,
            SourceChangeTrackingVersion bigint NOT NULL,
            CycleId uniqueidentifier NOT NULL,
            PlanHash char(64) NOT NULL,
            MarkerHash binary(32) NOT NULL,
            PublishedAtUtc datetime2(7) NOT NULL,
            Version rowversion NOT NULL,
            CONSTRAINT PK_App_QlhvDirectRealtimeApplyCheckpoint PRIMARY KEY
            (
                SourceProfileCode,
                Mode,
                MappingFingerprint,
                EnvironmentId
            ),
            CONSTRAINT UQ_App_QlhvDirectRealtimeApplyCheckpoint_Cycle
                UNIQUE (CycleId),
            CONSTRAINT FK_App_QlhvDirectRealtimeApplyCheckpoint_Marker
                FOREIGN KEY (CycleId)
                REFERENCES dbo.App_QlhvDirectRealtimeApplyMarker (CycleId),
            CONSTRAINT CK_App_QlhvDirectRealtimeApplyCheckpoint_Profile
                CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_QlhvDirectRealtimeApplyCheckpoint_Mode
                CHECK (Mode = N'DIRECT_REALTIME_APPLY'),
            CONSTRAINT CK_App_QlhvDirectRealtimeApplyCheckpoint_Environment
                CHECK (EnvironmentId = N'PRODUCTION'),
            CONSTRAINT CK_App_QlhvDirectRealtimeApplyCheckpoint_Version
                CHECK (SourceChangeTrackingVersion >= 0)
        );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeManualReview
        (
            ManualReviewId bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_App_QlhvDirectRealtimeManualReview PRIMARY KEY,
            CycleId uniqueidentifier NOT NULL,
            PlanHash char(64) NOT NULL,
            CandidateId nvarchar(100) NOT NULL,
            SourceProfileCode nvarchar(50) NOT NULL,
            IdentityHmac nvarchar(100) NOT NULL,
            Classification nvarchar(60) NOT NULL,
            RollbackImageHash char(64) NOT NULL,
            TargetRetainedActive bit NOT NULL,
            TargetMutated bit NOT NULL,
            CreatedAtUtc datetime2(7) NOT NULL,
            CONSTRAINT UQ_App_QlhvDirectRealtimeManualReview_Candidate
                UNIQUE (CycleId, CandidateId),
            CONSTRAINT CK_App_QlhvDirectRealtimeManualReview_Profile
                CHECK (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')),
            CONSTRAINT CK_App_QlhvDirectRealtimeManualReview_Retention
                CHECK (TargetRetainedActive = 1 AND TargetMutated = 0)
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF EXISTS
(
    SELECT 1
    FROM dbo.App_QlhvDirectRealtimeFeatureState
    WHERE FeatureStateId = 1
      AND
      (
          EnableProductionRealtime <> 0
          OR EnableProductionShadow <> 0
          OR EnableProductionWrites <> 0
          OR EnableProductionCanary <> 0
          OR EnableControlledCutover <> 0
          OR EnableProductionDeletes <> 0
      )
)
    THROW 527561, 'RT03_CONTROL_PLANE_DEFAULT_STATE_REJECTED.', 1;

SELECT N'RT03_CONTROL_PLANE_CREATED_FLAGS_OFF' AS Evidence;
GO
