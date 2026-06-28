USE [CSDT_V2];
GO

/*
    Task 5 B3W29-B3W31 - Moto CSDT_V1 vs CSDT_V2 schema comparison.

    READ-ONLY ONLY.
    - TEST/local databases only.
    - Do not run on production.
    - Does not INSERT/UPDATE/DELETE/MERGE/EXECUTE against source databases.
    - Reads system catalog metadata for candidate Moto old/new tables.
*/

SET NOCOUNT ON;

IF DB_ID(N'CSDT_V1') IS NULL OR DB_ID(N'CSDT_V2') IS NULL
BEGIN
    SELECT
        CAST(0 AS bit) AS CanRun,
        N'Missing CSDT_V1 or CSDT_V2 database. Stop.' AS Message;
    RETURN;
END;

/* Result set 1: table existence */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLX_GPLX'),
        (N'NguoiLXHS_GiayTo'),
        (N'KhoaHoc_GiaoVien'),
        (N'LichHoc'),
        (N'KhoaHoc_XeTap')
    ) v(TableName)
),
V1Tables AS (
    SELECT t.name AS TableName
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = N'dbo'
),
V2Tables AS (
    SELECT t.name AS TableName
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = N'dbo'
)
SELECT
    c.TableName,
    CAST(CASE WHEN v1.TableName IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1,
    CAST(CASE WHEN v2.TableName IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV2
FROM CandidateTables c
LEFT JOIN V1Tables v1 ON v1.TableName = c.TableName
LEFT JOIN V2Tables v2 ON v2.TableName = c.TableName
ORDER BY c.TableName;

/* Result set 2: column metadata from both databases */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLX_GPLX'),
        (N'NguoiLXHS_GiayTo'),
        (N'KhoaHoc_GiaoVien'),
        (N'LichHoc'),
        (N'KhoaHoc_XeTap')
    ) v(TableName)
),
ColumnsCombined AS (
    SELECT
        N'CSDT_V1' AS DatabaseName,
        t.name AS TableName,
        c.name AS ColumnName,
        ty.name AS DataType,
        CASE
            WHEN c.max_length = -1 THEN -1
            WHEN ty.name IN (N'nchar', N'nvarchar') THEN c.max_length / 2
            ELSE c.max_length
        END AS MaxLength,
        c.precision AS NumericPrecision,
        c.scale AS NumericScale,
        c.is_nullable AS IsNullable,
        c.column_id AS OrdinalPosition
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V1.sys.columns c ON c.object_id = t.object_id
    JOIN CSDT_V1.sys.types ty ON ty.user_type_id = c.user_type_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'

    UNION ALL

    SELECT
        N'CSDT_V2' AS DatabaseName,
        t.name AS TableName,
        c.name AS ColumnName,
        ty.name AS DataType,
        CASE
            WHEN c.max_length = -1 THEN -1
            WHEN ty.name IN (N'nchar', N'nvarchar') THEN c.max_length / 2
            ELSE c.max_length
        END AS MaxLength,
        c.precision AS NumericPrecision,
        c.scale AS NumericScale,
        c.is_nullable AS IsNullable,
        c.column_id AS OrdinalPosition
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.columns c ON c.object_id = t.object_id
    JOIN CSDT_V2.sys.types ty ON ty.user_type_id = c.user_type_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
)
SELECT
    DatabaseName,
    TableName,
    ColumnName,
    DataType,
    MaxLength,
    NumericPrecision,
    NumericScale,
    IsNullable,
    OrdinalPosition
FROM ColumnsCombined
ORDER BY TableName, ColumnName, DatabaseName;

/* Result set 3: columns only in V1 */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLX_GPLX'),
        (N'NguoiLXHS_GiayTo'),
        (N'KhoaHoc_GiaoVien'),
        (N'LichHoc'),
        (N'KhoaHoc_XeTap')
    ) v(TableName)
),
V1Columns AS (
    SELECT t.name AS TableName, c.name AS ColumnName
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V1.sys.columns c ON c.object_id = t.object_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
),
V2Columns AS (
    SELECT t.name AS TableName, c.name AS ColumnName
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.columns c ON c.object_id = t.object_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
)
SELECT v1.TableName, v1.ColumnName
FROM V1Columns v1
LEFT JOIN V2Columns v2
    ON v2.TableName = v1.TableName
   AND v2.ColumnName = v1.ColumnName
WHERE v2.ColumnName IS NULL
ORDER BY v1.TableName, v1.ColumnName;

/* Result set 4: columns only in V2 */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLX_GPLX'),
        (N'NguoiLXHS_GiayTo'),
        (N'KhoaHoc_GiaoVien'),
        (N'LichHoc'),
        (N'KhoaHoc_XeTap')
    ) v(TableName)
),
V1Columns AS (
    SELECT t.name AS TableName, c.name AS ColumnName
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V1.sys.columns c ON c.object_id = t.object_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
),
V2Columns AS (
    SELECT t.name AS TableName, c.name AS ColumnName
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.columns c ON c.object_id = t.object_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
)
SELECT v2.TableName, v2.ColumnName
FROM V2Columns v2
LEFT JOIN V1Columns v1
    ON v1.TableName = v2.TableName
   AND v1.ColumnName = v2.ColumnName
WHERE v1.ColumnName IS NULL
ORDER BY v2.TableName, v2.ColumnName;

/* Result set 5: same-name column differences */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLX_GPLX'),
        (N'NguoiLXHS_GiayTo'),
        (N'KhoaHoc_GiaoVien'),
        (N'LichHoc'),
        (N'KhoaHoc_XeTap')
    ) v(TableName)
),
V1Columns AS (
    SELECT
        t.name AS TableName,
        c.name AS ColumnName,
        ty.name AS DataType,
        CASE
            WHEN c.max_length = -1 THEN -1
            WHEN ty.name IN (N'nchar', N'nvarchar') THEN c.max_length / 2
            ELSE c.max_length
        END AS MaxLength,
        c.precision AS NumericPrecision,
        c.scale AS NumericScale,
        c.is_nullable AS IsNullable
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V1.sys.columns c ON c.object_id = t.object_id
    JOIN CSDT_V1.sys.types ty ON ty.user_type_id = c.user_type_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
),
V2Columns AS (
    SELECT
        t.name AS TableName,
        c.name AS ColumnName,
        ty.name AS DataType,
        CASE
            WHEN c.max_length = -1 THEN -1
            WHEN ty.name IN (N'nchar', N'nvarchar') THEN c.max_length / 2
            ELSE c.max_length
        END AS MaxLength,
        c.precision AS NumericPrecision,
        c.scale AS NumericScale,
        c.is_nullable AS IsNullable
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.columns c ON c.object_id = t.object_id
    JOIN CSDT_V2.sys.types ty ON ty.user_type_id = c.user_type_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
)
SELECT
    v1.TableName,
    v1.ColumnName,
    v1.DataType AS V1DataType,
    v2.DataType AS V2DataType,
    v1.MaxLength AS V1MaxLength,
    v2.MaxLength AS V2MaxLength,
    v1.NumericPrecision AS V1Precision,
    v2.NumericPrecision AS V2Precision,
    v1.NumericScale AS V1Scale,
    v2.NumericScale AS V2Scale,
    v1.IsNullable AS V1IsNullable,
    v2.IsNullable AS V2IsNullable
FROM V1Columns v1
JOIN V2Columns v2
    ON v2.TableName = v1.TableName
   AND v2.ColumnName = v1.ColumnName
WHERE
    v1.DataType <> v2.DataType
    OR v1.MaxLength <> v2.MaxLength
    OR v1.NumericPrecision <> v2.NumericPrecision
    OR v1.NumericScale <> v2.NumericScale
    OR v1.IsNullable <> v2.IsNullable
ORDER BY v1.TableName, v1.ColumnName;

/* Result set 6: PK/UQ/index discovery for key review */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLX_GPLX'),
        (N'NguoiLXHS_GiayTo'),
        (N'KhoaHoc_GiaoVien'),
        (N'LichHoc'),
        (N'KhoaHoc_XeTap')
    ) v(TableName)
),
IndexRows AS (
    SELECT
        N'CSDT_V1' AS DatabaseName,
        t.name AS TableName,
        i.name AS IndexName,
        i.type_desc AS IndexType,
        i.is_primary_key AS IsPrimaryKey,
        i.is_unique AS IsUnique,
        ic.key_ordinal AS KeyOrdinal,
        c.name AS ColumnName
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V1.sys.indexes i ON i.object_id = t.object_id
    JOIN CSDT_V1.sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN CSDT_V1.sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
      AND i.is_hypothetical = 0
      AND ic.key_ordinal > 0

    UNION ALL

    SELECT
        N'CSDT_V2' AS DatabaseName,
        t.name AS TableName,
        i.name AS IndexName,
        i.type_desc AS IndexType,
        i.is_primary_key AS IsPrimaryKey,
        i.is_unique AS IsUnique,
        ic.key_ordinal AS KeyOrdinal,
        c.name AS ColumnName
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.indexes i ON i.object_id = t.object_id
    JOIN CSDT_V2.sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN CSDT_V2.sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
      AND i.is_hypothetical = 0
      AND ic.key_ordinal > 0
)
SELECT
    DatabaseName,
    TableName,
    IndexName,
    IndexType,
    IsPrimaryKey,
    IsUnique,
    KeyOrdinal,
    ColumnName
FROM IndexRows
ORDER BY DatabaseName, TableName, IsPrimaryKey DESC, IsUnique DESC, IndexName, KeyOrdinal;

