USE [QLHV_APP];
GO
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.App_KhoaHoc', N'U') IS NULL
        THROW 51000, N'Thieu bang dbo.App_KhoaHoc.', 1;
    IF OBJECT_ID(N'dbo.App_GiaoVien', N'U') IS NULL
        THROW 51000, N'Thieu bang dbo.App_GiaoVien.', 1;
    IF OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien', N'U') IS NULL
        THROW 51000, N'Thieu bang dbo.App_KhoaHoc_GiaoVien.', 1;

    /* Course source identity and source-owned business fields. */
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SourceProfileCode') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SourceProfileCode NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SourceMaKhoaHoc') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SourceMaKhoaHoc NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SourceHash') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SourceHash NVARCHAR(64) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'MaCSDT') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD MaCSDT NVARCHAR(6) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'MaSoGTVT') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD MaSoGTVT NVARCHAR(6) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'HangGPLX') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD HangGPLX NVARCHAR(20) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SoQuyetDinhKhaiGiang') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SoQuyetDinhKhaiGiang NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'NgayQuyetDinhKhaiGiang') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD NgayQuyetDinhKhaiGiang DATE NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'MucTieuDaoTao') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD MucTieuDaoTao NVARCHAR(1000) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'NgayThi') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD NgayThi DATE NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'NgaySatHach') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD NgaySatHach DATE NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SoHocVienTotNghiep') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SoHocVienTotNghiep INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SoHocVienDuocCapGPLX') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SoHocVienDuocCapGPLX INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'ThoiGianDaoTao') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD ThoiGianDaoTao INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SoNgayOnKiemTra') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SoNgayOnKiemTra INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SoNgayThucHoc') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SoNgayThucHoc INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'SoNgayNghiLe') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD SoNgayNghiLe INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'TongSoNgay') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD TongSoNgay INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'TrangThaiNguon') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD TrangThaiNguon BIT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'TtXuLy') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD TtXuLy INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'HinhThucDaoTao') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD HinhThucDaoTao INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'CreatedAtUtc') IS NULL
        ALTER TABLE dbo.App_KhoaHoc ADD CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_App_KhoaHoc_CreatedAtUtc DEFAULT SYSUTCDATETIME() WITH VALUES;
    IF COL_LENGTH(N'dbo.App_KhoaHoc', N'UpdatedAtUtc') IS NULL ALTER TABLE dbo.App_KhoaHoc ADD UpdatedAtUtc DATETIME2(0) NULL;

    /* Teacher source identity and fields present in dbo.GiaoVien. */
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'SourceProfileCode') IS NULL ALTER TABLE dbo.App_GiaoVien ADD SourceProfileCode NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'SourceMaGV') IS NULL ALTER TABLE dbo.App_GiaoVien ADD SourceMaGV NVARCHAR(20) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'SourceHash') IS NULL ALTER TABLE dbo.App_GiaoVien ADD SourceHash NVARCHAR(64) NULL;
    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.App_GiaoVien')
          AND name = N'HangGPLX'
          AND max_length < 200)
        ALTER TABLE dbo.App_GiaoVien ALTER COLUMN HangGPLX NVARCHAR(100) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'NoiCtMaDvhc') IS NULL ALTER TABLE dbo.App_GiaoVien ADD NoiCtMaDvhc NVARCHAR(5) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'NoiCtMaDvql') IS NULL ALTER TABLE dbo.App_GiaoVien ADD NoiCtMaDvql NVARCHAR(5) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'MaCSDT') IS NULL ALTER TABLE dbo.App_GiaoVien ADD MaCSDT NVARCHAR(6) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'MaSoGTVT') IS NULL ALTER TABLE dbo.App_GiaoVien ADD MaSoGTVT NVARCHAR(6) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'SoQuyetDinhGCN') IS NULL ALTER TABLE dbo.App_GiaoVien ADD SoQuyetDinhGCN NVARCHAR(30) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'NgayQuyetDinhGCN') IS NULL ALTER TABLE dbo.App_GiaoVien ADD NgayQuyetDinhGCN DATE NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'LoaiHinhDaoTao') IS NULL ALTER TABLE dbo.App_GiaoVien ADD LoaiHinhDaoTao NVARCHAR(500) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'CacHangGPLXDuocDaoTao') IS NULL ALTER TABLE dbo.App_GiaoVien ADD CacHangGPLXDuocDaoTao NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'CauTaoSuaChua') IS NULL ALTER TABLE dbo.App_GiaoVien ADD CauTaoSuaChua NVARCHAR(1) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'DaoDucLaiXe') IS NULL ALTER TABLE dbo.App_GiaoVien ADD DaoDucLaiXe NVARCHAR(1) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'NghiepVuVanTai') IS NULL ALTER TABLE dbo.App_GiaoVien ADD NghiepVuVanTai NVARCHAR(1) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'LuatGTDB') IS NULL ALTER TABLE dbo.App_GiaoVien ADD LuatGTDB NVARCHAR(1) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'KyThuatLaiXe') IS NULL ALTER TABLE dbo.App_GiaoVien ADD KyThuatLaiXe NVARCHAR(1) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'MaFileTiepNhanXml') IS NULL ALTER TABLE dbo.App_GiaoVien ADD MaFileTiepNhanXml NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'ThoiGianTiepNhanXml') IS NULL ALTER TABLE dbo.App_GiaoVien ADD ThoiGianTiepNhanXml DATETIME2 NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'NoiCapGCN') IS NULL ALTER TABLE dbo.App_GiaoVien ADD NoiCapGCN NVARCHAR(500) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'LoaiGiaoVien') IS NULL ALTER TABLE dbo.App_GiaoVien ADD LoaiGiaoVien NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'CacHangDaCo') IS NULL ALTER TABLE dbo.App_GiaoVien ADD CacHangDaCo NVARCHAR(500) NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'TrangThaiNguon') IS NULL ALTER TABLE dbo.App_GiaoVien ADD TrangThaiNguon BIT NULL;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'CreatedAtUtc') IS NULL
        ALTER TABLE dbo.App_GiaoVien ADD CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_App_GiaoVien_CreatedAtUtc DEFAULT SYSUTCDATETIME() WITH VALUES;
    IF COL_LENGTH(N'dbo.App_GiaoVien', N'UpdatedAtUtc') IS NULL ALTER TABLE dbo.App_GiaoVien ADD UpdatedAtUtc DATETIME2(0) NULL;

    /* Course-teacher assignment identity is the actual source MaLichLV plus its source links. */
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'SourceProfileCode') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD SourceProfileCode NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'SourceMaLichLV') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD SourceMaLichLV BIGINT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'SourceMaKhoaHoc') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD SourceMaKhoaHoc NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'SourceMaGV') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD SourceMaGV NVARCHAR(20) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'SourceHash') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD SourceHash NVARCHAR(64) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'LoaiGV') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD LoaiGV NVARCHAR(50) NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'SoHocVien') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD SoHocVien INT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'NgayHieuLuc') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD NgayHieuLuc DATE NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'NgayHetHieuLuc') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD NgayHetHieuLuc DATE NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'IsKhoaHocGiaoVien') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD IsKhoaHocGiaoVien BIT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'TrangThaiNguon') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD TrangThaiNguon BIT NULL;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'CreatedAtUtc') IS NULL
        ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD CreatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_App_KhoaHoc_GiaoVien_CreatedAtUtc DEFAULT SYSUTCDATETIME() WITH VALUES;
    IF COL_LENGTH(N'dbo.App_KhoaHoc_GiaoVien', N'UpdatedAtUtc') IS NULL ALTER TABLE dbo.App_KhoaHoc_GiaoVien ADD UpdatedAtUtc DATETIME2(0) NULL;

    IF OBJECT_ID(N'dbo.CK_App_KhoaHoc_SourceIdentity', N'C') IS NULL
        ALTER TABLE dbo.App_KhoaHoc WITH CHECK ADD CONSTRAINT CK_App_KhoaHoc_SourceIdentity CHECK (
            (SourceProfileCode IS NULL AND SourceMaKhoaHoc IS NULL AND SourceHash IS NULL)
            OR (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO') AND SourceMaKhoaHoc IS NOT NULL AND SourceHash IS NOT NULL));
    IF OBJECT_ID(N'dbo.CK_App_GiaoVien_SourceIdentity', N'C') IS NULL
        ALTER TABLE dbo.App_GiaoVien WITH CHECK ADD CONSTRAINT CK_App_GiaoVien_SourceIdentity CHECK (
            (SourceProfileCode IS NULL AND SourceMaGV IS NULL AND SourceHash IS NULL)
            OR (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO') AND SourceMaGV IS NOT NULL AND SourceHash IS NOT NULL));
    IF OBJECT_ID(N'dbo.CK_App_KhoaHoc_GiaoVien_SourceIdentity', N'C') IS NULL
        ALTER TABLE dbo.App_KhoaHoc_GiaoVien WITH CHECK ADD CONSTRAINT CK_App_KhoaHoc_GiaoVien_SourceIdentity CHECK (
            (SourceProfileCode IS NULL AND SourceMaLichLV IS NULL AND SourceMaKhoaHoc IS NULL AND SourceMaGV IS NULL AND SourceHash IS NULL)
            OR (SourceProfileCode IN (N'CSDT_OTO', N'CSDT_MOTO') AND SourceMaLichLV IS NOT NULL AND SourceMaKhoaHoc IS NOT NULL AND SourceMaGV IS NOT NULL AND SourceHash IS NOT NULL));

    ALTER TABLE dbo.App_KhoaHoc WITH CHECK CHECK CONSTRAINT CK_App_KhoaHoc_SourceIdentity;
    ALTER TABLE dbo.App_GiaoVien WITH CHECK CHECK CONSTRAINT CK_App_GiaoVien_SourceIdentity;
    ALTER TABLE dbo.App_KhoaHoc_GiaoVien WITH CHECK CHECK CONSTRAINT CK_App_KhoaHoc_GiaoVien_SourceIdentity;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc') AND name = N'UX_App_KhoaHoc_SourceIdentity')
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_KhoaHoc_SourceIdentity
            ON dbo.App_KhoaHoc(SourceProfileCode, SourceMaKhoaHoc)
            WHERE SourceProfileCode IS NOT NULL AND SourceMaKhoaHoc IS NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.App_GiaoVien') AND name = N'UX_App_GiaoVien_SourceIdentity')
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_GiaoVien_SourceIdentity
            ON dbo.App_GiaoVien(SourceProfileCode, SourceMaGV)
            WHERE SourceProfileCode IS NOT NULL AND SourceMaGV IS NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien') AND name = N'UX_App_KhoaHoc_GiaoVien_SourceIdentity')
        CREATE UNIQUE NONCLUSTERED INDEX UX_App_KhoaHoc_GiaoVien_SourceIdentity
            ON dbo.App_KhoaHoc_GiaoVien(SourceProfileCode, SourceMaLichLV)
            WHERE SourceProfileCode IS NOT NULL AND SourceMaLichLV IS NOT NULL;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc') AND name = N'UX_App_KhoaHoc_SourceIdentity' AND is_disabled = 1)
        ALTER INDEX UX_App_KhoaHoc_SourceIdentity ON dbo.App_KhoaHoc REBUILD;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.App_GiaoVien') AND name = N'UX_App_GiaoVien_SourceIdentity' AND is_disabled = 1)
        ALTER INDEX UX_App_GiaoVien_SourceIdentity ON dbo.App_GiaoVien REBUILD;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien') AND name = N'UX_App_KhoaHoc_GiaoVien_SourceIdentity' AND is_disabled = 1)
        ALTER INDEX UX_App_KhoaHoc_GiaoVien_SourceIdentity ON dbo.App_KhoaHoc_GiaoVien REBUILD;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS targetIndex
        WHERE targetIndex.object_id = OBJECT_ID(N'dbo.App_KhoaHoc')
          AND targetIndex.name = N'UX_App_KhoaHoc_SourceIdentity'
          AND targetIndex.is_unique = 1 AND targetIndex.is_disabled = 0 AND targetIndex.has_filter = 1
          AND CHARINDEX(N'SourceProfileCode', targetIndex.filter_definition) > 0
          AND CHARINDEX(N'SourceMaKhoaHoc', targetIndex.filter_definition) > 0
          AND (SELECT COUNT(1) FROM sys.index_columns AS keyColumn WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns AS keyColumn INNER JOIN sys.columns AS targetColumn ON targetColumn.object_id = keyColumn.object_id AND targetColumn.column_id = keyColumn.column_id WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal = 1 AND targetColumn.name = N'SourceProfileCode')
          AND EXISTS (SELECT 1 FROM sys.index_columns AS keyColumn INNER JOIN sys.columns AS targetColumn ON targetColumn.object_id = keyColumn.object_id AND targetColumn.column_id = keyColumn.column_id WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal = 2 AND targetColumn.name = N'SourceMaKhoaHoc'))
        THROW 51000, N'UX_App_KhoaHoc_SourceIdentity ton tai nhung sai dinh nghia hoac khong hoat dong.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS targetIndex
        WHERE targetIndex.object_id = OBJECT_ID(N'dbo.App_GiaoVien')
          AND targetIndex.name = N'UX_App_GiaoVien_SourceIdentity'
          AND targetIndex.is_unique = 1 AND targetIndex.is_disabled = 0 AND targetIndex.has_filter = 1
          AND CHARINDEX(N'SourceProfileCode', targetIndex.filter_definition) > 0
          AND CHARINDEX(N'SourceMaGV', targetIndex.filter_definition) > 0
          AND (SELECT COUNT(1) FROM sys.index_columns AS keyColumn WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns AS keyColumn INNER JOIN sys.columns AS targetColumn ON targetColumn.object_id = keyColumn.object_id AND targetColumn.column_id = keyColumn.column_id WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal = 1 AND targetColumn.name = N'SourceProfileCode')
          AND EXISTS (SELECT 1 FROM sys.index_columns AS keyColumn INNER JOIN sys.columns AS targetColumn ON targetColumn.object_id = keyColumn.object_id AND targetColumn.column_id = keyColumn.column_id WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal = 2 AND targetColumn.name = N'SourceMaGV'))
        THROW 51000, N'UX_App_GiaoVien_SourceIdentity ton tai nhung sai dinh nghia hoac khong hoat dong.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS targetIndex
        WHERE targetIndex.object_id = OBJECT_ID(N'dbo.App_KhoaHoc_GiaoVien')
          AND targetIndex.name = N'UX_App_KhoaHoc_GiaoVien_SourceIdentity'
          AND targetIndex.is_unique = 1 AND targetIndex.is_disabled = 0 AND targetIndex.has_filter = 1
          AND CHARINDEX(N'SourceProfileCode', targetIndex.filter_definition) > 0
          AND CHARINDEX(N'SourceMaLichLV', targetIndex.filter_definition) > 0
          AND (SELECT COUNT(1) FROM sys.index_columns AS keyColumn WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns AS keyColumn INNER JOIN sys.columns AS targetColumn ON targetColumn.object_id = keyColumn.object_id AND targetColumn.column_id = keyColumn.column_id WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal = 1 AND targetColumn.name = N'SourceProfileCode')
          AND EXISTS (SELECT 1 FROM sys.index_columns AS keyColumn INNER JOIN sys.columns AS targetColumn ON targetColumn.object_id = keyColumn.object_id AND targetColumn.column_id = keyColumn.column_id WHERE keyColumn.object_id = targetIndex.object_id AND keyColumn.index_id = targetIndex.index_id AND keyColumn.key_ordinal = 2 AND targetColumn.name = N'SourceMaLichLV'))
        THROW 51000, N'UX_App_KhoaHoc_GiaoVien_SourceIdentity ton tai nhung sai dinh nghia hoac khong hoat dong.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
