/*
    Lossless V2 -> V1 schema compatibility for the fixed CSDL_OTO_V1 target.
    Schema-only and idempotent. Existing values are never shortened or rewritten.
    Mandatory learner/course tables still fail fast when incompatible. Optional
    GiaoVien compatibility is skipped with SKIPPED_UNSUPPORTED_SCHEMA when the
    table or its legacy FK shape is not compatible.
*/
USE [CSDL_OTO_V1];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() <> N'CSDL_OTO_V1'
    THROW 527500, 'Wrong database. Expected CSDL_OTO_V1.', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
)
    THROW 527501, 'CSDL_OTO_V1 must be ONLINE and read-write.', 1;
IF OBJECT_ID(N'dbo.DM_DonViGTVT', N'U') IS NULL
   OR OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL
    THROW 527502, 'A required CSDL_OTO_V1 compatibility table is missing.', 1;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.DM_DonViGTVT', N'U')
          AND columnMetadata.name = N'TenDV'
          AND typeMetadata.name = N'nvarchar'
          AND columnMetadata.is_nullable = 0
    )
        THROW 527503, 'dbo.DM_DonViGTVT.TenDV has an incompatible definition.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.DM_DonViGTVT', N'U')
          AND name = N'TenDV'
          AND max_length <> -1
          AND max_length < 2000
    )
        ALTER TABLE dbo.DM_DonViGTVT ALTER COLUMN TenDV nvarchar(1000) NOT NULL;
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.DM_DonViGTVT', N'U')
          AND columnMetadata.name = N'CoQuanQL'
          AND typeMetadata.name = N'nvarchar'
          AND columnMetadata.is_nullable = 1
    )
        THROW 527504, 'dbo.DM_DonViGTVT.CoQuanQL has an incompatible definition.', 1;
    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.DM_DonViGTVT', N'U')
          AND name = N'CoQuanQL'
          AND max_length <> -1
          AND max_length < 2000
    )
        ALTER TABLE dbo.DM_DonViGTVT ALTER COLUMN CoQuanQL nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.NguoiLX_HoSo', N'QDThucHanhHinh') IS NULL
        ALTER TABLE dbo.NguoiLX_HoSo ADD QDThucHanhHinh float NULL;
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
          AND columnMetadata.name = N'QDThucHanhHinh'
          AND typeMetadata.name = N'float'
          AND columnMetadata.is_nullable = 1
    )
        THROW 527510, 'dbo.NguoiLX_HoSo.QDThucHanhHinh has an incompatible definition.', 1;
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U')
          AND columnMetadata.name = N'SoGiayCNTN'
          AND typeMetadata.name = N'nvarchar'
          AND columnMetadata.max_length = 60
          AND columnMetadata.is_nullable = 1
    )
        THROW 527511, 'dbo.NguoiLX_HoSo.SoGiayCNTN must remain nvarchar(30) NULL.', 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
SET XACT_ABORT OFF;
GO
BEGIN TRY
    DECLARE @SkipOptionalGiaoVien bit = 0;
    IF OBJECT_ID(N'dbo.GiaoVien', N'U') IS NULL
    BEGIN
        SET @SkipOptionalGiaoVien = 1;
        PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: missing table.';
    END;
    IF @SkipOptionalGiaoVien = 0 AND NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
          AND columnMetadata.name = N'GhiChu'
          AND typeMetadata.name = N'nvarchar'
          AND columnMetadata.is_nullable = 1
    )
    BEGIN
        SET @SkipOptionalGiaoVien = 1;
        PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: GhiChu is incompatible.';
    END;
    IF @SkipOptionalGiaoVien = 0 AND NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
          AND columnMetadata.name = N'HinhThuc_TuyenDung'
          AND typeMetadata.name IN (N'varchar', N'nvarchar')
          AND columnMetadata.is_nullable = 1
    )
    BEGIN
        SET @SkipOptionalGiaoVien = 1;
        PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: HinhThuc_TuyenDung is incompatible.';
    END;
    IF @SkipOptionalGiaoVien = 0 AND NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
          AND columnMetadata.name = N'LoaiHinh_DaoTao'
          AND typeMetadata.name IN (N'varchar', N'nvarchar')
          AND columnMetadata.is_nullable = 1
    )
    BEGIN
        SET @SkipOptionalGiaoVien = 1;
        PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: LoaiHinh_DaoTao is incompatible.';
    END;
    IF @SkipOptionalGiaoVien = 0 AND NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys foreignKey
        JOIN sys.foreign_key_columns foreignKeyColumn
          ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
        JOIN sys.columns parentColumn
          ON parentColumn.object_id = foreignKey.parent_object_id
         AND parentColumn.column_id = foreignKeyColumn.parent_column_id
        JOIN sys.columns referencedColumn
          ON referencedColumn.object_id = foreignKey.referenced_object_id
         AND referencedColumn.column_id = foreignKeyColumn.referenced_column_id
        WHERE foreignKey.parent_object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
          AND foreignKey.referenced_object_id = OBJECT_ID(N'dbo.DM_HangGPLX', N'U')
          AND foreignKey.name = N'FK_GiaoVien_DM_HangGPLX'
          AND parentColumn.name = N'HangGPLX'
          AND referencedColumn.name = N'MaHang'
          AND foreignKey.is_disabled = 0
          AND foreignKey.is_not_trusted = 0
          AND foreignKey.delete_referential_action = 0
          AND foreignKey.update_referential_action = 0
    )
    BEGIN
        SET @SkipOptionalGiaoVien = 1;
        PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: FK_GiaoVien_DM_HangGPLX is incompatible.';
    END;
    IF @SkipOptionalGiaoVien = 0 AND NOT EXISTS
    (
        SELECT 1
        FROM sys.columns columnMetadata
        JOIN sys.types typeMetadata
          ON typeMetadata.user_type_id = columnMetadata.user_type_id
        WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
          AND columnMetadata.name = N'HangGPLX'
          AND typeMetadata.name = N'varchar'
          AND columnMetadata.max_length = 3
    )
    BEGIN
        SET @SkipOptionalGiaoVien = 1;
        PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: HangGPLX must remain FK-compatible varchar(3).';
    END;
    IF @SkipOptionalGiaoVien = 0
    BEGIN
        BEGIN TRANSACTION;
        IF COL_LENGTH(N'dbo.GiaoVien', N'CacHangDaCo') IS NULL
            ALTER TABLE dbo.GiaoVien ADD CacHangDaCo nvarchar(500) NULL;
        IF COL_LENGTH(N'dbo.GiaoVien', N'CacMonHoc') IS NULL
            ALTER TABLE dbo.GiaoVien ADD CacMonHoc nvarchar(500) NULL;
        IF COL_LENGTH(N'dbo.GiaoVien', N'LoaiGiaoVien') IS NULL
            ALTER TABLE dbo.GiaoVien ADD LoaiGiaoVien nvarchar(50) NULL;
        IF COL_LENGTH(N'dbo.GiaoVien', N'NgayHHGPLX') IS NULL
            ALTER TABLE dbo.GiaoVien ADD NgayHHGPLX datetime NULL;
        IF COL_LENGTH(N'dbo.GiaoVien', N'NoiCapGCN') IS NULL
            ALTER TABLE dbo.GiaoVien ADD NoiCapGCN nvarchar(500) NULL;
        IF EXISTS
        (
            SELECT 1
            FROM
            (
                VALUES
                    (N'CacHangDaCo', N'nvarchar', 1000),
                    (N'CacMonHoc', N'nvarchar', 1000),
                    (N'LoaiGiaoVien', N'nvarchar', 100),
                    (N'NgayHHGPLX', N'datetime', 8),
                    (N'NoiCapGCN', N'nvarchar', 1000)
            ) required(ColumnName, TypeName, MinimumMaxLength)
            LEFT JOIN sys.columns columnMetadata
              ON columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
             AND columnMetadata.name = required.ColumnName
            LEFT JOIN sys.types typeMetadata
              ON typeMetadata.user_type_id = columnMetadata.user_type_id
            WHERE columnMetadata.column_id IS NULL
               OR typeMetadata.name <> required.TypeName
               OR columnMetadata.is_nullable <> 1
               OR
               (
                   columnMetadata.max_length <> -1
                   AND columnMetadata.max_length < required.MinimumMaxLength
               )
        )
            THROW 527512, 'A V2-only dbo.GiaoVien compatibility column is incompatible.', 1;
        IF EXISTS
        (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
              AND name = N'GhiChu'
              AND max_length <> -1
              AND max_length < 1000
        )
            ALTER TABLE dbo.GiaoVien ALTER COLUMN GhiChu nvarchar(500) NULL;
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.columns columnMetadata
            JOIN sys.types typeMetadata
              ON typeMetadata.user_type_id = columnMetadata.user_type_id
            WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
              AND columnMetadata.name = N'HinhThuc_TuyenDung'
              AND typeMetadata.name = N'nvarchar'
              AND (columnMetadata.max_length = -1 OR columnMetadata.max_length >= 100)
              AND columnMetadata.is_nullable = 1
        )
            ALTER TABLE dbo.GiaoVien ALTER COLUMN HinhThuc_TuyenDung nvarchar(50) NULL;
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.columns columnMetadata
            JOIN sys.types typeMetadata
              ON typeMetadata.user_type_id = columnMetadata.user_type_id
            WHERE columnMetadata.object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
              AND columnMetadata.name = N'LoaiHinh_DaoTao'
              AND typeMetadata.name = N'nvarchar'
              AND (columnMetadata.max_length = -1 OR columnMetadata.max_length >= 1000)
              AND columnMetadata.is_nullable = 1
        )
            ALTER TABLE dbo.GiaoVien ALTER COLUMN LoaiHinh_DaoTao nvarchar(500) NULL;
        IF EXISTS
        (
            SELECT 1
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'dbo.GiaoVien', N'U')
              AND name = N'HangGPLX'
              AND is_nullable = 0
        )
        BEGIN
            ALTER TABLE dbo.GiaoVien DROP CONSTRAINT FK_GiaoVien_DM_HangGPLX;
            ALTER TABLE dbo.GiaoVien ALTER COLUMN HangGPLX varchar(3) NULL;
            ALTER TABLE dbo.GiaoVien WITH CHECK
                ADD CONSTRAINT FK_GiaoVien_DM_HangGPLX
                FOREIGN KEY (HangGPLX) REFERENCES dbo.DM_HangGPLX (MaHang);
            ALTER TABLE dbo.GiaoVien CHECK CONSTRAINT FK_GiaoVien_DM_HangGPLX;
        END;
        COMMIT TRANSACTION;
    END;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: ' + ERROR_MESSAGE();
END CATCH;
GO
SET XACT_ABORT ON;
GO