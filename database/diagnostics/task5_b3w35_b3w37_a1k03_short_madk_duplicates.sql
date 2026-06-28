USE [CSDT_V2];
GO

/*
    Task 5 B3W35-B3W37 - A1K03 shortened MaDK duplicate diagnostics.

    READ-ONLY diagnostics for TEST/local CSDT_V2 and CSDT_V1 only.
    This script returns counts, overlap checks, dependency impact, and sanitized samples.
    It leaves permanent data and permanent objects unchanged.
*/

SET NOCOUNT ON;

DECLARE @CourseMaKH varchar(13) = '66016K26A1003';
DECLARE @CourseSearch nvarchar(50) = N'A1K03';

IF DB_ID(N'CSDT_V1') IS NULL OR DB_ID(N'CSDT_V2') IS NULL
BEGIN
    SELECT
        CAST(0 AS bit) AS CanRun,
        N'Missing CSDT_V1 or CSDT_V2 database. Stop.' AS Message;
    RETURN;
END;

/* Result set 1: course discovery for exact MaKH and UI label search */
SELECT
    N'CSDT_V1' AS DatabaseName,
    kh.MaKH,
    kh.TenKH,
    kh.HangGPLX,
    kh.HangDT,
    kh.NgayKG,
    kh.NgayBG,
    kh.TrangThai
FROM CSDT_V1.dbo.KhoaHoc kh
WHERE kh.MaKH COLLATE DATABASE_DEFAULT = @CourseMaKH COLLATE DATABASE_DEFAULT
   OR kh.TenKH LIKE N'%' + @CourseSearch + N'%'

UNION ALL

SELECT
    N'CSDT_V2' AS DatabaseName,
    kh.MaKH,
    kh.TenKH,
    kh.HangGPLX,
    kh.HangDT,
    kh.NgayKG,
    kh.NgayBG,
    kh.TrangThai
FROM CSDT_V2.dbo.KhoaHoc kh
WHERE kh.MaKH COLLATE DATABASE_DEFAULT = @CourseMaKH COLLATE DATABASE_DEFAULT
   OR kh.TenKH LIKE N'%' + @CourseSearch + N'%'
ORDER BY DatabaseName, MaKH;

/* Result set 2: all V2 HoSo rows for the suspected A1K03 course */
SELECT
    @CourseMaKH AS MaKhoaHoc,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX_HoSo hs WHERE hs.MaKhoaHoc = @CourseMaKH) AS TotalHoSoRows,
    (SELECT COUNT_BIG(*) FROM (SELECT DISTINCT hs.MaDK FROM CSDT_V2.dbo.NguoiLX_HoSo hs WHERE hs.MaKhoaHoc = @CourseMaKH) d) AS DistinctMaDK,
    (SELECT COUNT_BIG(*) FROM (
        SELECT DISTINCT NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
        FROM CSDT_V2.dbo.NguoiLX_HoSo hs
        JOIN CSDT_V2.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
        WHERE hs.MaKhoaHoc = @CourseMaKH
          AND NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') IS NOT NULL
    ) d) AS DistinctSoCMT,
    MIN(hs.NgayNhanHSo) AS MinNgayNhanHSo,
    MAX(hs.NgayNhanHSo) AS MaxNgayNhanHSo
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE hs.MaKhoaHoc = @CourseMaKH;

/* Result set 3: TT_XuLy distribution for V2 A1K03 */
SELECT
    hs.TT_XuLy,
    COUNT_BIG(*) AS TotalRows
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE hs.MaKhoaHoc = @CourseMaKH
GROUP BY hs.TT_XuLy
ORDER BY TotalRows DESC, hs.TT_XuLy;

/* Result set 4: V2 duplicate business identity groups for A1K03 */
;WITH A1K03Rows AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    JOIN CSDT_V2.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE hs.MaKhoaHoc = @CourseMaKH
),
DuplicateGroups AS (
    SELECT
        NormalizedHoVaTen,
        NgaySinh,
        NormalizedSoCMT,
        MaKhoaHoc,
        COUNT_BIG(*) AS TotalRows
    FROM A1K03Rows
    GROUP BY NormalizedHoVaTen, NgaySinh, NormalizedSoCMT, MaKhoaHoc
    HAVING COUNT_BIG(*) > 1
)
SELECT
    CASE
        WHEN dg.NormalizedHoVaTen = N'' THEN N''
        ELSE LEFT(dg.NormalizedHoVaTen, 1) + N'***'
    END AS HoTenMasked,
    dg.NgaySinh,
    CASE
        WHEN dg.NormalizedSoCMT IS NULL THEN N''
        WHEN LEN(dg.NormalizedSoCMT) <= 4 THEN N'***'
        ELSE REPLICATE(N'*', LEN(dg.NormalizedSoCMT) - 4) + RIGHT(dg.NormalizedSoCMT, 4)
    END AS SoCMTMasked,
    LEN(ISNULL(dg.NormalizedSoCMT, N'')) AS SoCMTLength,
    dg.MaKhoaHoc,
    dg.TotalRows,
    STUFF((
        SELECT N', ' + r.MaDK
        FROM A1K03Rows r
        WHERE r.NormalizedHoVaTen = dg.NormalizedHoVaTen
          AND r.NgaySinh = dg.NgaySinh
          AND ISNULL(r.NormalizedSoCMT, N'') = ISNULL(dg.NormalizedSoCMT, N'')
          AND r.MaKhoaHoc = dg.MaKhoaHoc
        ORDER BY r.MaDK
        FOR XML PATH(N''), TYPE
    ).value(N'.', N'nvarchar(max)'), 1, 2, N'') AS MaDKValues
FROM DuplicateGroups dg
ORDER BY dg.TotalRows DESC, dg.MaKhoaHoc, dg.NormalizedHoVaTen, dg.NgaySinh;

/* Result set 5: short/full MaDK prefix pairs in V2 A1K03 */
;WITH A1K03Rows AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    JOIN CSDT_V2.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE hs.MaKhoaHoc = @CourseMaKH
)
SELECT
    shortRow.MaDK AS ShortMaDK,
    fullRow.MaDK AS FullMaDK,
    LEN(shortRow.MaDK) AS ShortMaDKLength,
    LEN(fullRow.MaDK) AS FullMaDKLength,
    LEN(fullRow.MaDK) - LEN(shortRow.MaDK) AS LengthDifference,
    shortRow.MaKhoaHoc,
    CASE
        WHEN shortRow.NormalizedHoVaTen = N'' THEN N''
        ELSE LEFT(shortRow.NormalizedHoVaTen, 1) + N'***'
    END AS HoTenMasked,
    shortRow.NgaySinh,
    CASE
        WHEN shortRow.NormalizedSoCMT IS NULL THEN N''
        WHEN LEN(shortRow.NormalizedSoCMT) <= 4 THEN N'***'
        ELSE REPLICATE(N'*', LEN(shortRow.NormalizedSoCMT) - 4) + RIGHT(shortRow.NormalizedSoCMT, 4)
    END AS SoCMTMasked,
    LEN(ISNULL(shortRow.NormalizedSoCMT, N'')) AS SoCMTLength
FROM A1K03Rows shortRow
JOIN A1K03Rows fullRow
    ON fullRow.MaDK LIKE shortRow.MaDK + N'%'
   AND fullRow.MaDK <> shortRow.MaDK
   AND LEN(fullRow.MaDK) > LEN(shortRow.MaDK)
   AND fullRow.NormalizedHoVaTen = shortRow.NormalizedHoVaTen
   AND fullRow.NgaySinh = shortRow.NgaySinh
   AND ISNULL(fullRow.NormalizedSoCMT, N'') = ISNULL(shortRow.NormalizedSoCMT, N'')
WHERE LEN(fullRow.MaDK) - LEN(shortRow.MaDK) = 3
ORDER BY shortRow.MaDK, fullRow.MaDK;

/* Result set 6: V1 vs V2 A1K03 exact/business overlap summary */
;WITH V1A1K03 AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
    FROM CSDT_V1.dbo.NguoiLX_HoSo hs
    JOIN CSDT_V1.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE hs.MaKhoaHoc COLLATE DATABASE_DEFAULT = @CourseMaKH COLLATE DATABASE_DEFAULT
),
V2A1K03 AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    JOIN CSDT_V2.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE hs.MaKhoaHoc = @CourseMaKH
)
SELECT N'V1 rows for exact MaKhoaHoc' AS MetricName, COUNT_BIG(*) AS TotalRows FROM V1A1K03
UNION ALL
SELECT N'V2 rows for exact MaKhoaHoc', COUNT_BIG(*) FROM V2A1K03
UNION ALL
SELECT N'Exact MaDK overlap', COUNT_BIG(*)
FROM V2A1K03 v2
WHERE EXISTS (
    SELECT 1
    FROM V1A1K03 v1
    WHERE v1.MaDK COLLATE DATABASE_DEFAULT = v2.MaDK COLLATE DATABASE_DEFAULT
)
UNION ALL
SELECT N'Business key overlap: SoCMT + NgaySinh + normalized HoVaTen', COUNT_BIG(*)
FROM V2A1K03 v2
WHERE EXISTS (
    SELECT 1
    FROM V1A1K03 v1
    WHERE ISNULL(v1.NormalizedSoCMT, N'') COLLATE DATABASE_DEFAULT = ISNULL(v2.NormalizedSoCMT, N'') COLLATE DATABASE_DEFAULT
      AND v1.NgaySinh COLLATE DATABASE_DEFAULT = v2.NgaySinh COLLATE DATABASE_DEFAULT
      AND v1.NormalizedHoVaTen COLLATE DATABASE_DEFAULT = v2.NormalizedHoVaTen COLLATE DATABASE_DEFAULT
);

/* Result set 7: dependency impact by short/full pair */
;WITH A1K03Rows AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    JOIN CSDT_V2.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE hs.MaKhoaHoc = @CourseMaKH
),
PrefixPairs AS (
    SELECT
        shortRow.MaDK AS ShortMaDK,
        fullRow.MaDK AS FullMaDK,
        shortRow.MaKhoaHoc,
        shortRow.NormalizedHoVaTen,
        shortRow.NgaySinh,
        shortRow.NormalizedSoCMT
    FROM A1K03Rows shortRow
    JOIN A1K03Rows fullRow
        ON fullRow.MaDK LIKE shortRow.MaDK + N'%'
       AND fullRow.MaDK <> shortRow.MaDK
       AND LEN(fullRow.MaDK) > LEN(shortRow.MaDK)
       AND fullRow.NormalizedHoVaTen = shortRow.NormalizedHoVaTen
       AND fullRow.NgaySinh = shortRow.NgaySinh
       AND ISNULL(fullRow.NormalizedSoCMT, N'') = ISNULL(shortRow.NormalizedSoCMT, N'')
    WHERE LEN(fullRow.MaDK) - LEN(shortRow.MaDK) = 3
)
SELECT
    p.ShortMaDK,
    p.FullMaDK,
    p.MaKhoaHoc,
    CASE
        WHEN p.NormalizedSoCMT IS NULL THEN N''
        WHEN LEN(p.NormalizedSoCMT) <= 4 THEN N'***'
        ELSE REPLICATE(N'*', LEN(p.NormalizedSoCMT) - 4) + RIGHT(p.NormalizedSoCMT, 4)
    END AS SoCMTMasked,
    LEN(ISNULL(p.NormalizedSoCMT, N'')) AS SoCMTLength,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX WHERE MaDK = p.ShortMaDK) AS ShortNguoiLXRows,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX WHERE MaDK = p.FullMaDK) AS FullNguoiLXRows,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX_HoSo WHERE MaDK = p.ShortMaDK) AS ShortHoSoRows,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLX_HoSo WHERE MaDK = p.FullMaDK) AS FullHoSoRows,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLXHS_GiayTo WHERE MaDK = p.ShortMaDK) AS ShortGiayToRows,
    (SELECT COUNT_BIG(*) FROM CSDT_V2.dbo.NguoiLXHS_GiayTo WHERE MaDK = p.FullMaDK) AS FullGiayToRows
FROM PrefixPairs p
ORDER BY p.ShortMaDK, p.FullMaDK;

/* Result set 8: sanitized sample of suspected duplicate groups */
;WITH A1K03Rows AS (
    SELECT
        hs.MaDK,
        hs.MaKhoaHoc,
        UPPER(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(ISNULL(nlx.HoVaTen, N''), CHAR(9), N' '), N'  ', N' '), N'  ', N' ')))) AS NormalizedHoVaTen,
        nlx.NgaySinh,
        NULLIF(LTRIM(RTRIM(ISNULL(nlx.SoCMT, N''))), N'') AS NormalizedSoCMT,
        hs.TT_XuLy,
        hs.NgayNhanHSo
    FROM CSDT_V2.dbo.NguoiLX_HoSo hs
    JOIN CSDT_V2.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
    WHERE hs.MaKhoaHoc = @CourseMaKH
),
DuplicateBusinessKeys AS (
    SELECT
        NormalizedHoVaTen,
        NgaySinh,
        NormalizedSoCMT,
        MaKhoaHoc
    FROM A1K03Rows
    GROUP BY NormalizedHoVaTen, NgaySinh, NormalizedSoCMT, MaKhoaHoc
    HAVING COUNT_BIG(*) > 1
)
SELECT TOP (100)
    r.MaDK,
    CASE
        WHEN r.NormalizedHoVaTen = N'' THEN N''
        ELSE LEFT(r.NormalizedHoVaTen, 1) + N'***'
    END AS HoTenMasked,
    r.NgaySinh,
    CASE
        WHEN r.NormalizedSoCMT IS NULL THEN N''
        WHEN LEN(r.NormalizedSoCMT) <= 4 THEN N'***'
        ELSE REPLICATE(N'*', LEN(r.NormalizedSoCMT) - 4) + RIGHT(r.NormalizedSoCMT, 4)
    END AS SoCMTMasked,
    LEN(ISNULL(r.NormalizedSoCMT, N'')) AS SoCMTLength,
    r.MaKhoaHoc,
    r.TT_XuLy,
    r.NgayNhanHSo
FROM A1K03Rows r
JOIN DuplicateBusinessKeys d
    ON d.NormalizedHoVaTen = r.NormalizedHoVaTen
   AND d.NgaySinh = r.NgaySinh
   AND ISNULL(d.NormalizedSoCMT, N'') = ISNULL(r.NormalizedSoCMT, N'')
   AND d.MaKhoaHoc = r.MaKhoaHoc
ORDER BY r.NormalizedHoVaTen, r.NgaySinh, r.MaDK;
