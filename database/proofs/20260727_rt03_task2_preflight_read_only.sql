USE [QLHV_APP];
GO

/* RT-03 Task 2 immediate preflight. SELECT-only; fail closed. */
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
   OR DB_ID() <> 12
   OR NOT EXISTS
      (
          SELECT 1 FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
      )
    THROW 527590, 'RT03_PRODUCTION_IDENTITY_REJECTED: Task 2 preflight.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'CSDL_OTO'
      AND databaseItem.database_id = 9
      AND recovery.database_guid = '9A8B9BC1-18F3-4823-8123-3DC197A9D540'
)
OR NOT EXISTS
(
    SELECT 1 FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'CSDL_MOTO'
      AND databaseItem.database_id = 8
      AND recovery.database_guid = '308BDDA8-80F3-4ACB-9836-578D80A9E98E'
)
    THROW 527591, 'RT03_PRODUCTION_IDENTITY_REJECTED: source ID/GUID.', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.App_QlhvAutoSyncRun
    WHERE Status IN (N'QUEUED', N'RUNNING') OR ActiveSlot = 1
)
OR EXISTS
(
    SELECT 1 FROM dbo.App_QlhvSyncOperationHistory
    WHERE Status IN (N'QUEUED', N'RUNNING')
)
    THROW 527592, 'RT03_AUTOSYNC_ACTIVE: active run/slot/operation.', 1;

IF EXISTS
(
    SELECT SourceProfileCode, LTRIM(RTRIM(SourceMaDK)) AS SourceIdentity
    FROM dbo.App_HocVien
    WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
      AND IsDeleted = 0
    GROUP BY SourceProfileCode, LTRIM(RTRIM(SourceMaDK))
    HAVING COUNT_BIG(*) > 1
)
    THROW 527593, 'RT03_DUPLICATE_ACTIVE_TARGET.', 1;

SELECT
    N'RT03_TASK2_PREFLIGHT_PASS' AS Evidence,
    (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun) AS AutoSyncHistoryRows,
    (SELECT MAX(Id) FROM dbo.App_QlhvAutoSyncRun WHERE Status = N'SUCCEEDED')
        AS LatestSuccessfulAutoSyncId,
    (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_OTO' AND IsDeleted = 0) AS OtoActiveRows,
    (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_MOTO' AND IsDeleted = 0) AS MotoActiveRows,
    (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_OTO' AND IsDeleted = 1) AS OtoSoftDeletedRows,
    (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_MOTO' AND IsDeleted = 1) AS MotoSoftDeletedRows,
    SYSUTCDATETIME() AS ReadAtUtc;
GO
