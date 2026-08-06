USE [$(Rt03TargetDatabase)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'QLHV_APP'
   AND ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'RT03_V9_DISPOSABLE_REHEARSAL')),0)<>1
    THROW 527730, 'RT03_V9_ROLLBACK_WRONG_DATABASE', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.App_QlhvDirectRealtimeWorkerState
    WHERE WorkerStateId=1 AND CycleActive=1
)
    THROW 527731, 'RT03_V9_ROLLBACK_WORKER_ACTIVE', 1;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'EvidenceContractVersion') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM dbo.App_QlhvDirectRealtimeManualReview
       WHERE EvidenceContractVersion=N'RT03-REVIEWED-RETAINED-1.0'
   )
    THROW 527732, 'RT03_V9_ROLLBACK_REVIEW_EVIDENCE_EXISTS', 1;

BEGIN TRANSACTION;

IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview') AND name=N'UX_App_QlhvDirectRealtimeManualReview_V9EvidenceHash')
    DROP INDEX UX_App_QlhvDirectRealtimeManualReview_V9EvidenceHash ON dbo.App_QlhvDirectRealtimeManualReview;
IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview') AND name=N'UX_App_QlhvDirectRealtimeManualReview_V9Supersedes')
    DROP INDEX UX_App_QlhvDirectRealtimeManualReview_V9Supersedes ON dbo.App_QlhvDirectRealtimeManualReview;
IF OBJECT_ID(N'dbo.CK_App_QlhvDirectRealtimeManualReview_V9Evidence',N'C') IS NOT NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP CONSTRAINT CK_App_QlhvDirectRealtimeManualReview_V9Evidence;
IF OBJECT_ID(N'dbo.FK_App_QlhvDirectRealtimeManualReview_Supersedes',N'F') IS NOT NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP CONSTRAINT FK_App_QlhvDirectRealtimeManualReview_Supersedes;

DECLARE @dropDefaults nvarchar(max)=N'';
SELECT @dropDefaults=@dropDefaults+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(name)+N';'
FROM sys.default_constraints
WHERE parent_object_id IN
(
    OBJECT_ID(N'dbo.App_QlhvDirectRealtimeCycleHistory'),
    OBJECT_ID(N'dbo.App_QlhvDirectRealtimeWorkerState')
)
AND COL_NAME(parent_object_id,parent_column_id) IN
(
    N'ReviewedRetainedCount',N'ReviewedRetainedDomains',N'ActiveReviewCount',
    N'StaleReviewCount',N'NewDriftCount',N'CycleOutcome'
);
IF LEN(@dropDefaults)>0 EXEC sys.sp_executesql @dropDefaults;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'CycleOutcome') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN CycleOutcome;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'NewestActiveReviewUtc') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN NewestActiveReviewUtc;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'OldestActiveReviewUtc') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN OldestActiveReviewUtc;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'NewDriftCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN NewDriftCount;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'StaleReviewCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN StaleReviewCount;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'ActiveReviewCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN ActiveReviewCount;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'ReviewedRetainedDomains') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN ReviewedRetainedDomains;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'ReviewedRetainedCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory DROP COLUMN ReviewedRetainedCount;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'CycleOutcome') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN CycleOutcome;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'NewestActiveReviewUtc') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN NewestActiveReviewUtc;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'OldestActiveReviewUtc') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN OldestActiveReviewUtc;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'NewDriftCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN NewDriftCount;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'StaleReviewCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN StaleReviewCount;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'ActiveReviewCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN ActiveReviewCount;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'ReviewedRetainedDomains') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN ReviewedRetainedDomains;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'ReviewedRetainedCount') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState DROP COLUMN ReviewedRetainedCount;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'DiagnosticId') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN DiagnosticId;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'DecisionEvidenceHash') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN DecisionEvidenceHash;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'SupersedesManualReviewId') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN SupersedesManualReviewId;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'ReviewState') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN ReviewState;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'QlhvOwnedFingerprint') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN QlhvOwnedFingerprint;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'TargetFingerprint') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN TargetFingerprint;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'SourceFingerprint') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN SourceFingerprint;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'EvidenceAnchorVersion') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN EvidenceAnchorVersion;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'ReviewedEventVersion') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN ReviewedEventVersion;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'ReviewedFieldSet') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN ReviewedFieldSet;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'TargetIdentity') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN TargetIdentity;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'SourceBusinessIdentityHash') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN SourceBusinessIdentityHash;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'DomainCode') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN DomainCode;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'EvidenceContractVersion') IS NOT NULL ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview DROP COLUMN EvidenceContractVersion;

COMMIT TRANSACTION;

SELECT N'RT03_V9_SAFE_ROLLBACK_COMPLETE' AS Result;
