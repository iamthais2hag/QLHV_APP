USE [CSDL_MOTO];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName'))<>N'CSDLTTTC'
   OR DB_ID()<>8
   OR NOT EXISTS
      (SELECT 1 FROM sys.database_recovery_status WHERE database_id=DB_ID()
       AND database_guid='308BDDA8-80F3-4ACB-9836-578D80A9E98E')
   OR (SELECT COUNT(1) FROM sys.change_tracking_tables)<>5
   OR (SELECT snapshot_isolation_state FROM sys.databases WHERE database_id=DB_ID())<>1
   OR (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id=DB_ID())<>0
    THROW 527600, 'RT03_MOTO_CAPABILITY_REJECTED.', 1;

DECLARE @InitialVersion bigint=CHANGE_TRACKING_CURRENT_VERSION();
IF @InitialVersion IS NULL THROW 527601, 'RT03_MOTO_CT_VERSION_MISSING.', 1;
CREATE TABLE #Rt03MotoBootstrap(InitialVersion bigint NOT NULL);
INSERT #Rt03MotoBootstrap VALUES(@InitialVersion);
GO

USE [QLHV_APP];
GO

IF EXISTS (SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint WHERE SourceProfileCode=N'CSDT_MOTO')
    THROW 527602, 'RT03_MOTO_CHECKPOINT_ALREADY_EXISTS.', 1;
IF NOT EXISTS
   (SELECT 1 FROM dbo.App_QlhvDirectRealtimeProfileState
    WHERE SourceProfileCode=N'CSDT_OTO' AND Enabled=1
      AND LastStatus=N'HEALTHY_NO_CHANGE')
    THROW 527603, 'RT03_OTO_HEALTH_PROOF_MISSING.', 1;

DECLARE @CycleId uniqueidentifier=NEWID();
DECLARE @Version bigint=(SELECT InitialVersion FROM #Rt03MotoBootstrap);
DECLARE @PlanHash char(64)=CONVERT(char(64),HASHBYTES('SHA2_256',
    CONCAT(N'RT03-MOTO-BOOTSTRAP|',@Version)),2);
DECLARE @DispositionHash char(64)=CONVERT(char(64),HASHBYTES('SHA2_256',
    N'NO_CHANGE|NO_BUSINESS_MUTATION'),2);
DECLARE @MarkerHash binary(32)=HASHBYTES('SHA2_256',
    CONCAT(CONVERT(nvarchar(36),@CycleId),N'|',@PlanHash,N'|',@Version));
DECLARE @PreservedHash char(64)=CONVERT(char(64),HASHBYTES('SHA2_256',
    N'MOTO_BOOTSTRAP_NO_BUSINESS_MUTATION'),2);

BEGIN TRANSACTION;
INSERT dbo.App_QlhvDirectRealtimeApplyMarker
(
    CycleId,SourceProfileCode,PlanHash,MarkerHash,DispositionHash,
    SourceDatabaseGuid,SourceChangeTrackingVersion,InsertedRows,UpdatedRows,
    RetainedRows,PreservedQlhvOwnedHash,CommittedAtUtc
)
VALUES
(
    @CycleId,N'CSDT_MOTO',@PlanHash,@MarkerHash,@DispositionHash,
    '308BDDA8-80F3-4ACB-9836-578D80A9E98E',@Version,0,0,0,@PreservedHash,SYSUTCDATETIME()
);

INSERT dbo.App_QlhvDirectRealtimeApplyCheckpoint
(
    SourceProfileCode,Mode,MappingFingerprint,EnvironmentId,
    SourceDatabaseGuid,SourceChangeTrackingVersion,CycleId,PlanHash,
    MarkerHash,PublishedAtUtc
)
VALUES
(
    N'CSDT_MOTO',N'DIRECT_REALTIME_APPLY',
    '7bb2c2fc99cd06a222af2e36c0c61f259a4488ceecad7064c6e308fc223e4ee9',
    N'PRODUCTION','308BDDA8-80F3-4ACB-9836-578D80A9E98E',@Version,
    @CycleId,@PlanHash,@MarkerHash,SYSUTCDATETIME()
);
COMMIT TRANSACTION;
GO

SELECT N'RT03_MOTO_CHECKPOINT_INITIALIZED_NO_MUTATION' AS Evidence;
GO
