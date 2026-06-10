/*
    QLHV_APP DATABASE SCHEMA
    Trung tâm Đào tạo lái xe Thành Công

    Nguyên tắc:
    - QLHV_APP là database trung tâm của phần mềm quản lý mới.
    - SQL V2 là nguồn gốc chính xác, chỉ đọc và đồng bộ một chiều sang QLHV_APP.
    - TTTC_WebSite chỉ đọc QLHV_APP qua API an toàn.
    - Không ghi trực tiếp vào SQL V1/V2 từ website/frontend.
    - Không DELETE vật lý; dùng IsDeleted.
    - Dùng RowVersion để chống ghi đè đồng thời.
*/

IF DB_ID(N'QLHV_APP') IS NULL
BEGIN
    CREATE DATABASE QLHV_APP;
END
GO

USE QLHV_APP;
GO


/* =========================================================
   1. CẤU HÌNH / USER / ROLE
   ========================================================= */

IF OBJECT_ID(N'dbo.App_CauHinh', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_CauHinh (
        CauHinhId BIGINT IDENTITY(1,1) PRIMARY KEY,
        NhomCauHinh NVARCHAR(100) NOT NULL,
        MaCauHinh NVARCHAR(150) NOT NULL,
        GiaTri NVARCHAR(MAX) NULL,
        GiaTriMaHoa BIT NOT NULL DEFAULT 0,
        MoTa NVARCHAR(500) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_App_CauHinh UNIQUE (NhomCauHinh, MaCauHinh)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.App_CauHinh WHERE NhomCauHinh = N'Storage' AND MaCauHinh = N'StudentPhotoRoot')
BEGIN
    INSERT INTO dbo.App_CauHinh (NhomCauHinh, MaCauHinh, GiaTri, MoTa)
    VALUES
    (N'Storage', N'StudentPhotoRoot', N'D:\IM_GPLX', N'Thư mục gốc ảnh JP2 học viên'),
    (N'Storage', N'TeacherPhotoRoot', N'D:\QLHV_FILES\GiaoVien', N'Thư mục gốc ảnh giáo viên'),
    (N'Storage', N'VehiclePdfRoot', N'D:\QLHV_FILES\XeTap', N'Thư mục gốc hồ sơ PDF xe tập lái');
END
GO

IF OBJECT_ID(N'dbo.App_Role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_Role (
        RoleId INT IDENTITY(1,1) PRIMARY KEY,
        RoleCode NVARCHAR(50) NOT NULL UNIQUE,
        RoleName NVARCHAR(150) NOT NULL,
        MoTa NVARCHAR(500) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_User', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_User (
        UserId BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserName NVARCHAR(100) NOT NULL UNIQUE,
        DisplayName NVARCHAR(200) NOT NULL,
        PasswordHash NVARCHAR(500) NULL,
        Email NVARCHAR(200) NULL,
        Phone NVARCHAR(50) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        LastLoginAt DATETIME2 NULL,
        FailedLoginCount INT NOT NULL DEFAULT 0,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_UserRole', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_UserRole (
        UserRoleId BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId BIGINT NOT NULL,
        RoleId INT NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        CONSTRAINT FK_App_UserRole_User FOREIGN KEY (UserId) REFERENCES dbo.App_User(UserId),
        CONSTRAINT FK_App_UserRole_Role FOREIGN KEY (RoleId) REFERENCES dbo.App_Role(RoleId),
        CONSTRAINT UQ_App_UserRole UNIQUE (UserId, RoleId)
    );
END
GO


/* =========================================================
   2. KHÓA HỌC
   ========================================================= */

IF OBJECT_ID(N'dbo.App_KhoaHoc', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_KhoaHoc (
        KhoaHocId BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaKhoa NVARCHAR(50) NOT NULL,
        TenKhoa NVARCHAR(255) NULL,
        HangDaoTao NVARCHAR(20) NULL,
        NgayKhaiGiang DATE NULL,
        NgayBeGiang DATE NULL,
        NgayBatDauThucHanh DATE NULL,
        TongSoHocVien INT NULL,
        LuuLuongDaoTao INT NULL,
        TrangThai NVARCHAR(50) NULL,
        GhiChuV2 NVARCHAR(500) NULL,
        GhiChuNoiBo NVARCHAR(1000) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        LastSyncStatus NVARCHAR(50) NULL,
        LastSyncMessage NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_App_KhoaHoc_MaKhoa UNIQUE (MaKhoa)
    );
END
GO


/* =========================================================
   3. HỌC VIÊN
   ========================================================= */

IF OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_HocVien (
        HocVienId BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaDK NVARCHAR(50) NOT NULL,
        MaDK_V1 NVARCHAR(50) NULL,
        MaDK_V2 NVARCHAR(50) NULL,
        MaKhoa NVARCHAR(50) NULL,
        TenKhoa NVARCHAR(255) NULL,
        MaHangDT NVARCHAR(20) NULL,
        HangGPLXHoc NVARCHAR(20) NULL,
        HoTen NVARCHAR(255) NULL,
        NgaySinh DATE NULL,
        GioiTinh NVARCHAR(20) NULL,
        SoCCCD NVARCHAR(20) NULL,
        DiaChiThuongTru NVARCHAR(500) NULL,
        SoGPLXDaCo NVARCHAR(50) NULL,
        HangGPLXDaCo NVARCHAR(20) NULL,
        AnhRelativePath NVARCHAR(500) NULL,
        ChatLuongAnh INT NULL,
        NgayThuNhanAnh DATETIME2 NULL,
        NguoiThuNhanAnh NVARCHAR(100) NULL,
        NguoiNhanHoSo NVARCHAR(150) NULL,
        GhiChuNoiBo NVARCHAR(1000) NULL,
        DaDoiChieuCCCD BIT NOT NULL DEFAULT 0,
        DaInThe BIT NOT NULL DEFAULT 0,
        DaTaoXML BIT NOT NULL DEFAULT 0,
        TrangThaiKiemTra NVARCHAR(100) NULL,
        CanhBaoDuLieu NVARCHAR(1000) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        LastSyncStatus NVARCHAR(50) NULL,
        LastSyncMessage NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_App_HocVien_MaDK UNIQUE (MaDK)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_HocVien_MaDK' AND object_id = OBJECT_ID(N'dbo.App_HocVien'))
    CREATE NONCLUSTERED INDEX IX_App_HocVien_MaDK ON dbo.App_HocVien(MaDK) WHERE IsDeleted = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_HocVien_SoCCCD' AND object_id = OBJECT_ID(N'dbo.App_HocVien'))
    CREATE NONCLUSTERED INDEX IX_App_HocVien_SoCCCD ON dbo.App_HocVien(SoCCCD) WHERE IsDeleted = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_HocVien_MaKhoa' AND object_id = OBJECT_ID(N'dbo.App_HocVien'))
    CREATE NONCLUSTERED INDEX IX_App_HocVien_MaKhoa ON dbo.App_HocVien(MaKhoa) WHERE IsDeleted = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_HocVien_MaHangDT' AND object_id = OBJECT_ID(N'dbo.App_HocVien'))
    CREATE NONCLUSTERED INDEX IX_App_HocVien_MaHangDT ON dbo.App_HocVien(MaHangDT) WHERE IsDeleted = 0;
GO


/* =========================================================
   4. GIÁO VIÊN
   ========================================================= */

IF OBJECT_ID(N'dbo.App_GiaoVien', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_GiaoVien (
        GiaoVienId BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaGV NVARCHAR(20) NOT NULL,
        HoTen NVARCHAR(255) NOT NULL,
        HoTenDem NVARCHAR(150) NULL,
        TenGV NVARCHAR(100) NULL,
        NgaySinh DATE NULL,
        SoCCCD NVARCHAR(20) NULL,
        GioiTinh NVARCHAR(20) NULL,
        DienThoai NVARCHAR(50) NULL,
        DiaChi NVARCHAR(500) NULL,
        HinhThucTuyenDung NVARCHAR(100) NULL,
        HopDongThoiHan NVARCHAR(100) NULL,
        TrinhDoVanHoa NVARCHAR(100) NULL,
        TrinhDoChuyenMon NVARCHAR(255) NULL,
        TrinhDoSuPham NVARCHAR(255) NULL,
        HangGPLX NVARCHAR(20) NULL,
        NgayCapGPLX DATE NULL,
        NgayHetHanGPLX DATE NULL,
        NgayTrungTuyen DATE NULL,
        ThamNienLaiXe INT NULL,
        MonHocGiangDay NVARCHAR(1000) NULL,
        AnhRelativePath NVARCHAR(500) NULL,
        GhiChu NVARCHAR(1000) NULL,
        TrangThai NVARCHAR(50) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        LastSyncStatus NVARCHAR(50) NULL,
        LastSyncMessage NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_App_GiaoVien_MaGV UNIQUE (MaGV)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_GiaoVien_HoTen' AND object_id = OBJECT_ID(N'dbo.App_GiaoVien'))
    CREATE NONCLUSTERED INDEX IX_App_GiaoVien_HoTen ON dbo.App_GiaoVien(HoTen) WHERE IsDeleted = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_GiaoVien_SoCCCD' AND object_id = OBJECT_ID(N'dbo.App_GiaoVien'))
    CREATE NONCLUSTERED INDEX IX_App_GiaoVien_SoCCCD ON dbo.App_GiaoVien(SoCCCD) WHERE IsDeleted = 0;
GO


/* =========================================================
   5. XE TẬP LÁI
   ========================================================= */

IF OBJECT_ID(N'dbo.App_XeTap', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_XeTap (
        XeTapId BIGINT IDENTITY(1,1) PRIMARY KEY,
        BienSoXe NVARCHAR(20) NOT NULL,
        SoDK NVARCHAR(50) NULL,
        SoHuu BIT NULL,
        XeCuaCoSoDaoTao BIT NULL,
        XeHopDong BIT NULL,
        NhanHieu NVARCHAR(100) NULL,
        LoaiXe NVARCHAR(100) NULL,
        MacXe NVARCHAR(100) NULL,
        HangXe NVARCHAR(50) NULL,
        HangGPLXXe NVARCHAR(20) NULL,
        MauXe NVARCHAR(100) NULL,
        NamSX INT NULL,
        SoDongCo NVARCHAR(50) NULL,
        SoKhung NVARCHAR(50) NULL,
        GiayPhepXTL BIT NULL,
        SoGPXTL NVARCHAR(50) NULL,
        CoQuanCapGPXTL NVARCHAR(255) NULL,
        NgayCapGPXTL DATE NULL,
        NgayHetHanGPXTL DATE NULL,
        HeThongPhanhPhu BIT NULL,
        BaoHiem BIT NULL,
        TuyenDuong NVARCHAR(500) NULL,
        ChatLuong NVARCHAR(100) NULL,
        SoGCNKiemDinh NVARCHAR(100) NULL,
        NgayCapGCNKD DATE NULL,
        NgayHetHanGCNKD DATE NULL,
        AnhRelativePath NVARCHAR(500) NULL,
        GVQuanLyMa NVARCHAR(20) NULL,
        GVQuanLyTen NVARCHAR(255) NULL,
        GhiChuV2 NVARCHAR(500) NULL,
        GhiChuNoiBo NVARCHAR(1000) NULL,
        TrangThai NVARCHAR(50) NULL,
        CanhBaoDuLieu NVARCHAR(1000) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        LastSyncStatus NVARCHAR(50) NULL,
        LastSyncMessage NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_App_XeTap_BienSoXe UNIQUE (BienSoXe)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_App_XeTap_SoDK' AND object_id = OBJECT_ID(N'dbo.App_XeTap'))
    CREATE UNIQUE INDEX UX_App_XeTap_SoDK ON dbo.App_XeTap(SoDK) WHERE IsDeleted = 0 AND SoDK IS NOT NULL AND SoDK <> '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_App_XeTap_SoGPXTL' AND object_id = OBJECT_ID(N'dbo.App_XeTap'))
    CREATE UNIQUE INDEX UX_App_XeTap_SoGPXTL ON dbo.App_XeTap(SoGPXTL) WHERE IsDeleted = 0 AND SoGPXTL IS NOT NULL AND SoGPXTL <> '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_App_XeTap_SoDongCo' AND object_id = OBJECT_ID(N'dbo.App_XeTap'))
    CREATE UNIQUE INDEX UX_App_XeTap_SoDongCo ON dbo.App_XeTap(SoDongCo) WHERE IsDeleted = 0 AND SoDongCo IS NOT NULL AND SoDongCo <> '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_App_XeTap_SoKhung' AND object_id = OBJECT_ID(N'dbo.App_XeTap'))
    CREATE UNIQUE INDEX UX_App_XeTap_SoKhung ON dbo.App_XeTap(SoKhung) WHERE IsDeleted = 0 AND SoKhung IS NOT NULL AND SoKhung <> '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_App_XeTap_GVQuanLyMa' AND object_id = OBJECT_ID(N'dbo.App_XeTap'))
    CREATE NONCLUSTERED INDEX IX_App_XeTap_GVQuanLyMa ON dbo.App_XeTap(GVQuanLyMa) WHERE IsDeleted = 0;
GO


/* =========================================================
   6. PHÂN CÔNG XE / GIÁO VIÊN THEO KHÓA
   ========================================================= */

IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_KhoaHoc_XeTap (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaKhoa NVARCHAR(50) NOT NULL,
        BienSoXe NVARCHAR(20) NOT NULL,
        MaGV NVARCHAR(20) NULL,
        TenGV NVARCHAR(255) NULL,
        NgayBatDau DATE NULL,
        NgayKetThuc DATE NULL,
        GhiChu NVARCHAR(500) NULL,
        TrangThai NVARCHAR(50) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_KhoaHoc_GiaoVien (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaKhoa NVARCHAR(50) NOT NULL,
        MaGV NVARCHAR(20) NOT NULL,
        TenGV NVARCHAR(255) NULL,
        MaMonHoc NVARCHAR(50) NULL,
        TenMonHoc NVARCHAR(255) NULL,
        BienSoXe NVARCHAR(20) NULL,
        NgayBatDau DATE NULL,
        NgayKetThuc DATE NULL,
        GhiChu NVARCHAR(500) NULL,
        TrangThai NVARCHAR(50) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO


/* =========================================================
   7. KẾT QUẢ TỐT NGHIỆP / SÁT HẠCH / THI LẠI
   ========================================================= */

IF OBJECT_ID(N'dbo.App_KetQuaTotNghiep', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_KetQuaTotNghiep (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaDK NVARCHAR(50) NOT NULL,
        MaKhoa NVARCHAR(50) NULL,
        KQLyThuyet NVARCHAR(50) NULL,
        KQThucHanh NVARCHAR(50) NULL,
        KetLuan NVARCHAR(100) NULL,
        NgayXet DATE NULL,
        GhiChu NVARCHAR(1000) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_KySatHach', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_KySatHach (
        KySatHachId BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaKySH NVARCHAR(50) NOT NULL,
        TenKySH NVARCHAR(255) NULL,
        NgaySatHach DATE NULL,
        HangGPLX NVARCHAR(20) NULL,
        DiaDiem NVARCHAR(255) NULL,
        TrangThai NVARCHAR(50) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_App_KySatHach_MaKySH UNIQUE (MaKySH)
    );
END
GO

IF OBJECT_ID(N'dbo.App_KetQuaSatHach', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_KetQuaSatHach (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaKySH NVARCHAR(50) NOT NULL,
        MaDK NVARCHAR(50) NOT NULL,
        MaKhoa NVARCHAR(50) NULL,
        KetQuaLyThuyet NVARCHAR(50) NULL,
        KetQuaMoPhong NVARCHAR(50) NULL,
        KetQuaThucHanh NVARCHAR(50) NULL,
        KetQuaDuongTruong NVARCHAR(50) NULL,
        KetLuan NVARCHAR(100) NULL,
        VangThi BIT NOT NULL DEFAULT 0,
        RotMon NVARCHAR(255) NULL,
        DuDieuKienThiLai BIT NOT NULL DEFAULT 0,
        GhiChu NVARCHAR(1000) NULL,
        SourceOfTruth NVARCHAR(30) NOT NULL DEFAULT N'V2',
        V2RowHash NVARCHAR(64) NULL,
        LastSyncFromV2At DATETIME2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_DangKyThiLai', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_DangKyThiLai (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        MaDK NVARCHAR(50) NOT NULL,
        MaKhoa NVARCHAR(50) NULL,
        MaKySHCu NVARCHAR(50) NULL,
        MaKySHMoi NVARCHAR(50) NULL,
        MonThiLai NVARCHAR(255) NULL,
        LyDo NVARCHAR(500) NULL,
        TrangThai NVARCHAR(50) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO


/* =========================================================
   8. TÀI LIỆU / IMPORT / OCR / LOG
   ========================================================= */

IF OBJECT_ID(N'dbo.App_TaiLieu', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_TaiLieu (
        TaiLieuId BIGINT IDENTITY(1,1) PRIMARY KEY,
        EntityType NVARCHAR(50) NOT NULL,
        EntityId NVARCHAR(100) NULL,
        EntityKey NVARCHAR(100) NULL,
        LoaiTaiLieu NVARCHAR(100) NULL,
        TenFileGoc NVARCHAR(255) NOT NULL,
        RelativePath NVARCHAR(500) NOT NULL,
        StorageProvider NVARCHAR(50) NOT NULL DEFAULT N'LocalDisk',
        FileHash NVARCHAR(64) NULL,
        FileSize BIGINT NULL,
        MimeType NVARCHAR(100) NULL,
        TrangThaiDocAI NVARCHAR(50) NULL,
        DiemTinCay DECIMAL(5,2) NULL,
        KetQuaDocJson NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_ImportBatch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_ImportBatch (
        ImportBatchId BIGINT IDENTITY(1,1) PRIMARY KEY,
        LoaiImport NVARCHAR(50) NOT NULL,
        EntityType NVARCHAR(50) NOT NULL,
        FileName NVARCHAR(255) NULL,
        RelativePath NVARCHAR(500) NULL,
        SuggestedCourseCode NVARCHAR(50) NULL,
        TotalRows INT NOT NULL DEFAULT 0,
        MatchedRows INT NOT NULL DEFAULT 0,
        WarningRows INT NOT NULL DEFAULT 0,
        ErrorRows INT NOT NULL DEFAULT 0,
        ApprovedRows INT NOT NULL DEFAULT 0,
        TrangThai NVARCHAR(50) NOT NULL DEFAULT N'Pending',
        KetQuaJson NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        DeletedAt DATETIME2 NULL,
        DeletedBy NVARCHAR(100) NULL,
        DeleteReason NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_OcrExtractResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_OcrExtractResult (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        ImportBatchId BIGINT NULL,
        TaiLieuId BIGINT NULL,
        EntityType NVARCHAR(50) NOT NULL,
        EntityKey NVARCHAR(100) NULL,
        FieldName NVARCHAR(100) NOT NULL,
        ExtractedValue NVARCHAR(500) NULL,
        CorrectedValue NVARCHAR(500) NULL,
        ApprovedValue NVARCHAR(500) NULL,
        Confidence DECIMAL(5,2) NULL,
        IsCorrected BIT NOT NULL DEFAULT 0,
        CorrectionReason NVARCHAR(500) NULL,
        CompareValue NVARCHAR(500) NULL,
        CompareStatus NVARCHAR(50) NULL,
        ModelVersion NVARCHAR(100) NULL,
        PromptVersion NVARCHAR(100) NULL,
        OcrEngine NVARCHAR(100) NULL,
        IsApproved BIT NOT NULL DEFAULT 0,
        ApprovedBy NVARCHAR(100) NULL,
        ApprovedAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CONSTRAINT FK_App_OcrExtractResult_ImportBatch FOREIGN KEY (ImportBatchId) REFERENCES dbo.App_ImportBatch(ImportBatchId),
        CONSTRAINT FK_App_OcrExtractResult_TaiLieu FOREIGN KEY (TaiLieuId) REFERENCES dbo.App_TaiLieu(TaiLieuId)
    );
END
GO

IF OBJECT_ID(N'dbo.App_DongBoLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_DongBoLog (
        DongBoLogId BIGINT IDENTITY(1,1) PRIMARY KEY,
        JobName NVARCHAR(150) NOT NULL,
        EntityType NVARCHAR(50) NOT NULL,
        SourceSystem NVARCHAR(50) NOT NULL DEFAULT N'V2',
        StartedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        EndedAt DATETIME2 NULL,
        DurationMs BIGINT NULL,
        Status NVARCHAR(50) NOT NULL,
        TotalRead INT NOT NULL DEFAULT 0,
        TotalInserted INT NOT NULL DEFAULT 0,
        TotalUpdated INT NOT NULL DEFAULT 0,
        TotalSkipped INT NOT NULL DEFAULT 0,
        TotalError INT NOT NULL DEFAULT 0,
        RetryCount INT NOT NULL DEFAULT 0,
        ErrorMessage NVARCHAR(MAX) NULL,
        DetailJson NVARCHAR(MAX) NULL,
        CreatedBy NVARCHAR(100) NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_AuditLog (
        AuditLogId BIGINT IDENTITY(1,1) PRIMARY KEY,
        ChucNang NVARCHAR(100) NOT NULL,
        HanhDong NVARCHAR(100) NOT NULL,
        EntityType NVARCHAR(100) NULL,
        EntityId NVARCHAR(100) NULL,
        EntityKey NVARCHAR(100) NULL,
        DuLieuTruoc NVARCHAR(MAX) NULL,
        DuLieuSau NVARCHAR(MAX) NULL,
        KetQua NVARCHAR(50) NOT NULL,
        Loi NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL,
        ClientIp NVARCHAR(50) NULL,
        UserAgent NVARCHAR(500) NULL
    );
END
GO

IF OBJECT_ID(N'dbo.App_PrintExportLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.App_PrintExportLog (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        LoaiChucNang NVARCHAR(100) NOT NULL,
        EntityType NVARCHAR(50) NULL,
        EntityKey NVARCHAR(100) NULL,
        MauXuat NVARCHAR(150) NULL,
        FileName NVARCHAR(255) NULL,
        RelativePath NVARCHAR(500) NULL,
        FilterJson NVARCHAR(MAX) NULL,
        TotalRecords INT NULL,
        KetQua NVARCHAR(50) NOT NULL,
        Loi NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy NVARCHAR(100) NULL
    );
END
GO


/* =========================================================
   9. VIEW CHO WEBSITE TTTC_WebSite
   ========================================================= */

CREATE OR ALTER VIEW dbo.vw_Web_HocVienTraCuu
AS
SELECT
    HocVienId,
    MaDK,
    HoTen,
    MaKhoa,
    TenKhoa,
    MaHangDT,
    HangGPLXHoc,
    TrangThaiKiemTra,
    DaInThe,
    UpdatedAt
FROM dbo.App_HocVien
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_Web_KhoaHoc
AS
SELECT
    KhoaHocId,
    MaKhoa,
    TenKhoa,
    HangDaoTao,
    NgayKhaiGiang,
    NgayBeGiang,
    TongSoHocVien,
    LuuLuongDaoTao,
    TrangThai
FROM dbo.App_KhoaHoc
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_Web_GiaoVienCongKhai
AS
SELECT
    GiaoVienId,
    MaGV,
    HoTen,
    HangGPLX,
    MonHocGiangDay,
    TrangThai
FROM dbo.App_GiaoVien
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vw_Web_XeTapCongKhai
AS
SELECT
    XeTapId,
    BienSoXe,
    NhanHieu,
    LoaiXe,
    HangGPLXXe,
    NamSX,
    TrangThai
FROM dbo.App_XeTap
WHERE IsDeleted = 0;
GO

/* =========================================================
   10. STORED PROCEDURE MẪU: UPDATE CÓ ROWVERSION
   ========================================================= */

CREATE OR ALTER PROCEDURE dbo.usp_App_HocVien_UpdateNguoiNhanHoSo
    @HocVienId BIGINT,
    @NguoiNhanHoSo NVARCHAR(150),
    @UpdatedBy NVARCHAR(100),
    @RowVersion VARBINARY(8)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.App_HocVien
    SET
        NguoiNhanHoSo = @NguoiNhanHoSo,
        UpdatedAt = SYSDATETIME(),
        UpdatedBy = @UpdatedBy
    WHERE
        HocVienId = @HocVienId
        AND RowVersion = @RowVersion
        AND IsDeleted = 0;

    IF @@ROWCOUNT = 0
    BEGIN
        THROW 51001, N'Dữ liệu đã được cập nhật bởi người khác. Vui lòng tải lại trang trước khi chỉnh sửa.', 1;
    END
END
GO

PRINT N'QLHV_APP database schema created successfully.';
GO
/* =========================================================
   11. BỔ SUNG INDEX TRA CỨU / JSON CHECK / LOOKUP PERFORMANCE
   Bổ sung theo quy chuẩn hiệu năng cho QLHV_APP
   ========================================================= */

-- 11.1. Index cho bảng phân công khóa học - xe tập lái
IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KhoaHoc_XeTap_MaKhoa'
      AND object_id = OBJECT_ID(N'dbo.App_KhoaHoc_XeTap')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_XeTap_MaKhoa
    ON dbo.App_KhoaHoc_XeTap(MaKhoa)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KhoaHoc_XeTap_BienSoXe'
      AND object_id = OBJECT_ID(N'dbo.App_KhoaHoc_XeTap')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_XeTap_BienSoXe
    ON dbo.App_KhoaHoc_XeTap(BienSoXe)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KhoaHoc_XeTap', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KhoaHoc_XeTap_MaGV'
      AND object_id = OBJECT_ID(N'dbo.App_KhoaHoc_XeTap')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_XeTap_MaGV
    ON dbo.App_KhoaHoc_XeTap(MaGV)
    WHERE IsDeleted = 0 AND MaGV IS NOT NULL;
END
GO

-- 11.2. Index cho bảng phân công khóa học - giáo viên
IF OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KhoaHoc_GiaoVien_MaKhoa'
      AND object_id = OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_GiaoVien_MaKhoa
    ON dbo.App_KhoaHoc_GiaoVien(MaKhoa)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KhoaHoc_GiaoVien_MaGV'
      AND object_id = OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_GiaoVien_MaGV
    ON dbo.App_KhoaHoc_GiaoVien(MaGV)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KhoaHoc_GiaoVien_MaMonHoc'
      AND object_id = OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_GiaoVien_MaMonHoc
    ON dbo.App_KhoaHoc_GiaoVien(MaMonHoc)
    WHERE IsDeleted = 0 AND MaMonHoc IS NOT NULL;
END
GO

-- 11.3. Index bổ sung cho kết quả tốt nghiệp / sát hạch / thi lại
IF OBJECT_ID(N'dbo.App_KetQuaTotNghiep', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KetQuaTotNghiep_MaKhoa'
      AND object_id = OBJECT_ID(N'dbo.App_KetQuaTotNghiep')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KetQuaTotNghiep_MaKhoa
    ON dbo.App_KetQuaTotNghiep(MaKhoa)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KetQuaTotNghiep', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KetQuaTotNghiep_MaDK'
      AND object_id = OBJECT_ID(N'dbo.App_KetQuaTotNghiep')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KetQuaTotNghiep_MaDK
    ON dbo.App_KetQuaTotNghiep(MaDK)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KetQuaSatHach', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KetQuaSatHach_MaKySH'
      AND object_id = OBJECT_ID(N'dbo.App_KetQuaSatHach')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KetQuaSatHach_MaKySH
    ON dbo.App_KetQuaSatHach(MaKySH)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_KetQuaSatHach', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_KetQuaSatHach_MaDK'
      AND object_id = OBJECT_ID(N'dbo.App_KetQuaSatHach')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_KetQuaSatHach_MaDK
    ON dbo.App_KetQuaSatHach(MaDK)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_DangKyThiLai', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_DangKyThiLai_MaDK'
      AND object_id = OBJECT_ID(N'dbo.App_DangKyThiLai')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_DangKyThiLai_MaDK
    ON dbo.App_DangKyThiLai(MaDK)
    WHERE IsDeleted = 0;
END
GO

-- 11.4. Index cho tài liệu / import / OCR
IF OBJECT_ID(N'dbo.App_TaiLieu', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_TaiLieu_Entity'
      AND object_id = OBJECT_ID(N'dbo.App_TaiLieu')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_TaiLieu_Entity
    ON dbo.App_TaiLieu(EntityType, EntityKey)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_ImportBatch', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_ImportBatch_EntityType_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.App_ImportBatch')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_ImportBatch_EntityType_CreatedAt
    ON dbo.App_ImportBatch(EntityType, CreatedAt DESC)
    WHERE IsDeleted = 0;
END
GO

IF OBJECT_ID(N'dbo.App_OcrExtractResult', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_OcrExtractResult_ImportBatchId'
      AND object_id = OBJECT_ID(N'dbo.App_OcrExtractResult')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_OcrExtractResult_ImportBatchId
    ON dbo.App_OcrExtractResult(ImportBatchId);
END
GO

IF OBJECT_ID(N'dbo.App_OcrExtractResult', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_OcrExtractResult_IsCorrected'
      AND object_id = OBJECT_ID(N'dbo.App_OcrExtractResult')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_OcrExtractResult_IsCorrected
    ON dbo.App_OcrExtractResult(IsCorrected, EntityType, FieldName)
    WHERE IsCorrected = 1;
END
GO

-- 11.5. Index cho Audit Log / Đồng bộ Log / Xuất file
IF OBJECT_ID(N'dbo.App_AuditLog', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_AuditLog_Entity'
      AND object_id = OBJECT_ID(N'dbo.App_AuditLog')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_AuditLog_Entity
    ON dbo.App_AuditLog(EntityType, EntityKey, CreatedAt DESC);
END
GO

IF OBJECT_ID(N'dbo.App_AuditLog', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_AuditLog_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.App_AuditLog')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_AuditLog_CreatedAt
    ON dbo.App_AuditLog(CreatedAt DESC);
END
GO

IF OBJECT_ID(N'dbo.App_DongBoLog', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_DongBoLog_EntityType_StartedAt'
      AND object_id = OBJECT_ID(N'dbo.App_DongBoLog')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_DongBoLog_EntityType_StartedAt
    ON dbo.App_DongBoLog(EntityType, StartedAt DESC);
END
GO

IF OBJECT_ID(N'dbo.App_PrintExportLog', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_App_PrintExportLog_Entity'
      AND object_id = OBJECT_ID(N'dbo.App_PrintExportLog')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_App_PrintExportLog_Entity
    ON dbo.App_PrintExportLog(EntityType, EntityKey, CreatedAt DESC);
END
GO

-- 11.6. CHECK CONSTRAINT bảo đảm Audit Log lưu JSON hợp lệ
-- Lưu ý: yêu cầu SQL Server 2016 trở lên để dùng ISJSON.
IF OBJECT_ID(N'dbo.App_AuditLog', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CHK_App_AuditLog_DuLieuTruoc_JSON'
      AND parent_object_id = OBJECT_ID(N'dbo.App_AuditLog')
)
BEGIN
    ALTER TABLE dbo.App_AuditLog
    ADD CONSTRAINT CHK_App_AuditLog_DuLieuTruoc_JSON
    CHECK (DuLieuTruoc IS NULL OR ISJSON(DuLieuTruoc) > 0);
END
GO

IF OBJECT_ID(N'dbo.App_AuditLog', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CHK_App_AuditLog_DuLieuSau_JSON'
      AND parent_object_id = OBJECT_ID(N'dbo.App_AuditLog')
)
BEGIN
    ALTER TABLE dbo.App_AuditLog
    ADD CONSTRAINT CHK_App_AuditLog_DuLieuSau_JSON
    CHECK (DuLieuSau IS NULL OR ISJSON(DuLieuSau) > 0);
END
GO

PRINT N'QLHV_APP performance indexes and JSON constraints added successfully.';
GO
