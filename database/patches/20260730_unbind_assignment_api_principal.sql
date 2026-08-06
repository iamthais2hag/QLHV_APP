/*
    Remove the production API database user from QLHV_AssignmentApiRole before
    running the exact assignment rollback.

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
    THROW 529380, 'QLHV_APP database identity mismatch.', 1;

IF @Principal IS NULL OR @Principal LIKE N'%$(%'
    THROW 529381, 'AssignmentApiPrincipal must be supplied explicitly and must not be empty.', 1;

IF @Principal IN
   (N'dbo',N'guest',N'public',N'NT SERVICE\QLHV_APP_RealtimeWorker')
    THROW 529384, 'AssignmentApiPrincipal is reserved or is the realtime writer.', 1;

IF DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole') IS NULL
   OR DATABASE_PRINCIPAL_ID(@Principal) IS NULL
    THROW 529382, 'Assignment role or API database user is missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members
    WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
      AND member_principal_id=DATABASE_PRINCIPAL_ID(@Principal)
)
BEGIN
    EXEC(N'ALTER ROLE [QLHV_AssignmentApiRole] DROP MEMBER ' + QUOTENAME(@Principal) + N';');
END;

IF EXISTS
(
    SELECT 1
    FROM sys.database_role_members
    WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'QLHV_AssignmentApiRole')
      AND member_principal_id=DATABASE_PRINCIPAL_ID(@Principal)
)
    THROW 529383, 'Assignment API role membership removal verification failed.', 1;
GO

SELECT N'ASSIGNMENT_API_PRINCIPAL_UNBOUND_AND_VERIFIED' AS Result;
GO
