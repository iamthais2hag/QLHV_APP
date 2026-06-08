
/*
    FORCE CHUYỂN ALL KHÓA / HỌC VIÊN TỪ V1 SANG V2
    BẢN NÀY TẠM BỎ QUA CẢNH BÁO CCCD TRÙNG NHƯNG MADK KHÁC.
    CHỈ CHẠY TRÊN DATABASE BACKUP CSDT_V1 / CSDT_V2, KHÔNG CHẠY TRÊN DATABASE GỐC.

    TEST CHUYỂN ALL KHÓA / HỌC VIÊN TỪ V1 SANG V2
    Source DB: CSDT_V1
    Target DB: CSDT_V2

    CHẾ ĐỘ MẶC ĐỊNH: DRY RUN = 1
    - Chạy thử, kiểm tra, ROLLBACK, không ghi dữ liệu.
    - Khi kiểm tra ổn mới đổi @DryRun = 0.

    Mức chuyển mặc định:
    - KhoaHoc
    - BaoCaoI
    - NguoiLX
    - NguoiLX_HoSo
    - NguoiLX_GPLX
    - NguoiLXHS_GiayTo

    Tùy chọn:
    - @CopyDaoTao = 1 để copy thêm KhoaHoc_GiaoVien, LichHoc.
    - @CreateKhoaHocXeTap = 1 để tự tạo KhoaHoc_XeTap từ KhoaHoc_GiaoVien nếu có BienSoXe.
    - @OnlyCoursesNotExistsInV2 = 1 để chỉ chuyển khóa chưa có trong V2.
    - @FromNgayKG/@ToNgayKG để giới hạn theo ngày khai giảng.
*/

USE [CSDT_V2];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun BIT = 1;                         -- 1: chạy thử ROLLBACK, 0: ghi thật COMMIT
DECLARE @CopyDaoTao BIT = 0;                     -- 0: chỉ hồ sơ học viên, 1: copy thêm giáo viên/lịch học
DECLARE @CreateKhoaHocXeTap BIT = 0;             -- 1: sinh KhoaHoc_XeTap từ KhoaHoc_GiaoVien.BienSoXe
DECLARE @OnlyCoursesNotExistsInV2 BIT = 0;        -- 0: quét tất cả khóa V1, 1: chỉ khóa V2 chưa có
DECLARE @BlockOnCccdConflict BIT = 0;            -- FORCE: tạm bỏ qua CCCD trùng nhưng MaDK khác
DECLARE @FromNgayKG DATE = NULL;                 -- ví dụ '2025-01-01', NULL = không lọc
DECLARE @ToNgayKG DATE = NULL;                   -- ví dụ '2026-12-31', NULL = không lọc

DECLARE @Rows INT;

IF DB_ID(N'CSDT_V1') IS NULL OR DB_ID(N'CSDT_V2') IS NULL
BEGIN
    THROW 53001, N'Không tìm thấy database CSDT_V1 hoặc CSDT_V2.', 1;
END;

IF OBJECT_ID('tempdb..#CourseList') IS NOT NULL DROP TABLE #CourseList;
IF OBJECT_ID('tempdb..#CccdConflict') IS NOT NULL DROP TABLE #CccdConflict;
IF OBJECT_ID('tempdb..#RunLog') IS NOT NULL DROP TABLE #RunLog;

CREATE TABLE #RunLog (
    StepName NVARCHAR(200) NOT NULL,
    RowsAffected INT NOT NULL,
    Note NVARCHAR(500) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

SELECT kh.MaKH
INTO #CourseList
FROM CSDT_V1.dbo.KhoaHoc kh
WHERE
    (@OnlyCoursesNotExistsInV2 = 0 OR NOT EXISTS (
        SELECT 1
        FROM CSDT_V2.dbo.KhoaHoc t
        WHERE t.MaKH = kh.MaKH
    ))
    AND (@FromNgayKG IS NULL OR TRY_CONVERT(date, kh.NgayKG) >= @FromNgayKG)
    AND (@ToNgayKG IS NULL OR TRY_CONVERT(date, kh.NgayKG) <= @ToNgayKG);

CREATE UNIQUE CLUSTERED INDEX IX_Temp_CourseList_MaKH ON #CourseList(MaKH);

IF NOT EXISTS (SELECT 1 FROM #CourseList)
BEGIN
    THROW 53002, N'Không có khóa nào trong danh sách chuyển. Kiểm tra bộ lọc @OnlyCoursesNotExistsInV2 / @FromNgayKG / @ToNgayKG.', 1;
END;

/* =========================================================
   1. PRECHECK
   ========================================================= */

PRINT N'===== PRECHECK ALL KHÓA V1 -> V2 =====';

SELECT
    @DryRun AS DryRun,
    @OnlyCoursesNotExistsInV2 AS OnlyCoursesNotExistsInV2,
    @CopyDaoTao AS CopyDaoTao,
    @CreateKhoaHocXeTap AS CreateKhoaHocXeTap,
    @FromNgayKG AS FromNgayKG,
    @ToNgayKG AS ToNgayKG,
    (SELECT COUNT(*) FROM #CourseList) AS TongKhoaSeQuet,
    (SELECT COUNT(*) FROM CSDT_V1.dbo.NguoiLX_HoSo hs WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = hs.MaKhoaHoc)) AS V1_TongHoSoTrongCacKhoa,
    (SELECT COUNT(*) FROM CSDT_V2.dbo.NguoiLX_HoSo hs WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = hs.MaKhoaHoc)) AS V2_DaCoHoSoTrongCacKhoa,
    (SELECT COUNT(*)
     FROM CSDT_V1.dbo.NguoiLX_HoSo hs
     WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = hs.MaKhoaHoc)
       AND NOT EXISTS (SELECT 1 FROM CSDT_V2.dbo.NguoiLX_HoSo t WHERE t.MaDK = hs.MaDK)
    ) AS DuKienThemHoSo;

-- Danh sách khóa sẽ quét
SELECT TOP 500
    kh.MaKH,
    kh.TenKH,
    kh.HangGPLX,
    kh.HangDT,
    kh.NgayKG,
    kh.NgayBG,
    kh.TongSoHV,
    CASE WHEN EXISTS (SELECT 1 FROM CSDT_V2.dbo.KhoaHoc t WHERE t.MaKH = kh.MaKH) THEN N'V2 đã có khóa' ELSE N'V2 chưa có khóa' END AS TrangThaiV2
FROM CSDT_V1.dbo.KhoaHoc kh
JOIN #CourseList c ON c.MaKH = kh.MaKH
ORDER BY kh.MaKH;

-- Học viên V1 đã có MaDK bên V2, sẽ bỏ qua phần hồ sơ đã có
SELECT TOP 300
    hs.MaKhoaHoc,
    hs.MaDK,
    nlx.HoVaTen,
    nlx.SoCMT,
    N'V2 đã có MaDK, sẽ bỏ qua phần đã có' AS CanhBao
FROM CSDT_V1.dbo.NguoiLX_HoSo hs
JOIN CSDT_V1.dbo.NguoiLX nlx ON nlx.MaDK = hs.MaDK
WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = hs.MaKhoaHoc)
  AND EXISTS (SELECT 1 FROM CSDT_V2.dbo.NguoiLX_HoSo t WHERE t.MaDK = hs.MaDK)
ORDER BY hs.MaKhoaHoc, hs.MaDK;

-- Cảnh báo CCCD trùng nhưng MaDK khác
SELECT
    v1hs.MaKhoaHoc AS V1_MaKhoa,
    v1hs.MaDK AS V1_MaDK,
    v1nlx.HoVaTen AS V1_HoTen,
    v1nlx.SoCMT AS CCCD,
    v2nlx.MaDK AS V2_MaDK,
    v2nlx.HoVaTen AS V2_HoTen,
    N'Trùng CCCD nhưng khác MaDK' AS CanhBao
INTO #CccdConflict
FROM CSDT_V1.dbo.NguoiLX_HoSo v1hs
JOIN CSDT_V1.dbo.NguoiLX v1nlx ON v1nlx.MaDK = v1hs.MaDK
JOIN CSDT_V2.dbo.NguoiLX v2nlx ON v2nlx.SoCMT = v1nlx.SoCMT
WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = v1hs.MaKhoaHoc)
  AND ISNULL(v1nlx.SoCMT, '') <> ''
  AND v2nlx.MaDK <> v1nlx.MaDK;

SELECT TOP 500 * FROM #CccdConflict ORDER BY V1_MaKhoa, CCCD;

IF @BlockOnCccdConflict = 1 AND EXISTS (SELECT 1 FROM #CccdConflict)
BEGIN
    THROW 53003, N'Có CCCD trùng nhưng MaDK khác giữa V1 và V2. Hãy kiểm tra danh sách #CccdConflict trước khi chuyển ALL.', 1;
END;

/* =========================================================
   2. TRANSFER ALL CORE DATA
   ========================================================= */

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT N'===== BẮT ĐẦU CHUYỂN ALL DỮ LIỆU V1 -> V2 =====';


    INSERT INTO CSDT_V2.dbo.[KhoaHoc] ([MaKH], [MaCSDT], [MaSoGTVT], [TenKH], [HangGPLX], [HangDT], [SoQD_KhaiGiang], [NgayQD_KhaiGiang], [NgayKG], [NgayBG], [MucTieuDT], [NgayThi], [NgaySH], [TongSoHV], [SoHVTotNghiep], [SoHVDuocCapGPLX], [ThoiGianDT], [SoNgayOnKT], [SoNgayThucHoc], [SoNgayNghiLe], [TongSoNgay], [GhiChu], [TrangThai], [NguoiTao], [NguoiSua], [NgayTao], [NgaySua], [TT_Xuly])
    SELECT s.[MaKH], s.[MaCSDT], s.[MaSoGTVT], s.[TenKH], s.[HangGPLX], s.[HangDT], s.[SoQD_KhaiGiang], s.[NgayQD_KhaiGiang], s.[NgayKG], s.[NgayBG], s.[MucTieuDT], s.[NgayThi], s.[NgaySH], s.[TongSoHV], s.[SoHVTotNghiep], s.[SoHVDuocCapGPLX], s.[ThoiGianDT], s.[SoNgayOnKT], s.[SoNgayThucHoc], s.[SoNgayNghiLe], s.[TongSoNgay], s.[GhiChu], s.[TrangThai], s.[NguoiTao], s.[NguoiSua], s.[NgayTao], s.[NgaySua], s.[TT_Xuly]
    FROM CSDT_V1.dbo.[KhoaHoc] s
    WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = s.MaKH)
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V2.dbo.[KhoaHoc] t
          WHERE t.MaKH = s.MaKH
      );

    SET @Rows = @@ROWCOUNT;
    INSERT INTO #RunLog(StepName, RowsAffected, Note)
    VALUES (N'KhoaHoc', @Rows, N'Insert missing records');
    PRINT CONCAT(N'Inserted KhoaHoc: ', @Rows);



    INSERT INTO CSDT_V2.dbo.[BaoCaoI] ([MaBCI], [MaCSDT], [MaKH], [SoBaoCao], [NgayBaoCao], [SoGP], [NgayCapGP], [LuuLuongGP], [SoHocSinh], [NgayKG], [NgayBG], [NgayTiepNhan], [NguoiTiepNhan], [ThoiGianTiepNhan], [ThoiGianDaoTao], [LuuLuong], [BoTriHocVienXeTap], [GhiChu], [TrangThai], [NguoiTao], [NguoiSua], [NgayTao], [NgaySua], [SoHSCanhBao], [TT_Xuly])
    SELECT s.[MaBCI], s.[MaCSDT], s.[MaKH], s.[SoBaoCao], s.[NgayBaoCao], s.[SoGP], s.[NgayCapGP], s.[LuuLuongGP], s.[SoHocSinh], s.[NgayKG], s.[NgayBG], s.[NgayTiepNhan], s.[NguoiTiepNhan], s.[ThoiGianTiepNhan], s.[ThoiGianDaoTao], s.[LuuLuong], s.[BoTriHocVienXeTap], s.[GhiChu], s.[TrangThai], s.[NguoiTao], s.[NguoiSua], s.[NgayTao], s.[NgaySua], s.[SoHSCanhBao], s.[TT_Xuly]
    FROM CSDT_V1.dbo.[BaoCaoI] s
    WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = s.MaKH)
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V2.dbo.[BaoCaoI] t
          WHERE t.MaBCI = s.MaBCI
      );

    SET @Rows = @@ROWCOUNT;
    INSERT INTO #RunLog(StepName, RowsAffected, Note)
    VALUES (N'BaoCaoI', @Rows, N'Insert missing records');
    PRINT CONCAT(N'Inserted BaoCaoI: ', @Rows);



    INSERT INTO CSDT_V2.dbo.[NguoiLX] ([MaDK], [DonViNhanHSo], [HoDemNLX], [TenNLX], [HoVaTen], [MaQuocTich], [NgaySinh], [NoiTT], [NoiTT_MaDVHC], [NoiTT_MaDVQL], [NoiCT], [NoiCT_MaDVHC], [NoiCT_MaDVQL], [SoCMT], [NgayCapCMT], [NoiCapCMT], [GhiChu], [TrangThai], [NguoiTao], [NguoiSua], [NgayTao], [NgaySua], [GioiTinh], [HoVaTenIn], [SO_CMND_CU])
    SELECT s.[MaDK], s.[DonViNhanHSo], s.[HoDemNLX], s.[TenNLX], s.[HoVaTen], s.[MaQuocTich], s.[NgaySinh], s.[NoiTT], s.[NoiTT_MaDVHC], s.[NoiTT_MaDVQL], s.[NoiCT], s.[NoiCT_MaDVHC], s.[NoiCT_MaDVQL], s.[SoCMT], s.[NgayCapCMT], s.[NoiCapCMT], s.[GhiChu], s.[TrangThai], s.[NguoiTao], s.[NguoiSua], s.[NgayTao], s.[NgaySua], s.[GioiTinh], s.[HoVaTenIn], s.[SO_CMND_CU]
    FROM CSDT_V1.dbo.[NguoiLX] s
    WHERE EXISTS (SELECT 1 FROM CSDT_V1.dbo.NguoiLX_HoSo hs JOIN #CourseList c ON c.MaKH = hs.MaKhoaHoc WHERE hs.MaDK = s.MaDK)
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V2.dbo.[NguoiLX] t
          WHERE t.MaDK = s.MaDK
      );

    SET @Rows = @@ROWCOUNT;
    INSERT INTO #RunLog(StepName, RowsAffected, Note)
    VALUES (N'NguoiLX', @Rows, N'Insert missing records');
    PRINT CONCAT(N'Inserted NguoiLX: ', @Rows);



    INSERT INTO CSDT_V2.dbo.[NguoiLX_HoSo] ([MaDK], [SoHoSo], [MaCSDT], [MaSoGTVT], [MaDVNhanHSo], [NgayNhanHSo], [NguoiNhanHSo], [NgayHenTra], [MaLoaiHs], [TT_XuLy], [DuongDanAnh], [ChatLuongAnh], [NgayThuNhanAnh], [NguoiThuNhanAnh], [SoGPLXDaCo], [HangGPLXDaCo], [DonViCapGPLXDaCo], [NoiCapGPLXDaCo], [NgayCapGPLXDaCo], [NgayHHGPLXDaCo], [NgayTTGPLXDaCo], [DonViHocLX], [NamHocLX], [HangGPLX], [SoNamLX], [SoKmLXAnToan], [GiayCNSK], [LyDoCapDoi], [MucDichCapDoi], [NoiDungSH], [MaKhoaHoc], [HangDaoTao], [SoGiayCNTN], [SoCCN], [MaBC1], [BC1_TuoiTS], [BC1_ThamNien], [MaBC2], [KetQuaBC2], [MaLyDoTCBC2], [MaKySH], [SoBD], [LanSH], [SoQDSH], [NgayQDSH], [KetQua_LyThuyet], [NhanXet_LyThuyet], [KetQua_Hinh], [NhanXet_Hinh], [KetQua_Duong], [NhanXet_Duong], [KetQuaSH], [SoQDTT], [NgayQDTT], [NguoiKy], [GhiChu], [NguoiTao], [NguoiSua], [NgayTao], [NgaySua], [SoGPLXTmp], [NgayKTBC1], [NguoiKTBC1], [NgayKTBC2], [NguoiKTBC2], [MaIn], [KetQuaDoiSanhTW], [GhiChuKQDSTW], [ChuKy], [TrangThai], [MaHTCap], [TT_XuLy_Old], [KQ_BC1], [KQ_BC1_GhiChu], [Transfer_flag], [VaoSoCNNSo], [NgayVaoSoCNN], [XepLoaiTotNghiep], [NgayCapCCN], [SoQuyetDinhTN], [NgayRaQDTN], [SoSoTN], [NgayVaoSoTN], [NgayInGiayTN], [NamcapLandau], [MaTrichNgang], [CoQuanQuanLyGPLX], [CHON_IN_GPLX], [KetQuaSHM], [NhanXet_MoPhong])
    SELECT s.[MaDK], s.[SoHoSo], s.[MaCSDT], s.[MaSoGTVT], s.[MaDVNhanHSo], s.[NgayNhanHSo], s.[NguoiNhanHSo], s.[NgayHenTra], s.[MaLoaiHs], s.[TT_XuLy], s.[DuongDanAnh], s.[ChatLuongAnh], s.[NgayThuNhanAnh], s.[NguoiThuNhanAnh], s.[SoGPLXDaCo], s.[HangGPLXDaCo], s.[DonViCapGPLXDaCo], s.[NoiCapGPLXDaCo], s.[NgayCapGPLXDaCo], s.[NgayHHGPLXDaCo], s.[NgayTTGPLXDaCo], s.[DonViHocLX], s.[NamHocLX], s.[HangGPLX], s.[SoNamLX], s.[SoKmLXAnToan], s.[GiayCNSK], s.[LyDoCapDoi], s.[MucDichCapDoi], s.[NoiDungSH], s.[MaKhoaHoc], s.[HangDaoTao], s.[SoGiayCNTN], s.[SoCCN], s.[MaBC1], s.[BC1_TuoiTS], s.[BC1_ThamNien], s.[MaBC2], s.[KetQuaBC2], s.[MaLyDoTCBC2], s.[MaKySH], s.[SoBD], s.[LanSH], s.[SoQDSH], s.[NgayQDSH], s.[KetQua_LyThuyet], s.[NhanXet_LyThuyet], s.[KetQua_Hinh], s.[NhanXet_Hinh], s.[KetQua_Duong], s.[NhanXet_Duong], s.[KetQuaSH], s.[SoQDTT], s.[NgayQDTT], s.[NguoiKy], s.[GhiChu], s.[NguoiTao], s.[NguoiSua], s.[NgayTao], s.[NgaySua], s.[SoGPLXTmp], s.[NgayKTBC1], s.[NguoiKTBC1], s.[NgayKTBC2], s.[NguoiKTBC2], s.[MaIn], s.[KetQuaDoiSanhTW], s.[GhiChuKQDSTW], s.[ChuKy], s.[TrangThai], s.[MaHTCap], s.[TT_XuLy_Old], s.[KQ_BC1], s.[KQ_BC1_GhiChu], s.[Transfer_flag], s.[VaoSoCNNSo], s.[NgayVaoSoCNN], s.[XepLoaiTotNghiep], s.[NgayCapCCN], s.[SoQuyetDinhTN], s.[NgayRaQDTN], s.[SoSoTN], s.[NgayVaoSoTN], s.[NgayInGiayTN], s.[NamcapLandau], s.[MaTrichNgang], s.[CoQuanQuanLyGPLX], s.[CHON_IN_GPLX], s.[KetQuaSHM], s.[NhanXet_MoPhong]
    FROM CSDT_V1.dbo.[NguoiLX_HoSo] s
    WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = s.MaKhoaHoc)
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V2.dbo.[NguoiLX_HoSo] t
          WHERE t.MaDK = s.MaDK
      );

    SET @Rows = @@ROWCOUNT;
    INSERT INTO #RunLog(StepName, RowsAffected, Note)
    VALUES (N'NguoiLX_HoSo', @Rows, N'Insert missing records');
    PRINT CONCAT(N'Inserted NguoiLX_HoSo: ', @Rows);



    INSERT INTO CSDT_V2.dbo.[NguoiLX_GPLX] ([MaDK], [SoGPLX], [HangGPLX], [SoHoSo], [SoGPLXCu], [NoiCapGPLX], [NgayCapGPLX], [CoQuanQLGPLX], [NgayHHGPLX], [NgayTTGPLX], [MoTaVN], [MoTaEN], [NguoiKy], [MaHTCap], [NoiHocGPLX], [NamHocGPLX], [DuongDanAnh], [HoTenDem], [TenNLX], [HoVaTen], [NgaySinh], [MaQuocTich], [NoiCT], [NoiCT_MaDVHC], [NoiCT_MaDVQL], [SoCMT], [SoSeri], [NoiIn], [NgayIn], [NgayTra], [NguoiTra], [NoiTra], [GhiChu], [NguoiTao], [NguoiSua], [NgayTao], [NgaySua], [TrangThai], [GioiTinh], [NgayTT_A1], [NgayTT_A2], [NgayTT_A3], [NgayTT_A4], [NgayTT_B1], [NgayTT_B2], [NgayTT_C], [NgayTT_D], [NgayTT_E], [NgayTT_F], [NgayTT_FB2], [NgayTT_FC], [NgayTT_FD], [NgayTT_FE])
    SELECT s.[MaDK], s.[SoGPLX], s.[HangGPLX], s.[SoHoSo], s.[SoGPLXCu], s.[NoiCapGPLX], s.[NgayCapGPLX], s.[CoQuanQLGPLX], s.[NgayHHGPLX], s.[NgayTTGPLX], s.[MoTaVN], s.[MoTaEN], s.[NguoiKy], s.[MaHTCap], s.[NoiHocGPLX], s.[NamHocGPLX], s.[DuongDanAnh], s.[HoTenDem], s.[TenNLX], s.[HoVaTen], s.[NgaySinh], s.[MaQuocTich], s.[NoiCT], s.[NoiCT_MaDVHC], s.[NoiCT_MaDVQL], s.[SoCMT], s.[SoSeri], s.[NoiIn], s.[NgayIn], s.[NgayTra], s.[NguoiTra], s.[NoiTra], s.[GhiChu], s.[NguoiTao], s.[NguoiSua], s.[NgayTao], s.[NgaySua], s.[TrangThai], s.[GioiTinh], s.[NgayTT_A1], s.[NgayTT_A2], s.[NgayTT_A3], s.[NgayTT_A4], s.[NgayTT_B1], s.[NgayTT_B2], s.[NgayTT_C], s.[NgayTT_D], s.[NgayTT_E], s.[NgayTT_F], s.[NgayTT_FB2], s.[NgayTT_FC], s.[NgayTT_FD], s.[NgayTT_FE]
    FROM CSDT_V1.dbo.[NguoiLX_GPLX] s
    WHERE EXISTS (SELECT 1 FROM CSDT_V1.dbo.NguoiLX_HoSo hs JOIN #CourseList c ON c.MaKH = hs.MaKhoaHoc WHERE hs.MaDK = s.MaDK)
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V2.dbo.[NguoiLX_GPLX] t
          WHERE t.MaDK = s.MaDK AND ISNULL(t.SoGPLX,'') = ISNULL(s.SoGPLX,'') AND ISNULL(t.HangGPLX,'') = ISNULL(s.HangGPLX,'')
      );

    SET @Rows = @@ROWCOUNT;
    INSERT INTO #RunLog(StepName, RowsAffected, Note)
    VALUES (N'NguoiLX_GPLX', @Rows, N'Insert missing records');
    PRINT CONCAT(N'Inserted NguoiLX_GPLX: ', @Rows);



    INSERT INTO CSDT_V2.dbo.[NguoiLXHS_GiayTo] ([MaGT], [MaDK], [SoHoSo], [TenGT], [TrangThai])
    SELECT s.[MaGT], s.[MaDK], s.[SoHoSo], s.[TenGT], s.[TrangThai]
    FROM CSDT_V1.dbo.[NguoiLXHS_GiayTo] s
    WHERE EXISTS (SELECT 1 FROM CSDT_V1.dbo.NguoiLX_HoSo hs JOIN #CourseList c ON c.MaKH = hs.MaKhoaHoc WHERE hs.MaDK = s.MaDK)
      AND NOT EXISTS (
          SELECT 1
          FROM CSDT_V2.dbo.[NguoiLXHS_GiayTo] t
          WHERE t.MaGT = s.MaGT AND t.MaDK = s.MaDK
      );

    SET @Rows = @@ROWCOUNT;
    INSERT INTO #RunLog(StepName, RowsAffected, Note)
    VALUES (N'NguoiLXHS_GiayTo', @Rows, N'Insert missing records');
    PRINT CONCAT(N'Inserted NguoiLXHS_GiayTo: ', @Rows);


    IF @CopyDaoTao = 1
    BEGIN
        INSERT INTO CSDT_V2.dbo.KhoaHoc_GiaoVien ([MaKH], [MaGV], [TenGV], [BienSoXe], [LoaiGV], [SoHV], [NgayHL], [NgayHetHL], [GhiChu], [TrangThai], [NguoiTao], [NguoiSua], [NgayTao], [NgaySua], [IsKhoaHocGiaoVien])
        SELECT s.[MaKH], s.[MaGV], s.[TenGV], s.[BienSoXe], s.[LoaiGV], s.[SoHV], s.[NgayHL], s.[NgayHetHL], s.[GhiChu], s.[TrangThai], s.[NguoiTao], s.[NguoiSua], s.[NgayTao], s.[NgaySua], CAST(1 AS bit)
        FROM CSDT_V1.dbo.KhoaHoc_GiaoVien s
        WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = s.MaKH)
          AND NOT EXISTS (
              SELECT 1
              FROM CSDT_V2.dbo.KhoaHoc_GiaoVien t
              WHERE t.MaKH = s.MaKH
                AND t.MaGV = s.MaGV
                AND ISNULL(t.LoaiGV,'') = ISNULL(s.LoaiGV,'')
                AND ISNULL(t.BienSoXe,'') = ISNULL(s.BienSoXe,'')
          );

        SET @Rows = @@ROWCOUNT;
        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'KhoaHoc_GiaoVien', @Rows, N'Optional @CopyDaoTao = 1');
        PRINT CONCAT(N'Inserted KhoaHoc_GiaoVien: ', @Rows);

        INSERT INTO CSDT_V2.dbo.LichHoc ([MaKH], [Thang], [Tuan], [TuNgay], [DenNgay], [GiaiDoan], [KiemTra], [GhiChu])
        SELECT s.[MaKH], s.[Thang], s.[Tuan], s.[TuNgay], s.[DenNgay], s.[GiaiDoan], s.[KiemTra], s.[GhiChu]
        FROM CSDT_V1.dbo.LichHoc s
        WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = s.MaKH)
          AND NOT EXISTS (
              SELECT 1
              FROM CSDT_V2.dbo.LichHoc t
              WHERE t.MaKH = s.MaKH
                AND t.Thang = s.Thang
                AND t.Tuan = s.Tuan
                AND t.TuNgay = s.TuNgay
                AND t.DenNgay = s.DenNgay
          );

        SET @Rows = @@ROWCOUNT;
        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'LichHoc', @Rows, N'Optional @CopyDaoTao = 1');
        PRINT CONCAT(N'Inserted LichHoc: ', @Rows);
    END;

    IF @CreateKhoaHocXeTap = 1
    BEGIN
        INSERT INTO CSDT_V2.dbo.KhoaHoc_XeTap
        (
            MaKH,
            BienSoXe,
            MaGV,
            MaHV,
            DiaDiem,
            GhiChu,
            TrangThai,
            NguoiTao,
            NguoiSua,
            NgayTao,
            NgaySua,
            NgayBD,
            NgayKT,
            IsKhoaHocXeTap,
            TenHV,
            TenGV
        )
        SELECT
            s.MaKH,
            s.BienSoXe,
            s.MaGV,
            NULL AS MaHV,
            NULL AS DiaDiem,
            s.GhiChu,
            s.TrangThai,
            s.NguoiTao,
            s.NguoiSua,
            s.NgayTao,
            s.NgaySua,
            s.NgayHL AS NgayBD,
            s.NgayHetHL AS NgayKT,
            CAST(1 AS bit) AS IsKhoaHocXeTap,
            NULL AS TenHV,
            s.TenGV
        FROM CSDT_V1.dbo.KhoaHoc_GiaoVien s
        WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = s.MaKH)
          AND ISNULL(s.BienSoXe, '') <> ''
          AND NOT EXISTS (
              SELECT 1
              FROM CSDT_V2.dbo.KhoaHoc_XeTap t
              WHERE t.MaKH = s.MaKH
                AND t.BienSoXe = s.BienSoXe
                AND ISNULL(t.MaGV,'') = ISNULL(s.MaGV,'')
          );

        SET @Rows = @@ROWCOUNT;
        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'KhoaHoc_XeTap', @Rows, N'Generated from KhoaHoc_GiaoVien.BienSoXe');
        PRINT CONCAT(N'Inserted KhoaHoc_XeTap generated from KhoaHoc_GiaoVien: ', @Rows);
    END;

    PRINT N'===== RUN LOG =====';
    SELECT * FROM #RunLog ORDER BY CreatedAt, StepName;

    PRINT N'===== KIỂM TRA SAU KHI CHUYỂN TRONG TRANSACTION =====';
    SELECT
        (SELECT COUNT(*) FROM #CourseList) AS TongKhoaDaQuet,
        (SELECT COUNT(*) FROM CSDT_V1.dbo.NguoiLX_HoSo hs WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = hs.MaKhoaHoc)) AS V1_TongHoSo,
        (SELECT COUNT(*) FROM CSDT_V2.dbo.NguoiLX_HoSo hs WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = hs.MaKhoaHoc)) AS V2_TongHoSoSauChuyen,
        (SELECT COUNT(*) FROM CSDT_V2.dbo.KhoaHoc kh WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = kh.MaKH)) AS V2_KhoaHoc,
        (SELECT COUNT(*) FROM CSDT_V2.dbo.BaoCaoI bc WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = bc.MaKH)) AS V2_BaoCaoI,
        (SELECT COUNT(*) FROM CSDT_V2.dbo.KhoaHoc_GiaoVien gv WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = gv.MaKH)) AS V2_KhoaHoc_GiaoVien,
        (SELECT COUNT(*) FROM CSDT_V2.dbo.LichHoc lh WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = lh.MaKH)) AS V2_LichHoc,
        (SELECT COUNT(*) FROM CSDT_V2.dbo.KhoaHoc_XeTap xt WHERE EXISTS (SELECT 1 FROM #CourseList c WHERE c.MaKH = xt.MaKH)) AS V2_KhoaHoc_XeTap;

    IF @DryRun = 1
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'DRY RUN: Đã ROLLBACK. Chưa ghi dữ liệu vào CSDT_V2.';
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION;
        PRINT N'COMMIT: Đã ghi dữ liệu vào CSDT_V2.';
    END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT N'LỖI: đã ROLLBACK toàn bộ transaction.';
    PRINT ERROR_MESSAGE();
    THROW;
END CATCH;
GO
