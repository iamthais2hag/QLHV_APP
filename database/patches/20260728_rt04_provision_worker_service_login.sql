/*
  RT-04 least-privilege Windows virtual service account.
  Security-only patch: no business rows, checkpoints, CT history, or Auto Sync
  history are changed. Re-runnable and fixed to the approved production identities.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'CSDLTTTC'
    THROW 527800, 'RT04_SERVICE_SERVER_IDENTITY_REJECTED', 1;

USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.server_principals
               WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker')
BEGIN
    CREATE LOGIN [NT SERVICE\QLHV_APP_RealtimeWorker] FROM WINDOWS;
END;

USE [QLHV_APP];
IF DB_ID()<>12 OR NOT EXISTS
(
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
)
    THROW 527801, 'RT04_SERVICE_TARGET_IDENTITY_REJECTED', 1;

IF USER_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NULL
    CREATE USER [NT SERVICE\QLHV_APP_RealtimeWorker]
      FOR LOGIN [NT SERVICE\QLHV_APP_RealtimeWorker];

IF IS_ROLEMEMBER(N'db_datareader',N'NT SERVICE\QLHV_APP_RealtimeWorker')<>1
    ALTER ROLE [db_datareader]
      ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];

GRANT CONNECT TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT INSERT, UPDATE ON dbo.App_HocVien
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
DENY DELETE ON dbo.App_HocVien
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT INSERT ON dbo.App_QlhvDirectRealtimeApplyMarker
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT INSERT, UPDATE ON dbo.App_QlhvDirectRealtimeApplyCheckpoint
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT INSERT ON dbo.App_QlhvDirectRealtimeCycleHistory
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT UPDATE ON dbo.App_QlhvDirectRealtimeProfileState
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT UPDATE ON dbo.App_QlhvDirectRealtimeWorkerState
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT INSERT ON dbo.App_QlhvDirectRealtimeManualReview
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];

USE [CSDL_OTO];
IF DB_ID()<>9 OR NOT EXISTS
(
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='9A8B9BC1-18F3-4823-8123-3DC197A9D540'
)
    THROW 527802, 'RT04_SERVICE_OTO_IDENTITY_REJECTED', 1;

IF USER_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NULL
    CREATE USER [NT SERVICE\QLHV_APP_RealtimeWorker]
      FOR LOGIN [NT SERVICE\QLHV_APP_RealtimeWorker];
IF IS_ROLEMEMBER(N'db_datareader',N'NT SERVICE\QLHV_APP_RealtimeWorker')<>1
    ALTER ROLE [db_datareader]
      ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT CONNECT TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX_HoSo
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.KhoaHoc
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_HangDT
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_DVHC
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];

USE [CSDL_MOTO];
IF DB_ID()<>8 OR NOT EXISTS
(
    SELECT 1 FROM sys.database_recovery_status
    WHERE database_id=DB_ID()
      AND database_guid='308BDDA8-80F3-4ACB-9836-578D80A9E98E'
)
    THROW 527803, 'RT04_SERVICE_MOTO_IDENTITY_REJECTED', 1;

IF USER_ID(N'NT SERVICE\QLHV_APP_RealtimeWorker') IS NULL
    CREATE USER [NT SERVICE\QLHV_APP_RealtimeWorker]
      FOR LOGIN [NT SERVICE\QLHV_APP_RealtimeWorker];
IF IS_ROLEMEMBER(N'db_datareader',N'NT SERVICE\QLHV_APP_RealtimeWorker')<>1
    ALTER ROLE [db_datareader]
      ADD MEMBER [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT CONNECT TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.NguoiLX_HoSo
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.KhoaHoc
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_HangDT
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];
GRANT VIEW CHANGE TRACKING ON OBJECT::dbo.DM_DVHC
  TO [NT SERVICE\QLHV_APP_RealtimeWorker];

SELECT N'RT04_WORKER_SERVICE_LOGIN_PROVISIONED' AS Result;
