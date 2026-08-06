USE [$(CourseCompletionTargetDatabase)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @ExpectedColumns TABLE(TableName sysname NOT NULL,ColumnName sysname NOT NULL,TypeName sysname NOT NULL,MaxLength smallint NOT NULL,IsNullable bit NOT NULL);
INSERT @ExpectedColumns VALUES
(N'App_CourseCompletion',N'CourseCompletionId',N'bigint',8,0),(N'App_CourseCompletion',N'KhoaHocId',N'bigint',8,0),
(N'App_CourseCompletion',N'SourceProfileCode',N'nvarchar',40,0),(N'App_CourseCompletion',N'SourceCourseKey',N'nvarchar',100,0),
(N'App_CourseCompletion',N'ContractVersion',N'nvarchar',40,0),(N'App_CourseCompletion',N'Status',N'nvarchar',40,0),
(N'App_CourseCompletion',N'CompletionBusinessDate',N'date',3,0),(N'App_CourseCompletion',N'SourceSnapshotHash',N'char',64,0),
(N'App_CourseCompletion',N'LearnerCount',N'int',4,0),(N'App_CourseCompletion',N'CompletedAtUtc',N'datetime2',8,0),
(N'App_CourseCompletion',N'CompletedBy',N'nvarchar',200,0),(N'App_CourseCompletion',N'CompletionReason',N'nvarchar',1000,0),
(N'App_CourseCompletion',N'CreatedOperationId',N'uniqueidentifier',16,0),(N'App_CourseCompletion',N'RowVersion',N'timestamp',8,0),
(N'App_CourseCompletionLearnerSnapshot',N'SnapshotId',N'bigint',8,0),(N'App_CourseCompletionLearnerSnapshot',N'CourseCompletionId',N'bigint',8,0),
(N'App_CourseCompletionLearnerSnapshot',N'SourceProfileCode',N'nvarchar',40,0),(N'App_CourseCompletionLearnerSnapshot',N'SourceCourseKey',N'nvarchar',100,0),
(N'App_CourseCompletionLearnerSnapshot',N'ProtectedLearnerIdentity',N'char',64,0),(N'App_CourseCompletionLearnerSnapshot',N'MaKhoaHoc',N'nvarchar',100,0),
(N'App_CourseCompletionLearnerSnapshot',N'TT_XuLy',N'nvarchar',20,0),(N'App_CourseCompletionLearnerSnapshot',N'LearnerClassification',N'nvarchar',80,0),
(N'App_CourseCompletionLearnerSnapshot',N'ResultCompletenessClassification',N'nvarchar',80,0),(N'App_CourseCompletionLearnerSnapshot',N'DownstreamClassification',N'nvarchar',80,0),
(N'App_CourseCompletionLearnerSnapshot',N'CanonicalLearnerRowHash',N'char',64,0),(N'App_CourseCompletionLearnerSnapshot',N'SnapshotAtUtc',N'datetime2',8,0),
(N'App_CourseCompletionLearnerSnapshot',N'RowVersion',N'timestamp',8,0),
(N'App_CourseCompletionOperation',N'OperationId',N'uniqueidentifier',16,0),(N'App_CourseCompletionOperation',N'SourceProfileCode',N'nvarchar',40,0),
(N'App_CourseCompletionOperation',N'SourceCourseKey',N'nvarchar',100,0),(N'App_CourseCompletionOperation',N'ActorId',N'nvarchar',200,0),
(N'App_CourseCompletionOperation',N'IdempotencyKeyHash',N'binary',32,0),(N'App_CourseCompletionOperation',N'RequestFingerprint',N'char',64,0),
(N'App_CourseCompletionOperation',N'PreviewSnapshotHash',N'char',64,0),(N'App_CourseCompletionOperation',N'ResultCode',N'nvarchar',80,0),
(N'App_CourseCompletionOperation',N'CourseCompletionId',N'bigint',8,1),(N'App_CourseCompletionOperation',N'CreatedAtUtc',N'datetime2',8,0),
(N'App_CourseCompletionOperation',N'CompletedAtUtc',N'datetime2',8,1),(N'App_CourseCompletionOperation',N'ErrorCode',N'nvarchar',160,1),
(N'App_CourseCompletionOperation',N'RowVersion',N'timestamp',8,0);

IF EXISTS
(
    SELECT TableName,ColumnName,TypeName,MaxLength,IsNullable FROM @ExpectedColumns
    EXCEPT
    SELECT OBJECT_NAME(c.object_id),c.name,t.name,c.max_length,c.is_nullable
    FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id IN (OBJECT_ID(N'dbo.App_CourseCompletion'),OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot'),OBJECT_ID(N'dbo.App_CourseCompletionOperation'))
)
OR EXISTS
(
    SELECT OBJECT_NAME(c.object_id),c.name,t.name,c.max_length,c.is_nullable
    FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id IN (OBJECT_ID(N'dbo.App_CourseCompletion'),OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot'),OBJECT_ID(N'dbo.App_CourseCompletionOperation'))
    EXCEPT
    SELECT TableName,ColumnName,TypeName,MaxLength,IsNullable FROM @ExpectedColumns
)
    THROW 52110, 'COURSE_COMPLETION_V1_COLUMN_DRIFT', 1;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_App_CourseCompletion_SourceIdentity' AND type=N'UQ')
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_App_CourseCompletionLearnerSnapshot_Identity' AND type=N'UQ')
 OR NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_App_CourseCompletionOperation_Idempotency' AND type=N'UQ')
 OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_App_CourseCompletion_KhoaHoc' AND delete_referential_action=0)
 OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_App_CourseCompletionLearnerSnapshot_Completion' AND delete_referential_action=0)
 OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_App_CourseCompletionOperation_Completion' AND delete_referential_action=0)
    THROW 52111, 'COURSE_COMPLETION_V1_CONSTRAINT_DRIFT', 1;

DECLARE @ExpectedConstraints TABLE(ConstraintName sysname NOT NULL,ConstraintType char(2) NOT NULL);
INSERT @ExpectedConstraints VALUES
(N'PK_App_CourseCompletion',N'PK'),(N'UQ_App_CourseCompletion_SourceIdentity',N'UQ'),
(N'UQ_App_CourseCompletion_CreatedOperation',N'UQ'),(N'FK_App_CourseCompletion_KhoaHoc',N'F'),
(N'CK_App_CourseCompletion_Profile',N'C'),(N'CK_App_CourseCompletion_Status',N'C'),
(N'CK_App_CourseCompletion_Contract',N'C'),(N'CK_App_CourseCompletion_Hash',N'C'),
(N'CK_App_CourseCompletion_LearnerCount',N'C'),(N'CK_App_CourseCompletion_Reason',N'C'),
(N'CK_App_CourseCompletion_Actor',N'C'),
(N'PK_App_CourseCompletionLearnerSnapshot',N'PK'),
(N'FK_App_CourseCompletionLearnerSnapshot_Completion',N'F'),
(N'UQ_App_CourseCompletionLearnerSnapshot_Identity',N'UQ'),
(N'CK_App_CourseCompletionLearnerSnapshot_Profile',N'C'),
(N'CK_App_CourseCompletionLearnerSnapshot_Hashes',N'C'),
(N'CK_App_CourseCompletionLearnerSnapshot_Status',N'C'),
(N'CK_App_CourseCompletionLearnerSnapshot_Class',N'C'),
(N'CK_App_CourseCompletionLearnerSnapshot_Result',N'C'),
(N'PK_App_CourseCompletionOperation',N'PK'),
(N'FK_App_CourseCompletionOperation_Completion',N'F'),
(N'UQ_App_CourseCompletionOperation_Idempotency',N'UQ'),
(N'CK_App_CourseCompletionOperation_Profile',N'C'),
(N'CK_App_CourseCompletionOperation_Result',N'C'),
(N'CK_App_CourseCompletionOperation_Hashes',N'C'),
(N'CK_App_CourseCompletionOperation_Actor',N'C');

DECLARE @ActualConstraints TABLE(ConstraintName sysname NOT NULL,ConstraintType char(2) NOT NULL);
INSERT @ActualConstraints
SELECT name,type FROM sys.objects
WHERE parent_object_id IN
(
    OBJECT_ID(N'dbo.App_CourseCompletion'),
    OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot'),
    OBJECT_ID(N'dbo.App_CourseCompletionOperation')
)
AND type IN(N'PK',N'UQ',N'F',N'C');

IF EXISTS(SELECT ConstraintName,ConstraintType FROM @ExpectedConstraints EXCEPT SELECT ConstraintName,ConstraintType FROM @ActualConstraints)
 OR EXISTS(SELECT ConstraintName,ConstraintType FROM @ActualConstraints EXCEPT SELECT ConstraintName,ConstraintType FROM @ExpectedConstraints)
 OR EXISTS
 (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.App_CourseCompletion'),
        OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot'),
        OBJECT_ID(N'dbo.App_CourseCompletionOperation')
    ) AND (is_disabled=1 OR is_not_trusted=1)
 )
    THROW 52114, 'COURSE_COMPLETION_V1_EXACT_CONSTRAINT_SET_DRIFT', 1;

DECLARE @ExpectedStandaloneIndexes TABLE(IndexName sysname NOT NULL,TableName sysname NOT NULL,IsUnique bit NOT NULL);
INSERT @ExpectedStandaloneIndexes VALUES
(N'UX_App_CourseCompletion_KhoaHocId',N'App_CourseCompletion',1),
(N'IX_App_CourseCompletionLearnerSnapshot_Completion',N'App_CourseCompletionLearnerSnapshot',0);

DECLARE @ActualStandaloneIndexes TABLE(IndexName sysname NOT NULL,TableName sysname NOT NULL,IsUnique bit NOT NULL);
INSERT @ActualStandaloneIndexes
SELECT i.name,OBJECT_NAME(i.object_id),i.is_unique
FROM sys.indexes i
WHERE i.object_id IN
(
    OBJECT_ID(N'dbo.App_CourseCompletion'),
    OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot'),
    OBJECT_ID(N'dbo.App_CourseCompletionOperation')
)
AND i.name IS NOT NULL AND i.is_primary_key=0 AND i.is_unique_constraint=0;

IF EXISTS(SELECT IndexName,TableName,IsUnique FROM @ExpectedStandaloneIndexes EXCEPT SELECT IndexName,TableName,IsUnique FROM @ActualStandaloneIndexes)
 OR EXISTS(SELECT IndexName,TableName,IsUnique FROM @ActualStandaloneIndexes EXCEPT SELECT IndexName,TableName,IsUnique FROM @ExpectedStandaloneIndexes)
    THROW 52115, 'COURSE_COMPLETION_V1_INDEX_DRIFT', 1;

IF DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api') IS NULL
    THROW 52112, 'COURSE_COMPLETION_V1_ROLE_MISSING', 1;

DECLARE @ExpectedPermissions TABLE(ObjectName sysname NOT NULL,PermissionName nvarchar(60) NOT NULL);
INSERT @ExpectedPermissions VALUES
(N'App_CourseCompletion',N'SELECT'),(N'App_CourseCompletion',N'INSERT'),
(N'App_CourseCompletionLearnerSnapshot',N'SELECT'),(N'App_CourseCompletionLearnerSnapshot',N'INSERT'),
(N'App_CourseCompletionOperation',N'SELECT'),(N'App_CourseCompletionOperation',N'INSERT'),
(N'App_KhoaHoc',N'SELECT'),(N'App_AuditLog',N'INSERT');

IF EXISTS
(
    SELECT ObjectName,PermissionName FROM @ExpectedPermissions
    EXCEPT
    SELECT OBJECT_NAME(p.major_id) COLLATE DATABASE_DEFAULT,
           p.permission_name COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api')
      AND p.class=1 AND p.state IN (N'G',N'W')
)
OR EXISTS
(
    SELECT OBJECT_NAME(p.major_id) COLLATE DATABASE_DEFAULT,
           p.permission_name COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions p
    WHERE p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api')
      AND p.class=1 AND p.state IN (N'G',N'W')
    EXCEPT
    SELECT ObjectName,PermissionName FROM @ExpectedPermissions
)
OR EXISTS
(
    SELECT 1 FROM sys.database_permissions p
    WHERE p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api')
      AND (p.class<>1 OR p.minor_id<>0)
)
OR EXISTS
(
    SELECT 1 FROM sys.database_role_members drm
    WHERE drm.member_principal_id=DATABASE_PRINCIPAL_ID(N'qlhv_course_completion_api')
)
    THROW 52113, 'COURSE_COMPLETION_V1_PERMISSION_DRIFT', 1;

SELECT N'COURSE_COMPLETION_V1_SCHEMA_VERIFIED' AS Evidence,
       3 AS TableCount,COUNT_BIG(1) AS ExpectedColumnCount,
       (SELECT COUNT_BIG(1) FROM @ExpectedConstraints) AS ExpectedConstraintCount,
       (SELECT COUNT_BIG(1) FROM @ExpectedPermissions) AS ExpectedPermissionCount
FROM @ExpectedColumns;
GO
