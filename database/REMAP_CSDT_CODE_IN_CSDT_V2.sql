/*
    REMAP_CSDT_CODE_IN_CSDT_V2.sql

    Mục tiêu:
    - Đổi mã CSĐT trong CSDT_V2 sau khi chuyển dữ liệu từ V1.
    - Ví dụ: V1 dùng 66016, V2 cần dùng 66026.

    Quy tắc:
    - MaDK: 66016-yyyymmdd-xxxxxx...  -> 66026-yyyymmdd-xxxxxx...
    - MaKH/MaKhoaHoc: 66016K...       -> 66026K...
    - MaCSDT: 66016                   -> 66026
    - Mặc định KHÔNG đổi đường dẫn ảnh/PDF để tránh làm mất link file thật.
      Nếu đã copy/rename thư mục ảnh theo mã mới thì mới bật @UpdateFilePathText = 1.

    CHỈ CHẠY TRÊN DATABASE BACKUP TEST TRƯỚC.
*/

USE [CSDT_V2];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @OldMaCSDT VARCHAR(5) = '66016';
DECLARE @NewMaCSDT VARCHAR(5) = '66026';

DECLARE @DryRun BIT = 1;                 -- 1: chạy thử ROLLBACK, 0: ghi thật COMMIT
DECLARE @DisableConstraints BIT = 1;      -- 1: tạm tắt FK khi đổi mã đồng loạt
DECLARE @BlockOnCollision BIT = 1;        -- 1: chặn nếu mã mới đã tồn tại
DECLARE @UpdateFilePathText BIT = 0;      -- 0: không đổi đường dẫn ảnh/PDF, 1: replace text trong cột đường dẫn

DECLARE @OldMaDKPrefix VARCHAR(6) = @OldMaCSDT + '-';
DECLARE @NewMaDKPrefix VARCHAR(6) = @NewMaCSDT + '-';

IF LEN(@OldMaCSDT) <> 5 OR LEN(@NewMaCSDT) <> 5
BEGIN
    THROW 54000, N'Mã CSĐT phải có đúng 5 ký tự.', 1;
END;

IF @OldMaCSDT = @NewMaCSDT
BEGIN
    THROW 54001, N'@OldMaCSDT và @NewMaCSDT đang giống nhau, không cần remap.', 1;
END;

IF OBJECT_ID('tempdb..#RunLog') IS NOT NULL DROP TABLE #RunLog;
IF OBJECT_ID('tempdb..#Collision') IS NOT NULL DROP TABLE #Collision;

CREATE TABLE #RunLog (
    StepName NVARCHAR(300) NOT NULL,
    RowsAffected INT NOT NULL,
    Note NVARCHAR(1000) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME()
);

CREATE TABLE #Collision (
    Entity NVARCHAR(100) NOT NULL,
    OldValue NVARCHAR(200) NOT NULL,
    NewValue NVARCHAR(200) NOT NULL,
    Note NVARCHAR(1000) NULL
);

/* =========================================================
   1. PRECHECK COLLISION
   ========================================================= */

-- MaDK collision trong NguoiLX
INSERT INTO #Collision(Entity, OldValue, NewValue, Note)
SELECT
    N'NguoiLX.MaDK',
    old.MaDK,
    @NewMaCSDT + SUBSTRING(old.MaDK, 6, 200),
    N'MaDK mới đã tồn tại trong NguoiLX'
FROM dbo.NguoiLX old
WHERE LEFT(old.MaDK, 6) = @OldMaDKPrefix
  AND EXISTS (
      SELECT 1
      FROM dbo.NguoiLX newrow
      WHERE newrow.MaDK = @NewMaCSDT + SUBSTRING(old.MaDK, 6, 200)
  );

-- MaDK collision trong NguoiLX_HoSo
INSERT INTO #Collision(Entity, OldValue, NewValue, Note)
SELECT
    N'NguoiLX_HoSo.MaDK',
    old.MaDK,
    @NewMaCSDT + SUBSTRING(old.MaDK, 6, 200),
    N'MaDK mới đã tồn tại trong NguoiLX_HoSo'
FROM dbo.NguoiLX_HoSo old
WHERE LEFT(old.MaDK, 6) = @OldMaDKPrefix
  AND EXISTS (
      SELECT 1
      FROM dbo.NguoiLX_HoSo newrow
      WHERE newrow.MaDK = @NewMaCSDT + SUBSTRING(old.MaDK, 6, 200)
  );

-- MaKH collision trong KhoaHoc
INSERT INTO #Collision(Entity, OldValue, NewValue, Note)
SELECT
    N'KhoaHoc.MaKH',
    old.MaKH,
    @NewMaCSDT + SUBSTRING(old.MaKH, 6, 200),
    N'MaKH mới đã tồn tại trong KhoaHoc'
FROM dbo.KhoaHoc old
WHERE LEFT(old.MaKH, 5) = @OldMaCSDT
  AND EXISTS (
      SELECT 1
      FROM dbo.KhoaHoc newrow
      WHERE newrow.MaKH = @NewMaCSDT + SUBSTRING(old.MaKH, 6, 200)
  );

SELECT * FROM #Collision ORDER BY Entity, OldValue;

IF @BlockOnCollision = 1 AND EXISTS (SELECT 1 FROM #Collision)
BEGIN
    THROW 54002, N'Có collision: mã mới đã tồn tại. Kiểm tra #Collision trước khi remap.', 1;
END;

/* =========================================================
   2. THỐNG KÊ TRƯỚC KHI REMAP
   ========================================================= */

SELECT
    @OldMaCSDT AS OldMaCSDT,
    @NewMaCSDT AS NewMaCSDT,
    @DryRun AS DryRun,
    (SELECT COUNT(*) FROM dbo.NguoiLX WHERE LEFT(MaDK, 6) = @OldMaDKPrefix) AS NguoiLX_OldMaDK,
    (SELECT COUNT(*) FROM dbo.NguoiLX_HoSo WHERE LEFT(MaDK, 6) = @OldMaDKPrefix) AS HoSo_OldMaDK,
    (SELECT COUNT(*) FROM dbo.NguoiLX_HoSo WHERE LEFT(MaKhoaHoc, 5) = @OldMaCSDT) AS HoSo_OldMaKhoaHoc,
    (SELECT COUNT(*) FROM dbo.KhoaHoc WHERE LEFT(MaKH, 5) = @OldMaCSDT) AS KhoaHoc_OldMaKH;

/* =========================================================
   3. REMAP
   ========================================================= */

BEGIN TRY
    BEGIN TRANSACTION;

    IF @DisableConstraints = 1
    BEGIN
        DECLARE @DisableSql NVARCHAR(MAX) = N'';
        SELECT @DisableSql = @DisableSql + N'ALTER TABLE '
            + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name)
            + N' NOCHECK CONSTRAINT ALL;' + CHAR(13)
        FROM sys.tables
        WHERE is_ms_shipped = 0;

        EXEC sp_executesql @DisableSql;
        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'Disable constraints', 0, N'Tạm tắt constraint để remap khóa đồng loạt');
    END;

    DECLARE @Sql NVARCHAR(MAX);
    DECLARE @SchemaName SYSNAME;
    DECLARE @TableName SYSNAME;
    DECLARE @ColumnName SYSNAME;
    DECLARE @Rows INT;

    /* 3.1. Remap các cột MaDK */
    DECLARE cur_madk CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name, t.name, c.name
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.columns c ON c.object_id = t.object_id
    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE t.is_ms_shipped = 0
      AND c.name IN (N'MaDK')
      AND ty.name IN (N'varchar', N'nvarchar', N'char', N'nchar');

    OPEN cur_madk;
    FETCH NEXT FROM cur_madk INTO @SchemaName, @TableName, @ColumnName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql = N'
            UPDATE ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'
            SET ' + QUOTENAME(@ColumnName) + N' = @NewMaCSDT + SUBSTRING(' + QUOTENAME(@ColumnName) + N', 6, 200)
            WHERE LEFT(' + QUOTENAME(@ColumnName) + N', 6) = @OldMaDKPrefix;
            SET @RowsOut = @@ROWCOUNT;
        ';

        EXEC sp_executesql
            @Sql,
            N'@OldMaDKPrefix varchar(6), @NewMaCSDT varchar(5), @RowsOut int OUTPUT',
            @OldMaDKPrefix = @OldMaDKPrefix,
            @NewMaCSDT = @NewMaCSDT,
            @RowsOut = @Rows OUTPUT;

        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'Remap MaDK: ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'.' + QUOTENAME(@ColumnName), @Rows, NULL);

        FETCH NEXT FROM cur_madk INTO @SchemaName, @TableName, @ColumnName;
    END;

    CLOSE cur_madk;
    DEALLOCATE cur_madk;

    /* 3.2. Remap MaHV nếu đang lưu dạng MaDK */
    DECLARE cur_mahv CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name, t.name, c.name
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.columns c ON c.object_id = t.object_id
    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE t.is_ms_shipped = 0
      AND c.name IN (N'MaHV')
      AND ty.name IN (N'varchar', N'nvarchar', N'char', N'nchar');

    OPEN cur_mahv;
    FETCH NEXT FROM cur_mahv INTO @SchemaName, @TableName, @ColumnName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql = N'
            UPDATE ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'
            SET ' + QUOTENAME(@ColumnName) + N' = @NewMaCSDT + SUBSTRING(' + QUOTENAME(@ColumnName) + N', 6, 200)
            WHERE LEFT(' + QUOTENAME(@ColumnName) + N', 6) = @OldMaDKPrefix;
            SET @RowsOut = @@ROWCOUNT;
        ';

        EXEC sp_executesql
            @Sql,
            N'@OldMaDKPrefix varchar(6), @NewMaCSDT varchar(5), @RowsOut int OUTPUT',
            @OldMaDKPrefix = @OldMaDKPrefix,
            @NewMaCSDT = @NewMaCSDT,
            @RowsOut = @Rows OUTPUT;

        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'Remap MaHV dạng MaDK: ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName), @Rows, NULL);

        FETCH NEXT FROM cur_mahv INTO @SchemaName, @TableName, @ColumnName;
    END;

    CLOSE cur_mahv;
    DEALLOCATE cur_mahv;

    /* 3.3. Remap các cột MaKH / MaKhoaHoc */
    DECLARE cur_makh CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name, t.name, c.name
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.columns c ON c.object_id = t.object_id
    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE t.is_ms_shipped = 0
      AND c.name IN (N'MaKH', N'MaKhoaHoc', N'MaKhoa')
      AND ty.name IN (N'varchar', N'nvarchar', N'char', N'nchar');

    OPEN cur_makh;
    FETCH NEXT FROM cur_makh INTO @SchemaName, @TableName, @ColumnName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql = N'
            UPDATE ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'
            SET ' + QUOTENAME(@ColumnName) + N' = @NewMaCSDT + SUBSTRING(' + QUOTENAME(@ColumnName) + N', 6, 200)
            WHERE LEFT(' + QUOTENAME(@ColumnName) + N', 5) = @OldMaCSDT;
            SET @RowsOut = @@ROWCOUNT;
        ';

        EXEC sp_executesql
            @Sql,
            N'@OldMaCSDT varchar(5), @NewMaCSDT varchar(5), @RowsOut int OUTPUT',
            @OldMaCSDT = @OldMaCSDT,
            @NewMaCSDT = @NewMaCSDT,
            @RowsOut = @Rows OUTPUT;

        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'Remap MaKH/MaKhoaHoc: ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'.' + QUOTENAME(@ColumnName), @Rows, NULL);

        FETCH NEXT FROM cur_makh INTO @SchemaName, @TableName, @ColumnName;
    END;

    CLOSE cur_makh;
    DEALLOCATE cur_makh;

    /* 3.4. Remap MaCSDT */
    DECLARE cur_macsdt CURSOR LOCAL FAST_FORWARD FOR
    SELECT s.name, t.name, c.name
    FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    JOIN sys.columns c ON c.object_id = t.object_id
    JOIN sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE t.is_ms_shipped = 0
      AND c.name IN (N'MaCSDT')
      AND ty.name IN (N'varchar', N'nvarchar', N'char', N'nchar');

    OPEN cur_macsdt;
    FETCH NEXT FROM cur_macsdt INTO @SchemaName, @TableName, @ColumnName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql = N'
            UPDATE ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'
            SET ' + QUOTENAME(@ColumnName) + N' = @NewMaCSDT
            WHERE ' + QUOTENAME(@ColumnName) + N' = @OldMaCSDT;
            SET @RowsOut = @@ROWCOUNT;
        ';

        EXEC sp_executesql
            @Sql,
            N'@OldMaCSDT varchar(5), @NewMaCSDT varchar(5), @RowsOut int OUTPUT',
            @OldMaCSDT = @OldMaCSDT,
            @NewMaCSDT = @NewMaCSDT,
            @RowsOut = @Rows OUTPUT;

        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'Remap MaCSDT: ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName), @Rows, NULL);

        FETCH NEXT FROM cur_macsdt INTO @SchemaName, @TableName, @ColumnName;
    END;

    CLOSE cur_macsdt;
    DEALLOCATE cur_macsdt;

    /* 3.5. Optional: Remap đường dẫn file/ảnh */
    IF @UpdateFilePathText = 1
    BEGIN
        DECLARE cur_path CURSOR LOCAL FAST_FORWARD FOR
        SELECT s.name, t.name, c.name
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE t.is_ms_shipped = 0
          AND c.name IN (N'DuongDanAnh', N'AnhCD')
          AND ty.name IN (N'varchar', N'nvarchar');

        OPEN cur_path;
        FETCH NEXT FROM cur_path INTO @SchemaName, @TableName, @ColumnName;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            SET @Sql = N'
                UPDATE ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'
                SET ' + QUOTENAME(@ColumnName) + N' = REPLACE(' + QUOTENAME(@ColumnName) + N', @OldMaCSDT, @NewMaCSDT)
                WHERE ' + QUOTENAME(@ColumnName) + N' LIKE ''%'' + @OldMaCSDT + ''%'';
                SET @RowsOut = @@ROWCOUNT;
            ';

            EXEC sp_executesql
                @Sql,
                N'@OldMaCSDT varchar(5), @NewMaCSDT varchar(5), @RowsOut int OUTPUT',
                @OldMaCSDT = @OldMaCSDT,
                @NewMaCSDT = @NewMaCSDT,
                @RowsOut = @Rows OUTPUT;

            INSERT INTO #RunLog(StepName, RowsAffected, Note)
            VALUES (N'Remap file path text: ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName), @Rows, N'Chỉ chạy khi @UpdateFilePathText = 1');

            FETCH NEXT FROM cur_path INTO @SchemaName, @TableName, @ColumnName;
        END;

        CLOSE cur_path;
        DEALLOCATE cur_path;
    END;

    IF @DisableConstraints = 1
    BEGIN
        DECLARE @EnableSql NVARCHAR(MAX) = N'';
        SELECT @EnableSql = @EnableSql + N'ALTER TABLE '
            + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name)
            + N' WITH CHECK CHECK CONSTRAINT ALL;' + CHAR(13)
        FROM sys.tables
        WHERE is_ms_shipped = 0;

        EXEC sp_executesql @EnableSql;
        INSERT INTO #RunLog(StepName, RowsAffected, Note)
        VALUES (N'Enable constraints', 0, N'Bật lại constraint và kiểm tra dữ liệu');
    END;

    SELECT * FROM #RunLog ORDER BY CreatedAt, StepName;

    SELECT
        (SELECT COUNT(*) FROM dbo.NguoiLX WHERE LEFT(MaDK, 6) = @OldMaDKPrefix) AS NguoiLX_ConOldMaDK,
        (SELECT COUNT(*) FROM dbo.NguoiLX WHERE LEFT(MaDK, 6) = @NewMaDKPrefix) AS NguoiLX_NewMaDK,
        (SELECT COUNT(*) FROM dbo.NguoiLX_HoSo WHERE LEFT(MaDK, 6) = @OldMaDKPrefix) AS HoSo_ConOldMaDK,
        (SELECT COUNT(*) FROM dbo.NguoiLX_HoSo WHERE LEFT(MaDK, 6) = @NewMaDKPrefix) AS HoSo_NewMaDK,
        (SELECT COUNT(*) FROM dbo.KhoaHoc WHERE LEFT(MaKH, 5) = @OldMaCSDT) AS KhoaHoc_ConOldMaKH,
        (SELECT COUNT(*) FROM dbo.KhoaHoc WHERE LEFT(MaKH, 5) = @NewMaCSDT) AS KhoaHoc_NewMaKH;

    IF @DryRun = 1
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT N'DRY RUN: Đã ROLLBACK. Chưa đổi mã trong CSDT_V2.';
    END
    ELSE
    BEGIN
        COMMIT TRANSACTION;
        PRINT N'COMMIT: Đã đổi mã CSĐT trong CSDT_V2.';
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
