USE [master];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedServer nvarchar(128) = N'CSDLTTTC\QLHVRT02';
DECLARE @ExpectedEnvironmentId nvarchar(128) =
    N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01';
DECLARE @ExpectedApprovalId nvarchar(128) =
    N'RT02B-OPERATOR-APPROVAL-20260727-01';
DECLARE @ExpectedExpiryUtc datetime2(0) =
    CONVERT(datetime2(0), N'2026-07-31T16:59:59', 126);

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> @ExpectedServer
    THROW 527440, 'ISOLATED_DATABASE_IDENTITY_REJECTED: exact server identity.', 1;

IF CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE N'%Developer%'
    THROW 527441, 'ISOLATED_DATABASE_IDENTITY_REJECTED: SQL edition/version.', 1;

IF SYSUTCDATETIME() > @ExpectedExpiryUtc
    THROW 527474, 'ISOLATED_DATABASE_IDENTITY_REJECTED: environment approval expired.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
      AND state_desc = N'ONLINE'
      AND is_read_only = 0
      AND source_database_id IS NULL
) <> 3
    THROW 527442, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database state.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id IN
    (
        DB_ID(N'QLHV_RT02_OTO_TEST'),
        DB_ID(N'QLHV_RT02_MOTO_TEST'),
        DB_ID(N'QLHV_RT02_TARGET_TEST')
    )
)
    THROW 527443, 'RT02 schema-only gate requires Change Tracking OFF.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
      AND
      (
          snapshot_isolation_state <> 0
          OR is_read_committed_snapshot_on <> 0
      )
)
    THROW 527444, 'RT02 schema-only gate requires Snapshot and RCSI OFF.', 1;

IF EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
    THROW 527445, 'ISOLATED_DATABASE_IDENTITY_REJECTED: linked server route.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.dm_exec_sessions
    WHERE is_user_process = 1
      AND session_id <> @@SPID
      AND
      (
          program_name LIKE N'%QLHV.Api%'
          OR program_name LIKE N'%QLHV.Worker%'
          OR program_name LIKE N'%Hangfire%'
          OR program_name LIKE N'%AutoSync%'
      )
)
    THROW 527446, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production session.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        SELECT
            databaseItem.name AS DatabaseName,
            databaseItem.database_id AS DatabaseId,
            recovery.database_guid AS DatabaseGuid
        FROM sys.databases AS databaseItem
        INNER JOIN sys.database_recovery_status AS recovery
            ON recovery.database_id = databaseItem.database_id
        WHERE databaseItem.name IN
        (
            N'QLHV_RT02_OTO_TEST',
            N'QLHV_RT02_MOTO_TEST',
            N'QLHV_RT02_TARGET_TEST'
        )
    ) AS isolated
    INNER JOIN
    (
        VALUES
            (N'CSDL_MOTO', 8, CONVERT(uniqueidentifier, N'308BDDA8-80F3-4ACB-9836-578D80A9E98E')),
            (N'CSDL_OTO', 9, CONVERT(uniqueidentifier, N'9A8B9BC1-18F3-4823-8123-3DC197A9D540')),
            (N'CSDL_MOTO_BAK', 10, CONVERT(uniqueidentifier, N'2F326753-B1A8-4E94-A404-49FA71E8260A')),
            (N'CSDL_OTO_BAK', 11, CONVERT(uniqueidentifier, N'22ABFD1B-5F1B-4DCA-9EFB-DFB0676623CE')),
            (N'QLHV_APP', 12, CONVERT(uniqueidentifier, N'9C44B304-8A84-4D0D-9A82-19C7233FF6BB')),
            (N'CSDL_MOTO_V1', 13, CONVERT(uniqueidentifier, N'454A56FA-EAA5-4EA7-B41E-9AD500C2D69F')),
            (N'CSDL_OTO_V1', 14, CONVERT(uniqueidentifier, N'41463BB9-532D-4BDB-B821-595D7A30BB2C')),
            (N'CSDL_MOTO_V1_BAK', 15, CONVERT(uniqueidentifier, N'EFC5F9A8-DF27-41BF-B6CF-B05B09C9E211')),
            (N'CSDL_OTO_V1_BAK', 16, CONVERT(uniqueidentifier, N'E1CCB750-B4EB-4D85-8D46-9C043A342C54'))
    ) AS production(DatabaseName, DatabaseId, DatabaseGuid)
        ON production.DatabaseName = isolated.DatabaseName
        OR production.DatabaseId = isolated.DatabaseId
        OR production.DatabaseGuid = isolated.DatabaseGuid
)
    THROW 527447, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production denylist.', 1;
GO

USE [QLHV_RT02_OTO_TEST];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'QLHV_RT02_OTO_TEST'
   OR DB_ID() <> 5
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789')
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
      N'CSDLTTTC\QLHVRT02'
    THROW 527448, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO identity.', 1;

IF DB_NAME() IN
(
    N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
    N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1',
    N'CSDL_OTO_V1_BAK', N'CSDL_MOTO_V1_BAK'
)
    THROW 527449, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production name.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B-OPERATOR-APPROVAL-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_DATASET_MODE'
      AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
      AND CONVERT(nvarchar(128), value) = N'FALSE'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_EXPIRES_AT_UTC'
      AND CONVERT(nvarchar(128), value) = N'2026-07-31T16:59:59Z'
)
    THROW 527450, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO markers.', 1;

IF EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527475, 'ISOLATED_DATABASE_IDENTITY_REJECTED: OTO synonym route.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.tables
    WHERE is_ms_shipped = 0
      AND
      (
          SCHEMA_NAME(schema_id) <> N'dbo'
          OR name NOT IN (N'NguoiLX', N'NguoiLX_HoSo')
      )
)
    THROW 527451, 'RT02 OTO contains a non-allowlisted table.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NguoiLX
    (
        IdentityHmac char(64) NOT NULL
            CONSTRAINT PK_RT02_OTO_NguoiLX PRIMARY KEY,
        ScenarioCode varchar(40) NOT NULL,
        DatasetRole varchar(40) NOT NULL,
        HoTen nvarchar(200) NOT NULL,
        SourceRowHash char(64) NOT NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_RT02_OTO_NguoiLX_IsActive DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_RT02_OTO_NguoiLX_CreatedAtUtc
            DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NguoiLX_HoSo
    (
        IdentityHmac char(64) NOT NULL
            CONSTRAINT PK_RT02_OTO_NguoiLX_HoSo PRIMARY KEY,
        PayloadHash char(64) NOT NULL,
        CONSTRAINT FK_RT02_OTO_NguoiLX_HoSo_NguoiLX
            FOREIGN KEY (IdentityHmac) REFERENCES dbo.NguoiLX(IdentityHmac)
    );
END;
GO

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'NguoiLX', 1, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (N'NguoiLX', 2, N'ScenarioCode', N'varchar', 40, 0, 0, 0),
            (N'NguoiLX', 3, N'DatasetRole', N'varchar', 40, 0, 0, 0),
            (N'NguoiLX', 4, N'HoTen', N'nvarchar', 400, 0, 0, 0),
            (N'NguoiLX', 5, N'SourceRowHash', N'char', 64, 0, 0, 0),
            (N'NguoiLX', 6, N'IsActive', N'bit', 1, 0, 0, 0),
            (N'NguoiLX', 7, N'CreatedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'NguoiLX_HoSo', 1, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (N'NguoiLX_HoSo', 2, N'PayloadHash', N'char', 64, 0, 0, 0)
    ) AS expected
    (
        TableName, ColumnId, ColumnName, TypeName, MaxLength, ScaleValue,
        IsNullable, IsIdentity
    )
    FULL OUTER JOIN
    (
        SELECT
            tableItem.name AS TableName,
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeItem.name AS TypeName,
            CONVERT(int, columnItem.max_length) AS MaxLength,
            CONVERT(int, columnItem.scale) AS ScaleValue,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity
        FROM sys.tables AS tableItem
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = tableItem.object_id
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        WHERE SCHEMA_NAME(tableItem.schema_id) = N'dbo'
          AND tableItem.name IN (N'NguoiLX', N'NguoiLX_HoSo')
    ) AS actual
        ON actual.TableName = expected.TableName
       AND actual.ColumnId = expected.ColumnId
       AND actual.ColumnName = expected.ColumnName
    WHERE expected.TableName IS NULL
       OR actual.TableName IS NULL
       OR actual.TypeName <> expected.TypeName
       OR actual.MaxLength <> expected.MaxLength
       OR actual.ScaleValue <> expected.ScaleValue
       OR actual.IsNullable <> expected.IsNullable
       OR actual.IsIdentity <> expected.IsIdentity
)
    THROW 527478, 'RT02 OTO exact column metadata is incompatible.', 1;

IF EXISTS (SELECT 1 FROM dbo.NguoiLX)
   OR EXISTS (SELECT 1 FROM dbo.NguoiLX_HoSo)
    THROW 527452, 'RT02 OTO schema-only tables must remain empty.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
) <> 2
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.NguoiLX')
   ) <> 7
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
   ) <> 2
    THROW 527453, 'RT02 OTO schema shape is incompatible.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND name = N'PK_RT02_OTO_NguoiLX'
      AND type = N'PK'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
      AND name = N'PK_RT02_OTO_NguoiLX_HoSo'
      AND type = N'PK'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
      AND referenced_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND name = N'FK_RT02_OTO_NguoiLX_HoSo_NguoiLX'
)
   OR (SELECT COUNT_BIG(*) FROM sys.key_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )) <> 2
   OR (SELECT COUNT_BIG(*) FROM sys.foreign_keys
       WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')) <> 1
   OR (SELECT COUNT_BIG(*) FROM sys.check_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )) <> 0
   OR (SELECT COUNT_BIG(*) FROM sys.default_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )) <> 2
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX'), N'IsActive', N'ColumnId')
      AND name = N'DF_RT02_OTO_NguoiLX_IsActive'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX'), N'CreatedAtUtc', N'ColumnId')
      AND name = N'DF_RT02_OTO_NguoiLX_CreatedAtUtc'
)
   OR (SELECT COUNT_BIG(*) FROM sys.indexes
       WHERE object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )
         AND index_id > 0) <> 2
   OR
(
    SELECT STRING_AGG(
        CONVERT(nvarchar(max), CONCAT(
            OBJECT_NAME(keyItem.parent_object_id), N'|',
            keyItem.name, N'|',
            indexColumn.key_ordinal, N'|',
            columnItem.name)),
        N';'
    ) WITHIN GROUP
    (
        ORDER BY
            OBJECT_NAME(keyItem.parent_object_id),
            keyItem.name,
            indexColumn.key_ordinal
    )
    FROM sys.key_constraints AS keyItem
    INNER JOIN sys.index_columns AS indexColumn
        ON indexColumn.object_id = keyItem.parent_object_id
       AND indexColumn.index_id = keyItem.unique_index_id
       AND indexColumn.key_ordinal > 0
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = indexColumn.object_id
       AND columnItem.column_id = indexColumn.column_id
    WHERE keyItem.parent_object_id IN
    (
        OBJECT_ID(N'dbo.NguoiLX'),
        OBJECT_ID(N'dbo.NguoiLX_HoSo')
    )
) <> N'NguoiLX|PK_RT02_OTO_NguoiLX|1|IdentityHmac;NguoiLX_HoSo|PK_RT02_OTO_NguoiLX_HoSo|1|IdentityHmac'
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_key_columns AS foreignColumn
    INNER JOIN sys.foreign_keys AS foreignItem
        ON foreignItem.object_id = foreignColumn.constraint_object_id
    WHERE foreignItem.name = N'FK_RT02_OTO_NguoiLX_HoSo_NguoiLX'
      AND foreignColumn.parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
      AND foreignColumn.parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'IdentityHmac', N'ColumnId')
      AND foreignColumn.referenced_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND foreignColumn.referenced_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX'), N'IdentityHmac', N'ColumnId')
)
    THROW 527454, 'RT02 OTO key/constraint allowlist failed.', 1;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'QLHV_RT02_MOTO_TEST'
   OR DB_ID() <> 6
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A')
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
      N'CSDLTTTC\QLHVRT02'
    THROW 527455, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO identity.', 1;

IF DB_NAME() IN
(
    N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
    N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1',
    N'CSDL_OTO_V1_BAK', N'CSDL_MOTO_V1_BAK'
)
    THROW 527456, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production name.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B-OPERATOR-APPROVAL-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_DATASET_MODE'
      AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
      AND CONVERT(nvarchar(128), value) = N'FALSE'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_EXPIRES_AT_UTC'
      AND CONVERT(nvarchar(128), value) = N'2026-07-31T16:59:59Z'
)
    THROW 527457, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO markers.', 1;

IF EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527476, 'ISOLATED_DATABASE_IDENTITY_REJECTED: MOTO synonym route.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.tables
    WHERE is_ms_shipped = 0
      AND
      (
          SCHEMA_NAME(schema_id) <> N'dbo'
          OR name NOT IN (N'NguoiLX', N'NguoiLX_HoSo')
      )
)
    THROW 527458, 'RT02 MOTO contains a non-allowlisted table.', 1;

IF OBJECT_ID(N'dbo.NguoiLX', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NguoiLX
    (
        IdentityHmac char(64) NOT NULL
            CONSTRAINT PK_RT02_MOTO_NguoiLX PRIMARY KEY,
        ScenarioCode varchar(40) NOT NULL,
        DatasetRole varchar(40) NOT NULL,
        HoTen nvarchar(200) NOT NULL,
        SourceRowHash char(64) NOT NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_RT02_MOTO_NguoiLX_IsActive DEFAULT (1),
        CreatedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_RT02_MOTO_NguoiLX_CreatedAtUtc
            DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'dbo.NguoiLX_HoSo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NguoiLX_HoSo
    (
        IdentityHmac char(64) NOT NULL
            CONSTRAINT PK_RT02_MOTO_NguoiLX_HoSo PRIMARY KEY,
        PayloadHash char(64) NOT NULL,
        CONSTRAINT FK_RT02_MOTO_NguoiLX_HoSo_NguoiLX
            FOREIGN KEY (IdentityHmac) REFERENCES dbo.NguoiLX(IdentityHmac)
    );
END;
GO

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'NguoiLX', 1, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (N'NguoiLX', 2, N'ScenarioCode', N'varchar', 40, 0, 0, 0),
            (N'NguoiLX', 3, N'DatasetRole', N'varchar', 40, 0, 0, 0),
            (N'NguoiLX', 4, N'HoTen', N'nvarchar', 400, 0, 0, 0),
            (N'NguoiLX', 5, N'SourceRowHash', N'char', 64, 0, 0, 0),
            (N'NguoiLX', 6, N'IsActive', N'bit', 1, 0, 0, 0),
            (N'NguoiLX', 7, N'CreatedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'NguoiLX_HoSo', 1, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (N'NguoiLX_HoSo', 2, N'PayloadHash', N'char', 64, 0, 0, 0)
    ) AS expected
    (
        TableName, ColumnId, ColumnName, TypeName, MaxLength, ScaleValue,
        IsNullable, IsIdentity
    )
    FULL OUTER JOIN
    (
        SELECT
            tableItem.name AS TableName,
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeItem.name AS TypeName,
            CONVERT(int, columnItem.max_length) AS MaxLength,
            CONVERT(int, columnItem.scale) AS ScaleValue,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity
        FROM sys.tables AS tableItem
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = tableItem.object_id
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        WHERE SCHEMA_NAME(tableItem.schema_id) = N'dbo'
          AND tableItem.name IN (N'NguoiLX', N'NguoiLX_HoSo')
    ) AS actual
        ON actual.TableName = expected.TableName
       AND actual.ColumnId = expected.ColumnId
       AND actual.ColumnName = expected.ColumnName
    WHERE expected.TableName IS NULL
       OR actual.TableName IS NULL
       OR actual.TypeName <> expected.TypeName
       OR actual.MaxLength <> expected.MaxLength
       OR actual.ScaleValue <> expected.ScaleValue
       OR actual.IsNullable <> expected.IsNullable
       OR actual.IsIdentity <> expected.IsIdentity
)
    THROW 527479, 'RT02 MOTO exact column metadata is incompatible.', 1;

IF EXISTS (SELECT 1 FROM dbo.NguoiLX)
   OR EXISTS (SELECT 1 FROM dbo.NguoiLX_HoSo)
    THROW 527459, 'RT02 MOTO schema-only tables must remain empty.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
) <> 2
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.NguoiLX')
   ) <> 7
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
   ) <> 2
    THROW 527460, 'RT02 MOTO schema shape is incompatible.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND name = N'PK_RT02_MOTO_NguoiLX'
      AND type = N'PK'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
      AND name = N'PK_RT02_MOTO_NguoiLX_HoSo'
      AND type = N'PK'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
      AND referenced_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND name = N'FK_RT02_MOTO_NguoiLX_HoSo_NguoiLX'
)
   OR (SELECT COUNT_BIG(*) FROM sys.key_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )) <> 2
   OR (SELECT COUNT_BIG(*) FROM sys.foreign_keys
       WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')) <> 1
   OR (SELECT COUNT_BIG(*) FROM sys.check_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )) <> 0
   OR (SELECT COUNT_BIG(*) FROM sys.default_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )) <> 2
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX'), N'IsActive', N'ColumnId')
      AND name = N'DF_RT02_MOTO_NguoiLX_IsActive'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX'), N'CreatedAtUtc', N'ColumnId')
      AND name = N'DF_RT02_MOTO_NguoiLX_CreatedAtUtc'
)
   OR (SELECT COUNT_BIG(*) FROM sys.indexes
       WHERE object_id IN
       (
           OBJECT_ID(N'dbo.NguoiLX'),
           OBJECT_ID(N'dbo.NguoiLX_HoSo')
       )
         AND index_id > 0) <> 2
   OR
(
    SELECT STRING_AGG(
        CONVERT(nvarchar(max), CONCAT(
            OBJECT_NAME(keyItem.parent_object_id), N'|',
            keyItem.name, N'|',
            indexColumn.key_ordinal, N'|',
            columnItem.name)),
        N';'
    ) WITHIN GROUP
    (
        ORDER BY
            OBJECT_NAME(keyItem.parent_object_id),
            keyItem.name,
            indexColumn.key_ordinal
    )
    FROM sys.key_constraints AS keyItem
    INNER JOIN sys.index_columns AS indexColumn
        ON indexColumn.object_id = keyItem.parent_object_id
       AND indexColumn.index_id = keyItem.unique_index_id
       AND indexColumn.key_ordinal > 0
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = indexColumn.object_id
       AND columnItem.column_id = indexColumn.column_id
    WHERE keyItem.parent_object_id IN
    (
        OBJECT_ID(N'dbo.NguoiLX'),
        OBJECT_ID(N'dbo.NguoiLX_HoSo')
    )
) <> N'NguoiLX|PK_RT02_MOTO_NguoiLX|1|IdentityHmac;NguoiLX_HoSo|PK_RT02_MOTO_NguoiLX_HoSo|1|IdentityHmac'
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_key_columns AS foreignColumn
    INNER JOIN sys.foreign_keys AS foreignItem
        ON foreignItem.object_id = foreignColumn.constraint_object_id
    WHERE foreignItem.name = N'FK_RT02_MOTO_NguoiLX_HoSo_NguoiLX'
      AND foreignColumn.parent_object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo')
      AND foreignColumn.parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX_HoSo'), N'IdentityHmac', N'ColumnId')
      AND foreignColumn.referenced_object_id = OBJECT_ID(N'dbo.NguoiLX')
      AND foreignColumn.referenced_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.NguoiLX'), N'IdentityHmac', N'ColumnId')
)
    THROW 527461, 'RT02 MOTO key/constraint allowlist failed.', 1;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'QLHV_RT02_TARGET_TEST'
   OR DB_ID() <> 7
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <>
      N'CSDLTTTC\QLHVRT02'
    THROW 527462, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target identity.', 1;

IF DB_NAME() IN
(
    N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
    N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1',
    N'CSDL_OTO_V1_BAK', N'CSDL_MOTO_V1_BAK'
)
    THROW 527463, 'ISOLATED_DATABASE_IDENTITY_REJECTED: production name.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_ISOLATED_ENVIRONMENT_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_OWNER_APPROVAL_ID'
      AND CONVERT(nvarchar(128), value) =
          N'RT02B-OPERATOR-APPROVAL-20260727-01'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_DATASET_MODE'
      AND CONVERT(nvarchar(128), value) = N'SYNTHETIC'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
      AND CONVERT(nvarchar(128), value) = N'FALSE'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 0
      AND name = N'RT02_EXPIRES_AT_UTC'
      AND CONVERT(nvarchar(128), value) = N'2026-07-31T16:59:59Z'
)
    THROW 527464, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target markers.', 1;

IF EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527477, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target synonym route.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.tables
    WHERE is_ms_shipped = 0
      AND
      (
          SCHEMA_NAME(schema_id) <> N'dbo'
          OR name NOT IN
          (
              N'Rt02Learner',
              N'Rt02ManualReviewEvidence',
              N'Rt02ApplyMarker',
              N'Rt02ApplyCheckpoint',
              N'Rt02EnvironmentState'
          )
      )
)
    THROW 527465, 'RT02 target contains a non-allowlisted table.', 1;

IF OBJECT_ID(N'dbo.Rt02Learner', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rt02Learner
    (
        IdentityHmac char(64) NOT NULL
            CONSTRAINT PK_Rt02Learner PRIMARY KEY,
        SourceProfile varchar(20) NOT NULL,
        ScenarioCode varchar(40) NOT NULL,
        DatasetRole varchar(40) NOT NULL,
        HoTen nvarchar(200) NOT NULL,
        MappedHash char(64) NOT NULL,
        QlhvOwnedHash char(64) NOT NULL,
        WorkflowState varchar(40) NOT NULL,
        NotesHash char(64) NOT NULL,
        PhotoState varchar(40) NOT NULL,
        Active bit NOT NULL,
        SoftDeleted bit NOT NULL,
        CreatedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_Rt02Learner_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2(7) NOT NULL
            CONSTRAINT DF_Rt02Learner_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Rt02Learner_DeleteState
            CHECK
            (
                (Active = 1 AND SoftDeleted = 0)
                OR (Active = 0 AND SoftDeleted = 1)
            )
    );
END;
GO

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (1, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (2, N'SourceProfile', N'varchar', 20, 0, 0, 0),
            (3, N'ScenarioCode', N'varchar', 40, 0, 0, 0),
            (4, N'DatasetRole', N'varchar', 40, 0, 0, 0),
            (5, N'HoTen', N'nvarchar', 400, 0, 0, 0),
            (6, N'MappedHash', N'char', 64, 0, 0, 0),
            (7, N'QlhvOwnedHash', N'char', 64, 0, 0, 0),
            (8, N'WorkflowState', N'varchar', 40, 0, 0, 0),
            (9, N'NotesHash', N'char', 64, 0, 0, 0),
            (10, N'PhotoState', N'varchar', 40, 0, 0, 0),
            (11, N'Active', N'bit', 1, 0, 0, 0),
            (12, N'SoftDeleted', N'bit', 1, 0, 0, 0),
            (13, N'CreatedAtUtc', N'datetime2', 8, 7, 0, 0),
            (14, N'UpdatedAtUtc', N'datetime2', 8, 7, 0, 0)
    ) AS expected
    (
        ColumnId, ColumnName, TypeName, MaxLength, ScaleValue,
        IsNullable, IsIdentity
    )
    FULL OUTER JOIN
    (
        SELECT
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeItem.name AS TypeName,
            CONVERT(int, columnItem.max_length) AS MaxLength,
            CONVERT(int, columnItem.scale) AS ScaleValue,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity
        FROM sys.columns AS columnItem
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        WHERE columnItem.object_id = OBJECT_ID(N'dbo.Rt02Learner')
    ) AS actual
        ON actual.ColumnId = expected.ColumnId
       AND actual.ColumnName = expected.ColumnName
    WHERE expected.ColumnName IS NULL
       OR actual.ColumnName IS NULL
       OR actual.TypeName <> expected.TypeName
       OR actual.MaxLength <> expected.MaxLength
       OR actual.ScaleValue <> expected.ScaleValue
       OR actual.IsNullable <> expected.IsNullable
       OR actual.IsIdentity <> expected.IsIdentity
)
    THROW 527480, 'RT02 target learner exact column metadata is incompatible.', 1;

IF EXISTS (SELECT 1 FROM dbo.Rt02Learner)
    THROW 527466, 'RT02 target learner table must remain empty.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
      AND index_id > 0
      AND name NOT IN
      (
          N'PK_Rt02Learner',
          N'UX_Rt02Learner_ActiveIdentity'
      )
)
    THROW 527481, 'RT02 target learner contains a non-allowlisted index.', 1;
GO

SELECT
    N'FILTERED_INDEX_SET_OPTIONS_BEFORE' AS Evidence,
    CONVERT(int, SESSIONPROPERTY(N'ANSI_NULLS')) AS ANSI_NULLS,
    CONVERT(int, SESSIONPROPERTY(N'ANSI_PADDING')) AS ANSI_PADDING,
    CONVERT(int, SESSIONPROPERTY(N'ANSI_WARNINGS')) AS ANSI_WARNINGS,
    CONVERT(int, SESSIONPROPERTY(N'ARITHABORT')) AS ARITHABORT,
    CONVERT(int, SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL'))
        AS CONCAT_NULL_YIELDS_NULL,
    CONVERT(int, SESSIONPROPERTY(N'QUOTED_IDENTIFIER'))
        AS QUOTED_IDENTIFIER,
    CONVERT(int, SESSIONPROPERTY(N'NUMERIC_ROUNDABORT'))
        AS NUMERIC_ROUNDABORT;
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF ISNULL(CONVERT(int, SESSIONPROPERTY(N'ANSI_NULLS')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'ANSI_PADDING')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'ANSI_WARNINGS')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'ARITHABORT')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'QUOTED_IDENTIFIER')), -1) <> 1
   OR ISNULL(CONVERT(int, SESSIONPROPERTY(N'NUMERIC_ROUNDABORT')), -1) <> 0
    THROW 527467, 'RT02 filtered-index canonical SET options are unresolved.', 1;

SELECT
    N'FILTERED_INDEX_SET_OPTIONS_AFTER' AS Evidence,
    CONVERT(int, SESSIONPROPERTY(N'ANSI_NULLS')) AS ANSI_NULLS,
    CONVERT(int, SESSIONPROPERTY(N'ANSI_PADDING')) AS ANSI_PADDING,
    CONVERT(int, SESSIONPROPERTY(N'ANSI_WARNINGS')) AS ANSI_WARNINGS,
    CONVERT(int, SESSIONPROPERTY(N'ARITHABORT')) AS ARITHABORT,
    CONVERT(int, SESSIONPROPERTY(N'CONCAT_NULL_YIELDS_NULL'))
        AS CONCAT_NULL_YIELDS_NULL,
    CONVERT(int, SESSIONPROPERTY(N'QUOTED_IDENTIFIER'))
        AS QUOTED_IDENTIFIER,
    CONVERT(int, SESSIONPROPERTY(N'NUMERIC_ROUNDABORT'))
        AS NUMERIC_ROUNDABORT;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
      AND name = N'UX_Rt02Learner_ActiveIdentity'
      AND
      (
          is_unique <> 1
          OR has_filter <> 1
          OR type <> 2
          OR is_disabled <> 0
          OR is_hypothetical <> 0
          OR REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                 LOWER(filter_definition),
                 N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
             <> N'active=1andsoftdeleted=0'
          OR
          (
              SELECT STRING_AGG(columnItem.name, N',')
                  WITHIN GROUP (ORDER BY indexColumn.key_ordinal)
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnItem
                  ON columnItem.object_id = indexColumn.object_id
                 AND columnItem.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = OBJECT_ID(N'dbo.Rt02Learner')
                AND indexColumn.index_id =
                (
                    SELECT index_id
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
                      AND name = N'UX_Rt02Learner_ActiveIdentity'
                )
                AND indexColumn.key_ordinal > 0
          ) <> N'SourceProfile,IdentityHmac'
          OR
          (
              SELECT COUNT_BIG(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = OBJECT_ID(N'dbo.Rt02Learner')
                AND indexColumn.index_id =
                (
                    SELECT index_id
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
                      AND name = N'UX_Rt02Learner_ActiveIdentity'
                )
                AND indexColumn.is_included_column = 1
          ) <> 0
      )
)
    THROW 527482, 'RT02 existing filtered index has an incompatible shape.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
          AND name = N'UX_Rt02Learner_ActiveIdentity'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_Rt02Learner_ActiveIdentity
            ON dbo.Rt02Learner(SourceProfile, IdentityHmac)
            WHERE Active = 1 AND SoftDeleted = 0;
    END;

    IF OBJECT_ID(N'dbo.Rt02ManualReviewEvidence', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Rt02ManualReviewEvidence
        (
            CycleId varchar(120) NOT NULL,
            OperationId varchar(160) NOT NULL,
            IdentityHmac char(64) NOT NULL,
            Disposition varchar(60) NOT NULL,
            DispositionHash char(64) NOT NULL,
            TargetRetainedActive bit NOT NULL,
            TargetMutated bit NOT NULL,
            CreatedAtUtc datetime2(7) NOT NULL
                CONSTRAINT DF_Rt02ManualReviewEvidence_CreatedAtUtc
                DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_Rt02ManualReviewEvidence
                PRIMARY KEY (CycleId, OperationId),
            CONSTRAINT CK_Rt02ManualReviewEvidence_NoMutation
                CHECK (TargetRetainedActive = 1 AND TargetMutated = 0)
        );
    END;

    IF OBJECT_ID(N'dbo.Rt02ApplyMarker', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Rt02ApplyMarker
        (
            CycleId varchar(120) NOT NULL
                CONSTRAINT PK_Rt02ApplyMarker PRIMARY KEY,
            PlanHash char(64) NOT NULL,
            DispositionHash char(64) NOT NULL,
            InsertedRows int NOT NULL,
            UpdatedRows int NOT NULL,
            RetainedRows int NOT NULL,
            PreservedQlhvOwnedHash char(64) NOT NULL,
            CommittedAtUtc datetime2(7) NOT NULL
        );
    END;

    IF OBJECT_ID(N'dbo.Rt02ApplyCheckpoint', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Rt02ApplyCheckpoint
        (
            SourceProfile varchar(20) NOT NULL,
            Mode varchar(40) NOT NULL,
            MappingFingerprint char(64) NOT NULL,
            EnvironmentId varchar(128) NOT NULL,
            CycleId varchar(120) NOT NULL,
            PlanHash char(64) NOT NULL,
            MarkerHash char(64) NOT NULL,
            SourceWatermark bigint NOT NULL,
            PublishedAtUtc datetime2(7) NOT NULL,
            CONSTRAINT PK_Rt02ApplyCheckpoint
                PRIMARY KEY
                (
                    SourceProfile,
                    Mode,
                    MappingFingerprint,
                    EnvironmentId
                )
        );
    END;

    IF OBJECT_ID(N'dbo.Rt02EnvironmentState', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Rt02EnvironmentState
        (
            EnvironmentId varchar(128) NOT NULL
                CONSTRAINT PK_Rt02EnvironmentState PRIMARY KEY,
            DatasetFingerprint char(64) NOT NULL,
            MappingFingerprint char(64) NOT NULL,
            SourceSchemaFingerprint char(64) NOT NULL,
            TargetSchemaFingerprint char(64) NOT NULL,
            IdentityNormalizationVersion varchar(60) NOT NULL,
            DatasetMode varchar(20) NOT NULL,
            PiiRows int NOT NULL,
            CreatedAtUtc datetime2(7) NOT NULL,
            CONSTRAINT CK_Rt02EnvironmentState_Synthetic
                CHECK (DatasetMode = 'SYNTHETIC' AND PiiRows = 0)
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'Rt02Learner', 1, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (N'Rt02Learner', 2, N'SourceProfile', N'varchar', 20, 0, 0, 0),
            (N'Rt02Learner', 3, N'ScenarioCode', N'varchar', 40, 0, 0, 0),
            (N'Rt02Learner', 4, N'DatasetRole', N'varchar', 40, 0, 0, 0),
            (N'Rt02Learner', 5, N'HoTen', N'nvarchar', 400, 0, 0, 0),
            (N'Rt02Learner', 6, N'MappedHash', N'char', 64, 0, 0, 0),
            (N'Rt02Learner', 7, N'QlhvOwnedHash', N'char', 64, 0, 0, 0),
            (N'Rt02Learner', 8, N'WorkflowState', N'varchar', 40, 0, 0, 0),
            (N'Rt02Learner', 9, N'NotesHash', N'char', 64, 0, 0, 0),
            (N'Rt02Learner', 10, N'PhotoState', N'varchar', 40, 0, 0, 0),
            (N'Rt02Learner', 11, N'Active', N'bit', 1, 0, 0, 0),
            (N'Rt02Learner', 12, N'SoftDeleted', N'bit', 1, 0, 0, 0),
            (N'Rt02Learner', 13, N'CreatedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'Rt02Learner', 14, N'UpdatedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'Rt02ManualReviewEvidence', 1, N'CycleId', N'varchar', 120, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 2, N'OperationId', N'varchar', 160, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 3, N'IdentityHmac', N'char', 64, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 4, N'Disposition', N'varchar', 60, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 5, N'DispositionHash', N'char', 64, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 6, N'TargetRetainedActive', N'bit', 1, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 7, N'TargetMutated', N'bit', 1, 0, 0, 0),
            (N'Rt02ManualReviewEvidence', 8, N'CreatedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'Rt02ApplyMarker', 1, N'CycleId', N'varchar', 120, 0, 0, 0),
            (N'Rt02ApplyMarker', 2, N'PlanHash', N'char', 64, 0, 0, 0),
            (N'Rt02ApplyMarker', 3, N'DispositionHash', N'char', 64, 0, 0, 0),
            (N'Rt02ApplyMarker', 4, N'InsertedRows', N'int', 4, 0, 0, 0),
            (N'Rt02ApplyMarker', 5, N'UpdatedRows', N'int', 4, 0, 0, 0),
            (N'Rt02ApplyMarker', 6, N'RetainedRows', N'int', 4, 0, 0, 0),
            (N'Rt02ApplyMarker', 7, N'PreservedQlhvOwnedHash', N'char', 64, 0, 0, 0),
            (N'Rt02ApplyMarker', 8, N'CommittedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'Rt02ApplyCheckpoint', 1, N'SourceProfile', N'varchar', 20, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 2, N'Mode', N'varchar', 40, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 3, N'MappingFingerprint', N'char', 64, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 4, N'EnvironmentId', N'varchar', 128, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 5, N'CycleId', N'varchar', 120, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 6, N'PlanHash', N'char', 64, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 7, N'MarkerHash', N'char', 64, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 8, N'SourceWatermark', N'bigint', 8, 0, 0, 0),
            (N'Rt02ApplyCheckpoint', 9, N'PublishedAtUtc', N'datetime2', 8, 7, 0, 0),
            (N'Rt02EnvironmentState', 1, N'EnvironmentId', N'varchar', 128, 0, 0, 0),
            (N'Rt02EnvironmentState', 2, N'DatasetFingerprint', N'char', 64, 0, 0, 0),
            (N'Rt02EnvironmentState', 3, N'MappingFingerprint', N'char', 64, 0, 0, 0),
            (N'Rt02EnvironmentState', 4, N'SourceSchemaFingerprint', N'char', 64, 0, 0, 0),
            (N'Rt02EnvironmentState', 5, N'TargetSchemaFingerprint', N'char', 64, 0, 0, 0),
            (N'Rt02EnvironmentState', 6, N'IdentityNormalizationVersion', N'varchar', 60, 0, 0, 0),
            (N'Rt02EnvironmentState', 7, N'DatasetMode', N'varchar', 20, 0, 0, 0),
            (N'Rt02EnvironmentState', 8, N'PiiRows', N'int', 4, 0, 0, 0),
            (N'Rt02EnvironmentState', 9, N'CreatedAtUtc', N'datetime2', 8, 7, 0, 0)
    ) AS expected
    (
        TableName, ColumnId, ColumnName, TypeName, MaxLength, ScaleValue,
        IsNullable, IsIdentity
    )
    FULL OUTER JOIN
    (
        SELECT
            tableItem.name AS TableName,
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeItem.name AS TypeName,
            CONVERT(int, columnItem.max_length) AS MaxLength,
            CONVERT(int, columnItem.scale) AS ScaleValue,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity
        FROM sys.tables AS tableItem
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = tableItem.object_id
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        WHERE SCHEMA_NAME(tableItem.schema_id) = N'dbo'
          AND tableItem.name IN
          (
              N'Rt02Learner',
              N'Rt02ManualReviewEvidence',
              N'Rt02ApplyMarker',
              N'Rt02ApplyCheckpoint',
              N'Rt02EnvironmentState'
          )
    ) AS actual
        ON actual.TableName = expected.TableName
       AND actual.ColumnId = expected.ColumnId
       AND actual.ColumnName = expected.ColumnName
    WHERE expected.TableName IS NULL
       OR actual.TableName IS NULL
       OR actual.TypeName <> expected.TypeName
       OR actual.MaxLength <> expected.MaxLength
       OR actual.ScaleValue <> expected.ScaleValue
       OR actual.IsNullable <> expected.IsNullable
       OR actual.IsIdentity <> expected.IsIdentity
)
    THROW 527483, 'RT02 target exact column metadata is incompatible.', 1;

IF EXISTS (SELECT 1 FROM dbo.Rt02Learner)
   OR EXISTS (SELECT 1 FROM dbo.Rt02ManualReviewEvidence)
   OR EXISTS (SELECT 1 FROM dbo.Rt02ApplyMarker)
   OR EXISTS (SELECT 1 FROM dbo.Rt02ApplyCheckpoint)
   OR EXISTS (SELECT 1 FROM dbo.Rt02EnvironmentState)
    THROW 527468, 'RT02 schema-only target tables must remain empty.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.tables
    WHERE is_ms_shipped = 0
) <> 5
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
   ) <> 14
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.Rt02ManualReviewEvidence')
   ) <> 8
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.Rt02ApplyMarker')
   ) <> 8
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.Rt02ApplyCheckpoint')
   ) <> 9
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.columns
       WHERE object_id = OBJECT_ID(N'dbo.Rt02EnvironmentState')
   ) <> 9
    THROW 527469, 'RT02 target schema shape is incompatible.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Rt02Learner')
      AND name = N'UX_Rt02Learner_ActiveIdentity'
      AND is_unique = 1
      AND has_filter = 1
      AND type = 2
      AND is_disabled = 0
      AND is_hypothetical = 0
      AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(filter_definition),
             N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'')
          = N'active=1andsoftdeleted=0'
)
   OR
   (
       SELECT STRING_AGG(columnItem.name, N',')
           WITHIN GROUP (ORDER BY indexColumn.key_ordinal)
       FROM sys.index_columns AS indexColumn
       INNER JOIN sys.indexes AS indexItem
           ON indexItem.object_id = indexColumn.object_id
          AND indexItem.index_id = indexColumn.index_id
       INNER JOIN sys.columns AS columnItem
           ON columnItem.object_id = indexColumn.object_id
          AND columnItem.column_id = indexColumn.column_id
       WHERE indexItem.object_id = OBJECT_ID(N'dbo.Rt02Learner')
         AND indexItem.name = N'UX_Rt02Learner_ActiveIdentity'
         AND indexColumn.key_ordinal > 0
   ) <> N'SourceProfile,IdentityHmac'
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.index_columns AS indexColumn
       INNER JOIN sys.indexes AS indexItem
           ON indexItem.object_id = indexColumn.object_id
          AND indexItem.index_id = indexColumn.index_id
       WHERE indexItem.object_id = OBJECT_ID(N'dbo.Rt02Learner')
         AND indexItem.name = N'UX_Rt02Learner_ActiveIdentity'
         AND indexColumn.is_included_column = 1
   ) <> 0
    THROW 527470, 'RT02 filtered-index allowlist verification failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.key_constraints
    WHERE parent_object_id IN
    (
        OBJECT_ID(N'dbo.Rt02Learner'),
        OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
        OBJECT_ID(N'dbo.Rt02ApplyMarker'),
        OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
        OBJECT_ID(N'dbo.Rt02EnvironmentState')
    )
      AND name IN
      (
          N'PK_Rt02Learner',
          N'PK_Rt02ManualReviewEvidence',
          N'PK_Rt02ApplyMarker',
          N'PK_Rt02ApplyCheckpoint',
          N'PK_Rt02EnvironmentState'
      )
) <> 5
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.check_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.Rt02Learner'),
           OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
           OBJECT_ID(N'dbo.Rt02EnvironmentState')
       )
         AND name IN
         (
             N'CK_Rt02Learner_DeleteState',
             N'CK_Rt02ManualReviewEvidence_NoMutation',
             N'CK_Rt02EnvironmentState_Synthetic'
         )
   ) <> 3
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.key_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.Rt02Learner'),
           OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
           OBJECT_ID(N'dbo.Rt02ApplyMarker'),
           OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
           OBJECT_ID(N'dbo.Rt02EnvironmentState')
       )
   ) <> 5
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.check_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.Rt02Learner'),
           OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
           OBJECT_ID(N'dbo.Rt02ApplyMarker'),
           OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
           OBJECT_ID(N'dbo.Rt02EnvironmentState')
       )
   ) <> 3
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.default_constraints
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.Rt02Learner'),
           OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
           OBJECT_ID(N'dbo.Rt02ApplyMarker'),
           OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
           OBJECT_ID(N'dbo.Rt02EnvironmentState')
       )
   ) <> 3
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.foreign_keys
       WHERE parent_object_id IN
       (
           OBJECT_ID(N'dbo.Rt02Learner'),
           OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
           OBJECT_ID(N'dbo.Rt02ApplyMarker'),
           OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
           OBJECT_ID(N'dbo.Rt02EnvironmentState')
       )
   ) <> 0
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.indexes
       WHERE object_id IN
       (
           OBJECT_ID(N'dbo.Rt02Learner'),
           OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
           OBJECT_ID(N'dbo.Rt02ApplyMarker'),
           OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
           OBJECT_ID(N'dbo.Rt02EnvironmentState')
       )
         AND index_id > 0
   ) <> 6
   OR EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id IN
    (
        OBJECT_ID(N'dbo.Rt02Learner'),
        OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
        OBJECT_ID(N'dbo.Rt02ApplyMarker'),
        OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
        OBJECT_ID(N'dbo.Rt02EnvironmentState')
    )
      AND index_id > 0
      AND name NOT IN
      (
          N'PK_Rt02Learner',
          N'UX_Rt02Learner_ActiveIdentity',
          N'PK_Rt02ManualReviewEvidence',
          N'PK_Rt02ApplyMarker',
          N'PK_Rt02ApplyCheckpoint',
          N'PK_Rt02EnvironmentState'
      )
)
   OR
(
    SELECT STRING_AGG(
        CONVERT(nvarchar(max), CONCAT(
            OBJECT_NAME(keyItem.parent_object_id), N'|',
            keyItem.name, N'|',
            indexColumn.key_ordinal, N'|',
            columnItem.name)),
        N';'
    ) WITHIN GROUP
    (
        ORDER BY
            OBJECT_NAME(keyItem.parent_object_id),
            keyItem.name,
            indexColumn.key_ordinal
    )
    FROM sys.key_constraints AS keyItem
    INNER JOIN sys.index_columns AS indexColumn
        ON indexColumn.object_id = keyItem.parent_object_id
       AND indexColumn.index_id = keyItem.unique_index_id
       AND indexColumn.key_ordinal > 0
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = indexColumn.object_id
       AND columnItem.column_id = indexColumn.column_id
    WHERE keyItem.parent_object_id IN
    (
        OBJECT_ID(N'dbo.Rt02Learner'),
        OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'),
        OBJECT_ID(N'dbo.Rt02ApplyMarker'),
        OBJECT_ID(N'dbo.Rt02ApplyCheckpoint'),
        OBJECT_ID(N'dbo.Rt02EnvironmentState')
    )
) <> N'Rt02ApplyCheckpoint|PK_Rt02ApplyCheckpoint|1|SourceProfile;Rt02ApplyCheckpoint|PK_Rt02ApplyCheckpoint|2|Mode;Rt02ApplyCheckpoint|PK_Rt02ApplyCheckpoint|3|MappingFingerprint;Rt02ApplyCheckpoint|PK_Rt02ApplyCheckpoint|4|EnvironmentId;Rt02ApplyMarker|PK_Rt02ApplyMarker|1|CycleId;Rt02EnvironmentState|PK_Rt02EnvironmentState|1|EnvironmentId;Rt02Learner|PK_Rt02Learner|1|IdentityHmac;Rt02ManualReviewEvidence|PK_Rt02ManualReviewEvidence|1|CycleId;Rt02ManualReviewEvidence|PK_Rt02ManualReviewEvidence|2|OperationId'
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Rt02Learner')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.Rt02Learner'), N'CreatedAtUtc', N'ColumnId')
      AND name = N'DF_Rt02Learner_CreatedAtUtc'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Rt02Learner')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.Rt02Learner'), N'UpdatedAtUtc', N'ColumnId')
      AND name = N'DF_Rt02Learner_UpdatedAtUtc'
)
   OR NOT EXISTS
(
    SELECT 1
    FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Rt02ManualReviewEvidence')
      AND parent_column_id = COLUMNPROPERTY(
          OBJECT_ID(N'dbo.Rt02ManualReviewEvidence'), N'CreatedAtUtc', N'ColumnId')
      AND name = N'DF_Rt02ManualReviewEvidence_CreatedAtUtc'
)
    THROW 527471, 'RT02 target constraint allowlist verification failed.', 1;
GO

USE [master];
GO
IF EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id IN
    (
        DB_ID(N'QLHV_RT02_OTO_TEST'),
        DB_ID(N'QLHV_RT02_MOTO_TEST'),
        DB_ID(N'QLHV_RT02_TARGET_TEST')
    )
)
    THROW 527472, 'RT02 schema-only postcondition requires Change Tracking OFF.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
      AND
      (
          snapshot_isolation_state <> 0
          OR is_read_committed_snapshot_on <> 0
      )
)
    THROW 527473, 'RT02 schema-only postcondition requires Snapshot/RCSI OFF.', 1;

SELECT
    N'RT02B_SCHEMA_GATE_MATERIALIZED' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_OTO_TEST].sys.tables
        WHERE is_ms_shipped = 0) AS OtoTableCount,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_MOTO_TEST].sys.tables
        WHERE is_ms_shipped = 0) AS MotoTableCount,
    (SELECT COUNT_BIG(*) FROM [QLHV_RT02_TARGET_TEST].sys.tables
        WHERE is_ms_shipped = 0) AS TargetTableCount,
    (
        SELECT COUNT_BIG(*)
        FROM [QLHV_RT02_TARGET_TEST].sys.indexes AS indexItem
        INNER JOIN [QLHV_RT02_TARGET_TEST].sys.tables AS tableItem
            ON tableItem.object_id = indexItem.object_id
        INNER JOIN [QLHV_RT02_TARGET_TEST].sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        WHERE schemaItem.name = N'dbo'
          AND tableItem.name = N'Rt02Learner'
          AND indexItem.name = N'UX_Rt02Learner_ActiveIdentity'
          AND indexItem.is_unique = 1
          AND indexItem.has_filter = 1
    ) AS FilteredIndexCount;
GO
