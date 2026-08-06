:ON ERROR EXIT
EXEC sys.sp_set_session_context
    @key=N'RT03_V9_DISPOSABLE_REHEARSAL',@value=1,@read_only=1;
GO
:r .\database\patches\20260731_add_rt03_v9_reviewed_retained.sql
GO
:r .\database\patches\20260731_add_rt03_v9_reviewed_retained.sql
GO
IF (SELECT COUNT(*) FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview')
      AND name IN
      (
          N'EvidenceContractVersion',N'DomainCode',N'SourceBusinessIdentityHash',
          N'TargetIdentity',N'ReviewedFieldSet',N'ReviewedEventVersion',
          N'EvidenceAnchorVersion',N'SourceFingerprint',N'TargetFingerprint',
          N'QlhvOwnedFingerprint',N'ReviewState',N'SupersedesManualReviewId',
          N'DecisionEvidenceHash',N'DiagnosticId'
      ))<>14 THROW 527740,'RT03_V9_REHEARSAL_EVIDENCE_SCHEMA_MISMATCH',1;

IF (SELECT COUNT(*) FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeWorkerState')
      AND name IN
      (
          N'ReviewedRetainedCount',N'ReviewedRetainedDomains',N'ActiveReviewCount',
          N'StaleReviewCount',N'NewDriftCount',N'OldestActiveReviewUtc',
          N'NewestActiveReviewUtc',N'CycleOutcome'
      ))<>8 THROW 527741,'RT03_V9_REHEARSAL_RUNTIME_SCHEMA_MISMATCH',1;

SELECT N'RT03_V9_SCHEMA_REHEARSAL_PASS' AS Result;
GO
