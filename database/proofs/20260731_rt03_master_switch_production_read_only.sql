USE [QLHV_APP];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

SELECT N'UTC' AS Section, SYSUTCDATETIME() AS SqlUtc;

SELECT N'MASTER_SCHEMA' AS Section,
       OBJECT_ID(N'dbo.App_Rt03RealtimeControl',N'U') AS ControlObjectId,
       OBJECT_ID(N'dbo.App_Rt03RealtimeControlAudit',N'U') AS AuditObjectId,
       OBJECT_ID(N'dbo.App_Rt03RealtimeRunRequest',N'U') AS RunRequestObjectId;

SELECT N'WORKER_STATE' AS Section, Status, CycleActive, CurrentProfile,
       LastErrorCode, LastHeartbeatUtc
FROM dbo.App_QlhvDirectRealtimeWorkerState
WHERE WorkerStateId=1;

SELECT N'WRITERS' AS Section,
       (SELECT COUNT_BIG(1)
        FROM dbo.App_QlhvAutoSyncRun
        WHERE ActiveSlot=1 AND Status IN(N'QUEUED',N'RUNNING')
          AND CompletedAtUtc IS NULL
          AND UpdatedAtUtc>=DATEADD(SECOND,-120,SYSUTCDATETIME())
          AND (CurrentSourceType IS NOT NULL OR CurrentStage IS NOT NULL))
           AS ActiveAutoSyncRuns,
       (SELECT COUNT_BIG(1)
        FROM dbo.App_QlhvSyncOperationHistory
        WHERE Status IN(N'QUEUED',N'RUNNING')) AS ActiveOperations;

SELECT N'CHECKPOINT' AS Section, SourceProfileCode,
       SourceChangeTrackingVersion AS CheckpointVersion,
       CASE WHEN EXISTS
       (
           SELECT 1
           FROM dbo.App_QlhvDirectRealtimeApplyMarker marker
           WHERE marker.CycleId=cp.CycleId
             AND marker.SourceProfileCode=cp.SourceProfileCode
             AND marker.SourceChangeTrackingVersion=
                 cp.SourceChangeTrackingVersion
             AND marker.PlanHash=cp.PlanHash
             AND marker.MarkerHash=cp.MarkerHash
       ) THEN 1 ELSE 0 END AS HasExactCommittedMarker
FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint cp
WHERE Mode=N'DIRECT_REALTIME_APPLY' AND EnvironmentId=N'PRODUCTION'
ORDER BY SourceProfileCode;

SELECT N'V9_REVIEWS' AS Section, COUNT_BIG(1) AS ReviewRows,
       COUNT_BIG(DISTINCT SourceBusinessIdentityHash) AS DistinctSourceIdentities,
       COUNT_BIG(DISTINCT TargetIdentity) AS DistinctTargetIdentities,
       SUM(CASE WHEN ReviewState=N'REVIEWED_AND_RETAINED'
                 AND TargetRetainedActive=1 AND TargetMutated=0
                THEN 1 ELSE 0 END) AS RetainedActiveRows
FROM dbo.App_QlhvDirectRealtimeManualReview
WHERE EvidenceContractVersion=N'RT03-REVIEWED-RETAINED-1.0';

SELECT N'DUPLICATES' AS Section,
 (SELECT COUNT_BIG(*) FROM
  (SELECT SourceProfileCode,SourceMaKhoaHoc FROM dbo.App_KhoaHoc
   WHERE SourceMaKhoaHoc IS NOT NULL
   GROUP BY SourceProfileCode,SourceMaKhoaHoc HAVING COUNT_BIG(*)>1)d)
     AS CourseGroups,
 (SELECT COUNT_BIG(*) FROM
  (SELECT SourceProfileCode,SourceMaGV FROM dbo.App_GiaoVien
   WHERE SourceMaGV IS NOT NULL
   GROUP BY SourceProfileCode,SourceMaGV HAVING COUNT_BIG(*)>1)d)
     AS TeacherGroups,
 (SELECT COUNT_BIG(*) FROM
  (SELECT SourceProfileCode,SourceBienSoXe FROM dbo.App_XeTap
   WHERE SourceBienSoXe IS NOT NULL
   GROUP BY SourceProfileCode,SourceBienSoXe HAVING COUNT_BIG(*)>1)d)
     AS VehicleGroups,
 (SELECT COUNT_BIG(*) FROM
  (SELECT SourceProfileCode,SourceMaDK FROM dbo.App_HocVien
   WHERE IsDeleted=0 AND SourceMaDK IS NOT NULL
   GROUP BY SourceProfileCode,SourceMaDK HAVING COUNT_BIG(*)>1)d)
     AS LearnerGroups;
GO

USE [CSDL_OTO];
GO
DECLARE @OtoCheckpoint bigint=(
    SELECT SourceChangeTrackingVersion
    FROM QLHV_APP.dbo.App_QlhvDirectRealtimeApplyCheckpoint
    WHERE SourceProfileCode=N'CSDT_OTO'
      AND Mode=N'DIRECT_REALTIME_APPLY'
      AND EnvironmentId=N'PRODUCTION');
SELECT N'OTO_CT' AS Section, @OtoCheckpoint AS CheckpointVersion,
       CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentVersion,
       CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))
           AS MinimumValidVersion;
GO

USE [CSDL_MOTO];
GO
DECLARE @MotoCheckpoint bigint=(
    SELECT SourceChangeTrackingVersion
    FROM QLHV_APP.dbo.App_QlhvDirectRealtimeApplyCheckpoint
    WHERE SourceProfileCode=N'CSDT_MOTO'
      AND Mode=N'DIRECT_REALTIME_APPLY'
      AND EnvironmentId=N'PRODUCTION');
SELECT N'MOTO_CT' AS Section, @MotoCheckpoint AS CheckpointVersion,
       CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentVersion,
       CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))
           AS MinimumValidVersion;
GO
