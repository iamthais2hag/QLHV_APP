USE [QLHV_APP];
GO
/*
  RT03 V7 least-privilege Worker permissions.

  Production execution requires sqlcmd variables from the sealed V7 manifest.
  The script never creates a login or database user and never grants source DML.
  Existing direct grants are preserved so rollback can restore the exact V6
  baseline. Broad db_datareader membership is replaced by exact object grants.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Principal sysname = N'NT SERVICE\QLHV_APP_RealtimeWorker';
DECLARE @Role sysname = N'QLHV_RealtimeWorkerRole';

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
   N'$(ExpectedServerName)'
    THROW 527900, 'RT03_V7_SERVER_IDENTITY_REJECTED', 1;

USE [master];
IF NOT EXISTS
(
    SELECT 1
    FROM sys.server_principals
    WHERE name = @Principal
      AND type = N'U'
      AND is_disabled = 0
)
    THROW 527901, 'RT03_V7_WORKER_LOGIN_REJECTED', 1;

IF IS_SRVROLEMEMBER(N'sysadmin', @Principal) <> 0
    THROW 527902, 'RT03_V7_WORKER_SYSADMIN_REJECTED', 1;

USE [QLHV_APP];
IF DB_ID() <> CONVERT(int, N'$(TargetDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
         AND database_guid =
             CONVERT(uniqueidentifier, N'$(TargetDatabaseGuid)')
   )
    THROW 527903, 'RT03_V7_TARGET_IDENTITY_REJECTED', 1;

IF USER_ID(@Principal) IS NULL
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_principals
       WHERE name = @Principal
         AND type = N'U'
         AND sid = SUSER_SID(@Principal)
   )
    THROW 527904, 'RT03_V7_TARGET_USER_REJECTED', 1;

DECLARE @ExpectedTargetObjects TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectType char(2) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (SchemaName, ObjectName)
);
INSERT @ExpectedTargetObjects VALUES
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

IF (SELECT COUNT(*) FROM @ExpectedTargetObjects) <>
   (
       SELECT COUNT(*)
       FROM @ExpectedTargetObjects expected
       INNER JOIN sys.schemas schemaRow
         ON schemaRow.name COLLATE DATABASE_DEFAULT = expected.SchemaName
       INNER JOIN sys.objects objectRow
         ON objectRow.schema_id = schemaRow.schema_id
        AND objectRow.name COLLATE DATABASE_DEFAULT = expected.ObjectName
        AND objectRow.type COLLATE DATABASE_DEFAULT = expected.ObjectType
   )
    THROW 527905, 'RT03_V7_TARGET_OBJECT_CONTRACT_REJECTED', 1;

DECLARE @TargetCanonical nvarchar(max);
;WITH Parts AS
(
    SELECT
        CONCAT(
            N'T|', schemaRow.name, N'|', objectRow.name, N'|',
            columnRow.column_id, N'|', columnRow.name, N'|', typeRow.name,
            N'|', columnRow.max_length, N'|', columnRow.precision, N'|',
            columnRow.scale, N'|', CONVERT(int, columnRow.is_nullable),
            N'|', CONVERT(int, columnRow.is_identity), N'|',
            COALESCE(CONVERT(nvarchar(100), identityRow.seed_value), N''),
            N'|',
            COALESCE(CONVERT(nvarchar(100), identityRow.increment_value), N''),
            N'|', CONVERT(int, columnRow.is_computed), N'|',
            COALESCE(columnRow.collation_name, N''), N'|',
            COALESCE(computedRow.definition, N''), N'|',
            COALESCE(defaultRow.definition, N''))
            COLLATE DATABASE_DEFAULT AS PartValue
    FROM @ExpectedTargetObjects expected
    INNER JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT = expected.SchemaName
    INNER JOIN sys.objects objectRow
      ON objectRow.schema_id = schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT = expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT = expected.ObjectType
    INNER JOIN sys.columns columnRow
      ON columnRow.object_id = objectRow.object_id
    INNER JOIN sys.types typeRow
      ON typeRow.user_type_id = columnRow.user_type_id
    LEFT JOIN sys.identity_columns identityRow
      ON identityRow.object_id = columnRow.object_id
     AND identityRow.column_id = columnRow.column_id
    LEFT JOIN sys.computed_columns computedRow
      ON computedRow.object_id = columnRow.object_id
     AND computedRow.column_id = columnRow.column_id
    LEFT JOIN sys.default_constraints defaultRow
      ON defaultRow.parent_object_id = columnRow.object_id
     AND defaultRow.parent_column_id = columnRow.column_id
    WHERE expected.ObjectType = N'U'

    UNION ALL

    SELECT
        CONCAT(
            N'P|', schemaRow.name, N'|', objectRow.name, N'|',
            CONVERT(
                varchar(64),
                HASHBYTES(
                    'SHA2_256',
                    CONVERT(varbinary(max), moduleRow.definition)),
                2)) COLLATE DATABASE_DEFAULT
    FROM @ExpectedTargetObjects expected
    INNER JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT = expected.SchemaName
    INNER JOIN sys.objects objectRow
      ON objectRow.schema_id = schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT = expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT = expected.ObjectType
    INNER JOIN sys.sql_modules moduleRow
      ON moduleRow.object_id = objectRow.object_id
    WHERE expected.ObjectType = N'P'
),
Numbered AS
(
    SELECT PartValue, ROW_NUMBER() OVER (ORDER BY PartValue) AS RowNumber
    FROM Parts
)
SELECT @TargetCanonical =
    STRING_AGG(CONVERT(nvarchar(max), PartValue), NCHAR(10))
        WITHIN GROUP (ORDER BY RowNumber)
FROM Numbered;

IF CONVERT(
       varchar(64),
       HASHBYTES('SHA2_256', CONVERT(varbinary(max), @TargetCanonical)),
       2) <> N'$(TargetSchemaFingerprint)'
    THROW 527906, 'RT03_V7_TARGET_SCHEMA_FINGERPRINT_REJECTED', 1;

DECLARE @ExpectedTargetRolePermission TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    PermissionName nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (SchemaName, ObjectName, PermissionName)
);
INSERT @ExpectedTargetRolePermission VALUES
(N'dbo',N'App_CsdtConnectionProfile',N'SELECT'),
(N'dbo',N'App_DataVersion',N'SELECT'),
(N'dbo',N'App_DataVersion',N'UPDATE'),
(N'dbo',N'App_GiaoVien',N'INSERT'),
(N'dbo',N'App_GiaoVien',N'SELECT'),
(N'dbo',N'App_GiaoVien',N'UPDATE'),
(N'dbo',N'App_HocVien',N'INSERT'),
(N'dbo',N'App_HocVien',N'SELECT'),
(N'dbo',N'App_HocVien',N'UPDATE'),
(N'dbo',N'App_KhoaHoc',N'INSERT'),
(N'dbo',N'App_KhoaHoc',N'SELECT'),
(N'dbo',N'App_KhoaHoc',N'UPDATE'),
(N'dbo',N'App_KhoaHoc_GiaoVien',N'INSERT'),
(N'dbo',N'App_KhoaHoc_GiaoVien',N'SELECT'),
(N'dbo',N'App_KhoaHoc_GiaoVien',N'UPDATE'),
(N'dbo',N'App_QlhvAutoSyncRun',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeApplyCheckpoint',N'INSERT'),
(N'dbo',N'App_QlhvDirectRealtimeApplyCheckpoint',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeApplyCheckpoint',N'UPDATE'),
(N'dbo',N'App_QlhvDirectRealtimeApplyMarker',N'INSERT'),
(N'dbo',N'App_QlhvDirectRealtimeApplyMarker',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeCycleHistory',N'INSERT'),
(N'dbo',N'App_QlhvDirectRealtimeCycleHistory',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeFeatureState',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeManualReview',N'INSERT'),
(N'dbo',N'App_QlhvDirectRealtimeManualReview',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeProfileState',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeProfileState',N'UPDATE'),
(N'dbo',N'App_QlhvDirectRealtimeWorkerState',N'SELECT'),
(N'dbo',N'App_QlhvDirectRealtimeWorkerState',N'UPDATE'),
(N'dbo',N'App_QlhvSyncOperationHistory',N'SELECT'),
(N'dbo',N'App_QlhvSyncPartitionState',N'INSERT'),
(N'dbo',N'App_QlhvSyncPartitionState',N'SELECT'),
(N'dbo',N'App_QlhvSyncPartitionState',N'UPDATE'),
(N'dbo',N'App_XeTap',N'INSERT'),
(N'dbo',N'App_XeTap',N'SELECT'),
(N'dbo',N'App_XeTap',N'UPDATE'),
(N'dbo',N'App_XeTap_RealtimeCheckpoint',N'INSERT'),
(N'dbo',N'App_XeTap_RealtimeCheckpoint',N'SELECT'),
(N'dbo',N'App_XeTap_RealtimeCheckpoint',N'UPDATE'),
(N'dbo',N'App_XeTap_RealtimeEvent',N'INSERT'),
(N'dbo',N'App_XeTap_RealtimeEvent',N'SELECT'),
(N'dbo',N'App_XeTap_RealtimeManualReview',N'INSERT'),
(N'dbo',N'App_XeTap_RealtimeManualReview',N'SELECT'),
(N'dbo',N'App_Rt03FullConvergenceSession',N'VIEW DEFINITION'),
(N'dbo',N'App_Rt03FullConvergenceDomain',N'VIEW DEFINITION'),
(N'dbo',N'App_Rt03FullConvergenceMarker',N'VIEW DEFINITION'),
(N'dbo',N'usp_App_Rt03BeginFullConvergence',N'EXECUTE'),
(N'dbo',N'usp_App_Rt03RecordFullConvergenceDomain',N'EXECUTE'),
(N'dbo',N'usp_App_Rt03VerifyFullConvergence',N'EXECUTE'),
(N'dbo',N'usp_App_Rt03FinalizeFullConvergence',N'EXECUTE');

DECLARE @ExpectedTargetDirectPermission TABLE
(
    StateDesc nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    PermissionName nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    Class tinyint NOT NULL,
    SchemaName sysname COLLATE DATABASE_DEFAULT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NULL
);
INSERT @ExpectedTargetDirectPermission VALUES
(N'GRANT',N'CONNECT',0,NULL,NULL),
(N'DENY',N'DELETE',1,N'dbo',N'App_HocVien'),
(N'DENY',N'DELETE',1,N'dbo',N'App_XeTap'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_HocVien'),
(N'GRANT',N'UPDATE',1,N'dbo',N'App_HocVien'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_QlhvDirectRealtimeApplyCheckpoint'),
(N'GRANT',N'UPDATE',1,N'dbo',N'App_QlhvDirectRealtimeApplyCheckpoint'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_QlhvDirectRealtimeApplyMarker'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_QlhvDirectRealtimeCycleHistory'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_QlhvDirectRealtimeManualReview'),
(N'GRANT',N'UPDATE',1,N'dbo',N'App_QlhvDirectRealtimeProfileState'),
(N'GRANT',N'UPDATE',1,N'dbo',N'App_QlhvDirectRealtimeWorkerState'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_XeTap'),
(N'GRANT',N'SELECT',1,N'dbo',N'App_XeTap'),
(N'GRANT',N'UPDATE',1,N'dbo',N'App_XeTap'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_XeTap_RealtimeCheckpoint'),
(N'GRANT',N'SELECT',1,N'dbo',N'App_XeTap_RealtimeCheckpoint'),
(N'GRANT',N'UPDATE',1,N'dbo',N'App_XeTap_RealtimeCheckpoint'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_XeTap_RealtimeEvent'),
(N'GRANT',N'SELECT',1,N'dbo',N'App_XeTap_RealtimeEvent'),
(N'GRANT',N'INSERT',1,N'dbo',N'App_XeTap_RealtimeManualReview'),
(N'GRANT',N'SELECT',1,N'dbo',N'App_XeTap_RealtimeManualReview');

IF EXISTS
(
    SELECT StateDesc,PermissionName,Class,
           COALESCE(SchemaName,N''),COALESCE(ObjectName,N'')
    FROM @ExpectedTargetDirectPermission
    EXCEPT
    SELECT permissionRow.state_desc COLLATE DATABASE_DEFAULT,
           permissionRow.permission_name COLLATE DATABASE_DEFAULT,
           permissionRow.class,
           COALESCE(schemaRow.name,N'') COLLATE DATABASE_DEFAULT,
           COALESCE(objectRow.name,N'') COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions permissionRow
    INNER JOIN sys.database_principals principalRow
      ON principalRow.principal_id = permissionRow.grantee_principal_id
    LEFT JOIN sys.objects objectRow
      ON permissionRow.class = 1
     AND objectRow.object_id = permissionRow.major_id
    LEFT JOIN sys.schemas schemaRow
      ON schemaRow.schema_id = objectRow.schema_id
    WHERE principalRow.name = @Principal
)
OR EXISTS
(
    SELECT permissionRow.state_desc COLLATE DATABASE_DEFAULT,
           permissionRow.permission_name COLLATE DATABASE_DEFAULT,
           permissionRow.class,
           COALESCE(schemaRow.name,N'') COLLATE DATABASE_DEFAULT,
           COALESCE(objectRow.name,N'') COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions permissionRow
    INNER JOIN sys.database_principals principalRow
      ON principalRow.principal_id = permissionRow.grantee_principal_id
    LEFT JOIN sys.objects objectRow
      ON permissionRow.class = 1
     AND objectRow.object_id = permissionRow.major_id
    LEFT JOIN sys.schemas schemaRow
      ON schemaRow.schema_id = objectRow.schema_id
    WHERE principalRow.name = @Principal
    EXCEPT
    SELECT StateDesc,PermissionName,Class,
           COALESCE(SchemaName,N''),COALESCE(ObjectName,N'')
    FROM @ExpectedTargetDirectPermission
)
    THROW 527907, 'RT03_V7_TARGET_DIRECT_PERMISSION_BASELINE_REJECTED', 1;

DECLARE @TargetRoleExists bit =
    CONVERT(bit, CASE WHEN DATABASE_PRINCIPAL_ID(@Role) IS NULL THEN 0 ELSE 1 END);

USE [CSDL_OTO];
IF DB_ID() <> CONVERT(int, N'$(OtoDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
         AND database_guid =
             CONVERT(uniqueidentifier, N'$(OtoDatabaseGuid)')
   )
    THROW 527908, 'RT03_V7_OTO_IDENTITY_REJECTED', 1;
IF USER_ID(@Principal) IS NULL
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_principals
       WHERE name=@Principal AND type=N'U' AND sid=SUSER_SID(@Principal)
   )
    THROW 527909, 'RT03_V7_OTO_USER_REJECTED', 1;

DECLARE @ExpectedSourceObjects TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectType char(2) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (SchemaName,ObjectName)
);
INSERT @ExpectedSourceObjects VALUES
(N'dbo',N'KhoaHoc',N'U'),(N'dbo',N'GiaoVien',N'U'),
(N'dbo',N'XeTap',N'U'),(N'dbo',N'NguoiLX',N'U'),
(N'dbo',N'NguoiLX_HoSo',N'U'),(N'dbo',N'DM_HangDT',N'U'),
(N'dbo',N'DM_DVHC',N'U'),(N'dbo',N'KhoaHoc_GiaoVien',N'U');

IF (SELECT COUNT(*) FROM @ExpectedSourceObjects) <>
   (
       SELECT COUNT(*)
       FROM @ExpectedSourceObjects expected
       INNER JOIN sys.schemas schemaRow
         ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
       INNER JOIN sys.objects objectRow
         ON objectRow.schema_id=schemaRow.schema_id
        AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
        AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType
   )
    THROW 527910, 'RT03_V7_OTO_OBJECT_CONTRACT_REJECTED', 1;

DECLARE @OtoCanonical nvarchar(max);
;WITH Parts AS
(
    SELECT
        CONCAT(
            N'T|',schemaRow.name,N'|',objectRow.name,N'|',
            columnRow.column_id,N'|',columnRow.name,N'|',typeRow.name,N'|',
            columnRow.max_length,N'|',columnRow.precision,N'|',
            columnRow.scale,N'|',CONVERT(int,columnRow.is_nullable),N'|',
            CONVERT(int,columnRow.is_identity),N'|',
            COALESCE(CONVERT(nvarchar(100),identityRow.seed_value),N''),N'|',
            COALESCE(CONVERT(nvarchar(100),identityRow.increment_value),N''),N'|',
            CONVERT(int,columnRow.is_computed),N'|',
            COALESCE(columnRow.collation_name,N''),N'|',
            COALESCE(computedRow.definition,N''),N'|',
            COALESCE(defaultRow.definition,N''))
            COLLATE DATABASE_DEFAULT AS PartValue
    FROM @ExpectedSourceObjects expected
    INNER JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
    INNER JOIN sys.objects objectRow
      ON objectRow.schema_id=schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType
    INNER JOIN sys.columns columnRow
      ON columnRow.object_id=objectRow.object_id
    INNER JOIN sys.types typeRow
      ON typeRow.user_type_id=columnRow.user_type_id
    LEFT JOIN sys.identity_columns identityRow
      ON identityRow.object_id=columnRow.object_id
     AND identityRow.column_id=columnRow.column_id
    LEFT JOIN sys.computed_columns computedRow
      ON computedRow.object_id=columnRow.object_id
     AND computedRow.column_id=columnRow.column_id
    LEFT JOIN sys.default_constraints defaultRow
      ON defaultRow.parent_object_id=columnRow.object_id
     AND defaultRow.parent_column_id=columnRow.column_id
),
Numbered AS
(
    SELECT PartValue,ROW_NUMBER() OVER(ORDER BY PartValue) RowNumber
    FROM Parts
)
SELECT @OtoCanonical =
    STRING_AGG(CONVERT(nvarchar(max),PartValue),NCHAR(10))
        WITHIN GROUP(ORDER BY RowNumber)
FROM Numbered;

IF CONVERT(
       varchar(64),
       HASHBYTES('SHA2_256',CONVERT(varbinary(max),@OtoCanonical)),
       2) <> N'$(SourceSchemaFingerprint)'
    THROW 527911, 'RT03_V7_OTO_SCHEMA_FINGERPRINT_REJECTED', 1;

DECLARE @ExpectedSourceRolePermission TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    PermissionName nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY(SchemaName,ObjectName,PermissionName)
);
INSERT @ExpectedSourceRolePermission VALUES
(N'dbo',N'KhoaHoc',N'SELECT'),
(N'dbo',N'GiaoVien',N'SELECT'),
(N'dbo',N'XeTap',N'SELECT'),
(N'dbo',N'NguoiLX',N'SELECT'),
(N'dbo',N'NguoiLX_HoSo',N'SELECT'),
(N'dbo',N'DM_HangDT',N'SELECT'),
(N'dbo',N'DM_DVHC',N'SELECT'),
(N'dbo',N'KhoaHoc_GiaoVien',N'SELECT'),
(N'dbo',N'NguoiLX',N'VIEW CHANGE TRACKING'),
(N'dbo',N'NguoiLX_HoSo',N'VIEW CHANGE TRACKING'),
(N'dbo',N'KhoaHoc',N'VIEW CHANGE TRACKING'),
(N'dbo',N'DM_HangDT',N'VIEW CHANGE TRACKING'),
(N'dbo',N'DM_DVHC',N'VIEW CHANGE TRACKING');

DECLARE @ExpectedSourceDirectPermission TABLE
(
    StateDesc nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    PermissionName nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    Class tinyint NOT NULL,
    SchemaName sysname COLLATE DATABASE_DEFAULT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NULL
);
INSERT @ExpectedSourceDirectPermission VALUES
(N'GRANT',N'CONNECT',0,NULL,NULL),
(N'GRANT',N'VIEW CHANGE TRACKING',1,N'dbo',N'DM_DVHC'),
(N'GRANT',N'VIEW CHANGE TRACKING',1,N'dbo',N'DM_HangDT'),
(N'GRANT',N'VIEW CHANGE TRACKING',1,N'dbo',N'KhoaHoc'),
(N'GRANT',N'VIEW CHANGE TRACKING',1,N'dbo',N'NguoiLX'),
(N'GRANT',N'VIEW CHANGE TRACKING',1,N'dbo',N'NguoiLX_HoSo');

IF EXISTS
(
    SELECT StateDesc,PermissionName,Class,
           COALESCE(SchemaName,N''),COALESCE(ObjectName,N'')
    FROM @ExpectedSourceDirectPermission
    EXCEPT
    SELECT permissionRow.state_desc COLLATE DATABASE_DEFAULT,
           permissionRow.permission_name COLLATE DATABASE_DEFAULT,
           permissionRow.class,
           COALESCE(schemaRow.name,N'') COLLATE DATABASE_DEFAULT,
           COALESCE(objectRow.name,N'') COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions permissionRow
    INNER JOIN sys.database_principals principalRow
      ON principalRow.principal_id=permissionRow.grantee_principal_id
    LEFT JOIN sys.objects objectRow
      ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
    LEFT JOIN sys.schemas schemaRow
      ON schemaRow.schema_id=objectRow.schema_id
    WHERE principalRow.name=@Principal
)
OR EXISTS
(
    SELECT permissionRow.state_desc COLLATE DATABASE_DEFAULT,
           permissionRow.permission_name COLLATE DATABASE_DEFAULT,
           permissionRow.class,
           COALESCE(schemaRow.name,N'') COLLATE DATABASE_DEFAULT,
           COALESCE(objectRow.name,N'') COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions permissionRow
    INNER JOIN sys.database_principals principalRow
      ON principalRow.principal_id=permissionRow.grantee_principal_id
    LEFT JOIN sys.objects objectRow
      ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
    LEFT JOIN sys.schemas schemaRow
      ON schemaRow.schema_id=objectRow.schema_id
    WHERE principalRow.name=@Principal
    EXCEPT
    SELECT StateDesc,PermissionName,Class,
           COALESCE(SchemaName,N''),COALESCE(ObjectName,N'')
    FROM @ExpectedSourceDirectPermission
)
    THROW 527912, 'RT03_V7_OTO_DIRECT_PERMISSION_BASELINE_REJECTED', 1;

DECLARE @OtoRoleExists bit =
    CONVERT(bit,CASE WHEN DATABASE_PRINCIPAL_ID(@Role) IS NULL THEN 0 ELSE 1 END);

USE [CSDL_MOTO];
IF DB_ID() <> CONVERT(int, N'$(MotoDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=CONVERT(uniqueidentifier,N'$(MotoDatabaseGuid)')
   )
    THROW 527913, 'RT03_V7_MOTO_IDENTITY_REJECTED', 1;
IF USER_ID(@Principal) IS NULL
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_principals
       WHERE name=@Principal AND type=N'U' AND sid=SUSER_SID(@Principal)
   )
    THROW 527914, 'RT03_V7_MOTO_USER_REJECTED', 1;

DECLARE @ExpectedMotoObjects TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectType char(2) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY(SchemaName,ObjectName)
);
INSERT @ExpectedMotoObjects
SELECT SchemaName,ObjectName,ObjectType FROM @ExpectedSourceObjects;

IF (SELECT COUNT(*) FROM @ExpectedMotoObjects) <>
   (
       SELECT COUNT(*)
       FROM @ExpectedMotoObjects expected
       INNER JOIN sys.schemas schemaRow
         ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
       INNER JOIN sys.objects objectRow
         ON objectRow.schema_id=schemaRow.schema_id
        AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
        AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType
   )
    THROW 527915, 'RT03_V7_MOTO_OBJECT_CONTRACT_REJECTED', 1;

DECLARE @MotoCanonical nvarchar(max);
;WITH Parts AS
(
    SELECT
        CONCAT(
            N'T|',schemaRow.name,N'|',objectRow.name,N'|',
            columnRow.column_id,N'|',columnRow.name,N'|',typeRow.name,N'|',
            columnRow.max_length,N'|',columnRow.precision,N'|',
            columnRow.scale,N'|',CONVERT(int,columnRow.is_nullable),N'|',
            CONVERT(int,columnRow.is_identity),N'|',
            COALESCE(CONVERT(nvarchar(100),identityRow.seed_value),N''),N'|',
            COALESCE(CONVERT(nvarchar(100),identityRow.increment_value),N''),N'|',
            CONVERT(int,columnRow.is_computed),N'|',
            COALESCE(columnRow.collation_name,N''),N'|',
            COALESCE(computedRow.definition,N''),N'|',
            COALESCE(defaultRow.definition,N''))
            COLLATE DATABASE_DEFAULT AS PartValue
    FROM @ExpectedMotoObjects expected
    INNER JOIN sys.schemas schemaRow
      ON schemaRow.name COLLATE DATABASE_DEFAULT=expected.SchemaName
    INNER JOIN sys.objects objectRow
      ON objectRow.schema_id=schemaRow.schema_id
     AND objectRow.name COLLATE DATABASE_DEFAULT=expected.ObjectName
     AND objectRow.type COLLATE DATABASE_DEFAULT=expected.ObjectType
    INNER JOIN sys.columns columnRow
      ON columnRow.object_id=objectRow.object_id
    INNER JOIN sys.types typeRow
      ON typeRow.user_type_id=columnRow.user_type_id
    LEFT JOIN sys.identity_columns identityRow
      ON identityRow.object_id=columnRow.object_id
     AND identityRow.column_id=columnRow.column_id
    LEFT JOIN sys.computed_columns computedRow
      ON computedRow.object_id=columnRow.object_id
     AND computedRow.column_id=columnRow.column_id
    LEFT JOIN sys.default_constraints defaultRow
      ON defaultRow.parent_object_id=columnRow.object_id
     AND defaultRow.parent_column_id=columnRow.column_id
),
Numbered AS
(
    SELECT PartValue,ROW_NUMBER() OVER(ORDER BY PartValue) RowNumber
    FROM Parts
)
SELECT @MotoCanonical =
    STRING_AGG(CONVERT(nvarchar(max),PartValue),NCHAR(10))
        WITHIN GROUP(ORDER BY RowNumber)
FROM Numbered;

IF CONVERT(
       varchar(64),
       HASHBYTES('SHA2_256',CONVERT(varbinary(max),@MotoCanonical)),
       2) <> N'$(SourceSchemaFingerprint)'
    THROW 527916, 'RT03_V7_MOTO_SCHEMA_FINGERPRINT_REJECTED', 1;

DECLARE @ExpectedMotoRolePermission TABLE
(
    SchemaName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NOT NULL,
    PermissionName nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY(SchemaName,ObjectName,PermissionName)
);
INSERT @ExpectedMotoRolePermission
SELECT SchemaName,ObjectName,PermissionName
FROM @ExpectedSourceRolePermission;

DECLARE @ExpectedMotoDirectPermission TABLE
(
    StateDesc nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    PermissionName nvarchar(60) COLLATE DATABASE_DEFAULT NOT NULL,
    Class tinyint NOT NULL,
    SchemaName sysname COLLATE DATABASE_DEFAULT NULL,
    ObjectName sysname COLLATE DATABASE_DEFAULT NULL
);
INSERT @ExpectedMotoDirectPermission
SELECT StateDesc,PermissionName,Class,SchemaName,ObjectName
FROM @ExpectedSourceDirectPermission;

IF EXISTS
(
    SELECT StateDesc,PermissionName,Class,
           COALESCE(SchemaName,N''),COALESCE(ObjectName,N'')
    FROM @ExpectedMotoDirectPermission
    EXCEPT
    SELECT permissionRow.state_desc COLLATE DATABASE_DEFAULT,
           permissionRow.permission_name COLLATE DATABASE_DEFAULT,
           permissionRow.class,
           COALESCE(schemaRow.name,N'') COLLATE DATABASE_DEFAULT,
           COALESCE(objectRow.name,N'') COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions permissionRow
    INNER JOIN sys.database_principals principalRow
      ON principalRow.principal_id=permissionRow.grantee_principal_id
    LEFT JOIN sys.objects objectRow
      ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
    LEFT JOIN sys.schemas schemaRow
      ON schemaRow.schema_id=objectRow.schema_id
    WHERE principalRow.name=@Principal
)
OR EXISTS
(
    SELECT permissionRow.state_desc COLLATE DATABASE_DEFAULT,
           permissionRow.permission_name COLLATE DATABASE_DEFAULT,
           permissionRow.class,
           COALESCE(schemaRow.name,N'') COLLATE DATABASE_DEFAULT,
           COALESCE(objectRow.name,N'') COLLATE DATABASE_DEFAULT
    FROM sys.database_permissions permissionRow
    INNER JOIN sys.database_principals principalRow
      ON principalRow.principal_id=permissionRow.grantee_principal_id
    LEFT JOIN sys.objects objectRow
      ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
    LEFT JOIN sys.schemas schemaRow
      ON schemaRow.schema_id=objectRow.schema_id
    WHERE principalRow.name=@Principal
    EXCEPT
    SELECT StateDesc,PermissionName,Class,
           COALESCE(SchemaName,N''),COALESCE(ObjectName,N'')
    FROM @ExpectedMotoDirectPermission
)
    THROW 527917, 'RT03_V7_MOTO_DIRECT_PERMISSION_BASELINE_REJECTED', 1;

DECLARE @MotoRoleExists bit =
    CONVERT(bit,CASE WHEN DATABASE_PRINCIPAL_ID(@Role) IS NULL THEN 0 ELSE 1 END);

IF CONVERT(int,@TargetRoleExists) +
   CONVERT(int,@OtoRoleExists) +
   CONVERT(int,@MotoRoleExists) NOT IN (0,3)
    THROW 527918, 'RT03_V7_ROLE_STATE_PARTIAL_REJECTED', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    USE [QLHV_APP];
    IF @TargetRoleExists=0
    BEGIN
        IF IS_ROLEMEMBER(N'db_datareader',@Principal)<>1
            THROW 527919, 'RT03_V7_TARGET_BASELINE_ROLE_REJECTED', 1;
        CREATE ROLE [QLHV_RealtimeWorkerRole] AUTHORIZATION [dbo];
    END
    ELSE
    BEGIN
        IF IS_ROLEMEMBER(@Role,@Principal)<>1
           OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>0
            THROW 527920, 'RT03_V7_TARGET_APPLIED_ROLE_REJECTED', 1;
    END;

    GRANT SELECT ON OBJECT::dbo.App_CsdtConnectionProfile TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,UPDATE ON OBJECT::dbo.App_DataVersion TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_GiaoVien TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_HocVien TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_KhoaHoc TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_KhoaHoc_GiaoVien TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.App_QlhvAutoSyncRun TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_QlhvDirectRealtimeApplyCheckpoint TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT ON OBJECT::dbo.App_QlhvDirectRealtimeApplyMarker TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT ON OBJECT::dbo.App_QlhvDirectRealtimeCycleHistory TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.App_QlhvDirectRealtimeFeatureState TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT ON OBJECT::dbo.App_QlhvDirectRealtimeManualReview TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,UPDATE ON OBJECT::dbo.App_QlhvDirectRealtimeProfileState TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,UPDATE ON OBJECT::dbo.App_QlhvDirectRealtimeWorkerState TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.App_QlhvSyncOperationHistory TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_QlhvSyncPartitionState TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_XeTap TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT,UPDATE ON OBJECT::dbo.App_XeTap_RealtimeCheckpoint TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT ON OBJECT::dbo.App_XeTap_RealtimeEvent TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT,INSERT ON OBJECT::dbo.App_XeTap_RealtimeManualReview TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW DEFINITION ON OBJECT::dbo.App_Rt03FullConvergenceSession TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW DEFINITION ON OBJECT::dbo.App_Rt03FullConvergenceDomain TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW DEFINITION ON OBJECT::dbo.App_Rt03FullConvergenceMarker TO [QLHV_RealtimeWorkerRole];
    GRANT EXECUTE ON OBJECT::dbo.usp_App_Rt03BeginFullConvergence TO [QLHV_RealtimeWorkerRole];
    GRANT EXECUTE ON OBJECT::dbo.usp_App_Rt03RecordFullConvergenceDomain TO [QLHV_RealtimeWorkerRole];
    GRANT EXECUTE ON OBJECT::dbo.usp_App_Rt03VerifyFullConvergence TO [QLHV_RealtimeWorkerRole];
    GRANT EXECUTE ON OBJECT::dbo.usp_App_Rt03FinalizeFullConvergence TO [QLHV_RealtimeWorkerRole];

    IF IS_ROLEMEMBER(@Role,@Principal)<>1
        ALTER ROLE [QLHV_RealtimeWorkerRole] ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    IF IS_ROLEMEMBER(N'db_datareader',@Principal)=1
        ALTER ROLE [db_datareader] DROP MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];

    USE [CSDL_OTO];
    IF @OtoRoleExists=0
    BEGIN
        IF IS_ROLEMEMBER(N'db_datareader',@Principal)<>1
            THROW 527921, 'RT03_V7_OTO_BASELINE_ROLE_REJECTED', 1;
        CREATE ROLE [QLHV_RealtimeWorkerRole] AUTHORIZATION [dbo];
    END
    ELSE IF IS_ROLEMEMBER(@Role,@Principal)<>1
         OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>0
        THROW 527922, 'RT03_V7_OTO_APPLIED_ROLE_REJECTED', 1;

    GRANT SELECT ON OBJECT::dbo.KhoaHoc TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.GiaoVien TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.XeTap TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.NguoiLX TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.NguoiLX_HoSo TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.DM_HangDT TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.DM_DVHC TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.KhoaHoc_GiaoVien TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX_HoSo TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.KhoaHoc TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_HangDT TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_DVHC TO [QLHV_RealtimeWorkerRole];
    IF IS_ROLEMEMBER(@Role,@Principal)<>1
        ALTER ROLE [QLHV_RealtimeWorkerRole] ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    IF IS_ROLEMEMBER(N'db_datareader',@Principal)=1
        ALTER ROLE [db_datareader] DROP MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];

    USE [CSDL_MOTO];
    IF @MotoRoleExists=0
    BEGIN
        IF IS_ROLEMEMBER(N'db_datareader',@Principal)<>1
            THROW 527923, 'RT03_V7_MOTO_BASELINE_ROLE_REJECTED', 1;
        CREATE ROLE [QLHV_RealtimeWorkerRole] AUTHORIZATION [dbo];
    END
    ELSE IF IS_ROLEMEMBER(@Role,@Principal)<>1
         OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>0
        THROW 527924, 'RT03_V7_MOTO_APPLIED_ROLE_REJECTED', 1;

    GRANT SELECT ON OBJECT::dbo.KhoaHoc TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.GiaoVien TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.XeTap TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.NguoiLX TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.NguoiLX_HoSo TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.DM_HangDT TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.DM_DVHC TO [QLHV_RealtimeWorkerRole];
    GRANT SELECT ON OBJECT::dbo.KhoaHoc_GiaoVien TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX_HoSo TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.KhoaHoc TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_HangDT TO [QLHV_RealtimeWorkerRole];
    GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_DVHC TO [QLHV_RealtimeWorkerRole];
    IF IS_ROLEMEMBER(@Role,@Principal)<>1
        ALTER ROLE [QLHV_RealtimeWorkerRole] ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    IF IS_ROLEMEMBER(N'db_datareader',@Principal)=1
        ALTER ROLE [db_datareader] DROP MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];

    USE [QLHV_APP];
    IF EXISTS
    (
        SELECT SchemaName,ObjectName,PermissionName
        FROM @ExpectedTargetRolePermission
        EXCEPT
        SELECT schemaRow.name COLLATE DATABASE_DEFAULT,
               objectRow.name COLLATE DATABASE_DEFAULT,
               permissionRow.permission_name COLLATE DATABASE_DEFAULT
        FROM sys.database_permissions permissionRow
        INNER JOIN sys.database_principals roleRow
          ON roleRow.principal_id=permissionRow.grantee_principal_id
        INNER JOIN sys.objects objectRow
          ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
        INNER JOIN sys.schemas schemaRow
          ON schemaRow.schema_id=objectRow.schema_id
        WHERE roleRow.name=@Role AND permissionRow.state=N'G'
    )
    OR EXISTS
    (
        SELECT schemaRow.name COLLATE DATABASE_DEFAULT,
               objectRow.name COLLATE DATABASE_DEFAULT,
               permissionRow.permission_name COLLATE DATABASE_DEFAULT
        FROM sys.database_permissions permissionRow
        INNER JOIN sys.database_principals roleRow
          ON roleRow.principal_id=permissionRow.grantee_principal_id
        INNER JOIN sys.objects objectRow
          ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
        INNER JOIN sys.schemas schemaRow
          ON schemaRow.schema_id=objectRow.schema_id
        WHERE roleRow.name=@Role AND permissionRow.state=N'G'
        EXCEPT
        SELECT SchemaName,ObjectName,PermissionName
        FROM @ExpectedTargetRolePermission
    )
        THROW 527925, 'RT03_V7_TARGET_ROLE_GRANT_ASSERT_FAILED', 1;

    EXECUTE AS USER=@Principal;
    IF EXISTS
    (
        SELECT 1
        FROM @ExpectedTargetRolePermission expected
        WHERE HAS_PERMS_BY_NAME(
                  QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                  N'OBJECT',
                  expected.PermissionName)<>1
    )
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'INSERT')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'UPDATE')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'DELETE')<>0
       OR HAS_PERMS_BY_NAME(N'dbo.App_HocVien',N'OBJECT',N'DELETE')<>0
       OR HAS_PERMS_BY_NAME(N'dbo.App_KhoaHoc',N'OBJECT',N'DELETE')<>0
       OR HAS_PERMS_BY_NAME(N'dbo.App_GiaoVien',N'OBJECT',N'DELETE')<>0
       OR HAS_PERMS_BY_NAME(N'dbo.App_KhoaHoc_GiaoVien',N'OBJECT',N'DELETE')<>0
       OR HAS_PERMS_BY_NAME(N'dbo.App_XeTap',N'OBJECT',N'DELETE')<>0
       OR HAS_PERMS_BY_NAME(N'sys.sp_getapplock',N'OBJECT',N'EXECUTE')<>1
       OR HAS_PERMS_BY_NAME(N'sys.sp_releaseapplock',N'OBJECT',N'EXECUTE')<>1
    BEGIN
        REVERT;
        THROW 527926, 'RT03_V7_TARGET_EFFECTIVE_PERMISSION_ASSERT_FAILED', 1;
    END;
    REVERT;

    USE [CSDL_OTO];
    IF EXISTS
    (
        SELECT SchemaName,ObjectName,PermissionName
        FROM @ExpectedSourceRolePermission
        EXCEPT
        SELECT schemaRow.name COLLATE DATABASE_DEFAULT,
               objectRow.name COLLATE DATABASE_DEFAULT,
               permissionRow.permission_name COLLATE DATABASE_DEFAULT
        FROM sys.database_permissions permissionRow
        INNER JOIN sys.database_principals roleRow
          ON roleRow.principal_id=permissionRow.grantee_principal_id
        INNER JOIN sys.objects objectRow
          ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
        INNER JOIN sys.schemas schemaRow
          ON schemaRow.schema_id=objectRow.schema_id
        WHERE roleRow.name=@Role AND permissionRow.state=N'G'
    )
    OR EXISTS
    (
        SELECT schemaRow.name COLLATE DATABASE_DEFAULT,
               objectRow.name COLLATE DATABASE_DEFAULT,
               permissionRow.permission_name COLLATE DATABASE_DEFAULT
        FROM sys.database_permissions permissionRow
        INNER JOIN sys.database_principals roleRow
          ON roleRow.principal_id=permissionRow.grantee_principal_id
        INNER JOIN sys.objects objectRow
          ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
        INNER JOIN sys.schemas schemaRow
          ON schemaRow.schema_id=objectRow.schema_id
        WHERE roleRow.name=@Role AND permissionRow.state=N'G'
        EXCEPT
        SELECT SchemaName,ObjectName,PermissionName
        FROM @ExpectedSourceRolePermission
    )
        THROW 527927, 'RT03_V7_OTO_ROLE_GRANT_ASSERT_FAILED', 1;
    EXECUTE AS USER=@Principal;
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedSourceRolePermission expected
        WHERE HAS_PERMS_BY_NAME(
                  QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                  N'OBJECT',
                  expected.PermissionName)<>1
    )
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'INSERT')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'UPDATE')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'DELETE')<>0
       OR EXISTS
          (
              SELECT 1
              FROM @ExpectedSourceObjects expected
              WHERE HAS_PERMS_BY_NAME(
                        QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                        N'OBJECT',N'INSERT')<>0
                 OR HAS_PERMS_BY_NAME(
                        QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                        N'OBJECT',N'UPDATE')<>0
                 OR HAS_PERMS_BY_NAME(
                        QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                        N'OBJECT',N'DELETE')<>0
          )
    BEGIN
        REVERT;
        THROW 527928, 'RT03_V7_OTO_EFFECTIVE_PERMISSION_ASSERT_FAILED', 1;
    END;
    REVERT;

    USE [CSDL_MOTO];
    IF EXISTS
    (
        SELECT SchemaName,ObjectName,PermissionName
        FROM @ExpectedMotoRolePermission
        EXCEPT
        SELECT schemaRow.name COLLATE DATABASE_DEFAULT,
               objectRow.name COLLATE DATABASE_DEFAULT,
               permissionRow.permission_name COLLATE DATABASE_DEFAULT
        FROM sys.database_permissions permissionRow
        INNER JOIN sys.database_principals roleRow
          ON roleRow.principal_id=permissionRow.grantee_principal_id
        INNER JOIN sys.objects objectRow
          ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
        INNER JOIN sys.schemas schemaRow
          ON schemaRow.schema_id=objectRow.schema_id
        WHERE roleRow.name=@Role AND permissionRow.state=N'G'
    )
    OR EXISTS
    (
        SELECT schemaRow.name COLLATE DATABASE_DEFAULT,
               objectRow.name COLLATE DATABASE_DEFAULT,
               permissionRow.permission_name COLLATE DATABASE_DEFAULT
        FROM sys.database_permissions permissionRow
        INNER JOIN sys.database_principals roleRow
          ON roleRow.principal_id=permissionRow.grantee_principal_id
        INNER JOIN sys.objects objectRow
          ON permissionRow.class=1 AND objectRow.object_id=permissionRow.major_id
        INNER JOIN sys.schemas schemaRow
          ON schemaRow.schema_id=objectRow.schema_id
        WHERE roleRow.name=@Role AND permissionRow.state=N'G'
        EXCEPT
        SELECT SchemaName,ObjectName,PermissionName
        FROM @ExpectedMotoRolePermission
    )
        THROW 527929, 'RT03_V7_MOTO_ROLE_GRANT_ASSERT_FAILED', 1;
    EXECUTE AS USER=@Principal;
    IF EXISTS
    (
        SELECT 1 FROM @ExpectedMotoRolePermission expected
        WHERE HAS_PERMS_BY_NAME(
                  QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                  N'OBJECT',
                  expected.PermissionName)<>1
    )
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'INSERT')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'UPDATE')<>0
       OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'DELETE')<>0
       OR EXISTS
          (
              SELECT 1
              FROM @ExpectedMotoObjects expected
              WHERE HAS_PERMS_BY_NAME(
                        QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                        N'OBJECT',N'INSERT')<>0
                 OR HAS_PERMS_BY_NAME(
                        QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                        N'OBJECT',N'UPDATE')<>0
                 OR HAS_PERMS_BY_NAME(
                        QUOTENAME(expected.SchemaName)+N'.'+QUOTENAME(expected.ObjectName),
                        N'OBJECT',N'DELETE')<>0
          )
    BEGIN
        REVERT;
        THROW 527930, 'RT03_V7_MOTO_EFFECTIVE_PERMISSION_ASSERT_FAILED', 1;
    END;
    REVERT;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    N'RT03_V7_LEAST_PRIVILEGE_PERMISSIONS_APPLIED' AS Result,
    @TargetRoleExists AS TargetRoleExistedBefore,
    @OtoRoleExists AS OtoRoleExistedBefore,
    @MotoRoleExists AS MotoRoleExistedBefore;
GO
