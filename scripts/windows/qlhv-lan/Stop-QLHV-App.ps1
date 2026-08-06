[CmdletBinding()]
param(
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$PidFile = Join-Path $RunDirectory 'qlhv.pid'
$RealtimeWorkerServiceScript = Join-Path $PSScriptRoot 'RealtimeWorkerService.ps1'
. $RealtimeWorkerServiceScript

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Get-ProcessRecord {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    return Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
}

function Test-IsQlhvRuntimeProcess {
    param([Parameter(Mandatory = $false)]$ProcessRecord)

    if ($null -eq $ProcessRecord -or [string]::IsNullOrWhiteSpace([string]$ProcessRecord.ExecutablePath)) {
        return $false
    }

    $actualExecutable = Get-NormalizedPath ([string]$ProcessRecord.ExecutablePath)
    $publishedExecutable = Get-NormalizedPath (Join-Path $AppDirectory 'QLHV.Api.exe')
    if ([string]::Equals($actualExecutable, $publishedExecutable, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $publishedDll = Get-NormalizedPath (Join-Path $AppDirectory 'QLHV.Api.dll')
    $commandLine = [string]$ProcessRecord.CommandLine
    $dllArgumentPattern = '(?i)(?:^|\s)"?' + [Regex]::Escape($publishedDll) + '"?(?:\s|$)'
    return [string]::Equals(
            [System.IO.Path]::GetFileName($actualExecutable),
            'dotnet.exe',
            [System.StringComparison]::OrdinalIgnoreCase) -and
        [Regex]::IsMatch($commandLine, $dllArgumentPattern)
}

function Get-QlhvRuntimeProcessIds {
    return @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { Test-IsQlhvRuntimeProcess -ProcessRecord $_ } |
        Select-Object -ExpandProperty ProcessId -Unique)
}

function Stop-VerifiedQlhvProcess {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $record = Get-ProcessRecord -ProcessId $ProcessId
    if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
        throw "PID $ProcessId does not belong to D:\QLHV_APP_RUNTIME\app\QLHV.Api. No process was stopped."
    }

    # Validate PID, command line and executable path again immediately before stop.
    $record = Get-ProcessRecord -ProcessId $ProcessId
    if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
        throw "QLHV process identity changed before stop. No process was stopped."
    }
    $liveProcess = Get-Process -Id $ProcessId -ErrorAction Stop
    $liveExecutablePath = Get-NormalizedPath $liveProcess.Path
    $recordedExecutablePath = Get-NormalizedPath ([string]$record.ExecutablePath)
    if (-not [string]::Equals(
        $liveExecutablePath,
        $recordedExecutablePath,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "QLHV process executable path changed before stop. No process was stopped."
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction Stop
    try {
        Wait-Process -Id $ProcessId -Timeout 15 -ErrorAction Stop
    }
    catch {
        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
            throw "QLHV PID $ProcessId could not be stopped."
        }
    }
}

# Stop the exact Windows service before replacing or removing its published
# binaries. Identity/path validation in the helper prevents a broad service stop.
$workerSnapshot = Get-QlhvRealtimeWorkerServiceSnapshot -RuntimeRoot $RuntimeRoot
Stop-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot

$runtimeIds = @(Get-QlhvRuntimeProcessIds)
$recordedId = 0
if (Test-Path -LiteralPath $PidFile -PathType Leaf) {
    $rawPid = (Get-Content -Raw -LiteralPath $PidFile -ErrorAction SilentlyContinue).Trim()
    [void][int]::TryParse($rawPid, [ref]$recordedId)
}

# Prefer the recorded exact runtime, then reconcile every other exact published
# runtime process. A stale/missing PID file never causes a broad dotnet kill.
$orderedIds = @()
if ($recordedId -gt 0 -and $runtimeIds -contains $recordedId) {
    $orderedIds += $recordedId
}
$orderedIds += @($runtimeIds | Where-Object { $_ -ne $recordedId })
$orderedIds = @($orderedIds | Select-Object -Unique)

foreach ($runtimeProcessId in $orderedIds) {
    Stop-VerifiedQlhvProcess -ProcessId ([int]$runtimeProcessId)
}

Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
if (-not $Quiet) {
    if ($orderedIds.Count -eq 0) {
        Write-Host 'QLHV is not running. Any stale runtime PID file was removed.'
    }
    else {
        Write-Host "Stopped verified QLHV runtime PID(s): $($orderedIds -join ', ')."
    }
    if ([bool]$workerSnapshot.Exists -and [bool]$workerSnapshot.WasRunning) {
        Write-Host 'Stopped Windows service QLHV_APP_RealtimeWorker.'
    }
}
