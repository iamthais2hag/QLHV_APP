SET NOCOUNT ON;
SET XACT_ABORT ON;
USE [CSDL_MOTO];

IF NOT EXISTS(SELECT 1 FROM sys.database_recovery_status WHERE database_id=DB_ID() AND database_guid=CONVERT(uniqueidentifier,N'308BDDA8-80F3-4ACB-9836-578D80A9E98E'))
    THROW 532720,'TVP_MOTO_DATABASE_GUID_REJECTED',1;
IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_databases WHERE database_id=DB_ID())
    THROW 532721,'TVP_MOTO_DATABASE_CT_DISABLED',1;
IF EXISTS
(
    SELECT required.TableName
    FROM (VALUES(N'GiaoVien',N'MaGV'),(N'XeTap',N'BienSoXe'),(N'KhoaHoc_GiaoVien',N'MaLichLV'),(N'KhoaHoc_XeTap',N'MaLichSD')) required(TableName,KeyName)
    WHERE OBJECT_ID(N'dbo.'+required.TableName,N'U') IS NULL OR NOT EXISTS
    (
        SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE i.object_id=OBJECT_ID(N'dbo.'+required.TableName,N'U') AND i.is_primary_key=1 AND ic.key_ordinal=1 AND c.name=required.KeyName
    )
)
    THROW 532722,'TVP_MOTO_SOURCE_IDENTITY_REJECTED',1;

IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_tables WHERE object_id=OBJECT_ID(N'dbo.GiaoVien'))
    ALTER TABLE dbo.GiaoVien ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED=ON);
IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_tables WHERE object_id=OBJECT_ID(N'dbo.XeTap'))
    ALTER TABLE dbo.XeTap ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED=ON);
IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_tables WHERE object_id=OBJECT_ID(N'dbo.KhoaHoc_GiaoVien'))
    ALTER TABLE dbo.KhoaHoc_GiaoVien ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED=ON);
IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_tables WHERE object_id=OBJECT_ID(N'dbo.KhoaHoc_XeTap'))
    ALTER TABLE dbo.KhoaHoc_XeTap ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED=ON);

IF (SELECT COUNT(*) FROM sys.change_tracking_tables WHERE object_id IN(OBJECT_ID(N'dbo.GiaoVien'),OBJECT_ID(N'dbo.XeTap'),OBJECT_ID(N'dbo.KhoaHoc_GiaoVien'),OBJECT_ID(N'dbo.KhoaHoc_XeTap')) AND is_track_columns_updated_on=1)<>4
    THROW 532723,'TVP_MOTO_CT_VERIFY_FAILED',1;
SELECT N'TEACHER_VEHICLE_PROJECTION_MOTO_CT_PASS' Marker,CHANGE_TRACKING_CURRENT_VERSION() AnchorVersion;
