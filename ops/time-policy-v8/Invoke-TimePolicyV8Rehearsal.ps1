[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PreflightExecutable,

    [Parameter(Mandatory)]
    [string]$StrictPreflightScript,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$preflight = (Resolve-Path -LiteralPath $PreflightExecutable).Path
$strictScript = (Resolve-Path -LiteralPath $StrictPreflightScript).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Get-W32TimeReadOnlySnapshot {
    [pscustomobject]@{
        CapturedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Timezone = (Get-TimeZone).Id
        Service = (Get-Service W32Time -ErrorAction SilentlyContinue).Status.ToString()
        Peer = (Get-ItemProperty `
            -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Parameters' `
            -Name NtpServer).NtpServer
        MinPollInterval = (Get-ItemProperty `
            -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Config' `
            -Name MinPollInterval).MinPollInterval
        MaxPollInterval = (Get-ItemProperty `
            -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Config' `
            -Name MaxPollInterval).MaxPollInterval
        SpecialPollInterval = (Get-ItemProperty `
            -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient' `
            -Name SpecialPollInterval).SpecialPollInterval
    }
}

function New-Observation {
    param(
        [int]$LastSyncError = 0,
        [double]$SyncAgeSeconds = 60,
        [double]$DatabaseOffsetSeconds = 0,
        [bool]$WindowsTimeRunning = $true,
        [string]$ConfiguredPeer = 'time.windows.com,0x9',
        [string]$CurrentSource = 'time.windows.com,0x9',
        [double]$DurableFutureSeconds = -60,
        [double]$PhaseOffsetMilliseconds = 20,
        [int]$ConsecutiveProbeFailures = 0,
        [bool]$ProbeSucceeded = $true,
        [bool]$ClockRollback = $false
    )

    $now = [DateTimeOffset]::Parse('2026-07-31T12:00:00Z')
    [ordered]@{
        ApiUtcAtQueryStart = $now.ToString('o')
        ApiUtcAfterQuery = $now.AddMilliseconds(20).ToString('o')
        DatabaseUtcNow = $now.AddMilliseconds(10).
            AddSeconds($DatabaseOffsetSeconds).ToString('o')
        MonotonicQueryDuration = '00:00:00.0200000'
        LastPersistedSystemUtc = $now.AddSeconds($DurableFutureSeconds).ToString('o')
        ServerTimeZone = 'SE Asia Standard Time'
        WindowsTimeRunning = $WindowsTimeRunning
        ConfiguredPeer = $ConfiguredPeer
        CurrentSource = $CurrentSource
        TimeSinceLastGoodSync = [TimeSpan]::FromSeconds($SyncAgeSeconds).ToString('c')
        NtpPhaseOffsetMilliseconds = $PhaseOffsetMilliseconds
        LastSyncError = $LastSyncError
        InterObservationWallElapsed = if ($ClockRollback) {
            '-00:00:01'
        } else {
            '00:00:01'
        }
        InterObservationMonotonicElapsed = '00:00:01'
        EffectivePollInterval = '00:05:00'
        LastNtpProbeSucceeded = $ProbeSucceeded
        ConsecutiveNtpProbeFailures = $ConsecutiveProbeFailures
        LastNtpProbeAtUtc = $now.ToString('o')
    }
}

$before = Get-W32TimeReadOnlySnapshot
$scenarios = @(
    [pscustomobject]@{Name='healthy'; Observation=(New-Observation); Exit=0; Classification='TIME_HEALTHY'},
    [pscustomobject]@{Name='error2_before_poll'; Observation=(New-Observation -LastSyncError 2 -SyncAgeSeconds 272); Exit=0; Classification='TIME_SAFE_WARNING'},
    [pscustomobject]@{Name='error2_fresh_good_skew'; Observation=(New-Observation -LastSyncError 2 -SyncAgeSeconds 60); Exit=0; Classification='TIME_SAFE_WARNING'},
    [pscustomobject]@{Name='poll_late_in_grace'; Observation=(New-Observation -LastSyncError 2 -SyncAgeSeconds 330); Exit=0; Classification='TIME_SAFE_WARNING'},
    [pscustomobject]@{Name='one_poll_missed'; Observation=(New-Observation -LastSyncError 2 -SyncAgeSeconds 400); Exit=0; Classification='TIME_SAFE_WARNING'},
    [pscustomobject]@{Name='two_polls_missed'; Observation=(New-Observation -LastSyncError 2 -SyncAgeSeconds 675); Exit=12; Classification='TIME_HEALTH_BLOCKED'},
    [pscustomobject]@{Name='last_sync_stale'; Observation=(New-Observation -SyncAgeSeconds 700); Exit=12; Classification='TIME_HEALTH_BLOCKED'},
    [pscustomobject]@{Name='ntp_probes_failed'; Observation=(New-Observation -ConsecutiveProbeFailures 3 -ProbeSucceeded $false); Exit=12; Classification='TIME_HEALTH_BLOCKED'},
    [pscustomobject]@{Name='w32time_stopped'; Observation=(New-Observation -WindowsTimeRunning $false); Exit=12; Classification='TIME_HEALTH_BLOCKED'},
    [pscustomobject]@{Name='source_mismatch'; Observation=(New-Observation -CurrentSource 'unapproved.example,0x9'); Exit=18; Classification='APPROVED_PEER_MISMATCH'},
    [pscustomobject]@{Name='durable_future'; Observation=(New-Observation -DurableFutureSeconds 31); Exit=19; Classification='DURABLE_UTC_FUTURE'},
    [pscustomobject]@{Name='clock_rollback'; Observation=(New-Observation -ClockRollback $true); Exit=12; Classification='TIME_HEALTH_BLOCKED'},
    [pscustomobject]@{Name='clock_skew_blocked'; Observation=(New-Observation -DatabaseOffsetSeconds 31); Exit=20; Classification='CLOCK_SKEW_EXCEEDED'},
    [pscustomobject]@{Name='unsafe_phase_warning'; Observation=(New-Observation -PhaseOffsetMilliseconds 2500); Exit=20; Classification='CLOCK_SKEW_EXCEEDED'}
)

$results = @()
foreach ($scenario in $scenarios) {
    $fixturePath = Join-Path $outputRoot "$($scenario.Name).observation.json"
    $resultPath = Join-Path $outputRoot "$($scenario.Name).result.json"
    $scenario.Observation |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $fixturePath -Encoding UTF8

    & $preflight `
        --mode standalone `
        --observation-file $fixturePath `
        --output $resultPath | Out-Null
    $actualExit = $LASTEXITCODE
    $actual = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $passed = $actualExit -eq $scenario.Exit -and
        [int]$actual.exitCode -eq $scenario.Exit -and
        [string]$actual.classification -eq $scenario.Classification
    $results += [pscustomobject]@{
        Name = $scenario.Name
        ExpectedExitCode = $scenario.Exit
        ActualExitCode = $actualExit
        ExpectedClassification = $scenario.Classification
        ActualClassification = [string]$actual.classification
        Passed = $passed
    }
}

$safeFixture = Join-Path $outputRoot 'error2_before_poll.observation.json'
$safeStrictOutput = Join-Path $outputRoot 'strict-safe-warning.json'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $strictScript `
    -Mode standalone `
    -PreflightExecutable $preflight `
    -ObservationFile $safeFixture `
    -OutputPath $safeStrictOutput | Out-Null
$safeStrictExit = $LASTEXITCODE

$unsafeFixture = Join-Path $outputRoot 'unsafe_phase_warning.observation.json'
$unsafeStrictOutput = Join-Path $outputRoot 'strict-unsafe-warning.json'
$unsafeStrictLog = Join-Path $outputRoot 'strict-unsafe-warning.log'
$savedErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $strictScript `
    -Mode standalone `
    -PreflightExecutable $preflight `
    -ObservationFile $unsafeFixture `
    -OutputPath $unsafeStrictOutput *> $unsafeStrictLog
$unsafeStrictExit = $LASTEXITCODE
$ErrorActionPreference = $savedErrorActionPreference

$after = Get-W32TimeReadOnlySnapshot
$w32TimeUnchanged =
    $before.Timezone -eq $after.Timezone -and
    $before.Peer -eq $after.Peer -and
    $before.MinPollInterval -eq $after.MinPollInterval -and
    $before.MaxPollInterval -eq $after.MaxPollInterval -and
    $before.SpecialPollInterval -eq $after.SpecialPollInterval
$overallPass =
    @($results | Where-Object { -not $_.Passed }).Count -eq 0 -and
    $safeStrictExit -eq 0 -and
    $unsafeStrictExit -ne 0 -and
    $w32TimeUnchanged

$report = [pscustomobject]@{
    Contract = 'QLHV_TIME_POLICY_V8_DISPOSABLE_REHEARSAL_1.1'
    CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    ScenarioCount = $results.Count
    PassedScenarioCount = @($results | Where-Object Passed).Count
    Results = $results
    StrictSafeWarningExitCode = $safeStrictExit
    StrictUnsafeWarningExitCode = $unsafeStrictExit
    W32TimeBefore = $before
    W32TimeAfter = $after
    W32TimeUnchanged = $w32TimeUnchanged
    ProductionDeployment = $false
    DatabaseMutation = $false
    W32TimeMutation = $false
    OverallPass = $overallPass
}
$reportPath = Join-Path $outputRoot 'REHEARSAL_RESULT.json'
$report |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $reportPath -Encoding UTF8
$report | ConvertTo-Json -Depth 10
if (-not $overallPass) {
    exit 1
}
exit 0
