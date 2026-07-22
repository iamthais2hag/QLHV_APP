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

if (-not (Test-Path -LiteralPath $PidFile -PathType Leaf)) {
    if (-not $Quiet) {
        Write-Host 'QLHV is not running (no runtime PID file).'
    }
    return
}

$rawPid = (Get-Content -Raw -LiteralPath $PidFile).Trim()
$runtimeProcessId = 0
if (-not [int]::TryParse($rawPid, [ref]$runtimeProcessId) -or $runtimeProcessId -le 0) {
    throw "The QLHV PID file is invalid: $PidFile. No process was stopped."
}

$record = Get-ProcessRecord -ProcessId $runtimeProcessId
if ($null -eq $record) {
    Remove-Item -LiteralPath $PidFile -Force
    if (-not $Quiet) {
        Write-Host "QLHV process $runtimeProcessId is no longer running. Removed the stale PID file."
    }
    return
}

if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
    throw "PID $runtimeProcessId does not belong to D:\QLHV_APP_RUNTIME\app\QLHV.Api. No process was stopped."
}

# Validate the PID and executable path immediately before stopping. Never stop all dotnet/node processes.
$record = Get-ProcessRecord -ProcessId $runtimeProcessId
if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
    throw "QLHV process identity changed before stop. No process was stopped."
}
$liveProcess = Get-Process -Id $runtimeProcessId -ErrorAction Stop
$liveExecutablePath = Get-NormalizedPath $liveProcess.Path
$recordedExecutablePath = Get-NormalizedPath ([string]$record.ExecutablePath)
if (-not [string]::Equals(
        $liveExecutablePath,
        $recordedExecutablePath,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "QLHV process executable path changed before stop. No process was stopped."
}

Stop-Process -Id $runtimeProcessId -Force -ErrorAction Stop
try {
    Wait-Process -Id $runtimeProcessId -Timeout 15 -ErrorAction Stop
}
catch {
    if (Get-Process -Id $runtimeProcessId -ErrorAction SilentlyContinue) {
        throw "QLHV PID $runtimeProcessId could not be stopped."
    }
}

Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
if (-not $Quiet) {
    Write-Host "Stopped QLHV runtime PID $runtimeProcessId."
}
