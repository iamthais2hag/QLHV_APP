USE [QLHV_APP];
GO

/* RT-03 production registration metadata only. All profile flags seed OFF. */
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
    THROW 527580, 'RT03_PRODUCTION_IDENTITY_REJECTED: registration state.', 1;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeProfileState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeProfileState
        (
            SourceProfileCode nvarchar(50) NOT NULL
                CONSTRAINT PK_App_QlhvDirectRealtimeProfileState PRIMARY KEY,
            Enabled bit NOT NULL,
            SequenceOrder tinyint NOT NULL,
            ExpectedMappingFingerprint char(64) NOT NULL,
            ExpectedSourceSchemaFingerprint char(64) NOT NULL,
            ExpectedTargetSchemaFingerprint char(64) NOT NULL,
            LastStatus nvarchar(50) NULL,
            LastSuccessfulCycleId uniqueidentifier NULL,
            LastCheckpointVersion bigint NULL,
            LastCycleCompletedAtUtc datetime2(7) NULL,
            ActivatedAtUtc datetime2(7) NULL,
            UpdatedAtUtc datetime2(7) NOT NULL,
            Version rowversion NOT NULL,
            CONSTRAINT CK_App_QlhvDirectRealtimeProfileState_Profile
                CHECK
                (
                    (SourceProfileCode=N'CSDT_OTO' AND SequenceOrder=1)
                    OR (SourceProfileCode=N'CSDT_MOTO' AND SequenceOrder=2)
                )
        );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeWorkerState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeWorkerState
        (
            WorkerStateId tinyint NOT NULL
                CONSTRAINT PK_App_QlhvDirectRealtimeWorkerState PRIMARY KEY,
            InstanceId nvarchar(64) NOT NULL,
            Status nvarchar(30) NOT NULL,
            CurrentProfile nvarchar(50) NULL,
            CycleActive bit NOT NULL,
            StartedAtUtc datetime2(7) NULL,
            StoppedAtUtc datetime2(7) NULL,
            LastHeartbeatUtc datetime2(7) NULL,
            LastSuccessfulCycleId uniqueidentifier NULL,
            LastErrorCode nvarchar(100) NULL,
            Version rowversion NOT NULL,
            CONSTRAINT CK_App_QlhvDirectRealtimeWorkerState_Singleton
                CHECK (WorkerStateId=1),
            CONSTRAINT CK_App_QlhvDirectRealtimeWorkerState_Profile
                CHECK (CurrentProfile IS NULL OR CurrentProfile IN (N'CSDT_OTO',N'CSDT_MOTO'))
        );
    END;

    IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeCycleHistory', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.App_QlhvDirectRealtimeCycleHistory
        (
            CycleId uniqueidentifier NOT NULL
                CONSTRAINT PK_App_QlhvDirectRealtimeCycleHistory PRIMARY KEY,
            WorkerInstanceId nvarchar(64) NOT NULL,
            SourceProfileCode nvarchar(50) NOT NULL,
            Status nvarchar(50) NOT NULL,
            CheckpointBefore bigint NOT NULL,
            CheckpointAfter bigint NOT NULL,
            InsertedRows int NOT NULL,
            UpdatedRows int NOT NULL,
            DeletedOrDeactivatedRows int NOT NULL,
            DuplicateActiveRows int NOT NULL,
            CompletedAtUtc datetime2(7) NOT NULL,
            CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Profile
                CHECK (SourceProfileCode IN (N'CSDT_OTO',N'CSDT_MOTO')),
            CONSTRAINT CK_App_QlhvDirectRealtimeCycleHistory_Mutations
                CHECK
                (
                    InsertedRows BETWEEN 0 AND 1
                    AND UpdatedRows BETWEEN 0 AND 1
                    AND InsertedRows + UpdatedRows <= 1
                    AND DeletedOrDeactivatedRows=0
                    AND DuplicateActiveRows=0
                    AND CheckpointAfter>=CheckpointBefore
                )
        );
        CREATE INDEX IX_App_QlhvDirectRealtimeCycleHistory_ProfileCompleted
            ON dbo.App_QlhvDirectRealtimeCycleHistory
               (SourceProfileCode, CompletedAtUtc DESC);
    END;

    IF NOT EXISTS
       (SELECT 1 FROM dbo.App_QlhvDirectRealtimeProfileState WHERE SourceProfileCode=N'CSDT_OTO')
        INSERT dbo.App_QlhvDirectRealtimeProfileState
        (
            SourceProfileCode, Enabled, SequenceOrder,
            ExpectedMappingFingerprint, ExpectedSourceSchemaFingerprint,
            ExpectedTargetSchemaFingerprint, UpdatedAtUtc
        )
        VALUES
        (
            N'CSDT_OTO',0,1,
            '7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9',
            'b406355371fc6ecda51156cf0c10f525288d5fd0a083fc97a6b1c175cc859bb8',
            '21075486fa8c34a59feada9bc82b1b2a8278240e980f97eaadc963aec6c51380',
            SYSUTCDATETIME()
        );

    IF NOT EXISTS
       (SELECT 1 FROM dbo.App_QlhvDirectRealtimeProfileState WHERE SourceProfileCode=N'CSDT_MOTO')
        INSERT dbo.App_QlhvDirectRealtimeProfileState
        (
            SourceProfileCode, Enabled, SequenceOrder,
            ExpectedMappingFingerprint, ExpectedSourceSchemaFingerprint,
            ExpectedTargetSchemaFingerprint, UpdatedAtUtc
        )
        VALUES
        (
            N'CSDT_MOTO',0,2,
            '7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9',
            'b406355371fc6ecda51156cf0c10f525288d5fd0a083fc97a6b1c175cc859bb8',
            '21075486fa8c34a59feada9bc82b1b2a8278240e980f97eaadc963aec6c51380',
            SYSUTCDATETIME()
        );

    IF NOT EXISTS
       (SELECT 1 FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1)
        INSERT dbo.App_QlhvDirectRealtimeWorkerState
            (WorkerStateId, InstanceId, Status, CycleActive)
        VALUES (1,N'UNREGISTERED',N'STOPPED',0);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF (SELECT COUNT(1) FROM dbo.App_QlhvDirectRealtimeProfileState)<>2
   OR EXISTS (SELECT 1 FROM dbo.App_QlhvDirectRealtimeProfileState WHERE Enabled<>0)
   OR NOT EXISTS
      (SELECT 1 FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1 AND CycleActive=0)
    THROW 527581, 'RT03_REGISTRATION_DEFAULT_STATE_REJECTED.', 1;

SELECT N'RT03_PRODUCTION_REGISTRATION_STATE_CREATED_FLAGS_OFF' AS Evidence;
GO
