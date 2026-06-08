
/*
    VALIDATE SAU KHI CHUYỂN ALL KHÓA V1 -> V2
*/

USE [CSDT_V2];
GO

SELECT 
    (SELECT COUNT(*) FROM CSDT_V1.dbo.KhoaHoc) AS V1_TongKhoa,
    (SELECT COUNT(*) FROM CSDT_V2.dbo.KhoaHoc WHERE MaKH IN (SELECT MaKH FROM CSDT_V1.dbo.KhoaHoc)) AS V2_KhoaDaCoTheoV1,
    (SELECT COUNT(*) FROM CSDT_V1.dbo.NguoiLX_HoSo) AS V1_TongHoSo,
    (SELECT COUNT(*) FROM CSDT_V2.dbo.NguoiLX_HoSo WHERE MaDK IN (SELECT MaDK FROM CSDT_V1.dbo.NguoiLX_HoSo)) AS V2_HoSoDaCoTheoV1;

-- Các khóa V1 còn thiếu trong V2
SELECT TOP 500
    kh.MaKH,
    kh.TenKH,
    kh.HangGPLX,
    kh.NgayKG,
    kh.NgayBG,
    kh.TongSoHV
FROM CSDT_V1.dbo.KhoaHoc kh
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.KhoaHoc t
    WHERE t.MaKH = kh.MaKH
)
ORDER BY kh.MaKH;

-- Học viên V1 còn thiếu trong V2
SELECT TOP 1000
    hs.MaKhoaHoc,
    hs.MaDK,
    nlx.HoVaTen,
    nlx.SoCMT
FROM CSDT_V1.dbo.NguoiLX_HoSo hs
JOIN CSDT_V1.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
WHERE NOT EXISTS (
    SELECT 1
    FROM CSDT_V2.dbo.NguoiLX_HoSo t
    WHERE t.MaDK = hs.MaDK
)
ORDER BY hs.MaKhoaHoc, hs.MaDK;
GO
