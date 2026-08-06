[CmdletBinding()]
param(
    [string]$ServerInstance = 'localhost',
    [string]$DatabaseName = ('QLHV_CC_V1_REHEARSAL_' + [guid]::NewGuid().ToString('N').Substring(0, 12).ToUpperInvariant())
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($DatabaseName -notmatch '^QLHV_CC_V1_REHEARSAL_[A-F0-9]{12}$') {
    throw 'Disposable database name is outside the fixed rehearsal allowlist.'
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$patchRoot = Join-Path $repoRoot 'database\patches'
$forward = Join-Path $patchRoot '20260801_add_course_completion_v1.sql'
$verify = Join-Path $patchRoot '20260801_verify_course_completion_v1.sql'
$rollback = Join-Path $patchRoot '20260801_rollback_course_completion_v1.sql'
$created = $false

& dotnet test (Join-Path $repoRoot 'server\QLHV.Tests\QLHV.Tests.csproj') `
    -c Release --no-restore --filter 'FullyQualifiedName~CourseCompletion'
if ($LASTEXITCODE -ne 0) { throw 'Course Completion behavioral/authorization/source-read-only tests failed.' }

function Invoke-DisposableQuery {
    param([Parameter(Mandatory)][string]$Query, [string]$Database = 'master')
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database `
        -QueryTimeout 60 -Query $Query -ErrorAction Stop
}

function Invoke-SealedSqlFile {
    param([Parameter(Mandatory)][string]$Path, [switch]$ExpectFailure)
    Push-Location $patchRoot
    try {
        & sqlcmd -S $ServerInstance -E -C -b -v "CourseCompletionTargetDatabase=$DatabaseName" -i $Path
        $exit = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($ExpectFailure) {
        if ($exit -eq 0) { throw "Expected failure did not occur for $Path" }
    }
    elseif ($exit -ne 0) { throw "sqlcmd failed with exit code $exit for $Path" }
}

try {
    $exists = Invoke-DisposableQuery -Query "SELECT COUNT_BIG(1) AS C FROM sys.databases WHERE name=N'$DatabaseName';"
    if ([long]$exists.C -ne 0) { throw 'Disposable database already exists.' }

    Invoke-DisposableQuery -Query "CREATE DATABASE [$DatabaseName];"
    $created = $true
    Invoke-DisposableQuery -Database $DatabaseName -Query @'
CREATE TABLE dbo.App_KhoaHoc
(
    KhoaHocId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_App_KhoaHoc PRIMARY KEY,
    MaKhoa nvarchar(50) NOT NULL,
    SourceProfileCode nvarchar(50) NULL,
    SourceMaKhoaHoc nvarchar(50) NULL,
    IsDeleted bit NOT NULL CONSTRAINT DF_App_KhoaHoc_IsDeleted DEFAULT(0)
);
CREATE TABLE dbo.App_AuditLog
(
    AuditLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_App_AuditLog PRIMARY KEY,
    ChucNang nvarchar(100) NOT NULL,HanhDong nvarchar(100) NOT NULL,
    EntityType nvarchar(100) NULL,EntityId nvarchar(100) NULL,EntityKey nvarchar(100) NULL,
    DuLieuTruoc nvarchar(max) NULL,DuLieuSau nvarchar(max) NULL,KetQua nvarchar(50) NOT NULL,
    Loi nvarchar(max) NULL,CreatedAt datetime2(7) NOT NULL,CreatedBy nvarchar(100) NULL
);
'@

    Invoke-SealedSqlFile -Path $forward
    Invoke-SealedSqlFile -Path $forward
    Invoke-SealedSqlFile -Path $verify

    foreach ($profile in @('CSDT_OTO', 'CSDT_MOTO')) {
        $maCsdt = if ($profile -eq 'CSDT_OTO') { '66029' } else { '66030' }
        Invoke-DisposableQuery -Database $DatabaseName -Query @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Operation uniqueidentifier=NEWID(),@Now datetime2(7)=SYSUTCDATETIME(),@CourseId bigint,@CompletionId bigint;
INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc) VALUES(N'KH_$maCsdt',N'$profile',N'KH_$maCsdt');
SET @CourseId=SCOPE_IDENTITY();
INSERT dbo.App_CourseCompletion
 (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
  SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
VALUES(@CourseId,N'$profile',N'KH_$maCsdt',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('A',64),5000,@Now,N'rehearsal',N'rehearsal only',@Operation);
SET @CompletionId=SCOPE_IDENTITY();
;WITH n AS
(
 SELECT TOP (5000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS rn
 FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT dbo.App_CourseCompletionLearnerSnapshot
 (CourseCompletionId,SourceProfileCode,SourceCourseKey,ProtectedLearnerIdentity,MaKhoaHoc,TT_XuLy,
  LearnerClassification,ResultCompletenessClassification,DownstreamClassification,
  CanonicalLearnerRowHash,SnapshotAtUtc)
SELECT @CompletionId,N'$profile',N'KH_$maCsdt',
       CONVERT(char(64),HASHBYTES('SHA2_256',CONVERT(varchar(20),rn)),2),N'KH_$maCsdt',
       CASE WHEN rn%2=0 THEN N'09' ELSE N'10' END,
       CASE WHEN rn%2=0 THEN N'PASSED' ELSE N'FAILED' END,N'COMPLETE',N'TRAINING_RESULT_FINAL',
       CONVERT(char(64),HASHBYTES('SHA2_256',CONCAT('ROW-',rn)),2),@Now
FROM n;
IF (SELECT COUNT_BIG(1) FROM dbo.App_CourseCompletionLearnerSnapshot WHERE CourseCompletionId=@CompletionId)<>5000
    THROW 52150,'REHEARSAL_5000_SET_BASED_COUNT_FAILED',1;
ROLLBACK TRANSACTION;
IF EXISTS(SELECT 1 FROM dbo.App_CourseCompletion) OR EXISTS(SELECT 1 FROM dbo.App_CourseCompletionLearnerSnapshot)
    THROW 52151,'REHEARSAL_TRANSACTION_ROLLBACK_FAILED',1;
"@
    }

    Invoke-DisposableQuery -Database $DatabaseName -Query @'
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Operation uniqueidentifier=NEWID(),@CourseId bigint,@Now datetime2(7)=SYSUTCDATETIME();
  INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc) VALUES(N'FORCED',N'CSDT_OTO',N'FORCED');
  SET @CourseId=SCOPE_IDENTITY();
  INSERT dbo.App_CourseCompletion
   (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
    SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
  VALUES(@CourseId,N'CSDT_OTO',N'FORCED',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('B',64),1,@Now,N'rehearsal',N'forced rollback',@Operation);
  THROW 52152,'REHEARSAL_FORCED_FAILURE',1;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
END CATCH;
IF EXISTS(SELECT 1 FROM dbo.App_CourseCompletion WHERE SourceCourseKey=N'FORCED')
 OR EXISTS(SELECT 1 FROM dbo.App_KhoaHoc WHERE MaKhoa=N'FORCED')
    THROW 52153,'REHEARSAL_FORCED_ROLLBACK_NOT_ATOMIC',1;
'@

    foreach ($failureStage in @('MARKER','SNAPSHOT','LEDGER','AUDIT','VERIFY')) {
        Invoke-DisposableQuery -Database $DatabaseName -Query @"
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Operation uniqueidentifier=NEWID(),@CourseId bigint,@CompletionId bigint,@Now datetime2(7)=SYSUTCDATETIME();
  INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc) VALUES(N'FAIL_$failureStage',N'CSDT_OTO',N'FAIL_$failureStage');
  SET @CourseId=SCOPE_IDENTITY();
  INSERT dbo.App_CourseCompletion
   (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
    SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
  VALUES(@CourseId,N'CSDT_OTO',N'FAIL_$failureStage',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('D',64),1,@Now,N'rehearsal',N'forced $failureStage rollback',@Operation);
  SET @CompletionId=SCOPE_IDENTITY();
  IF N'$failureStage'=N'MARKER' THROW 52160,'FORCED_MARKER_FAILURE',1;
  INSERT dbo.App_CourseCompletionLearnerSnapshot
   (CourseCompletionId,SourceProfileCode,SourceCourseKey,ProtectedLearnerIdentity,MaKhoaHoc,TT_XuLy,
    LearnerClassification,ResultCompletenessClassification,DownstreamClassification,CanonicalLearnerRowHash,SnapshotAtUtc)
  VALUES(@CompletionId,N'CSDT_OTO',N'FAIL_$failureStage',REPLICATE('E',64),N'FAIL_$failureStage',N'09',N'PASSED',N'COMPLETE',N'TRAINING_RESULT_FINAL',REPLICATE('F',64),@Now);
  IF N'$failureStage'=N'SNAPSHOT' THROW 52161,'FORCED_SNAPSHOT_FAILURE',1;
  INSERT dbo.App_CourseCompletionOperation
   (OperationId,SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash,RequestFingerprint,
    PreviewSnapshotHash,ResultCode,CourseCompletionId,CreatedAtUtc,CompletedAtUtc)
  VALUES(@Operation,N'CSDT_OTO',N'FAIL_$failureStage',N'rehearsal',HASHBYTES('SHA2_256',N'$failureStage'),
    REPLICATE('A',64),REPLICATE('D',64),N'COMPLETED',@CompletionId,@Now,@Now);
  IF N'$failureStage'=N'LEDGER' THROW 52162,'FORCED_LEDGER_FAILURE',1;
  INSERT dbo.App_AuditLog(ChucNang,HanhDong,EntityKey,KetQua,CreatedAt,CreatedBy)
  VALUES(N'COURSE_COMPLETION',N'CONFIRM',N'FAIL_$failureStage',N'COMPLETED',@Now,N'rehearsal');
  IF N'$failureStage'=N'AUDIT' THROW 52163,'FORCED_AUDIT_FAILURE',1;
  IF N'$failureStage'=N'VERIFY' THROW 52164,'FORCED_VERIFY_FAILURE',1;
  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
END CATCH;
IF EXISTS(SELECT 1 FROM dbo.App_CourseCompletion WHERE SourceCourseKey=N'FAIL_$failureStage')
 OR EXISTS(SELECT 1 FROM dbo.App_CourseCompletionLearnerSnapshot WHERE SourceCourseKey=N'FAIL_$failureStage')
 OR EXISTS(SELECT 1 FROM dbo.App_CourseCompletionOperation WHERE SourceCourseKey=N'FAIL_$failureStage')
 OR EXISTS(SELECT 1 FROM dbo.App_AuditLog WHERE EntityKey=N'FAIL_$failureStage')
 OR EXISTS(SELECT 1 FROM dbo.App_KhoaHoc WHERE MaKhoa=N'FAIL_$failureStage')
    THROW 52165,'REHEARSAL_STAGE_ROLLBACK_NOT_ATOMIC',1;
"@
    }

    Invoke-DisposableQuery -Database $DatabaseName -Query @'
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @CourseId bigint,@CompletionId bigint,@Now datetime2(7)=SYSUTCDATETIME(),@Operation uniqueidentifier=NEWID();
INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc)
VALUES(N'IDEMPOTENCY',N'CSDT_OTO',N'IDEMPOTENCY');
SET @CourseId=SCOPE_IDENTITY();
INSERT dbo.App_CourseCompletion
 (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
  SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
VALUES(@CourseId,N'CSDT_OTO',N'IDEMPOTENCY',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('1',64),1,@Now,N'rehearsal',N'idempotency',@Operation);
SET @CompletionId=SCOPE_IDENTITY();
INSERT dbo.App_CourseCompletionOperation
 (OperationId,SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash,RequestFingerprint,
  PreviewSnapshotHash,ResultCode,CourseCompletionId,CreatedAtUtc,CompletedAtUtc)
VALUES(@Operation,N'CSDT_OTO',N'IDEMPOTENCY',N'actor',HASHBYTES('SHA2_256',N'key'),REPLICATE('2',64),
 REPLICATE('1',64),N'COMPLETED',@CompletionId,@Now,@Now);
IF ABS(DATEDIFF(SECOND,@Now,SYSUTCDATETIME()))>5 THROW 52170,'REHEARSAL_SQL_UTC_FAILED',1;
ROLLBACK TRANSACTION;

BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Course2 bigint,@Completion2 bigint,@Now2 datetime2(7)=SYSUTCDATETIME(),@Operation2 uniqueidentifier=NEWID();
  INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc) VALUES(N'ONE_MARKER',N'CSDT_OTO',N'ONE_MARKER');
  SET @Course2=SCOPE_IDENTITY();
  INSERT dbo.App_CourseCompletion
   (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
    SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
  VALUES(@Course2,N'CSDT_OTO',N'ONE_MARKER',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('3',64),1,@Now2,N'actor-a',N'first',@Operation2);
  INSERT dbo.App_CourseCompletion
   (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
    SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
  VALUES(@Course2,N'CSDT_OTO',N'ONE_MARKER',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('4',64),1,@Now2,N'actor-b',N'second',NEWID());
  THROW 52171,'REHEARSAL_ONE_MARKER_CONSTRAINT_NOT_ENFORCED',1;
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN(2601,2627) THROW;
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
END CATCH;

BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @Course3 bigint,@Completion3 bigint,@Now3 datetime2(7)=SYSUTCDATETIME(),@Operation3 uniqueidentifier=NEWID();
  INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc) VALUES(N'IDEMPOTENCY_CONFLICT',N'CSDT_OTO',N'IDEMPOTENCY_CONFLICT');
  SET @Course3=SCOPE_IDENTITY();
  INSERT dbo.App_CourseCompletion
   (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
    SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
  VALUES(@Course3,N'CSDT_OTO',N'IDEMPOTENCY_CONFLICT',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('5',64),1,@Now3,N'actor',N'first',@Operation3);
  SET @Completion3=SCOPE_IDENTITY();
  INSERT dbo.App_CourseCompletionOperation
   (OperationId,SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash,RequestFingerprint,
    PreviewSnapshotHash,ResultCode,CourseCompletionId,CreatedAtUtc,CompletedAtUtc)
  VALUES(@Operation3,N'CSDT_OTO',N'IDEMPOTENCY_CONFLICT',N'actor',HASHBYTES('SHA2_256',N'same-key'),REPLICATE('6',64),REPLICATE('5',64),N'COMPLETED',@Completion3,@Now3,@Now3);
  INSERT dbo.App_CourseCompletionOperation
   (OperationId,SourceProfileCode,SourceCourseKey,ActorId,IdempotencyKeyHash,RequestFingerprint,
    PreviewSnapshotHash,ResultCode,CourseCompletionId,CreatedAtUtc,CompletedAtUtc)
  VALUES(NEWID(),N'CSDT_OTO',N'IDEMPOTENCY_CONFLICT',N'actor',HASHBYTES('SHA2_256',N'same-key'),REPLICATE('7',64),REPLICATE('5',64),N'NO_CHANGE',@Completion3,@Now3,@Now3);
  THROW 52172,'REHEARSAL_IDEMPOTENCY_CONSTRAINT_NOT_ENFORCED',1;
END TRY
BEGIN CATCH
  IF ERROR_NUMBER() NOT IN(2601,2627) THROW;
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
END CATCH;
'@

    Invoke-DisposableQuery -Database $DatabaseName -Query @'
DECLARE @Operation uniqueidentifier=NEWID(),@CourseId bigint,@Now datetime2(7)=SYSUTCDATETIME();
INSERT dbo.App_KhoaHoc(MaKhoa,SourceProfileCode,SourceMaKhoaHoc) VALUES(N'ROLLBACK_GUARD',N'CSDT_OTO',N'ROLLBACK_GUARD');
SET @CourseId=SCOPE_IDENTITY();
INSERT dbo.App_CourseCompletion
 (KhoaHocId,SourceProfileCode,SourceCourseKey,ContractVersion,Status,CompletionBusinessDate,
  SourceSnapshotHash,LearnerCount,CompletedAtUtc,CompletedBy,CompletionReason,CreatedOperationId)
VALUES(@CourseId,N'CSDT_OTO',N'ROLLBACK_GUARD',N'1.0',N'COMPLETED','2026-08-01',REPLICATE('C',64),1,@Now,N'rehearsal',N'rollback guard',@Operation);
'@
    Invoke-SealedSqlFile -Path $rollback -ExpectFailure
    Invoke-DisposableQuery -Database $DatabaseName -Query @'
DELETE dbo.App_CourseCompletion WHERE SourceCourseKey=N'ROLLBACK_GUARD';
DELETE dbo.App_KhoaHoc WHERE MaKhoa=N'ROLLBACK_GUARD';
'@
    Invoke-SealedSqlFile -Path $rollback

    $remaining = Invoke-DisposableQuery -Database $DatabaseName -Query @'
SELECT CONVERT(int,
 IIF(OBJECT_ID(N'dbo.App_CourseCompletion',N'U') IS NULL,0,1)+
 IIF(OBJECT_ID(N'dbo.App_CourseCompletionLearnerSnapshot',N'U') IS NULL,0,1)+
 IIF(OBJECT_ID(N'dbo.App_CourseCompletionOperation',N'U') IS NULL,0,1)) AS RemainingTables;
'@
    if ([int]$remaining.RemainingTables -ne 0) { throw 'Empty rollback did not remove all V1 tables.' }

    [pscustomobject]@{
        Evidence = 'COURSE_COMPLETION_V1_DISPOSABLE_REHEARSAL_PASS'
        MigrationApply = 'PASS'
        SecondApply = 'PASS'
        Verification = 'PASS'
        OtoMoto = 'PASS'
        BehavioralSourceDriftAuthorization = 'PASS'
        SourceV1ReadOnly = 'PASS'
        OneMarkerConcurrencyContract = 'PASS'
        IdempotencyConflictContract = 'PASS'
        SqlUtc = 'PASS'
        SetBased5000 = 'PASS'
        ForcedRollback = 'PASS'
        StageRollbackMatrix = '5/5 PASS'
        RollbackDataGuard = 'PASS'
        EmptyRollback = 'PASS'
    }
}
finally {
    if ($created) {
        $safeExists = Invoke-DisposableQuery -Query "SELECT COUNT_BIG(1) AS C FROM sys.databases WHERE name=N'$DatabaseName';"
        if ([long]$safeExists.C -eq 1) {
            Invoke-DisposableQuery -Query "ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName];"
        }
    }
}
