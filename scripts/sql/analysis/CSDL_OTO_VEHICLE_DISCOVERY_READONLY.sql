USE [CSDL_OTO];
GO

/*
  READ-ONLY: discover the CSDL_OTO training-vehicle model.
  This script returns metadata and bounded aggregate output only.
*/

SELECT
    @@SERVERNAME AS ServerName,
    DB_NAME() AS DatabaseName,
    DB_ID() AS DatabaseId,
    compatibility_level AS CompatibilityLevel,
    collation_name AS CollationName,
    snapshot_isolation_state_desc AS SnapshotIsolationState,
    is_read_committed_snapshot_on AS IsReadCommittedSnapshotOn
FROM sys.databases
WHERE database_id = DB_ID();

SELECT
    schemaRow.name AS SchemaName,
    objectRow.name AS ObjectName,
    objectRow.type_desc AS ObjectType,
    SUM(CASE WHEN partitionRow.index_id IN (0, 1) THEN partitionRow.rows ELSE 0 END) AS ApproximateRows
FROM sys.objects AS objectRow
INNER JOIN sys.schemas AS schemaRow
    ON schemaRow.schema_id = objectRow.schema_id
LEFT JOIN sys.partitions AS partitionRow
    ON partitionRow.object_id = objectRow.object_id
WHERE objectRow.name IN
(
    N'XeTap', N'KhoaHoc_XeTap', N'KhoaHoc_GiaoVien', N'KhoaHoc', N'LichHoc',
    N'DM_DonViGTVT', N'DM_HangGPLX', N'DM_HangDT', N'DM_LuuLuongDaoTao'
)
GROUP BY schemaRow.name, objectRow.name, objectRow.type_desc
ORDER BY schemaRow.name, objectRow.name;

SELECT
    schemaRow.name AS SchemaName,
    tableRow.name AS TableName,
    columnRow.column_id AS ColumnOrdinal,
    columnRow.name AS ColumnName,
    TYPE_NAME(columnRow.user_type_id) AS SqlType,
    columnRow.max_length AS MaxLengthBytes,
    columnRow.precision AS NumericPrecision,
    columnRow.scale AS NumericScale,
    columnRow.is_nullable AS IsNullable,
    columnRow.is_identity AS IsIdentity,
    columnRow.is_computed AS IsComputed,
    defaultRow.definition AS DefaultDefinition,
    CONVERT(nvarchar(4000), propertyRow.value) AS ColumnDescription
FROM sys.tables AS tableRow
INNER JOIN sys.schemas AS schemaRow
    ON schemaRow.schema_id = tableRow.schema_id
INNER JOIN sys.columns AS columnRow
    ON columnRow.object_id = tableRow.object_id
LEFT JOIN sys.default_constraints AS defaultRow
    ON defaultRow.parent_object_id = columnRow.object_id
   AND defaultRow.parent_column_id = columnRow.column_id
LEFT JOIN sys.extended_properties AS propertyRow
    ON propertyRow.major_id = columnRow.object_id
   AND propertyRow.minor_id = columnRow.column_id
   AND propertyRow.name = N'MS_Description'
WHERE tableRow.name IN (N'XeTap', N'KhoaHoc_XeTap', N'KhoaHoc_GiaoVien')
ORDER BY schemaRow.name, tableRow.name, columnRow.column_id;

SELECT
    OBJECT_SCHEMA_NAME(indexRow.object_id) AS SchemaName,
    OBJECT_NAME(indexRow.object_id) AS TableName,
    indexRow.name AS IndexName,
    indexRow.is_primary_key AS IsPrimaryKey,
    indexRow.is_unique AS IsUnique,
    indexColumnRow.key_ordinal AS KeyOrdinal,
    columnRow.name AS ColumnName,
    indexRow.has_filter AS HasFilter,
    indexRow.filter_definition AS FilterDefinition
FROM sys.indexes AS indexRow
INNER JOIN sys.index_columns AS indexColumnRow
    ON indexColumnRow.object_id = indexRow.object_id
   AND indexColumnRow.index_id = indexRow.index_id
INNER JOIN sys.columns AS columnRow
    ON columnRow.object_id = indexColumnRow.object_id
   AND columnRow.column_id = indexColumnRow.column_id
WHERE indexRow.object_id IN
(
    OBJECT_ID(N'dbo.XeTap'),
    OBJECT_ID(N'dbo.KhoaHoc_XeTap'),
    OBJECT_ID(N'dbo.KhoaHoc_GiaoVien')
)
  AND indexRow.index_id > 0
ORDER BY TableName, IndexName, indexColumnRow.key_ordinal, indexColumnRow.index_column_id;

SELECT
    foreignKeyRow.name AS ForeignKeyName,
    OBJECT_SCHEMA_NAME(foreignKeyRow.parent_object_id) AS ParentSchema,
    OBJECT_NAME(foreignKeyRow.parent_object_id) AS ParentTable,
    parentColumnRow.name AS ParentColumn,
    OBJECT_SCHEMA_NAME(foreignKeyRow.referenced_object_id) AS ReferencedSchema,
    OBJECT_NAME(foreignKeyRow.referenced_object_id) AS ReferencedTable,
    referencedColumnRow.name AS ReferencedColumn,
    foreignKeyRow.delete_referential_action_desc AS DeleteAction,
    foreignKeyRow.update_referential_action_desc AS UpdateAction
FROM sys.foreign_keys AS foreignKeyRow
INNER JOIN sys.foreign_key_columns AS foreignKeyColumnRow
    ON foreignKeyColumnRow.constraint_object_id = foreignKeyRow.object_id
INNER JOIN sys.columns AS parentColumnRow
    ON parentColumnRow.object_id = foreignKeyColumnRow.parent_object_id
   AND parentColumnRow.column_id = foreignKeyColumnRow.parent_column_id
INNER JOIN sys.columns AS referencedColumnRow
    ON referencedColumnRow.object_id = foreignKeyColumnRow.referenced_object_id
   AND referencedColumnRow.column_id = foreignKeyColumnRow.referenced_column_id
WHERE foreignKeyRow.parent_object_id IN
(
    OBJECT_ID(N'dbo.XeTap'),
    OBJECT_ID(N'dbo.KhoaHoc_XeTap'),
    OBJECT_ID(N'dbo.KhoaHoc_GiaoVien')
)
ORDER BY ParentTable, ForeignKeyName, foreignKeyColumnRow.constraint_column_id;

SELECT
    schemaRow.name AS SchemaName,
    objectRow.name AS ModuleName,
    objectRow.type_desc AS ModuleType,
    CASE WHEN moduleRow.definition LIKE N'%XeTap%' THEN 1 ELSE 0 END AS ReferencesVehicleName,
    CASE WHEN moduleRow.definition LIKE N'%BienSoXe%' THEN 1 ELSE 0 END AS ReferencesPlate,
    CASE WHEN moduleRow.definition LIKE N'%SoKhung%' THEN 1 ELSE 0 END AS ReferencesFrameNumber,
    CASE WHEN moduleRow.definition LIKE N'%SoDongCo%' THEN 1 ELSE 0 END AS ReferencesEngineNumber,
    CASE WHEN moduleRow.definition LIKE N'%MaFileTiepNhanXML%' THEN 1 ELSE 0 END AS UsesXmlReceiptIdentity
FROM sys.objects AS objectRow
INNER JOIN sys.schemas AS schemaRow
    ON schemaRow.schema_id = objectRow.schema_id
INNER JOIN sys.sql_modules AS moduleRow
    ON moduleRow.object_id = objectRow.object_id
WHERE objectRow.name LIKE N'%XeTap%'
   OR moduleRow.definition LIKE N'%XeTap%'
   OR moduleRow.definition LIKE N'%BienSoXe%'
   OR moduleRow.definition LIKE N'%SoKhung%'
   OR moduleRow.definition LIKE N'%SoDongCo%'
ORDER BY objectRow.type_desc, schemaRow.name, objectRow.name;

SELECT
    changeTableRow.begin_version AS ChangeTrackingBeginVersion,
    changeTableRow.min_valid_version AS ChangeTrackingMinValidVersion
FROM sys.change_tracking_tables AS changeTableRow
WHERE changeTableRow.object_id = OBJECT_ID(N'dbo.XeTap');

SELECT @@TRANCOUNT AS SessionOpenTransactionCount;
GO
