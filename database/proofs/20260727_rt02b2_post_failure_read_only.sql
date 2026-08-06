USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    N'RT02B2_POST_FAILURE_SERVER' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) AS Edition,
    CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) AS ProductMajorVersion;

SELECT
    N'RT02B2_POST_FAILURE_DATABASE_FEATURES' AS Evidence,
    databaseItem.name AS DatabaseName,
    databaseItem.state_desc AS StateDescription,
    databaseItem.is_read_only AS IsReadOnly,
    databaseItem.source_database_id AS SourceDatabaseId,
    databaseItem.snapshot_isolation_state AS SnapshotIsolationState,
    databaseItem.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn,
    CASE
        WHEN tracking.database_id IS NULL THEN 0
        ELSE 1
    END AS IsChangeTrackingEnabled
FROM sys.databases AS databaseItem
LEFT JOIN sys.change_tracking_databases AS tracking
    ON tracking.database_id = databaseItem.database_id
WHERE databaseItem.name IN
(
    N'QLHV_RT02_OTO_TEST',
    N'QLHV_RT02_MOTO_TEST',
    N'QLHV_RT02_TARGET_TEST'
)
ORDER BY databaseItem.name;

SELECT
    N'RT02B2_POST_FAILURE_LINKED_SERVERS' AS Evidence,
    COUNT_BIG(*) AS LinkedServerCount
FROM sys.servers
WHERE is_linked = 1;

SELECT
    N'RT02B2_POST_FAILURE_PRODUCTION_SESSIONS' AS Evidence,
    COUNT_BIG(*) AS ProductionApplicationSessionCount
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
  AND session_id <> @@SPID
  AND
  (
      program_name LIKE N'%QLHV.Api%'
      OR program_name LIKE N'%QLHV.Worker%'
      OR program_name LIKE N'%Hangfire%'
      OR program_name LIKE N'%AutoSync%'
  );
GO

USE [QLHV_RT02_OTO_TEST];
GO
SELECT
    N'QLHV_RT02_OTO_TEST' AS DatabaseName,
    tableItem.name AS TableName,
    COALESCE(SUM(partitionItem.row_count), 0) AS [RowCount]
FROM sys.tables AS tableItem
LEFT JOIN sys.dm_db_partition_stats AS partitionItem
    ON partitionItem.object_id = tableItem.object_id
   AND partitionItem.index_id IN (0, 1)
WHERE tableItem.is_ms_shipped = 0
GROUP BY tableItem.name
ORDER BY tableItem.name;
SELECT
    N'QLHV_RT02_OTO_TEST' AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTables,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SELECT
    N'QLHV_RT02_MOTO_TEST' AS DatabaseName,
    tableItem.name AS TableName,
    COALESCE(SUM(partitionItem.row_count), 0) AS [RowCount]
FROM sys.tables AS tableItem
LEFT JOIN sys.dm_db_partition_stats AS partitionItem
    ON partitionItem.object_id = tableItem.object_id
   AND partitionItem.index_id IN (0, 1)
WHERE tableItem.is_ms_shipped = 0
GROUP BY tableItem.name
ORDER BY tableItem.name;
SELECT
    N'QLHV_RT02_MOTO_TEST' AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTables,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
SELECT
    N'QLHV_RT02_TARGET_TEST' AS DatabaseName,
    tableItem.name AS TableName,
    COALESCE(SUM(partitionItem.row_count), 0) AS [RowCount]
FROM sys.tables AS tableItem
LEFT JOIN sys.dm_db_partition_stats AS partitionItem
    ON partitionItem.object_id = tableItem.object_id
   AND partitionItem.index_id IN (0, 1)
WHERE tableItem.is_ms_shipped = 0
GROUP BY tableItem.name
ORDER BY tableItem.name;
SELECT
    N'QLHV_RT02_TARGET_TEST' AS DatabaseName,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTables,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount,
    CASE WHEN OBJECT_ID(N'dbo.Rt02ApplyMarker', N'U') IS NULL THEN 0 ELSE 1 END
        AS ApplyMarkerTableExists,
    CASE WHEN OBJECT_ID(N'dbo.Rt02ApplyCheckpoint', N'U') IS NULL THEN 0 ELSE 1 END
        AS ApplyCheckpointTableExists,
    CASE WHEN OBJECT_ID(N'dbo.Rt02ManualReviewEvidence', N'U') IS NULL THEN 0 ELSE 1 END
        AS ManualReviewTableExists;
GO
