[CmdletBinding()]
param(
    [string]$StagePath,
    [string]$RuntimeRoot,
    [string]$ServiceScriptPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($requiredValue in @($StagePath, $RuntimeRoot, $ServiceScriptPath)) {
    if ([string]::IsNullOrWhiteSpace($requiredValue)) {
        throw 'RT03_WORKER_DEPLOY_REQUIRED_ARGUMENT_EMPTY'
    }
}

$repositoryRoot = [IO.Path]::GetFullPath('D:\QLHV_APP').TrimEnd('\')
$expectedStage = [IO.Path]::GetFullPath(
    'D:\QLHV_APP\.runlogs\rt03-worker-recovery-stage-20260728-1010').TrimEnd('\')
$expectedRuntimeRoot = [IO.Path]::GetFullPath('D:\QLHV_APP_RUNTIME').TrimEnd('\')
$expectedServiceScript = [IO.Path]::GetFullPath(
    'D:\QLHV_APP\scripts\windows\qlhv-lan\RealtimeWorkerService.ps1')
$resolvedStage = [IO.Path]::GetFullPath($StagePath).TrimEnd('\')
$resolvedRuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
$resolvedServiceScript = [IO.Path]::GetFullPath($ServiceScriptPath)

if (-not [string]::Equals($resolvedStage, $expectedStage, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($resolvedRuntimeRoot, $expectedRuntimeRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($resolvedServiceScript, $expectedServiceScript, [StringComparison]::OrdinalIgnoreCase) -or
    -not $resolvedServiceScript.StartsWith($repositoryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT03_WORKER_DEPLOY_PATH_REJECTED'
}
if (-not (Test-Path -LiteralPath $resolvedStage -PathType Container) -or
    -not (Test-Path -LiteralPath (Join-Path -Path $resolvedStage -ChildPath 'QLHV.Worker.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath $resolvedServiceScript -PathType Leaf)) {
    throw 'RT03_WORKER_DEPLOY_INPUT_MISSING'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'RT03_WORKER_DEPLOY_ADMIN_REQUIRED'
}

. $resolvedServiceScript
$snapshot = Get-QlhvRealtimeWorkerServiceSnapshot -RuntimeRoot $resolvedRuntimeRoot
if (-not $snapshot.Exists -or $snapshot.WasRunning -or $snapshot.ProcessId -ne 0) {
    throw 'RT03_WORKER_DEPLOY_SERVICE_NOT_EXACTLY_STOPPED'
}

$targetDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $resolvedRuntimeRoot -ChildPath 'app\worker')).TrimEnd('\')
$runDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $resolvedRuntimeRoot -ChildPath 'run')).TrimEnd('\')
if (-not $targetDirectory.StartsWith($resolvedRuntimeRoot + '\app\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $runDirectory.StartsWith($resolvedRuntimeRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT03_WORKER_DEPLOY_TARGET_SCOPE_REJECTED'
}

$outsideWorker = @(Get-CimInstance Win32_Process | Where-Object {
    [string]::Equals(
        [string]$_.ExecutablePath,
        (Join-Path -Path $targetDirectory -ChildPath 'QLHV.Worker.exe'),
        [StringComparison]::OrdinalIgnoreCase)
})
if ($outsideWorker.Count -ne 0) {
    throw 'RT03_WORKER_DEPLOY_PROCESS_STILL_RUNNING'
}

$backupDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $runDirectory -ChildPath 'rt03-worker-pre-unsupported-drift-recovery-retry2'))
$failedDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $runDirectory -ChildPath 'rt03-worker-failed-unsupported-drift-recovery-retry2'))
if (-not $backupDirectory.StartsWith($runDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $failedDirectory.StartsWith($runDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $backupDirectory) -or
    (Test-Path -LiteralPath $failedDirectory)) {
    throw 'RT03_WORKER_DEPLOY_BACKUP_SCOPE_OR_EXISTENCE_REJECTED'
}

$movedOriginal = $false
try {
    if (Test-Path -LiteralPath $targetDirectory -PathType Container) {
        Move-Item -LiteralPath $targetDirectory -Destination $backupDirectory -ErrorAction Stop
        $movedOriginal = $true
    }
    New-Item -ItemType Directory -Path $targetDirectory -ErrorAction Stop | Out-Null
    Copy-Item -Path (Join-Path -Path $resolvedStage -ChildPath '*') `
        -Destination $targetDirectory -Recurse -Force -ErrorAction Stop
    if (-not (Test-Path -LiteralPath (Join-Path -Path $targetDirectory -ChildPath 'QLHV.Worker.exe') -PathType Leaf)) {
        throw 'RT03_WORKER_DEPLOY_OUTPUT_MISSING'
    }

    Start-QlhvRealtimeWorkerService -RuntimeRoot $resolvedRuntimeRoot
}
catch {
    try {
        Stop-QlhvRealtimeWorkerService -RuntimeRoot $resolvedRuntimeRoot
    }
    catch {
        # Preserve the original deployment failure.
    }
    if (Test-Path -LiteralPath $targetDirectory -PathType Container) {
        Move-Item -LiteralPath $targetDirectory -Destination $failedDirectory -ErrorAction SilentlyContinue
    }
    if ($movedOriginal -and (Test-Path -LiteralPath $backupDirectory -PathType Container)) {
        Move-Item -LiteralPath $backupDirectory -Destination $targetDirectory -ErrorAction SilentlyContinue
    }
    throw
}

Write-Output 'RT03_REALTIME_WORKER_RECOVERY_DEPLOYED_AND_STARTED'
