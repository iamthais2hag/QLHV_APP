USE [CSDT_V2];
GO

/*
    Task 5 B3W29-B3W31 - Moto CSDT_V1 vs CSDT_V2 row/key diagnostics.

    READ-ONLY ONLY.
    - TEST/local databases only.
    - Do not run on production.
    - Does not INSERT/UPDATE/DELETE/MERGE/EXECUTE against source databases.
    - Uses local #temp results only for reporting.
*/

SET NOCOUNT ON;

IF DB_ID(N'CSDT_V1') IS NULL OR DB_ID(N'CSDT_V2') IS NULL
BEGIN
    SELECT
        CAST(0 AS bit) AS CanRun,
        N'Missing CSDT_V1 or CSDT_V2 database. Stop.' AS Message;
    RETURN;
END;

/* Result set 1: row counts by candidate table, using metadata only */
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
V1Counts AS (
    SELECT
        t.name AS TableName,
        SUM(ps.rows) AS TotalRows
    FROM CSDT_V1.sys.tables t
    JOIN CSDT_V1.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V1.sys.partitions ps ON ps.object_id = t.object_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
      AND ps.index_id IN (0, 1)
    GROUP BY t.name
),
V2Counts AS (
    SELECT
        t.name AS TableName,
        SUM(ps.rows) AS TotalRows
    FROM CSDT_V2.sys.tables t
    JOIN CSDT_V2.sys.schemas s ON s.schema_id = t.schema_id
    JOIN CSDT_V2.sys.partitions ps ON ps.object_id = t.object_id
    JOIN CandidateTables ct ON ct.TableName = t.name
    WHERE s.name = N'dbo'
      AND ps.index_id IN (0, 1)
    GROUP BY t.name
)
SELECT
    ct.TableName,
    CAST(CASE WHEN v1.TotalRows IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV1,
    CAST(CASE WHEN v2.TotalRows IS NULL THEN 0 ELSE 1 END AS bit) AS ExistsInV2,
    v1.TotalRows AS V1RowCount,
    v2.TotalRows AS V2RowCount
FROM CandidateTables ct
LEFT JOIN V1Counts v1 ON v1.TableName = ct.TableName
LEFT JOIN V2Counts v2 ON v2.TableName = ct.TableName
ORDER BY ct.TableName;

CREATE TABLE #KeyDiagnostics (
    TableName sysname NOT NULL,
    KeyName nvarchar(200) NOT NULL,
    KeyConfidence nvarchar(30) NOT NULL,
    V1Exists bit NOT NULL,
    V2Exists bit NOT NULL,
    V1MissingKeyRows bigint NULL,
    V2MissingKeyRows bigint NULL,
    V1DuplicateKeyGroups bigint NULL,
    V2DuplicateKeyGroups bigint NULL,
    V1DuplicateRows bigint NULL,
    V2DuplicateRows bigint NULL,
    OverlapKeyCount bigint NULL,
    Notes nvarchar(1000) NULL
);

/* KhoaHoc: MaKH */
IF OBJECT_ID(N'CSDT_V1.dbo.KhoaHoc', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.KhoaHoc', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'KhoaHoc',
        N'MaKH',
        N'High',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.KhoaHoc WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.KhoaHoc WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N''),
        (SELECT COUNT_BIG(*) FROM (SELECT MaKH FROM CSDT_V1.dbo.KhoaHoc WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'' GROUP BY MaKH HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (SELECT MaKH FROM CSDT_V2.dbo.KhoaHoc WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'' GROUP BY MaKH HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V1.dbo.KhoaHoc WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'' GROUP BY MaKH HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V2.dbo.KhoaHoc WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'' GROUP BY MaKH HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaKH
            FROM CSDT_V1.dbo.KhoaHoc v1
            JOIN CSDT_V2.dbo.KhoaHoc v2 ON v2.MaKH COLLATE DATABASE_DEFAULT = v1.MaKH COLLATE DATABASE_DEFAULT
        ) x),
        N'PK in both schema references.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'KhoaHoc', N'MaKH', N'High',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.KhoaHoc', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.KhoaHoc', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* BaoCaoI: MaBCI */
IF OBJECT_ID(N'CSDT_V1.dbo.BaoCaoI', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.BaoCaoI', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'BaoCaoI',
        N'MaBCI',
        N'High',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.BaoCaoI WHERE MaBCI IS NULL OR LTRIM(RTRIM(MaBCI)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.BaoCaoI WHERE MaBCI IS NULL OR LTRIM(RTRIM(MaBCI)) = N''),
        (SELECT COUNT_BIG(*) FROM (SELECT MaBCI FROM CSDT_V1.dbo.BaoCaoI WHERE MaBCI IS NOT NULL AND LTRIM(RTRIM(MaBCI)) <> N'' GROUP BY MaBCI HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (SELECT MaBCI FROM CSDT_V2.dbo.BaoCaoI WHERE MaBCI IS NOT NULL AND LTRIM(RTRIM(MaBCI)) <> N'' GROUP BY MaBCI HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V1.dbo.BaoCaoI WHERE MaBCI IS NOT NULL AND LTRIM(RTRIM(MaBCI)) <> N'' GROUP BY MaBCI HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V2.dbo.BaoCaoI WHERE MaBCI IS NOT NULL AND LTRIM(RTRIM(MaBCI)) <> N'' GROUP BY MaBCI HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaBCI
            FROM CSDT_V1.dbo.BaoCaoI v1
            JOIN CSDT_V2.dbo.BaoCaoI v2 ON v2.MaBCI COLLATE DATABASE_DEFAULT = v1.MaBCI COLLATE DATABASE_DEFAULT
        ) x),
        N'PK in both schema references. SoBaoCao uniqueness should be reviewed separately if business uses it.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'BaoCaoI', N'MaBCI', N'High',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.BaoCaoI', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.BaoCaoI', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* NguoiLX: MaDK */
IF OBJECT_ID(N'CSDT_V1.dbo.NguoiLX', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.NguoiLX', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'NguoiLX',
        N'MaDK',
        N'High',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.NguoiLX WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM (SELECT MaDK FROM CSDT_V1.dbo.NguoiLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (SELECT MaDK FROM CSDT_V2.dbo.NguoiLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V1.dbo.NguoiLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V2.dbo.NguoiLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaDK
            FROM CSDT_V1.dbo.NguoiLX v1
            JOIN CSDT_V2.dbo.NguoiLX v2 ON v2.MaDK COLLATE DATABASE_DEFAULT = v1.MaDK COLLATE DATABASE_DEFAULT
        ) x),
        N'PK in both schema references. Same CCCD with different MaDK needs separate conflict review.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'NguoiLX', N'MaDK', N'High',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.NguoiLX', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.NguoiLX', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* NguoiLX_HoSo: MaDK. SoHoSo exists but PK is MaDK in references. */
IF OBJECT_ID(N'CSDT_V1.dbo.NguoiLX_HoSo', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.NguoiLX_HoSo', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'NguoiLX_HoSo',
        N'MaDK',
        N'High',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.NguoiLX_HoSo WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX_HoSo WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM (SELECT MaDK FROM CSDT_V1.dbo.NguoiLX_HoSo WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (SELECT MaDK FROM CSDT_V2.dbo.NguoiLX_HoSo WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V1.dbo.NguoiLX_HoSo WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V2.dbo.NguoiLX_HoSo WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaDK
            FROM CSDT_V1.dbo.NguoiLX_HoSo v1
            JOIN CSDT_V2.dbo.NguoiLX_HoSo v2 ON v2.MaDK COLLATE DATABASE_DEFAULT = v1.MaDK COLLATE DATABASE_DEFAULT
        ) x),
        N'PK/unique by MaDK in references. SoHoSo should be compared but is not the primary sync key yet.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'NguoiLX_HoSo', N'MaDK', N'High',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.NguoiLX_HoSo', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.NguoiLX_HoSo', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* NguoiLX_GPLX: schema PK is MaDK, but old transfer script uses MaDK + SoGPLX + HangGPLX. */
IF OBJECT_ID(N'CSDT_V1.dbo.NguoiLX_GPLX', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.NguoiLX_GPLX', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'NguoiLX_GPLX',
        N'MaDK',
        N'Medium',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.NguoiLX_GPLX WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX_GPLX WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM (SELECT MaDK FROM CSDT_V1.dbo.NguoiLX_GPLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (SELECT MaDK FROM CSDT_V2.dbo.NguoiLX_GPLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V1.dbo.NguoiLX_GPLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT ISNULL(SUM(Cnt), 0) FROM (SELECT COUNT_BIG(*) AS Cnt FROM CSDT_V2.dbo.NguoiLX_GPLX WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaDK
            FROM CSDT_V1.dbo.NguoiLX_GPLX v1
            JOIN CSDT_V2.dbo.NguoiLX_GPLX v2 ON v2.MaDK COLLATE DATABASE_DEFAULT = v1.MaDK COLLATE DATABASE_DEFAULT
        ) x),
        N'Schema PK is MaDK, but business transfer may need MaDK + SoGPLX + HangGPLX review.';

    INSERT INTO #KeyDiagnostics
    SELECT
        N'NguoiLX_GPLX',
        N'MaDK + SoGPLX + HangGPLX',
        N'Uncertain',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.NguoiLX_GPLX WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N'' OR SoGPLX IS NULL OR LTRIM(RTRIM(SoGPLX)) = N'' OR HangGPLX IS NULL OR LTRIM(RTRIM(HangGPLX)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX_GPLX WHERE MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N'' OR SoGPLX IS NULL OR LTRIM(RTRIM(SoGPLX)) = N'' OR HangGPLX IS NULL OR LTRIM(RTRIM(HangGPLX)) = N''),
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaDK, SoGPLX, HangGPLX
            FROM CSDT_V1.dbo.NguoiLX_GPLX
            WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N''
              AND SoGPLX IS NOT NULL AND LTRIM(RTRIM(SoGPLX)) <> N''
              AND HangGPLX IS NOT NULL AND LTRIM(RTRIM(HangGPLX)) <> N''
            GROUP BY MaDK, SoGPLX, HangGPLX
            HAVING COUNT_BIG(*) > 1
        ) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaDK, SoGPLX, HangGPLX
            FROM CSDT_V2.dbo.NguoiLX_GPLX
            WHERE MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N''
              AND SoGPLX IS NOT NULL AND LTRIM(RTRIM(SoGPLX)) <> N''
              AND HangGPLX IS NOT NULL AND LTRIM(RTRIM(HangGPLX)) <> N''
            GROUP BY MaDK, SoGPLX, HangGPLX
            HAVING COUNT_BIG(*) > 1
        ) d),
        NULL,
        NULL,
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaDK, v1.SoGPLX, v1.HangGPLX
            FROM CSDT_V1.dbo.NguoiLX_GPLX v1
            JOIN CSDT_V2.dbo.NguoiLX_GPLX v2
              ON v2.MaDK COLLATE DATABASE_DEFAULT = v1.MaDK COLLATE DATABASE_DEFAULT
             AND v2.SoGPLX COLLATE DATABASE_DEFAULT = v1.SoGPLX COLLATE DATABASE_DEFAULT
             AND v2.HangGPLX COLLATE DATABASE_DEFAULT = v1.HangGPLX COLLATE DATABASE_DEFAULT
        ) x),
        N'Functional key used by existing V1->V2 script; requires owner confirmation.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'NguoiLX_GPLX', N'MaDK / MaDK + SoGPLX + HangGPLX', N'Uncertain',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.NguoiLX_GPLX', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.NguoiLX_GPLX', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* NguoiLXHS_GiayTo: MaGT + MaDK */
IF OBJECT_ID(N'CSDT_V1.dbo.NguoiLXHS_GiayTo', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.NguoiLXHS_GiayTo', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'NguoiLXHS_GiayTo',
        N'MaGT + MaDK',
        N'High',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.NguoiLXHS_GiayTo WHERE MaGT IS NULL OR MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLXHS_GiayTo WHERE MaGT IS NULL OR MaDK IS NULL OR LTRIM(RTRIM(MaDK)) = N''),
        (SELECT COUNT_BIG(*) FROM (SELECT MaGT, MaDK FROM CSDT_V1.dbo.NguoiLXHS_GiayTo WHERE MaGT IS NOT NULL AND MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaGT, MaDK HAVING COUNT_BIG(*) > 1) d),
        (SELECT COUNT_BIG(*) FROM (SELECT MaGT, MaDK FROM CSDT_V2.dbo.NguoiLXHS_GiayTo WHERE MaGT IS NOT NULL AND MaDK IS NOT NULL AND LTRIM(RTRIM(MaDK)) <> N'' GROUP BY MaGT, MaDK HAVING COUNT_BIG(*) > 1) d),
        NULL,
        NULL,
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaGT, v1.MaDK
            FROM CSDT_V1.dbo.NguoiLXHS_GiayTo v1
            JOIN CSDT_V2.dbo.NguoiLXHS_GiayTo v2
              ON v2.MaGT = v1.MaGT
             AND v2.MaDK COLLATE DATABASE_DEFAULT = v1.MaDK COLLATE DATABASE_DEFAULT
        ) x),
        N'Composite PK in both schema references.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'NguoiLXHS_GiayTo', N'MaGT + MaDK', N'High',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.NguoiLXHS_GiayTo', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.NguoiLXHS_GiayTo', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* KhoaHoc_GiaoVien: PK differs. Use functional key from existing transfer script. */
IF OBJECT_ID(N'CSDT_V1.dbo.KhoaHoc_GiaoVien', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.KhoaHoc_GiaoVien', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'KhoaHoc_GiaoVien',
        N'MaKH + MaGV + LoaiGV + BienSoXe',
        N'Uncertain',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.KhoaHoc_GiaoVien WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N'' OR MaGV IS NULL OR LTRIM(RTRIM(MaGV)) = N'' OR LoaiGV IS NULL OR LTRIM(RTRIM(LoaiGV)) = N''),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.KhoaHoc_GiaoVien WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N'' OR MaGV IS NULL OR LTRIM(RTRIM(MaGV)) = N'' OR LoaiGV IS NULL OR LTRIM(RTRIM(LoaiGV)) = N''),
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaKH, MaGV, LoaiGV, ISNULL(BienSoXe, N'') AS BienSoXeKey
            FROM CSDT_V1.dbo.KhoaHoc_GiaoVien
            WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N''
              AND MaGV IS NOT NULL AND LTRIM(RTRIM(MaGV)) <> N''
              AND LoaiGV IS NOT NULL AND LTRIM(RTRIM(LoaiGV)) <> N''
            GROUP BY MaKH, MaGV, LoaiGV, ISNULL(BienSoXe, N'')
            HAVING COUNT_BIG(*) > 1
        ) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaKH, MaGV, LoaiGV, ISNULL(BienSoXe, N'') AS BienSoXeKey
            FROM CSDT_V2.dbo.KhoaHoc_GiaoVien
            WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N''
              AND MaGV IS NOT NULL AND LTRIM(RTRIM(MaGV)) <> N''
              AND LoaiGV IS NOT NULL AND LTRIM(RTRIM(LoaiGV)) <> N''
            GROUP BY MaKH, MaGV, LoaiGV, ISNULL(BienSoXe, N'')
            HAVING COUNT_BIG(*) > 1
        ) d),
        NULL,
        NULL,
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaKH, v1.MaGV, v1.LoaiGV, ISNULL(v1.BienSoXe, N'') AS BienSoXeKey
            FROM CSDT_V1.dbo.KhoaHoc_GiaoVien v1
            JOIN CSDT_V2.dbo.KhoaHoc_GiaoVien v2
              ON v2.MaKH COLLATE DATABASE_DEFAULT = v1.MaKH COLLATE DATABASE_DEFAULT
             AND v2.MaGV COLLATE DATABASE_DEFAULT = v1.MaGV COLLATE DATABASE_DEFAULT
             AND v2.LoaiGV COLLATE DATABASE_DEFAULT = v1.LoaiGV COLLATE DATABASE_DEFAULT
             AND ISNULL(v2.BienSoXe, N'') COLLATE DATABASE_DEFAULT = ISNULL(v1.BienSoXe, N'') COLLATE DATABASE_DEFAULT
        ) x),
        N'V1 PK is MaKH+MaGV; V2 PK is identity MaLichLV. Functional key follows existing transfer script and needs confirmation.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'KhoaHoc_GiaoVien', N'MaKH + MaGV + LoaiGV + BienSoXe', N'Uncertain',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.KhoaHoc_GiaoVien', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.KhoaHoc_GiaoVien', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* LichHoc: PK is identity. Functional key from existing transfer script. */
IF OBJECT_ID(N'CSDT_V1.dbo.LichHoc', N'U') IS NOT NULL
   AND OBJECT_ID(N'CSDT_V2.dbo.LichHoc', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'LichHoc',
        N'MaKH + Thang + Tuan + TuNgay + DenNgay',
        N'Uncertain',
        1,
        1,
        (SELECT COUNT_BIG(*) FROM CSDT_V1.dbo.LichHoc WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N'' OR Thang IS NULL OR Tuan IS NULL OR TuNgay IS NULL OR DenNgay IS NULL),
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.LichHoc WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N'' OR Thang IS NULL OR Tuan IS NULL OR TuNgay IS NULL OR DenNgay IS NULL),
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaKH, Thang, Tuan, TuNgay, DenNgay
            FROM CSDT_V1.dbo.LichHoc
            WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'' AND Thang IS NOT NULL AND Tuan IS NOT NULL AND TuNgay IS NOT NULL AND DenNgay IS NOT NULL
            GROUP BY MaKH, Thang, Tuan, TuNgay, DenNgay
            HAVING COUNT_BIG(*) > 1
        ) d),
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaKH, Thang, Tuan, TuNgay, DenNgay
            FROM CSDT_V2.dbo.LichHoc
            WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N'' AND Thang IS NOT NULL AND Tuan IS NOT NULL AND TuNgay IS NOT NULL AND DenNgay IS NOT NULL
            GROUP BY MaKH, Thang, Tuan, TuNgay, DenNgay
            HAVING COUNT_BIG(*) > 1
        ) d),
        NULL,
        NULL,
        (SELECT COUNT_BIG(*) FROM (
            SELECT DISTINCT v1.MaKH, v1.Thang, v1.Tuan, v1.TuNgay, v1.DenNgay
            FROM CSDT_V1.dbo.LichHoc v1
            JOIN CSDT_V2.dbo.LichHoc v2
              ON v2.MaKH COLLATE DATABASE_DEFAULT = v1.MaKH COLLATE DATABASE_DEFAULT
             AND v2.Thang = v1.Thang
             AND v2.Tuan = v1.Tuan
             AND v2.TuNgay = v1.TuNgay
             AND v2.DenNgay = v1.DenNgay
        ) x),
        N'PK is identity. Functional key follows existing transfer script and needs confirmation.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'LichHoc', N'MaKH + Thang + Tuan + TuNgay + DenNgay', N'Uncertain',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.LichHoc', N'U') IS NULL THEN 0 ELSE 1 END,
        CASE WHEN OBJECT_ID(N'CSDT_V2.dbo.LichHoc', N'U') IS NULL THEN 0 ELSE 1 END,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in one database; key diagnostics skipped.';
END;

/* KhoaHoc_XeTap: V2-only in reference; generated from V1 KhoaHoc_GiaoVien if needed. */
IF OBJECT_ID(N'CSDT_V2.dbo.KhoaHoc_XeTap', N'U') IS NOT NULL
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT
        N'KhoaHoc_XeTap',
        N'MaKH + BienSoXe + MaGV',
        N'Uncertain',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.KhoaHoc_XeTap', N'U') IS NULL THEN 0 ELSE 1 END,
        1,
        NULL,
        (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.KhoaHoc_XeTap WHERE MaKH IS NULL OR LTRIM(RTRIM(MaKH)) = N'' OR BienSoXe IS NULL OR LTRIM(RTRIM(BienSoXe)) = N'' OR MaGV IS NULL OR LTRIM(RTRIM(MaGV)) = N''),
        NULL,
        (SELECT COUNT_BIG(*) FROM (
            SELECT MaKH, BienSoXe, MaGV
            FROM CSDT_V2.dbo.KhoaHoc_XeTap
            WHERE MaKH IS NOT NULL AND LTRIM(RTRIM(MaKH)) <> N''
              AND BienSoXe IS NOT NULL AND LTRIM(RTRIM(BienSoXe)) <> N''
              AND MaGV IS NOT NULL AND LTRIM(RTRIM(MaGV)) <> N''
            GROUP BY MaKH, BienSoXe, MaGV
            HAVING COUNT_BIG(*) > 1
        ) d),
        NULL,
        NULL,
        NULL,
        N'V2 table. Existing transfer script can generate from V1 KhoaHoc_GiaoVien.BienSoXe; no V1 overlap unless table exists there.';
END
ELSE
BEGIN
    INSERT INTO #KeyDiagnostics
    SELECT N'KhoaHoc_XeTap', N'MaKH + BienSoXe + MaGV', N'Uncertain',
        CASE WHEN OBJECT_ID(N'CSDT_V1.dbo.KhoaHoc_XeTap', N'U') IS NULL THEN 0 ELSE 1 END,
        0,
        NULL, NULL, NULL, NULL, NULL, NULL, NULL,
        N'Table missing in CSDT_V2; key diagnostics skipped.';
END;

/* Result set 2: key diagnostics */
SELECT
    TableName,
    KeyName,
    KeyConfidence,
    V1Exists,
    V2Exists,
    V1MissingKeyRows,
    V2MissingKeyRows,
    V1DuplicateKeyGroups,
    V2DuplicateKeyGroups,
    V1DuplicateRows,
    V2DuplicateRows,
    OverlapKeyCount,
    Notes
FROM #KeyDiagnostics
ORDER BY
    CASE TableName
        WHEN N'KhoaHoc' THEN 1
        WHEN N'BaoCaoI' THEN 2
        WHEN N'NguoiLX' THEN 3
        WHEN N'NguoiLX_HoSo' THEN 4
        WHEN N'NguoiLX_GPLX' THEN 5
        WHEN N'NguoiLXHS_GiayTo' THEN 6
        WHEN N'KhoaHoc_GiaoVien' THEN 7
        WHEN N'LichHoc' THEN 8
        WHEN N'KhoaHoc_XeTap' THEN 9
        ELSE 99
    END,
    KeyName;

/* Result set 3: high-value cross-table read-only diagnostics */
SELECT
    N'CccdSameDifferentMaDK' AS DiagnosticName,
    COUNT_BIG(*) AS IssueCount,
    N'Same SoCMT between V1 and V2 but different MaDK. Review before any transfer.' AS Note
FROM CSDT_V1.dbo.NguoiLX_HoSo v1hs
JOIN CSDT_V1.dbo.NguoiLX v1nlx ON v1nlx.MaDK = v1hs.MaDK
JOIN CSDT_V2.dbo.NguoiLX v2nlx ON v2nlx.SoCMT COLLATE DATABASE_DEFAULT = v1nlx.SoCMT COLLATE DATABASE_DEFAULT
WHERE ISNULL(v1nlx.SoCMT, N'') <> N''
  AND v2nlx.MaDK COLLATE DATABASE_DEFAULT <> v1nlx.MaDK COLLATE DATABASE_DEFAULT

UNION ALL

SELECT
    N'V2HoSoMissingNguoiLX' AS DiagnosticName,
    COUNT_BIG(*) AS IssueCount,
    N'V2 NguoiLX_HoSo rows without matching NguoiLX.' AS Note
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.NguoiLX nlx
    WHERE nlx.MaDK COLLATE DATABASE_DEFAULT = hs.MaDK COLLATE DATABASE_DEFAULT
)

UNION ALL

SELECT
    N'V2HoSoMissingKhoaHoc' AS DiagnosticName,
    COUNT_BIG(*) AS IssueCount,
    N'V2 NguoiLX_HoSo rows without matching KhoaHoc.' AS Note
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.KhoaHoc kh
    WHERE kh.MaKH COLLATE DATABASE_DEFAULT = hs.MaKhoaHoc COLLATE DATABASE_DEFAULT
)

UNION ALL

SELECT
    N'V2GplxMissingNguoiLX' AS DiagnosticName,
    COUNT_BIG(*) AS IssueCount,
    N'V2 NguoiLX_GPLX rows without matching NguoiLX.' AS Note
FROM CSDT_V2.dbo.NguoiLX_GPLX g
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.NguoiLX nlx
    WHERE nlx.MaDK COLLATE DATABASE_DEFAULT = g.MaDK COLLATE DATABASE_DEFAULT
);

DROP TABLE #KeyDiagnostics;
