USE [QLHV_APP];
GO
/*
  Disposable behavioral rehearsal for the RT03 V7 Worker permission role.

  This script is deliberately locked to the named disposable SQL instance.
  Every successful target mutation is enclosed by an outer transaction and
  rolled back. Source DML probes use TOP (0), so even an unexpected grant
  cannot alter source rows.
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Principal sysname=N'NT SERVICE\QLHV_APP_RealtimeWorker';

IF CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName'))<>
   N'$(ExpectedServerName)'
   OR N'$(ExpectedServerName)'<>N'CSDLTTTC\QLHVRT02'
    THROW 528100,'RT03_V7_REHEARSAL_DISPOSABLE_SERVER_REJECTED',1;

IF DB_ID()<>CONVERT(int,N'$(TargetDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=
             CONVERT(uniqueidentifier,N'$(TargetDatabaseGuid)')
   )
    THROW 528101,'RT03_V7_REHEARSAL_TARGET_IDENTITY_REJECTED',1;

DECLARE
    @CourseCt120Update bit=0,
    @CourseCt122Update bit=0,
    @CourseNoChange bit=0,
    @CourseOwnedPreserved bit=0,
    @LearnerInsert bit=0,
    @LearnerUpdate bit=0,
    @LearnerOwnedPreserved bit=0,
    @TeacherUpdate bit=0,
    @TeacherOwnedPreserved bit=0,
    @VehicleInsert bit=0,
    @VehicleUpdate bit=0,
    @VehicleOwnedPreserved bit=0,
    @RecoveryMarkerCommitted bit=0,
    @RecoveryCheckpointAdvanced bit=0,
    @TargetDeleteDenied bit=0,
    @OtoSourceRead bit=0,
    @OtoSourceUpdateDenied bit=0,
    @OtoSourceDeleteDenied bit=0,
    @MotoSourceRead bit=0,
    @MotoSourceUpdateDenied bit=0,
    @MotoSourceDeleteDenied bit=0,
    @TransactionRollbackClean bit=0,
    @AssignmentBoundaryPreserved bit=0;

DECLARE
    @CourseId bigint,
    @TeacherId bigint,
    @LearnerId bigint,
    @VehicleId bigint,
    @CourseOwnedBefore varbinary(32),
    @CourseOwnedAfter varbinary(32),
    @TeacherOwnedBefore varbinary(32),
    @TeacherOwnedAfter varbinary(32),
    @LearnerOwnedBefore varbinary(32),
    @LearnerOwnedAfter varbinary(32),
    @VehicleOwnedBefore varbinary(32),
    @VehicleOwnedAfter varbinary(32),
    @RowsBeforeCourse bigint=(SELECT COUNT_BIG(*) FROM dbo.App_KhoaHoc),
    @RowsBeforeTeacher bigint=(SELECT COUNT_BIG(*) FROM dbo.App_GiaoVien),
    @RowsBeforeLearner bigint=(SELECT COUNT_BIG(*) FROM dbo.App_HocVien),
    @RowsBeforeVehicle bigint=(SELECT COUNT_BIG(*) FROM dbo.App_XeTap),
    @RowsBeforeMarker bigint=
        (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeApplyMarker),
    @CheckpointBefore bigint,
    @AnchorVersion bigint,
    @CheckpointGuid uniqueidentifier,
    @RecoveryId uniqueidentifier=NEWID(),
    @SyntheticSuffix varchar(12)=
        RIGHT(REPLACE(CONVERT(varchar(36),NEWID()),'-',''),12),
    @Hash64 char(64)=REPLICATE('A',64),
    @Stage nvarchar(100)=N'FIXTURE';

SELECT TOP(1)
    @CourseId=KhoaHocId,
    @CourseOwnedBefore=HASHBYTES
    (
        'SHA2_256',
        CONCAT(
            KhoaHocId,N'|',COALESCE(GhiChuNoiBo,N'<NULL>'),N'|',
            COALESCE(TrangThai,N'<NULL>'),N'|',
            COALESCE(CONVERT(nvarchar(33),NgayBatDauThucHanh,126),N'<NULL>'),
            N'|',COALESCE(CONVERT(nvarchar(30),LuuLuongDaoTao),N'<NULL>'),
            N'|',CONVERT(nvarchar(33),CreatedAt,126),N'|',
            COALESCE(CreatedBy,N'<NULL>'))
    )
FROM dbo.App_KhoaHoc
WHERE SourceProfileCode IS NOT NULL
  AND SourceMaKhoaHoc IS NOT NULL
ORDER BY KhoaHocId;

SELECT TOP(1)
    @TeacherId=GiaoVienId,
    @TeacherOwnedBefore=HASHBYTES
    (
        'SHA2_256',
        CONCAT(
            GiaoVienId,N'|',CONVERT(nvarchar(33),CreatedAt,126),N'|',
            COALESCE(CreatedBy,N'<NULL>'))
    )
FROM dbo.App_GiaoVien
WHERE SourceProfileCode IS NOT NULL
  AND SourceMaGV IS NOT NULL
ORDER BY GiaoVienId;

SELECT TOP(1)
    @CheckpointBefore=SourceChangeTrackingVersion,
    @CheckpointGuid=SourceDatabaseGuid
FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
WHERE Mode=N'DIRECT_REALTIME_APPLY'
  AND EnvironmentId=N'PRODUCTION'
  AND SourceProfileCode=N'CSDT_OTO';
SET @AnchorVersion=@CheckpointBefore+1;

IF @CourseId IS NULL OR @TeacherId IS NULL
   OR @CheckpointBefore IS NULL OR @CheckpointGuid IS NULL
    THROW 528102,'RT03_V7_REHEARSAL_FIXTURE_REJECTED',1;

BEGIN TRY
    BEGIN TRANSACTION;
    EXECUTE AS USER=@Principal;

    SET @Stage=N'COURSE';
    UPDATE dbo.App_KhoaHoc
    SET SourceHash=CONCAT(N'RT03-V7-CT120-',@SyntheticSuffix),
        UpdatedAtUtc=SYSUTCDATETIME()
    WHERE KhoaHocId=@CourseId;
    SET @CourseCt120Update=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);

    UPDATE dbo.App_KhoaHoc
    SET SourceHash=CONCAT(N'RT03-V7-CT122-',@SyntheticSuffix),
        UpdatedAtUtc=SYSUTCDATETIME()
    WHERE KhoaHocId=@CourseId;
    SET @CourseCt122Update=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);

    UPDATE dbo.App_KhoaHoc
    SET SourceHash=SourceHash
    WHERE KhoaHocId=@CourseId
      AND ISNULL(SourceHash,N'')<>ISNULL(SourceHash,N'');
    SET @CourseNoChange=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=0 THEN 1 ELSE 0 END);

    SELECT
        @CourseOwnedAfter=HASHBYTES
        (
            'SHA2_256',
            CONCAT(
                KhoaHocId,N'|',COALESCE(GhiChuNoiBo,N'<NULL>'),N'|',
                COALESCE(TrangThai,N'<NULL>'),N'|',
                COALESCE(
                    CONVERT(nvarchar(33),NgayBatDauThucHanh,126),
                    N'<NULL>'),
                N'|',
                COALESCE(CONVERT(nvarchar(30),LuuLuongDaoTao),N'<NULL>'),
                N'|',CONVERT(nvarchar(33),CreatedAt,126),N'|',
                COALESCE(CreatedBy,N'<NULL>'))
        )
    FROM dbo.App_KhoaHoc
    WHERE KhoaHocId=@CourseId;
    SET @CourseOwnedPreserved=
        CONVERT(bit,CASE WHEN @CourseOwnedAfter=@CourseOwnedBefore
                        THEN 1 ELSE 0 END);

    SET @Stage=N'TEACHER';
    UPDATE dbo.App_GiaoVien
    SET SourceHash=CONCAT(N'RT03-V7-TEACHER-',@SyntheticSuffix),
        UpdatedAtUtc=SYSUTCDATETIME()
    WHERE GiaoVienId=@TeacherId;
    SET @TeacherUpdate=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);

    SELECT
        @TeacherOwnedAfter=HASHBYTES
        (
            'SHA2_256',
            CONCAT(
                GiaoVienId,N'|',CONVERT(nvarchar(33),CreatedAt,126),N'|',
                COALESCE(CreatedBy,N'<NULL>'))
        )
    FROM dbo.App_GiaoVien
    WHERE GiaoVienId=@TeacherId;
    SET @TeacherOwnedPreserved=
        CONVERT(bit,CASE WHEN @TeacherOwnedAfter=@TeacherOwnedBefore
                        THEN 1 ELSE 0 END);

    SET @Stage=N'LEARNER';
    INSERT dbo.App_HocVien
    (
        MaDK,SourceProfileCode,SourceMaDK,SourceSystem,SourceVersion
    )
    VALUES
    (
        CONCAT(N'V7-',@SyntheticSuffix),
        N'CSDT_OTO',
        CONCAT(N'V7-',@SyntheticSuffix),
        N'V2',
        N'INSERT'
    );
    SET @LearnerInsert=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);
    SET @LearnerId=CONVERT(bigint,SCOPE_IDENTITY());

    SELECT
        @LearnerOwnedBefore=HASHBYTES
        (
            'SHA2_256',
            CONCAT(
                HocVienId,N'|',COALESCE(SourceProfileCode,N'<NULL>'),N'|',
                COALESCE(SourceMaDK,N'<NULL>'),N'|',CONVERT(int,IsDeleted),
                N'|',COALESCE(GhiChuNoiBo,N'<NULL>'),N'|',
                CONVERT(int,DaDoiChieuCCCD),N'|',CONVERT(int,DaInThe),N'|',
                CONVERT(int,DaTaoXML),N'|',COALESCE(CreatedBy,N'<NULL>'),
                N'|',COALESCE(UpdatedBy,N'<NULL>'),N'|',
                COALESCE(DeletedBy,N'<NULL>'),N'|',
                COALESCE(DeleteReason,N'<NULL>'))
        )
    FROM dbo.App_HocVien
    WHERE HocVienId=@LearnerId;

    UPDATE dbo.App_HocVien
    SET SourceVersion=N'UPDATE',
        LastSyncFromV2At=SYSUTCDATETIME()
    WHERE HocVienId=@LearnerId;
    SET @LearnerUpdate=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);

    SELECT
        @LearnerOwnedAfter=HASHBYTES
        (
            'SHA2_256',
            CONCAT(
                HocVienId,N'|',COALESCE(SourceProfileCode,N'<NULL>'),N'|',
                COALESCE(SourceMaDK,N'<NULL>'),N'|',CONVERT(int,IsDeleted),
                N'|',COALESCE(GhiChuNoiBo,N'<NULL>'),N'|',
                CONVERT(int,DaDoiChieuCCCD),N'|',CONVERT(int,DaInThe),N'|',
                CONVERT(int,DaTaoXML),N'|',COALESCE(CreatedBy,N'<NULL>'),
                N'|',COALESCE(UpdatedBy,N'<NULL>'),N'|',
                COALESCE(DeletedBy,N'<NULL>'),N'|',
                COALESCE(DeleteReason,N'<NULL>'))
        )
    FROM dbo.App_HocVien
    WHERE HocVienId=@LearnerId;
    SET @LearnerOwnedPreserved=
        CONVERT(bit,CASE WHEN @LearnerOwnedAfter=@LearnerOwnedBefore
                        THEN 1 ELSE 0 END);

    SET @Stage=N'VEHICLE';
    INSERT dbo.App_XeTap
    (
        BienSoXe,SourceProfileCode,SourceBienSoXe,NormalizedBienSoXe,
        SourceLifecycle,SourceCtVersion,SourceLastSeenAt,SourceOfTruth
    )
    VALUES
    (
        CONCAT(N'V7',@SyntheticSuffix),
        N'CSDT_OTO',
        CONCAT(N'V7',@SyntheticSuffix),
        CONCAT(N'V7',@SyntheticSuffix),
        N'ACTIVE',
        120,
        SYSUTCDATETIME(),
        N'V2'
    );
    SET @VehicleInsert=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);
    SET @VehicleId=CONVERT(bigint,SCOPE_IDENTITY());

    REVERT;
    UPDATE dbo.App_XeTap
    SET GhiChuNoiBo=N'RT03-V7-QLHV-OWNED-SENTINEL'
    WHERE XeTapId=@VehicleId;
    SELECT
        @VehicleOwnedBefore=HASHBYTES
        (
            'SHA2_256',
            CONCAT(
                XeTapId,N'|',COALESCE(GhiChuNoiBo,N'<NULL>'),N'|',
                CONVERT(nvarchar(33),CreatedAt,126),N'|',
                COALESCE(CreatedBy,N'<NULL>'),N'|',
                COALESCE(UpdatedBy,N'<NULL>'),N'|',
                COALESCE(DeletedBy,N'<NULL>'),N'|',
                COALESCE(DeleteReason,N'<NULL>'))
        )
    FROM dbo.App_XeTap
    WHERE XeTapId=@VehicleId;
    EXECUTE AS USER=@Principal;

    UPDATE dbo.App_XeTap
    SET SourceCtVersion=122,
        SourceLastSeenAt=SYSUTCDATETIME()
    WHERE XeTapId=@VehicleId;
    SET @VehicleUpdate=
        CONVERT(bit,CASE WHEN @@ROWCOUNT=1 THEN 1 ELSE 0 END);

    SELECT
        @VehicleOwnedAfter=HASHBYTES
        (
            'SHA2_256',
            CONCAT(
                XeTapId,N'|',COALESCE(GhiChuNoiBo,N'<NULL>'),N'|',
                CONVERT(nvarchar(33),CreatedAt,126),N'|',
                COALESCE(CreatedBy,N'<NULL>'),N'|',
                COALESCE(UpdatedBy,N'<NULL>'),N'|',
                COALESCE(DeletedBy,N'<NULL>'),N'|',
                COALESCE(DeleteReason,N'<NULL>'))
        )
    FROM dbo.App_XeTap
    WHERE XeTapId=@VehicleId;
    SET @VehicleOwnedPreserved=
        CONVERT(bit,CASE WHEN @VehicleOwnedAfter=@VehicleOwnedBefore
                        THEN 1 ELSE 0 END);

    SET @Stage=N'RECOVERY_BEGIN';
    EXEC dbo.usp_App_Rt03BeginFullConvergence
        @RecoveryId=@RecoveryId,
        @SourceProfileCode=N'CSDT_OTO',
        @SourceDatabaseGuid=@CheckpointGuid,
        @CheckpointBefore=@CheckpointBefore,
        @AnchorVersion=@AnchorVersion,
        @MappingFingerprint=@Hash64,
        @SourceSchemaFingerprint=@Hash64;

    SET @Stage=N'RECOVERY_DOMAINS';
    EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
        @RecoveryId,@DomainCode=N'COURSE',@SequenceOrder=1,
        @SourceRows=0,@InsertedRows=0,@UpdatedRows=0,@InactiveRows=0,
        @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=0,
        @VerificationHash=@Hash64;
    EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
        @RecoveryId,@DomainCode=N'TEACHER',@SequenceOrder=2,
        @SourceRows=0,@InsertedRows=0,@UpdatedRows=0,@InactiveRows=0,
        @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=0,
        @VerificationHash=@Hash64;
    EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
        @RecoveryId,@DomainCode=N'VEHICLE',@SequenceOrder=3,
        @SourceRows=0,@InsertedRows=0,@UpdatedRows=0,@InactiveRows=0,
        @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=0,
        @VerificationHash=@Hash64;
    EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
        @RecoveryId,@DomainCode=N'LEARNER',@SequenceOrder=4,
        @SourceRows=0,@InsertedRows=0,@UpdatedRows=0,@InactiveRows=0,
        @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=0,
        @VerificationHash=@Hash64;
    EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
        @RecoveryId,@DomainCode=N'RELATION',@SequenceOrder=5,
        @SourceRows=0,@InsertedRows=0,@UpdatedRows=0,@InactiveRows=0,
        @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=0,
        @VerificationHash=@Hash64;
    SET @Stage=N'RECOVERY_VERIFY';
    EXEC dbo.usp_App_Rt03VerifyFullConvergence @RecoveryId;
    SET @Stage=N'RECOVERY_FINALIZE';
    EXEC dbo.usp_App_Rt03FinalizeFullConvergence
        @RecoveryId=@RecoveryId,
        @VerificationHash=@Hash64;
    REVERT;

    SET @RecoveryMarkerCommitted=
        CONVERT(bit,CASE WHEN EXISTS
        (
            SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyMarker
            WHERE CycleId=@RecoveryId
              AND SourceChangeTrackingVersion=@AnchorVersion
        ) THEN 1 ELSE 0 END);
    SET @RecoveryCheckpointAdvanced=
        CONVERT(bit,CASE WHEN EXISTS
        (
            SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
            WHERE Mode=N'DIRECT_REALTIME_APPLY'
              AND EnvironmentId=N'PRODUCTION'
              AND SourceProfileCode=N'CSDT_OTO'
              AND CycleId=@RecoveryId
              AND SourceChangeTrackingVersion=@AnchorVersion
        ) THEN 1 ELSE 0 END);

    SET @Stage=N'ASSIGNMENT_BOUNDARY';
    SET @AssignmentBoundaryPreserved=
        CONVERT(bit,CASE WHEN
            OBJECT_ID(N'dbo.App_HocVien_PhanCong',N'U') IS NULL
            AND OBJECT_ID(N'dbo.App_KhoaHoc_NhomDaoTao',N'U') IS NULL
            AND OBJECT_ID(N'dbo.App_KhoaHoc_XeTap',N'U') IS NULL
            THEN 1
            WHEN NOT EXISTS
            (
                SELECT 1
                FROM sys.database_permissions permissionRow
                INNER JOIN sys.database_principals roleRow
                  ON roleRow.principal_id=
                     permissionRow.grantee_principal_id
                INNER JOIN sys.objects objectRow
                  ON permissionRow.class=1
                 AND objectRow.object_id=permissionRow.major_id
                WHERE roleRow.name=N'QLHV_RealtimeWorkerRole'
                  AND objectRow.name IN
                      (
                          N'App_HocVien_PhanCong',
                          N'App_KhoaHoc_NhomDaoTao',
                          N'App_KhoaHoc_XeTap'
                      )
            )
            THEN 1 ELSE 0 END);

    ROLLBACK TRANSACTION;
END TRY
BEGIN CATCH
    DECLARE @CaughtNumber int=ERROR_NUMBER(),
            @CaughtMessage nvarchar(2048)=ERROR_MESSAGE();
    PRINT CONCAT(
        N'RT03_V7_REHEARSAL_CAUGHT|',
        @Stage,N'|',@CaughtNumber,N'|',@CaughtMessage);
    IF USER_NAME()=@Principal REVERT;
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    DECLARE @RehearsalError nvarchar(2048)=
        CONCAT(
            N'RT03_V7_REHEARSAL_STAGE_FAILED|',
            @Stage,N'|',@CaughtNumber,N'|',@CaughtMessage);
    THROW 528106,@RehearsalError,1;
END CATCH;

SET @TransactionRollbackClean=
    CONVERT(bit,CASE WHEN
        @RowsBeforeCourse=(SELECT COUNT_BIG(*) FROM dbo.App_KhoaHoc)
        AND @RowsBeforeTeacher=(SELECT COUNT_BIG(*) FROM dbo.App_GiaoVien)
        AND @RowsBeforeLearner=(SELECT COUNT_BIG(*) FROM dbo.App_HocVien)
        AND @RowsBeforeVehicle=(SELECT COUNT_BIG(*) FROM dbo.App_XeTap)
        AND @RowsBeforeMarker=
            (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeApplyMarker)
        AND EXISTS
        (
            SELECT 1 FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
            WHERE Mode=N'DIRECT_REALTIME_APPLY'
              AND EnvironmentId=N'PRODUCTION'
              AND SourceProfileCode=N'CSDT_OTO'
              AND SourceDatabaseGuid=@CheckpointGuid
              AND SourceChangeTrackingVersion=@CheckpointBefore
        )
        AND NOT EXISTS
        (
            SELECT 1 FROM dbo.App_Rt03FullConvergenceSession
            WHERE RecoveryId=@RecoveryId
        )
        THEN 1 ELSE 0 END);

BEGIN TRY
    EXECUTE AS USER=@Principal;
    DELETE TOP(0) FROM dbo.App_KhoaHoc;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
    SET @TargetDeleteDenied=
        CONVERT(bit,CASE WHEN ERROR_NUMBER()=229 THEN 1 ELSE 0 END);
END CATCH;

USE [CSDL_OTO];
IF DB_ID()<>CONVERT(int,N'$(OtoDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=
             CONVERT(uniqueidentifier,N'$(OtoDatabaseGuid)')
   )
    THROW 528103,'RT03_V7_REHEARSAL_OTO_IDENTITY_REJECTED',1;
BEGIN TRY
    EXECUTE AS USER=@Principal;
    IF EXISTS(SELECT TOP(1) 1 FROM dbo.KhoaHoc)
        SET @OtoSourceRead=1;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
END CATCH;
BEGIN TRY
    EXECUTE AS USER=@Principal;
    UPDATE TOP(0) dbo.KhoaHoc SET MaKH=MaKH;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
    SET @OtoSourceUpdateDenied=
        CONVERT(bit,CASE WHEN ERROR_NUMBER()=229 THEN 1 ELSE 0 END);
END CATCH;
BEGIN TRY
    EXECUTE AS USER=@Principal;
    DELETE TOP(0) FROM dbo.KhoaHoc;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
    SET @OtoSourceDeleteDenied=
        CONVERT(bit,CASE WHEN ERROR_NUMBER()=229 THEN 1 ELSE 0 END);
END CATCH;

USE [CSDL_MOTO];
IF DB_ID()<>CONVERT(int,N'$(MotoDatabaseId)')
   OR NOT EXISTS
   (
       SELECT 1 FROM sys.database_recovery_status
       WHERE database_id=DB_ID()
         AND database_guid=
             CONVERT(uniqueidentifier,N'$(MotoDatabaseGuid)')
   )
    THROW 528104,'RT03_V7_REHEARSAL_MOTO_IDENTITY_REJECTED',1;
BEGIN TRY
    EXECUTE AS USER=@Principal;
    IF EXISTS(SELECT TOP(1) 1 FROM dbo.KhoaHoc)
        SET @MotoSourceRead=1;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
END CATCH;
BEGIN TRY
    EXECUTE AS USER=@Principal;
    UPDATE TOP(0) dbo.KhoaHoc SET MaKH=MaKH;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
    SET @MotoSourceUpdateDenied=
        CONVERT(bit,CASE WHEN ERROR_NUMBER()=229 THEN 1 ELSE 0 END);
END CATCH;
BEGIN TRY
    EXECUTE AS USER=@Principal;
    DELETE TOP(0) FROM dbo.KhoaHoc;
    REVERT;
END TRY
BEGIN CATCH
    IF USER_NAME()=@Principal REVERT;
    SET @MotoSourceDeleteDenied=
        CONVERT(bit,CASE WHEN ERROR_NUMBER()=229 THEN 1 ELSE 0 END);
END CATCH;

USE [QLHV_APP];
DECLARE @Results TABLE(TestName nvarchar(100) NOT NULL,Passed bit NOT NULL);
INSERT @Results VALUES
(N'COURSE_CT120_UPDATE',@CourseCt120Update),
(N'COURSE_CT122_UPDATE',@CourseCt122Update),
(N'COURSE_NO_CHANGE',@CourseNoChange),
(N'COURSE_QLHV_OWNED_PRESERVED',@CourseOwnedPreserved),
(N'LEARNER_INSERT',@LearnerInsert),
(N'LEARNER_UPDATE',@LearnerUpdate),
(N'LEARNER_QLHV_OWNED_PRESERVED',@LearnerOwnedPreserved),
(N'TEACHER_UPDATE',@TeacherUpdate),
(N'TEACHER_QLHV_OWNED_PRESERVED',@TeacherOwnedPreserved),
(N'VEHICLE_INSERT_FIXTURE',@VehicleInsert),
(N'VEHICLE_UPDATE',@VehicleUpdate),
(N'VEHICLE_QLHV_OWNED_PRESERVED',@VehicleOwnedPreserved),
(N'RECOVERY_MARKER_COMMITTED_BEFORE_ROLLBACK',@RecoveryMarkerCommitted),
(N'RECOVERY_CHECKPOINT_ADVANCED_BEFORE_ROLLBACK',@RecoveryCheckpointAdvanced),
(N'TARGET_DELETE_DENIED',@TargetDeleteDenied),
(N'OTO_SOURCE_READ',@OtoSourceRead),
(N'OTO_SOURCE_UPDATE_DENIED',@OtoSourceUpdateDenied),
(N'OTO_SOURCE_DELETE_DENIED',@OtoSourceDeleteDenied),
(N'MOTO_SOURCE_READ',@MotoSourceRead),
(N'MOTO_SOURCE_UPDATE_DENIED',@MotoSourceUpdateDenied),
(N'MOTO_SOURCE_DELETE_DENIED',@MotoSourceDeleteDenied),
(N'ASSIGNMENT_BOUNDARY_PRESERVED',@AssignmentBoundaryPreserved),
(N'OUTER_TRANSACTION_ROLLBACK_CLEAN',@TransactionRollbackClean);

SELECT TestName,Passed FROM @Results ORDER BY TestName;
IF EXISTS(SELECT 1 FROM @Results WHERE Passed=0)
    THROW 528105,'RT03_V7_PERMISSION_BEHAVIOR_REHEARSAL_FAILED',1;

SELECT N'RT03_V7_PERMISSION_BEHAVIOR_REHEARSAL_PASS' AS Result;
GO
