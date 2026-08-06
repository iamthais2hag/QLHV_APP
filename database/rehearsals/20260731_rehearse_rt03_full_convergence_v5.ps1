[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$ServerName = 'CSDLTTTC',

    [Parameter()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$migrationPath = [IO.Path]::GetFullPath((Join-Path `
    $PSScriptRoot '..\patches\20260731_add_rt03_full_convergence_recovery.sql'))
$rollbackPath = [IO.Path]::GetFullPath((Join-Path `
    $PSScriptRoot '..\patches\20260731_rollback_rt03_full_convergence_recovery.sql'))
$databaseName = (
    'QLHV_RT03_V5_REHEARSAL_' +
    [Guid]::NewGuid().ToString('N').Substring(0, 12).ToUpperInvariant())
$recoveryId = [Guid]::NewGuid()
$oldCycleId = [Guid]::NewGuid()
$sourceDatabaseGuid = [Guid]::NewGuid()
$hashA = 'A' * 64
$hashB = 'B' * 64
$steps = [Collections.Generic.List[object]]::new()
$databaseCreated = $false
$startedAtUtc = [DateTimeOffset]::UtcNow

if ($databaseName -notmatch '^QLHV_RT03_V5_REHEARSAL_[0-9A-F]{12}$' -or
    $databaseName -eq 'QLHV_APP') {
    throw 'Generated rehearsal database is outside the exact disposable allowlist.'
}

foreach ($path in @($migrationPath, $rollbackPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required recovery artifact is missing: $path"
    }
}

function Add-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Result,
        [Parameter(Mandatory)][string]$Evidence
    )
    $steps.Add([pscustomobject]@{
        name = $Name
        result = $Result
        evidence = $Evidence
    })
}

function Open-Connection {
    param([Parameter(Mandatory)][string]$Catalog)
    $builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = $ServerName
    $builder['Initial Catalog'] = $Catalog
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $false
    $builder['Pooling'] = $false
    $builder['Application Name'] = 'QLHV.RT03.V5.DisposableRehearsal'
    $connection = [Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open()
        return $connection
    }
    catch {
        $connection.Dispose()
        throw
    }
}

function Invoke-Sql {
    param(
        [Parameter(Mandatory)][Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory)][string]$Sql
    )
    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 120
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $command.Dispose()
    }
}

function Read-Scalar {
    param(
        [Parameter(Mandatory)][Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory)][string]$Sql
    )
    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 120
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}

function Invoke-Batches {
    param(
        [Parameter(Mandatory)][Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory)][string]$Sql
    )
    foreach ($batch in [regex]::Split($Sql, '(?im)^\s*GO\s*(?:--.*)?$')) {
        if (-not [string]::IsNullOrWhiteSpace($batch)) {
            Invoke-Sql -Connection $Connection -Sql $batch
        }
    }
}

function Convert-PatchForDisposableDatabase {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][Guid]$ActualDatabaseGuid
    )
    $sql = [IO.File]::ReadAllText($Path)
    if ([regex]::Matches(
            $sql,
            '(?im)^\s*USE\s+\[QLHV_APP\];\s*$').Count -ne 1) {
        throw "Patch must have exactly one sealed production USE statement: $Path"
    }
    $sql = [regex]::Replace(
        $sql,
        '(?im)^\s*USE\s+\[QLHV_APP\];\s*$',
        "USE [$databaseName];",
        1)
    $sql = $sql.Replace(
        "DB_NAME()<>N'QLHV_APP'",
        "DB_NAME()<>N'$databaseName'")
    $sql = $sql.Replace(
        '9C44B304-8A84-4D0D-9A82-19C7233FF6BB',
        $ActualDatabaseGuid.ToString().ToUpperInvariant())
    if ($sql -match '(?im)^\s*USE\s+\[QLHV_APP\];\s*$') {
        throw 'Production target remained in the disposable patch copy.'
    }
    return $sql
}

function Assert-SqlError {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][int]$ExpectedNumber,
        [Parameter(Mandatory)][string]$Label
    )
    $sqlException = $null
    try {
        & $Action
    }
    catch {
        $current = $_.Exception
        while ($null -ne $current) {
            if ($current -is [Data.SqlClient.SqlException]) {
                $sqlException = $current
                break
            }
            $current = $current.InnerException
        }
    }
    if ($null -eq $sqlException -or $sqlException.Number -ne $ExpectedNumber) {
        $observed = if ($null -eq $sqlException) { 'none' } else {
            $sqlException.Number.ToString()
        }
        throw "$Label expected SQL error $ExpectedNumber, observed $observed."
    }
}

$master = $null
$database = $null
$finalResult = 'FAIL'
$failure = $null
try {
    $master = Open-Connection -Catalog 'master'
    Invoke-Sql -Connection $master -Sql (
        "CREATE DATABASE [$databaseName];")
    $databaseCreated = $true
    $database = Open-Connection -Catalog $databaseName

    $actualDatabaseGuid = [Guid](Read-Scalar -Connection $database -Sql @'
SELECT database_guid
FROM sys.database_recovery_status
WHERE database_id=DB_ID();
'@)
    Invoke-Sql -Connection $database -Sql @"
CREATE TABLE dbo.App_QlhvDirectRealtimeApplyMarker
(
    CycleId uniqueidentifier NOT NULL PRIMARY KEY,
    SourceProfileCode nvarchar(50) NOT NULL,
    PlanHash char(64) NOT NULL,
    MarkerHash binary(32) NOT NULL,
    DispositionHash char(64) NOT NULL,
    SourceDatabaseGuid uniqueidentifier NOT NULL,
    SourceChangeTrackingVersion bigint NOT NULL,
    InsertedRows int NOT NULL,
    UpdatedRows int NOT NULL,
    RetainedRows int NOT NULL,
    PreservedQlhvOwnedHash char(64) NOT NULL,
    CommittedAtUtc datetime2(7) NOT NULL
);
CREATE TABLE dbo.App_QlhvDirectRealtimeApplyCheckpoint
(
    SourceProfileCode nvarchar(50) NOT NULL,
    Mode nvarchar(40) NOT NULL,
    MappingFingerprint char(64) NOT NULL,
    EnvironmentId nvarchar(40) NOT NULL,
    SourceDatabaseGuid uniqueidentifier NOT NULL,
    SourceChangeTrackingVersion bigint NOT NULL,
    CycleId uniqueidentifier NOT NULL UNIQUE
        REFERENCES dbo.App_QlhvDirectRealtimeApplyMarker(CycleId),
    PlanHash char(64) NOT NULL,
    MarkerHash binary(32) NOT NULL,
    PublishedAtUtc datetime2(7) NOT NULL,
    Version rowversion NOT NULL,
    PRIMARY KEY(SourceProfileCode,Mode,MappingFingerprint,EnvironmentId)
);
INSERT dbo.App_QlhvDirectRealtimeApplyMarker
(
    CycleId,SourceProfileCode,PlanHash,MarkerHash,DispositionHash,
    SourceDatabaseGuid,SourceChangeTrackingVersion,
    InsertedRows,UpdatedRows,RetainedRows,PreservedQlhvOwnedHash,CommittedAtUtc
)
VALUES
(
    '$oldCycleId',N'CSDT_OTO','$hashA',HASHBYTES('SHA2_256',N'OLD'),
    '$hashA','$sourceDatabaseGuid',25,0,0,0,'$hashA',SYSUTCDATETIME()
);
INSERT dbo.App_QlhvDirectRealtimeApplyCheckpoint
(
    SourceProfileCode,Mode,MappingFingerprint,EnvironmentId,
    SourceDatabaseGuid,SourceChangeTrackingVersion,CycleId,
    PlanHash,MarkerHash,PublishedAtUtc
)
VALUES
(
    N'CSDT_OTO',N'DIRECT_REALTIME_APPLY','$hashA',N'PRODUCTION',
    '$sourceDatabaseGuid',25,'$oldCycleId',
    '$hashA',HASHBYTES('SHA2_256',N'OLD'),SYSUTCDATETIME()
);
"@

    $migration = Convert-PatchForDisposableDatabase `
        -Path $migrationPath `
        -ActualDatabaseGuid $actualDatabaseGuid
    Invoke-Batches -Connection $database -Sql $migration
    Add-Step -Name 'schema_prerequisite' -Result 'PASS' `
        -Evidence 'Sealed migration installed only in allowlisted disposable database.'

    $classification = [string](Read-Scalar -Connection $database -Sql @'
SELECT CASE
    WHEN CONVERT(bigint,70)=CONVERT(bigint,70) THEN
        N'INCREMENTAL_VALID'
    ELSE N'UNCLASSIFIED'
END;
'@)
    if ($classification -ne 'INCREMENTAL_VALID') {
        throw 'Scenario A classification failed.'
    }
    Add-Step -Name 'scenario_a_checkpoint_equal_minimum' -Result 'PASS' `
        -Evidence 'checkpoint=minimum-valid remains incremental; no skip.'

    $classification = [string](Read-Scalar -Connection $database -Sql @'
SELECT CASE
    WHEN CONVERT(bigint,25)<CONVERT(bigint,70) THEN
        N'EXPIRED_REQUIRES_FULL_CONVERGENCE'
    ELSE N'UNCLASSIFIED'
END;
'@)
    if ($classification -ne 'EXPIRED_REQUIRES_FULL_CONVERGENCE') {
        throw 'Scenario B classification failed.'
    }
    Add-Step -Name 'scenario_b_expired_window' -Result 'PASS' `
        -Evidence 'checkpoint 25 below minimum-valid 70 selects full convergence.'

    Invoke-Sql -Connection $database -Sql @'
CREATE TABLE dbo.RehearsalSource
(
    ExactIdentity int NOT NULL PRIMARY KEY,
    SourceHash char(64) NOT NULL
);
CREATE TABLE dbo.RehearsalTarget
(
    ExactIdentity int NOT NULL PRIMARY KEY,
    SourceHash char(64) NOT NULL,
    QlhvOwned nvarchar(100) NOT NULL,
    IsDeleted bit NOT NULL
);
WITH rows AS
(
    SELECT TOP(5000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) RowNumber
    FROM sys.all_objects firstSet CROSS JOIN sys.all_objects secondSet
)
INSERT dbo.RehearsalSource(ExactIdentity,SourceHash)
SELECT RowNumber,CONVERT(char(64),HASHBYTES('SHA2_256',
    CONVERT(varbinary(20),RowNumber)),2)
FROM rows;

INSERT dbo.RehearsalTarget(ExactIdentity,SourceHash,QlhvOwned,IsDeleted)
SELECT ExactIdentity,
       CASE WHEN ExactIdentity=2 THEN REPLICATE('0',64) ELSE SourceHash END,
       CONCAT(N'QLHV-',ExactIdentity),0
FROM dbo.RehearsalSource
WHERE ExactIdentity<=2500;
INSERT dbo.RehearsalTarget VALUES(6000,REPLICATE('6',64),N'KEEP-ASSIGNMENT',0);

UPDATE targetRow
SET SourceHash=sourceRow.SourceHash,IsDeleted=0
FROM dbo.RehearsalTarget targetRow
INNER JOIN dbo.RehearsalSource sourceRow
  ON sourceRow.ExactIdentity=targetRow.ExactIdentity
WHERE targetRow.SourceHash<>sourceRow.SourceHash OR targetRow.IsDeleted=1;

INSERT dbo.RehearsalTarget(ExactIdentity,SourceHash,QlhvOwned,IsDeleted)
SELECT sourceRow.ExactIdentity,sourceRow.SourceHash,
       CONCAT(N'QLHV-',sourceRow.ExactIdentity),0
FROM dbo.RehearsalSource sourceRow
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.RehearsalTarget targetRow
    WHERE targetRow.ExactIdentity=sourceRow.ExactIdentity
);

UPDATE targetRow
SET IsDeleted=1
FROM dbo.RehearsalTarget targetRow
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.RehearsalSource sourceRow
    WHERE sourceRow.ExactIdentity=targetRow.ExactIdentity
);
'@
    $convergenceFailures = [int](Read-Scalar -Connection $database -Sql @'
SELECT
    (SELECT COUNT(*) FROM dbo.RehearsalSource) - 5000
    + (SELECT COUNT(*) FROM dbo.RehearsalTarget WHERE ExactIdentity<=5000
         AND (IsDeleted<>0 OR QlhvOwned<>CONCAT(N'QLHV-',ExactIdentity)))
    + (SELECT COUNT(*) FROM dbo.RehearsalTarget WHERE ExactIdentity=6000
         AND (IsDeleted<>1 OR QlhvOwned<>N'KEEP-ASSIGNMENT'));
'@)
    if ($convergenceFailures -ne 0) {
        throw "Scenario C convergence/ownership failures=$convergenceFailures."
    }
    Add-Step -Name 'scenario_c_set_based_full_convergence' -Result 'PASS' `
        -Evidence '5000 rows converged; stale row updated; QLHV-owned values preserved; missing row soft-inactivated.'

    Invoke-Sql -Connection $database -Sql @'
CREATE TABLE dbo.RehearsalCourse
(
    CourseCode nvarchar(20) NOT NULL PRIMARY KEY
);
CREATE TABLE dbo.RehearsalLearner
(
    LearnerCode nvarchar(20) NOT NULL PRIMARY KEY,
    CourseCode nvarchar(20) NOT NULL
        REFERENCES dbo.RehearsalCourse(CourseCode)
);
'@
    Assert-SqlError -ExpectedNumber 547 -Label 'learner-before-course' -Action {
        Invoke-Sql -Connection $database -Sql @'
INSERT dbo.RehearsalLearner VALUES(N'L1',N'C1');
'@
    }
    Invoke-Sql -Connection $database -Sql @'
INSERT dbo.RehearsalCourse VALUES(N'C1');
INSERT dbo.RehearsalLearner VALUES(N'L1',N'C1');
'@
    Add-Step -Name 'scenario_d_dependency_order' -Result 'PASS' `
        -Evidence 'Learner-before-course rejected; COURSE then LEARNER committed.'

    Invoke-Sql -Connection $database -Sql @'
CREATE TABLE dbo.RehearsalVehicle
(
    ExactIdentity nvarchar(30) NOT NULL PRIMARY KEY,
    IsActive bit NOT NULL,
    HasActiveAssignment bit NOT NULL
);
CREATE TABLE dbo.RehearsalManualReview
(
    ExactIdentity nvarchar(30) NOT NULL PRIMARY KEY,
    Classification nvarchar(60) NOT NULL
);
INSERT dbo.RehearsalVehicle VALUES(N'51A-ASSIGNED',1,1);
INSERT dbo.RehearsalManualReview
SELECT ExactIdentity,N'MISSING_ASSIGNED_VEHICLE'
FROM dbo.RehearsalVehicle
WHERE HasActiveAssignment=1;
'@
    $vehicleSafe = [int](Read-Scalar -Connection $database -Sql @'
SELECT COUNT(*) FROM dbo.RehearsalVehicle vehicleRow
INNER JOIN dbo.RehearsalManualReview reviewRow
  ON reviewRow.ExactIdentity=vehicleRow.ExactIdentity
WHERE vehicleRow.IsActive=1 AND vehicleRow.HasActiveAssignment=1
  AND reviewRow.Classification=N'MISSING_ASSIGNED_VEHICLE';
'@)
    if ($vehicleSafe -ne 1) {
        throw 'Scenario E assigned vehicle retention/manual review failed.'
    }
    Add-Step -Name 'scenario_e_ct_off_vehicle_snapshot' -Result 'PASS' `
        -Evidence 'Missing assigned vehicle retained active and classified manual review; no delete.'

    Invoke-Sql -Connection $database -Sql @'
CREATE TABLE dbo.RehearsalDuplicate
(
    ExactIdentity nvarchar(30) NOT NULL
);
INSERT dbo.RehearsalDuplicate VALUES(N'DUP'),(N'DUP');
'@
    $duplicateGroups = [int](Read-Scalar -Connection $database -Sql @'
SELECT COUNT(*) FROM
(
    SELECT ExactIdentity FROM dbo.RehearsalDuplicate
    GROUP BY ExactIdentity HAVING COUNT(*)>1
) duplicateRow;
'@)
    if ($duplicateGroups -ne 1) {
        throw 'Scenario F duplicate fail-closed detector failed.'
    }
    Add-Step -Name 'scenario_f_duplicate_identity' -Result 'PASS' `
        -Evidence 'Duplicate exact identity detected before target write.'

    Invoke-Sql -Connection $database -Sql @"
EXEC dbo.usp_App_Rt03BeginFullConvergence
    @RecoveryId='$recoveryId',
    @SourceProfileCode=N'CSDT_OTO',
    @SourceDatabaseGuid='$sourceDatabaseGuid',
    @CheckpointBefore=25,@AnchorVersion=70,
    @MappingFingerprint='$hashA',
    @SourceSchemaFingerprint='$hashB';
EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
    @RecoveryId='$recoveryId',@DomainCode=N'COURSE',@SequenceOrder=1,
    @SourceRows=5,@InsertedRows=1,@UpdatedRows=1,@InactiveRows=0,
    @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=3,
    @VerificationHash='$hashA';
"@
    Assert-SqlError -ExpectedNumber 528530 -Label 'partial-domain-verify' -Action {
        Invoke-Sql -Connection $database -Sql (
            "EXEC dbo.usp_App_Rt03VerifyFullConvergence " +
            "@RecoveryId='$recoveryId';")
    }
    $partialPublication = [int](Read-Scalar -Connection $database -Sql @"
SELECT
    (SELECT COUNT(*) FROM dbo.App_Rt03FullConvergenceMarker
     WHERE RecoveryId='$recoveryId')
    + CASE WHEN
        (SELECT SourceChangeTrackingVersion
         FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
         WHERE SourceProfileCode=N'CSDT_OTO')<>25
      THEN 1 ELSE 0 END;
"@)
    if ($partialPublication -ne 0) {
        throw 'Partial recovery published marker or checkpoint.'
    }

    Invoke-Sql -Connection $database -Sql @"
EXEC dbo.usp_App_Rt03BeginFullConvergence
    @RecoveryId='$recoveryId',
    @SourceProfileCode=N'CSDT_OTO',
    @SourceDatabaseGuid='$sourceDatabaseGuid',
    @CheckpointBefore=25,@AnchorVersion=72,
    @MappingFingerprint='$hashA',
    @SourceSchemaFingerprint='$hashB';
"@
    $resumeInvalidated = [int](Read-Scalar -Connection $database -Sql @"
SELECT CASE WHEN
    sessionRow.AttemptCount=2
    AND sessionRow.AnchorVersion=72
    AND sessionRow.Status=N'PREPARING'
    AND sessionRow.VerificationPassed=0
    AND domainRow.Status=N'BLOCKED'
    AND domainRow.CompletedAtUtc IS NULL
    THEN 1 ELSE 0 END
FROM dbo.App_Rt03FullConvergenceSession sessionRow
INNER JOIN dbo.App_Rt03FullConvergenceDomain domainRow
  ON domainRow.RecoveryId=sessionRow.RecoveryId
 AND domainRow.DomainCode=N'COURSE'
WHERE sessionRow.RecoveryId='$recoveryId';
"@)
    if ($resumeInvalidated -ne 1) {
        throw 'Resume did not force exact all-domain replay.'
    }

    $domains = @(
        @{ Code = 'COURSE'; Order = 1 },
        @{ Code = 'TEACHER'; Order = 2 },
        @{ Code = 'VEHICLE'; Order = 3 },
        @{ Code = 'LEARNER'; Order = 4 },
        @{ Code = 'RELATION'; Order = 5 }
    )
    foreach ($domain in $domains) {
        Invoke-Sql -Connection $database -Sql @"
EXEC dbo.usp_App_Rt03RecordFullConvergenceDomain
    @RecoveryId='$recoveryId',
    @DomainCode=N'$($domain.Code)',@SequenceOrder=$($domain.Order),
    @SourceRows=1,@InsertedRows=0,@UpdatedRows=0,@InactiveRows=0,
    @MissingRows=0,@ManualReviewRows=0,@NoChangeRows=1,
    @VerificationHash='$hashB';
"@
    }
    Invoke-Sql -Connection $database -Sql @"
EXEC dbo.usp_App_Rt03VerifyFullConvergence
    @RecoveryId='$recoveryId';
EXEC dbo.usp_App_Rt03FinalizeFullConvergence
    @RecoveryId='$recoveryId',@VerificationHash='$hashB';
"@
    $finalizeProof = [int](Read-Scalar -Connection $database -Sql @"
SELECT CASE WHEN
    sessionRow.Status=N'COMPLETED'
    AND sessionRow.VerificationPassed=1
    AND checkpointRow.SourceChangeTrackingVersion=72
    AND checkpointRow.CycleId='$recoveryId'
    AND checkpointRow.PlanHash='$hashB'
    AND recoveryMarker.RecoveryId='$recoveryId'
    AND applyMarker.CycleId='$recoveryId'
    AND applyMarker.MarkerHash=checkpointRow.MarkerHash
    THEN 1 ELSE 0 END
FROM dbo.App_Rt03FullConvergenceSession sessionRow
INNER JOIN dbo.App_Rt03FullConvergenceMarker recoveryMarker
  ON recoveryMarker.RecoveryId=sessionRow.RecoveryId
INNER JOIN dbo.App_QlhvDirectRealtimeApplyCheckpoint checkpointRow
  ON checkpointRow.SourceProfileCode=sessionRow.SourceProfileCode
INNER JOIN dbo.App_QlhvDirectRealtimeApplyMarker applyMarker
  ON applyMarker.CycleId=checkpointRow.CycleId
WHERE sessionRow.RecoveryId='$recoveryId';
"@)
    if ($finalizeProof -ne 1) {
        throw 'Atomic marker/checkpoint/session publication proof failed.'
    }
    Add-Step -Name 'scenario_g_failure_resume_atomic_finalize' -Result 'PASS' `
        -Evidence 'Partial verify rejected; checkpoint stayed 25; resume advanced anchor and forced all-domain replay; marker/apply-marker/checkpoint/session committed atomically at 72.'

    Invoke-Sql -Connection $database -Sql @"
ALTER DATABASE [$databaseName] SET CHANGE_TRACKING=ON
    (CHANGE_RETENTION=2 DAYS,AUTO_CLEANUP=ON);
CREATE TABLE dbo.RehearsalTracked
(
    Id int NOT NULL PRIMARY KEY,
    Value nvarchar(30) NOT NULL
);
ALTER TABLE dbo.RehearsalTracked ENABLE CHANGE_TRACKING
    WITH(TRACK_COLUMNS_UPDATED=ON);
INSERT dbo.RehearsalTracked VALUES(1,N'before-anchor');
"@
    $anchor = [long](Read-Scalar -Connection $database -Sql @'
SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION());
'@)
    Invoke-Sql -Connection $database -Sql @'
INSERT dbo.RehearsalTracked VALUES(2,N'after-anchor');
'@
    $postAnchor = [int](Read-Scalar -Connection $database -Sql @"
SELECT COUNT(*) FROM CHANGETABLE
    (CHANGES dbo.RehearsalTracked,$anchor) changeRow;
"@)
    $current = [long](Read-Scalar -Connection $database -Sql @'
SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION());
'@)
    if ($postAnchor -ne 1 -or $current -le $anchor) {
        throw 'Scenario H post-anchor replay evidence failed.'
    }
    Add-Step -Name 'scenario_h_post_anchor_event_pending' -Result 'PASS' `
        -Evidence "anchor=$anchor; current=$current; one event remains visible after anchor."

    $lockResource = 'QLHV:RT03:RECOVERY:CSDT_OTO'
    $heldLock = [int](Read-Scalar -Connection $database -Sql @"
DECLARE @result int;
EXEC @result=sys.sp_getapplock
    @Resource=N'$lockResource',@LockMode=N'Exclusive',
    @LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public';
SELECT @result;
"@)
    if ($heldLock -lt 0) {
        throw 'Could not establish the rehearsal profile lock holder.'
    }
    $contender = Open-Connection -Catalog $databaseName
    try {
        $contenderResult = [int](Read-Scalar -Connection $contender -Sql @"
DECLARE @result int;
EXEC @result=sys.sp_getapplock
    @Resource=N'$lockResource',@LockMode=N'Exclusive',
    @LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public';
SELECT @result;
"@)
        if ($contenderResult -ge 0) {
            throw 'Competing recovery unexpectedly acquired the profile lock.'
        }
    }
    finally {
        $contender.Dispose()
        Invoke-Sql -Connection $database -Sql @"
EXEC sys.sp_releaseapplock
    @Resource=N'$lockResource',@LockOwner=N'Session',
    @DbPrincipal=N'public';
"@
    }
    Add-Step -Name 'multiple_writer_lock_timeout' -Result 'PASS' `
        -Evidence 'Competing profile recovery received immediate lock rejection.'

    $rollback = Convert-PatchForDisposableDatabase `
        -Path $rollbackPath `
        -ActualDatabaseGuid $actualDatabaseGuid
    Assert-SqlError -ExpectedNumber 528551 -Label 'durable-evidence-rollback' -Action {
        Invoke-Batches -Connection $database -Sql $rollback
    }
    Add-Step -Name 'rollback_refuses_durable_evidence' -Result 'PASS' `
        -Evidence 'Rollback refused non-empty recovery session as designed.'

    $finalResult = 'PASS'
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    if ($null -ne $database) {
        $database.Dispose()
    }
    if ($databaseCreated) {
        if ($databaseName -notmatch '^QLHV_RT03_V5_REHEARSAL_[0-9A-F]{12}$') {
            throw 'Cleanup target left the disposable allowlist.'
        }
        if ($null -eq $master) {
            $master = Open-Connection -Catalog 'master'
        }
        Invoke-Sql -Connection $master -Sql @"
IF DB_ID(N'$databaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$databaseName];
END;
"@
    }
    if ($null -ne $master) {
        $master.Dispose()
    }

    $report = [ordered]@{
        result = $finalResult
        startedAtUtc = $startedAtUtc.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        server = $ServerName
        databaseClass = 'DISPOSABLE_ALLOWLISTED'
        databaseDropped = $databaseCreated
        productionDatabaseTouched = $false
        recoveryId = $recoveryId
        failure = $failure
        steps = $steps
    }
    $json = $report | ConvertTo-Json -Depth 8
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        $parent = Split-Path -Parent $resolvedOutput
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            [IO.Directory]::CreateDirectory($parent) | Out-Null
        }
        [IO.File]::WriteAllText(
            $resolvedOutput,
            $json,
            [Text.UTF8Encoding]::new($false))
    }
    Write-Output $json
}
