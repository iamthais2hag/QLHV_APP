USE [master];
GO
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC\QLHVRT02'
    THROW 527500, 'RT02_SCHEMA_PROOF_REJECTED: exact server identity.', 1;

IF CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE N'%Developer%'
    THROW 527501, 'RT02_SCHEMA_PROOF_REJECTED: SQL edition/version.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_OTO_TEST'
      AND databaseItem.database_id = 5
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
      AND databaseItem.snapshot_isolation_state = 0
      AND databaseItem.is_read_committed_snapshot_on = 0
)
    THROW 527502, 'RT02_SCHEMA_PROOF_REJECTED: OTO database identity/state.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_MOTO_TEST'
      AND databaseItem.database_id = 6
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
      AND databaseItem.snapshot_isolation_state = 0
      AND databaseItem.is_read_committed_snapshot_on = 0
)
    THROW 527503, 'RT02_SCHEMA_PROOF_REJECTED: MOTO database identity/state.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_TARGET_TEST'
      AND databaseItem.database_id = 7
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
      AND databaseItem.snapshot_isolation_state = 0
      AND databaseItem.is_read_committed_snapshot_on = 0
)
    THROW 527504, 'RT02_SCHEMA_PROOF_REJECTED: target database identity/state.', 1;

IF
(
    SELECT COUNT(DISTINCT databaseItem.database_id)
    FROM sys.databases AS databaseItem
    WHERE databaseItem.name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
) <> 3
   OR
(
    SELECT COUNT(DISTINCT recoveryItem.database_guid)
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
) <> 3
    THROW 527505, 'RT02_SCHEMA_PROOF_REJECTED: database identities are not distinct.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases AS trackingItem
    INNER JOIN sys.databases AS databaseItem
        ON databaseItem.database_id = trackingItem.database_id
    WHERE databaseItem.name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
)
    THROW 527506, 'RT02_SCHEMA_PROOF_REJECTED: Change Tracking is enabled.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'CSDL_OTO',
        N'CSDL_MOTO',
        N'CSDL_OTO_BAK',
        N'CSDL_MOTO_BAK',
        N'QLHV_APP',
        N'CSDL_OTO_V1',
        N'CSDL_MOTO_V1',
        N'CSDL_OTO_V1_BAK',
        N'CSDL_MOTO_V1_BAK'
    )
)
    THROW 527507, 'RT02_SCHEMA_PROOF_REJECTED: production database denylist conflict.', 1;

IF EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
    THROW 527508, 'RT02_SCHEMA_PROOF_REJECTED: linked server route exists.', 1;

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
    THROW 527509, 'RT02_SCHEMA_PROOF_REJECTED: production application session exists.', 1;

SELECT
    N'RT02B2_SCHEMA_GATE_SERVER' AS Evidence,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) AS Edition,
    CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) AS ProductMajorVersion,
    (SELECT COUNT_BIG(*) FROM sys.servers WHERE is_linked = 1)
        AS LinkedServerCount,
    (
        SELECT COUNT_BIG(*)
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
    ) AS ProductionApplicationSessionCount,
    (
        SELECT COUNT_BIG(*)
        FROM sys.change_tracking_databases AS trackingItem
        INNER JOIN sys.databases AS databaseItem
            ON databaseItem.database_id = trackingItem.database_id
        WHERE databaseItem.name IN
        (
            N'QLHV_RT02_OTO_TEST',
            N'QLHV_RT02_MOTO_TEST',
            N'QLHV_RT02_TARGET_TEST'
        )
    ) AS ChangeTrackingDatabaseCount;
GO

USE [QLHV_RT02_OTO_TEST];
GO
SET NOCOUNT ON;

IF DB_NAME() <> N'QLHV_RT02_OTO_TEST'
   OR DB_ID() <> 5
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789')
    THROW 527510, 'RT02_SCHEMA_PROOF_REJECTED: OTO context identity.', 1;

IF EXISTS
(
    SELECT expectedItem.PropertyName, expectedItem.PropertyValue
    FROM
    (
        VALUES
            (N'RT02_ISOLATED_ENVIRONMENT_ID', N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'),
            (N'RT02_OWNER_APPROVAL_ID', N'RT02B-OPERATOR-APPROVAL-20260727-01'),
            (N'RT02_DATASET_MODE', N'SYNTHETIC'),
            (N'RT02_PRODUCTION_ROUTE_ALLOWED', N'FALSE'),
            (N'RT02_EXPIRES_AT_UTC', N'2026-07-31T16:59:59Z')
    ) AS expectedItem(PropertyName, PropertyValue)
    EXCEPT
    SELECT
        CONVERT(nvarchar(128), propertyItem.name),
        CONVERT(nvarchar(128), propertyItem.value)
    FROM sys.extended_properties AS propertyItem
    WHERE propertyItem.class = 0
)
    THROW 527511, 'RT02_SCHEMA_PROOF_REJECTED: OTO TEST markers.', 1;

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527512, 'RT02_SCHEMA_PROOF_REJECTED: OTO feature/route state.', 1;

IF EXISTS
(
    SELECT expectedItem.SchemaName, expectedItem.TableName
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX'),
            (N'dbo', N'NguoiLX_HoSo')
    ) AS expectedItem(SchemaName, TableName)
    EXCEPT
    SELECT schemaItem.name, tableItem.name
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
)
   OR EXISTS
(
    SELECT schemaItem.name, tableItem.name
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
    EXCEPT
    SELECT expectedItem.SchemaName, expectedItem.TableName
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX'),
            (N'dbo', N'NguoiLX_HoSo')
    ) AS expectedItem(SchemaName, TableName)
)
    THROW 527513, 'RT02_SCHEMA_PROOF_REJECTED: OTO table allowlist.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', 1, N'IdentityHmac', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 2, N'ScenarioCode', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 3, N'DatasetRole', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 4, N'HoTen', N'nvarchar(200)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 5, N'SourceRowHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 6, N'IsActive', N'bit', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 7, N'CreatedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX_HoSo', 1, N'IdentityHmac', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX_HoSo', 2, N'PayloadHash', N'char(64)', 0, 0, 0, 0, 0)
    ) AS expectedItem
    (
        SchemaName, TableName, ColumnId, ColumnName, TypeSpec, IsNullable,
        IsIdentity, IsComputed, IsSparse, GeneratedAlwaysType
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeSpecItem.TypeSpec,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity,
            CONVERT(int, columnItem.is_computed) AS IsComputed,
            CONVERT(int, columnItem.is_sparse) AS IsSparse,
            CONVERT(int, columnItem.generated_always_type) AS GeneratedAlwaysType
        FROM sys.columns AS columnItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = columnItem.object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        CROSS APPLY
        (
            VALUES
            (
                CASE
                    WHEN typeItem.name IN (N'char', N'varchar', N'binary', N'varbinary')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'nchar', N'nvarchar')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length / 2)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'decimal', N'numeric')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            columnItem.precision,
                            N',',
                            columnItem.scale,
                            N')')
                    WHEN typeItem.name IN (N'datetime2', N'datetimeoffset', N'time')
                        THEN CONCAT(typeItem.name, N'(', columnItem.scale, N')')
                    ELSE typeItem.name
                END
            )
        ) AS typeSpecItem(TypeSpec)
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.ColumnName IS NULL
       OR actualItem.ColumnName IS NULL
       OR actualItem.ColumnId <> expectedItem.ColumnId
       OR actualItem.TypeSpec <> expectedItem.TypeSpec
       OR actualItem.IsNullable <> expectedItem.IsNullable
       OR actualItem.IsIdentity <> expectedItem.IsIdentity
       OR actualItem.IsComputed <> expectedItem.IsComputed
       OR actualItem.IsSparse <> expectedItem.IsSparse
       OR actualItem.GeneratedAlwaysType <> expectedItem.GeneratedAlwaysType
)
    THROW 527514, 'RT02_SCHEMA_PROOF_REJECTED: OTO column metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', N'IsActive', N'DF_RT02_OTO_NguoiLX_IsActive', N'1'),
            (N'dbo', N'NguoiLX', N'CreatedAtUtc', N'DF_RT02_OTO_NguoiLX_CreatedAtUtc', N'sysutcdatetime')
    ) AS expectedItem(SchemaName, TableName, ColumnName, ConstraintName, Definition)
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            columnItem.name AS ColumnName,
            defaultItem.name AS ConstraintName,
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    CONVERT(nvarchar(max), defaultItem.definition),
                    N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
            ) AS Definition
        FROM sys.default_constraints AS defaultItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = defaultItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = defaultItem.parent_object_id
           AND columnItem.column_id = defaultItem.parent_column_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.ColumnName IS NULL
       OR actualItem.ColumnName IS NULL
       OR actualItem.ConstraintName <> expectedItem.ConstraintName
       OR actualItem.Definition <> expectedItem.Definition
)
    THROW 527515, 'RT02_SCHEMA_PROOF_REJECTED: OTO default metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints AS checkItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = checkItem.parent_object_id
    WHERE tableItem.is_ms_shipped = 0
)
    THROW 527516, 'RT02_SCHEMA_PROOF_REJECTED: unexpected OTO check constraint.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', N'PK_RT02_OTO_NguoiLX', N'PK', N'PK_RT02_OTO_NguoiLX'),
            (N'dbo', N'NguoiLX_HoSo', N'PK_RT02_OTO_NguoiLX_HoSo', N'PK', N'PK_RT02_OTO_NguoiLX_HoSo')
    ) AS expectedItem(SchemaName, TableName, ConstraintName, ConstraintType, IndexName)
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            keyItem.name AS ConstraintName,
            keyItem.type AS ConstraintType,
            indexItem.name AS IndexName
        FROM sys.key_constraints AS keyItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = keyItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.indexes AS indexItem
            ON indexItem.object_id = keyItem.parent_object_id
           AND indexItem.index_id = keyItem.unique_index_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ConstraintName = expectedItem.ConstraintName
    WHERE expectedItem.ConstraintName IS NULL
       OR actualItem.ConstraintName IS NULL
       OR actualItem.ConstraintType <> expectedItem.ConstraintType
       OR actualItem.IndexName <> expectedItem.IndexName
)
    THROW 527517, 'RT02_SCHEMA_PROOF_REJECTED: OTO key constraint metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
        (
            N'dbo',
            N'NguoiLX_HoSo',
            N'FK_RT02_OTO_NguoiLX_HoSo_NguoiLX',
            N'dbo',
            N'NguoiLX',
            N'IdentityHmac',
            N'IdentityHmac',
            1,
            N'NO_ACTION',
            N'NO_ACTION',
            0,
            0
        )
    ) AS expectedItem
    (
        ParentSchema, ParentTable, ConstraintName, ReferencedSchema,
        ReferencedTable, ParentColumn, ReferencedColumn, ColumnOrdinal,
        UpdateAction, DeleteAction,
        IsDisabled, IsNotTrusted
    )
    FULL OUTER JOIN
    (
        SELECT
            parentSchema.name AS ParentSchema,
            parentTable.name AS ParentTable,
            foreignItem.name AS ConstraintName,
            referencedSchema.name AS ReferencedSchema,
            referencedTable.name AS ReferencedTable,
            parentColumn.name AS ParentColumn,
            referencedColumn.name AS ReferencedColumn,
            foreignColumn.constraint_column_id AS ColumnOrdinal,
            foreignItem.update_referential_action_desc AS UpdateAction,
            foreignItem.delete_referential_action_desc AS DeleteAction,
            CONVERT(int, foreignItem.is_disabled) AS IsDisabled,
            CONVERT(int, foreignItem.is_not_trusted) AS IsNotTrusted
        FROM sys.foreign_keys AS foreignItem
        INNER JOIN sys.tables AS parentTable
            ON parentTable.object_id = foreignItem.parent_object_id
        INNER JOIN sys.schemas AS parentSchema
            ON parentSchema.schema_id = parentTable.schema_id
        INNER JOIN sys.tables AS referencedTable
            ON referencedTable.object_id = foreignItem.referenced_object_id
        INNER JOIN sys.schemas AS referencedSchema
            ON referencedSchema.schema_id = referencedTable.schema_id
        INNER JOIN sys.foreign_key_columns AS foreignColumn
            ON foreignColumn.constraint_object_id = foreignItem.object_id
        INNER JOIN sys.columns AS parentColumn
            ON parentColumn.object_id = foreignColumn.parent_object_id
           AND parentColumn.column_id = foreignColumn.parent_column_id
        INNER JOIN sys.columns AS referencedColumn
            ON referencedColumn.object_id = foreignColumn.referenced_object_id
           AND referencedColumn.column_id = foreignColumn.referenced_column_id
        WHERE parentTable.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.ParentSchema = expectedItem.ParentSchema
       AND actualItem.ParentTable = expectedItem.ParentTable
       AND actualItem.ConstraintName = expectedItem.ConstraintName
    WHERE expectedItem.ConstraintName IS NULL
       OR actualItem.ConstraintName IS NULL
       OR actualItem.ReferencedSchema <> expectedItem.ReferencedSchema
       OR actualItem.ReferencedTable <> expectedItem.ReferencedTable
       OR actualItem.ParentColumn <> expectedItem.ParentColumn
       OR actualItem.ReferencedColumn <> expectedItem.ReferencedColumn
       OR actualItem.ColumnOrdinal <> expectedItem.ColumnOrdinal
       OR actualItem.UpdateAction <> expectedItem.UpdateAction
       OR actualItem.DeleteAction <> expectedItem.DeleteAction
       OR actualItem.IsDisabled <> expectedItem.IsDisabled
       OR actualItem.IsNotTrusted <> expectedItem.IsNotTrusted
)
    THROW 527518, 'RT02_SCHEMA_PROOF_REJECTED: OTO foreign key metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', N'PK_RT02_OTO_NguoiLX', N'CLUSTERED', 1, 1, 0, 0, N'', N'IdentityHmac', 1, 0, 0),
            (N'dbo', N'NguoiLX_HoSo', N'PK_RT02_OTO_NguoiLX_HoSo', N'CLUSTERED', 1, 1, 0, 0, N'', N'IdentityHmac', 1, 0, 0)
    ) AS expectedItem
    (
        SchemaName, TableName, IndexName, TypeDescription, IsUnique,
        IsPrimaryKey, IsUniqueConstraint, HasFilter, FilterDefinition,
        ColumnName, KeyOrdinal, IsDescending, IsIncluded
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            indexItem.name AS IndexName,
            indexItem.type_desc AS TypeDescription,
            CONVERT(int, indexItem.is_unique) AS IsUnique,
            CONVERT(int, indexItem.is_primary_key) AS IsPrimaryKey,
            CONVERT(int, indexItem.is_unique_constraint) AS IsUniqueConstraint,
            CONVERT(int, indexItem.has_filter) AS HasFilter,
            COALESCE(
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), indexItem.filter_definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                ),
                N''
            ) AS FilterDefinition,
            columnItem.name AS ColumnName,
            indexColumn.key_ordinal AS KeyOrdinal,
            CONVERT(int, indexColumn.is_descending_key) AS IsDescending,
            CONVERT(int, indexColumn.is_included_column) AS IsIncluded
        FROM sys.indexes AS indexItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = indexItem.object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.index_columns AS indexColumn
            ON indexColumn.object_id = indexItem.object_id
           AND indexColumn.index_id = indexItem.index_id
           AND
           (
               indexColumn.key_ordinal > 0
               OR indexColumn.is_included_column = 1
           )
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = indexColumn.object_id
           AND columnItem.column_id = indexColumn.column_id
        WHERE tableItem.is_ms_shipped = 0
          AND indexItem.index_id > 0
    ) AS actualItem
       ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.IndexName = expectedItem.IndexName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.IndexName IS NULL
       OR actualItem.IndexName IS NULL
       OR actualItem.TypeDescription <> expectedItem.TypeDescription
       OR actualItem.IsUnique <> expectedItem.IsUnique
       OR actualItem.IsPrimaryKey <> expectedItem.IsPrimaryKey
       OR actualItem.IsUniqueConstraint <> expectedItem.IsUniqueConstraint
       OR actualItem.HasFilter <> expectedItem.HasFilter
       OR actualItem.FilterDefinition <> expectedItem.FilterDefinition
       OR actualItem.KeyOrdinal <> expectedItem.KeyOrdinal
       OR actualItem.IsDescending <> expectedItem.IsDescending
       OR actualItem.IsIncluded <> expectedItem.IsIncluded
)
    THROW 527519, 'RT02_SCHEMA_PROOF_REJECTED: OTO index metadata.', 1;

IF EXISTS
(
    SELECT tableItem.object_id
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    LEFT JOIN sys.dm_db_partition_stats AS partitionItem
        ON partitionItem.object_id = tableItem.object_id
       AND partitionItem.index_id IN (0, 1)
    WHERE tableItem.is_ms_shipped = 0
    GROUP BY tableItem.object_id
    HAVING COALESCE(SUM(partitionItem.row_count), 0) <> 0
)
    THROW 527520, 'RT02_SCHEMA_PROOF_REJECTED: OTO business rows are not zero.', 1;

;WITH SchemaMetadata AS
(
    SELECT
        CONVERT(nvarchar(max), CONCAT(N'T|', schemaItem.name, N'|', tableItem.name)) AS MetadataLine
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'C|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                RIGHT(CONCAT(N'0000', columnItem.column_id), 4),
                N'|',
                columnItem.name,
                N'|',
                CASE
                    WHEN typeItem.name IN (N'char', N'varchar', N'binary', N'varbinary')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'nchar', N'nvarchar')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length / 2)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'decimal', N'numeric')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            columnItem.precision,
                            N',',
                            columnItem.scale,
                            N')')
                    WHEN typeItem.name IN (N'datetime2', N'datetimeoffset', N'time')
                        THEN CONCAT(typeItem.name, N'(', columnItem.scale, N')')
                    ELSE typeItem.name
                END,
                N'|NULLABLE=',
                CONVERT(int, columnItem.is_nullable),
                N'|COLLATION=',
                COALESCE(columnItem.collation_name, N'-'),
                N'|IDENTITY=',
                CONVERT(int, columnItem.is_identity),
                N'|COMPUTED=',
                CONVERT(int, columnItem.is_computed),
                N'|SPARSE=',
                CONVERT(int, columnItem.is_sparse),
                N'|GENERATED=',
                CONVERT(int, columnItem.generated_always_type)
            )
        )
    FROM sys.columns AS columnItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = columnItem.object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.types AS typeItem
        ON typeItem.user_type_id = columnItem.user_type_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'D|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                columnItem.name,
                N'|',
                defaultItem.name,
                N'|',
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), defaultItem.definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                )
            )
        )
    FROM sys.default_constraints AS defaultItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = defaultItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = defaultItem.parent_object_id
       AND columnItem.column_id = defaultItem.parent_column_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'CK|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                checkItem.name,
                N'|',
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), checkItem.definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                ),
                N'|DISABLED=',
                CONVERT(int, checkItem.is_disabled),
                N'|UNTRUSTED=',
                CONVERT(int, checkItem.is_not_trusted)
            )
        )
    FROM sys.check_constraints AS checkItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = checkItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'K|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                keyItem.name,
                N'|',
                keyItem.type,
                N'|',
                indexItem.name
            )
        )
    FROM sys.key_constraints AS keyItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = keyItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.indexes AS indexItem
        ON indexItem.object_id = keyItem.parent_object_id
       AND indexItem.index_id = keyItem.unique_index_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'FK|',
                parentSchema.name,
                N'|',
                parentTable.name,
                N'|',
                foreignItem.name,
                N'|',
                referencedSchema.name,
                N'|',
                referencedTable.name,
                N'|',
                (
                    SELECT STRING_AGG(
                        CONVERT(
                            nvarchar(max),
                            CONCAT(
                                parentColumn.name,
                                N':',
                                referencedColumn.name,
                                N':',
                                foreignColumn.constraint_column_id
                            )
                        ),
                        N'|'
                    ) WITHIN GROUP (ORDER BY foreignColumn.constraint_column_id)
                    FROM sys.foreign_key_columns AS foreignColumn
                    INNER JOIN sys.columns AS parentColumn
                        ON parentColumn.object_id = foreignColumn.parent_object_id
                       AND parentColumn.column_id = foreignColumn.parent_column_id
                    INNER JOIN sys.columns AS referencedColumn
                        ON referencedColumn.object_id = foreignColumn.referenced_object_id
                       AND referencedColumn.column_id = foreignColumn.referenced_column_id
                    WHERE foreignColumn.constraint_object_id = foreignItem.object_id
                ),
                N'|UPDATE=',
                foreignItem.update_referential_action_desc,
                N'|DELETE=',
                foreignItem.delete_referential_action_desc,
                N'|DISABLED=',
                CONVERT(int, foreignItem.is_disabled),
                N'|UNTRUSTED=',
                CONVERT(int, foreignItem.is_not_trusted)
            )
        )
    FROM sys.foreign_keys AS foreignItem
    INNER JOIN sys.tables AS parentTable
        ON parentTable.object_id = foreignItem.parent_object_id
    INNER JOIN sys.schemas AS parentSchema
        ON parentSchema.schema_id = parentTable.schema_id
    INNER JOIN sys.tables AS referencedTable
        ON referencedTable.object_id = foreignItem.referenced_object_id
    INNER JOIN sys.schemas AS referencedSchema
        ON referencedSchema.schema_id = referencedTable.schema_id
    WHERE parentTable.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'I|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                indexItem.name,
                N'|',
                indexItem.type_desc,
                N'|UNIQUE=',
                CONVERT(int, indexItem.is_unique),
                N'|PK=',
                CONVERT(int, indexItem.is_primary_key),
                N'|UQ=',
                CONVERT(int, indexItem.is_unique_constraint),
                N'|FILTER=',
                CONVERT(int, indexItem.has_filter),
                N':',
                COALESCE(
                    LOWER(
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            CONVERT(nvarchar(max), indexItem.filter_definition),
                            N'[', N''), N']', N''), N'(', N''), N')', N''),
                            N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                    ),
                    N''
                ),
                N'|COLUMNS=',
                (
                    SELECT STRING_AGG(
                        CONVERT(
                            nvarchar(max),
                            CONCAT(
                                columnItem.name,
                                N':',
                                indexColumn.key_ordinal,
                                N':',
                                CONVERT(int, indexColumn.is_descending_key),
                                N':',
                                CONVERT(int, indexColumn.is_included_column)
                            )
                        ),
                        N'|'
                    ) WITHIN GROUP (ORDER BY indexColumn.index_column_id)
                    FROM sys.index_columns AS indexColumn
                    INNER JOIN sys.columns AS columnItem
                        ON columnItem.object_id = indexColumn.object_id
                       AND columnItem.column_id = indexColumn.column_id
                    WHERE indexColumn.object_id = indexItem.object_id
                      AND indexColumn.index_id = indexItem.index_id
                      AND
                      (
                          indexColumn.key_ordinal > 0
                          OR indexColumn.is_included_column = 1
                      )
                )
            )
        )
    FROM sys.indexes AS indexItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = indexItem.object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
      AND indexItem.index_id > 0
)
SELECT
    N'QLHV_RT02_OTO_TEST' AS DatabaseName,
    CONVERT(
        char(64),
        HASHBYTES(
            N'SHA2_256',
            STRING_AGG(
                MetadataLine COLLATE DATABASE_DEFAULT,
                NCHAR(10)
            ) WITHIN GROUP
            (
                ORDER BY MetadataLine COLLATE DATABASE_DEFAULT
            )
        ),
        2
    ) AS SchemaFingerprintSha256,
    (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) AS BusinessTableCount,
    (
        SELECT COALESCE(SUM(partitionItem.row_count), 0)
        FROM sys.tables AS tableItem
        LEFT JOIN sys.dm_db_partition_stats AS partitionItem
            ON partitionItem.object_id = tableItem.object_id
           AND partitionItem.index_id IN (0, 1)
        WHERE tableItem.is_ms_shipped = 0
    ) AS BusinessRowCount,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount
FROM SchemaMetadata;
GO

USE [QLHV_RT02_MOTO_TEST];
GO
SET NOCOUNT ON;

IF DB_NAME() <> N'QLHV_RT02_MOTO_TEST'
   OR DB_ID() <> 6
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A')
    THROW 527521, 'RT02_SCHEMA_PROOF_REJECTED: MOTO context identity.', 1;

IF EXISTS
(
    SELECT expectedItem.PropertyName, expectedItem.PropertyValue
    FROM
    (
        VALUES
            (N'RT02_ISOLATED_ENVIRONMENT_ID', N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'),
            (N'RT02_OWNER_APPROVAL_ID', N'RT02B-OPERATOR-APPROVAL-20260727-01'),
            (N'RT02_DATASET_MODE', N'SYNTHETIC'),
            (N'RT02_PRODUCTION_ROUTE_ALLOWED', N'FALSE'),
            (N'RT02_EXPIRES_AT_UTC', N'2026-07-31T16:59:59Z')
    ) AS expectedItem(PropertyName, PropertyValue)
    EXCEPT
    SELECT
        CONVERT(nvarchar(128), propertyItem.name),
        CONVERT(nvarchar(128), propertyItem.value)
    FROM sys.extended_properties AS propertyItem
    WHERE propertyItem.class = 0
)
    THROW 527522, 'RT02_SCHEMA_PROOF_REJECTED: MOTO TEST markers.', 1;

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527523, 'RT02_SCHEMA_PROOF_REJECTED: MOTO feature/route state.', 1;

IF EXISTS
(
    SELECT expectedItem.SchemaName, expectedItem.TableName
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX'),
            (N'dbo', N'NguoiLX_HoSo')
    ) AS expectedItem(SchemaName, TableName)
    EXCEPT
    SELECT schemaItem.name, tableItem.name
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
)
   OR EXISTS
(
    SELECT schemaItem.name, tableItem.name
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
    EXCEPT
    SELECT expectedItem.SchemaName, expectedItem.TableName
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX'),
            (N'dbo', N'NguoiLX_HoSo')
    ) AS expectedItem(SchemaName, TableName)
)
    THROW 527524, 'RT02_SCHEMA_PROOF_REJECTED: MOTO table allowlist.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', 1, N'IdentityHmac', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 2, N'ScenarioCode', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 3, N'DatasetRole', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 4, N'HoTen', N'nvarchar(200)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 5, N'SourceRowHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 6, N'IsActive', N'bit', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX', 7, N'CreatedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX_HoSo', 1, N'IdentityHmac', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'NguoiLX_HoSo', 2, N'PayloadHash', N'char(64)', 0, 0, 0, 0, 0)
    ) AS expectedItem
    (
        SchemaName, TableName, ColumnId, ColumnName, TypeSpec, IsNullable,
        IsIdentity, IsComputed, IsSparse, GeneratedAlwaysType
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeSpecItem.TypeSpec,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity,
            CONVERT(int, columnItem.is_computed) AS IsComputed,
            CONVERT(int, columnItem.is_sparse) AS IsSparse,
            CONVERT(int, columnItem.generated_always_type) AS GeneratedAlwaysType
        FROM sys.columns AS columnItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = columnItem.object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        CROSS APPLY
        (
            VALUES
            (
                CASE
                    WHEN typeItem.name IN (N'char', N'varchar', N'binary', N'varbinary')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'nchar', N'nvarchar')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length / 2)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'decimal', N'numeric')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            columnItem.precision,
                            N',',
                            columnItem.scale,
                            N')')
                    WHEN typeItem.name IN (N'datetime2', N'datetimeoffset', N'time')
                        THEN CONCAT(typeItem.name, N'(', columnItem.scale, N')')
                    ELSE typeItem.name
                END
            )
        ) AS typeSpecItem(TypeSpec)
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.ColumnName IS NULL
       OR actualItem.ColumnName IS NULL
       OR actualItem.ColumnId <> expectedItem.ColumnId
       OR actualItem.TypeSpec <> expectedItem.TypeSpec
       OR actualItem.IsNullable <> expectedItem.IsNullable
       OR actualItem.IsIdentity <> expectedItem.IsIdentity
       OR actualItem.IsComputed <> expectedItem.IsComputed
       OR actualItem.IsSparse <> expectedItem.IsSparse
       OR actualItem.GeneratedAlwaysType <> expectedItem.GeneratedAlwaysType
)
    THROW 527525, 'RT02_SCHEMA_PROOF_REJECTED: MOTO column metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', N'IsActive', N'DF_RT02_MOTO_NguoiLX_IsActive', N'1'),
            (N'dbo', N'NguoiLX', N'CreatedAtUtc', N'DF_RT02_MOTO_NguoiLX_CreatedAtUtc', N'sysutcdatetime')
    ) AS expectedItem(SchemaName, TableName, ColumnName, ConstraintName, Definition)
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            columnItem.name AS ColumnName,
            defaultItem.name AS ConstraintName,
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    CONVERT(nvarchar(max), defaultItem.definition),
                    N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
            ) AS Definition
        FROM sys.default_constraints AS defaultItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = defaultItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = defaultItem.parent_object_id
           AND columnItem.column_id = defaultItem.parent_column_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.ColumnName IS NULL
       OR actualItem.ColumnName IS NULL
       OR actualItem.ConstraintName <> expectedItem.ConstraintName
       OR actualItem.Definition <> expectedItem.Definition
)
    THROW 527526, 'RT02_SCHEMA_PROOF_REJECTED: MOTO default metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints AS checkItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = checkItem.parent_object_id
    WHERE tableItem.is_ms_shipped = 0
)
    THROW 527527, 'RT02_SCHEMA_PROOF_REJECTED: unexpected MOTO check constraint.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', N'PK_RT02_MOTO_NguoiLX', N'PK', N'PK_RT02_MOTO_NguoiLX'),
            (N'dbo', N'NguoiLX_HoSo', N'PK_RT02_MOTO_NguoiLX_HoSo', N'PK', N'PK_RT02_MOTO_NguoiLX_HoSo')
    ) AS expectedItem(SchemaName, TableName, ConstraintName, ConstraintType, IndexName)
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            keyItem.name AS ConstraintName,
            keyItem.type AS ConstraintType,
            indexItem.name AS IndexName
        FROM sys.key_constraints AS keyItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = keyItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.indexes AS indexItem
            ON indexItem.object_id = keyItem.parent_object_id
           AND indexItem.index_id = keyItem.unique_index_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ConstraintName = expectedItem.ConstraintName
    WHERE expectedItem.ConstraintName IS NULL
       OR actualItem.ConstraintName IS NULL
       OR actualItem.ConstraintType <> expectedItem.ConstraintType
       OR actualItem.IndexName <> expectedItem.IndexName
)
    THROW 527528, 'RT02_SCHEMA_PROOF_REJECTED: MOTO key constraint metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
        (
            N'dbo',
            N'NguoiLX_HoSo',
            N'FK_RT02_MOTO_NguoiLX_HoSo_NguoiLX',
            N'dbo',
            N'NguoiLX',
            N'IdentityHmac:IdentityHmac:1',
            N'NO_ACTION',
            N'NO_ACTION',
            0,
            0
        )
    ) AS expectedItem
    (
        ParentSchema, ParentTable, ConstraintName, ReferencedSchema,
        ReferencedTable, ColumnMap, UpdateAction, DeleteAction,
        IsDisabled, IsNotTrusted
    )
    FULL OUTER JOIN
    (
        SELECT
            parentSchema.name AS ParentSchema,
            parentTable.name AS ParentTable,
            foreignItem.name AS ConstraintName,
            referencedSchema.name AS ReferencedSchema,
            referencedTable.name AS ReferencedTable,
            (
                SELECT STRING_AGG(
                    CONVERT(
                        nvarchar(max),
                        CONCAT(
                            parentColumn.name,
                            N':',
                            referencedColumn.name,
                            N':',
                            foreignColumn.constraint_column_id
                        )
                    ),
                    N'|'
                ) WITHIN GROUP (ORDER BY foreignColumn.constraint_column_id)
                FROM sys.foreign_key_columns AS foreignColumn
                INNER JOIN sys.columns AS parentColumn
                    ON parentColumn.object_id = foreignColumn.parent_object_id
                   AND parentColumn.column_id = foreignColumn.parent_column_id
                INNER JOIN sys.columns AS referencedColumn
                    ON referencedColumn.object_id = foreignColumn.referenced_object_id
                   AND referencedColumn.column_id = foreignColumn.referenced_column_id
                WHERE foreignColumn.constraint_object_id = foreignItem.object_id
            ) AS ColumnMap,
            foreignItem.update_referential_action_desc AS UpdateAction,
            foreignItem.delete_referential_action_desc AS DeleteAction,
            CONVERT(int, foreignItem.is_disabled) AS IsDisabled,
            CONVERT(int, foreignItem.is_not_trusted) AS IsNotTrusted
        FROM sys.foreign_keys AS foreignItem
        INNER JOIN sys.tables AS parentTable
            ON parentTable.object_id = foreignItem.parent_object_id
        INNER JOIN sys.schemas AS parentSchema
            ON parentSchema.schema_id = parentTable.schema_id
        INNER JOIN sys.tables AS referencedTable
            ON referencedTable.object_id = foreignItem.referenced_object_id
        INNER JOIN sys.schemas AS referencedSchema
            ON referencedSchema.schema_id = referencedTable.schema_id
        WHERE parentTable.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.ParentSchema = expectedItem.ParentSchema
       AND actualItem.ParentTable = expectedItem.ParentTable
       AND actualItem.ConstraintName = expectedItem.ConstraintName
    WHERE expectedItem.ConstraintName IS NULL
       OR actualItem.ConstraintName IS NULL
       OR actualItem.ReferencedSchema <> expectedItem.ReferencedSchema
       OR actualItem.ReferencedTable <> expectedItem.ReferencedTable
       OR actualItem.ColumnMap <> expectedItem.ColumnMap
       OR actualItem.UpdateAction <> expectedItem.UpdateAction
       OR actualItem.DeleteAction <> expectedItem.DeleteAction
       OR actualItem.IsDisabled <> expectedItem.IsDisabled
       OR actualItem.IsNotTrusted <> expectedItem.IsNotTrusted
)
    THROW 527529, 'RT02_SCHEMA_PROOF_REJECTED: MOTO foreign key metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'NguoiLX', N'PK_RT02_MOTO_NguoiLX', N'CLUSTERED', 1, 1, 0, 0, N'', N'IdentityHmac:1:0:0'),
            (N'dbo', N'NguoiLX_HoSo', N'PK_RT02_MOTO_NguoiLX_HoSo', N'CLUSTERED', 1, 1, 0, 0, N'', N'IdentityHmac:1:0:0')
    ) AS expectedItem
    (
        SchemaName, TableName, IndexName, TypeDescription, IsUnique,
        IsPrimaryKey, IsUniqueConstraint, HasFilter, FilterDefinition, ColumnMap
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            indexItem.name AS IndexName,
            indexItem.type_desc AS TypeDescription,
            CONVERT(int, indexItem.is_unique) AS IsUnique,
            CONVERT(int, indexItem.is_primary_key) AS IsPrimaryKey,
            CONVERT(int, indexItem.is_unique_constraint) AS IsUniqueConstraint,
            CONVERT(int, indexItem.has_filter) AS HasFilter,
            COALESCE(
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), indexItem.filter_definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                ),
                N''
            ) AS FilterDefinition,
            (
                SELECT STRING_AGG(
                    CONVERT(
                        nvarchar(max),
                        CONCAT(
                            columnItem.name,
                            N':',
                            indexColumn.key_ordinal,
                            N':',
                            CONVERT(int, indexColumn.is_descending_key),
                            N':',
                            CONVERT(int, indexColumn.is_included_column)
                        )
                    ),
                    N'|'
                ) WITHIN GROUP (ORDER BY indexColumn.index_column_id)
                FROM sys.index_columns AS indexColumn
                INNER JOIN sys.columns AS columnItem
                    ON columnItem.object_id = indexColumn.object_id
                   AND columnItem.column_id = indexColumn.column_id
                WHERE indexColumn.object_id = indexItem.object_id
                  AND indexColumn.index_id = indexItem.index_id
                  AND
                  (
                      indexColumn.key_ordinal > 0
                      OR indexColumn.is_included_column = 1
                  )
            ) AS ColumnMap
        FROM sys.indexes AS indexItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = indexItem.object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        WHERE tableItem.is_ms_shipped = 0
          AND indexItem.index_id > 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.IndexName = expectedItem.IndexName
    WHERE expectedItem.IndexName IS NULL
       OR actualItem.IndexName IS NULL
       OR actualItem.TypeDescription <> expectedItem.TypeDescription
       OR actualItem.IsUnique <> expectedItem.IsUnique
       OR actualItem.IsPrimaryKey <> expectedItem.IsPrimaryKey
       OR actualItem.IsUniqueConstraint <> expectedItem.IsUniqueConstraint
       OR actualItem.HasFilter <> expectedItem.HasFilter
       OR actualItem.FilterDefinition <> expectedItem.FilterDefinition
       OR actualItem.ColumnMap <> expectedItem.ColumnMap
)
    THROW 527530, 'RT02_SCHEMA_PROOF_REJECTED: MOTO index metadata.', 1;

IF EXISTS
(
    SELECT tableItem.object_id
    FROM sys.tables AS tableItem
    LEFT JOIN sys.dm_db_partition_stats AS partitionItem
        ON partitionItem.object_id = tableItem.object_id
       AND partitionItem.index_id IN (0, 1)
    WHERE tableItem.is_ms_shipped = 0
    GROUP BY tableItem.object_id
    HAVING COALESCE(SUM(partitionItem.row_count), 0) <> 0
)
    THROW 527531, 'RT02_SCHEMA_PROOF_REJECTED: MOTO business rows are not zero.', 1;

;WITH SchemaMetadata AS
(
    SELECT
        CONVERT(nvarchar(max), CONCAT(N'T|', schemaItem.name, N'|', tableItem.name)) AS MetadataLine
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'C|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                RIGHT(CONCAT(N'0000', columnItem.column_id), 4),
                N'|',
                columnItem.name,
                N'|',
                CASE
                    WHEN typeItem.name IN (N'char', N'varchar', N'binary', N'varbinary')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'nchar', N'nvarchar')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length / 2)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'decimal', N'numeric')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            columnItem.precision,
                            N',',
                            columnItem.scale,
                            N')')
                    WHEN typeItem.name IN (N'datetime2', N'datetimeoffset', N'time')
                        THEN CONCAT(typeItem.name, N'(', columnItem.scale, N')')
                    ELSE typeItem.name
                END,
                N'|NULLABLE=',
                CONVERT(int, columnItem.is_nullable),
                N'|COLLATION=',
                COALESCE(columnItem.collation_name, N'-'),
                N'|IDENTITY=',
                CONVERT(int, columnItem.is_identity),
                N'|COMPUTED=',
                CONVERT(int, columnItem.is_computed),
                N'|SPARSE=',
                CONVERT(int, columnItem.is_sparse),
                N'|GENERATED=',
                CONVERT(int, columnItem.generated_always_type)
            )
        )
    FROM sys.columns AS columnItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = columnItem.object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.types AS typeItem
        ON typeItem.user_type_id = columnItem.user_type_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'D|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                columnItem.name,
                N'|',
                defaultItem.name,
                N'|',
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), defaultItem.definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                )
            )
        )
    FROM sys.default_constraints AS defaultItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = defaultItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = defaultItem.parent_object_id
       AND columnItem.column_id = defaultItem.parent_column_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'CK|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                checkItem.name,
                N'|',
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), checkItem.definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                ),
                N'|DISABLED=',
                CONVERT(int, checkItem.is_disabled),
                N'|UNTRUSTED=',
                CONVERT(int, checkItem.is_not_trusted)
            )
        )
    FROM sys.check_constraints AS checkItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = checkItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'K|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                keyItem.name,
                N'|',
                keyItem.type,
                N'|',
                indexItem.name
            )
        )
    FROM sys.key_constraints AS keyItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = keyItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.indexes AS indexItem
        ON indexItem.object_id = keyItem.parent_object_id
       AND indexItem.index_id = keyItem.unique_index_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'FK|',
                parentSchema.name,
                N'|',
                parentTable.name,
                N'|',
                foreignItem.name,
                N'|',
                referencedSchema.name,
                N'|',
                referencedTable.name,
                N'|',
                (
                    SELECT STRING_AGG(
                        CONVERT(
                            nvarchar(max),
                            CONCAT(
                                parentColumn.name,
                                N':',
                                referencedColumn.name,
                                N':',
                                foreignColumn.constraint_column_id
                            )
                        ),
                        N'|'
                    ) WITHIN GROUP (ORDER BY foreignColumn.constraint_column_id)
                    FROM sys.foreign_key_columns AS foreignColumn
                    INNER JOIN sys.columns AS parentColumn
                        ON parentColumn.object_id = foreignColumn.parent_object_id
                       AND parentColumn.column_id = foreignColumn.parent_column_id
                    INNER JOIN sys.columns AS referencedColumn
                        ON referencedColumn.object_id = foreignColumn.referenced_object_id
                       AND referencedColumn.column_id = foreignColumn.referenced_column_id
                    WHERE foreignColumn.constraint_object_id = foreignItem.object_id
                ),
                N'|UPDATE=',
                foreignItem.update_referential_action_desc,
                N'|DELETE=',
                foreignItem.delete_referential_action_desc,
                N'|DISABLED=',
                CONVERT(int, foreignItem.is_disabled),
                N'|UNTRUSTED=',
                CONVERT(int, foreignItem.is_not_trusted)
            )
        )
    FROM sys.foreign_keys AS foreignItem
    INNER JOIN sys.tables AS parentTable
        ON parentTable.object_id = foreignItem.parent_object_id
    INNER JOIN sys.schemas AS parentSchema
        ON parentSchema.schema_id = parentTable.schema_id
    INNER JOIN sys.tables AS referencedTable
        ON referencedTable.object_id = foreignItem.referenced_object_id
    INNER JOIN sys.schemas AS referencedSchema
        ON referencedSchema.schema_id = referencedTable.schema_id
    WHERE parentTable.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'I|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                indexItem.name,
                N'|',
                indexItem.type_desc,
                N'|UNIQUE=',
                CONVERT(int, indexItem.is_unique),
                N'|PK=',
                CONVERT(int, indexItem.is_primary_key),
                N'|UQ=',
                CONVERT(int, indexItem.is_unique_constraint),
                N'|FILTER=',
                CONVERT(int, indexItem.has_filter),
                N':',
                COALESCE(
                    LOWER(
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            CONVERT(nvarchar(max), indexItem.filter_definition),
                            N'[', N''), N']', N''), N'(', N''), N')', N''),
                            N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                    ),
                    N''
                ),
                N'|COLUMNS=',
                (
                    SELECT STRING_AGG(
                        CONVERT(
                            nvarchar(max),
                            CONCAT(
                                columnItem.name,
                                N':',
                                indexColumn.key_ordinal,
                                N':',
                                CONVERT(int, indexColumn.is_descending_key),
                                N':',
                                CONVERT(int, indexColumn.is_included_column)
                            )
                        ),
                        N'|'
                    ) WITHIN GROUP (ORDER BY indexColumn.index_column_id)
                    FROM sys.index_columns AS indexColumn
                    INNER JOIN sys.columns AS columnItem
                        ON columnItem.object_id = indexColumn.object_id
                       AND columnItem.column_id = indexColumn.column_id
                    WHERE indexColumn.object_id = indexItem.object_id
                      AND indexColumn.index_id = indexItem.index_id
                      AND
                      (
                          indexColumn.key_ordinal > 0
                          OR indexColumn.is_included_column = 1
                      )
                )
            )
        )
    FROM sys.indexes AS indexItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = indexItem.object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
      AND indexItem.index_id > 0
)
SELECT
    N'QLHV_RT02_MOTO_TEST' AS DatabaseName,
    CONVERT(
        char(64),
        HASHBYTES(
            N'SHA2_256',
            STRING_AGG(
                MetadataLine COLLATE DATABASE_DEFAULT,
                NCHAR(10)
            ) WITHIN GROUP
            (
                ORDER BY MetadataLine COLLATE DATABASE_DEFAULT
            )
        ),
        2
    ) AS SchemaFingerprintSha256,
    (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) AS BusinessTableCount,
    (
        SELECT COALESCE(SUM(partitionItem.row_count), 0)
        FROM sys.tables AS tableItem
        LEFT JOIN sys.dm_db_partition_stats AS partitionItem
            ON partitionItem.object_id = tableItem.object_id
           AND partitionItem.index_id IN (0, 1)
        WHERE tableItem.is_ms_shipped = 0
    ) AS BusinessRowCount,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount
FROM SchemaMetadata;
GO

USE [QLHV_RT02_TARGET_TEST];
GO
SET NOCOUNT ON;

IF DB_NAME() <> N'QLHV_RT02_TARGET_TEST'
   OR DB_ID() <> 7
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
    THROW 527532, 'RT02_SCHEMA_PROOF_REJECTED: target context identity.', 1;

IF EXISTS
(
    SELECT expectedItem.PropertyName, expectedItem.PropertyValue
    FROM
    (
        VALUES
            (N'RT02_ISOLATED_ENVIRONMENT_ID', N'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'),
            (N'RT02_OWNER_APPROVAL_ID', N'RT02B-OPERATOR-APPROVAL-20260727-01'),
            (N'RT02_DATASET_MODE', N'SYNTHETIC'),
            (N'RT02_PRODUCTION_ROUTE_ALLOWED', N'FALSE'),
            (N'RT02_EXPIRES_AT_UTC', N'2026-07-31T16:59:59Z')
    ) AS expectedItem(PropertyName, PropertyValue)
    EXCEPT
    SELECT
        CONVERT(nvarchar(128), propertyItem.name),
        CONVERT(nvarchar(128), propertyItem.value)
    FROM sys.extended_properties AS propertyItem
    WHERE propertyItem.class = 0
)
    THROW 527533, 'RT02_SCHEMA_PROOF_REJECTED: target TEST markers.', 1;

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR EXISTS (SELECT 1 FROM sys.synonyms)
    THROW 527534, 'RT02_SCHEMA_PROOF_REJECTED: target feature/route state.', 1;

IF EXISTS
(
    SELECT expectedItem.SchemaName, expectedItem.TableName
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner'),
            (N'dbo', N'Rt02ManualReviewEvidence'),
            (N'dbo', N'Rt02ApplyMarker'),
            (N'dbo', N'Rt02ApplyCheckpoint'),
            (N'dbo', N'Rt02EnvironmentState')
    ) AS expectedItem(SchemaName, TableName)
    EXCEPT
    SELECT schemaItem.name, tableItem.name
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
)
   OR EXISTS
(
    SELECT schemaItem.name, tableItem.name
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
    EXCEPT
    SELECT expectedItem.SchemaName, expectedItem.TableName
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner'),
            (N'dbo', N'Rt02ManualReviewEvidence'),
            (N'dbo', N'Rt02ApplyMarker'),
            (N'dbo', N'Rt02ApplyCheckpoint'),
            (N'dbo', N'Rt02EnvironmentState')
    ) AS expectedItem(SchemaName, TableName)
)
    THROW 527535, 'RT02_SCHEMA_PROOF_REJECTED: target table allowlist.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner', 1, N'IdentityHmac', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 2, N'SourceProfile', N'varchar(20)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 3, N'ScenarioCode', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 4, N'DatasetRole', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 5, N'HoTen', N'nvarchar(200)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 6, N'MappedHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 7, N'QlhvOwnedHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 8, N'WorkflowState', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 9, N'NotesHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 10, N'PhotoState', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 11, N'Active', N'bit', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 12, N'SoftDeleted', N'bit', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 13, N'CreatedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02Learner', 14, N'UpdatedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 1, N'CycleId', N'varchar(120)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 2, N'OperationId', N'varchar(160)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 3, N'IdentityHmac', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 4, N'Disposition', N'varchar(60)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 5, N'DispositionHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 6, N'TargetRetainedActive', N'bit', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 7, N'TargetMutated', N'bit', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', 8, N'CreatedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 1, N'CycleId', N'varchar(120)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 2, N'PlanHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 3, N'DispositionHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 4, N'InsertedRows', N'int', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 5, N'UpdatedRows', N'int', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 6, N'RetainedRows', N'int', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 7, N'PreservedQlhvOwnedHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyMarker', 8, N'CommittedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 1, N'SourceProfile', N'varchar(20)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 2, N'Mode', N'varchar(40)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 3, N'MappingFingerprint', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 4, N'EnvironmentId', N'varchar(128)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 5, N'CycleId', N'varchar(120)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 6, N'PlanHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 7, N'MarkerHash', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 8, N'SourceWatermark', N'bigint', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02ApplyCheckpoint', 9, N'PublishedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 1, N'EnvironmentId', N'varchar(128)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 2, N'DatasetFingerprint', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 3, N'MappingFingerprint', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 4, N'SourceSchemaFingerprint', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 5, N'TargetSchemaFingerprint', N'char(64)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 6, N'IdentityNormalizationVersion', N'varchar(60)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 7, N'DatasetMode', N'varchar(20)', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 8, N'PiiRows', N'int', 0, 0, 0, 0, 0),
            (N'dbo', N'Rt02EnvironmentState', 9, N'CreatedAtUtc', N'datetime2(7)', 0, 0, 0, 0, 0)
    ) AS expectedItem
    (
        SchemaName, TableName, ColumnId, ColumnName, TypeSpec, IsNullable,
        IsIdentity, IsComputed, IsSparse, GeneratedAlwaysType
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            columnItem.column_id AS ColumnId,
            columnItem.name AS ColumnName,
            typeSpecItem.TypeSpec,
            CONVERT(int, columnItem.is_nullable) AS IsNullable,
            CONVERT(int, columnItem.is_identity) AS IsIdentity,
            CONVERT(int, columnItem.is_computed) AS IsComputed,
            CONVERT(int, columnItem.is_sparse) AS IsSparse,
            CONVERT(int, columnItem.generated_always_type) AS GeneratedAlwaysType
        FROM sys.columns AS columnItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = columnItem.object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.types AS typeItem
            ON typeItem.user_type_id = columnItem.user_type_id
        CROSS APPLY
        (
            VALUES
            (
                CASE
                    WHEN typeItem.name IN (N'char', N'varchar', N'binary', N'varbinary')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'nchar', N'nvarchar')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length / 2)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'decimal', N'numeric')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            columnItem.precision,
                            N',',
                            columnItem.scale,
                            N')')
                    WHEN typeItem.name IN (N'datetime2', N'datetimeoffset', N'time')
                        THEN CONCAT(typeItem.name, N'(', columnItem.scale, N')')
                    ELSE typeItem.name
                END
            )
        ) AS typeSpecItem(TypeSpec)
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.ColumnName IS NULL
       OR actualItem.ColumnName IS NULL
       OR actualItem.ColumnId <> expectedItem.ColumnId
       OR actualItem.TypeSpec <> expectedItem.TypeSpec
       OR actualItem.IsNullable <> expectedItem.IsNullable
       OR actualItem.IsIdentity <> expectedItem.IsIdentity
       OR actualItem.IsComputed <> expectedItem.IsComputed
       OR actualItem.IsSparse <> expectedItem.IsSparse
       OR actualItem.GeneratedAlwaysType <> expectedItem.GeneratedAlwaysType
)
    THROW 527536, 'RT02_SCHEMA_PROOF_REJECTED: target column metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner', N'CreatedAtUtc', N'DF_Rt02Learner_CreatedAtUtc', N'sysutcdatetime'),
            (N'dbo', N'Rt02Learner', N'UpdatedAtUtc', N'DF_Rt02Learner_UpdatedAtUtc', N'sysutcdatetime'),
            (N'dbo', N'Rt02ManualReviewEvidence', N'CreatedAtUtc', N'DF_Rt02ManualReviewEvidence_CreatedAtUtc', N'sysutcdatetime')
    ) AS expectedItem(SchemaName, TableName, ColumnName, ConstraintName, Definition)
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            columnItem.name AS ColumnName,
            defaultItem.name AS ConstraintName,
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    CONVERT(nvarchar(max), defaultItem.definition),
                    N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
            ) AS Definition
        FROM sys.default_constraints AS defaultItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = defaultItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.columns AS columnItem
            ON columnItem.object_id = defaultItem.parent_object_id
           AND columnItem.column_id = defaultItem.parent_column_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ColumnName = expectedItem.ColumnName
    WHERE expectedItem.ColumnName IS NULL
       OR actualItem.ColumnName IS NULL
       OR actualItem.ConstraintName <> expectedItem.ConstraintName
       OR actualItem.Definition <> expectedItem.Definition
)
    THROW 527537, 'RT02_SCHEMA_PROOF_REJECTED: target default metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner', N'CK_Rt02Learner_DeleteState', N'active=1andsoftdeleted=0oractive=0andsoftdeleted=1', 0, 0),
            (N'dbo', N'Rt02ManualReviewEvidence', N'CK_Rt02ManualReviewEvidence_NoMutation', N'targetretainedactive=1andtargetmutated=0', 0, 0),
            (N'dbo', N'Rt02EnvironmentState', N'CK_Rt02EnvironmentState_Synthetic', N'datasetmode=''synthetic''andpiirows=0', 0, 0)
    ) AS expectedItem
    (
        SchemaName, TableName, ConstraintName, Definition, IsDisabled, IsNotTrusted
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            checkItem.name AS ConstraintName,
            LOWER(
                REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    CONVERT(nvarchar(max), checkItem.definition),
                    N'[', N''), N']', N''), N'(', N''), N')', N''),
                    N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
            ) AS Definition,
            CONVERT(int, checkItem.is_disabled) AS IsDisabled,
            CONVERT(int, checkItem.is_not_trusted) AS IsNotTrusted
        FROM sys.check_constraints AS checkItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = checkItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ConstraintName = expectedItem.ConstraintName
    WHERE expectedItem.ConstraintName IS NULL
       OR actualItem.ConstraintName IS NULL
       OR actualItem.Definition <> expectedItem.Definition
       OR actualItem.IsDisabled <> expectedItem.IsDisabled
       OR actualItem.IsNotTrusted <> expectedItem.IsNotTrusted
)
    THROW 527538, 'RT02_SCHEMA_PROOF_REJECTED: target check constraint metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner', N'PK_Rt02Learner', N'PK', N'PK_Rt02Learner'),
            (N'dbo', N'Rt02ManualReviewEvidence', N'PK_Rt02ManualReviewEvidence', N'PK', N'PK_Rt02ManualReviewEvidence'),
            (N'dbo', N'Rt02ApplyMarker', N'PK_Rt02ApplyMarker', N'PK', N'PK_Rt02ApplyMarker'),
            (N'dbo', N'Rt02ApplyCheckpoint', N'PK_Rt02ApplyCheckpoint', N'PK', N'PK_Rt02ApplyCheckpoint'),
            (N'dbo', N'Rt02EnvironmentState', N'PK_Rt02EnvironmentState', N'PK', N'PK_Rt02EnvironmentState')
    ) AS expectedItem(SchemaName, TableName, ConstraintName, ConstraintType, IndexName)
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            keyItem.name AS ConstraintName,
            keyItem.type AS ConstraintType,
            indexItem.name AS IndexName
        FROM sys.key_constraints AS keyItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = keyItem.parent_object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        INNER JOIN sys.indexes AS indexItem
            ON indexItem.object_id = keyItem.parent_object_id
           AND indexItem.index_id = keyItem.unique_index_id
        WHERE tableItem.is_ms_shipped = 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.ConstraintName = expectedItem.ConstraintName
    WHERE expectedItem.ConstraintName IS NULL
       OR actualItem.ConstraintName IS NULL
       OR actualItem.ConstraintType <> expectedItem.ConstraintType
       OR actualItem.IndexName <> expectedItem.IndexName
)
    THROW 527539, 'RT02_SCHEMA_PROOF_REJECTED: target key constraint metadata.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys AS foreignItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = foreignItem.parent_object_id
    WHERE tableItem.is_ms_shipped = 0
)
    THROW 527540, 'RT02_SCHEMA_PROOF_REJECTED: unexpected target foreign key.', 1;

IF EXISTS
(
    SELECT 1
    FROM
    (
        VALUES
            (N'dbo', N'Rt02Learner', N'PK_Rt02Learner', N'CLUSTERED', 1, 1, 0, 0, N'', N'IdentityHmac:1:0:0'),
            (N'dbo', N'Rt02Learner', N'UX_Rt02Learner_ActiveIdentity', N'NONCLUSTERED', 1, 0, 0, 1, N'active=1andsoftdeleted=0', N'SourceProfile:1:0:0|IdentityHmac:2:0:0'),
            (N'dbo', N'Rt02ManualReviewEvidence', N'PK_Rt02ManualReviewEvidence', N'CLUSTERED', 1, 1, 0, 0, N'', N'CycleId:1:0:0|OperationId:2:0:0'),
            (N'dbo', N'Rt02ApplyMarker', N'PK_Rt02ApplyMarker', N'CLUSTERED', 1, 1, 0, 0, N'', N'CycleId:1:0:0'),
            (N'dbo', N'Rt02ApplyCheckpoint', N'PK_Rt02ApplyCheckpoint', N'CLUSTERED', 1, 1, 0, 0, N'', N'SourceProfile:1:0:0|Mode:2:0:0|MappingFingerprint:3:0:0|EnvironmentId:4:0:0'),
            (N'dbo', N'Rt02EnvironmentState', N'PK_Rt02EnvironmentState', N'CLUSTERED', 1, 1, 0, 0, N'', N'EnvironmentId:1:0:0')
    ) AS expectedItem
    (
        SchemaName, TableName, IndexName, TypeDescription, IsUnique,
        IsPrimaryKey, IsUniqueConstraint, HasFilter, FilterDefinition, ColumnMap
    )
    FULL OUTER JOIN
    (
        SELECT
            schemaItem.name AS SchemaName,
            tableItem.name AS TableName,
            indexItem.name AS IndexName,
            indexItem.type_desc AS TypeDescription,
            CONVERT(int, indexItem.is_unique) AS IsUnique,
            CONVERT(int, indexItem.is_primary_key) AS IsPrimaryKey,
            CONVERT(int, indexItem.is_unique_constraint) AS IsUniqueConstraint,
            CONVERT(int, indexItem.has_filter) AS HasFilter,
            COALESCE(
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), indexItem.filter_definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                ),
                N''
            ) AS FilterDefinition,
            (
                SELECT STRING_AGG(
                    CONVERT(
                        nvarchar(max),
                        CONCAT(
                            columnItem.name,
                            N':',
                            indexColumn.key_ordinal,
                            N':',
                            CONVERT(int, indexColumn.is_descending_key),
                            N':',
                            CONVERT(int, indexColumn.is_included_column)
                        )
                    ),
                    N'|'
                ) WITHIN GROUP (ORDER BY indexColumn.index_column_id)
                FROM sys.index_columns AS indexColumn
                INNER JOIN sys.columns AS columnItem
                    ON columnItem.object_id = indexColumn.object_id
                   AND columnItem.column_id = indexColumn.column_id
                WHERE indexColumn.object_id = indexItem.object_id
                  AND indexColumn.index_id = indexItem.index_id
                  AND
                  (
                      indexColumn.key_ordinal > 0
                      OR indexColumn.is_included_column = 1
                  )
            ) AS ColumnMap
        FROM sys.indexes AS indexItem
        INNER JOIN sys.tables AS tableItem
            ON tableItem.object_id = indexItem.object_id
        INNER JOIN sys.schemas AS schemaItem
            ON schemaItem.schema_id = tableItem.schema_id
        WHERE tableItem.is_ms_shipped = 0
          AND indexItem.index_id > 0
    ) AS actualItem
        ON actualItem.SchemaName = expectedItem.SchemaName
       AND actualItem.TableName = expectedItem.TableName
       AND actualItem.IndexName = expectedItem.IndexName
    WHERE expectedItem.IndexName IS NULL
       OR actualItem.IndexName IS NULL
       OR actualItem.TypeDescription <> expectedItem.TypeDescription
       OR actualItem.IsUnique <> expectedItem.IsUnique
       OR actualItem.IsPrimaryKey <> expectedItem.IsPrimaryKey
       OR actualItem.IsUniqueConstraint <> expectedItem.IsUniqueConstraint
       OR actualItem.HasFilter <> expectedItem.HasFilter
       OR actualItem.FilterDefinition <> expectedItem.FilterDefinition
       OR actualItem.ColumnMap <> expectedItem.ColumnMap
)
    THROW 527541, 'RT02_SCHEMA_PROOF_REJECTED: target index metadata.', 1;

IF EXISTS
(
    SELECT tableItem.object_id
    FROM sys.tables AS tableItem
    LEFT JOIN sys.dm_db_partition_stats AS partitionItem
        ON partitionItem.object_id = tableItem.object_id
       AND partitionItem.index_id IN (0, 1)
    WHERE tableItem.is_ms_shipped = 0
    GROUP BY tableItem.object_id
    HAVING COALESCE(SUM(partitionItem.row_count), 0) <> 0
)
    THROW 527542, 'RT02_SCHEMA_PROOF_REJECTED: target business rows are not zero.', 1;

;WITH SchemaMetadata AS
(
    SELECT
        CONVERT(nvarchar(max), CONCAT(N'T|', schemaItem.name, N'|', tableItem.name)) AS MetadataLine
    FROM sys.tables AS tableItem
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'C|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                RIGHT(CONCAT(N'0000', columnItem.column_id), 4),
                N'|',
                columnItem.name,
                N'|',
                CASE
                    WHEN typeItem.name IN (N'char', N'varchar', N'binary', N'varbinary')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'nchar', N'nvarchar')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            CASE
                                WHEN columnItem.max_length = -1 THEN N'max'
                                ELSE CONVERT(nvarchar(10), columnItem.max_length / 2)
                            END,
                            N')')
                    WHEN typeItem.name IN (N'decimal', N'numeric')
                        THEN CONCAT(
                            typeItem.name,
                            N'(',
                            columnItem.precision,
                            N',',
                            columnItem.scale,
                            N')')
                    WHEN typeItem.name IN (N'datetime2', N'datetimeoffset', N'time')
                        THEN CONCAT(typeItem.name, N'(', columnItem.scale, N')')
                    ELSE typeItem.name
                END,
                N'|NULLABLE=',
                CONVERT(int, columnItem.is_nullable),
                N'|COLLATION=',
                COALESCE(columnItem.collation_name, N'-'),
                N'|IDENTITY=',
                CONVERT(int, columnItem.is_identity),
                N'|COMPUTED=',
                CONVERT(int, columnItem.is_computed),
                N'|SPARSE=',
                CONVERT(int, columnItem.is_sparse),
                N'|GENERATED=',
                CONVERT(int, columnItem.generated_always_type)
            )
        )
    FROM sys.columns AS columnItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = columnItem.object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.types AS typeItem
        ON typeItem.user_type_id = columnItem.user_type_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'D|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                columnItem.name,
                N'|',
                defaultItem.name,
                N'|',
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), defaultItem.definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                )
            )
        )
    FROM sys.default_constraints AS defaultItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = defaultItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.columns AS columnItem
        ON columnItem.object_id = defaultItem.parent_object_id
       AND columnItem.column_id = defaultItem.parent_column_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'CK|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                checkItem.name,
                N'|',
                LOWER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        CONVERT(nvarchar(max), checkItem.definition),
                        N'[', N''), N']', N''), N'(', N''), N')', N''),
                        N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                ),
                N'|DISABLED=',
                CONVERT(int, checkItem.is_disabled),
                N'|UNTRUSTED=',
                CONVERT(int, checkItem.is_not_trusted)
            )
        )
    FROM sys.check_constraints AS checkItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = checkItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'K|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                keyItem.name,
                N'|',
                keyItem.type,
                N'|',
                indexItem.name
            )
        )
    FROM sys.key_constraints AS keyItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = keyItem.parent_object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    INNER JOIN sys.indexes AS indexItem
        ON indexItem.object_id = keyItem.parent_object_id
       AND indexItem.index_id = keyItem.unique_index_id
    WHERE tableItem.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'FK|',
                parentSchema.name,
                N'|',
                parentTable.name,
                N'|',
                foreignItem.name,
                N'|',
                referencedSchema.name,
                N'|',
                referencedTable.name,
                N'|',
                (
                    SELECT STRING_AGG(
                        CONVERT(
                            nvarchar(max),
                            CONCAT(
                                parentColumn.name,
                                N':',
                                referencedColumn.name,
                                N':',
                                foreignColumn.constraint_column_id
                            )
                        ),
                        N'|'
                    ) WITHIN GROUP (ORDER BY foreignColumn.constraint_column_id)
                    FROM sys.foreign_key_columns AS foreignColumn
                    INNER JOIN sys.columns AS parentColumn
                        ON parentColumn.object_id = foreignColumn.parent_object_id
                       AND parentColumn.column_id = foreignColumn.parent_column_id
                    INNER JOIN sys.columns AS referencedColumn
                        ON referencedColumn.object_id = foreignColumn.referenced_object_id
                       AND referencedColumn.column_id = foreignColumn.referenced_column_id
                    WHERE foreignColumn.constraint_object_id = foreignItem.object_id
                ),
                N'|UPDATE=',
                foreignItem.update_referential_action_desc,
                N'|DELETE=',
                foreignItem.delete_referential_action_desc,
                N'|DISABLED=',
                CONVERT(int, foreignItem.is_disabled),
                N'|UNTRUSTED=',
                CONVERT(int, foreignItem.is_not_trusted)
            )
        )
    FROM sys.foreign_keys AS foreignItem
    INNER JOIN sys.tables AS parentTable
        ON parentTable.object_id = foreignItem.parent_object_id
    INNER JOIN sys.schemas AS parentSchema
        ON parentSchema.schema_id = parentTable.schema_id
    INNER JOIN sys.tables AS referencedTable
        ON referencedTable.object_id = foreignItem.referenced_object_id
    INNER JOIN sys.schemas AS referencedSchema
        ON referencedSchema.schema_id = referencedTable.schema_id
    WHERE parentTable.is_ms_shipped = 0

    UNION ALL

    SELECT
        CONVERT(
            nvarchar(max),
            CONCAT(
                N'I|',
                schemaItem.name,
                N'|',
                tableItem.name,
                N'|',
                indexItem.name,
                N'|',
                indexItem.type_desc,
                N'|UNIQUE=',
                CONVERT(int, indexItem.is_unique),
                N'|PK=',
                CONVERT(int, indexItem.is_primary_key),
                N'|UQ=',
                CONVERT(int, indexItem.is_unique_constraint),
                N'|FILTER=',
                CONVERT(int, indexItem.has_filter),
                N':',
                COALESCE(
                    LOWER(
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            CONVERT(nvarchar(max), indexItem.filter_definition),
                            N'[', N''), N']', N''), N'(', N''), N')', N''),
                            N' ', N''), NCHAR(13), N''), NCHAR(10), N'')
                    ),
                    N''
                ),
                N'|COLUMNS=',
                (
                    SELECT STRING_AGG(
                        CONVERT(
                            nvarchar(max),
                            CONCAT(
                                columnItem.name,
                                N':',
                                indexColumn.key_ordinal,
                                N':',
                                CONVERT(int, indexColumn.is_descending_key),
                                N':',
                                CONVERT(int, indexColumn.is_included_column)
                            )
                        ),
                        N'|'
                    ) WITHIN GROUP (ORDER BY indexColumn.index_column_id)
                    FROM sys.index_columns AS indexColumn
                    INNER JOIN sys.columns AS columnItem
                        ON columnItem.object_id = indexColumn.object_id
                       AND columnItem.column_id = indexColumn.column_id
                    WHERE indexColumn.object_id = indexItem.object_id
                      AND indexColumn.index_id = indexItem.index_id
                      AND
                      (
                          indexColumn.key_ordinal > 0
                          OR indexColumn.is_included_column = 1
                      )
                )
            )
        )
    FROM sys.indexes AS indexItem
    INNER JOIN sys.tables AS tableItem
        ON tableItem.object_id = indexItem.object_id
    INNER JOIN sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE tableItem.is_ms_shipped = 0
      AND indexItem.index_id > 0
)
SELECT
    N'QLHV_RT02_TARGET_TEST' AS DatabaseName,
    CONVERT(
        char(64),
        HASHBYTES(
            N'SHA2_256',
            STRING_AGG(
                MetadataLine COLLATE DATABASE_DEFAULT,
                NCHAR(10)
            ) WITHIN GROUP
            (
                ORDER BY MetadataLine COLLATE DATABASE_DEFAULT
            )
        ),
        2
    ) AS SchemaFingerprintSha256,
    (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped = 0) AS BusinessTableCount,
    (
        SELECT COALESCE(SUM(partitionItem.row_count), 0)
        FROM sys.tables AS tableItem
        LEFT JOIN sys.dm_db_partition_stats AS partitionItem
            ON partitionItem.object_id = tableItem.object_id
           AND partitionItem.index_id IN (0, 1)
        WHERE tableItem.is_ms_shipped = 0
    ) AS BusinessRowCount,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS ChangeTrackedTableCount,
    (SELECT COUNT_BIG(*) FROM sys.synonyms) AS SynonymCount
FROM SchemaMetadata;
GO
