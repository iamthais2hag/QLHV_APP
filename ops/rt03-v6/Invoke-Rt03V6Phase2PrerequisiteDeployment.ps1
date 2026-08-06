[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineEvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$DatabaseBackupEvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$RuntimeBackupEvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,

    [switch]$ResumeAfterVehicleSetOptionFailure,

    [string]$PartialSchemaProofPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = 'D:\QLHV_APP\handoff\RT03_FULL_CONVERGENCE_RECOVERY_20260731_V6'
$apiRuntime = 'D:\QLHV_APP_RUNTIME\app'
$workerRuntime = 'D:\QLHV_APP_RUNTIME\app\worker'
$workerExecutable = Join-Path $workerRuntime 'QLHV.Worker.exe'
$workerService = 'QLHV_APP_RealtimeWorker'
$manifestPath = Join-Path $package 'MANIFEST.sha256'
$deploymentManifestPath = Join-Path $package 'DEPLOYMENT_MANIFEST.json'
$preflightScript = Join-Path $package 'Invoke-Rt03V6FreshPreflight.ps1'
$immediatePreflightPath =
    Join-Path $EvidenceDirectory '04_immediate_predeployment_preflight.json'
$resultFileName = if ($ResumeAfterVehicleSetOptionFailure) {
    '14_prerequisite_binary_deployment_resume.json'
}
else {
    '08_prerequisite_binary_deployment.json'
}
$failureFileName = if ($ResumeAfterVehicleSetOptionFailure) {
    '14_prerequisite_binary_deployment_resume_failure.json'
}
else {
    '08_prerequisite_binary_deployment_failure.json'
}
$resultPath = Join-Path $EvidenceDirectory $resultFileName
$failurePath = Join-Path $EvidenceDirectory $failureFileName
$writerResources = @(
    'QLHV:CSDT_AUTO_SYNC',
    'QLHV:CSDT_OPERATIONS:OTO',
    'QLHV:CSDT_OPERATIONS:MOTO'
)

$baseline = Get-Content -LiteralPath $BaselineEvidencePath -Raw |
    ConvertFrom-Json
$databaseBackup = Get-Content -LiteralPath $DatabaseBackupEvidencePath -Raw |
    ConvertFrom-Json
$runtimeBackup = Get-Content -LiteralPath $RuntimeBackupEvidencePath -Raw |
    ConvertFrom-Json
$deployment = Get-Content -LiteralPath $deploymentManifestPath -Raw |
    ConvertFrom-Json

$lockConnection = $null
$heldLocks = [Collections.Generic.List[string]]::new()
$schemaResults = [Collections.Generic.List[object]]::new()
$releaseResults = [Collections.Generic.List[object]]::new()

function New-SqlConnection {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$ApplicationName
    )

    $builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = 'CSDLTTTC'
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $false
    $builder['Pooling'] = $false
    $builder['Connect Timeout'] = 15
    $builder['Application Name'] = $ApplicationName
    return [Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
}

function Invoke-SqlTable {
    param(
        [Parameter(Mandatory = $true)]
        [Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $command = $Connection.CreateCommand()
    try {
        $command.CommandTimeout = 60
        $command.CommandText = $Sql
        $table = [Data.DataTable]::new()
        $reader = $command.ExecuteReader()
        try {
            $table.Load($reader)
        }
        finally {
            $reader.Dispose()
        }
        return ,$table
    }
    finally {
        $command.Dispose()
    }
}

function Get-SourceCtAudit {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Profile,
        [Parameter(Mandatory = $true)][long]$Checkpoint,
        [Parameter(Mandatory = $true)][Guid]$ExpectedDatabaseGuid
    )

    $connection = New-SqlConnection `
        -Database $Database `
        -ApplicationName 'QLHV RT03 V6 Dynamic CT Source Gate'
    try {
        $connection.Open()
        $table = Invoke-SqlTable `
            -Connection $connection `
            -Sql @"
WITH Expected(TableName) AS
(
    SELECT value FROM
    (VALUES
        (N'DM_DVHC'),(N'DM_HangDT'),(N'GiaoVien'),(N'KhoaHoc'),
        (N'KhoaHoc_GiaoVien'),(N'NguoiLX'),(N'NguoiLX_HoSo'),(N'XeTap')
    ) item(value)
)
SELECT DB_NAME() DatabaseName,
       CONVERT(nvarchar(36),identityRow.database_guid) DatabaseGuid,
       CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) CurrentVersion,
       expected.TableName,
       CONVERT(bit,CASE WHEN tableRow.object_id IS NULL THEN 0 ELSE 1 END)
           TableExists,
       CONVERT(bit,CASE WHEN tracking.object_id IS NULL THEN 0 ELSE 1 END)
           ChangeTrackingEnabled,
       CONVERT(bigint,CASE WHEN tracking.object_id IS NULL THEN NULL
            ELSE CHANGE_TRACKING_MIN_VALID_VERSION(tableRow.object_id) END)
           MinimumValidVersion
FROM Expected expected
CROSS JOIN sys.database_recovery_status identityRow
LEFT JOIN sys.tables tableRow
  ON tableRow.schema_id=SCHEMA_ID(N'dbo')
 AND tableRow.name=expected.TableName
LEFT JOIN sys.change_tracking_tables tracking
  ON tracking.object_id=tableRow.object_id
WHERE identityRow.database_id=DB_ID()
ORDER BY expected.TableName;
"@
        if ($table.Rows.Count -ne 8) {
            throw "Dynamic CT audit table count is not exact: $Profile."
        }

        $rows = @()
        foreach ($row in $table.Rows) {
            if ([string]$row.DatabaseName -ne $Database -or
                [Guid][string]$row.DatabaseGuid -ne $ExpectedDatabaseGuid -or
                -not [bool]$row.TableExists) {
                throw "Dynamic CT source identity/table gate failed: $Profile."
            }

            $minimumValid = if ($row.MinimumValidVersion -is [DBNull]) {
                $null
            }
            else {
                [long]$row.MinimumValidVersion
            }
            $classification = if ([bool]$row.ChangeTrackingEnabled) {
                if ($null -eq $minimumValid) {
                    throw "CT minimum-valid unavailable: $Profile/$($row.TableName)."
                }
                if ($Checkpoint -lt $minimumValid) {
                    'EXPIRED_REQUIRES_FULL_CONVERGENCE'
                }
                else {
                    'INCREMENTAL_VALID'
                }
            }
            else {
                'CT_DISABLED_REQUIRES_SNAPSHOT'
            }
            $rows += [pscustomobject]@{
                Table = "dbo.$($row.TableName)"
                ChangeTrackingEnabled = [bool]$row.ChangeTrackingEnabled
                CurrentVersion = [long]$row.CurrentVersion
                MinimumValidVersion = $minimumValid
                CommittedCheckpoint = $Checkpoint
                Classification = $classification
            }
        }

        return [pscustomobject]@{
            SourceProfileCode = $Profile
            SourceDatabase = $Database
            SourceDatabaseGuid = $ExpectedDatabaseGuid
            CurrentVersion = [long]$table.Rows[0].CurrentVersion
            CommittedCheckpoint = $Checkpoint
            Tables = $rows
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Assert-WorkerStopped {
    $service = Get-CimInstance Win32_Service `
        -Filter "Name='$workerService'"
    if ($null -eq $service -or
        $service.State -ne 'Stopped' -or
        [int]$service.ProcessId -ne 0) {
        throw 'Worker is not Stopped/PID 0.'
    }
    if (([string]$service.PathName).Trim('"') -ine $workerExecutable) {
        throw 'Worker service path changed.'
    }
    return $service
}

function Assert-TimeApiHealthy {
    $response = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri 'http://127.0.0.1:8088/api/system/runtime-status' `
        -TimeoutSec 45
    $status = $response.Content | ConvertFrom-Json
    if ([int]$response.StatusCode -ne 200 -or
        [string]$status.timeContractVersion -ne '1.0' -or
        $null -eq $status.time.lastSyncError -or
        [string]$status.time.health -ne 'HEALTHY' -or
        [int]$status.time.lastSyncError -ne 0 -or
        [bool]$status.autoSyncPolling.enabled -or
        [bool]$status.autoSyncPolling.isPolling) {
        throw 'API TimeHealth/Auto Sync gate failed.'
    }
    return $status
}

function Assert-PackageManifest {
    $lines = @(Get-Content -LiteralPath $manifestPath)
    $failures = @()
    foreach ($line in $lines) {
        if ($line -notmatch '^([0-9A-Fa-f]{64}) \*(.+)$') {
            $failures += "FORMAT:$line"
            continue
        }
        $relativePath = $Matches[2]
        $path = Join-Path $package $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $failures += "MISSING:$relativePath"
            continue
        }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual -ne $Matches[1].ToUpperInvariant()) {
            $failures += "HASH:$relativePath"
        }
    }
    if ($lines.Count -ne 189 -or $failures.Count -ne 0) {
        throw "Package manifest failed: files=$($lines.Count), failures=$($failures.Count)."
    }
}

function Invoke-ImmediatePreflight {
    $preflightConsoleFileName = if ($ResumeAfterVehicleSetOptionFailure) {
        '11_resume_immediate_predeployment_console.txt'
    }
    else {
        '04_immediate_predeployment_console.txt'
    }
    $consolePath = Join-Path $EvidenceDirectory $preflightConsoleFileName
    $preflightOutputPath = if ($ResumeAfterVehicleSetOptionFailure) {
        Join-Path $EvidenceDirectory '11_resume_immediate_predeployment.json'
    }
    else {
        $immediatePreflightPath
    }
    & powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $preflightScript `
        -SampleCount 3 `
        -ApiContractMode Required `
        -OutputPath $preflightOutputPath 2>&1 |
        Out-File -LiteralPath $consolePath -Encoding utf8 -Width 65535
    $preflightExitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $preflightOutputPath -PathType Leaf)) {
        throw 'Immediate pre-deployment preflight failed.'
    }

    $fresh = Get-Content -LiteralPath $preflightOutputPath -Raw |
        ConvertFrom-Json
    if ($ResumeAfterVehicleSetOptionFailure) {
        if ($preflightExitCode -ne 20 -or
            [string]$fresh.PreflightMode -ne
                'UNCHANGED_PRODUCTION_BASELINE' -or
            [string]$fresh.TimeAuthority.TimeHealth -ne 'HEALTHY' -or
            [int]$fresh.TimeAuthority.LastSyncErrorCode -ne 0 -or
            $fresh.ApiContract.Healthy -ne $true -or
            [int]$fresh.ApiContract.ExitCode -ne 0 -or
            -not [bool]$fresh.Gates.WorkerStoppedPidZero -or
            -not [bool]$fresh.Gates.WorkerPathExact -or
            -not [bool]$fresh.Gates.TargetIdentityExact -or
            -not [bool]$fresh.Gates.AutoSyncAndFullSyncInactive -or
            -not [bool]$fresh.Gates.CheckpointsStableDuringAudit -or
            -not [bool]$fresh.Gates.EveryDomainClassified -or
            [bool]$fresh.Runtime.AssignmentSchemaPresent -or
            -not [bool]$fresh.Runtime.CoursePrerequisitePresent -or
            -not [bool]$fresh.Runtime.VehicleMappingPresent -or
            [bool]$fresh.Runtime.RecoverySchemaPresent -or
            @($fresh.Blockers).Count -ne 1) {
            throw 'Resume preflight has a blocker beyond the sealed partial schema.'
        }
        return
    }

    if ($preflightExitCode -ne 0 -or
        -not [bool]$fresh.ProductionRecoveryAllowed -or
        [string]$fresh.PreflightMode -ne 'UNCHANGED_PRODUCTION_BASELINE' -or
        [string]$fresh.TimeAuthority.TimeHealth -ne 'HEALTHY' -or
        [int]$fresh.TimeAuthority.LastSyncErrorCode -ne 0 -or
        $fresh.ApiContract.Healthy -ne $true -or
        [int]$fresh.ApiContract.ExitCode -ne 0) {
        throw 'Immediate pre-deployment Time/StrictMode gate failed.'
    }
}

function Acquire-WriterLock {
    param([Parameter(Mandatory = $true)][string]$Resource)

    $command = $lockConnection.CreateCommand()
    try {
        $command.CommandTimeout = 10
        $command.CommandText = @'
DECLARE @Result int;
EXEC @Result=sys.sp_getapplock
    @Resource=@Resource,
    @LockMode=N'Exclusive',
    @LockOwner=N'Session',
    @LockTimeout=0,
    @DbPrincipal=N'public';
SELECT @Result LockResult;
'@
        [void]$command.Parameters.Add(
            '@Resource',
            [Data.SqlDbType]::NVarChar,
            255)
        $command.Parameters['@Resource'].Value = $Resource
        $result = [int]$command.ExecuteScalar()
        if ($result -lt 0) {
            throw "Writer lock unavailable: $Resource ($result)."
        }
        $heldLocks.Add($Resource)
        return $result
    }
    finally {
        $command.Dispose()
    }
}

function Release-HeldLocks {
    if ($null -eq $lockConnection -or
        $lockConnection.State -ne [Data.ConnectionState]::Open) {
        return
    }

    for ($index = $heldLocks.Count - 1; $index -ge 0; $index--) {
        $resource = $heldLocks[$index]
        $command = $lockConnection.CreateCommand()
        try {
            $command.CommandTimeout = 10
            $command.CommandText = @'
DECLARE @Result int;
EXEC @Result=sys.sp_releaseapplock
    @Resource=@Resource,
    @LockOwner=N'Session',
    @DbPrincipal=N'public';
SELECT @Result LockResult;
'@
            [void]$command.Parameters.Add(
                '@Resource',
                [Data.SqlDbType]::NVarChar,
                255)
            $command.Parameters['@Resource'].Value = $resource
            $result = [int]$command.ExecuteScalar()
            $releaseResults.Add([pscustomobject]@{
                Resource = $resource
                Result = $result
            })
            if ($result -lt 0) {
                throw "Writer lock release failed: $resource ($result)."
            }
        }
        finally {
            $command.Dispose()
        }
    }
    $heldLocks.Clear()
}

function Assert-LockedBaseline {
    [void](Assert-WorkerStopped)
    [void](Assert-TimeApiHealthy)

    $target = Invoke-SqlTable -Connection $lockConnection -Sql @'
SELECT DB_NAME() DatabaseName,DB_ID() DatabaseId,
       CONVERT(nvarchar(36),database_guid) DatabaseGuid,
       (SELECT COUNT(1) FROM dbo.App_QlhvAutoSyncRun
        WHERE ActiveSlot=1 AND Status IN(N'QUEUED',N'RUNNING')
          AND CompletedAtUtc IS NULL) ActiveAutoSyncRuns,
       (SELECT COUNT(1) FROM dbo.App_QlhvSyncOperationHistory
        WHERE Status IN(N'QUEUED',N'RUNNING')) ActiveOperations,
       CASE WHEN OBJECT_ID(N'dbo.App_AssignmentOperation',N'U') IS NULL
            THEN 0 ELSE 1 END AssignmentSchemaPresent,
       CASE WHEN COL_LENGTH(N'dbo.App_XeTap',N'SourceProfileCode') IS NULL
            THEN 0 ELSE 1 END VehicleMappingPresent,
       CASE WHEN OBJECT_ID(N'dbo.App_Rt03FullConvergenceSession',N'U') IS NULL
            THEN 0 ELSE 1 END RecoverySchemaPresent,
       CASE WHEN OBJECT_ID(N'dbo.UQ_App_KhoaHoc_MaKhoa',N'UQ') IS NULL
            THEN 0 ELSE 1 END GlobalCourseUniquePresent,
       CASE WHEN EXISTS
       (
           SELECT 1 FROM sys.indexes
           WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc')
             AND name=N'UX_App_KhoaHoc_SourceIdentity'
             AND is_unique=1 AND is_disabled=0
       ) THEN 1 ELSE 0 END CourseSourceIdentityIndexPresent,
       CASE WHEN EXISTS
       (
           SELECT 1 FROM sys.indexes
           WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc')
             AND name=N'IX_App_KhoaHoc_SourceProfile_MaKhoa'
             AND is_disabled=0
       ) THEN 1 ELSE 0 END ProfileCourseLookupPresent
       ,
       (SELECT COUNT(1)
        FROM sys.columns
        WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
          AND name IN
          (
              N'SourceProfileCode',N'SourceBienSoXe',
              N'NormalizedBienSoXe',N'NormalizedSoDK',
              N'NormalizedSoKhung',N'NormalizedSoDongCo',
              N'MaCSDT',N'MaSoGTVT',N'SourceRowHash',
              N'SourceTrangThai',N'SourceLifecycle',N'SourceCtVersion',
              N'SourceLastSeenAt',N'SourceMissingSince',
              N'ManualReviewCode',N'ManualReviewAt',
              N'SourceCreatedBy',N'SourceUpdatedBy',
              N'SourceCreatedAt',N'SourceUpdatedAt',
              N'SourceImagePathHash',N'SourceMaFileTiepNhanXml',
              N'SourceThoiGianTiepNhanXml'
          )) VehicleSourceColumnCount,
       (SELECT COUNT(1)
        FROM sys.check_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.App_XeTap')
          AND is_disabled=0 AND is_not_trusted=0
          AND name IN
          (
              N'CK_App_XeTap_SourceIdentityPair',
              N'CK_App_XeTap_SourceLifecycle',
              N'CK_App_XeTap_SourceRowHash',
              N'CK_App_XeTap_SourceImagePathHash',
              N'CK_App_XeTap_SourceMissing',
              N'CK_App_XeTap_ManualReviewPair'
          )) VehicleTrustedConstraintCount,
       (SELECT COUNT(1)
        FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.App_XeTap')
          AND name IN
          (
              N'UX_App_XeTap_SourceIdentity',
              N'IX_App_XeTap_NormalizedBienSoXe',
              N'IX_App_XeTap_SourceLifecycle'
          )) VehicleNewIndexCount,
       (SELECT COUNT(1)
        FROM sys.tables
        WHERE name IN
          (
              N'App_XeTap_RealtimeCheckpoint',
              N'App_XeTap_RealtimeEvent',
              N'App_XeTap_RealtimeManualReview'
          )) VehicleRealtimeTableCount,
       (SELECT COUNT_BIG(*)
        FROM dbo.App_XeTap
        WHERE SourceProfileCode IS NOT NULL
           OR SourceBienSoXe IS NOT NULL) VehiclePopulatedSourceRows
FROM sys.database_recovery_status
WHERE database_id=DB_ID();
'@
    if ($target.Rows.Count -ne 1) {
        throw 'Locked target identity is not exact.'
    }
    $row = $target.Rows[0]
    $schemaMatches = if ($ResumeAfterVehicleSetOptionFailure) {
        [int]$row.AssignmentSchemaPresent -eq 0 -and
        [int]$row.VehicleMappingPresent -eq 1 -and
        [int]$row.RecoverySchemaPresent -eq 0 -and
        [int]$row.GlobalCourseUniquePresent -eq 0 -and
        [int]$row.CourseSourceIdentityIndexPresent -eq 1 -and
        [int]$row.ProfileCourseLookupPresent -eq 1 -and
        [int]$row.VehicleSourceColumnCount -eq 23 -and
        [int]$row.VehicleTrustedConstraintCount -eq 6 -and
        [int]$row.VehicleNewIndexCount -eq 0 -and
        [int]$row.VehicleRealtimeTableCount -eq 0 -and
        [long]$row.VehiclePopulatedSourceRows -eq 0
    }
    else {
        [int]$row.AssignmentSchemaPresent -eq 0 -and
        [int]$row.VehicleMappingPresent -eq 0 -and
        [int]$row.RecoverySchemaPresent -eq 0 -and
        [int]$row.GlobalCourseUniquePresent -eq 1 -and
        [int]$row.CourseSourceIdentityIndexPresent -eq 1 -and
        [int]$row.ProfileCourseLookupPresent -eq 0
    }
    if ([string]$row.DatabaseName -ne 'QLHV_APP' -or
        [int]$row.DatabaseId -ne 12 -or
        [string]$row.DatabaseGuid -ne
            '9C44B304-8A84-4D0D-9A82-19C7233FF6BB' -or
        [int]$row.ActiveAutoSyncRuns -ne 0 -or
        [int]$row.ActiveOperations -ne 0 -or
        -not $schemaMatches) {
        throw 'Locked production baseline changed before DDL.'
    }

    $checkpoints = Invoke-SqlTable -Connection $lockConnection -Sql @'
SELECT SourceProfileCode,
       CONVERT(bigint,SourceChangeTrackingVersion) CheckpointVersion
FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
WHERE Mode=N'DIRECT_REALTIME_APPLY'
  AND EnvironmentId=N'PRODUCTION'
ORDER BY SourceProfileCode;
'@
    foreach ($profile in @('CSDT_OTO', 'CSDT_MOTO')) {
        $actual = @(
            $checkpoints.Rows |
                Where-Object {
                    [string]$_.SourceProfileCode -eq $profile
                }
        )
        $expected = @(
            $baseline.CheckpointsAfter |
                Where-Object {
                    $_.SourceProfileCode -eq $profile
                }
        )
        if ($actual.Count -ne 1 -or
            $expected.Count -ne 1 -or
            [long]$actual[0].CheckpointVersion -ne
                [long]$expected[0].CheckpointVersion) {
            throw "Locked checkpoint changed: $profile"
        }
    }

    $otoCheckpoint = [long](@(
        $checkpoints.Rows |
            Where-Object SourceProfileCode -eq 'CSDT_OTO'
    )[0].CheckpointVersion)
    $motoCheckpoint = [long](@(
        $checkpoints.Rows |
            Where-Object SourceProfileCode -eq 'CSDT_MOTO'
    )[0].CheckpointVersion)
    $otoAudit = Get-SourceCtAudit `
        -Database 'CSDL_OTO' `
        -Profile 'CSDT_OTO' `
        -Checkpoint $otoCheckpoint `
        -ExpectedDatabaseGuid '9A8B9BC1-18F3-4823-8123-3DC197A9D540'
    $motoAudit = Get-SourceCtAudit `
        -Database 'CSDL_MOTO' `
        -Profile 'CSDT_MOTO' `
        -Checkpoint $motoCheckpoint `
        -ExpectedDatabaseGuid '308BDDA8-80F3-4ACB-9836-578D80A9E98E'

    return [pscustomobject]@{
        CheckpointOTO = $otoCheckpoint
        CheckpointMOTO = $motoCheckpoint
        BaselineVersionOTO = [long]$baseline.SourceVersionsAfter.CSDT_OTO
        BaselineVersionMOTO = [long]$baseline.SourceVersionsAfter.CSDT_MOTO
        LockedCurrentVersionOTO = $otoAudit.CurrentVersion
        LockedCurrentVersionMOTO = $motoAudit.CurrentVersion
        SourceAudits = @($otoAudit, $motoAudit)
        DynamicCurrentVersionIncreaseAllowed = $true
        ActiveAutoSyncRuns = [int]$row.ActiveAutoSyncRuns
        ActiveOperations = [int]$row.ActiveOperations
    }
}

function Invoke-SchemaScript {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$OutputFile,
        [string[]]$Variables = @()
    )

    $path = Join-Path $package $RelativePath
    $arguments = @(
        '-S', 'CSDLTTTC',
        '-d', 'QLHV_APP',
        '-E',
        '-b',
        '-I',
        '-r', '1',
        '-i', $path,
        '-o', $OutputFile
    )
    if ($Variables.Count -gt 0) {
        $arguments += '-v'
        $arguments += $Variables
    }
    & sqlcmd.exe @arguments
    $exitCode = $LASTEXITCODE
    $schemaResults.Add([pscustomobject]@{
        File = $RelativePath
        Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        ExitCode = $exitCode
        OutputFile = $OutputFile
    })
    if ($exitCode -ne 0) {
        throw "Schema prerequisite failed: $RelativePath (exit $exitCode)."
    }
}

if ($ResumeAfterVehicleSetOptionFailure) {
    if ([string]$baseline.PreflightMode -ne
            'UNCHANGED_PRODUCTION_BASELINE' -or
        [string]$baseline.TimeAuthority.TimeHealth -ne 'HEALTHY' -or
        [int]$baseline.TimeAuthority.LastSyncErrorCode -ne 0 -or
        $baseline.ApiContract.Healthy -ne $true -or
        [int]$baseline.ApiContract.ExitCode -ne 0 -or
        -not [bool]$baseline.Gates.WorkerStoppedPidZero -or
        -not [bool]$baseline.Gates.WorkerPathExact -or
        -not [bool]$baseline.Gates.TargetIdentityExact -or
        -not [bool]$baseline.Gates.AutoSyncAndFullSyncInactive -or
        -not [bool]$baseline.WriterLease.Available -or
        -not [bool]$baseline.Gates.EveryDomainClassified -or
        @($baseline.Blockers).Count -ne 1) {
        throw 'Fresh partial-schema baseline has a non-schema blocker.'
    }
}
elseif (-not [bool]$baseline.ProductionRecoveryAllowed -or
    [string]$baseline.PreflightMode -ne 'UNCHANGED_PRODUCTION_BASELINE') {
    throw 'Baseline evidence is not approved for unchanged-production deployment.'
}
if (-not [bool]$databaseBackup.CopyOnly -or
    -not [bool]$databaseBackup.HasBackupChecksums -or
    -not [bool]$databaseBackup.RestoreVerifyOnlyWithChecksumPassed) {
    throw 'Database backup evidence is incomplete.'
}
if (-not [bool]$runtimeBackup.AllHashesVerified -or
    [int]$runtimeBackup.FileCount -ne 3) {
    throw 'Runtime backup evidence is incomplete.'
}
if ($ResumeAfterVehicleSetOptionFailure) {
    if ([string]::IsNullOrWhiteSpace($PartialSchemaProofPath) -or
        -not (Test-Path -LiteralPath $PartialSchemaProofPath -PathType Leaf)) {
        throw 'Partial schema proof is required for resume.'
    }
    $partialProof = Get-Content -LiteralPath $PartialSchemaProofPath -Raw |
        ConvertFrom-Json
    if (-not [bool]$partialProof.PartialPrefixExact -or
        -not [bool]$partialProof.SafeResumeRequiresSqlcmdQuotedIdentifierOn) {
        throw 'Partial schema proof does not authorize safe resume.'
    }
}

New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

try {
    Assert-PackageManifest
    [void](Assert-WorkerStopped)
    [void](Assert-TimeApiHealthy)

    foreach ($entry in $deployment.Phase1ApiFiles) {
        $path = Join-Path $apiRuntime ([string]$entry.File)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne
                [string]$entry.Sha256) {
            throw "Phase 1 API runtime changed: $($entry.File)"
        }
    }
    foreach ($record in $runtimeBackup.Files) {
        $path = Join-Path $workerRuntime ([string]$record.File)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne
                [string]$record.OriginalSha256) {
            throw "Worker runtime changed after backup: $($record.File)"
        }
    }

    Invoke-ImmediatePreflight

    $lockConnection = New-SqlConnection `
        -Database 'QLHV_APP' `
        -ApplicationName 'QLHV RT03 V6 Phase2 Deployment Locks'
    $lockConnection.Open()
    $acquireResults = @()
    foreach ($resource in $writerResources) {
        $acquireResults += [pscustomobject]@{
            Resource = $resource
            Result = Acquire-WriterLock -Resource $resource
        }
    }

    $lockedBaseline = Assert-LockedBaseline

    $courseOutputName = if ($ResumeAfterVehicleSetOptionFailure) {
        '12_course_prerequisite_resume.txt'
    }
    else {
        '05_course_prerequisite.txt'
    }
    $vehicleOutputName = if ($ResumeAfterVehicleSetOptionFailure) {
        '12_vehicle_prerequisite_resume.txt'
    }
    else {
        '06_vehicle_prerequisite.txt'
    }
    $recoveryOutputName = if ($ResumeAfterVehicleSetOptionFailure) {
        '13_recovery_prerequisite_resume.txt'
    }
    else {
        '07_recovery_prerequisite.txt'
    }
    $courseOutput = Join-Path $EvidenceDirectory $courseOutputName
    $vehicleOutput = Join-Path $EvidenceDirectory $vehicleOutputName
    $recoveryOutput = Join-Path $EvidenceDirectory $recoveryOutputName

    if (-not $ResumeAfterVehicleSetOptionFailure) {
        Invoke-SchemaScript `
            -RelativePath 'sql\20260730_rt03_support_khoahoc_business_identity.sql' `
            -OutputFile $courseOutput `
            -Variables @(
                'Rt03TargetDatabase=QLHV_APP',
                'Rt03ExpectedDatabaseId=12',
                'Rt03ExpectedDatabaseGuid=9C44B304-8A84-4D0D-9A82-19C7233FF6BB',
                'Rt03ExecutionMode=PRODUCTION',
                'Rt03ForceFailureStep=NONE'
            )
    }
    Invoke-SchemaScript `
        -RelativePath 'sql\20260730_add_vehicle_realtime_mapping.sql' `
        -OutputFile $vehicleOutput
    Invoke-SchemaScript `
        -RelativePath 'sql\20260731_add_rt03_full_convergence_recovery.sql' `
        -OutputFile $recoveryOutput

    $deployedBinaries = @()
    foreach ($entry in $deployment.DeploymentBinaries) {
        $source = Join-Path $package ([string]$entry.File)
        $destination = Join-Path $workerRuntime ([string]$entry.File)
        $sourceHash =
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ($sourceHash -ne [string]$entry.Sha256) {
            throw "Package binary hash mismatch: $($entry.File)"
        }
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $destinationHash =
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($destinationHash -ne [string]$entry.Sha256) {
            throw "Runtime binary hash mismatch: $($entry.File)"
        }
        $deployedBinaries += [pscustomobject]@{
            File = [string]$entry.File
            RuntimePath = $destination
            Sha256 = $destinationHash
        }
    }

    [void](Assert-WorkerStopped)
    $timeAfterCopy = Assert-TimeApiHealthy

    Release-HeldLocks
    $lockConnection.Dispose()
    $lockConnection = $null

    [ordered]@{
        Contract = 'RT03_V6_PHASE2_PREREQUISITE_BINARY_DEPLOYMENT'
        CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Passed = $true
        ResumeAfterVehicleSetOptionFailure =
            [bool]$ResumeAfterVehicleSetOptionFailure
        CoursePrerequisiteRerun =
            -not [bool]$ResumeAfterVehicleSetOptionFailure
        SqlcmdQuotedIdentifierOption = $true
        DatabaseBackupPath = [string]$databaseBackup.BackupPath
        RuntimeBackupDirectory = [string]$runtimeBackup.BackupDirectory
        LockedBaseline = $lockedBaseline
        AcquiredLocks = $acquireResults
        ReleasedLocks = @($releaseResults)
        SchemaPrerequisites = @($schemaResults)
        WorkerBinaries = $deployedBinaries
        WorkerStarted = $false
        AutoSyncEnabled = $false
        ManualCheckpointOrStateWrite = $false
        TimeHealthAfterCopy = [string]$timeAfterCopy.time.health
        LastSyncErrorAfterCopy = [int]$timeAfterCopy.time.lastSyncError
    } | ConvertTo-Json -Depth 9 |
        Set-Content -LiteralPath $resultPath -Encoding UTF8

    Write-Output (
        "PHASE2_PREREQUISITES_PASS " +
        "SCHEMA=$($schemaResults.Count) " +
        "BINARIES=$($deployedBinaries.Count) " +
        "LOCKS=$($acquireResults.Count)/$($releaseResults.Count) " +
        "TIME=$($timeAfterCopy.time.health)/$($timeAfterCopy.time.lastSyncError) " +
        "WORKER=Stopped/0"
    )
    Write-Output "EVIDENCE=$resultPath"
}
catch {
    $failure = $_.Exception.Message
    $releaseFailure = $null
    try {
        Release-HeldLocks
    }
    catch {
        $releaseFailure = $_.Exception.Message
    }
    if ($null -ne $lockConnection) {
        $lockConnection.Dispose()
        $lockConnection = $null
    }

    [ordered]@{
        Contract = 'RT03_V6_PHASE2_PREREQUISITE_BINARY_DEPLOYMENT_FAILURE'
        CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Passed = $false
        Failure = $failure
        LockReleaseFailure = $releaseFailure
        HeldLocksAfterCleanup = @($heldLocks)
        ReleasedLocks = @($releaseResults)
        SchemaPrerequisitesAttempted = @($schemaResults)
        WorkerStarted = $false
        AutoSyncEnabled = $false
        ManualCheckpointOrStateWrite = $false
    } | ConvertTo-Json -Depth 9 |
        Set-Content -LiteralPath $failurePath -Encoding UTF8

    if ($null -ne $releaseFailure) {
        throw (
            "Phase 2 prerequisite deployment failed and lock release was uncertain. " +
            "Original: $failure Release: $releaseFailure"
        )
    }
    throw $failure
}
