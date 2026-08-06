[CmdletBinding()]
param(
    [string]$StagePath,
    [string]$RuntimeRoot,
    [int]$ExpectedApiProcessId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($requiredValue in @($StagePath, $RuntimeRoot)) {
    if ([string]::IsNullOrWhiteSpace($requiredValue)) {
        throw 'RT03_API_UI_DEPLOY_REQUIRED_ARGUMENT_EMPTY'
    }
}
if ($ExpectedApiProcessId -le 0) {
    throw 'RT03_API_UI_DEPLOY_API_PID_EMPTY'
}

$expectedStage = [IO.Path]::GetFullPath(
    'D:\QLHV_APP\.runlogs\rt03-api-ui-stage-20260728-1100').TrimEnd('\')
$expectedRuntimeRoot = [IO.Path]::GetFullPath('D:\QLHV_APP_RUNTIME').TrimEnd('\')
$resolvedStage = [IO.Path]::GetFullPath($StagePath).TrimEnd('\')
$resolvedRuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
if (-not [string]::Equals($resolvedStage, $expectedStage, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($resolvedRuntimeRoot, $expectedRuntimeRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT03_API_UI_DEPLOY_PATH_REJECTED'
}
if (-not (Test-Path -LiteralPath $resolvedStage -PathType Container) -or
    -not (Test-Path -LiteralPath (Join-Path -Path $resolvedStage -ChildPath 'QLHV.Api.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path -Path $resolvedStage -ChildPath 'wwwroot\index.html') -PathType Leaf) -or
    (Test-Path -LiteralPath (Join-Path -Path $resolvedStage -ChildPath 'worker'))) {
    throw 'RT03_API_UI_DEPLOY_STAGE_REJECTED'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'RT03_API_UI_DEPLOY_ADMIN_REQUIRED'
}

$runtimeApp = [IO.Path]::GetFullPath(
    (Join-Path -Path $resolvedRuntimeRoot -ChildPath 'app')).TrimEnd('\')
$workerDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $runtimeApp -ChildPath 'worker')).TrimEnd('\')
$workerExecutable = Join-Path -Path $workerDirectory -ChildPath 'QLHV.Worker.exe'
$runDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $resolvedRuntimeRoot -ChildPath 'run')).TrimEnd('\')
if (-not $runtimeApp.StartsWith($resolvedRuntimeRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $workerDirectory.StartsWith($runtimeApp + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $runDirectory.StartsWith($resolvedRuntimeRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not (Test-Path -LiteralPath $workerExecutable -PathType Leaf)) {
    throw 'RT03_API_UI_DEPLOY_RUNTIME_SCOPE_REJECTED'
}

$service = Get-CimInstance Win32_Service -Filter "Name='QLHV_APP_RealtimeWorker'"
if ($null -eq $service -or $service.State -ne 'Running' -or [int]$service.ProcessId -le 0) {
    throw 'RT03_API_UI_DEPLOY_REALTIME_NOT_RUNNING'
}
$workerProcessId = [int]$service.ProcessId
$workerHashBefore = (Get-FileHash -Algorithm SHA256 $workerExecutable).Hash

$apiExecutable = [IO.Path]::GetFullPath(
    (Join-Path -Path $runtimeApp -ChildPath 'QLHV.Api.exe'))
$apiProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$ExpectedApiProcessId"
if ($null -eq $apiProcess -or
    -not [string]::Equals(
        [IO.Path]::GetFullPath([string]$apiProcess.ExecutablePath),
        $apiExecutable,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT03_API_UI_DEPLOY_API_IDENTITY_REJECTED'
}
$listeners = @(Get-NetTCPConnection -State Listen -LocalPort 8088 -ErrorAction Stop)
if ($listeners.Count -ne 1 -or [int]$listeners[0].OwningProcess -ne $ExpectedApiProcessId) {
    throw 'RT03_API_UI_DEPLOY_LISTENER_REJECTED'
}

$backupDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $runDirectory -ChildPath 'rt03-api-ui-pre-deployment-worker-process-fix'))
$failedDirectory = [IO.Path]::GetFullPath(
    (Join-Path -Path $runDirectory -ChildPath 'rt03-api-ui-failed-deployment-worker-process-fix'))
if (-not $backupDirectory.StartsWith($runDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $failedDirectory.StartsWith($runDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $backupDirectory) -or
    (Test-Path -LiteralPath $failedDirectory)) {
    throw 'RT03_API_UI_DEPLOY_BACKUP_SCOPE_OR_EXISTENCE_REJECTED'
}

Stop-Process -Id $ExpectedApiProcessId -Force -ErrorAction Stop
$stopDeadline = [DateTime]::UtcNow.AddSeconds(15)
do {
    $remainingProcess = Get-CimInstance Win32_Process `
        -Filter "ProcessId=$ExpectedApiProcessId" `
        -ErrorAction SilentlyContinue
    $remainingListener = @(Get-NetTCPConnection `
        -State Listen `
        -LocalPort 8088 `
        -ErrorAction SilentlyContinue | Where-Object {
            [int]$_.OwningProcess -eq $ExpectedApiProcessId
        })
    if ($null -eq $remainingProcess -and $remainingListener.Count -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $stopDeadline)
if ($null -ne $remainingProcess -or $remainingListener.Count -ne 0) {
    throw 'RT03_API_UI_DEPLOY_API_STOP_FAILED'
}

New-Item -ItemType Directory -Path $backupDirectory -ErrorAction Stop | Out-Null
$movedItems = $false
try {
    $runtimeItems = @(Get-ChildItem -LiteralPath $runtimeApp -Force | Where-Object {
        -not [string]::Equals($_.Name, 'worker', [StringComparison]::OrdinalIgnoreCase)
    })
    foreach ($item in $runtimeItems) {
        $resolvedItem = [IO.Path]::GetFullPath($item.FullName)
        if (-not $resolvedItem.StartsWith($runtimeApp + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'RT03_API_UI_DEPLOY_CHILD_SCOPE_REJECTED'
        }
        Move-Item -LiteralPath $resolvedItem -Destination $backupDirectory -ErrorAction Stop
    }
    $movedItems = $true

    foreach ($item in @(Get-ChildItem -LiteralPath $resolvedStage -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $runtimeApp -Recurse -Force -ErrorAction Stop
    }
    if (-not (Test-Path -LiteralPath $apiExecutable -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path -Path $runtimeApp -ChildPath 'wwwroot\index.html') -PathType Leaf)) {
        throw 'RT03_API_UI_DEPLOY_OUTPUT_MISSING'
    }

    $serviceAfter = Get-CimInstance Win32_Service -Filter "Name='QLHV_APP_RealtimeWorker'"
    $workerHashAfter = (Get-FileHash -Algorithm SHA256 $workerExecutable).Hash
    if ($serviceAfter.State -ne 'Running' -or
        [int]$serviceAfter.ProcessId -ne $workerProcessId -or
        -not [string]::Equals($workerHashAfter, $workerHashBefore, [StringComparison]::Ordinal)) {
        throw 'RT03_API_UI_DEPLOY_REALTIME_CHANGED'
    }
}
catch {
    if ($movedItems) {
        New-Item -ItemType Directory -Path $failedDirectory -ErrorAction SilentlyContinue | Out-Null
        foreach ($item in @(Get-ChildItem -LiteralPath $runtimeApp -Force | Where-Object {
            -not [string]::Equals($_.Name, 'worker', [StringComparison]::OrdinalIgnoreCase)
        })) {
            Move-Item -LiteralPath $item.FullName -Destination $failedDirectory -ErrorAction SilentlyContinue
        }
        foreach ($item in @(Get-ChildItem -LiteralPath $backupDirectory -Force)) {
            Move-Item -LiteralPath $item.FullName -Destination $runtimeApp -ErrorAction SilentlyContinue
        }
    }
    throw
}

Write-Output 'RT03_API_UI_DEPLOYED_WORKER_UNTOUCHED'
