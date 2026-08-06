[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Open-Db([string]$Database, [string]$ApplicationName) {
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

function Read-One([Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 30
    try {
        $reader = $command.ExecuteReader()
        try {
            $table = [Data.DataTable]::new()
            $table.Load($reader)
            if ($table.Rows.Count -ne 1) {
                throw 'RT04_MONITOR_QUERY_CARDINALITY_REJECTED'
            }
            return $table.Rows[0]
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $command.Dispose()
    }
}

function Read-SourceHealth(
    [string]$Database,
    [int]$ExpectedDatabaseId,
    [string]$ExpectedGuid
) {
    $connection = Open-Db $Database 'RT04 PII-safe source monitor'
    try {
        $row = Read-One $connection @'
SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) ServerIdentity,
 DB_NAME() DatabaseName,DB_ID() DatabaseId,
 CONVERT(nvarchar(36),identityRow.database_guid) DatabaseGuid,
 CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) CurrentCtVersion,
 CONVERT(bigint,(SELECT MIN(versionRow.MinVersion) FROM (VALUES
   (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX'))),
   (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))),
   (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.KhoaHoc'))),
   (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.DM_HangDT'))),
   (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.DM_DVHC')))
 ) versionRow(MinVersion))) MinValidCtVersion,
 databaseRow.is_read_committed_snapshot_on RcsiEnabled,
 databaseRow.snapshot_isolation_state SnapshotIsolationState
FROM sys.database_recovery_status identityRow
JOIN sys.databases databaseRow ON databaseRow.database_id=identityRow.database_id
WHERE identityRow.database_id=DB_ID();
'@
        $identityPass =
            [string]$row.ServerIdentity -eq 'CSDLTTTC' -and
            [string]$row.DatabaseName -eq $Database -and
            [int]$row.DatabaseId -eq $ExpectedDatabaseId -and
            [string]$row.DatabaseGuid -eq $ExpectedGuid
        return [pscustomobject]@{
            Database = $Database
            IdentityPass = $identityPass
            CurrentCtVersion = [long]$row.CurrentCtVersion
            MinValidCtVersion = [long]$row.MinValidCtVersion
            RcsiEnabled = [bool]$row.RcsiEnabled
            SnapshotIsolationState = [int]$row.SnapshotIsolationState
        }
    }
    finally {
        $connection.Dispose()
    }
}

$service = Get-CimInstance Win32_Service `
    -Filter "Name='QLHV_APP_RealtimeWorker'" `
    -ErrorAction SilentlyContinue
$approvedWorkerPath = 'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe'
$workerProcesses = @(Get-CimInstance Win32_Process `
    -Filter "Name='QLHV.Worker.exe'" `
    -ErrorAction SilentlyContinue)
$configuredExecutableApproved = $null -ne $service -and
    [string]::Equals(
        ([string]$service.PathName).Trim('"'),
        $approvedWorkerPath,
        [StringComparison]::OrdinalIgnoreCase)
$unexpectedExposedPath = @($workerProcesses | Where-Object {
    -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
    -not [string]::Equals(
        [string]$_.ExecutablePath,
        $approvedWorkerPath,
        [StringComparison]::OrdinalIgnoreCase)
}).Count -ne 0
$approvedInfrastructurePath =
    'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Infrastructure.dll'
$workerExecutableHash = (Get-FileHash `
    -LiteralPath $approvedWorkerPath `
    -Algorithm SHA256).Hash
$workerInfrastructureHash = (Get-FileHash `
    -LiteralPath $approvedInfrastructurePath `
    -Algorithm SHA256).Hash
$runtimeBundleApproved =
    $workerExecutableHash -eq
        '2AB4C3E76103FA8C9B5585DFE63157BD65AA7089D2BB122C2E7CA9FBCF1FD6BC' -and
    $workerInfrastructureHash -eq
        '04B8E9588FF1BE9E2997D5BB22CC24C1635966BA2F52CA502FE4EFA9A2D38418'

$target = Open-Db 'QLHV_APP' 'RT04 PII-safe production monitor'
try {
    $targetState = Read-One $target @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0) OtoTargetActiveRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_MOTO' AND IsDeleted=0) MotoTargetActiveRows,
 (SELECT COUNT_BIG(*) FROM
   (SELECT SourceProfileCode,SourceMaDK FROM dbo.App_HocVien WHERE IsDeleted=0
    GROUP BY SourceProfileCode,SourceMaDK HAVING COUNT_BIG(*)>1) duplicates) DuplicateActiveRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun) AutoSyncHistoryRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun
  WHERE Status IN(N'QUEUED',N'RUNNING') OR ActiveSlot=1) ActiveAutoSyncRunOrSlotRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvSyncOperationHistory
  WHERE Status IN(N'QUEUED',N'RUNNING')) ActiveAutoSyncOperationRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeManualReview) ManualReviewRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeFeatureState
  WHERE FeatureStateId=1 AND EnableProductionRealtime=1 AND EnableProductionShadow=1
    AND EnableProductionWrites=1 AND EnableProductionCanary=0
    AND EnableControlledCutover=1 AND EnableProductionDeletes=0) ExactFeatureRows,
 (SELECT Status FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) WorkerStatus,
 (SELECT CycleActive FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) WorkerCycleActive,
 (SELECT LastErrorCode FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) WorkerLastErrorCode,
 (SELECT LastHeartbeatUtc FROM dbo.App_QlhvDirectRealtimeWorkerState WHERE WorkerStateId=1) WorkerLastHeartbeatUtc,
 (SELECT LastStatus FROM dbo.App_QlhvDirectRealtimeProfileState
  WHERE SourceProfileCode=N'CSDT_OTO') OtoProfileStatus,
 (SELECT LastStatus FROM dbo.App_QlhvDirectRealtimeProfileState
  WHERE SourceProfileCode=N'CSDT_MOTO') MotoProfileStatus,
 (SELECT SourceChangeTrackingVersion FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
  WHERE SourceProfileCode=N'CSDT_OTO') OtoCheckpoint,
 (SELECT SourceChangeTrackingVersion FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
  WHERE SourceProfileCode=N'CSDT_MOTO') MotoCheckpoint,
 (SELECT MAX(CompletedAtUtc) FROM dbo.App_QlhvDirectRealtimeCycleHistory
  WHERE Status=N'HEALTHY_NO_CHANGE') LastHealthyCycleUtc,
 (SELECT COUNT_BIG(*) FROM sys.dm_tran_database_transactions databaseTransaction
  JOIN sys.dm_tran_session_transactions sessionTransaction
    ON sessionTransaction.transaction_id=databaseTransaction.transaction_id
  WHERE databaseTransaction.database_id=DB_ID()
    AND sessionTransaction.session_id<>@@SPID) OtherTransactions,
 (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests
  WHERE database_id=DB_ID() AND session_id<>@@SPID
    AND (blocking_session_id<>0 OR wait_type LIKE N'LCK%')) BlockedRequests,
 (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id=DB_ID()) RcsiEnabled;
'@

    $mutexCommand = $target.CreateCommand()
    $mutexCommand.CommandText = @'
DECLARE @Result int;
EXEC @Result=sys.sp_getapplock @Resource=N'QLHV:CSDT_AUTO_SYNC',
 @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public';
IF @Result>=0
 EXEC sys.sp_releaseapplock @Resource=N'QLHV:CSDT_AUTO_SYNC',
  @LockOwner=N'Session',@DbPrincipal=N'public';
SELECT @Result;
'@
    $mutexResult = [int]$mutexCommand.ExecuteScalar()
    $mutexCommand.Dispose()
}
finally {
    $target.Dispose()
}

$oto = Read-SourceHealth 'CSDL_OTO' 9 '9A8B9BC1-18F3-4823-8123-3DC197A9D540'
$moto = Read-SourceHealth 'CSDL_MOTO' 8 '308BDDA8-80F3-4ACB-9836-578D80A9E98E'
$otoLag = $oto.CurrentCtVersion - [long]$targetState.OtoCheckpoint
$motoLag = $moto.CurrentCtVersion - [long]$targetState.MotoCheckpoint

$processId = if ($workerProcesses.Count -eq 1) {
    [int]$workerProcesses[0].ProcessId
}
else { 0 }
$process = if ($processId -gt 0) {
    Get-Process -Id $processId -ErrorAction Stop
}
else { $null }
$applicationLog = Get-WinEvent -ListLog Application -ErrorAction Stop
$registryPath =
    'HKLM:\SYSTEM\CurrentControlSet\Services\QLHV_APP_RealtimeWorker'
$delayedAuto = if (Test-Path -LiteralPath $registryPath) {
    [int](Get-ItemProperty -LiteralPath $registryPath `
        -Name DelayedAutoStart -ErrorAction SilentlyContinue).DelayedAutoStart
}
else { 0 }
$failureOutput = if ($null -ne $service) {
    (& sc.exe qfailure QLHV_APP_RealtimeWorker 2>&1) -join "`n"
}
else { '' }

$gates = [ordered]@{
    ServiceRunning = $null -ne $service -and [string]$service.State -eq 'Running'
    ServiceAutomaticDelayed = $null -ne $service -and
        [string]$service.StartMode -eq 'Auto' -and $delayedAuto -eq 1
    ServiceAccountLeastPrivilege = $null -ne $service -and
        [string]$service.StartName -eq 'NT SERVICE\QLHV_APP_RealtimeWorker'
    ServiceExecutableConfigured = $configuredExecutableApproved
    RuntimeBundleApproved = $runtimeBundleApproved
    ExactOneWorkerProcess = $workerProcesses.Count -eq 1 -and
        -not $unexpectedExposedPath
    ServiceProcessMatches = $null -ne $service -and
        [int]$service.ProcessId -eq $processId -and $processId -gt 0
    WorkerHealthy = [string]$targetState.WorkerStatus -eq 'HEALTHY' -and
        $targetState.WorkerLastErrorCode -is [DBNull] -and
        [DateTime]$targetState.WorkerLastHeartbeatUtc -ge
            [DateTime]::UtcNow.AddMinutes(-2)
    ProfilesHealthy = [string]$targetState.OtoProfileStatus -eq 'HEALTHY_NO_CHANGE' -and
        [string]$targetState.MotoProfileStatus -eq 'HEALTHY_NO_CHANGE'
    CountsHealthy = [long]$targetState.OtoTargetActiveRows -eq 156 -and
        [long]$targetState.MotoTargetActiveRows -eq 5
    CheckpointsCurrent = $otoLag -eq 0 -and $motoLag -eq 0
    CheckpointsWithinRetention = [long]$targetState.OtoCheckpoint -ge $oto.MinValidCtVersion -and
        [long]$targetState.MotoCheckpoint -ge $moto.MinValidCtVersion
    AutoSyncOff = [long]$targetState.AutoSyncHistoryRows -eq 11 -and
        [long]$targetState.ActiveAutoSyncRunOrSlotRows -eq 0 -and
        [long]$targetState.ActiveAutoSyncOperationRows -eq 0
    MutexHeld = $mutexResult -lt 0
    NoDuplicatesOrManualReview = [long]$targetState.DuplicateActiveRows -eq 0 -and
        [long]$targetState.ManualReviewRows -eq 0
    NoTransactionsOrBlockers = [long]$targetState.OtherTransactions -eq 0 -and
        [long]$targetState.BlockedRequests -eq 0
    ExactFeatureState = [long]$targetState.ExactFeatureRows -eq 1
    DatabaseIdentities = $oto.IdentityPass -and $moto.IdentityPass
    RcsiOff = -not [bool]$targetState.RcsiEnabled -and
        -not $oto.RcsiEnabled -and -not $moto.RcsiEnabled
    RecoveryPolicy = $failureOutput -match 'RESTART'
}
$healthy = @($gates.Values | Where-Object { -not $_ }).Count -eq 0

$result = [ordered]@{
    Evidence = 'RT04_PRODUCTION_HEALTH_PII_SAFE'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    Healthy = $healthy
    Gates = $gates
    Service = [ordered]@{
        Name = if ($null -ne $service) { [string]$service.Name } else { $null }
        State = if ($null -ne $service) { [string]$service.State } else { $null }
        StartMode = if ($null -ne $service) { [string]$service.StartMode } else { $null }
        DelayedAutomatic = $delayedAuto -eq 1
        Account = if ($null -ne $service) { [string]$service.StartName } else { $null }
        ProcessId = $processId
        ConfiguredExecutable = if ($null -ne $service) {
            [string]$service.PathName
        } else { $null }
        WorkerExecutableSha256 = $workerExecutableHash
        WorkerInfrastructureSha256 = $workerInfrastructureHash
        WorkingDirectoryContract = 'AppContext.BaseDirectory'
        CpuSeconds = if ($null -ne $process) { [math]::Round($process.CPU,3) } else { $null }
        WorkingSetMb = if ($null -ne $process) {
            [math]::Round($process.WorkingSet64 / 1MB,2)
        } else { $null }
        RecoveryRestartConfigured = $failureOutput -match 'RESTART'
    }
    Realtime = [ordered]@{
        WorkerStatus = [string]$targetState.WorkerStatus
        CycleActive = [bool]$targetState.WorkerCycleActive
        LastErrorCode = if ($targetState.WorkerLastErrorCode -is [DBNull]) {
            $null
        } else { [string]$targetState.WorkerLastErrorCode }
        OtoStatus = [string]$targetState.OtoProfileStatus
        MotoStatus = [string]$targetState.MotoProfileStatus
        LastHealthyCycleUtc = Get-Date $targetState.LastHealthyCycleUtc -Format O
        LastHeartbeatUtc = Get-Date $targetState.WorkerLastHeartbeatUtc -Format O
        MutexHeld = $mutexResult -lt 0
    }
    Recovery = [ordered]@{
        FailureMode = 'FAIL_CLOSED_HOST_EXIT'
        ScmRestartScheduleSeconds = @(5,15,60)
        ScmResetPeriodSeconds = 86400
        InProcessRetryTelemetry = 'NOT_PERSISTED'
        LastErrorCode = if ($targetState.WorkerLastErrorCode -is [DBNull]) {
            $null
        } else { [string]$targetState.WorkerLastErrorCode }
    }
    Checkpoints = @(
        [ordered]@{
            Profile = 'CSDT_OTO'
            Checkpoint = [long]$targetState.OtoCheckpoint
            CurrentCtVersion = $oto.CurrentCtVersion
            MinValidCtVersion = $oto.MinValidCtVersion
            Lag = $otoLag
        },
        [ordered]@{
            Profile = 'CSDT_MOTO'
            Checkpoint = [long]$targetState.MotoCheckpoint
            CurrentCtVersion = $moto.CurrentCtVersion
            MinValidCtVersion = $moto.MinValidCtVersion
            Lag = $motoLag
        }
    )
    Integrity = [ordered]@{
        OtoTargetActiveRows = [long]$targetState.OtoTargetActiveRows
        MotoTargetActiveRows = [long]$targetState.MotoTargetActiveRows
        DuplicateActiveRows = [long]$targetState.DuplicateActiveRows
        ManualReviewRows = [long]$targetState.ManualReviewRows
        AutoSyncHistoryRows = [long]$targetState.AutoSyncHistoryRows
        ActiveRunSlotOperation = '0/0/0'
        OtherTransactions = [long]$targetState.OtherTransactions
        BlockedRequests = [long]$targetState.BlockedRequests
    }
    Logging = [ordered]@{
        Channel = 'Windows Application Event Log'
        MaximumSizeMb = [math]::Round($applicationLog.MaximumSizeInBytes / 1MB,2)
        LogMode = [string]$applicationLog.LogMode
        ContainsPiiOrSecretsByContract = $false
    }
}

$json = $result | ConvertTo-Json -Depth 12
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutput = [IO.Path]::GetFullPath($OutputPath)
    if (-not $fullOutput.StartsWith(
        'D:\QLHV_RT04_EVIDENCE\',
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'RT04_MONITOR_OUTPUT_PATH_REJECTED'
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
    [IO.File]::WriteAllText($fullOutput,$json,[Text.UTF8Encoding]::new($false))
}
Write-Output $json
if (-not $healthy) {
    exit 2
}
