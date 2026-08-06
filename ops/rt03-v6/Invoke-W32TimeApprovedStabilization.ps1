[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$approvedPeer = 'time.windows.com,0x9'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'W32Time stabilization requires an elevated process.'
}

$timezoneBefore = (Get-TimeZone).Id
$parametersPath =
    'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Parameters'
$parametersBefore = Get-ItemProperty -LiteralPath $parametersPath
if ([string]$parametersBefore.NtpServer -ne $approvedPeer -or
    [string]$parametersBefore.Type -ne 'NTP') {
    throw 'Approved W32Time peer/type precondition failed.'
}

function Wait-ServiceState {
    param(
        [Parameter(Mandatory)][string]$ExpectedState,
        [int]$TimeoutSeconds = 30
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $service = Get-CimInstance Win32_Service -Filter "Name='W32Time'"
        if ([string]$service.State -eq $ExpectedState) {
            return $service
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "W32Time did not reach $ExpectedState within $TimeoutSeconds seconds."
}

$stopOutput = & sc.exe stop W32Time 2>&1 | Out-String
$stopExit = $LASTEXITCODE
if ($stopExit -ne 0) {
    throw "Stopping W32Time failed with exit code $stopExit."
}
[void](Wait-ServiceState -ExpectedState 'Stopped')

$startOutput = & sc.exe start W32Time 2>&1 | Out-String
$startExit = $LASTEXITCODE
if ($startExit -ne 0) {
    throw "Starting W32Time failed with exit code $startExit."
}
$running = Wait-ServiceState -ExpectedState 'Running'

$resyncOutput = & w32tm.exe /resync /rediscover 2>&1 | Out-String
$resyncExit = $LASTEXITCODE
$statusOutput = & w32tm.exe /query /status /verbose 2>&1 | Out-String
$statusExit = $LASTEXITCODE

$parametersAfter = Get-ItemProperty -LiteralPath $parametersPath
$timezoneAfter = (Get-TimeZone).Id
$lastSyncErrorMatch = [regex]::Match(
    $statusOutput,
    '(?im)^Last Sync Error:\s*(?<code>\d+)')
$sourceMatch = [regex]::Match(
    $statusOutput,
    '(?im)^Source:\s*(?<value>.+?)\s*$')
$lastSyncError = if ($lastSyncErrorMatch.Success) {
    [int]$lastSyncErrorMatch.Groups['code'].Value
} else {
    $null
}
$source = if ($sourceMatch.Success) {
    $sourceMatch.Groups['value'].Value.Trim()
} else {
    $null
}

$passed = $resyncExit -eq 0 -and
    $statusExit -eq 0 -and
    $lastSyncError -eq 0 -and
    $source -eq $approvedPeer -and
    [string]$parametersAfter.NtpServer -eq $approvedPeer -and
    [string]$parametersAfter.Type -eq 'NTP' -and
    $timezoneAfter -eq $timezoneBefore -and
    [string]$running.State -eq 'Running'

$result = [ordered]@{
    ContractVersion = 'QLHV_W32TIME_APPROVED_STABILIZATION_V1'
    CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    ElevatedIdentity = $identity.Name
    ApprovedPeer = $approvedPeer
    PeerBefore = [string]$parametersBefore.NtpServer
    PeerAfter = [string]$parametersAfter.NtpServer
    TypeBefore = [string]$parametersBefore.Type
    TypeAfter = [string]$parametersAfter.Type
    TimeZoneBefore = $timezoneBefore
    TimeZoneAfter = $timezoneAfter
    ManualClockChange = $false
    RegistryChange = $false
    DurableStateChange = $false
    StopExitCode = $stopExit
    StartExitCode = $startExit
    ResyncExitCode = $resyncExit
    StatusExitCode = $statusExit
    ServiceState = [string]$running.State
    ServicePid = [int]$running.ProcessId
    Source = $source
    LastSyncError = $lastSyncError
    StopOutput = $stopOutput.Trim()
    StartOutput = $startOutput.Trim()
    ResyncOutput = $resyncOutput.Trim()
    StatusOutput = $statusOutput.Trim()
    Passed = $passed
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $resolvedOutput
[IO.Directory]::CreateDirectory($parent) | Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    ($result | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))

if (-not $passed) {
    exit 40
}
