/*
    Enable SQL Server Change Tracking for the fixed MOTO V2 BAK source database.
    Safety:
    - Schema/metadata only; this patch never changes business rows.
    - Exact database and exact table allowlist; no caller-provided database names.
    - Re-running is safe. Existing Change Tracking checkpoints are never reset.
    - Optional domains are skipped with SKIPPED_UNSUPPORTED_SCHEMA when absent or missing a PK.
*/
USE [CSDL_MOTO_BAK];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() <> N'CSDL_MOTO_BAK'
BEGIN
    THROW 527460, 'Wrong database. Expected CSDL_MOTO_BAK.', 1;
END;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID()
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
)
BEGIN
    THROW 527461, 'CSDL_MOTO_BAK must be ONLINE and read-write.', 1;
END;
DECLARE @RequiredTables table
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    PRIMARY KEY (SchemaName, TableName)
);
DECLARE @OptionalTables table
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    PRIMARY KEY (SchemaName, TableName)
);
INSERT INTO @RequiredTables (SchemaName, TableName)
VALUES
    (N'dbo', N'DM_DonViGTVT'),
    (N'dbo', N'KhoaHoc'),
    (N'dbo', N'BaoCaoI'),
    (N'dbo', N'NguoiLX'),
    (N'dbo', N'NguoiLX_HoSo'),
    (N'dbo', N'NguoiLX_GPLX'),
    (N'dbo', N'NguoiLXHS_GiayTo');
INSERT INTO @OptionalTables (SchemaName, TableName)
VALUES
    (N'dbo', N'GiaoVien'),
    (N'dbo', N'KhoaHoc_GiaoVien');
IF EXISTS
(
    SELECT 1
    FROM @RequiredTables required
    LEFT JOIN sys.schemas schemas
      ON schemas.name = required.SchemaName
    LEFT JOIN sys.tables tables
      ON tables.schema_id = schemas.schema_id
     AND tables.name = required.TableName
    WHERE tables.object_id IS NULL
)
BEGIN
    THROW 527462, 'A required CSDL_MOTO_BAK Change Tracking table is missing.', 1;
END;
IF EXISTS
(
    SELECT 1
    FROM @RequiredTables required
    JOIN sys.schemas schemas
      ON schemas.name = required.SchemaName
    JOIN sys.tables tables
      ON tables.schema_id = schemas.schema_id
     AND tables.name = required.TableName
    LEFT JOIN sys.key_constraints primaryKey
      ON primaryKey.parent_object_id = tables.object_id
     AND primaryKey.[type] = N'PK'
    WHERE primaryKey.object_id IS NULL
)
BEGIN
    THROW 527463, 'Every required CSDL_MOTO_BAK Change Tracking table must have a primary key.', 1;
END;
DECLARE @SkippedOptional table
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    Reason nvarchar(200) NOT NULL,
    PRIMARY KEY (SchemaName, TableName)
);
INSERT INTO @SkippedOptional (SchemaName, TableName, Reason)
SELECT optional.SchemaName, optional.TableName, N'missing table'
FROM @OptionalTables optional
LEFT JOIN sys.schemas schemas
  ON schemas.name = optional.SchemaName
LEFT JOIN sys.tables tables
  ON tables.schema_id = schemas.schema_id
 AND tables.name = optional.TableName
WHERE tables.object_id IS NULL;
INSERT INTO @SkippedOptional (SchemaName, TableName, Reason)
SELECT optional.SchemaName, optional.TableName, N'missing primary key'
FROM @OptionalTables optional
JOIN sys.schemas schemas
  ON schemas.name = optional.SchemaName
JOIN sys.tables tables
  ON tables.schema_id = schemas.schema_id
 AND tables.name = optional.TableName
LEFT JOIN sys.key_constraints primaryKey
  ON primaryKey.parent_object_id = tables.object_id
 AND primaryKey.[type] = N'PK'
WHERE primaryKey.object_id IS NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM @SkippedOptional skipped
      WHERE skipped.SchemaName = optional.SchemaName
        AND skipped.TableName = optional.TableName
  );
DECLARE @SkippedMessage nvarchar(400);
DECLARE skippedOptionalCursor CURSOR LOCAL FAST_FORWARD FOR
SELECT N'SKIPPED_UNSUPPORTED_SCHEMA: optional ' + QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N' skipped: ' + Reason + N'.'
FROM @SkippedOptional
ORDER BY SchemaName, TableName;
OPEN skippedOptionalCursor;
FETCH NEXT FROM skippedOptionalCursor INTO @SkippedMessage;
WHILE @@FETCH_STATUS = 0
BEGIN
    PRINT @SkippedMessage;
    FETCH NEXT FROM skippedOptionalCursor INTO @SkippedMessage;
END;
CLOSE skippedOptionalCursor;
DEALLOCATE skippedOptionalCursor;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID()
      AND retention_period = 7
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
)
BEGIN
    ALTER DATABASE [CSDL_MOTO_BAK]
        SET CHANGE_TRACKING = ON
        (
            CHANGE_RETENTION = 7 DAYS,
            AUTO_CLEANUP = ON
        );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.DM_DonViGTVT', N'U'))
    ALTER TABLE dbo.DM_DonViGTVT ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.DM_DonViGTVT', N'U') AND is_track_columns_updated_on = 0)
    THROW 527464, 'dbo.DM_DonViGTVT is tracked without TRACK_COLUMNS_UPDATED.', 1;
IF OBJECT_ID(N'dbo.GiaoVien', N'U') IS NULL
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: missing table.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.GiaoVien', N'U') AND [type] = N'PK')
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: missing primary key.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.GiaoVien', N'U'))
    ALTER TABLE dbo.GiaoVien ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.GiaoVien', N'U') AND is_track_columns_updated_on = 0)
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.GiaoVien skipped: tracked without TRACK_COLUMNS_UPDATED.';
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.KhoaHoc', N'U'))
    ALTER TABLE dbo.KhoaHoc ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.KhoaHoc', N'U') AND is_track_columns_updated_on = 0)
    THROW 527466, 'dbo.KhoaHoc is tracked without TRACK_COLUMNS_UPDATED.', 1;
IF OBJECT_ID(N'dbo.KhoaHoc_GiaoVien', N'U') IS NULL
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.KhoaHoc_GiaoVien skipped: missing table.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.KhoaHoc_GiaoVien', N'U') AND [type] = N'PK')
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.KhoaHoc_GiaoVien skipped: missing primary key.';
ELSE IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.KhoaHoc_GiaoVien', N'U'))
    ALTER TABLE dbo.KhoaHoc_GiaoVien ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.KhoaHoc_GiaoVien', N'U') AND is_track_columns_updated_on = 0)
    PRINT N'SKIPPED_UNSUPPORTED_SCHEMA: optional dbo.KhoaHoc_GiaoVien skipped: tracked without TRACK_COLUMNS_UPDATED.';
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.BaoCaoI', N'U'))
    ALTER TABLE dbo.BaoCaoI ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.BaoCaoI', N'U') AND is_track_columns_updated_on = 0)
    THROW 527468, 'dbo.BaoCaoI is tracked without TRACK_COLUMNS_UPDATED.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX', N'U'))
    ALTER TABLE dbo.NguoiLX ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX', N'U') AND is_track_columns_updated_on = 0)
    THROW 527469, 'dbo.NguoiLX is tracked without TRACK_COLUMNS_UPDATED.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U'))
    ALTER TABLE dbo.NguoiLX_HoSo ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') AND is_track_columns_updated_on = 0)
    THROW 527470, 'dbo.NguoiLX_HoSo is tracked without TRACK_COLUMNS_UPDATED.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_GPLX', N'U'))
    ALTER TABLE dbo.NguoiLX_GPLX ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_GPLX', N'U') AND is_track_columns_updated_on = 0)
    THROW 527471, 'dbo.NguoiLX_GPLX is tracked without TRACK_COLUMNS_UPDATED.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLXHS_GiayTo', N'U'))
    ALTER TABLE dbo.NguoiLXHS_GiayTo ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLXHS_GiayTo', N'U') AND is_track_columns_updated_on = 0)
    THROW 527472, 'dbo.NguoiLXHS_GiayTo is tracked without TRACK_COLUMNS_UPDATED.', 1;
GO
IF
(
    SELECT COUNT(*)
    FROM sys.change_tracking_tables
    WHERE object_id IN
    (
        OBJECT_ID(N'dbo.DM_DonViGTVT', N'U'),
        OBJECT_ID(N'dbo.KhoaHoc', N'U'),
        OBJECT_ID(N'dbo.BaoCaoI', N'U'),
        OBJECT_ID(N'dbo.NguoiLX', N'U'),
        OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U'),
        OBJECT_ID(N'dbo.NguoiLX_GPLX', N'U'),
        OBJECT_ID(N'dbo.NguoiLXHS_GiayTo', N'U')
    )
      AND is_track_columns_updated_on = 1
) <> 7
BEGIN
    THROW 527473, 'CSDL_MOTO_BAK required Change Tracking verification failed.', 1;
END;
GO