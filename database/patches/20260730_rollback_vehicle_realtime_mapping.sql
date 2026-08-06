/*
    NOT EXECUTED BY THIS TASK.
    Destructive rollback is allowed only before vehicle baseline/backfill,
    checkpoint activation, event/manual-review history or assignment references.
    Once any such data exists, disable the writer and roll forward instead.
*/
USE [QLHV_APP];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME()<>N'QLHV_APP' OR NOT EXISTS
(
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
)
    THROW 527930, 'VEHICLE_ROLLBACK_TARGET_IDENTITY_REJECTED', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.App_XeTap
    WHERE SourceProfileCode IS NOT NULL
       OR SourceBienSoXe IS NOT NULL
       OR SourceRowHash IS NOT NULL
)
    THROW 527931, 'VEHICLE_ROLLBACK_HAS_SOURCE_DATA_ROLL_FORWARD_REQUIRED', 1;

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeCheckpoint',N'U') IS NOT NULL
   AND EXISTS(SELECT 1 FROM dbo.App_XeTap_RealtimeCheckpoint)
    THROW 527932, 'VEHICLE_ROLLBACK_HAS_CHECKPOINT_ROLL_FORWARD_REQUIRED', 1;

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeEvent',N'U') IS NOT NULL
   AND EXISTS(SELECT 1 FROM dbo.App_XeTap_RealtimeEvent)
    THROW 527933, 'VEHICLE_ROLLBACK_HAS_EVENT_HISTORY_ROLL_FORWARD_REQUIRED', 1;

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeManualReview',N'U') IS NOT NULL
   AND EXISTS(SELECT 1 FROM dbo.App_XeTap_RealtimeManualReview)
    THROW 527934, 'VEHICLE_ROLLBACK_HAS_REVIEW_HISTORY_ROLL_FORWARD_REQUIRED', 1;
GO

IF OBJECT_ID(N'dbo.App_XeTap_RealtimeManualReview',N'U') IS NOT NULL
    DROP TABLE dbo.App_XeTap_RealtimeManualReview;
IF OBJECT_ID(N'dbo.App_XeTap_RealtimeEvent',N'U') IS NOT NULL
    DROP TABLE dbo.App_XeTap_RealtimeEvent;
IF OBJECT_ID(N'dbo.App_XeTap_RealtimeCheckpoint',N'U') IS NOT NULL
    DROP TABLE dbo.App_XeTap_RealtimeCheckpoint;
GO

IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
            AND name=N'IX_App_XeTap_SourceLifecycle')
    DROP INDEX IX_App_XeTap_SourceLifecycle ON dbo.App_XeTap;
IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
            AND name=N'IX_App_XeTap_NormalizedBienSoXe')
    DROP INDEX IX_App_XeTap_NormalizedBienSoXe ON dbo.App_XeTap;
IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
            AND name=N'UX_App_XeTap_SourceIdentity')
    DROP INDEX UX_App_XeTap_SourceIdentity ON dbo.App_XeTap;
GO

IF OBJECT_ID(N'dbo.CK_App_XeTap_ManualReviewPair',N'C') IS NOT NULL
    ALTER TABLE dbo.App_XeTap DROP CONSTRAINT CK_App_XeTap_ManualReviewPair;
IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceMissing',N'C') IS NOT NULL
    ALTER TABLE dbo.App_XeTap DROP CONSTRAINT CK_App_XeTap_SourceMissing;
IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceImagePathHash',N'C') IS NOT NULL
    ALTER TABLE dbo.App_XeTap DROP CONSTRAINT CK_App_XeTap_SourceImagePathHash;
IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceRowHash',N'C') IS NOT NULL
    ALTER TABLE dbo.App_XeTap DROP CONSTRAINT CK_App_XeTap_SourceRowHash;
IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceLifecycle',N'C') IS NOT NULL
    ALTER TABLE dbo.App_XeTap DROP CONSTRAINT CK_App_XeTap_SourceLifecycle;
IF OBJECT_ID(N'dbo.CK_App_XeTap_SourceIdentityPair',N'C') IS NOT NULL
    ALTER TABLE dbo.App_XeTap DROP CONSTRAINT CK_App_XeTap_SourceIdentityPair;
GO

DECLARE @DropColumns nvarchar(max)=N'';
SELECT @DropColumns=STRING_AGG(QUOTENAME(columnName),N',')
FROM
(
    VALUES
      (N'SourceProfileCode'),(N'SourceBienSoXe'),(N'NormalizedBienSoXe'),
      (N'NormalizedSoDK'),(N'NormalizedSoKhung'),(N'NormalizedSoDongCo'),
      (N'MaCSDT'),(N'MaSoGTVT'),(N'SourceRowHash'),(N'SourceTrangThai'),
      (N'SourceLifecycle'),(N'SourceCtVersion'),(N'SourceLastSeenAt'),
      (N'SourceMissingSince'),(N'ManualReviewCode'),(N'ManualReviewAt'),
      (N'SourceCreatedBy'),(N'SourceUpdatedBy'),(N'SourceCreatedAt'),
      (N'SourceUpdatedAt'),(N'SourceImagePathHash'),
      (N'SourceMaFileTiepNhanXml'),(N'SourceThoiGianTiepNhanXml')
) requested(columnName)
WHERE COL_LENGTH(N'dbo.App_XeTap',columnName) IS NOT NULL;

IF NULLIF(@DropColumns,N'') IS NOT NULL
    EXEC(N'ALTER TABLE dbo.App_XeTap DROP COLUMN '+@DropColumns+N';');
GO

SELECT N'VEHICLE_REALTIME_TARGET_SCHEMA_ROLLED_BACK_NO_DATA' AS Result;
GO
