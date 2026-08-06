/*
    Bind the already-created production API database user to the least-privilege
    assignment role. Run only after 20260730_add_integrated_course_assignment.sql.

    Example (do not log credentials):
      sqlcmd -S <server> -d QLHV_APP -E -C -b -v AssignmentApiPrincipal="<database-user>" -i <this-file>

    PRODUCTION STATUS: NOT EXECUTED BY THIS TASK.
*/

:on error exit

USE [QLHV_APP];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @ExpectedProductionDatabaseGuid uniqueidentifier =
    '9C44B304-8A84-4D0D-9A82-19C7233FF6BB';
DECLARE @Principal sysname = NULLIF(LTRIM(RTRIM(N'$(AssignmentApiPrincipal)')),N'');

IF DB_NAME()<>N'QLHV_APP'
   OR NOT EXISTS
      (SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID() AND database_guid=@ExpectedProductionDatabaseGuid)
    THROW 529370, 'QLHV_APP database identity mismatch.', 1;

IF @Principal IS NULL OR @Principal LIKE N'%$(%'
    THROW 529371, 'AssignmentApiPrincipal must be supplied explicitly and must not be empty.', 1;

IF @Principal IN
   (N'dbo',N'guest',N'public',N'NT SERVICE\QLHV_APP_RealtimeWorker')
    THROW 529372, 'AssignmentApiPrincipal is reserved or is the realtime writer.', 1;

IF DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole') IS NULL
    THROW 529373, 'QLHV_AssignmentApiRole is missing; apply the assignment migration first.', 1;

IF DATABASE_PRINCIPAL_ID(@Principal) IS NULL
    THROW 529374, 'AssignmentApiPrincipal is not an existing database user.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.database_role_members
        WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
          AND member_principal_id=DATABASE_PRINCIPAL_ID(@Principal)
    )
    BEGIN
        EXEC(N'ALTER ROLE [QLHV_AssignmentApiRole] ADD MEMBER ' + QUOTENAME(@Principal) + N';');
    END;

    DECLARE @Verify nvarchar(max)=N'
BEGIN TRY
    EXECUTE AS USER=' + QUOTENAME(@Principal,'''') + N';
    IF
       HAS_PERMS_BY_NAME(N''dbo.App_GiaoVien_hs'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_GiaoVien_hs'',N''OBJECT'',N''INSERT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_GiaoVien_hs'',N''OBJECT'',N''UPDATE'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_KhoaHoc_NhomDaoTao'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_KhoaHoc_NhomDaoTao'',N''OBJECT'',N''INSERT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_KhoaHoc_NhomDaoTao'',N''OBJECT'',N''UPDATE'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_HocVien_PhanCong'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_HocVien_PhanCong'',N''OBJECT'',N''INSERT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_HocVien_PhanCong'',N''OBJECT'',N''UPDATE'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_ImportBatch'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_ImportBatch'',N''OBJECT'',N''INSERT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_ImportBatch'',N''OBJECT'',N''UPDATE'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_AssignmentOperation'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_AssignmentOperation'',N''OBJECT'',N''INSERT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_KhoaHoc'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_HocVien'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_GiaoVien'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_XeTap'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_AuditLog'',N''OBJECT'',N''SELECT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_AuditLog'',N''OBJECT'',N''INSERT'')<>1
       OR HAS_PERMS_BY_NAME(N''dbo.App_GiaoVien_hs'',N''OBJECT'',N''DELETE'')<>0
       OR HAS_PERMS_BY_NAME(N''dbo.App_KhoaHoc_NhomDaoTao'',N''OBJECT'',N''DELETE'')<>0
       OR HAS_PERMS_BY_NAME(N''dbo.App_HocVien_PhanCong'',N''OBJECT'',N''DELETE'')<>0
       OR HAS_PERMS_BY_NAME(N''dbo.App_ImportBatch'',N''OBJECT'',N''DELETE'')<>0
       OR HAS_PERMS_BY_NAME(N''dbo.App_AssignmentOperation'',N''OBJECT'',N''DELETE'')<>0
        THROW 529375, ''AssignmentApiPrincipal effective permission verification failed.'', 1;
    REVERT;
END TRY
BEGIN CATCH
    BEGIN TRY
        REVERT;
    END TRY
    BEGIN CATCH
        -- The failed permission check may already have reverted its context.
    END CATCH;
    THROW;
END CATCH;';
    EXEC sys.sp_executesql @Verify;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT N'ASSIGNMENT_API_PRINCIPAL_BOUND_AND_VERIFIED' AS Result;
GO
