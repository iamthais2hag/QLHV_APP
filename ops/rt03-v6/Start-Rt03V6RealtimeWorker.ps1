[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$serviceName = 'QLHV_APP_RealtimeWorker'
$expectedPath = 'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe'

$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if ($null -eq $service) {
    throw "Service '$serviceName' was not found."
}

$actualPath = ([string]$service.PathName).Trim().Trim('"')
if ($service.State -ne 'Stopped' -or
    [int]$service.ProcessId -ne 0 -or
    $actualPath -ne $expectedPath) {
    throw 'Worker start precondition failed: state, PID, or executable path changed.'
}

Start-Service -Name $serviceName

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 250
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
} while (
    [DateTimeOffset]::UtcNow -lt $deadline -and
    ($service.State -ne 'Running' -or [int]$service.ProcessId -le 0)
)

$result = [ordered]@{
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    ServiceName = $serviceName
    State = [string]$service.State
    ProcessId = [int]$service.ProcessId
    PathName = [string]$service.PathName
    PathExact = (([string]$service.PathName).Trim().Trim('"') -eq $expectedPath)
    StartMode = [string]$service.StartMode
}

$parent = Split-Path -Parent $EvidencePath
if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
    throw "Evidence directory '$parent' does not exist."
}
$result | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath $EvidencePath -Encoding UTF8

if ($service.State -ne 'Running' -or
    [int]$service.ProcessId -le 0 -or
    -not [bool]$result.PathExact) {
    throw 'Worker did not reach the exact Running/PID/path contract.'
}

$result | ConvertTo-Json -Depth 4
