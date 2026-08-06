USE [QLHV_APP];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedObjects TABLE
(
    SchemaName sysname NOT NULL,
    ObjectName sysname NOT NULL,
    ObjectType char(2) NOT NULL,
    PRIMARY KEY (SchemaName,ObjectName)
);
INSERT @ExpectedObjects(SchemaName,ObjectName,ObjectType)
VALUES
(N'dbo',N'App_CsdtConnectionProfile',N'U'),
(N'dbo',N'App_DataVersion',N'U'),
(N'dbo',N'App_GiaoVien',N'U'),
(N'dbo',N'App_HocVien',N'U'),
(N'dbo',N'App_KhoaHoc',N'U'),
(N'dbo',N'App_KhoaHoc_GiaoVien',N'U'),
(N'dbo',N'App_QlhvAutoSyncRun',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeApplyCheckpoint',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeApplyMarker',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeCycleHistory',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeFeatureState',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeManualReview',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeProfileState',N'U'),
(N'dbo',N'App_QlhvDirectRealtimeWorkerState',N'U'),
(N'dbo',N'App_QlhvSyncOperationHistory',N'U'),
(N'dbo',N'App_QlhvSyncPartitionState',N'U'),
(N'dbo',N'App_XeTap',N'U'),
(N'dbo',N'App_XeTap_RealtimeCheckpoint',N'U'),
(N'dbo',N'App_XeTap_RealtimeEvent',N'U'),
(N'dbo',N'App_XeTap_RealtimeManualReview',N'U'),
(N'dbo',N'App_Rt03FullConvergenceSession',N'U'),
(N'dbo',N'App_Rt03FullConvergenceDomain',N'U'),
(N'dbo',N'App_Rt03FullConvergenceMarker',N'U'),
(N'dbo',N'usp_App_Rt03BeginFullConvergence',N'P'),
(N'dbo',N'usp_App_Rt03RecordFullConvergenceDomain',N'P'),
(N'dbo',N'usp_App_Rt03VerifyFullConvergence',N'P'),
(N'dbo',N'usp_App_Rt03FinalizeFullConvergence',N'P');

IF (SELECT COUNT(*) FROM @ExpectedObjects) <>
   (SELECT COUNT(*) FROM @ExpectedObjects expected
    JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
    JOIN sys.objects objectRow ON objectRow.schema_id=schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType)
    THROW 528120,'V7_V9_EXPECTED_OBJECT_MISMATCH',1;

DECLARE @V9Columns TABLE
(
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL,
    TypeName sysname NOT NULL,
    MaxLength smallint NOT NULL,
    [Precision] tinyint NOT NULL,
    Scale tinyint NOT NULL,
    IsNullable bit NOT NULL,
    DefaultName sysname NULL,
    DefaultDefinition nvarchar(100) NULL,
    PRIMARY KEY(TableName,ColumnName)
);

INSERT @V9Columns VALUES
(N'App_QlhvDirectRealtimeManualReview',N'EvidenceContractVersion',N'nvarchar',128,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'DomainCode',N'nvarchar',120,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'SourceBusinessIdentityHash',N'char',64,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'TargetIdentity',N'bigint',8,19,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'ReviewedFieldSet',N'nvarchar',800,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'ReviewedEventVersion',N'bigint',8,19,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'EvidenceAnchorVersion',N'bigint',8,19,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'SourceFingerprint',N'char',64,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'TargetFingerprint',N'char',64,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'QlhvOwnedFingerprint',N'char',64,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'ReviewState',N'nvarchar',80,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'SupersedesManualReviewId',N'bigint',8,19,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'DecisionEvidenceHash',N'char',64,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeManualReview',N'DiagnosticId',N'nvarchar',128,0,0,1,NULL,NULL),
(N'App_QlhvDirectRealtimeCycleHistory',N'ReviewedRetainedCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeCycleHistory_ReviewedRetainedCount',N'((0))'),
(N'App_QlhvDirectRealtimeCycleHistory',N'ReviewedRetainedDomains',N'nvarchar',800,0,0,0,N'DF_App_QlhvDirectRealtimeCycleHistory_ReviewedRetainedDomains',N'(N'''')'),
(N'App_QlhvDirectRealtimeCycleHistory',N'ActiveReviewCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeCycleHistory_ActiveReviewCount',N'((0))'),
(N'App_QlhvDirectRealtimeCycleHistory',N'StaleReviewCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeCycleHistory_StaleReviewCount',N'((0))'),
(N'App_QlhvDirectRealtimeCycleHistory',N'NewDriftCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeCycleHistory_NewDriftCount',N'((0))'),
(N'App_QlhvDirectRealtimeCycleHistory',N'OldestActiveReviewUtc',N'datetime2',8,27,7,1,NULL,NULL),
(N'App_QlhvDirectRealtimeCycleHistory',N'NewestActiveReviewUtc',N'datetime2',8,27,7,1,NULL,NULL),
(N'App_QlhvDirectRealtimeCycleHistory',N'CycleOutcome',N'nvarchar',200,0,0,0,N'DF_App_QlhvDirectRealtimeCycleHistory_CycleOutcome',N'(N'''')'),
(N'App_QlhvDirectRealtimeWorkerState',N'ReviewedRetainedCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeWorkerState_ReviewedRetainedCount',N'((0))'),
(N'App_QlhvDirectRealtimeWorkerState',N'ReviewedRetainedDomains',N'nvarchar',800,0,0,0,N'DF_App_QlhvDirectRealtimeWorkerState_ReviewedRetainedDomains',N'(N'''')'),
(N'App_QlhvDirectRealtimeWorkerState',N'ActiveReviewCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeWorkerState_ActiveReviewCount',N'((0))'),
(N'App_QlhvDirectRealtimeWorkerState',N'StaleReviewCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeWorkerState_StaleReviewCount',N'((0))'),
(N'App_QlhvDirectRealtimeWorkerState',N'NewDriftCount',N'int',4,10,0,0,N'DF_App_QlhvDirectRealtimeWorkerState_NewDriftCount',N'((0))'),
(N'App_QlhvDirectRealtimeWorkerState',N'OldestActiveReviewUtc',N'datetime2',8,27,7,1,NULL,NULL),
(N'App_QlhvDirectRealtimeWorkerState',N'NewestActiveReviewUtc',N'datetime2',8,27,7,1,NULL,NULL),
(N'App_QlhvDirectRealtimeWorkerState',N'CycleOutcome',N'nvarchar',200,0,0,0,N'DF_App_QlhvDirectRealtimeWorkerState_CycleOutcome',N'(N'''')');

IF (SELECT COUNT(*) FROM @V9Columns)<>30
    THROW 528121,'V7_V9_COLUMN_CONTRACT_COUNT_MISMATCH',1;

IF EXISTS
(
    SELECT expected.*
    FROM @V9Columns expected
    EXCEPT
    SELECT OBJECT_NAME(columnRow.object_id),columnRow.name,typeRow.name,
           columnRow.max_length,columnRow.precision,columnRow.scale,
           columnRow.is_nullable,defaultRow.name,defaultRow.definition
    FROM sys.columns columnRow
    JOIN sys.types typeRow ON typeRow.user_type_id=columnRow.user_type_id
    LEFT JOIN sys.default_constraints defaultRow
      ON defaultRow.parent_object_id=columnRow.object_id
     AND defaultRow.parent_column_id=columnRow.column_id
    WHERE OBJECT_NAME(columnRow.object_id) IN
      (N'App_QlhvDirectRealtimeManualReview',N'App_QlhvDirectRealtimeCycleHistory',
       N'App_QlhvDirectRealtimeWorkerState')
)
OR EXISTS
(
    SELECT OBJECT_NAME(columnRow.object_id),columnRow.name,typeRow.name,
           columnRow.max_length,columnRow.precision,columnRow.scale,
           columnRow.is_nullable,defaultRow.name,defaultRow.definition
    FROM sys.columns columnRow
    JOIN sys.types typeRow ON typeRow.user_type_id=columnRow.user_type_id
    LEFT JOIN sys.default_constraints defaultRow
      ON defaultRow.parent_object_id=columnRow.object_id
     AND defaultRow.parent_column_id=columnRow.column_id
    JOIN @V9Columns expected
      ON expected.TableName=OBJECT_NAME(columnRow.object_id) COLLATE DATABASE_DEFAULT
     AND expected.ColumnName=columnRow.name COLLATE DATABASE_DEFAULT
    EXCEPT
    SELECT * FROM @V9Columns
)
    THROW 528122,'V7_V9_COLUMN_DEFINITION_MISMATCH',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
    WHERE fk.name=N'FK_App_QlhvDirectRealtimeManualReview_Supersedes'
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
      AND OBJECT_NAME(fk.parent_object_id)=N'App_QlhvDirectRealtimeManualReview'
      AND OBJECT_NAME(fk.referenced_object_id)=N'App_QlhvDirectRealtimeManualReview'
      AND COL_NAME(fk.parent_object_id,fkc.parent_column_id)=N'SupersedesManualReviewId'
      AND COL_NAME(fk.referenced_object_id,fkc.referenced_column_id)=N'ManualReviewId'
      AND fk.delete_referential_action=0 AND fk.update_referential_action=0
)
    THROW 528123,'V7_V9_FOREIGN_KEY_MISMATCH',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE name=N'CK_App_QlhvDirectRealtimeManualReview_V9Evidence'
      AND is_disabled=0 AND is_not_trusted=0
      AND definition LIKE N'%RT03-REVIEWED-RETAINED-1.0%'
      AND definition LIKE N'%REVIEWED_AND_RETAINED%'
      AND definition LIKE N'%TargetRetainedActive%'
      AND definition LIKE N'%TargetMutated%'
)
    THROW 528124,'V7_V9_CHECK_CONSTRAINT_MISMATCH',1;

IF 2<>(SELECT COUNT(*) FROM sys.indexes indexRow
       WHERE indexRow.object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeManualReview')
         AND indexRow.is_unique=1 AND indexRow.has_filter=1
         AND
         (
             (indexRow.name=N'UX_App_QlhvDirectRealtimeManualReview_V9EvidenceHash'
              AND indexRow.filter_definition=N'([DecisionEvidenceHash] IS NOT NULL)')
          OR (indexRow.name=N'UX_App_QlhvDirectRealtimeManualReview_V9Supersedes'
              AND indexRow.filter_definition=N'([SupersedesManualReviewId] IS NOT NULL)')
         ))
    THROW 528125,'V7_V9_FILTERED_INDEX_MISMATCH',1;

DECLARE @Modes TABLE(ModeName nvarchar(20) PRIMARY KEY,ExcludeV9 bit NOT NULL);
INSERT @Modes VALUES(N'CURRENT_V9',0),(N'PRE_V9',1);
DECLARE @Fingerprints TABLE(ModeName nvarchar(20) PRIMARY KEY,Fingerprint char(64));

;WITH Parts AS
(
    SELECT modeRow.ModeName,
        CONCAT(N'T|',schemaRow.name,N'|',objectRow.name,N'|',columnRow.column_id,
          N'|',columnRow.name,N'|',typeRow.name,N'|',columnRow.max_length,N'|',
          columnRow.precision,N'|',columnRow.scale,N'|',CONVERT(int,columnRow.is_nullable),
          N'|',CONVERT(int,columnRow.is_identity),N'|',
          COALESCE(CONVERT(nvarchar(100),identityRow.seed_value),N''),N'|',
          COALESCE(CONVERT(nvarchar(100),identityRow.increment_value),N''),N'|',
          CONVERT(int,columnRow.is_computed),N'|',COALESCE(columnRow.collation_name,N''),
          N'|',COALESCE(computedRow.definition,N''),N'|',
          COALESCE(defaultRow.definition,N'')) COLLATE DATABASE_DEFAULT PartValue
    FROM @Modes modeRow
    CROSS JOIN @ExpectedObjects expected
    JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
    JOIN sys.objects objectRow ON objectRow.schema_id=schemaRow.schema_id
      AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
      AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType
    JOIN sys.columns columnRow ON columnRow.object_id=objectRow.object_id
    JOIN sys.types typeRow ON typeRow.user_type_id=columnRow.user_type_id
    LEFT JOIN sys.identity_columns identityRow ON identityRow.object_id=objectRow.object_id
      AND identityRow.column_id=columnRow.column_id
    LEFT JOIN sys.computed_columns computedRow ON computedRow.object_id=objectRow.object_id
      AND computedRow.column_id=columnRow.column_id
    LEFT JOIN sys.default_constraints defaultRow ON defaultRow.parent_object_id=objectRow.object_id
      AND defaultRow.parent_column_id=columnRow.column_id
    WHERE expected.ObjectType=N'U'
      AND (modeRow.ExcludeV9=0 OR NOT EXISTS
          (SELECT 1 FROM @V9Columns v9
           WHERE v9.TableName=objectRow.name COLLATE DATABASE_DEFAULT
             AND v9.ColumnName=columnRow.name COLLATE DATABASE_DEFAULT))

    UNION ALL

    SELECT modeRow.ModeName,
      CONCAT(N'P|',schemaRow.name,N'|',objectRow.name,N'|',
        CONVERT(varchar(64),HASHBYTES('SHA2_256',
          CONVERT(varbinary(max),moduleRow.definition)),2)) COLLATE DATABASE_DEFAULT
    FROM @Modes modeRow
    CROSS JOIN @ExpectedObjects expected
    JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
    JOIN sys.objects objectRow ON objectRow.schema_id=schemaRow.schema_id
      AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
      AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType
    JOIN sys.sql_modules moduleRow ON moduleRow.object_id=objectRow.object_id
    WHERE expected.ObjectType=N'P'
), Numbered AS
(
    SELECT ModeName,PartValue,
      ROW_NUMBER() OVER(PARTITION BY ModeName ORDER BY PartValue) RowNumber
    FROM Parts
)
INSERT @Fingerprints(ModeName,Fingerprint)
SELECT ModeName,CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),
       STRING_AGG(CONVERT(nvarchar(max),PartValue),NCHAR(10))
       WITHIN GROUP(ORDER BY RowNumber))),2)
FROM Numbered GROUP BY ModeName;

IF (SELECT Fingerprint FROM @Fingerprints WHERE ModeName=N'PRE_V9')<>
   N'C1572874BA588ECA0707979ED4D6825047EAF140620126C24780390CB75A7BF3'
    THROW 528126,'NON_V9_TARGET_SCHEMA_DRIFT_DETECTED',1;
IF (SELECT Fingerprint FROM @Fingerprints WHERE ModeName=N'CURRENT_V9')<>
   N'DD2CF4737A0C3F09C0B92591F185E053DCD2DBF2406645F2BF4958636A9F7DF6'
    THROW 528127,'CURRENT_V9_TARGET_SCHEMA_FINGERPRINT_MISMATCH',1;

SELECT N'PASS' Result,N'EXACT_V7_PLUS_V9_ADDITIVE_SCHEMA' Classification,
       (SELECT Fingerprint FROM @Fingerprints WHERE ModeName=N'PRE_V9') PreV9Fingerprint,
       (SELECT Fingerprint FROM @Fingerprints WHERE ModeName=N'CURRENT_V9') CurrentV9Fingerprint,
       (SELECT COUNT(*) FROM @V9Columns) ExactV9ColumnCount,
       CONVERT(datetime2(7),SYSUTCDATETIME()) ObservedAtUtc;
GO
