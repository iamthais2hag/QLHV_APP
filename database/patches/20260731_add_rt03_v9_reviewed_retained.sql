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

IF DB_NAME() <> N'QLHV_APP'
   AND ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'RT03_V9_DISPOSABLE_REHEARSAL')),0)<>1
    THROW 527700, 'RT03_V9_WRONG_DATABASE', 1;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'EvidenceContractVersion') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD EvidenceContractVersion nvarchar(64) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'DomainCode') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD DomainCode nvarchar(60) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'SourceBusinessIdentityHash') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD SourceBusinessIdentityHash char(64) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'TargetIdentity') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD TargetIdentity bigint NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'ReviewedFieldSet') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD ReviewedFieldSet nvarchar(400) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'ReviewedEventVersion') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD ReviewedEventVersion bigint NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'EvidenceAnchorVersion') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD EvidenceAnchorVersion bigint NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'SourceFingerprint') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD SourceFingerprint char(64) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'TargetFingerprint') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD TargetFingerprint char(64) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'QlhvOwnedFingerprint') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD QlhvOwnedFingerprint char(64) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'ReviewState') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD ReviewState nvarchar(40) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'SupersedesManualReviewId') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD SupersedesManualReviewId bigint NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'DecisionEvidenceHash') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD DecisionEvidenceHash char(64) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeManualReview',N'DiagnosticId') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview ADD DiagnosticId nvarchar(64) NULL;

GO

IF OBJECT_ID(N'dbo.FK_App_QlhvDirectRealtimeManualReview_Supersedes',N'F') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview WITH CHECK ADD CONSTRAINT
        FK_App_QlhvDirectRealtimeManualReview_Supersedes
        FOREIGN KEY(SupersedesManualReviewId)
        REFERENCES dbo.App_QlhvDirectRealtimeManualReview(ManualReviewId);

IF OBJECT_ID(N'dbo.CK_App_QlhvDirectRealtimeManualReview_V9Evidence',N'C') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeManualReview WITH CHECK ADD CONSTRAINT
        CK_App_QlhvDirectRealtimeManualReview_V9Evidence CHECK
        (
            EvidenceContractVersion IS NULL
            OR
            (
                EvidenceContractVersion=N'RT03-REVIEWED-RETAINED-1.0'
                AND DomainCode=N'LEARNER'
                AND LEN(SourceBusinessIdentityHash)=64
                AND TargetIdentity IS NOT NULL
                AND LEN(ReviewedFieldSet)>0
                AND ReviewedEventVersion>=0
                AND EvidenceAnchorVersion>=ReviewedEventVersion
                AND LEN(SourceFingerprint)=64
                AND LEN(TargetFingerprint)=64
                AND LEN(QlhvOwnedFingerprint)=64
                AND ReviewState IN(N'REVIEWED_AND_RETAINED',N'SUPERSEDED')
                AND SupersedesManualReviewId IS NOT NULL
                AND LEN(DecisionEvidenceHash)=64
                AND LEN(DiagnosticId)>0
                AND TargetRetainedActive=1
                AND TargetMutated=0
            )
        );

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview')
      AND name=N'UX_App_QlhvDirectRealtimeManualReview_V9EvidenceHash'
)
    CREATE UNIQUE INDEX UX_App_QlhvDirectRealtimeManualReview_V9EvidenceHash
        ON dbo.App_QlhvDirectRealtimeManualReview(DecisionEvidenceHash)
        WHERE DecisionEvidenceHash IS NOT NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview')
      AND name=N'UX_App_QlhvDirectRealtimeManualReview_V9Supersedes'
)
    CREATE UNIQUE INDEX UX_App_QlhvDirectRealtimeManualReview_V9Supersedes
        ON dbo.App_QlhvDirectRealtimeManualReview(SupersedesManualReviewId)
        WHERE SupersedesManualReviewId IS NOT NULL;

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'ReviewedRetainedCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD ReviewedRetainedCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeCycleHistory_ReviewedRetainedCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'ReviewedRetainedDomains') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD ReviewedRetainedDomains nvarchar(400) NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeCycleHistory_ReviewedRetainedDomains DEFAULT(N'');
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'ActiveReviewCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD ActiveReviewCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeCycleHistory_ActiveReviewCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'StaleReviewCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD StaleReviewCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeCycleHistory_StaleReviewCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'NewDriftCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD NewDriftCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeCycleHistory_NewDriftCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'OldestActiveReviewUtc') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD OldestActiveReviewUtc datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'NewestActiveReviewUtc') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD NewestActiveReviewUtc datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeCycleHistory',N'CycleOutcome') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeCycleHistory ADD CycleOutcome nvarchar(100) NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeCycleHistory_CycleOutcome DEFAULT(N'');

IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'ReviewedRetainedCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD ReviewedRetainedCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeWorkerState_ReviewedRetainedCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'ReviewedRetainedDomains') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD ReviewedRetainedDomains nvarchar(400) NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeWorkerState_ReviewedRetainedDomains DEFAULT(N'');
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'ActiveReviewCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD ActiveReviewCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeWorkerState_ActiveReviewCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'StaleReviewCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD StaleReviewCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeWorkerState_StaleReviewCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'NewDriftCount') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD NewDriftCount int NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeWorkerState_NewDriftCount DEFAULT(0);
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'OldestActiveReviewUtc') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD OldestActiveReviewUtc datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'NewestActiveReviewUtc') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD NewestActiveReviewUtc datetime2(7) NULL;
IF COL_LENGTH(N'dbo.App_QlhvDirectRealtimeWorkerState',N'CycleOutcome') IS NULL
    ALTER TABLE dbo.App_QlhvDirectRealtimeWorkerState ADD CycleOutcome nvarchar(100) NOT NULL
        CONSTRAINT DF_App_QlhvDirectRealtimeWorkerState_CycleOutcome DEFAULT(N'');

COMMIT TRANSACTION;

SELECT N'RT03_V9_SCHEMA_READY' AS Result,
       COUNT(*) AS EvidenceColumns
FROM sys.columns
WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview')
  AND name IN
  (
      N'EvidenceContractVersion',N'DomainCode',N'SourceBusinessIdentityHash',
      N'TargetIdentity',N'ReviewedFieldSet',N'ReviewedEventVersion',
      N'EvidenceAnchorVersion',N'SourceFingerprint',N'TargetFingerprint',
      N'QlhvOwnedFingerprint',N'ReviewState',N'SupersedesManualReviewId',
      N'DecisionEvidenceHash',N'DiagnosticId'
  );
