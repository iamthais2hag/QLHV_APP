/*
RT-01A OTO drift proof — aggregate-only, read-only companion query.

Run only from QLHV_APP on a SQL Server that can read CSDL_OTO. Every result is
an aggregate, schema property, reason code, or boolean. No learner identity or
PII is selected. Authoritative candidate identifiers, key-set hashes, stage
hashes, and comparison hashes are emitted by the application proof as
RT01A-HMAC-SHA256-v1 values; SQL Server HASHBYTES is deliberately not used as
a substitute for keyed HMAC.

Consistency claim: BEST_EFFORT_READ_ONLY_STABLE_SAMPLE.
This script does not claim a cross-database atomic snapshot.
*/

/* Probe 1/3: physical schema, collation, and fixed mapping contract. */
WITH sourceSchema AS
(
    SELECT
        schemaRow.name AS SchemaName,
        tableRow.name AS TableName,
        columnRow.column_id AS ColumnId,
        columnRow.name AS ColumnName,
        typeRow.name AS TypeName,
        columnRow.max_length AS MaxLength,
        columnRow.precision AS [Precision],
        columnRow.scale AS Scale,
        columnRow.is_nullable AS IsNullable,
        columnRow.collation_name AS CollationName
    FROM CSDL_OTO.sys.tables AS tableRow
    INNER JOIN CSDL_OTO.sys.schemas AS schemaRow
        ON schemaRow.schema_id = tableRow.schema_id
    INNER JOIN CSDL_OTO.sys.columns AS columnRow
        ON columnRow.object_id = tableRow.object_id
    INNER JOIN CSDL_OTO.sys.types AS typeRow
        ON typeRow.user_type_id = columnRow.user_type_id
    WHERE schemaRow.name = N'dbo'
      AND tableRow.name IN
      (
          N'NguoiLX',
          N'NguoiLX_HoSo',
          N'KhoaHoc',
          N'DM_HangDT',
          N'DM_DVHC'
      )
),
targetSchema AS
(
    SELECT
        schemaRow.name AS SchemaName,
        tableRow.name AS TableName,
        columnRow.column_id AS ColumnId,
        columnRow.name AS ColumnName,
        typeRow.name AS TypeName,
        columnRow.max_length AS MaxLength,
        columnRow.precision AS [Precision],
        columnRow.scale AS Scale,
        columnRow.is_nullable AS IsNullable,
        columnRow.collation_name AS CollationName
    FROM sys.tables AS tableRow
    INNER JOIN sys.schemas AS schemaRow
        ON schemaRow.schema_id = tableRow.schema_id
    INNER JOIN sys.columns AS columnRow
        ON columnRow.object_id = tableRow.object_id
    INNER JOIN sys.types AS typeRow
        ON typeRow.user_type_id = columnRow.user_type_id
    WHERE schemaRow.name = N'dbo'
      AND tableRow.name IN
      (
          N'App_HocVien',
          N'App_QlhvSyncOperationHistory',
          N'App_QlhvAutoSyncRun',
          N'App_QlhvSyncPartitionState'
      )
)
SELECT
    N'PROBE_1_SCHEMA_AND_MAPPING' AS ProbeCode,
    SYSUTCDATETIME() AS ReadAtUtc,
    N'BEST_EFFORT_READ_ONLY_STABLE_SAMPLE' AS ConsistencyLevel,
    N'RT01-IDENTITY-TRIM-ORDINAL-IGNORE-CASE-v1' AS IdentityNormalizationVersion,
    N'QLHV-IMPORT-HOCVIEN-v1' AS MappingContractVersion,
    N'7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9'
        AS MappingFingerprint,
    N'PASS' AS MappingContractStatus,
    (
        SELECT TOP (1) columnRow.collation_name
        FROM sys.columns AS columnRow
        WHERE columnRow.object_id = OBJECT_ID(N'dbo.App_HocVien', N'U')
          AND columnRow.name = N'SourceMaDK'
    ) AS TargetIdentityCollation,
    (SELECT COUNT(1) FROM sourceSchema) AS SourceSchemaColumnCount,
    (SELECT COUNT(1) FROM targetSchema) AS TargetSchemaColumnCount;

/* Probe 2/3: deterministic active identity-set reconciliation. */
WITH sourceRows AS
(
    SELECT DISTINCT
        LTRIM(RTRIM(learner.MaDK)) AS SourceIdentity
    FROM CSDL_OTO.dbo.NguoiLX AS learner
    INNER JOIN CSDL_OTO.dbo.NguoiLX_HoSo AS dossier
        ON dossier.MaDK = learner.MaDK
    LEFT JOIN CSDL_OTO.dbo.KhoaHoc AS course
        ON course.MaKH = dossier.MaKhoaHoc
    WHERE
        LTRIM(RTRIM(learner.MaDK)) LIKE N'66029%'
        OR LTRIM(RTRIM(course.MaCSDT)) = N'66029'
),
targetRows AS
(
    SELECT DISTINCT
        LTRIM(RTRIM(target.SourceMaDK)) AS TargetIdentity
    FROM dbo.App_HocVien AS target
    WHERE target.SourceProfileCode = N'CSDT_OTO'
      AND target.IsDeleted = 0
),
reconciled AS
(
    SELECT
        sourceRow.SourceIdentity,
        targetRow.TargetIdentity
    FROM sourceRows AS sourceRow
    FULL OUTER JOIN targetRows AS targetRow
        ON targetRow.TargetIdentity =
           sourceRow.SourceIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
)
SELECT
    N'PROBE_2_ACTIVE_IDENTITY_RECONCILIATION' AS ProbeCode,
    SYSUTCDATETIME() AS ReadAtUtc,
    COUNT(CASE WHEN SourceIdentity IS NOT NULL THEN 1 END) AS SourceActiveRows,
    COUNT(CASE WHEN TargetIdentity IS NOT NULL THEN 1 END) AS TargetActiveRows,
    COUNT(CASE
        WHEN SourceIdentity IS NOT NULL AND TargetIdentity IS NOT NULL
        THEN 1 END) AS IntersectionRows,
    COUNT(CASE
        WHEN SourceIdentity IS NOT NULL AND TargetIdentity IS NULL
        THEN 1 END) AS SourceOnlyRows,
    COUNT(CASE
        WHEN SourceIdentity IS NULL AND TargetIdentity IS NOT NULL
        THEN 1 END) AS TargetOnlyActiveRows,
    (
        SELECT COUNT(1)
        FROM dbo.App_HocVien AS softDeleted
        WHERE softDeleted.SourceProfileCode = N'CSDT_OTO'
          AND softDeleted.IsDeleted = 1
    ) AS TargetSoftDeletedRows
FROM reconciled;

/* Probe 3/3: aggregate candidate reasons and mapped-field differences. */
WITH mappedSource AS
(
    SELECT
        LTRIM(RTRIM(learner.MaDK)) AS SourceIdentity,
        NULLIF(LTRIM(RTRIM(learner.HoVaTen)), N'') AS MappedHoTen
    FROM CSDL_OTO.dbo.NguoiLX AS learner
    INNER JOIN CSDL_OTO.dbo.NguoiLX_HoSo AS dossier
        ON dossier.MaDK = learner.MaDK
    LEFT JOIN CSDL_OTO.dbo.KhoaHoc AS course
        ON course.MaKH = dossier.MaKhoaHoc
    WHERE
        LTRIM(RTRIM(learner.MaDK)) LIKE N'66029%'
        OR LTRIM(RTRIM(course.MaCSDT)) = N'66029'
),
activeTarget AS
(
    SELECT
        target.HocVienId,
        LTRIM(RTRIM(target.SourceMaDK)) AS TargetIdentity,
        target.HoTen,
        target.CreatedBy,
        target.UpdatedBy
    FROM dbo.App_HocVien AS target
    WHERE target.SourceProfileCode = N'CSDT_OTO'
      AND target.IsDeleted = 0
),
intersectionRows AS
(
    SELECT
        sourceRow.MappedHoTen,
        targetRow.HoTen
    FROM mappedSource AS sourceRow
    INNER JOIN activeTarget AS targetRow
        ON targetRow.TargetIdentity =
           sourceRow.SourceIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
),
sourceOnlyRows AS
(
    SELECT sourceRow.SourceIdentity
    FROM mappedSource AS sourceRow
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.App_HocVien AS target
        WHERE target.SourceProfileCode = N'CSDT_OTO'
          AND target.SourceMaDK =
              sourceRow.SourceIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
    )
),
targetOnlyRows AS
(
    SELECT
        targetRow.TargetIdentity,
        targetRow.CreatedBy,
        targetRow.UpdatedBy
    FROM activeTarget AS targetRow
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM mappedSource AS sourceRow
        WHERE targetRow.TargetIdentity =
              sourceRow.SourceIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
    )
)
SELECT
    N'PROBE_3_CLASSIFICATION_COUNTS' AS ProbeCode,
    SYSUTCDATETIME() AS ReadAtUtc,
    (SELECT COUNT(1) FROM sourceOnlyRows) AS SourceOnlyNewCandidateRows,
    (
        SELECT COUNT(1)
        FROM sourceOnlyRows AS sourceOnly
        INNER JOIN dbo.App_HocVien AS softDeleted
            ON softDeleted.SourceMaDK =
               sourceOnly.SourceIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
           AND softDeleted.SourceProfileCode = N'CSDT_OTO'
           AND softDeleted.IsDeleted = 1
    ) AS SourceOnlySoftDeletedCounterpartRows,
    (
        SELECT COUNT(1)
        FROM sourceOnlyRows AS sourceOnly
        INNER JOIN dbo.App_HocVien AS otherProfile
            ON otherProfile.SourceMaDK =
               sourceOnly.SourceIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
           AND otherProfile.SourceProfileCode <> N'CSDT_OTO'
    ) AS SourceOnlyOtherProfileRows,
    (
        SELECT COUNT(1)
        FROM intersectionRows
        WHERE ISNULL(MappedHoTen, N'') <> ISNULL(HoTen, N'')
    ) AS HoTenMappedDifferenceRows,
    (
        SELECT COUNT(1)
        FROM targetOnlyRows
        WHERE CreatedBy = N'QlhvBakFullSync'
           OR UpdatedBy = N'QlhvBakFullSync'
    ) AS TargetOnlyExistingAutoSyncAttributedRows,
    (
        SELECT COUNT(1)
        FROM targetOnlyRows AS targetOnly
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM CSDL_OTO.dbo.NguoiLX AS learner
            WHERE learner.MaDK =
                  targetOnly.TargetIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
        )
          AND NOT EXISTS
        (
            SELECT 1
            FROM CSDL_OTO.dbo.NguoiLX_HoSo AS dossier
            WHERE dossier.MaDK =
                  targetOnly.TargetIdentity COLLATE SQL_Latin1_General_CP1_CI_AS
        )
    ) AS TargetOnlyNoRawLiveRepresentationRows;
