SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    N'CLOCK' AS SectionName,
    CONVERT(varchar(33), SYSUTCDATETIME(), 126) AS UtcNow,
    CONVERT(varchar(33), SYSDATETIME(), 126) AS LocalNow;

SELECT
    N'SESSION_REQUEST' AS SectionName,
    sessionItem.session_id,
    sessionItem.login_name,
    sessionItem.host_name,
    sessionItem.program_name,
    sessionItem.status AS SessionStatus,
    requestItem.status AS RequestStatus,
    requestItem.command,
    requestItem.wait_type,
    requestItem.wait_time,
    requestItem.blocking_session_id,
    DB_NAME(requestItem.database_id) AS DatabaseName,
    sessionItem.open_transaction_count,
    LEFT(sqlText.text, 1000) AS SqlText
FROM sys.dm_exec_sessions AS sessionItem
LEFT JOIN sys.dm_exec_requests AS requestItem
    ON requestItem.session_id = sessionItem.session_id
OUTER APPLY sys.dm_exec_sql_text(requestItem.sql_handle) AS sqlText
WHERE sessionItem.is_user_process = 1
ORDER BY sessionItem.session_id;

SELECT
    N'LOCK' AS SectionName,
    lockItem.request_session_id,
    DB_NAME(lockItem.resource_database_id) AS DatabaseName,
    lockItem.resource_type,
    lockItem.request_mode,
    lockItem.request_status,
    lockItem.resource_associated_entity_id
FROM sys.dm_tran_locks AS lockItem
WHERE lockItem.request_session_id <> @@SPID
ORDER BY
    lockItem.request_session_id,
    lockItem.resource_database_id,
    lockItem.resource_type,
    lockItem.request_mode;

SELECT
    N'ACTIVE_TRANSACTION' AS SectionName,
    sessionTransaction.session_id,
    activeTransaction.transaction_id,
    activeTransaction.name,
    activeTransaction.transaction_begin_time,
    activeTransaction.transaction_state,
    activeTransaction.transaction_type
FROM sys.dm_tran_session_transactions AS sessionTransaction
INNER JOIN sys.dm_tran_active_transactions AS activeTransaction
    ON activeTransaction.transaction_id = sessionTransaction.transaction_id
ORDER BY sessionTransaction.session_id, activeTransaction.transaction_id;

SELECT
    N'FEATURE_STATE' AS SectionName,
    databaseItem.name,
    databaseItem.is_read_committed_snapshot_on,
    databaseItem.snapshot_isolation_state,
    CASE
        WHEN trackingDatabase.database_id IS NULL THEN 0
        ELSE 1
    END AS ChangeTrackingDatabaseEnabled,
    CASE databaseItem.name
        WHEN N'QLHV_RT02_OTO_TEST' THEN
            (SELECT COUNT_BIG(*)
             FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables)
        WHEN N'QLHV_RT02_MOTO_TEST' THEN
            (SELECT COUNT_BIG(*)
             FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables)
        WHEN N'QLHV_RT02_TARGET_TEST' THEN
            (SELECT COUNT_BIG(*)
             FROM [QLHV_RT02_TARGET_TEST].sys.change_tracking_tables)
    END AS TrackedTableCount
FROM sys.databases AS databaseItem
LEFT JOIN sys.change_tracking_databases AS trackingDatabase
    ON trackingDatabase.database_id = databaseItem.database_id
WHERE databaseItem.name IN
(
    N'QLHV_RT02_OTO_TEST',
    N'QLHV_RT02_MOTO_TEST',
    N'QLHV_RT02_TARGET_TEST'
)
ORDER BY databaseItem.database_id;

SELECT
    N'TARGET_STATE' AS SectionName,
    stateItem.EnvironmentId,
    CONVERT(varchar(33), stateItem.CreatedAtUtc, 126) AS CreatedAtUtc,
    stateItem.DatasetFingerprint,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyMarker)
        AS MarkerCount,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyCheckpoint)
        AS CheckpointCount,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ManualReviewEvidence)
        AS ManualReviewCount
FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState AS stateItem
WHERE stateItem.EnvironmentId =
    'RT02B0-CSDLTTTC-QLHVRT02-20260727-01';
