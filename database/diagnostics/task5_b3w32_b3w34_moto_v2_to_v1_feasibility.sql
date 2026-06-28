USE [CSDT_V2];
GO

/*
    Task 5 B3W32-B3W34 - Moto CSDT_V2 -> CSDT_V1 feasibility diagnostics.

    READ-ONLY ONLY.
    TEST/local databases only.
    Do not run on production.
    This script returns aggregate diagnostics and sanitized samples only.
    It does not modify permanent data or permanent objects.
*/

SET NOCOUNT ON;

IF DB_ID(N'CSDT_V1') IS NULL OR DB_ID(N'CSDT_V2') IS NULL
BEGIN
    SELECT
        CAST(0 AS bit) AS CanRun,
        N'Missing CSDT_V1 or CSDT_V2 database. Stop.' AS Message;
    RETURN;
END;

/* Result set 1: V2-only NguoiLX_HoSo by course, training rank, status, and received-date range */
;WITH V2OnlyHoSo AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        hs.HangDaoTao,
        hs.TT_XuLy,
        hs.NgayNhanHSo
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    WHERE NOT EXISTS (
        SELECT 1
        FROM CSDT_V1.dbo.NguoiLX_HoSo v1
        WHERE v1.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    )
)
SELECT
    MaKhoaHoc,
    HangDaoTao,
    TT_XuLy,
    COUNT_BIG(*) AS TotalRows,
    MIN(NgayNhanHSo) AS MinNgayNhanHSo,
    MAX(NgayNhanHSo) AS MaxNgayNhanHSo
FROM V2OnlyHoSo
GROUP BY MaKhoaHoc, HangDaoTao, TT_XuLy
ORDER BY TotalRows DESC, MaKhoaHoc, HangDaoTao, TT_XuLy;

/* Result set 2: V2-only NguoiLX summary */
;WITH V2OnlyNguoiLX AS (
    SELECT nlx.MaDK
    FROM CSDT_V2.dbo.NguoiLX nlx
    WHERE NOT EXISTS (
        SELECT 1
        FROM CSDT_V1.dbo.NguoiLX v1
        WHERE v1.MaDK COLLATE DATABASE_DEFAULT = nlx.MaDK COLLATE DATABASE_DEFAULT
    )
),
V2OnlyHoSo AS (
    SELECT hs.MaDK
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    WHERE NOT EXISTS (
        SELECT 1
        FROM CSDT_V1.dbo.NguoiLX_HoSo v1
        WHERE v1.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    )
)
SELECT
    N'V2OnlyNguoiLX_AllByMaDK' AS MetricName,
    COUNT_BIG(*) AS TotalRows
FROM V2OnlyNguoiLX
UNION ALL
SELECT
    N'V2OnlyNguoiLX_WithV2OnlyHoSo',
    COUNT_BIG(*)
FROM V2OnlyNguoiLX nlx
WHERE EXISTS (
    SELECT 1
    FROM V2OnlyHoSo hs
    WHERE hs.MaDK COLLATE DATABASE_DEFAULT = nlx.MaDK COLLATE DATABASE_DEFAULT
)
UNION ALL
SELECT
    N'V2OnlyHoSoRows',
    COUNT_BIG(*)
FROM V2OnlyHoSo;

/* Result set 3: sanitized V2-only NguoiLX sample */
SELECT TOP (50)
    nlx.MaDK,
    CASE
        WHEN nlx.HoVaTen IS NULL OR LTRIM(RTRIM(nlx.HoVaTen)) = N'' THEN N''
        ELSE LEFT(LTRIM(RTRIM(nlx.HoVaTen)), 1) + N'***'
    END AS HoTenPreview,
    nlx.GioiTinh,
    nlx.NgaySinh,
    LEN(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N'')))) AS SoCMTLength,
    CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM CSDT_V2.dbo.NguoiLX_HoSo hs
        WHERE hs.MaDK COLLATE DATABASE_DEFAULT = nlx.MaDK COLLATE DATABASE_DEFAULT
    ) THEN 1 ELSE 0 END AS bit) AS HasV2HoSo
FROM CSDT_V2.dbo.NguoiLX nlx
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V1.dbo.NguoiLX v1
    WHERE v1.MaDK COLLATE DATABASE_DEFAULT = nlx.MaDK COLLATE DATABASE_DEFAULT
)
ORDER BY nlx.MaDK;

/* Result set 4: dependencies for V2-only HoSo */
;WITH V2OnlyHoSo AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        hs.HangDaoTao,
        hs.TT_XuLy,
        hs.NgayNhanHSo
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    WHERE NOT EXISTS (
        SELECT 1
        FROM CSDT_V1.dbo.NguoiLX_HoSo v1
        WHERE v1.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    )
),
DependencyRows AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        hs.HangDaoTao,
        hs.TT_XuLy,
        hs.NgayNhanHSo,
        CAST(CASE WHEN v1nlx.MaDK IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1NguoiLX,
        CAST(CASE WHEN v1kh.MaKH IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1KhoaHoc,
        CAST(CASE WHEN v1bci.MaKH IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1BaoCaoIByCourse,
        gt.V2GiayToRows
    FROM V2OnlyHoSo hs
    LEFT JOIN CSDT_V1.dbo.NguoiLX v1nlx
        ON v1nlx.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    LEFT JOIN CSDT_V1.dbo.KhoaHoc v1kh
        ON v1kh.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
    LEFT JOIN (
        SELECT MaKH
        FROM CSDT_V1.dbo.BaoCaoI
        GROUP BY MaKH
    ) v1bci
        ON v1bci.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
    OUTER APPLY (
        SELECT COUNT_BIG(*) AS V2GiayToRows
        FROM CSDT_V2.dbo.NguoiLXHS_GiayTo gt
        WHERE gt.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    ) gt
)
SELECT
    COUNT_BIG(*) AS TotalV2OnlyHoSo,
    SUM(CASE WHEN ExistsInV1NguoiLX = 1 THEN 1 ELSE 0 END) AS HasV1NguoiLX,
    SUM(CASE WHEN ExistsInV1NguoiLX = 0 THEN 1 ELSE 0 END) AS MissingV1NguoiLX,
    SUM(CASE WHEN ExistsInV1KhoaHoc = 1 THEN 1 ELSE 0 END) AS HasV1KhoaHoc,
    SUM(CASE WHEN ExistsInV1KhoaHoc = 0 THEN 1 ELSE 0 END) AS MissingV1KhoaHoc,
    SUM(CASE WHEN ExistsInV1BaoCaoIByCourse = 1 THEN 1 ELSE 0 END) AS HasV1BaoCaoIByCourse,
    SUM(CASE WHEN ExistsInV1BaoCaoIByCourse = 0 THEN 1 ELSE 0 END) AS MissingV1BaoCaoIByCourse,
    SUM(CASE WHEN V2GiayToRows > 0 THEN 1 ELSE 0 END) AS HoSoWithV2GiayTo,
    SUM(V2GiayToRows) AS TotalV2GiayToRowsForV2OnlyHoSo
FROM DependencyRows;

/* Result set 5: dependencies grouped by course, rank, and status */
;WITH V2OnlyHoSo AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        hs.HangDaoTao,
        hs.TT_XuLy,
        hs.NgayNhanHSo
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    WHERE NOT EXISTS (
        SELECT 1
        FROM CSDT_V1.dbo.NguoiLX_HoSo v1
        WHERE v1.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    )
),
DependencyRows AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        hs.HangDaoTao,
        hs.TT_XuLy,
        hs.NgayNhanHSo,
        CAST(CASE WHEN v1nlx.MaDK IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1NguoiLX,
        CAST(CASE WHEN v1kh.MaKH IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1KhoaHoc,
        CAST(CASE WHEN v1bci.MaKH IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1BaoCaoIByCourse,
        gt.V2GiayToRows
    FROM V2OnlyHoSo hs
    LEFT JOIN CSDT_V1.dbo.NguoiLX v1nlx
        ON v1nlx.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    LEFT JOIN CSDT_V1.dbo.KhoaHoc v1kh
        ON v1kh.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
    LEFT JOIN (
        SELECT MaKH
        FROM CSDT_V1.dbo.BaoCaoI
        GROUP BY MaKH
    ) v1bci
        ON v1bci.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
    OUTER APPLY (
        SELECT COUNT_BIG(*) AS V2GiayToRows
        FROM CSDT_V2.dbo.NguoiLXHS_GiayTo gt
        WHERE gt.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    ) gt
)
SELECT
    MaKhoaHoc,
    HangDaoTao,
    TT_XuLy,
    COUNT_BIG(*) AS TotalV2OnlyHoSo,
    MIN(NgayNhanHSo) AS MinNgayNhanHSo,
    MAX(NgayNhanHSo) AS MaxNgayNhanHSo,
    SUM(CASE WHEN ExistsInV1NguoiLX = 0 THEN 1 ELSE 0 END) AS MissingV1NguoiLX,
    SUM(CASE WHEN ExistsInV1KhoaHoc = 0 THEN 1 ELSE 0 END) AS MissingV1KhoaHoc,
    SUM(CASE WHEN ExistsInV1BaoCaoIByCourse = 0 THEN 1 ELSE 0 END) AS MissingV1BaoCaoIByCourse,
    SUM(V2GiayToRows) AS V2GiayToRows
FROM DependencyRows
GROUP BY MaKhoaHoc, HangDaoTao, TT_XuLy
ORDER BY TotalV2OnlyHoSo DESC, MaKhoaHoc, HangDaoTao, TT_XuLy;

/* Result set 6: field compatibility - same-name columns */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLXHS_GiayTo')
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
        c.is_nullable AS IsNullable,
        c.column_id AS OrdinalPosition
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
    v2.TableName,
    v2.ColumnName,
    v1.DataType AS V1DataType,
    v2.DataType AS V2DataType,
    v1.MaxLength AS V1MaxLength,
    v2.MaxLength AS V2MaxLength,
    v1.IsNullable AS V1IsNullable,
    v2.IsNullable AS V2IsNullable,
    v1.OrdinalPosition AS V1OrdinalPosition,
    v2.OrdinalPosition AS V2OrdinalPosition
FROM V2Columns v2
JOIN V1Columns v1
    ON v1.TableName = v2.TableName
   AND v1.ColumnName = v2.ColumnName
ORDER BY v2.TableName, v2.OrdinalPosition;

/* Result set 7: V2 columns with greater declared length than V1 */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLXHS_GiayTo')
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
        END AS MaxLength
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
        END AS MaxLength
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.columns c ON c.object_id = t.object_id
    JOIN CSDT_V2.sys.types ty ON ty.user_type_id = c.user_type_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
)
SELECT
    v2.TableName,
    v2.ColumnName,
    v1.DataType AS V1DataType,
    v2.DataType AS V2DataType,
    v1.MaxLength AS V1MaxLength,
    v2.MaxLength AS V2MaxLength,
    N'V2 length is greater than V1; review truncation risk before any write plan.' AS RiskNote
FROM V2Columns v2
JOIN V1Columns v1
    ON v1.TableName = v2.TableName
   AND v1.ColumnName = v2.ColumnName
WHERE v1.MaxLength <> -1
  AND v2.MaxLength <> -1
  AND v2.MaxLength > v1.MaxLength
ORDER BY v2.TableName, v2.ColumnName;

/* Result set 8: fields present only in V2 */
;WITH CandidateTables AS (
    SELECT TableName
    FROM (VALUES
        (N'KhoaHoc'),
        (N'BaoCaoI'),
        (N'NguoiLX'),
        (N'NguoiLX_HoSo'),
        (N'NguoiLXHS_GiayTo')
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
    SELECT
        t.name AS TableName,
        c.name AS ColumnName,
        ty.name AS DataType,
        CASE
            WHEN c.max_length = -1 THEN -1
            WHEN ty.name IN (N'nchar', N'nvarchar') THEN c.max_length / 2
            ELSE c.max_length
        END AS MaxLength,
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
    v2.TableName,
    v2.ColumnName,
    v2.DataType,
    v2.MaxLength,
    v2.IsNullable,
    N'V2-only field cannot be written into V1 without an explicit mapping decision.' AS RiskNote
FROM V2Columns v2
LEFT JOIN V1Columns v1
    ON v1.TableName = v2.TableName
   AND v1.ColumnName = v2.ColumnName
WHERE v1.ColumnName IS NULL
ORDER BY v2.TableName, v2.OrdinalPosition;

/* Result set 9: read-only add/review counts for a possible V2 -> V1 plan */
;WITH V2OnlyHoSo AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    WHERE NOT EXISTS (
        SELECT 1
        FROM CSDT_V1.dbo.NguoiLX_HoSo v1
        WHERE v1.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
    )
)
SELECT
    N'NguoiLX would-add to V1 - all V2 MaDK missing in V1' AS PlanMetric,
    COUNT_BIG(*) AS TotalRows
FROM CSDT_V2.dbo.NguoiLX v2
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V1.dbo.NguoiLX v1
    WHERE v1.MaDK COLLATE DATABASE_DEFAULT = v2.MaDK COLLATE DATABASE_DEFAULT
)
UNION ALL
SELECT
    N'NguoiLX would-add to V1 - only V2-only HoSo driver',
    COUNT_BIG(*)
FROM V2OnlyHoSo hs
JOIN CSDT_V2.dbo.NguoiLX v2
    ON v2.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V1.dbo.NguoiLX v1
    WHERE v1.MaDK COLLATE DATABASE_DEFAULT = v2.MaDK COLLATE DATABASE_DEFAULT
)
UNION ALL
SELECT
    N'NguoiLX_HoSo would-add to V1 by MaDK',
    COUNT_BIG(*)
FROM V2OnlyHoSo
UNION ALL
SELECT
    N'NguoiLXHS_GiayTo would-add to V1 by MaGT + MaDK for V2-only HoSo',
    COUNT_BIG(*)
FROM CSDT_V2.dbo.NguoiLXHS_GiayTo v2gt
JOIN V2OnlyHoSo hs
    ON hs.MaDK COLLATE DATABASE_DEFAULT = v2gt.MaDK COLLATE DATABASE_DEFAULT
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V1.dbo.NguoiLXHS_GiayTo v1gt
    WHERE v1gt.MaDK COLLATE DATABASE_DEFAULT = v2gt.MaDK COLLATE DATABASE_DEFAULT
      AND v1gt.MaGT = v2gt.MaGT
)
UNION ALL
SELECT
    N'KhoaHoc would-add to V1 for V2-only HoSo courses',
    COUNT_BIG(*)
FROM (
    SELECT DISTINCT hs.MaKhoaHoc
    FROM V2OnlyHoSo hs
    WHERE hs.MaKhoaHoc IS NOT NULL
      AND LTRIM(RTRIM(hs.MaKhoaHoc)) <> N''
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V1.dbo.KhoaHoc v1kh
          WHERE v1kh.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
      )
) x
UNION ALL
SELECT
    N'BaoCaoI missing in V1 by MaKH for V2-only HoSo courses',
    COUNT_BIG(*)
FROM (
    SELECT DISTINCT hs.MaKhoaHoc
    FROM V2OnlyHoSo hs
    WHERE hs.MaKhoaHoc IS NOT NULL
      AND LTRIM(RTRIM(hs.MaKhoaHoc)) <> N''
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V1.dbo.BaoCaoI v1bci
          WHERE v1bci.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
      )
) x;

/* Result set 10: same-key rows that would need change review, not automatic write */
SELECT
    N'NguoiLX same MaDK field-difference review' AS ReviewMetric,
    COUNT_BIG(*) AS TotalRows
FROM CSDT_V2.dbo.NguoiLX v2
JOIN CSDT_V1.dbo.NguoiLX v1
    ON v1.MaDK COLLATE DATABASE_DEFAULT = v2.MaDK COLLATE DATABASE_DEFAULT
WHERE ISNULL(v1.HoVaTen, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.HoVaTen, N'') COLLATE DATABASE_DEFAULT
   OR ISNULL(v1.GioiTinh, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.GioiTinh, N'') COLLATE DATABASE_DEFAULT
   OR ISNULL(v1.NgaySinh, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.NgaySinh, N'') COLLATE DATABASE_DEFAULT
UNION ALL
SELECT
    N'NguoiLX_HoSo same MaDK field-difference review',
    COUNT_BIG(*)
FROM CSDT_V2.dbo.NguoiLX_HoSo v2
JOIN CSDT_V1.dbo.NguoiLX_HoSo v1
    ON v1.MaDK COLLATE DATABASE_DEFAULT = v2.MaDK COLLATE DATABASE_DEFAULT
WHERE ISNULL(v1.MaKhoaHoc, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.MaKhoaHoc, N'') COLLATE DATABASE_DEFAULT
   OR ISNULL(v1.HangDaoTao, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.HangDaoTao, N'') COLLATE DATABASE_DEFAULT
   OR ISNULL(v1.TT_XuLy, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.TT_XuLy, N'') COLLATE DATABASE_DEFAULT
   OR ISNULL(v1.SoGiayCNTN, N'') COLLATE DATABASE_DEFAULT <> ISNULL(v2.SoGiayCNTN, N'') COLLATE DATABASE_DEFAULT;
