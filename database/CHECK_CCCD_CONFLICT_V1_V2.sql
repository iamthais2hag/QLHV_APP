/*
    CHECK_CCCD_CONFLICT_V1_V2.sql
    Kiểm tra CCCD trùng nhưng MaDK khác giữa CSDT_V1 và CSDT_V2.

    Dùng trước khi chuyển ALL khóa.
*/

USE [CSDT_V2];
GO

SET NOCOUNT ON;

DECLARE @OnlyCoursesNotExistsInV2 BIT = 0;
DECLARE @FromNgayKG DATE = NULL;
DECLARE @ToNgayKG DATE = NULL;

IF OBJECT_ID('tempdb..#CourseList') IS NOT NULL DROP TABLE #CourseList;

SELECT kh.MaKH
INTO #CourseList
FROM CSDT_V1.dbo.KhoaHoc kh
WHERE
    (@OnlyCoursesNotExistsInV2 = 0 OR NOT EXISTS (
        SELECT 1 FROM CSDT_V2.dbo.KhoaHoc t WHERE t.MaKH = kh.MaKH
    ))
    AND (@FromNgayKG IS NULL OR TRY_CONVERT(date, kh.NgayKG) >= @FromNgayKG)
    AND (@ToNgayKG IS NULL OR TRY_CONVERT(date, kh.NgayKG) <= @ToNgayKG);

SELECT
    v1hs.MaKhoaHoc AS V1_MaKhoa,
    v1hs.MaDK AS V1_MaDK,
    v1nlx.HoVaTen AS V1_HoTen,
    v1nlx.NgaySinh AS V1_NgaySinh,
    v1nlx.SoCMT AS CCCD,
    v2nlx.MaDK AS V2_MaDK,
    v2nlx.HoVaTen AS V2_HoTen,
    v2nlx.NgaySinh AS V2_NgaySinh,
    CASE
        WHEN ISNULL(v1nlx.HoVaTen, '') = ISNULL(v2nlx.HoVaTen, '')
         AND ISNULL(CONVERT(varchar(10), v1nlx.NgaySinh, 120), '') = ISNULL(CONVERT(varchar(10), v2nlx.NgaySinh, 120), '')
        THEN N'Có thể cùng 1 người, khác MaDK'
        ELSE N'Cần kiểm tra thủ công'
    END AS GoiY
FROM CSDT_V1.dbo.NguoiLX_HoSo v1hs
JOIN CSDT_V1.dbo.NguoiLX v1nlx
    ON v1nlx.MaDK = v1hs.MaDK
JOIN CSDT_V2.dbo.NguoiLX v2nlx
    ON v2nlx.SoCMT = v1nlx.SoCMT
WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = v1hs.MaKhoaHoc)
  AND ISNULL(v1nlx.SoCMT, '') <> ''
  AND v2nlx.MaDK <> v1nlx.MaDK
ORDER BY v1hs.MaKhoaHoc, v1nlx.SoCMT, v1hs.MaDK;

-- Tổng hợp số lượng conflict theo khóa
SELECT
    v1hs.MaKhoaHoc,
    COUNT(*) AS SoDongConflict
FROM CSDT_V1.dbo.NguoiLX_HoSo v1hs
JOIN CSDT_V1.dbo.NguoiLX v1nlx
    ON v1nlx.MaDK = v1hs.MaDK
JOIN CSDT_V2.dbo.NguoiLX v2nlx
    ON v2nlx.SoCMT = v1nlx.SoCMT
WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = v1hs.MaKhoaHoc)
  AND ISNULL(v1nlx.SoCMT, '') <> ''
  AND v2nlx.MaDK <> v1nlx.MaDK
GROUP BY v1hs.MaKhoaHoc
ORDER BY v1hs.MaKhoaHoc;
GO
