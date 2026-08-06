[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeRoot,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($required in @($RepositoryRoot,$RuntimeRoot,$EvidenceRoot)) {
    if ([string]::IsNullOrWhiteSpace($required)) {
        throw 'RT04_LIFECYCLE_ARGUMENT_EMPTY'
    }
}
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot).TrimEnd('\')
if ($RepositoryRoot -ne 'D:\QLHV_APP' -or
    $RuntimeRoot -ne 'D:\QLHV_APP_RUNTIME' -or
    -not $EvidenceRoot.StartsWith(
        'D:\QLHV_RT04_EVIDENCE\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_LIFECYCLE_ABSOLUTE_PATH_REJECTED'
}

$serviceScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'))
$workerExecutable = [IO.Path]::GetFullPath((Join-Path `
    -Path $RuntimeRoot `
    -ChildPath 'app\worker\QLHV.Worker.exe'))
$workerDirectory = [IO.Path]::GetFullPath((Join-Path `
    -Path $RuntimeRoot `
    -ChildPath 'app\worker'))
foreach ($leaf in @($serviceScript,$workerExecutable)) {
    if (-not (Test-Path -LiteralPath $leaf -PathType Leaf)) {
        throw "RT04_LIFECYCLE_REQUIRED_LEAF_MISSING: $leaf"
    }
}
. $serviceScript
Assert-QlhvRealtimeWorkerAdministrator

function Open-Rt04Database([string]$Database,[string]$ApplicationName) {
    $builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = 'lpc:CSDLTTTC'
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $false
    $builder['TrustServerCertificate'] = $true
    $builder['Application Name'] = $ApplicationName
    $connection = [Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $connection.Open()
    return $connection
}

function Read-Rt04One([Data.SqlClient.SqlConnection]$Connection,[string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 30
    try {
        $reader = $command.ExecuteReader()
        try {
            $table = [Data.DataTable]::new()
            $table.Load($reader)
            if ($table.Rows.Count -ne 1) {
                throw 'RT04_LIFECYCLE_QUERY_CARDINALITY_REJECTED'
            }
            return $table.Rows[0]
        }
        finally { $reader.Dispose() }
    }
    finally { $command.Dispose() }
}

function Read-Rt04Snapshot {
    $target = Open-Rt04Database 'QLHV_APP' 'RT04 lifecycle proof'
    try {
        $row = Read-Rt04One $target @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0) OtoRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_MOTO' AND IsDeleted=0) MotoRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun) AutoSyncHistory,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun
  WHERE Status IN(N'QUEUED',N'RUNNING') OR ActiveSlot=1) AutoSyncActive,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvSyncOperationHistory
  WHERE Status IN(N'QUEUED',N'RUNNING')) AutoSyncOperationActive,
 (SELECT COUNT_BIG(*) FROM
  (SELECT SourceProfileCode,SourceMaDK FROM dbo.App_HocVien WHERE IsDeleted=0
   GROUP BY SourceProfileCode,SourceMaDK HAVING COUNT_BIG(*)>1) duplicateRow) Duplicates,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeManualReview) ManualReview,
 (SELECT SourceChangeTrackingVersion FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
  WHERE SourceProfileCode=N'CSDT_OTO') OtoCheckpoint,
 (SELECT SourceChangeTrackingVersion FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
  WHERE SourceProfileCode=N'CSDT_MOTO') MotoCheckpoint,
 (SELECT Status FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) WorkerStatus,
 (SELECT CycleActive FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) CycleActive,
 (SELECT LastErrorCode FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) LastErrorCode,
 (SELECT LastStatus FROM dbo.App_QlhvDirectRealtimeProfileState
  WHERE SourceProfileCode=N'CSDT_OTO') OtoStatus,
 (SELECT LastStatus FROM dbo.App_QlhvDirectRealtimeProfileState
  WHERE SourceProfileCode=N'CSDT_MOTO') MotoStatus;
'@
    }
    finally { $target.Dispose() }

    $oto = Open-Rt04Database 'CSDL_OTO' 'RT04 lifecycle OTO CT proof'
    try {
        $otoRow = Read-Rt04One $oto `
            'SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) Ct;'
        $otoCt = [long]$otoRow.Ct
    }
    finally { $oto.Dispose() }
    $moto = Open-Rt04Database 'CSDL_MOTO' 'RT04 lifecycle MOTO CT proof'
    try {
        $motoRow = Read-Rt04One $moto `
            'SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) Ct;'
        $motoCt = [long]$motoRow.Ct
    }
    finally { $moto.Dispose() }

    return [pscustomobject]@{
        OtoRows = [long]$row.OtoRows
        MotoRows = [long]$row.MotoRows
        AutoSyncHistory = [long]$row.AutoSyncHistory
        AutoSyncActive = [long]$row.AutoSyncActive
        AutoSyncOperationActive = [long]$row.AutoSyncOperationActive
        Duplicates = [long]$row.Duplicates
        ManualReview = [long]$row.ManualReview
        OtoCheckpoint = [long]$row.OtoCheckpoint
        MotoCheckpoint = [long]$row.MotoCheckpoint
        OtoCt = $otoCt
        MotoCt = $motoCt
        WorkerStatus = [string]$row.WorkerStatus
        CycleActive = [bool]$row.CycleActive
        LastErrorCode = if ($row.LastErrorCode -is [DBNull]) {
            $null
        } else { [string]$row.LastErrorCode }
        OtoStatus = [string]$row.OtoStatus
        MotoStatus = [string]$row.MotoStatus
    }
}

function Assert-Rt04BusinessState($Snapshot,[string]$Phase) {
    if ($Snapshot.OtoRows -ne 156 -or $Snapshot.MotoRows -ne 5 -or
        $Snapshot.AutoSyncHistory -ne 11 -or $Snapshot.AutoSyncActive -ne 0 -or
        $Snapshot.AutoSyncOperationActive -ne 0 -or $Snapshot.Duplicates -ne 0 -or
        $Snapshot.ManualReview -ne 0 -or
        $Snapshot.OtoCheckpoint -ne $Snapshot.OtoCt -or
        $Snapshot.MotoCheckpoint -ne $Snapshot.MotoCt -or
        $Snapshot.OtoStatus -ne 'HEALTHY_NO_CHANGE' -or
        $Snapshot.MotoStatus -ne 'HEALTHY_NO_CHANGE') {
        throw "RT04_LIFECYCLE_BUSINESS_STATE_REJECTED: $Phase"
    }
}

function Wait-Rt04WorkerState(
    [string]$ExpectedStatus,
    [int]$TimeoutSeconds
) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $snapshot = Read-Rt04Snapshot
        if ($snapshot.WorkerStatus -eq $ExpectedStatus -and
            -not $snapshot.CycleActive -and
            $null -eq $snapshot.LastErrorCode) {
            return $snapshot
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "RT04_LIFECYCLE_WORKER_STATE_TIMEOUT: $ExpectedStatus"
}

function Wait-Rt04HealthyIdleSignal([int]$TimeoutSeconds) {
    $connection = Open-Rt04Database 'QLHV_APP' 'RT04 lifecycle idle signal'
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = @'
SELECT Status,CycleActive,LastErrorCode
FROM dbo.App_QlhvDirectRealtimeWorkerState
WHERE WorkerStateId=1;
'@
        $command.CommandTimeout = 5
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            do {
                $reader = $command.ExecuteReader()
                try {
                    if (-not $reader.Read()) {
                        throw 'RT04_LIFECYCLE_IDLE_SIGNAL_MISSING'
                    }
                    $status = [string]$reader.GetValue(0)
                    $active = [bool]$reader.GetValue(1)
                    $errorIsNull = $reader.IsDBNull(2)
                    if ($reader.Read()) {
                        throw 'RT04_LIFECYCLE_IDLE_SIGNAL_CARDINALITY_REJECTED'
                    }
                }
                finally { $reader.Dispose() }
                if ($status -eq 'HEALTHY' -and -not $active -and $errorIsNull) {
                    return
                }
                Start-Sleep -Milliseconds 50
            } while ([DateTime]::UtcNow -lt $deadline)
        }
        finally { $command.Dispose() }
    }
    finally { $connection.Dispose() }
    throw 'RT04_LIFECYCLE_HEALTHY_IDLE_TIMEOUT'
}

function Test-Rt04MutexHeld {
    $connection = Open-Rt04Database 'QLHV_APP' 'RT04 lifecycle mutex proof'
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = @'
DECLARE @Result int;
EXEC @Result=sys.sp_getapplock @Resource=N'QLHV:CSDT_AUTO_SYNC',
 @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public';
IF @Result>=0
 EXEC sys.sp_releaseapplock @Resource=N'QLHV:CSDT_AUTO_SYNC',
  @LockOwner=N'Session',@DbPrincipal=N'public';
SELECT @Result;
'@
        try { return [int]$command.ExecuteScalar() -lt 0 }
        finally { $command.Dispose() }
    }
    finally { $connection.Dispose() }
}

$initialService = Get-QlhvRealtimeWorkerServiceRecord
if ($null -eq $initialService -or $initialService.State -ne 'Running' -or
    [int]$initialService.ProcessId -le 0) {
    throw 'RT04_LIFECYCLE_INITIAL_SERVICE_NOT_RUNNING'
}
Assert-QlhvRealtimeWorkerServiceIdentity `
    -ServiceRecord $initialService `
    -RuntimeRoot $RuntimeRoot
$initialSnapshot = Read-Rt04Snapshot
Assert-Rt04BusinessState $initialSnapshot 'INITIAL'
if ($initialSnapshot.WorkerStatus -ne 'HEALTHY' -or
    $null -ne $initialSnapshot.LastErrorCode -or
    -not (Test-Rt04MutexHeld)) {
    throw 'RT04_LIFECYCLE_INITIAL_RUNTIME_REJECTED'
}
$initialPid = [int]$initialService.ProcessId

Stop-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
$stoppedSnapshot = Wait-Rt04WorkerState -ExpectedStatus 'STOPPED' -TimeoutSeconds 30
Assert-Rt04BusinessState $stoppedSnapshot 'GRACEFUL_STOP'
$stoppedService = Get-QlhvRealtimeWorkerServiceRecord
if ($stoppedService.State -ne 'Stopped' -or
    [int]$stoppedService.ProcessId -ne 0 -or
    (Test-Rt04MutexHeld)) {
    throw 'RT04_LIFECYCLE_GRACEFUL_STOP_REJECTED'
}

Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
$restartedSnapshot = Wait-Rt04WorkerState -ExpectedStatus 'HEALTHY' -TimeoutSeconds 45
Assert-Rt04BusinessState $restartedSnapshot 'GRACEFUL_RESTART'
$restartedService = Get-QlhvRealtimeWorkerServiceRecord
if ($restartedService.State -ne 'Running' -or
    [int]$restartedService.ProcessId -le 0 -or
    [int]$restartedService.ProcessId -eq $initialPid -or
    -not (Test-Rt04MutexHeld)) {
    throw 'RT04_LIFECYCLE_GRACEFUL_RESTART_REJECTED'
}
$restartedPid = [int]$restartedService.ProcessId

$secondStdout = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_second_worker_mutex_stdout.log'
$secondStderr = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_second_worker_mutex_stderr.log'
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $workerExecutable
$startInfo.WorkingDirectory = $workerDirectory
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($entry in $script:QlhvRealtimeWorkerEnvironment) {
    $parts = [string]$entry -split '=',2
    if ($parts.Count -ne 2 -or
        [string]::IsNullOrWhiteSpace($parts[0]) -or
        [string]::IsNullOrWhiteSpace($parts[1])) {
        throw 'RT04_LIFECYCLE_WORKER_ENVIRONMENT_REJECTED'
    }
    $startInfo.EnvironmentVariables[$parts[0]] = $parts[1]
}
$secondWorker = [Diagnostics.Process]::new()
$secondWorker.StartInfo = $startInfo
if (-not $secondWorker.Start()) {
    throw 'RT04_LIFECYCLE_SECOND_WORKER_START_REJECTED'
}
$stdoutTask = $secondWorker.StandardOutput.ReadToEndAsync()
$stderrTask = $secondWorker.StandardError.ReadToEndAsync()
if (-not $secondWorker.WaitForExit(20000)) {
    Stop-Process -Id $secondWorker.Id -Force -ErrorAction SilentlyContinue
    throw 'RT04_LIFECYCLE_SECOND_WORKER_DID_NOT_FAIL_CLOSED'
}
$secondStdoutText = $stdoutTask.GetAwaiter().GetResult()
$secondStderrText = $stderrTask.GetAwaiter().GetResult()
[IO.File]::WriteAllText(
    $secondStdout,
    $secondStdoutText,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $secondStderr,
    $secondStderrText,
    [Text.UTF8Encoding]::new($false))
$secondExitCode = [int]$secondWorker.ExitCode
if ($secondExitCode -ne 0 -or
    $secondStdoutText -notmatch
        'could not acquire the Existing Auto Sync lifetime lock' -or
    -not [string]::IsNullOrWhiteSpace($secondStderrText)) {
    throw "RT04_LIFECYCLE_SECOND_WORKER_EXIT_REJECTED: $secondExitCode"
}
$afterMutexSnapshot = Wait-Rt04WorkerState `
    -ExpectedStatus 'HEALTHY' `
    -TimeoutSeconds 45
Assert-Rt04BusinessState $afterMutexSnapshot 'MUTEX_EXCLUSION'
$afterMutexService = Get-QlhvRealtimeWorkerServiceRecord
if ($afterMutexService.State -ne 'Running' -or
    [int]$afterMutexService.ProcessId -ne $restartedPid -or
    $afterMutexSnapshot.WorkerStatus -ne 'HEALTHY' -or
    $afterMutexSnapshot.CycleActive -or
    -not (Test-Rt04MutexHeld)) {
    throw 'RT04_LIFECYCLE_MUTEX_EXCLUSION_REJECTED'
}

$crashPid = [int]$afterMutexService.ProcessId
Wait-Rt04HealthyIdleSignal -TimeoutSeconds 45
$crashProcess = Get-CimInstance Win32_Process `
    -Filter "ProcessId=$crashPid" `
    -ErrorAction Stop
if ($null -eq $crashProcess -or $crashProcess.Name -ne 'QLHV.Worker.exe') {
    throw 'RT04_LIFECYCLE_CRASH_PID_IDENTITY_REJECTED'
}
& taskkill.exe /PID $crashPid /F | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'RT04_LIFECYCLE_EXACT_CRASH_INJECTION_REJECTED'
}

$recoveryDeadline = [DateTime]::UtcNow.AddSeconds(60)
$recoveredService = $null
do {
    Start-Sleep -Milliseconds 500
    $candidateService = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -ne $candidateService -and
        $candidateService.State -eq 'Running' -and
        [int]$candidateService.ProcessId -gt 0 -and
        [int]$candidateService.ProcessId -ne $crashPid) {
        $recoveredService = $candidateService
        break
    }
} while ([DateTime]::UtcNow -lt $recoveryDeadline)
if ($null -eq $recoveredService) {
    throw 'RT04_LIFECYCLE_SCM_RECOVERY_TIMEOUT'
}
$recoveredSnapshot = Wait-Rt04WorkerState -ExpectedStatus 'HEALTHY' -TimeoutSeconds 45
Assert-Rt04BusinessState $recoveredSnapshot 'CRASH_RECOVERY'
if (-not (Test-Rt04MutexHeld)) {
    throw 'RT04_LIFECYCLE_RECOVERED_MUTEX_MISSING'
}

$result = [ordered]@{
    Evidence = 'RT04_SERVICE_LIFECYCLE_VALIDATION_PASS'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    GracefulStopRecorded = $true
    GracefulRestartPassed = $true
    MutualExclusionSecondWorkerExitCode = $secondExitCode
    ExactCrashInjectionPid = $crashPid
    ScmCrashRecoveryPassed = $true
    InitialPid = $initialPid
    GracefulRestartPid = $restartedPid
    RecoveredPid = [int]$recoveredService.ProcessId
    ProductionCounts = '156/5'
    AutoSyncHistoryAndActive = '11/0/0'
    Checkpoints = "OTO=$($recoveredSnapshot.OtoCheckpoint);MOTO=$($recoveredSnapshot.MotoCheckpoint)"
    CheckpointsCurrent = $true
    DuplicateRows = 0
    ManualReviewRows = 0
    FinalWorkerStatus = $recoveredSnapshot.WorkerStatus
    FinalProfiles = "$($recoveredSnapshot.OtoStatus)/$($recoveredSnapshot.MotoStatus)"
    FinalMutexHeld = $true
}
$resultPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_service_lifecycle_validation.json'
[IO.File]::WriteAllText(
    $resultPath,
    ($result | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
Write-Output 'RT04_SERVICE_LIFECYCLE_VALIDATION_PASS'
