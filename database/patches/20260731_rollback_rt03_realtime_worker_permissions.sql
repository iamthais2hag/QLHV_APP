USE [QLHV_APP];
GO
/*
  Roll back only the RT03 V7 dedicated roles and restore the exact V6
  db_datareader membership baseline. Direct V6/vehicle grants are not changed.
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

DECLARE @Principal sysname=N'NT SERVICE\QLHV_APP_RealtimeWorker';
DECLARE @Role sysname=N'QLHV_RealtimeWorkerRole';

IF CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName'))<>
   N'$(ExpectedServerName)'
    THROW 527950,'RT03_V7_ROLLBACK_SERVER_IDENTITY_REJECTED',1;

USE [master];
IF NOT EXISTS
(
    SELECT 1 FROM sys.server_principals
    WHERE name=@Principal AND type=N'U' AND is_disabled=0
)
    THROW 527951,'RT03_V7_ROLLBACK_LOGIN_REJECTED',1;

USE [QLHV_APP];
IF DB_ID()<>CONVERT(int,N'$(TargetDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=CONVERT(uniqueidentifier,N'$(TargetDatabaseGuid)')
   )
    THROW 527952,'RT03_V7_ROLLBACK_TARGET_IDENTITY_REJECTED',1;

USE [CSDL_OTO];
IF DB_ID()<>CONVERT(int,N'$(OtoDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=CONVERT(uniqueidentifier,N'$(OtoDatabaseGuid)')
   )
    THROW 527953,'RT03_V7_ROLLBACK_OTO_IDENTITY_REJECTED',1;

USE [CSDL_MOTO];
IF DB_ID()<>CONVERT(int,N'$(MotoDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=CONVERT(uniqueidentifier,N'$(MotoDatabaseGuid)')
   )
    THROW 527954,'RT03_V7_ROLLBACK_MOTO_IDENTITY_REJECTED',1;

BEGIN TRY
    BEGIN TRANSACTION;

    USE [QLHV_APP];
    IF DATABASE_PRINCIPAL_ID(@Role) IS NULL
       OR IS_ROLEMEMBER(@Role,@Principal)<>1
       OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>0
       OR EXISTS
          (
              SELECT 1
              FROM sys.database_role_members memberRow
              INNER JOIN sys.database_principals roleRow
                ON roleRow.principal_id=memberRow.role_principal_id
              INNER JOIN sys.database_principals memberPrincipal
                ON memberPrincipal.principal_id=memberRow.member_principal_id
              WHERE roleRow.name=@Role AND memberPrincipal.name<>@Principal
          )
    THROW 527955,'RT03_V7_ROLLBACK_TARGET_STATE_UNSAFE',1;

    ALTER ROLE [QLHV_RealtimeWorkerRole]
        DROP MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    ALTER ROLE [db_datareader]
        ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    REVOKE SELECT ON OBJECT::dbo.App_CsdtConnectionProfile FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,UPDATE ON OBJECT::dbo.App_DataVersion FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_GiaoVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_HocVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_KhoaHoc FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_KhoaHoc_GiaoVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.App_QlhvAutoSyncRun FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_QlhvDirectRealtimeApplyCheckpoint FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_QlhvDirectRealtimeApplyMarker FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_QlhvDirectRealtimeCycleHistory FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.App_QlhvDirectRealtimeFeatureState FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_QlhvDirectRealtimeManualReview FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,UPDATE ON OBJECT::dbo.App_QlhvDirectRealtimeProfileState FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,UPDATE ON OBJECT::dbo.App_QlhvDirectRealtimeWorkerState FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.App_QlhvSyncOperationHistory FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_QlhvSyncPartitionState FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_XeTap FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT,UPDATE ON OBJECT::dbo.App_XeTap_RealtimeCheckpoint FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_XeTap_RealtimeEvent FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT,INSERT ON OBJECT::dbo.App_XeTap_RealtimeManualReview FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW DEFINITION ON OBJECT::dbo.App_Rt03FullConvergenceSession FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW DEFINITION ON OBJECT::dbo.App_Rt03FullConvergenceDomain FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW DEFINITION ON OBJECT::dbo.App_Rt03FullConvergenceMarker FROM [QLHV_RealtimeWorkerRole];
    REVOKE EXECUTE ON OBJECT::dbo.usp_App_Rt03BeginFullConvergence FROM [QLHV_RealtimeWorkerRole];
    REVOKE EXECUTE ON OBJECT::dbo.usp_App_Rt03RecordFullConvergenceDomain FROM [QLHV_RealtimeWorkerRole];
    REVOKE EXECUTE ON OBJECT::dbo.usp_App_Rt03VerifyFullConvergence FROM [QLHV_RealtimeWorkerRole];
    REVOKE EXECUTE ON OBJECT::dbo.usp_App_Rt03FinalizeFullConvergence FROM [QLHV_RealtimeWorkerRole];
    IF EXISTS
    (
        SELECT 1 FROM sys.database_permissions
        WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(@Role)
    )
       OR EXISTS
       (
           SELECT 1 FROM sys.database_role_members
           WHERE role_principal_id=DATABASE_PRINCIPAL_ID(@Role)
       )
        THROW 527956,'RT03_V7_ROLLBACK_TARGET_ROLE_NOT_EMPTY',1;
    DROP ROLE [QLHV_RealtimeWorkerRole];

    USE [CSDL_OTO];
    IF DATABASE_PRINCIPAL_ID(@Role) IS NULL
       OR IS_ROLEMEMBER(@Role,@Principal)<>1
       OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>0
       OR EXISTS
          (
              SELECT 1
              FROM sys.database_role_members memberRow
              INNER JOIN sys.database_principals roleRow
                ON roleRow.principal_id=memberRow.role_principal_id
              INNER JOIN sys.database_principals memberPrincipal
                ON memberPrincipal.principal_id=memberRow.member_principal_id
              WHERE roleRow.name=@Role AND memberPrincipal.name<>@Principal
          )
        THROW 527957,'RT03_V7_ROLLBACK_OTO_STATE_UNSAFE',1;
    ALTER ROLE [QLHV_RealtimeWorkerRole]
        DROP MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    ALTER ROLE [db_datareader]
        ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    REVOKE SELECT ON OBJECT::dbo.KhoaHoc FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.GiaoVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.XeTap FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.NguoiLX FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.NguoiLX_HoSo FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.DM_HangDT FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.DM_DVHC FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.KhoaHoc_GiaoVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX_HoSo FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.KhoaHoc FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.DM_HangDT FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.DM_DVHC FROM [QLHV_RealtimeWorkerRole];
    IF EXISTS
    (
        SELECT 1 FROM sys.database_permissions
        WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(@Role)
    )
       OR EXISTS
       (
           SELECT 1 FROM sys.database_role_members
           WHERE role_principal_id=DATABASE_PRINCIPAL_ID(@Role)
       )
        THROW 527958,'RT03_V7_ROLLBACK_OTO_ROLE_NOT_EMPTY',1;
    DROP ROLE [QLHV_RealtimeWorkerRole];

    USE [CSDL_MOTO];
    IF DATABASE_PRINCIPAL_ID(@Role) IS NULL
       OR IS_ROLEMEMBER(@Role,@Principal)<>1
       OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>0
       OR EXISTS
          (
              SELECT 1
              FROM sys.database_role_members memberRow
              INNER JOIN sys.database_principals roleRow
                ON roleRow.principal_id=memberRow.role_principal_id
              INNER JOIN sys.database_principals memberPrincipal
                ON memberPrincipal.principal_id=memberRow.member_principal_id
              WHERE roleRow.name=@Role AND memberPrincipal.name<>@Principal
          )
        THROW 527959,'RT03_V7_ROLLBACK_MOTO_STATE_UNSAFE',1;
    ALTER ROLE [QLHV_RealtimeWorkerRole]
        DROP MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    ALTER ROLE [db_datareader]
        ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
    REVOKE SELECT ON OBJECT::dbo.KhoaHoc FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.GiaoVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.XeTap FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.NguoiLX FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.NguoiLX_HoSo FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.DM_HangDT FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.DM_DVHC FROM [QLHV_RealtimeWorkerRole];
    REVOKE SELECT ON OBJECT::dbo.KhoaHoc_GiaoVien FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX_HoSo FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.KhoaHoc FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.DM_HangDT FROM [QLHV_RealtimeWorkerRole];
    REVOKE VIEW CHANGE TRACKING ON OBJECT::dbo.DM_DVHC FROM [QLHV_RealtimeWorkerRole];
    IF EXISTS
    (
        SELECT 1 FROM sys.database_permissions
        WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(@Role)
    )
       OR EXISTS
       (
           SELECT 1 FROM sys.database_role_members
           WHERE role_principal_id=DATABASE_PRINCIPAL_ID(@Role)
       )
        THROW 527960,'RT03_V7_ROLLBACK_MOTO_ROLE_NOT_EMPTY',1;
    DROP ROLE [QLHV_RealtimeWorkerRole];

    USE [QLHV_APP];
    IF DATABASE_PRINCIPAL_ID(@Role) IS NOT NULL
       OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>1
        THROW 527961,'RT03_V7_ROLLBACK_TARGET_BASELINE_ASSERT_FAILED',1;
    USE [CSDL_OTO];
    IF DATABASE_PRINCIPAL_ID(@Role) IS NOT NULL
       OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>1
        THROW 527962,'RT03_V7_ROLLBACK_OTO_BASELINE_ASSERT_FAILED',1;
    USE [CSDL_MOTO];
    IF DATABASE_PRINCIPAL_ID(@Role) IS NOT NULL
       OR IS_ROLEMEMBER(N'db_datareader',@Principal)<>1
        THROW 527963,'RT03_V7_ROLLBACK_MOTO_BASELINE_ASSERT_FAILED',1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT N'RT03_V7_WORKER_PERMISSIONS_ROLLED_BACK_TO_V6_BASELINE' AS Result;
GO
