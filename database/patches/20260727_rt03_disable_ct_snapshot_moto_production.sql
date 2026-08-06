USE [CSDL_MOTO];
GO

/* Exact rollback for 20260727_rt03_enable_ct_snapshot_moto_production.sql. */
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
    THROW 527552, 'RT03_PRODUCTION_IDENTITY_REJECTED: CSDL_MOTO rollback.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.dm_tran_active_snapshot_database_transactions AS snapshotItem
    INNER JOIN sys.dm_exec_sessions AS sessionItem
        ON sessionItem.session_id = snapshotItem.session_id
    WHERE sessionItem.database_id = DB_ID()
)
    THROW 527553, 'RT03_SNAPSHOT_ROLLBACK_BLOCKED: active CSDL_MOTO snapshot.', 1;
GO

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.DM_DVHC'))
    ALTER TABLE dbo.DM_DVHC DISABLE CHANGE_TRACKING;
IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.DM_HangDT'))
    ALTER TABLE dbo.DM_HangDT DISABLE CHANGE_TRACKING;
IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.KhoaHoc'))
    ALTER TABLE dbo.KhoaHoc DISABLE CHANGE_TRACKING;
IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX_HoSo'))
    ALTER TABLE dbo.NguoiLX_HoSo DISABLE CHANGE_TRACKING;
IF EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.NguoiLX'))
    ALTER TABLE dbo.NguoiLX DISABLE CHANGE_TRACKING;
GO

ALTER DATABASE [CSDL_MOTO] SET CHANGE_TRACKING = OFF;
GO
ALTER DATABASE [CSDL_MOTO] SET ALLOW_SNAPSHOT_ISOLATION OFF;
GO

IF EXISTS (SELECT 1 FROM sys.change_tracking_tables)
   OR (SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()) <> 0
   OR (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) <> 0
    THROW 527554, 'RT03_CT_ROLLBACK_POSTCONDITION_REJECTED: CSDL_MOTO.', 1;

SELECT N'RT03_MOTO_CT_ROLLBACK_VERIFIED' AS Evidence;
GO
