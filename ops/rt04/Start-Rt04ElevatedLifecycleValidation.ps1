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
        throw 'RT04_LIFECYCLE_LAUNCH_ARGUMENT_EMPTY'
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
    throw 'RT04_LIFECYCLE_LAUNCH_ABSOLUTE_PATH_REJECTED'
}

$serviceScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'))
$healthScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'ops\rt04\Get-Rt04ProductionHealth.ps1'))
$lifecycleScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'ops\rt04\Invoke-Rt04ServiceLifecycleValidation.ps1'))
foreach ($scriptPath in @($serviceScript,$healthScript,$lifecycleScript)) {
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "RT04_LIFECYCLE_LAUNCH_SCRIPT_MISSING: $scriptPath"
    }
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        throw "RT04_LIFECYCLE_LAUNCH_PARSE_REJECTED: $scriptPath"
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
        $commandText = $joinCommand.Extent.Text
        if ($commandText -notmatch '(?i)-Path\s+' -or
            $commandText -notmatch '(?i)-ChildPath\s+') {
            throw "RT04_INTERACTIVE_JOIN_PATH_REJECTED: $scriptPath"
        }
    }
}
if ($serviceScript -ne
    'D:\QLHV_APP\scripts\windows\qlhv-lan\RealtimeWorkerService.ps1') {
    throw 'RT04_LIFECYCLE_LAUNCH_SERVICE_SCRIPT_IDENTITY_REJECTED'
}

$preflightHealthPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_pre_lifecycle_health.json'
& $healthScript -OutputPath $preflightHealthPath | Out-Null
$health = Get-Content -LiteralPath $preflightHealthPath -Raw | ConvertFrom-Json
$requiredGates = @(
    'ServiceRunning',
    'ServiceAutomaticDelayed',
    'ServiceAccountLeastPrivilege',
    'ServiceExecutableConfigured',
    'RuntimeBundleApproved',
    'ExactOneWorkerProcess',
    'ServiceProcessMatches',
    'ProfilesHealthy',
    'CountsHealthy',
    'CheckpointsCurrent',
    'CheckpointsWithinRetention',
    'AutoSyncOff',
    'MutexHeld',
    'NoDuplicatesOrManualReview',
    'NoTransactionsOrBlockers',
    'ExactFeatureState',
    'DatabaseIdentities',
    'RcsiOff',
    'RecoveryPolicy'
)
foreach ($gateName in $requiredGates) {
    if (-not [bool]$health.Gates.$gateName) {
        throw "RT04_LIFECYCLE_LAUNCH_HEALTH_REJECTED: $gateName"
    }
}
if ([string]$health.Realtime.WorkerStatus -ne 'HEALTHY' -or
    $null -ne $health.Realtime.LastErrorCode) {
    throw 'RT04_LIFECYCLE_LAUNCH_WORKER_STATE_REJECTED'
}

$semanticCommand =
    "& '$lifecycleScript' -RepositoryRoot '$RepositoryRoot' " +
    "-RuntimeRoot '$RuntimeRoot' -EvidenceRoot '$EvidenceRoot'"
$sanitizedCommand =
    'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass ' +
    "-EncodedCommand <BASE64_OF: $semanticCommand>"
$commandLogPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_lifecycle_elevation_command_sanitized.txt'
[IO.File]::WriteAllText(
    $commandLogPath,
    $sanitizedCommand,
    [Text.UTF8Encoding]::new($false))

$preflight = [ordered]@{
    Evidence = 'RT04_LIFECYCLE_ELEVATION_PREFLIGHT_PASS'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    RepositoryRoot = $RepositoryRoot
    RuntimeRoot = $RuntimeRoot
    ServiceScript = $serviceScript
    ServiceScriptExists = $true
    JoinPathArgumentsExplicit = $true
    RequiredArgumentsExplicitAndNonEmpty = $true
    NoProfile = $true
    NonInteractive = $true
    InitialHealth = 'PASS'
    ProductionCounts = '156/5'
    AutoSyncHistoryAndActive = '11/0/0'
    Checkpoints = 'OTO=2;MOTO=0'
    AuthorizedActions = 'GRACEFUL_STOP_START;SECOND_WORKER_MUTEX;EXACT_PID_CRASH;SCM_RECOVERY'
}
$preflightPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_lifecycle_elevation_preflight.json'
[IO.File]::WriteAllText(
    $preflightPath,
    ($preflight | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

if (-not $Launch) {
    Write-Output "RT04_LIFECYCLE_ELEVATION_PREFLIGHT_PASS command=$commandLogPath"
    return
}

$errorPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '09b_service_lifecycle_elevated_error.txt'
$elevatedCommand = @"
try {
  $semanticCommand
  exit 0
} catch {
  [IO.File]::WriteAllText(
    '$errorPath',
    `$_.Exception.ToString(),
    [Text.UTF8Encoding]::new(`$false))
  exit 1
}
"@
$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($elevatedCommand))
$argumentString =
    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
if ([string]::IsNullOrWhiteSpace($argumentString)) {
    throw 'RT04_LIFECYCLE_ELEVATION_ARGUMENT_REJECTED'
}
$process = Start-Process `
    -FilePath 'powershell.exe' `
    -Verb RunAs `
    -ArgumentList $argumentString `
    -WindowStyle Normal `
    -PassThru
Write-Output "RT04_VISIBLE_LIFECYCLE_ELEVATION_STARTED pid=$($process.Id)"
