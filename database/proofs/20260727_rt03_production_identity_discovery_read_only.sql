USE [QLHV_APP];
GO

/*
RT-03 production identity discovery. SELECT-only.

This is the first half of the two-pass production identity gate. It emits the
server/database identities and capability metadata needed to render the pinned
RT-03 readiness proof. It performs no DDL, DML, checkpoint publication, sync,
or feature-state change.
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
    THROW 527500, 'RT03_PRODUCTION_IDENTITY_REJECTED: unexpected SQL Server.', 1;

IF DB_NAME() <> N'QLHV_APP'
    THROW 527501, 'RT03_PRODUCTION_IDENTITY_REJECTED: unexpected initial database.', 1;

IF EXISTS
(
    SELECT required.DatabaseName
    FROM
    (
        VALUES
            (N'CSDL_OTO'),
            (N'CSDL_MOTO'),
            (N'CSDL_OTO_BAK'),
            (N'CSDL_MOTO_BAK'),
            (N'QLHV_APP'),
            (N'CSDL_OTO_V1'),
            (N'CSDL_MOTO_V1')
    ) AS required(DatabaseName)
    LEFT JOIN sys.databases AS actual
        ON actual.name = required.DatabaseName
    WHERE actual.database_id IS NULL
       OR actual.state_desc <> N'ONLINE'
)
    THROW 527502, 'RT03_PRODUCTION_IDENTITY_REJECTED: required route missing/offline.', 1;

SELECT
    N'RT03_PRODUCTION_IDENTITY_DISCOVERY' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'MachineName')) AS MachineIdentity,
    databaseItem.name AS DatabaseName,
    databaseItem.database_id AS DatabaseId,
    CONVERT(nvarchar(36), recovery.database_guid) AS DatabaseGuid,
    databaseItem.state_desc AS StateDescription,
    databaseItem.user_access_desc AS UserAccessDescription,
    databaseItem.is_read_only AS IsReadOnly,
    databaseItem.snapshot_isolation_state_desc AS SnapshotIsolationState,
    databaseItem.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn,
    databaseItem.recovery_model_desc AS RecoveryModel,
    databaseItem.collation_name AS CollationName,
    CASE WHEN tracking.database_id IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END
        AS ChangeTrackingEnabled,
    tracking.retention_period AS ChangeTrackingRetentionPeriod,
    tracking.retention_period_units_desc AS ChangeTrackingRetentionUnits,
    tracking.is_auto_cleanup_on AS ChangeTrackingAutoCleanup
FROM sys.databases AS databaseItem
INNER JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
LEFT JOIN sys.change_tracking_databases AS tracking
    ON tracking.database_id = databaseItem.database_id
WHERE databaseItem.name IN
(
    N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
    N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
)
ORDER BY databaseItem.name;

SELECT
    N'RT03_PRODUCTION_ACTIVE_SESSIONS' AS Evidence,
    databaseItem.name AS DatabaseName,
    COUNT_BIG(sessionItem.session_id) AS SessionCount,
    SUM(CASE WHEN requestItem.session_id IS NULL THEN CONVERT(bigint, 0)
             ELSE CONVERT(bigint, 1) END) AS ActiveRequestCount,
    SUM(CASE WHEN requestItem.blocking_session_id > 0 THEN CONVERT(bigint, 1)
             ELSE CONVERT(bigint, 0) END) AS BlockedRequestCount,
    MAX(requestItem.wait_type) AS ObservedWaitType
FROM sys.databases AS databaseItem
LEFT JOIN sys.dm_exec_sessions AS sessionItem
    ON sessionItem.database_id = databaseItem.database_id
LEFT JOIN sys.dm_exec_requests AS requestItem
    ON requestItem.session_id = sessionItem.session_id
WHERE databaseItem.name IN (N'CSDL_OTO', N'CSDL_MOTO', N'QLHV_APP')
GROUP BY databaseItem.name
ORDER BY databaseItem.name;
GO
