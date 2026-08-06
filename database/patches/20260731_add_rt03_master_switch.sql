USE [$(Rt03TargetDatabase)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF DB_NAME()<>N'QLHV_APP'
   AND ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'RT03_MASTER_DISPOSABLE_REHEARSAL')),0)<>1
    THROW 527800, 'RT03_MASTER_WRONG_DATABASE', 1;

IF OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvDirectRealtimeWorkerState',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview',N'U') IS NULL
    THROW 527801, 'RT03_MASTER_PREREQUISITE_MISSING', 1;

BEGIN TRANSACTION;

DECLARE @InitializedControl bit=0;

IF OBJECT_ID(N'dbo.App_Rt03RealtimeControl',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Rt03RealtimeControl
    (
        ControlId tinyint NOT NULL
            CONSTRAINT PK_App_Rt03RealtimeControl PRIMARY KEY,
        State nvarchar(10) NOT NULL,
        UpdatedAtUtc datetime2(7) NOT NULL,
        UpdatedBy nvarchar(100) NOT NULL,
        Reason nvarchar(100) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_App_Rt03RealtimeControl_Singleton CHECK(ControlId=1),
        CONSTRAINT CK_App_Rt03RealtimeControl_State
            CHECK(State IN(N'OFF',N'ON',N'BLOCKED'))
    );
END;

IF OBJECT_ID(N'dbo.App_Rt03RealtimeControlAudit',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Rt03RealtimeControlAudit
    (
        AuditId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_App_Rt03RealtimeControlAudit PRIMARY KEY,
        BeforeState nvarchar(10) NOT NULL,
        AfterState nvarchar(10) NOT NULL,
        Action nvarchar(40) NOT NULL,
        Actor nvarchar(100) NOT NULL,
        Reason nvarchar(100) NULL,
        OccurredAtUtc datetime2(7) NOT NULL,
        BeforeRowVersion binary(8) NOT NULL,
        AfterRowVersion binary(8) NOT NULL,
        CONSTRAINT CK_App_Rt03RealtimeControlAudit_State
            CHECK(BeforeState IN(N'OFF',N'ON',N'BLOCKED')
              AND AfterState IN(N'OFF',N'ON',N'BLOCKED'))
    );
END;

IF OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Rt03RealtimeRunRequest
    (
        RunRequestId uniqueidentifier NOT NULL
            CONSTRAINT PK_App_Rt03RealtimeRunRequest PRIMARY KEY,
        Status nvarchar(20) NOT NULL,
        RequestedBy nvarchar(100) NOT NULL,
        RequestedAtUtc datetime2(7) NOT NULL,
        StartedAtUtc datetime2(7) NULL,
        CompletedAtUtc datetime2(7) NULL,
        WorkerInstanceId nvarchar(64) NULL,
        Outcome nvarchar(100) NULL,
        Reason nvarchar(100) NULL,
        ActiveSlot bit NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_App_Rt03RealtimeRunRequest_Status CHECK
        (
            (Status IN(N'PENDING',N'RUNNING') AND ActiveSlot=1 AND CompletedAtUtc IS NULL)
            OR
            (Status IN(N'COMPLETED',N'BLOCKED') AND ActiveSlot IS NULL
             AND CompletedAtUtc IS NOT NULL)
        )
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest')
      AND name=N'UX_App_Rt03RealtimeRunRequest_ActiveSlot'
)
    CREATE UNIQUE INDEX UX_App_Rt03RealtimeRunRequest_ActiveSlot
        ON dbo.App_Rt03RealtimeRunRequest(ActiveSlot)
        WHERE ActiveSlot=1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest')
      AND name=N'IX_App_Rt03RealtimeRunRequest_RequestedAtUtc'
)
    CREATE INDEX IX_App_Rt03RealtimeRunRequest_RequestedAtUtc
        ON dbo.App_Rt03RealtimeRunRequest(RequestedAtUtc DESC);

IF NOT EXISTS(SELECT 1 FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1)
BEGIN
    INSERT dbo.App_Rt03RealtimeControl
        (ControlId,State,UpdatedAtUtc,UpdatedBy,Reason)
    VALUES
        (1,N'OFF',SYSUTCDATETIME(),N'SCHEMA_MIGRATION',N'DEFAULT_OFF');
    SET @InitializedControl=1;
END;

IF (SELECT COUNT_BIG(1) FROM dbo.App_Rt03RealtimeControl)<>1
   OR (@InitializedControl=1 AND NOT EXISTS
      (SELECT 1 FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1 AND State=N'OFF'))
    THROW 527802, 'RT03_MASTER_DEFAULT_OFF_REJECTED', 1;

IF (SELECT COUNT(1) FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.App_Rt03RealtimeControl'))<>6
   OR COL_LENGTH(N'dbo.App_Rt03RealtimeControl',N'RowVersion')<>8
   OR (SELECT COUNT(1) FROM sys.columns
       WHERE object_id=OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest'))<>11
   OR NOT EXISTS
      (SELECT 1 FROM sys.indexes
       WHERE object_id=OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest')
         AND name=N'UX_App_Rt03RealtimeRunRequest_ActiveSlot' AND is_unique=1)
    THROW 527803, 'RT03_MASTER_SCHEMA_MISMATCH', 1;

COMMIT TRANSACTION;

SELECT N'PASS' AS MigrationStatus,
       State,UpdatedAtUtc,UpdatedBy,Reason,
       CONVERT(varchar(18),RowVersion,1) AS RowVersion
FROM dbo.App_Rt03RealtimeControl
WHERE ControlId=1;
GO
