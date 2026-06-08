/*
    V2_POST_TRANSFER_HEALTH_CHECK.sql

    Kiểm tra lỗi sau khi chuyển dữ liệu từ CSDT_V1 sang CSDT_V2.

    Cách dùng:
    - Nếu chưa đổi mã CSĐT, để @TargetMaCSDT = @SourceMaCSDT.
    - Nếu muốn kiểm tra theo mã mới, ví dụ V1 66016 chuyển sang V2 66026:
        @SourceMaCSDT = '66016'
        @TargetMaCSDT = '66026'
*/

USE [CSDT_V2];
GO

SET NOCOUNT ON;

DECLARE @SourceMaCSDT VARCHAR(5) = '66016';
DECLARE @TargetMaCSDT VARCHAR(5) = '66026';

IF OBJECT_ID('tempdb..#CheckResult') IS NOT NULL DROP TABLE #CheckResult;

CREATE TABLE #CheckResult (
    CheckName NVARCHAR(200) NOT NULL,
    Severity NVARCHAR(20) NOT NULL,
    ErrorCount INT NOT NULL,
    Note NVARCHAR(1000) NULL
);

DECLARE @TargetMaKhoaExpr NVARCHAR(200);
DECLARE @TargetMaDKExpr NVARCHAR(200);

/* =========================================================
   1. TỔNG QUAN SỐ LƯỢNG
   ========================================================= */

SELECT
    @SourceMaCSDT AS SourceMaCSDT,
    @TargetMaCSDT AS TargetMaCSDT,
    (SELECT COUNT(*) FROM CSDT_V1.dbo.KhoaHoc) AS V1_TongKhoa,
    (SELECT COUNT(*) FROM CSDT_V2.dbo.KhoaHoc) AS V2_TongKhoa,
    (SELECT COUNT(*) FROM CSDT_V1.dbo.NguoiLX) AS V1_TongNguoiLX,
    (SELECT COUNT(*) FROM CSDT_V2.dbo.NguoiLX) AS V2_TongNguoiLX,
    (SELECT COUNT(*) FROM CSDT_V1.dbo.NguoiLX_HoSo) AS V1_TongHoSo,
    (SELECT COUNT(*) FROM CSDT_V2.dbo.NguoiLX_HoSo) AS V2_TongHoSo;

/* =========================================================
   2. CHECK RESULT
   ========================================================= */

-- Khóa V1 thiếu trong V2 theo mã kỳ vọng
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'Khóa học V1 thiếu trong V2 theo mã kỳ vọng',
    N'ERROR',
    COUNT(*),
    N'Nếu @SourceMaCSDT khác @TargetMaCSDT thì MaKH V2 được kỳ vọng đổi 5 ký tự đầu sang mã mới.'
FROM CSDT_V1.dbo.KhoaHoc kh
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.KhoaHoc t
    WHERE t.MaKH =
        CASE
            WHEN LEFT(kh.MaKH, 5) = @SourceMaCSDT
            THEN @TargetMaCSDT + SUBSTRING(kh.MaKH, 6, 100)
            ELSE kh.MaKH
        END
);

-- Hồ sơ V1 thiếu trong V2 theo MaDK kỳ vọng
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'Hồ sơ học viên V1 thiếu trong V2 theo MaDK kỳ vọng',
    N'ERROR',
    COUNT(*),
    N'Nếu @SourceMaCSDT khác @TargetMaCSDT thì MaDK V2 được kỳ vọng đổi 5 ký tự đầu sang mã mới.'
FROM CSDT_V1.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.NguoiLX_HoSo t
    WHERE t.MaDK =
        CASE
            WHEN LEFT(hs.MaDK, 6) = @SourceMaCSDT + '-'
            THEN @TargetMaCSDT + SUBSTRING(hs.MaDK, 6, 100)
            ELSE hs.MaDK
        END
);

-- Hồ sơ không có NguoiLX
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'NguoiLX_HoSo không có bản ghi NguoiLX',
    N'ERROR',
    COUNT(*),
    N'Mỗi hồ sơ phải có bản ghi tương ứng trong NguoiLX.'
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1 FROM CSDT_V2.dbo.NguoiLX nlx WHERE nlx.MaDK = hs.MaDK
);

-- Hồ sơ không có khóa học
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'NguoiLX_HoSo không có KhoaHoc',
    N'ERROR',
    COUNT(*),
    N'MaKhoaHoc trong hồ sơ phải tồn tại ở KhoaHoc.MaKH.'
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1 FROM CSDT_V2.dbo.KhoaHoc kh WHERE kh.MaKH = hs.MaKhoaHoc
);

-- GPLX không có NguoiLX
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'NguoiLX_GPLX không có NguoiLX',
    N'ERROR',
    COUNT(*),
    N'GPLX đã có của học viên phải có NguoiLX tương ứng.'
FROM CSDT_V2.dbo.NguoiLX_GPLX g
WHERE NOT EXISTS (
    SELECT 1 FROM CSDT_V2.dbo.NguoiLX nlx WHERE nlx.MaDK = g.MaDK
);

-- Giấy tờ không có hồ sơ
IF OBJECT_ID(N'CSDT_V2.dbo.NguoiLXHS_GiayTo', N'U') IS NOT NULL
BEGIN
    INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
    SELECT
        N'NguoiLXHS_GiayTo không có NguoiLX_HoSo',
        N'ERROR',
        COUNT(*),
        N'Giấy tờ phải có hồ sơ học viên tương ứng.'
    FROM CSDT_V2.dbo.NguoiLXHS_GiayTo gt
    WHERE NOT EXISTS (
        SELECT 1 FROM CSDT_V2.dbo.NguoiLX_HoSo hs WHERE hs.MaDK = gt.MaDK
    );
END;

-- MaDK sai format cơ bản
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'MaDK sai cấu trúc cơ bản',
    N'WARNING',
    COUNT(*),
    N'Kiểm tra 5 số đầu, dấu gạch ngang, ngày yyyymmdd và chuỗi số cuối.'
FROM CSDT_V2.dbo.NguoiLX nlx
WHERE
    LEN(nlx.MaDK) < 17
    OR SUBSTRING(nlx.MaDK, 6, 1) <> '-'
    OR SUBSTRING(nlx.MaDK, 15, 1) <> '-'
    OR TRY_CONVERT(date, SUBSTRING(nlx.MaDK, 7, 8), 112) IS NULL
    OR PATINDEX('%[^0-9]%', LEFT(nlx.MaDK, 5)) > 0
    OR PATINDEX('%[^0-9]%', SUBSTRING(nlx.MaDK, 16, 50)) > 0;

-- MaDK không đúng mã CSĐT đích
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'MaDK không đúng mã CSĐT đích',
    N'WARNING',
    COUNT(*),
    N'Dùng khi đã quyết định mã CSĐT V2 phải là @TargetMaCSDT.'
FROM CSDT_V2.dbo.NguoiLX nlx
WHERE LEFT(nlx.MaDK, 5) <> @TargetMaCSDT;

-- MaKH không đúng mã CSĐT đích
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'MaKH không đúng mã CSĐT đích',
    N'WARNING',
    COUNT(*),
    N'Dùng khi đã quyết định mã khóa V2 phải đổi 5 ký tự đầu sang @TargetMaCSDT.'
FROM CSDT_V2.dbo.KhoaHoc kh
WHERE LEFT(kh.MaKH, 5) <> @TargetMaCSDT;

-- MaCSDT không đúng mã đích nếu có cột MaCSDT
IF COL_LENGTH(N'CSDT_V2.dbo.KhoaHoc', 'MaCSDT') IS NOT NULL
BEGIN
    INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
    SELECT
        N'KhoaHoc.MaCSDT không đúng mã CSĐT đích',
        N'WARNING',
        COUNT(*),
        N'KhoaHoc.MaCSDT nên là @TargetMaCSDT.'
    FROM CSDT_V2.dbo.KhoaHoc
    WHERE ISNULL(MaCSDT, '') <> @TargetMaCSDT;
END;

IF COL_LENGTH(N'CSDT_V2.dbo.BaoCaoI', 'MaCSDT') IS NOT NULL
BEGIN
    INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
    SELECT
        N'BaoCaoI.MaCSDT không đúng mã CSĐT đích',
        N'WARNING',
        COUNT(*),
        N'BaoCaoI.MaCSDT nên là @TargetMaCSDT.'
    FROM CSDT_V2.dbo.BaoCaoI
    WHERE ISNULL(MaCSDT, '') <> @TargetMaCSDT;
END;

-- Trùng MaDK trong hồ sơ
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'Trùng MaDK trong NguoiLX_HoSo',
    N'ERROR',
    COUNT(*),
    N'Một MaDK không nên có nhiều hồ sơ nếu phần mềm không cho phép.'
FROM (
    SELECT MaDK
    FROM CSDT_V2.dbo.NguoiLX_HoSo
    GROUP BY MaDK
    HAVING COUNT(*) > 1
) x;

-- Trùng CCCD
INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
SELECT
    N'Trùng CCCD trong NguoiLX',
    N'WARNING',
    COUNT(*),
    N'Có thể là cùng người nhiều hồ sơ hoặc trùng dữ liệu, cần kiểm tra.'
FROM (
    SELECT SoCMT
    FROM CSDT_V2.dbo.NguoiLX
    WHERE ISNULL(SoCMT, '') <> ''
    GROUP BY SoCMT
    HAVING COUNT(*) > 1
) x;

-- KhoaHoc_XeTap lỗi liên kết nếu có bảng
IF OBJECT_ID(N'CSDT_V2.dbo.KhoaHoc_XeTap', N'U') IS NOT NULL
BEGIN
    INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
    SELECT
        N'KhoaHoc_XeTap không có KhoaHoc',
        N'ERROR',
        COUNT(*),
        N'Mỗi dòng xe theo khóa phải có KhoaHoc tương ứng.'
    FROM CSDT_V2.dbo.KhoaHoc_XeTap xt
    WHERE NOT EXISTS (
        SELECT 1 FROM CSDT_V2.dbo.KhoaHoc kh WHERE kh.MaKH = xt.MaKH
    );

    INSERT INTO #CheckResult(CheckName, Severity, ErrorCount, Note)
    SELECT
        N'KhoaHoc_XeTap không có XeTap',
        N'WARNING',
        COUNT(*),
        N'Nếu BienSoXe không có trong XeTap thì cần bổ sung xe hoặc kiểm tra biển số.'
    FROM CSDT_V2.dbo.KhoaHoc_XeTap xt
    WHERE ISNULL(xt.BienSoXe, '') <> ''
      AND NOT EXISTS (
        SELECT 1 FROM CSDT_V2.dbo.XeTap x WHERE x.BienSoXe = xt.BienSoXe
    );
END;

SELECT *
FROM #CheckResult
ORDER BY
    CASE Severity WHEN N'ERROR' THEN 1 WHEN N'WARNING' THEN 2 ELSE 3 END,
    ErrorCount DESC,
    CheckName;

/* =========================================================
   3. DETAIL CÁC LỖI QUAN TRỌNG
   ========================================================= */

-- Detail: hồ sơ thiếu NguoiLX
SELECT TOP 300
    hs.MaDK,
    hs.MaKhoaHoc,
    N'Hồ sơ không có NguoiLX' AS Loi
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1 FROM CSDT_V2.dbo.NguoiLX nlx WHERE nlx.MaDK = hs.MaDK
)
ORDER BY hs.MaKhoaHoc, hs.MaDK;

-- Detail: hồ sơ không có khóa
SELECT TOP 300
    hs.MaDK,
    hs.MaKhoaHoc,
    N'Hồ sơ không có KhoaHoc' AS Loi
FROM CSDT_V2.dbo.NguoiLX_HoSo hs
WHERE NOT EXISTS (
    SELECT 1 FROM CSDT_V2.dbo.KhoaHoc kh WHERE kh.MaKH = hs.MaKhoaHoc
)
ORDER BY hs.MaKhoaHoc, hs.MaDK;

-- Detail: MaDK không đúng mã CSĐT đích
SELECT TOP 300
    nlx.MaDK,
    nlx.HoVaTen,
    nlx.SoCMT,
    N'MaDK không đúng mã CSĐT đích' AS Loi
FROM CSDT_V2.dbo.NguoiLX nlx
WHERE LEFT(nlx.MaDK, 5) <> @TargetMaCSDT
ORDER BY nlx.MaDK;

-- Detail: trùng CCCD
SELECT TOP 300
    SoCMT,
    COUNT(*) AS SoDong,
    STRING_AGG(CONVERT(NVARCHAR(MAX), MaDK), N', ') AS DanhSachMaDK
FROM CSDT_V2.dbo.NguoiLX
WHERE ISNULL(SoCMT, '') <> ''
GROUP BY SoCMT
HAVING COUNT(*) > 1
ORDER BY SoDong DESC, SoCMT;
GO
