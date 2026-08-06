USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    N'PRODUCTION_AUTO_SYNC_DRIFT' AS Evidence,
    runItem.Id,
    CONVERT(nvarchar(36), runItem.RunId) AS RunId,
    runItem.TriggerType,
    runItem.Actor,
    runItem.Status,
    runItem.CurrentStage,
    CONVERT(varchar(33), runItem.CreatedAtUtc, 126) AS CreatedAtUtc,
    CONVERT(varchar(33), runItem.StartedAtUtc, 126) AS StartedAtUtc,
    CONVERT(varchar(33), runItem.CompletedAtUtc, 126) AS CompletedAtUtc,
    CONVERT(varchar(33), runItem.UpdatedAtUtc, 126) AS UpdatedAtUtc,
    runItem.ActiveSlot,
    CONVERT(char(64), HASHBYTES('SHA2_256',
        COALESCE(runItem.OtoResultJson, N'')), 2) AS OtoResultHash,
    CONVERT(char(64), HASHBYTES('SHA2_256',
        COALESCE(runItem.MotoResultJson, N'')), 2) AS MotoResultHash,
    runItem.ErrorMessage
FROM [QLHV_APP].dbo.App_QlhvAutoSyncRun AS runItem
WHERE runItem.Id > 6
ORDER BY runItem.Id;

SELECT
    N'PRODUCTION_AUTO_SYNC_DRIFT_COUNTS' AS Evidence,
    COUNT_BIG(*) AS RunCount,
    SUM(CASE
        WHEN ActiveSlot IS NULL THEN CONVERT(bigint, 0)
        ELSE CONVERT(bigint, 1)
    END) AS ActiveRunCount,
    SUM(CASE
        WHEN Status = N'SUCCEEDED' THEN CONVERT(bigint, 1)
        ELSE CONVERT(bigint, 0)
    END) AS SucceededRunCount
FROM [QLHV_APP].dbo.App_QlhvAutoSyncRun;

SELECT
    N'PRODUCTION_AUTO_SYNC_DRIFT_BUSINESS_COUNTS' AS Evidence,
    (SELECT COUNT_BIG(*) FROM [CSDL_OTO].dbo.NguoiLX) AS OtoLive,
    (SELECT COUNT_BIG(*) FROM [CSDL_OTO_BAK].dbo.NguoiLX) AS OtoBak,
    (SELECT COUNT_BIG(*) FROM [CSDL_MOTO].dbo.NguoiLX) AS MotoLive,
    (SELECT COUNT_BIG(*) FROM [CSDL_MOTO_BAK].dbo.NguoiLX) AS MotoBak,
    (SELECT COUNT_BIG(*) FROM [QLHV_APP].dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_OTO' AND IsDeleted = 0)
        AS OtoActive,
    (SELECT COUNT_BIG(*) FROM [QLHV_APP].dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_OTO' AND IsDeleted = 1)
        AS OtoSoftDeleted,
    (SELECT COUNT_BIG(*) FROM [QLHV_APP].dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_MOTO' AND IsDeleted = 0)
        AS MotoActive,
    (SELECT COUNT_BIG(*) FROM [QLHV_APP].dbo.App_HocVien
        WHERE SourceProfileCode = N'CSDT_MOTO' AND IsDeleted = 1)
        AS MotoSoftDeleted;
GO
