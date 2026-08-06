USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    N'SERVER_IDENTITY' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) AS Edition,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')) AS ProductVersion,
    CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) AS ProductMajorVersion;

SELECT
    N'DATABASE_IDENTITY' AS Evidence,
    databaseItem.name AS DatabaseName,
    databaseItem.database_id AS DatabaseId,
    databaseItem.state_desc AS StateDescription,
    databaseItem.is_read_only AS IsReadOnly,
    databaseItem.recovery_model_desc AS RecoveryModel,
    databaseItem.source_database_id AS SourceDatabaseId,
    CONVERT(nvarchar(36), recovery.database_guid) AS DatabaseGuid,
    databaseItem.snapshot_isolation_state AS SnapshotIsolationState,
    databaseItem.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn,
    CASE
        WHEN tracking.database_id IS NULL THEN 0
        ELSE 1
    END AS IsChangeTrackingEnabled
FROM sys.databases AS databaseItem
LEFT JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
LEFT JOIN sys.change_tracking_databases AS tracking
    ON tracking.database_id = databaseItem.database_id
WHERE databaseItem.name IN
(
    N'QLHV_RT02_OTO_TEST',
    N'QLHV_RT02_MOTO_TEST',
    N'QLHV_RT02_TARGET_TEST'
)
ORDER BY databaseItem.name;

WITH ProductionDenylist(DatabaseName, DatabaseId, DatabaseGuid) AS
(
    SELECT *
    FROM
    (
        VALUES
            (N'CSDL_MOTO', 8, CONVERT(uniqueidentifier, N'308BDDA8-80F3-4ACB-9836-578D80A9E98E')),
            (N'CSDL_OTO', 9, CONVERT(uniqueidentifier, N'9A8B9BC1-18F3-4823-8123-3DC197A9D540')),
            (N'CSDL_MOTO_BAK', 10, CONVERT(uniqueidentifier, N'2F326753-B1A8-4E94-A404-49FA71E8260A')),
            (N'CSDL_OTO_BAK', 11, CONVERT(uniqueidentifier, N'22ABFD1B-5F1B-4DCA-9EFB-DFB0676623CE')),
            (N'QLHV_APP', 12, CONVERT(uniqueidentifier, N'9C44B304-8A84-4D0D-9A82-19C7233FF6BB')),
            (N'CSDL_MOTO_V1', 13, CONVERT(uniqueidentifier, N'454A56FA-EAA5-4EA7-B41E-9AD500C2D69F')),
            (N'CSDL_OTO_V1', 14, CONVERT(uniqueidentifier, N'41463BB9-532D-4BDB-B821-595D7A30BB2C')),
            (N'CSDL_MOTO_V1_BAK', 15, CONVERT(uniqueidentifier, N'EFC5F9A8-DF27-41BF-B6CF-B05B09C9E211')),
            (N'CSDL_OTO_V1_BAK', 16, CONVERT(uniqueidentifier, N'E1CCB750-B4EB-4D85-8D46-9C043A342C54'))
    ) AS valuesTable(DatabaseName, DatabaseId, DatabaseGuid)
),
IsolatedIdentities AS
(
    SELECT
        databaseItem.name AS DatabaseName,
        databaseItem.database_id AS DatabaseId,
        recovery.database_guid AS DatabaseGuid
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = databaseItem.database_id
    WHERE databaseItem.name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
)
SELECT
    N'PRODUCTION_DENYLIST_CONFLICTS' AS Evidence,
    COUNT_BIG(*) AS ConflictCount
FROM IsolatedIdentities AS isolated
INNER JOIN ProductionDenylist AS production
    ON production.DatabaseId = isolated.DatabaseId
    OR production.DatabaseGuid = isolated.DatabaseGuid
    OR production.DatabaseName = isolated.DatabaseName;

SELECT
    N'ISOLATED_DISTINCT_IDENTITIES' AS Evidence,
    COUNT_BIG(*) AS DatabaseCount,
    COUNT_BIG(DISTINCT databaseItem.database_id) AS DistinctDatabaseIds,
    COUNT_BIG(DISTINCT recovery.database_guid) AS DistinctDatabaseGuids
FROM sys.databases AS databaseItem
INNER JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
WHERE databaseItem.name IN
(
    N'QLHV_RT02_OTO_TEST',
    N'QLHV_RT02_MOTO_TEST',
    N'QLHV_RT02_TARGET_TEST'
);

SELECT
    N'LINKED_SERVER_COUNT' AS Evidence,
    COUNT_BIG(*) AS LinkedServerCount
FROM sys.servers
WHERE is_linked = 1;

SELECT
    N'DATABASE_FILES_OUTSIDE_APPROVED_ROOT' AS Evidence,
    COUNT_BIG(*) AS FileCount
FROM sys.master_files
WHERE physical_name NOT LIKE N'D:\QLHV_RT02_SQLDATA\%';

SELECT
    N'PRODUCTION_APPLICATION_SESSION_COUNT' AS Evidence,
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
    name AS MarkerName,
    CONVERT(nvarchar(256), value) AS MarkerValue
FROM sys.extended_properties
WHERE class = 0
ORDER BY name;
SELECT N'QLHV_RT02_OTO_TEST' AS DatabaseName,
       (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) AS UserTableCount,
       (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SELECT
    N'QLHV_RT02_MOTO_TEST' AS DatabaseName,
    name AS MarkerName,
    CONVERT(nvarchar(256), value) AS MarkerValue
FROM sys.extended_properties
WHERE class = 0
ORDER BY name;
SELECT N'QLHV_RT02_MOTO_TEST' AS DatabaseName,
       (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) AS UserTableCount,
       (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
SELECT
    N'QLHV_RT02_TARGET_TEST' AS DatabaseName,
    name AS MarkerName,
    CONVERT(nvarchar(256), value) AS MarkerValue
FROM sys.extended_properties
WHERE class = 0
ORDER BY name;
SELECT N'QLHV_RT02_TARGET_TEST' AS DatabaseName,
       (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) AS UserTableCount,
       (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO
