USE [$(Rt03TargetDatabase)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME()<>N'QLHV_APP'
   AND ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'RT03_MASTER_DISPOSABLE_REHEARSAL')),0)<>1
    THROW 527810, 'RT03_MASTER_VERIFY_WRONG_DATABASE', 1;

IF OBJECT_ID(N'dbo.App_Rt03RealtimeControl',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_Rt03RealtimeControlAudit',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest',N'U') IS NULL
    THROW 527811, 'RT03_MASTER_OBJECT_MISSING', 1;

IF (SELECT COUNT_BIG(1) FROM dbo.App_Rt03RealtimeControl)<>1
   OR NOT EXISTS(SELECT 1 FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1)
    THROW 527812, 'RT03_MASTER_SINGLETON_REJECTED', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.App_Rt03RealtimeControl
    WHERE State NOT IN(N'OFF',N'ON',N'BLOCKED')
       OR UpdatedAtUtc>DATEADD(second,30,SYSUTCDATETIME())
)
    THROW 527813, 'RT03_MASTER_CONTROL_VALUE_REJECTED', 1;

IF (SELECT COUNT_BIG(1) FROM dbo.App_Rt03RealtimeRunRequest WHERE ActiveSlot=1)>1
    THROW 527814, 'RT03_MASTER_MULTIPLE_ACTIVE_RUNS', 1;

SELECT N'PASS' AS VerificationStatus,
       control.State,control.UpdatedAtUtc,control.UpdatedBy,control.Reason,
       CONVERT(varchar(18),control.RowVersion,1) AS RowVersion,
       (SELECT COUNT_BIG(1) FROM dbo.App_Rt03RealtimeControlAudit) AS AuditRows,
       (SELECT COUNT_BIG(1) FROM dbo.App_Rt03RealtimeRunRequest
        WHERE ActiveSlot=1) AS ActiveRunRequests,
       (SELECT COUNT_BIG(1) FROM dbo.App_QlhvDirectRealtimeManualReview
        WHERE ReviewedEventVersion IN(123,124)) AS HistoricalReviewRows
FROM dbo.App_Rt03RealtimeControl control
WHERE control.ControlId=1;
GO
