USE [QLHV_APP];
GO

/*
RT-03 current production readiness baseline. SELECT-only.

Pinned route:
  CSDL_OTO  -> QLHV_APP (CSDT_OTO / 66029)
  CSDL_MOTO -> QLHV_APP (CSDT_MOTO / 66030)

This proof performs no DDL, DML, Change Tracking/Snapshot state change,
checkpoint publication, Auto Sync trigger, or realtime activation. It emits
only metadata, aggregate counts, timestamps, actor/source codes, and hashes.
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
    THROW 527510, 'RT03_PRODUCTION_IDENTITY_REJECTED: unexpected SQL Server.', 1;

IF DB_NAME() <> N'QLHV_APP'
    THROW 527511, 'RT03_PRODUCTION_IDENTITY_REJECTED: unexpected initial database.', 1;

IF EXISTS
(
    SELECT expected.DatabaseName
    FROM
    (
        VALUES
            (N'CSDL_MOTO', 8, CONVERT(uniqueidentifier, '308BDDA8-80F3-4ACB-9836-578D80A9E98E')),
            (N'CSDL_MOTO_BAK', 10, CONVERT(uniqueidentifier, '2F326753-B1A8-4E94-A404-49FA71E8260A')),
            (N'CSDL_MOTO_V1', 13, CONVERT(uniqueidentifier, '454A56FA-EAA5-4EA7-B41E-9AD500C2D69F')),
            (N'CSDL_OTO', 9, CONVERT(uniqueidentifier, '9A8B9BC1-18F3-4823-8123-3DC197A9D540')),
            (N'CSDL_OTO_BAK', 11, CONVERT(uniqueidentifier, '22ABFD1B-5F1B-4DCA-9EFB-DFB0676623CE')),
            (N'CSDL_OTO_V1', 14, CONVERT(uniqueidentifier, '41463BB9-532D-4BDB-B821-595D7A30BB2C')),
            (N'QLHV_APP', 12, CONVERT(uniqueidentifier, '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'))
    ) AS expected(DatabaseName, DatabaseId, DatabaseGuid)
    LEFT JOIN sys.databases AS actual
        ON actual.name = expected.DatabaseName
       AND actual.database_id = expected.DatabaseId
       AND actual.state_desc = N'ONLINE'
    LEFT JOIN sys.database_recovery_status AS recovery
        ON recovery.database_id = actual.database_id
       AND recovery.database_guid = expected.DatabaseGuid
    WHERE actual.database_id IS NULL OR recovery.database_id IS NULL
)
    THROW 527512, 'RT03_PRODUCTION_IDENTITY_REJECTED: pinned database ID/GUID changed.', 1;

SELECT
    N'RT03_PINNED_PRODUCTION_ROUTE' AS Evidence,
    expected.DatabaseName,
    expected.DatabaseId,
    CONVERT(nvarchar(36), expected.DatabaseGuid) AS DatabaseGuid,
    actual.state_desc AS StateDescription,
    actual.snapshot_isolation_state_desc AS SnapshotIsolationState,
    actual.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn,
    CASE WHEN tracking.database_id IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END
        AS ChangeTrackingEnabled,
    tracking.retention_period AS ChangeTrackingRetentionPeriod,
    tracking.retention_period_units_desc AS ChangeTrackingRetentionUnits,
    tracking.is_auto_cleanup_on AS ChangeTrackingAutoCleanup
FROM
(
    VALUES
        (N'CSDL_MOTO', 8, CONVERT(uniqueidentifier, '308BDDA8-80F3-4ACB-9836-578D80A9E98E')),
        (N'CSDL_MOTO_BAK', 10, CONVERT(uniqueidentifier, '2F326753-B1A8-4E94-A404-49FA71E8260A')),
        (N'CSDL_MOTO_V1', 13, CONVERT(uniqueidentifier, '454A56FA-EAA5-4EA7-B41E-9AD500C2D69F')),
        (N'CSDL_OTO', 9, CONVERT(uniqueidentifier, '9A8B9BC1-18F3-4823-8123-3DC197A9D540')),
        (N'CSDL_OTO_BAK', 11, CONVERT(uniqueidentifier, '22ABFD1B-5F1B-4DCA-9EFB-DFB0676623CE')),
        (N'CSDL_OTO_V1', 14, CONVERT(uniqueidentifier, '41463BB9-532D-4BDB-B821-595D7A30BB2C')),
        (N'QLHV_APP', 12, CONVERT(uniqueidentifier, '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'))
) AS expected(DatabaseName, DatabaseId, DatabaseGuid)
INNER JOIN sys.databases AS actual
    ON actual.database_id = expected.DatabaseId
LEFT JOIN sys.change_tracking_databases AS tracking
    ON tracking.database_id = actual.database_id
ORDER BY expected.DatabaseName;
GO

USE [CSDL_OTO];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF DB_ID() <> 9
   OR NOT EXISTS
      (
          SELECT 1
          FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '9A8B9BC1-18F3-4823-8123-3DC197A9D540'
      )
    THROW 527513, 'RT03_PRODUCTION_IDENTITY_REJECTED: CSDL_OTO identity.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL
   OR OBJECT_ID(N'dbo.KhoaHoc', N'U') IS NULL
    THROW 527514, 'RT03_SOURCE_SCHEMA_REJECTED: CSDL_OTO required table missing.', 1;

WITH scopedRows AS
(
    SELECT LTRIM(RTRIM(learner.MaDK)) AS SourceIdentity
    FROM dbo.NguoiLX AS learner
    INNER JOIN dbo.NguoiLX_HoSo AS dossier
        ON dossier.MaDK = learner.MaDK
    LEFT JOIN dbo.KhoaHoc AS course
        ON course.MaKH = dossier.MaKhoaHoc
    WHERE LTRIM(RTRIM(learner.MaDK)) LIKE N'66029%'
       OR LTRIM(RTRIM(course.MaCSDT)) = N'66029'
),
schemaRows AS
(
    SELECT
        schemaItem.name AS SchemaName,
        tableItem.name AS TableName,
        columnItem.column_id AS ColumnId,
        columnItem.name AS ColumnName,
        typeItem.name AS TypeName,
        columnItem.max_length AS MaxLength,
        columnItem.precision AS [Precision],
        columnItem.scale AS Scale,
        columnItem.is_nullable AS IsNullable,
        COALESCE(columnItem.collation_name, N'') AS CollationName
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = tableItem.object_id
    INNER JOIN sys.types AS typeItem
        ON typeItem.user_type_id = columnItem.user_type_id
    WHERE schemaItem.name = N'dbo'
      AND tableItem.name IN
          (N'NguoiLX', N'NguoiLX_HoSo', N'KhoaHoc', N'DM_HangDT', N'DM_DVHC')
)
SELECT
    N'RT03_SOURCE_BASELINE' AS Evidence,
    N'OTO' AS SourceType,
    DB_NAME() AS DatabaseName,
    DB_ID() AS DatabaseId,
    CONVERT(nvarchar(36), recovery.database_guid) AS DatabaseGuid,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX) AS RawNguoiLxRows,
    (SELECT COUNT_BIG(*) FROM scopedRows) AS ScopedMappedRows,
    (SELECT COUNT_BIG(*) FROM
        (SELECT SourceIdentity FROM scopedRows GROUP BY SourceIdentity HAVING COUNT_BIG(*) > 1)
        AS duplicateGroups) AS DuplicateIdentityGroups,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTableCount,
    CHANGE_TRACKING_CURRENT_VERSION() AS CurrentChangeTrackingVersion,
    CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX'))
        AS NguoiLxMinimumValidVersion,
    CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))
        AS NguoiLxHoSoMinimumValidVersion,
    databaseItem.snapshot_isolation_state_desc AS SnapshotIsolationState,
    databaseItem.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn,
    (SELECT COUNT_BIG(*) FROM schemaRows) AS SchemaColumnCount,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(
        (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(
            SchemaName, N'|', TableName, N'|', ColumnId, N'|', ColumnName, N'|',
            TypeName, N'|', MaxLength, N'|', [Precision], N'|', Scale, N'|',
            IsNullable, N'|', CollationName)), NCHAR(10))
         WITHIN GROUP (ORDER BY SchemaName, TableName, ColumnId)
         FROM schemaRows), N'')), 2) AS SchemaFingerprint,
    CONVERT(bit, 0) AS DirectRealtimeCheckpointInitialized,
    CONVERT(bigint, NULL) AS DirectRealtimeCheckpointVersion,
    CONVERT(bigint, NULL) AS CandidatesSinceCheckpoint,
    (SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID())
        AS RelatedSessionCount,
    (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests
        WHERE database_id = DB_ID() AND session_id <> @@SPID) AS RelatedActiveRequestCount,
    SYSUTCDATETIME() AS ReadAtUtc
FROM sys.databases AS databaseItem
INNER JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
WHERE databaseItem.database_id = DB_ID();
GO

USE [CSDL_MOTO];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF DB_ID() <> 8
   OR NOT EXISTS
      (
          SELECT 1
          FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '308BDDA8-80F3-4ACB-9836-578D80A9E98E'
      )
    THROW 527515, 'RT03_PRODUCTION_IDENTITY_REJECTED: CSDL_MOTO identity.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL
   OR OBJECT_ID(N'dbo.KhoaHoc', N'U') IS NULL
    THROW 527516, 'RT03_SOURCE_SCHEMA_REJECTED: CSDL_MOTO required table missing.', 1;

WITH scopedRows AS
(
    SELECT LTRIM(RTRIM(learner.MaDK)) AS SourceIdentity
    FROM dbo.NguoiLX AS learner
    INNER JOIN dbo.NguoiLX_HoSo AS dossier
        ON dossier.MaDK = learner.MaDK
    LEFT JOIN dbo.KhoaHoc AS course
        ON course.MaKH = dossier.MaKhoaHoc
    WHERE LTRIM(RTRIM(learner.MaDK)) LIKE N'66030%'
       OR LTRIM(RTRIM(course.MaCSDT)) = N'66030'
),
schemaRows AS
(
    SELECT
        schemaItem.name AS SchemaName,
        tableItem.name AS TableName,
        columnItem.column_id AS ColumnId,
        columnItem.name AS ColumnName,
        typeItem.name AS TypeName,
        columnItem.max_length AS MaxLength,
        columnItem.precision AS [Precision],
        columnItem.scale AS Scale,
        columnItem.is_nullable AS IsNullable,
        COALESCE(columnItem.collation_name, N'') AS CollationName
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = tableItem.object_id
    INNER JOIN sys.types AS typeItem
        ON typeItem.user_type_id = columnItem.user_type_id
    WHERE schemaItem.name = N'dbo'
      AND tableItem.name IN
          (N'NguoiLX', N'NguoiLX_HoSo', N'KhoaHoc', N'DM_HangDT', N'DM_DVHC')
)
SELECT
    N'RT03_SOURCE_BASELINE' AS Evidence,
    N'MOTO' AS SourceType,
    DB_NAME() AS DatabaseName,
    DB_ID() AS DatabaseId,
    CONVERT(nvarchar(36), recovery.database_guid) AS DatabaseGuid,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX) AS RawNguoiLxRows,
    (SELECT COUNT_BIG(*) FROM scopedRows) AS ScopedMappedRows,
    (SELECT COUNT_BIG(*) FROM
        (SELECT SourceIdentity FROM scopedRows GROUP BY SourceIdentity HAVING COUNT_BIG(*) > 1)
        AS duplicateGroups) AS DuplicateIdentityGroups,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTableCount,
    CHANGE_TRACKING_CURRENT_VERSION() AS CurrentChangeTrackingVersion,
    CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX'))
        AS NguoiLxMinimumValidVersion,
    CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))
        AS NguoiLxHoSoMinimumValidVersion,
    databaseItem.snapshot_isolation_state_desc AS SnapshotIsolationState,
    databaseItem.is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn,
    (SELECT COUNT_BIG(*) FROM schemaRows) AS SchemaColumnCount,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(
        (SELECT STRING_AGG(CONVERT(nvarchar(max), CONCAT(
            SchemaName, N'|', TableName, N'|', ColumnId, N'|', ColumnName, N'|',
            TypeName, N'|', MaxLength, N'|', [Precision], N'|', Scale, N'|',
            IsNullable, N'|', CollationName)), NCHAR(10))
         WITHIN GROUP (ORDER BY SchemaName, TableName, ColumnId)
         FROM schemaRows), N'')), 2) AS SchemaFingerprint,
    CONVERT(bit, 0) AS DirectRealtimeCheckpointInitialized,
    CONVERT(bigint, NULL) AS DirectRealtimeCheckpointVersion,
    CONVERT(bigint, NULL) AS CandidatesSinceCheckpoint,
    (SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID())
        AS RelatedSessionCount,
    (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests
        WHERE database_id = DB_ID() AND session_id <> @@SPID) AS RelatedActiveRequestCount,
    SYSUTCDATETIME() AS ReadAtUtc
FROM sys.databases AS databaseItem
INNER JOIN sys.database_recovery_status AS recovery
    ON recovery.database_id = databaseItem.database_id
WHERE databaseItem.database_id = DB_ID();
GO

USE [QLHV_APP];
GO

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF DB_ID() <> 12
   OR NOT EXISTS
      (
          SELECT 1
          FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
      )
    THROW 527517, 'RT03_PRODUCTION_IDENTITY_REJECTED: QLHV_APP identity.', 1;

IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvAutoSyncRun', N'U') IS NULL
   OR OBJECT_ID(N'dbo.App_QlhvSyncOperationHistory', N'U') IS NULL
    THROW 527518, 'RT03_TARGET_SCHEMA_REJECTED: required QLHV_APP table missing.', 1;

SELECT
    N'RT03_TARGET_PROFILE_COUNTS' AS Evidence,
    SourceProfileCode,
    SUM(CASE WHEN IsDeleted = 0 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS ActiveRows,
    SUM(CASE WHEN IsDeleted = 1 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END)
        AS SoftDeletedRows,
    COUNT_BIG(*) AS TotalRows
FROM dbo.App_HocVien
WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
GROUP BY SourceProfileCode
ORDER BY SourceProfileCode;

SELECT
    N'RT03_TARGET_DUPLICATE_ACTIVE' AS Evidence,
    profile.SourceProfileCode,
    COUNT_BIG(duplicateGroup.SourceIdentity) AS DuplicateActiveIdentityGroups
FROM (VALUES (N'CSDT_OTO'), (N'CSDT_MOTO')) AS profile(SourceProfileCode)
LEFT JOIN
(
    SELECT SourceProfileCode, LTRIM(RTRIM(SourceMaDK)) AS SourceIdentity
    FROM dbo.App_HocVien
    WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
      AND IsDeleted = 0
    GROUP BY SourceProfileCode, LTRIM(RTRIM(SourceMaDK))
    HAVING COUNT_BIG(*) > 1
) AS duplicateGroup
    ON duplicateGroup.SourceProfileCode = profile.SourceProfileCode
GROUP BY profile.SourceProfileCode
ORDER BY profile.SourceProfileCode;

SELECT
    N'RT03_TARGET_OWNERSHIP' AS Evidence,
    SourceProfileCode,
    COALESCE(NULLIF(LTRIM(RTRIM(SourceSystem)), N''), N'<NULL_OR_BLANK>') AS SourceSystem,
    COALESCE(NULLIF(LTRIM(RTRIM(CreatedBy)), N''), N'<NULL_OR_BLANK>') AS CreatedBy,
    COALESCE(NULLIF(LTRIM(RTRIM(UpdatedBy)), N''), N'<NULL_OR_BLANK>') AS UpdatedBy,
    IsDeleted,
    COUNT_BIG(*) AS OwnershipRows
FROM dbo.App_HocVien
WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
GROUP BY
    SourceProfileCode,
    COALESCE(NULLIF(LTRIM(RTRIM(SourceSystem)), N''), N'<NULL_OR_BLANK>'),
    COALESCE(NULLIF(LTRIM(RTRIM(CreatedBy)), N''), N'<NULL_OR_BLANK>'),
    COALESCE(NULLIF(LTRIM(RTRIM(UpdatedBy)), N''), N'<NULL_OR_BLANK>'),
    IsDeleted
ORDER BY SourceProfileCode, IsDeleted, CreatedBy, UpdatedBy;

WITH otoSource AS
(
    SELECT DISTINCT LTRIM(RTRIM(learner.MaDK)) AS SourceIdentity
    FROM CSDL_OTO.dbo.NguoiLX AS learner
    INNER JOIN CSDL_OTO.dbo.NguoiLX_HoSo AS dossier
        ON dossier.MaDK = learner.MaDK
    LEFT JOIN CSDL_OTO.dbo.KhoaHoc AS course
        ON course.MaKH = dossier.MaKhoaHoc
    WHERE LTRIM(RTRIM(learner.MaDK)) LIKE N'66029%'
       OR LTRIM(RTRIM(course.MaCSDT)) = N'66029'
),
motoSource AS
(
    SELECT DISTINCT LTRIM(RTRIM(learner.MaDK)) AS SourceIdentity
    FROM CSDL_MOTO.dbo.NguoiLX AS learner
    INNER JOIN CSDL_MOTO.dbo.NguoiLX_HoSo AS dossier
        ON dossier.MaDK = learner.MaDK
    LEFT JOIN CSDL_MOTO.dbo.KhoaHoc AS course
        ON course.MaKH = dossier.MaKhoaHoc
    WHERE LTRIM(RTRIM(learner.MaDK)) LIKE N'66030%'
       OR LTRIM(RTRIM(course.MaCSDT)) = N'66030'
)
SELECT
    N'RT03_TARGET_ONLY_COUNTS' AS Evidence,
    profile.SourceProfileCode,
    CASE profile.SourceProfileCode
        WHEN N'CSDT_OTO' THEN
            (SELECT COUNT_BIG(*) FROM dbo.App_HocVien AS target
             WHERE target.SourceProfileCode = N'CSDT_OTO'
               AND target.IsDeleted = 0
               AND NOT EXISTS
                   (SELECT 1 FROM otoSource AS source
                    WHERE source.SourceIdentity = target.SourceMaDK))
        WHEN N'CSDT_MOTO' THEN
            (SELECT COUNT_BIG(*) FROM dbo.App_HocVien AS target
             WHERE target.SourceProfileCode = N'CSDT_MOTO'
               AND target.IsDeleted = 0
               AND NOT EXISTS
                   (SELECT 1 FROM motoSource AS source
                    WHERE source.SourceIdentity = target.SourceMaDK))
    END AS TargetOnlyActiveRows
FROM (VALUES (N'CSDT_OTO'), (N'CSDT_MOTO')) AS profile(SourceProfileCode)
ORDER BY profile.SourceProfileCode;

WITH canonicalRows AS
(
    SELECT
        SourceProfileCode,
        CONCAT(
            COALESCE(SourceMaDK, N'<NULL>'), N'|', COALESCE(MaDK, N'<NULL>'), N'|',
            COALESCE(MaKhoa, N'<NULL>'), N'|', COALESCE(TenKhoa, N'<NULL>'), N'|',
            COALESCE(MaHangDT, N'<NULL>'), N'|', COALESCE(HangGPLXHoc, N'<NULL>'), N'|',
            COALESCE(HoTen, N'<NULL>'), N'|', COALESCE(CONVERT(nvarchar(33), NgaySinh, 126), N'<NULL>'), N'|',
            COALESCE(GioiTinh, N'<NULL>'), N'|', COALESCE(SoCCCD, N'<NULL>'), N'|',
            COALESCE(DiaChiThuongTru, N'<NULL>'), N'|', COALESCE(SoGPLXDaCo, N'<NULL>'), N'|',
            COALESCE(HangGPLXDaCo, N'<NULL>'), N'|', COALESCE(NguoiNhanHoSo, N'<NULL>'), N'|',
            COALESCE(AnhRelativePath, N'<NULL>'), N'|', COALESCE(CONVERT(nvarchar(20), ChatLuongAnh), N'<NULL>'), N'|',
            COALESCE(CONVERT(nvarchar(33), NgayThuNhanAnh, 126), N'<NULL>'), N'|',
            COALESCE(NguoiThuNhanAnh, N'<NULL>'), N'|', COALESCE(SourceOfTruth, N'<NULL>'), N'|',
            COALESCE(V2RowHash, N'<NULL>')) AS SourceOwnedCanonical,
        CONCAT(
            COALESCE(CONVERT(nvarchar(20), HocVienId), N'<NULL>'), N'|', IsDeleted, N'|',
            COALESCE(GhiChuNoiBo, N'<NULL>'), N'|', DaDoiChieuCCCD, N'|', DaInThe, N'|', DaTaoXML, N'|',
            COALESCE(CreatedBy, N'<NULL>'), N'|', COALESCE(UpdatedBy, N'<NULL>'), N'|',
            COALESCE(DeletedBy, N'<NULL>'), N'|', COALESCE(DeleteReason, N'<NULL>'), N'|',
            COALESCE(CONVERT(nvarchar(33), CreatedAt, 126), N'<NULL>'), N'|',
            COALESCE(CONVERT(nvarchar(33), UpdatedAt, 126), N'<NULL>')) AS QlhvOwnedCanonical,
        SourceMaDK,
        HocVienId
    FROM dbo.App_HocVien
    WHERE SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO')
)
SELECT
    N'RT03_TARGET_OWNED_FIELD_FINGERPRINTS' AS Evidence,
    profile.SourceProfileCode,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(
        (SELECT STRING_AGG(CONVERT(nvarchar(max), SourceOwnedCanonical), NCHAR(10))
         WITHIN GROUP (ORDER BY SourceMaDK, HocVienId)
         FROM canonicalRows AS sourceRows
         WHERE sourceRows.SourceProfileCode = profile.SourceProfileCode), N'')), 2)
        AS SourceOwnedFingerprint,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(
        (SELECT STRING_AGG(CONVERT(nvarchar(max), QlhvOwnedCanonical), NCHAR(10))
         WITHIN GROUP (ORDER BY SourceMaDK, HocVienId)
         FROM canonicalRows AS qlhvRows
         WHERE qlhvRows.SourceProfileCode = profile.SourceProfileCode), N'')), 2)
        AS QlhvOwnedFingerprint
FROM (VALUES (N'CSDT_OTO'), (N'CSDT_MOTO')) AS profile(SourceProfileCode)
ORDER BY profile.SourceProfileCode;

SELECT TOP (1)
    N'RT03_AUTOSYNC_LATEST_RUN' AS Evidence,
    Id,
    CONVERT(nvarchar(36), RunId) AS RunId,
    TriggerType,
    Actor,
    Status,
    CurrentSourceType,
    CurrentStage,
    SourceOrderJson,
    ActiveSlot,
    CONVERT(varchar(33), CreatedAtUtc, 126) AS CreatedAtUtc,
    CONVERT(varchar(33), StartedAtUtc, 126) AS StartedAtUtc,
    CONVERT(varchar(33), CompletedAtUtc, 126) AS CompletedAtUtc,
    CONVERT(varchar(33), UpdatedAtUtc, 126) AS UpdatedAtUtc,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(OtoResultJson, N'')), 2)
        AS OtoResultHash,
    CONVERT(varchar(64), HASHBYTES(N'SHA2_256', COALESCE(MotoResultJson, N'')), 2)
        AS MotoResultHash,
    CASE WHEN ErrorMessage IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END
        AS HasError
FROM dbo.App_QlhvAutoSyncRun
ORDER BY Id DESC;

SELECT
    N'RT03_AUTOSYNC_STATE' AS Evidence,
    COUNT_BIG(*) AS HistoryRows,
    SUM(CASE WHEN Status IN (N'QUEUED', N'RUNNING')
             THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS ActiveRunRows,
    SUM(CASE WHEN ActiveSlot = 1
             THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS ActiveSlotRows,
    MAX(CASE WHEN Status = N'SUCCEEDED' THEN Id END) AS LatestSuccessfulId,
    MAX(CASE WHEN Status IN (N'QUEUED', N'RUNNING') THEN Id END) AS ActiveRunId
FROM dbo.App_QlhvAutoSyncRun;

SELECT
    N'RT03_AUTOSYNC_OPERATION_STATE' AS Evidence,
    COUNT_BIG(*) AS OperationHistoryRows,
    SUM(CASE WHEN Status IN (N'QUEUED', N'RUNNING')
             THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS ActiveOperationRows,
    MAX(CASE WHEN Status = N'SUCCEEDED' THEN Id END) AS LatestSuccessfulOperationId
FROM dbo.App_QlhvSyncOperationHistory;

SELECT
    N'RT03_DIRECT_REALTIME_DURABLE_STATE' AS Evidence,
    expected.TableName,
    CASE WHEN tableItem.object_id IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END
        AS TablePresent,
    COALESCE(SUM(partitionItem.row_count), 0) AS MetadataRowCount
FROM
(
    VALUES
        (N'App_QlhvDirectRealtimeManualReview'),
        (N'App_QlhvDirectRealtimeApplyMarker'),
        (N'App_QlhvDirectRealtimeApplyCheckpoint')
) AS expected(TableName)
LEFT JOIN sys.tables AS tableItem
    ON tableItem.schema_id = SCHEMA_ID(N'dbo')
   AND tableItem.name = expected.TableName
LEFT JOIN sys.dm_db_partition_stats AS partitionItem
    ON partitionItem.object_id = tableItem.object_id
   AND partitionItem.index_id IN (0, 1)
GROUP BY expected.TableName, tableItem.object_id
ORDER BY expected.TableName;

SELECT
    N'RT03_V2_TO_V1_SEPARATE_PIPELINE_STATE' AS Evidence,
    expected.TableName,
    CASE WHEN tableItem.object_id IS NULL THEN CONVERT(bit, 0) ELSE CONVERT(bit, 1) END
        AS TablePresent,
    COALESCE(SUM(partitionItem.row_count), 0) AS MetadataRowCount
FROM
(
    VALUES
        (N'App_CsdtRealtimeStream'),
        (N'App_CsdtRealtimeRun'),
        (N'App_CsdtRealtimeConflict')
) AS expected(TableName)
LEFT JOIN sys.tables AS tableItem
    ON tableItem.schema_id = SCHEMA_ID(N'dbo')
   AND tableItem.name = expected.TableName
LEFT JOIN sys.dm_db_partition_stats AS partitionItem
    ON partitionItem.object_id = tableItem.object_id
   AND partitionItem.index_id IN (0, 1)
GROUP BY expected.TableName, tableItem.object_id
ORDER BY expected.TableName;

SELECT
    N'RT03_TARGET_SESSIONS' AS Evidence,
    COUNT_BIG(*) AS RelatedSessionCount,
    SUM(CASE WHEN requestItem.session_id IS NULL THEN CONVERT(bigint, 0)
             ELSE CONVERT(bigint, 1) END) AS ActiveRequestCount,
    SUM(CASE WHEN requestItem.blocking_session_id > 0 THEN CONVERT(bigint, 1)
             ELSE CONVERT(bigint, 0) END) AS BlockedRequestCount,
    SYSUTCDATETIME() AS ReadAtUtc
FROM sys.dm_exec_sessions AS sessionItem
LEFT JOIN sys.dm_exec_requests AS requestItem
    ON requestItem.session_id = sessionItem.session_id
WHERE sessionItem.database_id = DB_ID();
GO
