USE [EXACT_TEST_DB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- REVIEW TEMPLATE ONLY. RT-02A MUST NOT EXECUTE THIS FILE.
DECLARE @ExpectedDatabase sysname = N'EXACT_TEST_DB';
DECLARE @ExpectedServer nvarchar(128) = N'__RT02_SQL_SERVER_INSTANCE__';
DECLARE @ExpectedEnvironmentId nvarchar(128) = N'__RT02_ENVIRONMENT_ID__';
DECLARE @ExpectedApprovalId nvarchar(128) = N'__RT02_OWNER_APPROVAL_ID__';
DECLARE @ExpectedDatabaseId int = 0; -- __RT02_DATABASE_ID__
DECLARE @ExpectedDatabaseGuid uniqueidentifier =
    TRY_CONVERT(uniqueidentifier, N'__RT02_DATABASE_GUID__');

IF @ExpectedDatabase = N'EXACT_TEST_DB'
   OR @ExpectedServer LIKE N'__RT02_%'
   OR @ExpectedEnvironmentId LIKE N'__RT02_%'
   OR @ExpectedApprovalId LIKE N'__RT02_%'
   OR @ExpectedDatabaseId <= 0
   OR @ExpectedDatabaseGuid IS NULL
    THROW 527320, 'RT02 rollback template placeholders are unresolved.', 1;

IF DB_NAME() <> @ExpectedDatabase
   OR DB_NAME() IN
      (
          N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
          N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
      )
   OR CAST(SERVERPROPERTY(N'ServerName') AS nvarchar(128)) <> @ExpectedServer
   OR DB_ID() <> @ExpectedDatabaseId
   OR
      (
          SELECT database_guid
          FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
      ) <> @ExpectedDatabaseGuid
    THROW 527321, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO rollback identity.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) = @ExpectedEnvironmentId
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) = @ExpectedApprovalId
)
    THROW 527322, 'RT02 approved TEST markers are missing for MOTO rollback.', 1;
GO

IF EXISTS
(
    SELECT 1 FROM sys.change_tracking_tables
    WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
)
    ALTER TABLE dbo.NguoiLX_HoSo DISABLE CHANGE_TRACKING;
GO

IF EXISTS
(
    SELECT 1 FROM sys.change_tracking_tables
    WHERE object_id = OBJECT_ID(N'dbo.NguoiLX', N'U')
)
    ALTER TABLE dbo.NguoiLX DISABLE CHANGE_TRACKING;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID()
)
    ALTER DATABASE [EXACT_TEST_DB] SET CHANGE_TRACKING = OFF;
GO

IF
(
    SELECT snapshot_isolation_state
    FROM sys.databases
    WHERE database_id = DB_ID()
) <> 0
    ALTER DATABASE [EXACT_TEST_DB] SET ALLOW_SNAPSHOT_ISOLATION OFF;
GO

-- READ_COMMITTED_SNAPSHOT is intentionally not changed.
