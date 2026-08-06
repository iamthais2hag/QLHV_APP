USE [$(Rt03TargetDatabase)];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Role sysname=N'QLHV_RealtimeWorkerRole';
DECLARE @Principal sysname=N'NT SERVICE\QLHV_APP_RealtimeWorker';

IF DB_NAME()<>N'QLHV_APP'
   AND ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'RT03_MASTER_DISPOSABLE_REHEARSAL')),0)<>1
    THROW 527830, 'RT03_MASTER_PERMISSION_WRONG_DATABASE', 1;
IF USER_ID(@Role) IS NULL OR USER_ID(@Principal) IS NULL
    THROW 527831, 'RT03_MASTER_V7_PRINCIPAL_MISSING', 1;
IF IS_ROLEMEMBER(@Role,@Principal)<>1
   OR IS_ROLEMEMBER(N'db_owner',@Principal)<>0
   OR IS_ROLEMEMBER(N'db_datawriter',@Principal)<>0
   OR IS_SRVROLEMEMBER(N'sysadmin',@Principal)<>0
    THROW 527832, 'RT03_MASTER_V7_BASELINE_REJECTED', 1;
IF OBJECT_ID(N'dbo.App_Rt03RealtimeControl',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_Rt03RealtimeControlAudit',N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest',N'U') IS NULL
    THROW 527833, 'RT03_MASTER_SCHEMA_MISSING', 1;

BEGIN TRANSACTION;
GRANT SELECT,UPDATE ON OBJECT::dbo.App_Rt03RealtimeControl
    TO [QLHV_RealtimeWorkerRole];
GRANT INSERT ON OBJECT::dbo.App_Rt03RealtimeControlAudit
    TO [QLHV_RealtimeWorkerRole];
GRANT SELECT,UPDATE ON OBJECT::dbo.App_Rt03RealtimeRunRequest
    TO [QLHV_RealtimeWorkerRole];
DENY DELETE ON OBJECT::dbo.App_Rt03RealtimeControl
    TO [QLHV_RealtimeWorkerRole];
DENY UPDATE,DELETE ON OBJECT::dbo.App_Rt03RealtimeControlAudit
    TO [QLHV_RealtimeWorkerRole];
DENY INSERT,DELETE ON OBJECT::dbo.App_Rt03RealtimeRunRequest
    TO [QLHV_RealtimeWorkerRole];
COMMIT TRANSACTION;

EXECUTE AS USER=N'NT SERVICE\QLHV_APP_RealtimeWorker';
DECLARE @Pass bit=CONVERT(bit,CASE WHEN
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControl',N'OBJECT',N'SELECT')=1 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControl',N'OBJECT',N'UPDATE')=1 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControl',N'OBJECT',N'DELETE')=0 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControlAudit',N'OBJECT',N'INSERT')=1 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControlAudit',N'OBJECT',N'UPDATE')=0 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeControlAudit',N'OBJECT',N'DELETE')=0 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeRunRequest',N'OBJECT',N'SELECT')=1 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeRunRequest',N'OBJECT',N'UPDATE')=1 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeRunRequest',N'OBJECT',N'INSERT')=0 AND
    HAS_PERMS_BY_NAME(N'dbo.App_Rt03RealtimeRunRequest',N'OBJECT',N'DELETE')=0
THEN 1 ELSE 0 END);
REVERT;
IF @Pass<>1 THROW 527834, 'RT03_MASTER_EFFECTIVE_PERMISSION_REJECTED', 1;

SELECT N'PASS' AS PermissionStatus,@Principal AS PrincipalName,@Role AS RoleName;
GO
