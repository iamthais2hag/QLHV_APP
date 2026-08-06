USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    N'PRODUCTION_SERVER' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) AS Edition,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')) AS ProductVersion;

SELECT
    N'PRODUCTION_SOURCE_COUNTS' AS Evidence,
    (SELECT COUNT_BIG(*) FROM [CSDL_OTO].dbo.NguoiLX) AS OtoLive,
    (SELECT COUNT_BIG(*) FROM [CSDL_OTO_BAK].dbo.NguoiLX) AS OtoBak,
    (SELECT COUNT_BIG(*) FROM [CSDL_MOTO].dbo.NguoiLX) AS MotoLive,
    (SELECT COUNT_BIG(*) FROM [CSDL_MOTO_BAK].dbo.NguoiLX) AS MotoBak;

SELECT
    N'PRODUCTION_QLHV_PROFILE_COUNTS' AS Evidence,
    SourceProfileCode,
    SUM(CASE WHEN IsDeleted = 0 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS ActiveRows,
    SUM(CASE WHEN IsDeleted = 1 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS SoftDeletedRows,
    COUNT_BIG(*) AS TotalRows
FROM [QLHV_APP].dbo.App_HocVien
WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
GROUP BY SourceProfileCode
ORDER BY SourceProfileCode;

WITH DuplicateGroups AS
(
    SELECT SourceProfileCode, SourceMaDK
    FROM [QLHV_APP].dbo.App_HocVien
    WHERE IsDeleted = 0
      AND SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
    GROUP BY SourceProfileCode, SourceMaDK
    HAVING COUNT_BIG(*) > 1
)
SELECT
    N'PRODUCTION_DUPLICATE_ACTIVE_GROUPS' AS Evidence,
    SourceProfileCode,
    COUNT(SourceMaDK) AS DuplicateActiveGroups
FROM
(
    SELECT profile.SourceProfileCode, duplicate.SourceMaDK
    FROM (VALUES (N'CSDT_OTO'), (N'CSDT_MOTO')) profile(SourceProfileCode)
    LEFT JOIN DuplicateGroups duplicate
        ON duplicate.SourceProfileCode = profile.SourceProfileCode
) AS grouped
GROUP BY SourceProfileCode
ORDER BY SourceProfileCode;

SELECT
    N'EXISTING_AUTO_SYNC_STATE' AS Evidence,
    COUNT_BIG(*) AS RunCount,
    SUM(CASE WHEN ActiveSlot IS NOT NULL THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS ActiveRunCount,
    CONVERT(nvarchar(36),
        (
            SELECT TOP (1) RunId
            FROM [QLHV_APP].dbo.App_QlhvAutoSyncRun
            ORDER BY CreatedAtUtc DESC, Id DESC
        )) AS LatestRunId,
    (
        SELECT TOP (1) Status
        FROM [QLHV_APP].dbo.App_QlhvAutoSyncRun
        ORDER BY CreatedAtUtc DESC, Id DESC
    ) AS LatestStatus,
    (
        SELECT TOP (1) CompletedAtUtc
        FROM [QLHV_APP].dbo.App_QlhvAutoSyncRun
        ORDER BY CreatedAtUtc DESC, Id DESC
    ) AS LatestCompletedAtUtc
FROM [QLHV_APP].dbo.App_QlhvAutoSyncRun;

SELECT
    N'PRODUCTION_TEST_NAME_ABSENCE' AS Evidence,
    COUNT_BIG(*) AS TestDatabaseNameCount
FROM sys.databases
WHERE name IN
(
    N'QLHV_RT02_OTO_TEST',
    N'QLHV_RT02_MOTO_TEST',
    N'QLHV_RT02_TARGET_TEST'
);
GO
