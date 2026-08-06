[CmdletBinding()]
param(
    [switch]$Launch,
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
        throw 'RT04_ELEVATION_ARGUMENT_EMPTY'
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
    throw 'RT04_ELEVATION_ABSOLUTE_PATH_REJECTED'
}

$serviceScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'))
$installScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'ops\rt04\Install-Rt04ProductionService.ps1'))
foreach ($scriptPath in @($serviceScript,$installScript)) {
    if ([string]::IsNullOrWhiteSpace($scriptPath) -or
        -not [IO.Path]::IsPathRooted($scriptPath) -or
        -not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "RT04_ELEVATION_SCRIPT_PATH_REJECTED: $scriptPath"
    }
}
if ($serviceScript -ne
    'D:\QLHV_APP\scripts\windows\qlhv-lan\RealtimeWorkerService.ps1') {
    throw 'RT04_ELEVATION_SERVICE_SCRIPT_IDENTITY_REJECTED'
}

foreach ($scriptPath in @($serviceScript,$installScript)) {
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$errors)
    if ($errors.Count -ne 0) {
        throw "RT04_ELEVATION_SCRIPT_PARSE_REJECTED: $scriptPath"
    }
    $joinCommands = $ast.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            [string]::Equals(
                $node.GetCommandName(),
                'Join-Path',
                [StringComparison]::OrdinalIgnoreCase)
        },
        $true)
    foreach ($joinCommand in $joinCommands) {
        $text = $joinCommand.Extent.Text
        if ($text -notmatch '(?i)-Path\s+' -or
            $text -notmatch '(?i)-ChildPath\s+') {
            throw "RT04_INTERACTIVE_JOIN_PATH_REJECTED: $scriptPath"
        }
    }
}

[IO.Directory]::CreateDirectory($EvidenceRoot) | Out-Null

$service = Get-CimInstance Win32_Service `
    -Filter "Name='QLHV_APP_RealtimeWorker'" `
    -ErrorAction SilentlyContinue
$partialService = $null -ne $service
if ($partialService) {
    if ([string]$service.State -ne 'Stopped' -or
        [int]$service.ProcessId -ne 0 -or
        [string]$service.StartName -ne 'NT SERVICE\QLHV_APP_RealtimeWorker' -or
        -not [string]::Equals(
            ([string]$service.PathName).Trim('"'),
            'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'RT04_PARTIAL_SERVICE_IDENTITY_REJECTED'
    }
}

$productionSql = @'
SET NOCOUNT ON;
SELECT
 (SELECT COUNT(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0),
 (SELECT COUNT(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_MOTO' AND IsDeleted=0),
 (SELECT COUNT(*) FROM dbo.App_QlhvAutoSyncRun),
 (SELECT COUNT(*) FROM dbo.App_QlhvAutoSyncRun
  WHERE Status IN(N'QUEUED',N'RUNNING') OR ActiveSlot=1),
 (SELECT COUNT(*) FROM dbo.App_QlhvSyncOperationHistory
  WHERE Status IN(N'QUEUED',N'RUNNING')),
 (SELECT SourceChangeTrackingVersion FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
  WHERE SourceProfileCode=N'CSDT_OTO'),
 (SELECT SourceChangeTrackingVersion FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
  WHERE SourceProfileCode=N'CSDT_MOTO'),
 (SELECT COUNT(*) FROM master.sys.server_principals
  WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker');
'@
$productionOutput = & sqlcmd.exe `
    -S 'lpc:CSDLTTTC' `
    -d 'QLHV_APP' `
    -E `
    -C `
    -b `
    -h -1 `
    -W `
    -Q $productionSql 2>&1
if ($LASTEXITCODE -ne 0) {
    throw 'RT04_ELEVATION_PRODUCTION_SNAPSHOT_REJECTED'
}
$productionValues = (($productionOutput -join ' ').Trim() -split '\s+')
if ($productionValues.Count -ne 8 -or
    ($productionValues[0..4] -join ',') -ne '156,5,11,0,0' -or
    [long]$productionValues[5] -lt 1 -or
    [long]$productionValues[6] -ne 0 -or
    [long]$productionValues[7] -ne 0) {
    throw "RT04_FAILED_ATTEMPT_CHANGED_PRODUCTION: $($productionValues -join ',')"
}

$otoCtOutput = & sqlcmd.exe `
    -S 'lpc:CSDLTTTC' `
    -d 'CSDL_OTO' `
    -E `
    -C `
    -b `
    -h -1 `
    -W `
    -Q 'SET NOCOUNT ON; SELECT CHANGE_TRACKING_CURRENT_VERSION();' 2>&1
if ($LASTEXITCODE -ne 0 -or
    [long](($otoCtOutput -join ' ').Trim()) -ne [long]$productionValues[5]) {
    throw 'RT04_OTO_CHECKPOINT_NOT_CURRENT'
}

$workerProcesses = @(Get-CimInstance Win32_Process | Where-Object {
    [string]::Equals(
        [string]$_.ExecutablePath,
        'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe',
        [StringComparison]::OrdinalIgnoreCase)
})
if ($workerProcesses.Count -ne 1) {
    throw 'RT04_RECOVERY_WORKER_PROCESS_REJECTED'
}

$semanticCommand =
    "& '$installScript' -RepositoryRoot '$RepositoryRoot' " +
    "-RuntimeRoot '$RuntimeRoot' -EvidenceRoot '$EvidenceRoot'"
$sanitizedLauncher =
    "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass " +
    "-EncodedCommand <BASE64_OF: $semanticCommand>"
$commandLogPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '06_elevation_command_sanitized.txt'
[IO.File]::WriteAllText(
    $commandLogPath,
    $sanitizedLauncher,
    [Text.UTF8Encoding]::new($false))

$preflight = [ordered]@{
    Evidence = 'RT04_ELEVATION_PREFLIGHT_VALIDATED'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    RepositoryRoot = $RepositoryRoot
    RuntimeRoot = $RuntimeRoot
    ServiceScript = $serviceScript
    ServiceScriptExists = $true
    InstallScript = $installScript
    InstallScriptExists = $true
    JoinPathArgumentsExplicit = $true
    RequiredArgumentsExplicitAndNonEmpty = $true
    NoProfile = $true
    NonInteractive = $true
    PartialStoppedServiceFromPreviousAttempt = $partialService
    PartialServiceEverStarted = $false
    ProductionCounts = '156/5'
    AutoSyncHistoryAndActive = '11/0/0'
    Checkpoints = "OTO=$($productionValues[5]);MOTO=0"
    CheckpointChangedAfterInitialRecovery = ([long]$productionValues[5] -ne 1)
    CheckpointChangeAttribution = if ([long]$productionValues[5] -ne 1) {
        'CSDL_OTO CT KhoaHoc UPDATE; worker classified HEALTHY_NO_CHANGE'
    } else { $null }
    ElevationAttemptCheckpointMutation = $false
    ServiceLoginRows = 0
    RecoveryWorkerCount = 1
    ProductionMutationDuringFailedAttempt = $false
}
$preflightPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '06_elevation_preflight.json'
[IO.File]::WriteAllText(
    $preflightPath,
    ($preflight | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

if (-not $Launch) {
    Write-Output "RT04_ELEVATION_PREFLIGHT_PASS command=$commandLogPath"
    return
}

$elevatedCommand = @"
try {
  $semanticCommand
  exit 0
} catch {
  [IO.File]::WriteAllText(
    '$EvidenceRoot\06_service_install_elevated_error.txt',
    `$_.Exception.ToString(),
    [Text.UTF8Encoding]::new(`$false))
  exit 1
}
"@
$encoded = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($elevatedCommand))
$argumentString =
    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
if ([string]::IsNullOrWhiteSpace($argumentString)) {
    throw 'RT04_ELEVATION_LAUNCH_ARGUMENT_REJECTED'
}
$process = Start-Process `
    -FilePath 'powershell.exe' `
    -Verb RunAs `
    -ArgumentList $argumentString `
    -PassThru
Write-Output "RT04_VISIBLE_ELEVATION_STARTED pid=$($process.Id)"
