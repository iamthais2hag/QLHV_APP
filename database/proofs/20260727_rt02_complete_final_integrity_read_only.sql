USE [master];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

DECLARE @ExpectedServer nvarchar(128) = N'CSDLTTTC\QLHVRT02';
DECLARE @ExpectedEnvironmentId varchar(128) =
    'RT02B0-CSDLTTTC-QLHVRT02-20260727-01';
DECLARE @ExpectedApprovalId nvarchar(128) =
    N'RT02B-OPERATOR-APPROVAL-20260727-01';
DECLARE @ExpectedExpiresAtUtc nvarchar(128) =
    N'2026-07-31T16:59:59Z';

IF TRY_CONVERT(datetime2(0), @ExpectedExpiresAtUtc, 127) <= SYSUTCDATETIME()
    THROW 528107, 'ISOLATED_DATABASE_IDENTITY_REJECTED: approval expired.', 1;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> @ExpectedServer
    THROW 528100, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server identity.', 1;

IF CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE
      N'Developer Edition%'
    THROW 528101, 'ISOLATED_DATABASE_IDENTITY_REJECTED: engine identity.', 1;

IF EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
    THROW 528102, 'ISOLATED_DATABASE_IDENTITY_REJECTED: linked route.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.dm_exec_sessions
    WHERE is_user_process = 1
      AND session_id <> @@SPID
      AND
      (
          program_name LIKE N'%QLHV.Api%'
          OR program_name LIKE N'%QLHV.Worker%'
          OR program_name LIKE N'%Hangfire%'
          OR program_name LIKE N'%AutoSync%'
      )
)
    THROW 528108, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production application session.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'CSDL_OTO',
        N'CSDL_MOTO',
        N'CSDL_OTO_BAK',
        N'CSDL_MOTO_BAK',
        N'QLHV_APP',
        N'CSDL_OTO_V1',
        N'CSDL_MOTO_V1'
    )
)
    THROW 528103, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production route.', 1;

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
      AND databaseItem.source_database_id IS NULL
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
         AND databaseItem.state_desc = N'ONLINE'
         AND databaseItem.is_read_only = 0
         AND databaseItem.source_database_id IS NULL
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
         AND databaseItem.state_desc = N'ONLINE'
         AND databaseItem.is_read_only = 0
         AND databaseItem.source_database_id IS NULL
   )
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.databases
       WHERE name IN
       (
           N'QLHV_RT02_OTO_TEST',
           N'QLHV_RT02_MOTO_TEST',
           N'QLHV_RT02_TARGET_TEST'
       )
   ) <> 3
    THROW 528104, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database identity.', 1;

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
      AND is_read_committed_snapshot_on <> 0
)
    THROW 528105, 'RT02 RCSI state is invalid.', 1;

DECLARE @CtDatabaseCount bigint =
(
    SELECT COUNT_BIG(*)
    FROM sys.change_tracking_databases
);
DECLARE @OtoCtTableCount bigint =
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables
);
DECLARE @MotoCtTableCount bigint =
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables
);
DECLARE @TargetCtTableCount bigint =
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].sys.change_tracking_tables
);
DECLARE @OtoSnapshotState tinyint =
(
    SELECT snapshot_isolation_state
    FROM sys.databases
    WHERE name = N'QLHV_RT02_OTO_TEST'
);
DECLARE @MotoSnapshotState tinyint =
(
    SELECT snapshot_isolation_state
    FROM sys.databases
    WHERE name = N'QLHV_RT02_MOTO_TEST'
);
DECLARE @TargetSnapshotState tinyint =
(
    SELECT snapshot_isolation_state
    FROM sys.databases
    WHERE name = N'QLHV_RT02_TARGET_TEST'
);
DECLARE @FeatureState varchar(3);

IF @CtDatabaseCount = 2
   AND @OtoCtTableCount = 2
   AND @MotoCtTableCount = 2
   AND @TargetCtTableCount = 0
   AND @OtoSnapshotState = 1
   AND @MotoSnapshotState = 1
   AND @TargetSnapshotState = 0
   AND
   (
       SELECT COUNT_BIG(*)
       FROM sys.change_tracking_databases
       WHERE database_id IN (5, 6)
         AND retention_period = 2
         AND retention_period_units_desc = N'DAYS'
         AND is_auto_cleanup_on = 1
   ) = 2
   AND NOT EXISTS
   (
       SELECT 1
       FROM sys.change_tracking_databases
       WHERE database_id NOT IN (5, 6)
   )
   AND NOT EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables
       WHERE object_id NOT IN
       (
           OBJECT_ID(N'QLHV_RT02_OTO_TEST.dbo.NguoiLX'),
           OBJECT_ID(N'QLHV_RT02_OTO_TEST.dbo.NguoiLX_HoSo')
       )
          OR is_track_columns_updated_on <> 1
   )
   AND NOT EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables
       WHERE object_id NOT IN
       (
           OBJECT_ID(N'QLHV_RT02_MOTO_TEST.dbo.NguoiLX'),
           OBJECT_ID(N'QLHV_RT02_MOTO_TEST.dbo.NguoiLX_HoSo')
       )
          OR is_track_columns_updated_on <> 1
   )
BEGIN
    SET @FeatureState = 'ON';
END;
ELSE IF @CtDatabaseCount = 0
        AND @OtoCtTableCount = 0
        AND @MotoCtTableCount = 0
        AND @TargetCtTableCount = 0
        AND @OtoSnapshotState = 0
        AND @MotoSnapshotState = 0
        AND @TargetSnapshotState = 0
BEGIN
    SET @FeatureState = 'OFF';
END;
ELSE
BEGIN
    THROW 528106, 'RT02 CT/Snapshot state is mixed or invalid.', 1;
END;

SELECT
    N'RT02_FINAL_SERVER_AND_FEATURE_STATE' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    @ExpectedEnvironmentId AS EnvironmentId,
    @ExpectedApprovalId AS ApprovalId,
    @FeatureState AS FeatureState,
    @CtDatabaseCount AS ChangeTrackingDatabaseCount,
    @OtoCtTableCount AS OtoChangeTrackedTableCount,
    @MotoCtTableCount AS MotoChangeTrackedTableCount,
    @TargetCtTableCount AS TargetChangeTrackedTableCount,
    @OtoSnapshotState AS OtoSnapshotState,
    @MotoSnapshotState AS MotoSnapshotState,
    @TargetSnapshotState AS TargetSnapshotState,
    CONVERT(int, 0) AS RcsiEnabledDatabaseCount,
    (
        SELECT COUNT_BIG(*)
        FROM sys.dm_exec_sessions
        WHERE is_user_process = 1
          AND session_id <> @@SPID
          AND
          (
              program_name LIKE N'%QLHV.Api%'
              OR program_name LIKE N'%QLHV.Worker%'
              OR program_name LIKE N'%Hangfire%'
              OR program_name LIKE N'%AutoSync%'
          )
    ) AS ProductionApplicationSessionCount;
GO

USE [QLHV_RT02_OTO_TEST];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF DB_NAME() <> N'QLHV_RT02_OTO_TEST'
   OR DB_ID() <> 5
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
      N'CSDLTTTC\QLHVRT02'
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> 'FEE7CD94-A717-4E73-89F0-0FBFF71D1789'
    THROW 528110, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO route.', 1;

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
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_DATASET_MODE'
         AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
         AND CONVERT(nvarchar(128), value) = N'FALSE'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_EXPIRES_AT_UTC'
         AND CONVERT(nvarchar(128), value) =
             N'2026-07-31T16:59:59Z'
   )
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.extended_properties
       WHERE class = 0
         AND name LIKE N'RT02[_]%'
   ) <> 5
    THROW 528111, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO markers.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
) <> 2
   OR EXISTS
   (
       SELECT 1
       FROM sys.tables
       WHERE is_ms_shipped = 0
         AND
         (
             SCHEMA_NAME(schema_id) <> N'dbo'
             OR name NOT IN (N'NguoiLX', N'NguoiLX_HoSo')
         )
   )
   OR EXISTS (SELECT 1 FROM sys.synonyms)
   OR EXISTS (SELECT 1 FROM sys.external_data_sources)
    THROW 528112, 'RT02 OTO object allowlist is invalid.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.NguoiLX) <> 1370
   OR (SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo) <> 1370
   OR EXISTS
   (
       SELECT IdentityHmac FROM dbo.NguoiLX
       EXCEPT
       SELECT IdentityHmac FROM dbo.NguoiLX_HoSo
   )
   OR EXISTS
   (
       SELECT IdentityHmac FROM dbo.NguoiLX_HoSo
       EXCEPT
       SELECT IdentityHmac FROM dbo.NguoiLX
   )
    THROW 528113, 'RT02 OTO final row count is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM dbo.NguoiLX
    WHERE ScenarioCode = 'CORE'
      AND DatasetRole = 'NO_CHANGE'
) <> 150
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.NguoiLX
       WHERE ScenarioCode = 'CORE'
         AND DatasetRole = 'SOURCE_ONLY_NEW_ROW'
   ) <> 1
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.NguoiLX
       WHERE ScenarioCode = 'CORE'
         AND DatasetRole = 'STALE_IMPORTED_VALUE'
   ) <> 1
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.NguoiLX
       WHERE ScenarioCode <> 'CORE'
   ) <> 1218
    THROW 528114, 'RT02 OTO core/noncore counts are invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.NguoiLX AS sourceItem
    INNER JOIN dbo.NguoiLX_HoSo AS dossier
        ON dossier.IdentityHmac = sourceItem.IdentityHmac
    WHERE sourceItem.IsActive <> 1
       OR sourceItem.HoTen NOT LIKE N'SYNTHETIC %'
       OR sourceItem.IdentityHmac COLLATE Latin1_General_100_BIN2
          LIKE '%[^0-9A-F]%'
       OR LEN(sourceItem.IdentityHmac) <> 64
       OR sourceItem.SourceRowHash <>
          CASE
              WHEN sourceItem.ScenarioCode = 'SOURCECHG-I'
                  THEN CONVERT
                  (
                      char(64),
                      HASHBYTES
                      (
                          'SHA2_256',
                          CONVERT(varchar(max), 'RT02B2-SOURCE-CHANGED')
                      ),
                      2
                  )
              ELSE CONVERT
              (
                  char(64),
                  HASHBYTES
                  (
                      'SHA2_256',
                      CONVERT
                      (
                          varchar(max),
                          CONCAT
                          (
                              'RT02B2|SOURCE|',
                              sourceItem.IdentityHmac,
                              '|',
                              sourceItem.HoTen
                          )
                      )
                  ),
                  2
              )
          END
       OR dossier.PayloadHash <> CONVERT
          (
              char(64),
              HASHBYTES
              (
                  'SHA2_256',
                  CONVERT
                  (
                      varchar(max),
                      CONCAT('RT02B2|HOSO|', sourceItem.IdentityHmac)
                  )
              ),
              2
          )
)
    THROW 528115, 'RT02 OTO source/hash invariant is invalid.', 1;

DECLARE @OtoCoreCanonical varchar(max);
SELECT @OtoCoreCanonical = STRING_AGG
(
    CONVERT
    (
        varchar(max),
        CONCAT
        (
            sourceItem.IdentityHmac,
            '|',
            sourceItem.ScenarioCode,
            '|',
            sourceItem.DatasetRole,
            '|',
            sourceItem.HoTen,
            '|',
            sourceItem.SourceRowHash,
            '|',
            dossier.PayloadHash,
            '|',
            CONVERT(varchar(1), sourceItem.IsActive)
        )
    ),
    ';'
) WITHIN GROUP
(
    ORDER BY sourceItem.IdentityHmac COLLATE Latin1_General_100_BIN2
)
FROM dbo.NguoiLX AS sourceItem
INNER JOIN dbo.NguoiLX_HoSo AS dossier
    ON dossier.IdentityHmac = sourceItem.IdentityHmac
WHERE sourceItem.ScenarioCode = 'CORE';

DECLARE @OtoCoreDigest char(64) = CONVERT
(
    char(64),
    HASHBYTES('SHA2_256', @OtoCoreCanonical),
    2
);

IF @OtoCoreDigest <>
   '9B8C2575278A09C46EA714FEAF0AB407EB841D1A5C1C678F3BFAF9B034BCF391'
    THROW 528116, 'RT02 OTO deterministic core digest is invalid.', 1;

SELECT
    N'RT02_FINAL_OTO_SOURCE' AS Evidence,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX) AS SourceRows,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo) AS DossierRows,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX WHERE ScenarioCode = 'CORE')
        AS CoreRows,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX WHERE ScenarioCode <> 'CORE')
        AS NonCoreRows,
    @OtoCoreDigest AS CoreDigest,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF DB_NAME() <> N'QLHV_RT02_MOTO_TEST'
   OR DB_ID() <> 6
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
      N'CSDLTTTC\QLHVRT02'
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> '6D8101F9-07AB-4F0F-B378-29ED084F7B2A'
    THROW 528120, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO route.', 1;

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
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_DATASET_MODE'
         AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
         AND CONVERT(nvarchar(128), value) = N'FALSE'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_EXPIRES_AT_UTC'
         AND CONVERT(nvarchar(128), value) =
             N'2026-07-31T16:59:59Z'
   )
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.extended_properties
       WHERE class = 0
         AND name LIKE N'RT02[_]%'
   ) <> 5
    THROW 528121, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO markers.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
) <> 2
   OR EXISTS
   (
       SELECT 1
       FROM sys.tables
       WHERE is_ms_shipped = 0
         AND
         (
             SCHEMA_NAME(schema_id) <> N'dbo'
             OR name NOT IN (N'NguoiLX', N'NguoiLX_HoSo')
         )
   )
   OR EXISTS (SELECT 1 FROM sys.synonyms)
   OR EXISTS (SELECT 1 FROM sys.external_data_sources)
    THROW 528122, 'RT02 MOTO object allowlist is invalid.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.NguoiLX) <> 5
   OR (SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo) <> 5
   OR EXISTS
   (
       SELECT IdentityHmac FROM dbo.NguoiLX
       EXCEPT
       SELECT IdentityHmac FROM dbo.NguoiLX_HoSo
   )
   OR EXISTS
   (
       SELECT IdentityHmac FROM dbo.NguoiLX_HoSo
       EXCEPT
       SELECT IdentityHmac FROM dbo.NguoiLX
   )
    THROW 528123, 'RT02 MOTO final row count is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.NguoiLX AS sourceItem
    INNER JOIN dbo.NguoiLX_HoSo AS dossier
        ON dossier.IdentityHmac = sourceItem.IdentityHmac
    WHERE sourceItem.ScenarioCode <> 'CORE'
       OR sourceItem.DatasetRole <> 'NO_CHANGE'
       OR sourceItem.IsActive <> 1
       OR sourceItem.HoTen NOT LIKE N'SYNTHETIC MOTO NOCHANGE %'
       OR sourceItem.IdentityHmac COLLATE Latin1_General_100_BIN2
          LIKE '%[^0-9A-F]%'
       OR LEN(sourceItem.IdentityHmac) <> 64
       OR sourceItem.SourceRowHash <> CONVERT
          (
              char(64),
              HASHBYTES
              (
                  'SHA2_256',
                  CONVERT
                  (
                      varchar(max),
                      CONCAT
                      (
                          'RT02B2|SOURCE|',
                          sourceItem.IdentityHmac,
                          '|',
                          sourceItem.HoTen
                      )
                  )
              ),
              2
          )
       OR dossier.PayloadHash <> CONVERT
          (
              char(64),
              HASHBYTES
              (
                  'SHA2_256',
                  CONVERT
                  (
                      varchar(max),
                      CONCAT('RT02B2|HOSO|', sourceItem.IdentityHmac)
                  )
              ),
              2
          )
)
    THROW 528124, 'RT02 MOTO source/hash invariant is invalid.', 1;

DECLARE @MotoCoreCanonical varchar(max);
SELECT @MotoCoreCanonical = STRING_AGG
(
    CONVERT
    (
        varchar(max),
        CONCAT
        (
            sourceItem.IdentityHmac,
            '|',
            sourceItem.ScenarioCode,
            '|',
            sourceItem.DatasetRole,
            '|',
            sourceItem.HoTen,
            '|',
            sourceItem.SourceRowHash,
            '|',
            dossier.PayloadHash,
            '|',
            CONVERT(varchar(1), sourceItem.IsActive)
        )
    ),
    ';'
) WITHIN GROUP
(
    ORDER BY sourceItem.IdentityHmac COLLATE Latin1_General_100_BIN2
)
FROM dbo.NguoiLX AS sourceItem
INNER JOIN dbo.NguoiLX_HoSo AS dossier
    ON dossier.IdentityHmac = sourceItem.IdentityHmac;

DECLARE @MotoCoreDigest char(64) = CONVERT
(
    char(64),
    HASHBYTES('SHA2_256', @MotoCoreCanonical),
    2
);

IF @MotoCoreDigest <>
   '219B066948B707D914E346D31409782F2D4E624DA78AB19C1A50384AF63393F3'
    THROW 528125, 'RT02 MOTO deterministic core digest is invalid.', 1;

SELECT
    N'RT02_FINAL_MOTO_SOURCE' AS Evidence,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX) AS SourceRows,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo) AS DossierRows,
    @MotoCoreDigest AS CoreDigest,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF DB_NAME() <> N'QLHV_RT02_TARGET_TEST'
   OR DB_ID() <> 7
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
      N'CSDLTTTC\QLHVRT02'
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> 'F7BAC56F-8329-47AB-A17C-A0D592ADD484'
    THROW 528130, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target route.', 1;

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
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_DATASET_MODE'
         AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
         AND CONVERT(nvarchar(128), value) = N'FALSE'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM sys.extended_properties
       WHERE class = 0
         AND name = N'RT02_EXPIRES_AT_UTC'
         AND CONVERT(nvarchar(128), value) =
             N'2026-07-31T16:59:59Z'
   )
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.extended_properties
       WHERE class = 0
         AND name LIKE N'RT02[_]%'
   ) <> 5
    THROW 528131, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target markers.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
) <> 5
   OR EXISTS
   (
       SELECT 1
       FROM sys.tables
       WHERE is_ms_shipped = 0
         AND
         (
             SCHEMA_NAME(schema_id) <> N'dbo'
             OR name NOT IN
             (
                 N'Rt02Learner',
                 N'Rt02ManualReviewEvidence',
                 N'Rt02ApplyMarker',
                 N'Rt02ApplyCheckpoint',
                 N'Rt02EnvironmentState'
             )
         )
   )
   OR EXISTS (SELECT 1 FROM sys.synonyms)
   OR EXISTS (SELECT 1 FROM sys.external_data_sources)
    THROW 528132, 'RT02 target object allowlist is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM dbo.Rt02EnvironmentState
    WHERE EnvironmentId = 'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
      AND MappingFingerprint =
          '9938DDC131D2C3DE91C1F35A7CBCAA5B69FE40577514CC4B38A43B1A667A0743'
      AND SourceSchemaFingerprint =
          'DD02431B83E36736108A3083E1268D49EC9D7C9A3030CF9990C39B83CE7E5A1C'
      AND TargetSchemaFingerprint =
          '3BDCF5C0C7CC5F0F17DA69709E03FB91C10E6CD8D1772533CF061752AEFE7634'
      AND IdentityNormalizationVersion =
          'HMACSHA256-UTF8-RT02B2-ISOLATED-HARNESS-V1'
      AND DatasetMode = 'SYNTHETIC'
      AND PiiRows = 0
      AND DatasetFingerprint =
          '864FDBC375868C1C6EC672794980C411C2269236829FDB86ECBBC129A77FFA8C'
) <> 1
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02EnvironmentState) <> 1
    THROW 528133, 'RT02 environment state is invalid.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner) <> 1372
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE Active = 1 AND SoftDeleted = 0
   ) <> 1369
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE Active = 0 AND SoftDeleted = 1
   ) <> 3
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE ScenarioCode = 'CORE'
   ) <> 160
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE ScenarioCode <> 'CORE'
   ) <> 1212
    THROW 528134, 'RT02 target final row count is invalid.', 1;

IF EXISTS
(
    SELECT SourceProfile, IdentityHmac
    FROM dbo.Rt02Learner
    WHERE Active = 1 AND SoftDeleted = 0
    GROUP BY SourceProfile, IdentityHmac
    HAVING COUNT_BIG(*) > 1
)
   OR EXISTS
   (
       SELECT 1
       FROM dbo.Rt02Learner
       WHERE ScenarioCode <> 'CORE'
         AND (Active <> 1 OR SoftDeleted <> 0)
   )
   OR EXISTS
   (
       SELECT 1
       FROM dbo.Rt02Learner
       WHERE HoTen NOT LIKE N'SYNTHETIC %'
          OR IdentityHmac COLLATE Latin1_General_100_BIN2
             LIKE '%[^0-9A-F]%'
          OR LEN(IdentityHmac) <> 64
          OR PhotoState <> 'PHOTO_DISABLED'
          OR
          (
              SoftDeleted = 0
              AND WorkflowState <> 'READY'
          )
          OR
          (
              SoftDeleted = 1
              AND WorkflowState <> 'CLOSED'
          )
   )
    THROW 528135, 'RT02 target active/privacy invariant is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            ('CSDT_OTO', CONVERT(bigint, 156)),
            ('CSDT_MOTO', CONVERT(bigint, 5)),
            ('RT02_ROLLBACK', CONVERT(bigint, 1)),
            ('RT02_VERIFYFAIL', CONVERT(bigint, 1)),
            ('RT02_CONCURRENT', CONVERT(bigint, 1)),
            ('RT02_TARGETCHG', CONVERT(bigint, 1)),
            ('RT02_KILL_POST', CONVERT(bigint, 1)),
            ('RT02_REPLAY', CONVERT(bigint, 1)),
            ('RT02_TIMEOUT', CONVERT(bigint, 1)),
            ('RT02_DEADLOCK', CONVERT(bigint, 1)),
            ('RT02_DRIFT_MAP', CONVERT(bigint, 1)),
            ('RT02_DRIFT_SRC', CONVERT(bigint, 1)),
            ('RT02_DRIFT_TGT', CONVERT(bigint, 1)),
            ('RT02_LOAD_I100', CONVERT(bigint, 100)),
            ('RT02_LOAD_U100', CONVERT(bigint, 100)),
            ('RT02_LOAD_M1000', CONVERT(bigint, 1000))
    ) AS expected(SourceProfile, [RowCount])
    FULL OUTER JOIN
    (
        SELECT SourceProfile, COUNT_BIG(*) AS [RowCount]
        FROM dbo.Rt02Learner
        GROUP BY SourceProfile
    ) AS actual
        ON actual.SourceProfile = expected.SourceProfile
    WHERE expected.SourceProfile IS NULL
       OR actual.SourceProfile IS NULL
       OR actual.[RowCount] <> expected.[RowCount]
)
    THROW 528136, 'RT02 target profile distribution is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Rt02Learner AS learner
    WHERE learner.DatasetRole <> 'CONCURRENT_TARGET'
      AND
      (
          learner.QlhvOwnedHash <> CONVERT
          (
              char(64),
              HASHBYTES
              (
                  'SHA2_256',
                  CONVERT
                  (
                      varchar(max),
                      CONCAT
                      (
                          'RT02B2|QLHV|',
                          learner.IdentityHmac,
                          '|',
                          learner.WorkflowState,
                          '|NOTES|PHOTO_DISABLED'
                      )
                  )
              ),
              2
          )
          OR learner.NotesHash <> CONVERT
          (
              char(64),
              HASHBYTES
              (
                  'SHA2_256',
                  CONVERT
                  (
                      varchar(max),
                      CONCAT('RT02B2|NOTES|', learner.IdentityHmac)
                  )
              ),
              2
          )
      )
)
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE DatasetRole = 'CONCURRENT_TARGET'
         AND ScenarioCode = 'FAULT_INJECTION'
         AND SourceProfile = 'RT02_CONCURRENT'
         AND HoTen = N'SYNTHETIC CONCURRENT TARGET'
         AND MappedHash = CONVERT
         (
             char(64),
             HASHBYTES
             (
                 'SHA2_256',
                 CONVERT(varchar(max), 'RT02B2-CONCURRENT-MAPPED')
             ),
             2
         )
         AND QlhvOwnedHash = CONVERT
         (
             char(64),
             HASHBYTES
             (
                 'SHA2_256',
                 CONVERT(varchar(max), 'RT02B2-CONCURRENT-QLHV')
             ),
             2
         )
         AND NotesHash = CONVERT
         (
             char(64),
             HASHBYTES
             (
                 'SHA2_256',
                 CONVERT(varchar(max), 'RT02B2-CONCURRENT-NOTES')
             ),
             2
         )
         AND Active = 1
         AND SoftDeleted = 0
   ) <> 1
    THROW 528137, 'RT02 QLHV-owned invariant is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM dbo.Rt02Learner
    WHERE DatasetRole = 'NO_CHANGE'
) <> 155
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE DatasetRole = 'SOURCE_ONLY_NEW_ROW'
   ) <> 604
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE DatasetRole = 'STALE_IMPORTED_VALUE'
   ) <> 607
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE DatasetRole = 'SOURCE_ROW_REMOVED'
   ) <> 2
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE DatasetRole = 'SOFT_DELETED_BASELINE'
   ) <> 3
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner
       WHERE DatasetRole = 'CONCURRENT_TARGET'
   ) <> 1
    THROW 528138, 'RT02 target role distribution is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Rt02Learner AS learner
    INNER JOIN [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
        ON sourceItem.IdentityHmac = learner.IdentityHmac
    WHERE learner.DatasetRole = 'SOURCE_ONLY_NEW_ROW'
      AND
      (
          learner.HoTen <> sourceItem.HoTen
          OR learner.MappedHash <> sourceItem.SourceRowHash
      )
)
   OR
   (
       SELECT COUNT_BIG(*)
       FROM dbo.Rt02Learner AS learner
       INNER JOIN [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
           ON sourceItem.IdentityHmac = learner.IdentityHmac
       WHERE learner.DatasetRole = 'SOURCE_ONLY_NEW_ROW'
   ) <> 604
    THROW 528139, 'RT02 source-only mapped state is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM dbo.Rt02Learner AS learner
    INNER JOIN [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
        ON sourceItem.IdentityHmac = learner.IdentityHmac
    WHERE sourceItem.ScenarioCode = 'CORE'
      AND sourceItem.DatasetRole = 'SOURCE_ONLY_NEW_ROW'
      AND learner.IdentityHmac =
          '24A9DBFB0AC0A9BD455DFF55C2388AD310FDEBF6A52D022BDAB9FAC4ADF3FDC1'
      AND learner.SourceProfile = 'CSDT_OTO'
      AND learner.ScenarioCode = 'HARNESS'
      AND learner.DatasetRole = 'SOURCE_ONLY_NEW_ROW'
      AND learner.HoTen = N'SYNTHETIC OTO ' + N'INS' + N'ERT'
      AND learner.HoTen = sourceItem.HoTen
      AND learner.MappedHash = sourceItem.SourceRowHash
      AND learner.Active = 1
      AND learner.SoftDeleted = 0
) <> 1
    THROW 528140, 'RT02 core source-only final state is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM dbo.Rt02Learner AS learner
    INNER JOIN [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
        ON sourceItem.IdentityHmac = learner.IdentityHmac
    WHERE sourceItem.ScenarioCode = 'CORE'
      AND sourceItem.DatasetRole = 'STALE_IMPORTED_VALUE'
      AND learner.IdentityHmac =
          '4C347DB3B154DCEF7DAA7C9E8BE482CED61737D4E0A4F0A38E7AB5B4F50AC39F'
      AND learner.SourceProfile = 'CSDT_OTO'
      AND learner.ScenarioCode = 'CORE'
      AND learner.DatasetRole = 'STALE_IMPORTED_VALUE'
      AND learner.HoTen = N'SYNTHETIC OTO UPDATED'
      AND learner.HoTen = sourceItem.HoTen
      AND learner.MappedHash = sourceItem.SourceRowHash
      AND learner.Active = 1
      AND learner.SoftDeleted = 0
) <> 1
    THROW 528141, 'RT02 core HoTen/mapped final state is invalid.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM dbo.Rt02Learner
    WHERE IdentityHmac =
          'F340B82925F5B4EF4EF02A85189036E18BDA4729A6493811AD55C0DC7F956CA3'
      AND SourceProfile = 'CSDT_OTO'
      AND ScenarioCode = 'CORE'
      AND DatasetRole = 'SOURCE_ROW_REMOVED'
      AND HoTen = N'SYNTHETIC OTO TARGET ONLY'
      AND MappedHash = CONVERT
      (
          char(64),
          HASHBYTES
          (
              'SHA2_256',
              CONVERT
              (
                  varchar(max),
                  CONCAT
                  (
                      'RT02B2|TARGETONLY|',
                      'F340B82925F5B4EF4EF02A85189036E18BDA4729A6493811AD55C0DC7F956CA3'
                  )
              )
          ),
          2
      )
      AND Active = 1
      AND SoftDeleted = 0
) <> 1
   OR EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
       WHERE IdentityHmac =
             'F340B82925F5B4EF4EF02A85189036E18BDA4729A6493811AD55C0DC7F956CA3'
   )
    THROW 528142, 'RT02 core target-only final state is invalid.', 1;

DECLARE @TargetCoreCanonical varchar(max);
SELECT @TargetCoreCanonical = STRING_AGG
(
    CONVERT
    (
        varchar(max),
        CONCAT
        (
            learner.IdentityHmac,
            '|',
            learner.SourceProfile,
            '|',
            learner.ScenarioCode,
            '|',
            learner.DatasetRole,
            '|',
            learner.HoTen,
            '|',
            learner.MappedHash,
            '|',
            learner.QlhvOwnedHash,
            '|',
            learner.WorkflowState,
            '|',
            learner.NotesHash,
            '|',
            learner.PhotoState,
            '|',
            CONVERT(varchar(1), learner.Active),
            '|',
            CONVERT(varchar(1), learner.SoftDeleted)
        )
    ),
    ';'
) WITHIN GROUP
(
    ORDER BY learner.IdentityHmac COLLATE Latin1_General_100_BIN2
)
FROM dbo.Rt02Learner AS learner
WHERE learner.ScenarioCode = 'CORE';

DECLARE @TargetCoreDigest char(64) = CONVERT
(
    char(64),
    HASHBYTES('SHA2_256', @TargetCoreCanonical),
    2
);

IF @TargetCoreDigest <>
   '7CF9F3F077C0A8E07692B6E30F340A8F089FB1B91D505C26FBB59B257F3DA426'
    THROW 528143, 'RT02 target deterministic core digest is invalid.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.Rt02ManualReviewEvidence) <> 2
   OR EXISTS
   (
       SELECT 1
       FROM dbo.Rt02ManualReviewEvidence AS evidence
       LEFT JOIN dbo.Rt02Learner AS learner
           ON learner.IdentityHmac = evidence.IdentityHmac
       LEFT JOIN dbo.Rt02ApplyMarker AS marker
           ON marker.CycleId = evidence.CycleId
       WHERE evidence.Disposition <> 'MANUAL_REVIEW_REQUIRED'
          OR evidence.TargetRetainedActive <> 1
          OR evidence.TargetMutated <> 0
          OR learner.IdentityHmac IS NULL
          OR learner.DatasetRole <> 'SOURCE_ROW_REMOVED'
          OR learner.Active <> 1
          OR learner.SoftDeleted <> 0
          OR marker.CycleId IS NULL
          OR marker.DispositionHash <> evidence.DispositionHash
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM dbo.Rt02ManualReviewEvidence
       WHERE CycleId = 'RT02B2-CORE-MINIMAL'
         AND OperationId = 'OP-CORE-RETAIN'
         AND IdentityHmac =
             'F340B82925F5B4EF4EF02A85189036E18BDA4729A6493811AD55C0DC7F956CA3'
   )
   OR NOT EXISTS
   (
       SELECT 1
       FROM dbo.Rt02ManualReviewEvidence
       WHERE CycleId = 'RT02B2-LOAD-MIXED-1000'
         AND OperationId = 'OP-LOAD-M1000-R-RETAIN'
         AND IdentityHmac =
             '81ABA9FF1EB8E24C8FE367D42226205185568B02B3F474316E8E37FF4997B00C'
   )
    THROW 528144, 'RT02 manual-review evidence is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Rt02Learner AS learner
    WHERE learner.DatasetRole = 'SOURCE_ROW_REMOVED'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Rt02ManualReviewEvidence AS evidence
          WHERE evidence.IdentityHmac = learner.IdentityHmac
      )
)
   OR EXISTS
   (
       SELECT 1
       FROM dbo.Rt02ManualReviewEvidence AS evidence
       WHERE NOT EXISTS
       (
           SELECT 1
           FROM dbo.Rt02Learner AS learner
           WHERE learner.IdentityHmac = evidence.IdentityHmac
             AND learner.DatasetRole = 'SOURCE_ROW_REMOVED'
             AND learner.Active = 1
             AND learner.SoftDeleted = 0
       )
   )
    THROW 528145, 'RT02 retained/evidence relationship is invalid.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyMarker) <> 10
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyCheckpoint) <> 10
    THROW 528146, 'RT02 marker/checkpoint count is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            ('RT02B2-CORE-MINIMAL', 1, 1, 1),
            ('RT02B2-MOTO-NOCHANGE', 0, 0, 0),
            ('RT02B2-CHECKPOINT-CONFLICT-PRIOR', 0, 0, 0),
            ('RT02B2-KILL-AFTER-COMMIT', 1, 0, 0),
            ('RT02B2-IDEMPOTENT-REPLAY', 1, 0, 0),
            ('RT02B2-TARGET-TIMEOUT', 1, 0, 0),
            ('RT02B2-DEADLOCK', 0, 1, 0),
            ('RT02B2-LOAD-' + 'INS' + 'ERT-100', 100, 0, 0),
            ('RT02B2-LOAD-' + 'UPD' + 'ATE-100', 0, 100, 0),
            ('RT02B2-LOAD-MIXED-1000', 500, 499, 1)
    ) AS expected
    (
        CycleId,
        ExpectedInsertedRows,
        ExpectedUpdatedRows,
        ExpectedRetainedRows
    )
    FULL OUTER JOIN dbo.Rt02ApplyMarker AS marker
        ON marker.CycleId = expected.CycleId
    WHERE expected.CycleId IS NULL
       OR marker.CycleId IS NULL
       OR marker.InsertedRows <> expected.ExpectedInsertedRows
       OR marker.UpdatedRows <> expected.ExpectedUpdatedRows
       OR marker.RetainedRows <> expected.ExpectedRetainedRows
)
    THROW 528147, 'RT02 exact marker set is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            ('CSDT_OTO', 'RT02B2-CORE-MINIMAL'),
            ('CSDT_MOTO', 'RT02B2-MOTO-NOCHANGE'),
            ('RT02_CPCONFLICT', 'RT02B2-CHECKPOINT-CONFLICT-PRIOR'),
            ('RT02_KILL_POST', 'RT02B2-KILL-AFTER-COMMIT'),
            ('RT02_REPLAY', 'RT02B2-IDEMPOTENT-REPLAY'),
            ('RT02_TIMEOUT', 'RT02B2-TARGET-TIMEOUT'),
            ('RT02_DEADLOCK', 'RT02B2-DEADLOCK'),
            ('RT02_LOAD_I100', 'RT02B2-LOAD-' + 'INS' + 'ERT-100'),
            ('RT02_LOAD_U100', 'RT02B2-LOAD-' + 'UPD' + 'ATE-100'),
            ('RT02_LOAD_M1000', 'RT02B2-LOAD-MIXED-1000')
    ) AS expected(SourceProfile, CycleId)
    FULL OUTER JOIN dbo.Rt02ApplyCheckpoint AS checkpointItem
        ON checkpointItem.SourceProfile = expected.SourceProfile
       AND checkpointItem.CycleId = expected.CycleId
    WHERE expected.SourceProfile IS NULL
       OR checkpointItem.SourceProfile IS NULL
       OR checkpointItem.Mode <> 'DIRECT_REALTIME_APPLY'
       OR checkpointItem.EnvironmentId <>
          'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
       OR checkpointItem.MappingFingerprint <>
          '9938DDC131D2C3DE91C1F35A7CBCAA5B69FE40577514CC4B38A43B1A667A0743'
       OR checkpointItem.SourceWatermark <= 0
)
    THROW 528148, 'RT02 exact checkpoint set is invalid.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Rt02ApplyMarker AS marker
    FULL OUTER JOIN dbo.Rt02ApplyCheckpoint AS checkpointItem
        ON checkpointItem.CycleId = marker.CycleId
    WHERE marker.CycleId IS NULL
       OR checkpointItem.CycleId IS NULL
       OR checkpointItem.PlanHash <> marker.PlanHash
       OR checkpointItem.PublishedAtUtc < marker.CommittedAtUtc
       OR checkpointItem.MarkerHash <> CONVERT
          (
              char(64),
              HASHBYTES
              (
                  'SHA2_256',
                  CONVERT
                  (
                      varchar(max),
                      CONCAT
                      (
                          marker.CycleId,
                          '|',
                          marker.PlanHash,
                          '|',
                          marker.DispositionHash,
                          '|',
                          marker.InsertedRows,
                          '|',
                          marker.UpdatedRows,
                          '|',
                          marker.RetainedRows,
                          '|',
                          marker.PreservedQlhvOwnedHash
                      )
                  )
              ),
              2
          )
)
   OR EXISTS
   (
       SELECT CycleId
       FROM dbo.Rt02ApplyCheckpoint
       GROUP BY CycleId
       HAVING COUNT_BIG(*) <> 1
   )
   OR EXISTS
   (
       SELECT 1
       FROM dbo.Rt02ApplyMarker
       WHERE PlanHash COLLATE Latin1_General_100_BIN2
                LIKE '%[^0-9A-F]%'
          OR DispositionHash COLLATE Latin1_General_100_BIN2
                LIKE '%[^0-9A-F]%'
          OR PreservedQlhvOwnedHash COLLATE Latin1_General_100_BIN2
                LIKE '%[^0-9A-F]%'
          OR LEN(PlanHash) <> 64
          OR LEN(DispositionHash) <> 64
          OR LEN(PreservedQlhvOwnedHash) <> 64
   )
    THROW 528149, 'RT02 marker/checkpoint integrity is invalid.', 1;

IF
(
    SELECT SUM(CONVERT(bigint, InsertedRows))
    FROM dbo.Rt02ApplyMarker
) <> 604
   OR
   (
       SELECT SUM(CONVERT(bigint, UpdatedRows))
       FROM dbo.Rt02ApplyMarker
   ) <> 601
   OR
   (
       SELECT SUM(CONVERT(bigint, RetainedRows))
       FROM dbo.Rt02ApplyMarker
   ) <> 2
    THROW 528150, 'RT02 marker aggregate is invalid.', 1;

SELECT
    N'RT02_FINAL_TARGET_INTEGRITY' AS Evidence,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner) AS LearnerRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE Active = 1 AND SoftDeleted = 0) AS ActiveRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE Active = 0 AND SoftDeleted = 1) AS SoftRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode = 'CORE') AS CoreRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
        WHERE ScenarioCode <> 'CORE') AS NonCoreRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ManualReviewEvidence)
        AS ManualReviewRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyMarker) AS MarkerRows,
    (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyCheckpoint) AS CheckpointRows,
    @TargetCoreDigest AS CoreDigest,
    (SELECT DatasetFingerprint FROM dbo.Rt02EnvironmentState)
        AS DatasetFingerprint;

SELECT
    N'RT02_FINAL_MARKER_CHECKPOINT_PAIRS' AS Evidence,
    checkpointItem.SourceProfile,
    marker.CycleId,
    marker.InsertedRows,
    marker.UpdatedRows,
    marker.RetainedRows,
    marker.PlanHash,
    marker.DispositionHash,
    checkpointItem.MarkerHash,
    CONVERT
    (
        char(64),
        HASHBYTES
        (
            'SHA2_256',
            CONVERT
            (
                varchar(max),
                CONCAT
                (
                    marker.CycleId,
                    '|',
                    marker.PlanHash,
                    '|',
                    marker.DispositionHash,
                    '|',
                    marker.InsertedRows,
                    '|',
                    marker.UpdatedRows,
                    '|',
                    marker.RetainedRows,
                    '|',
                    marker.PreservedQlhvOwnedHash
                )
            )
        ),
        2
    ) AS RecomputedMarkerHash,
    marker.CommittedAtUtc,
    checkpointItem.PublishedAtUtc
FROM dbo.Rt02ApplyMarker AS marker
INNER JOIN dbo.Rt02ApplyCheckpoint AS checkpointItem
    ON checkpointItem.CycleId = marker.CycleId
ORDER BY marker.CycleId;

SELECT
    N'RT02_FINAL_MANUAL_REVIEW' AS Evidence,
    evidence.CycleId,
    evidence.OperationId,
    evidence.IdentityHmac,
    evidence.Disposition,
    evidence.DispositionHash,
    evidence.TargetRetainedActive,
    evidence.TargetMutated,
    learner.SourceProfile,
    learner.ScenarioCode,
    learner.DatasetRole,
    learner.HoTen,
    learner.MappedHash,
    learner.QlhvOwnedHash,
    learner.Active,
    learner.SoftDeleted,
    (
        SELECT COUNT_BIG(*)
        FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
        WHERE sourceItem.IdentityHmac = evidence.IdentityHmac
    ) AS OtoSourceRows
FROM dbo.Rt02ManualReviewEvidence AS evidence
INNER JOIN dbo.Rt02Learner AS learner
    ON learner.IdentityHmac = evidence.IdentityHmac
ORDER BY evidence.CycleId, evidence.OperationId;
GO
