USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
    N'CSDLTTTC\QLHVRT02'
    THROW 527400, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server identity.', 1;

IF CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE
      N'Developer Edition%'
    THROW 527401, 'ISOLATED_DATABASE_IDENTITY_REJECTED: engine identity.', 1;

IF EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
    THROW 527402, 'ISOLATED_DATABASE_IDENTITY_REJECTED: linked server.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
        N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
    )
)
    THROW 527403, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production database.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_OTO_TEST'
      AND databaseItem.database_id = 5
      AND recovery.database_guid =
          'FEE7CD94-A717-4E73-89F0-0FBFF71D1789'
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.snapshot_isolation_state = 1
      AND databaseItem.is_read_committed_snapshot_on = 0
)
    THROW 527404, 'RT02 OTO enabled feature state is invalid.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_MOTO_TEST'
      AND databaseItem.database_id = 6
      AND recovery.database_guid =
          '6D8101F9-07AB-4F0F-B378-29ED084F7B2A'
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.snapshot_isolation_state = 1
      AND databaseItem.is_read_committed_snapshot_on = 0
)
    THROW 527405, 'RT02 MOTO enabled feature state is invalid.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_TARGET_TEST'
      AND databaseItem.database_id = 7
      AND recovery.database_guid =
          'F7BAC56F-8329-47AB-A17C-A0D592ADD484'
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.snapshot_isolation_state = 0
      AND databaseItem.is_read_committed_snapshot_on = 0
)
    THROW 527406, 'RT02 target feature state is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.change_tracking_databases
    WHERE database_id IN (5, 6)
      AND retention_period = 2
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
) <> 2
   OR EXISTS
   (
       SELECT 1
       FROM sys.change_tracking_databases
       WHERE database_id NOT IN (5, 6)
   )
    THROW 527407, 'RT02 Change Tracking database allowlist is invalid.', 1;

SELECT
    N'RT02_FEATURES_ON_SERVER' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_databases)
        AS ChangeTrackingDatabaseCount,
    (SELECT snapshot_isolation_state FROM sys.databases
        WHERE name = N'QLHV_RT02_OTO_TEST') AS OtoSnapshotState,
    (SELECT snapshot_isolation_state FROM sys.databases
        WHERE name = N'QLHV_RT02_MOTO_TEST') AS MotoSnapshotState,
    (SELECT snapshot_isolation_state FROM sys.databases
        WHERE name = N'QLHV_RT02_TARGET_TEST') AS TargetSnapshotState,
    (SELECT SUM(CONVERT(int, is_read_committed_snapshot_on))
        FROM sys.databases
        WHERE name IN
        (
            N'QLHV_RT02_OTO_TEST',
            N'QLHV_RT02_MOTO_TEST',
            N'QLHV_RT02_TARGET_TEST'
        )) AS RcsiEnabledDatabaseCount;
GO

USE [QLHV_RT02_OTO_TEST];
GO
SET NOCOUNT ON;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.change_tracking_tables
    WHERE object_id IN
    (
        OBJECT_ID(N'dbo.NguoiLX', N'U'),
        OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
    )
      AND is_track_columns_updated_on = 1
) <> 2
   OR (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) <> 2
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527408, 'RT02 OTO table Change Tracking allowlist is invalid.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_OWNER_APPROVAL_ID'
         AND CONVERT(nvarchar(128), value) =
             N'RT02B-OPERATOR-APPROVAL-20260727-01'
   )
    THROW 527409, 'RT02 OTO TEST markers are invalid.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables)
        AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables
        WHERE is_track_columns_updated_on = 1)
        AS TrackColumnsUpdatedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SET NOCOUNT ON;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.change_tracking_tables
    WHERE object_id IN
    (
        OBJECT_ID(N'dbo.NguoiLX', N'U'),
        OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
    )
      AND is_track_columns_updated_on = 1
) <> 2
   OR (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) <> 2
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527410, 'RT02 MOTO table Change Tracking allowlist is invalid.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_OWNER_APPROVAL_ID'
         AND CONVERT(nvarchar(128), value) =
             N'RT02B-OPERATOR-APPROVAL-20260727-01'
   )
    THROW 527411, 'RT02 MOTO TEST markers are invalid.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables)
        AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables
        WHERE is_track_columns_updated_on = 1)
        AS TrackColumnsUpdatedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO
