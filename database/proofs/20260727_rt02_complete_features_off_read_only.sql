USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
    N'CSDLTTTC\QLHVRT02'
    THROW 527420, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server identity.', 1;

IF EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
   OR EXISTS
   (
       SELECT 1
       FROM sys.databases
       WHERE name IN
       (
           N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
           N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
       )
   )
    THROW 527421, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production route.', 1;

IF EXISTS (SELECT 1 FROM sys.change_tracking_databases)
    THROW 527422, 'RT02 Change Tracking database cleanup failed.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
      AND
      (
          state_desc <> N'ONLINE'
          OR is_read_only <> 0
          OR snapshot_isolation_state <> 0
          OR is_read_committed_snapshot_on <> 0
      )
)
    THROW 527423, 'RT02 Snapshot/RCSI cleanup state is invalid.', 1;

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
)
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.databases AS databaseItem
       INNER JOIN sys.database_recovery_status AS recovery
           ON recovery.database_id = databaseItem.database_id
       WHERE databaseItem.name = N'QLHV_RT02_MOTO_TEST'
         AND databaseItem.database_id = 6
         AND recovery.database_guid =
             '6D8101F9-07AB-4F0F-B378-29ED084F7B2A'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.databases AS databaseItem
       INNER JOIN sys.database_recovery_status AS recovery
           ON recovery.database_id = databaseItem.database_id
       WHERE databaseItem.name = N'QLHV_RT02_TARGET_TEST'
         AND databaseItem.database_id = 7
         AND recovery.database_guid =
             'F7BAC56F-8329-47AB-A17C-A0D592ADD484'
   )
    THROW 527424, 'RT02 database identity changed during cleanup.', 1;

SELECT
    N'RT02_FEATURES_OFF_SERVER' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_databases)
        AS ChangeTrackingDatabaseCount,
    (SELECT SUM(CONVERT(int, snapshot_isolation_state))
        FROM sys.databases
        WHERE name IN
        (
            N'QLHV_RT02_OTO_TEST',
            N'QLHV_RT02_MOTO_TEST',
            N'QLHV_RT02_TARGET_TEST'
        )) AS SnapshotStateSum,
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

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527425, 'RT02 OTO feature cleanup failed.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables)
        AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527426, 'RT02 MOTO feature cleanup failed.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables)
        AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527427, 'RT02 target feature state changed unexpectedly.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables)
        AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO
