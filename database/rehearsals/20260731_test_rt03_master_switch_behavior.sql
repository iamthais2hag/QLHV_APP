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
EXEC sys.sp_set_session_context
    @key=N'RT03_MASTER_DISPOSABLE_REHEARSAL',@value=1;

DECLARE @InitialReviewHash varbinary(32)=HASHBYTES(N'SHA2_256',
    (SELECT * FROM dbo.App_QlhvDirectRealtimeManualReview ORDER BY ManualReviewId FOR JSON PATH));
DECLARE @InitialCheckpointHash varbinary(32)=HASHBYTES(N'SHA2_256',
    (SELECT * FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
     ORDER BY SourceProfileCode FOR JSON PATH));
DECLARE @RowVersion binary(8),@NewRowVersion binary(8),@Rows int;
SELECT @RowVersion=RowVersion FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1;

BEGIN TRANSACTION;
UPDATE dbo.App_Rt03RealtimeControl
SET State=N'ON',UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'REHEARSAL',Reason=N'CAS_PASS'
WHERE ControlId=1 AND RowVersion=@RowVersion;
IF @@ROWCOUNT<>1 THROW 527851,'RT03_MASTER_REHEARSAL_CAS_FIRST_WRITE_FAILED',1;
SELECT @NewRowVersion=RowVersion FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1;
UPDATE dbo.App_Rt03RealtimeControl
SET State=N'OFF',UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'STALE',Reason=N'CAS_MUST_FAIL'
WHERE ControlId=1 AND RowVersion=@RowVersion;
SET @Rows=@@ROWCOUNT;
IF @Rows<>0 THROW 527852,'RT03_MASTER_REHEARSAL_STALE_ROWVERSION_ACCEPTED',1;
UPDATE dbo.App_Rt03RealtimeControl
SET State=N'OFF',UpdatedAtUtc=SYSUTCDATETIME(),UpdatedBy=N'REHEARSAL',Reason=N'CAS_PASS'
WHERE ControlId=1 AND RowVersion=@NewRowVersion;
IF @@ROWCOUNT<>1 THROW 527853,'RT03_MASTER_REHEARSAL_CAS_SECOND_WRITE_FAILED',1;
COMMIT TRANSACTION;

DECLARE @RunId uniqueidentifier=NEWID();
INSERT dbo.App_Rt03RealtimeRunRequest
    (RunRequestId,Status,RequestedBy,RequestedAtUtc,ActiveSlot)
VALUES(@RunId,N'PENDING',N'REHEARSAL',SYSUTCDATETIME(),1);
BEGIN TRY
    INSERT dbo.App_Rt03RealtimeRunRequest
        (RunRequestId,Status,RequestedBy,RequestedAtUtc,ActiveSlot)
    VALUES(NEWID(),N'PENDING',N'REHEARSAL_CONCURRENT',SYSUTCDATETIME(),1);
    THROW 527854,'RT03_MASTER_REHEARSAL_MULTIPLE_ACTIVE_ACCEPTED',1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER()=527854 THROW;
END CATCH;
UPDATE dbo.App_Rt03RealtimeRunRequest
SET Status=N'COMPLETED',CompletedAtUtc=SYSUTCDATETIME(),
    Outcome=N'NO_CHANGE',ActiveSlot=NULL
WHERE RunRequestId=@RunId;

IF @InitialReviewHash<>HASHBYTES(N'SHA2_256',
    (SELECT * FROM dbo.App_QlhvDirectRealtimeManualReview ORDER BY ManualReviewId FOR JSON PATH))
    THROW 527855,'RT03_MASTER_REHEARSAL_REVIEW_MUTATED',1;
IF @InitialCheckpointHash<>HASHBYTES(N'SHA2_256',
    (SELECT * FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
     ORDER BY SourceProfileCode FOR JSON PATH))
    THROW 527856,'RT03_MASTER_REHEARSAL_CHECKPOINT_MUTATED',1;
IF NOT EXISTS(SELECT 1 FROM dbo.App_Rt03RealtimeControl WHERE ControlId=1 AND State=N'OFF')
    THROW 527857,'RT03_MASTER_REHEARSAL_NOT_OFF',1;

SELECT N'PASS' AS BehavioralRehearsal,
       N'OFF_DEFAULT_IDEMPOTENT_CAS_RUNONCE_UNIQUE' AS Contract,
       CONVERT(varchar(18),@RowVersion,1) AS InitialRowVersion,
       CONVERT(varchar(18),(SELECT RowVersion FROM dbo.App_Rt03RealtimeControl
                            WHERE ControlId=1),1) AS FinalRowVersion,
       (SELECT COUNT_BIG(1) FROM dbo.App_Rt03RealtimeRunRequest
        WHERE ActiveSlot=1) AS ActiveRunRequests;
GO
