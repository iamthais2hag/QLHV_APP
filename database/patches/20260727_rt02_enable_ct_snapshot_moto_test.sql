USE [EXACT_TEST_DB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- REVIEW TEMPLATE ONLY. RT-02A MUST NOT EXECUTE THIS FILE.
-- RT-02B must replace every __RT02_*__ value from a separately approved
-- isolated-environment identity record.
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
    THROW 527290, 'RT02 template placeholders must be replaced from an approved isolated environment.', 1;

IF DB_NAME() <> @ExpectedDatabase
    THROW 527291, 'RT02 exact isolated MOTO database guard failed.', 1;

IF DB_NAME() IN
(
    N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
    N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
)
    THROW 527292, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production database name.', 1;

IF CAST(SERVERPROPERTY(N'ServerName') AS nvarchar(128)) <> @ExpectedServer
    THROW 527293, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server identity.', 1;

IF DB_ID() <> @ExpectedDatabaseId
    THROW 527294, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database_id.', 1;

IF
(
    SELECT database_guid
    FROM sys.database_recovery_status
    WHERE database_id = DB_ID()
) <> @ExpectedDatabaseGuid
    THROW 527295, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database GUID.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
      AND recovery_model_desc IS NOT NULL
)
    THROW 527296, 'RT02 isolated database must be ONLINE and READ_WRITE.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) = @ExpectedEnvironmentId
)
    THROW 527297, 'RT02 approved TEST environment marker is missing.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) = @ExpectedApprovalId
)
    THROW 527298, 'RT02 owner approval marker is missing.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL
    THROW 527299, 'RT02 MOTO table allowlist preflight failed.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX', N'U')
      AND [type] = N'PK'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
      AND [type] = N'PK'
)
    THROW 527300, 'RT02 MOTO allowlisted tables require primary keys.', 1;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID()
      AND retention_period = 2
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
)
BEGIN
    ALTER DATABASE [EXACT_TEST_DB]
        SET CHANGE_TRACKING = ON
        (
            CHANGE_RETENTION = 2 DAYS,
            AUTO_CLEANUP = ON
        );
END;
GO

IF
(
    SELECT snapshot_isolation_state
    FROM sys.databases
    WHERE database_id = DB_ID()
) = 0
    ALTER DATABASE [EXACT_TEST_DB] SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

-- READ_COMMITTED_SNAPSHOT is intentionally not enabled without separate approval.
IF NOT EXISTS
(
    SELECT 1 FROM sys.change_tracking_tables
    WHERE object_id = OBJECT_ID(N'dbo.NguoiLX', N'U')
)
    ALTER TABLE dbo.NguoiLX
        ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.change_tracking_tables
    WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
)
    ALTER TABLE dbo.NguoiLX_HoSo
        ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
GO

IF
(
    SELECT COUNT(*)
    FROM sys.change_tracking_tables
    WHERE object_id IN
    (
        OBJECT_ID(N'dbo.NguoiLX', N'U'),
        OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
    )
      AND is_track_columns_updated_on = 1
) <> 2
    THROW 527301, 'RT02 MOTO Change Tracking verification failed.', 1;
GO
