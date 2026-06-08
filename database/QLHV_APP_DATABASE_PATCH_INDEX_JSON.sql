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
