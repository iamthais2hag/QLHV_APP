/*
    NOT EXECUTED BY THIS TASK.
    Enables Change Tracking only for the proven CSDL_OTO.dbo.XeTap source.
    It does not initialize/backfill a QLHV_APP vehicle checkpoint.
*/
USE [CSDL_OTO];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_NAME()<>N'CSDL_OTO' OR DB_ID()<>9 OR NOT EXISTS
(
    SELECT 1
    FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='9A8B9BC1-18F3-4823-8123-3DC197A9D540'
)
    THROW 527910, 'VEHICLE_OTO_SOURCE_IDENTITY_REJECTED', 1;

IF OBJECT_ID(N'dbo.XeTap',N'U') IS NULL
    THROW 527911, 'VEHICLE_OTO_SOURCE_TABLE_MISSING', 1;

IF (SELECT COUNT(1) FROM sys.columns
    WHERE object_id=OBJECT_ID(N'dbo.XeTap'))<>34
    THROW 527912, 'VEHICLE_OTO_SOURCE_COLUMN_COUNT_MISMATCH', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    JOIN sys.index_columns ic
      ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    JOIN sys.columns c
      ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    JOIN sys.types t
      ON t.user_type_id=c.user_type_id
    WHERE i.object_id=OBJECT_ID(N'dbo.XeTap')
      AND i.is_primary_key=1
      AND ic.key_ordinal=1
      AND c.name=N'BienSoXe'
      AND t.name=N'varchar'
      AND c.max_length=10
      AND c.is_nullable=0
)
    THROW 527913, 'VEHICLE_OTO_SOURCE_PRIMARY_KEY_MISMATCH', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.change_tracking_databases
    WHERE database_id=DB_ID()
)
    ALTER DATABASE [CSDL_OTO] SET CHANGE_TRACKING=ON
    (CHANGE_RETENTION=7 DAYS,AUTO_CLEANUP=ON);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.change_tracking_tables
    WHERE object_id=OBJECT_ID(N'dbo.XeTap')
)
    ALTER TABLE dbo.XeTap ENABLE CHANGE_TRACKING
    WITH(TRACK_COLUMNS_UPDATED=ON);
ELSE IF EXISTS
(
    SELECT 1 FROM sys.change_tracking_tables
    WHERE object_id=OBJECT_ID(N'dbo.XeTap')
      AND is_track_columns_updated_on=0
)
    THROW 527914, 'VEHICLE_OTO_TRACK_COLUMNS_UPDATED_REQUIRED', 1;
GO

IF USER_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NOT NULL
    GRANT SELECT,VIEW CHANGE TRACKING ON OBJECT::dbo.XeTap
      TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GO

IF CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.XeTap')) IS NULL
    THROW 527915, 'VEHICLE_OTO_CHANGE_TRACKING_VERIFICATION_FAILED', 1;

SELECT N'VEHICLE_OTO_CT_READY_CHECKPOINT_NOT_INITIALIZED' AS Result,
       CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentCtVersion,
       CONVERT(bigint,CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.XeTap')))
           AS MinimumValidVersion;
GO
