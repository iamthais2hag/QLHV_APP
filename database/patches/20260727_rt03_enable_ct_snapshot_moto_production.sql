USE [CSDL_MOTO];
GO

/* RT-03 Task 2 only. Run only after the OTO canary is PASS. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
   OR DB_ID() <> 8
   OR NOT EXISTS
      (
          SELECT 1 FROM sys.database_recovery_status
          WHERE database_id = DB_ID()
            AND database_guid = '308BDDA8-80F3-4ACB-9836-578D80A9E98E'
      )
    THROW 527547, 'RT03_PRODUCTION_IDENTITY_REJECTED: CSDL_MOTO.', 1;

IF N'$(RT03_OTO_CANARY_RESULT)' <> N'PASSED'
    THROW 527548, 'RT03_OTO_MUST_PASS_FIRST.', 1;

IF EXISTS
(
    SELECT required.TableName
    FROM
    (
        VALUES
            (N'NguoiLX'),
            (N'NguoiLX_HoSo'),
            (N'KhoaHoc'),
            (N'DM_HangDT'),
            (N'DM_DVHC')
    ) AS required(TableName)
    LEFT JOIN sys.tables AS tableItem
        ON tableItem.schema_id = SCHEMA_ID(N'dbo')
       AND tableItem.name = required.TableName
    WHERE tableItem.object_id IS NULL
       OR NOT EXISTS
          (
              SELECT 1 FROM sys.indexes AS indexItem
              WHERE indexItem.object_id = tableItem.object_id
                AND indexItem.is_primary_key = 1
          )
)
    THROW 527549, 'RT03_CT_PRECONDITION_REJECTED: exact MOTO table/PK allowlist.', 1;

IF EXISTS
(
    SELECT 1 FROM sys.databases
    WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 1
)
    THROW 527550, 'RT03_RCSI_FORBIDDEN: CSDL_MOTO.', 1;
GO

ALTER DATABASE [CSDL_MOTO] SET ALLOW_SNAPSHOT_ISOLATION ON;
GO
ALTER DATABASE [CSDL_MOTO]
    SET CHANGE_TRACKING = ON
    (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON);
GO

IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX'))
    ALTER TABLE dbo.NguoiLX ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo'))
    ALTER TABLE dbo.NguoiLX_HoSo ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.KhoaHoc'))
    ALTER TABLE dbo.KhoaHoc ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.DM_HangDT'))
    ALTER TABLE dbo.DM_HangDT ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.DM_DVHC'))
    ALTER TABLE dbo.DM_DVHC ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);
GO

IF (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) <> 5
   OR (SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()) <> 1
   OR (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) <> 0
    THROW 527551, 'RT03_CT_POSTCONDITION_REJECTED: CSDL_MOTO.', 1;

SELECT
    N'RT03_MOTO_CT_ENABLED' AS Evidence,
    CHANGE_TRACKING_CURRENT_VERSION() AS InitialChangeTrackingVersion,
    (SELECT COUNT_BIG(*) FROM sys.change_tracking_tables) AS TrackedTableCount,
    (SELECT snapshot_isolation_state_desc FROM sys.databases WHERE database_id = DB_ID())
        AS SnapshotIsolationState,
    (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID())
        AS IsReadCommittedSnapshotOn;
GO
