[CmdletBinding()]
param(
    [string]$PreflightExecutable =
        'D:\QLHV_APP\server\QLHV.TimeHealth.Preflight\bin\Release\net8.0\QLHV.TimeHealth.Preflight.exe',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PreflightExecutable -PathType Leaf)) {
    throw "Preflight executable not found: $PreflightExecutable"
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'qlhv-time-health-v6-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

function Write-JsonFixture {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)]$Value
    )
    $path = Join-Path $temporaryRoot $Name
    [IO.File]::WriteAllText(
        $path,
        ($Value | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    return $path
}

function New-HealthyTime {
    param([Parameter(Mandatory)][DateTimeOffset]$Now)
    return [ordered]@{
        timeHealth = 'HEALTHY'
        health = 'HEALTHY'
        reasonCode = 'NONE'
        writesAllowed = $true
        serverUtcNow = $Now.ToString('o')
        databaseUtcNow = $Now.ToString('o')
        durableLastObservedUtc = $Now.AddMinutes(-1).ToString('o')
        clockSkewMilliseconds = 0.0
        monotonicQueryMilliseconds = 20.0
        timeZone = 'SE Asia Standard Time'
        displayTimeZone = 'Asia/Ho_Chi_Minh'
        windowsTimeServiceState = 'Running'
        configuredPeer = 'time.windows.com,0x9'
        currentSource = 'time.windows.com,0x9'
        lastSuccessfulSyncUtc = $Now.AddMinutes(-1).ToString('o')
        lastSyncError = 0
        phaseOffsetMilliseconds = 20.0
        evaluatedAtUtc = $Now.ToString('o')
        messages = @()
    }
}

function New-HealthyContract {
    param([Parameter(Mandatory)][DateTimeOffset]$Now)
    return [ordered]@{
        timeContractVersion = '1.0'
        time = New-HealthyTime -Now $Now
    }
}

function New-Observation {
    param(
        [Parameter(Mandatory)][DateTimeOffset]$Now,
        [int]$LastSyncError = 0
    )
    $start = $Now.AddMilliseconds(-20)
    return [ordered]@{
        ApiUtcAtQueryStart = $start.ToString('o')
        ApiUtcAfterQuery = $Now.ToString('o')
        DatabaseUtcNow = $start.AddMilliseconds(10).ToString('o')
        MonotonicQueryDuration = '00:00:00.0200000'
        LastPersistedSystemUtc = $start.AddMinutes(-1).ToString('o')
        ServerTimeZone = 'SE Asia Standard Time'
        WindowsTimeRunning = $true
        ConfiguredPeer = 'time.windows.com,0x9'
        CurrentSource = 'time.windows.com,0x9'
        TimeSinceLastGoodSync = '01:00:00'
        NtpPhaseOffsetMilliseconds = 20.0
        LastSyncError = $LastSyncError
        InterObservationWallElapsed = $null
        InterObservationMonotonicElapsed = $null
    }
}

function Invoke-PreflightCase {
    param(
        [Parameter(Mandatory)][string]$Scenario,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][int]$ExpectedExitCode,
        [Parameter(Mandatory)][string]$ExpectedClassification
    )
    $raw = & $PreflightExecutable @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $response = $raw | ConvertFrom-Json
    return [pscustomobject]@{
        Scenario = $Scenario
        ExitCode = $exitCode
        Classification = [string]$response.classification
        ExpectedExitCode = $ExpectedExitCode
        ExpectedClassification = $ExpectedClassification
        Passed = $exitCode -eq $ExpectedExitCode -and
            [string]$response.classification -eq $ExpectedClassification
        ReadOnly = [bool]$response.readOnly
        DatabaseMutation = [bool]$response.databaseMutation
        StateMutation = [bool]$response.stateMutation
    }
}

try {
    $results = [Collections.Generic.List[object]]::new()
    $now = [DateTimeOffset]::UtcNow

    $oldContract = Write-JsonFixture -Name 'old-runtime-status.json' -Value (
        [ordered]@{ isReady = $true; version = 'old-production-api' })
    $healthyContract = Write-JsonFixture -Name 'healthy-contract.json' -Value (
        New-HealthyContract -Now $now)
    $stale = New-HealthyContract -Now $now
    $stale.time.evaluatedAtUtc = $now.AddMinutes(-1).ToString('o')
    $staleContract = Write-JsonFixture -Name 'stale-contract.json' -Value $stale
    $errorTwo = New-HealthyContract -Now $now
    $errorTwo.time.lastSyncError = 2
    $errorTwo.time.timeHealth = 'BLOCKED'
    $errorTwo.time.health = 'BLOCKED'
    $errorTwo.time.reasonCode = 'LAST_SYNC_ERROR'
    $errorTwo.time.writesAllowed = $false
    $errorTwoContract = Write-JsonFixture -Name 'error-two-contract.json' -Value $errorTwo
    $versionMismatch = New-HealthyContract -Now $now
    $versionMismatch.timeContractVersion = '0.9'
    $versionMismatchContract = Write-JsonFixture `
        -Name 'version-mismatch.json' -Value $versionMismatch
    $healthyObservation = Write-JsonFixture `
        -Name 'healthy-observation.json' -Value (
            New-Observation -Now $now)
    $errorTwoObservation = Write-JsonFixture `
        -Name 'error-two-observation.json' -Value (
            New-Observation -Now $now -LastSyncError 2)

    $results.Add((Invoke-PreflightCase `
        -Scenario 'A_OLD_API_TIME_OBJECT_MISSING' `
        -Arguments @('--mode','api','--contract-file',$oldContract) `
        -ExpectedExitCode 11 -ExpectedClassification 'TIME_OBJECT_MISSING'))
    $results.Add((Invoke-PreflightCase `
        -Scenario 'B_NEW_API_HEALTHY' `
        -Arguments @('--mode','api','--contract-file',$healthyContract) `
        -ExpectedExitCode 0 -ExpectedClassification 'TIME_HEALTHY'))
    $results.Add((Invoke-PreflightCase `
        -Scenario 'C_API_ROLLBACK_FAILS_CLOSED' `
        -Arguments @('--mode','api','--contract-file',$oldContract) `
        -ExpectedExitCode 11 -ExpectedClassification 'TIME_OBJECT_MISSING'))
    $results.Add((Invoke-PreflightCase `
        -Scenario 'D_STALE_TIME_OBJECT' `
        -Arguments @('--mode','api','--contract-file',$staleContract) `
        -ExpectedExitCode 16 -ExpectedClassification 'TIME_CONTRACT_STALE'))
    $results.Add((Invoke-PreflightCase `
        -Scenario 'E_LAST_SYNC_ERROR_NONZERO' `
        -Arguments @('--mode','api','--contract-file',$errorTwoContract) `
        -ExpectedExitCode 17 -ExpectedClassification 'LAST_SYNC_ERROR'))
    $results.Add((Invoke-PreflightCase `
        -Scenario 'F_HEALTHY_ALLOWS_RECOVERY_PREFLIGHT' `
        -Arguments @('--mode','standalone','--observation-file',$healthyObservation) `
        -ExpectedExitCode 0 -ExpectedClassification 'TIME_HEALTHY'))
    $results.Add((Invoke-PreflightCase `
        -Scenario 'G_TIME_REGRESSES_BEFORE_WORKER_START' `
        -Arguments @('--mode','standalone','--observation-file',$errorTwoObservation) `
        -ExpectedExitCode 17 -ExpectedClassification 'LAST_SYNC_ERROR'))

    $standaloneWhenApiDown = Invoke-PreflightCase `
        -Scenario 'H_STANDALONE_AUTHORITY_WHEN_API_DOWN' `
        -Arguments @('--mode','standalone','--observation-file',$healthyObservation) `
        -ExpectedExitCode 0 -ExpectedClassification 'TIME_HEALTHY'
    $apiUnavailable = Invoke-PreflightCase `
        -Scenario 'H_API_UNAVAILABLE_DIAGNOSTIC' `
        -Arguments @('--mode','api','--api-uri','http://127.0.0.1:1/runtime-status') `
        -ExpectedExitCode 10 -ExpectedClassification 'API_UNAVAILABLE'
    $standaloneWhenApiDown | Add-Member -NotePropertyName ApiDiagnosticExitCode `
        -NotePropertyValue $apiUnavailable.ExitCode
    $standaloneWhenApiDown | Add-Member -NotePropertyName ApiDiagnosticClassification `
        -NotePropertyValue $apiUnavailable.Classification
    $standaloneWhenApiDown.Passed = $standaloneWhenApiDown.Passed -and
        $apiUnavailable.Passed
    $results.Add($standaloneWhenApiDown)

    $results.Add((Invoke-PreflightCase `
        -Scenario 'I_CONTRACT_VERSION_MISMATCH' `
        -Arguments @('--mode','api','--contract-file',$versionMismatchContract) `
        -ExpectedExitCode 14 -ExpectedClassification 'CONTRACT_VERSION_MISMATCH'))

    $divergentContract = New-HealthyContract -Now $now
    $divergentContract.time.serverUtcNow = $now.ToString('o')
    $divergentContract.time.databaseUtcNow =
        $now.AddMilliseconds(-10).ToString('o')
    $divergentContract.time.durableLastObservedUtc =
        $now.AddMilliseconds(-20).AddMinutes(-1).ToString('o')
    $divergentContract.time.lastSuccessfulSyncUtc =
        $now.AddHours(-1).ToString('o')
    $divergentContract.time.health = 'BLOCKED'
    $divergentContract.time.timeHealth = 'BLOCKED'
    $divergentContract.time.reasonCode = 'EVALUATION_UNAVAILABLE'
    $divergentContract.time.writesAllowed = $false
    $divergentPath = Write-JsonFixture `
        -Name 'divergent-contract.json' -Value $divergentContract
    $results.Add((Invoke-PreflightCase `
        -Scenario 'J_SAME_INPUT_DIFFERENT_POLICY_DECISION' `
        -Arguments @(
            '--mode','both',
            '--observation-file',$healthyObservation,
            '--contract-file',$divergentPath) `
        -ExpectedExitCode 25 -ExpectedClassification 'TIME_POLICY_DIVERGENCE'))

    $passed = @($results | Where-Object { -not $_.Passed }).Count -eq 0
    $report = [ordered]@{
        ContractVersion = 'RT03_TIME_HEALTH_REHEARSAL_V6'
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Disposable = $true
        ProductionDatabaseRead = $false
        ProductionDatabaseMutation = $false
        ProductionStateMutation = $false
        Passed = $passed
        ScenarioCount = $results.Count
        Results = $results
    }
    $json = $report | ConvertTo-Json -Depth 8
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolved = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory((Split-Path -Parent $resolved)) | Out-Null
        [IO.File]::WriteAllText(
            $resolved,
            $json,
            [Text.UTF8Encoding]::new($false))
    }
    Write-Output $json
    if (-not $passed) {
        exit 30
    }
    exit 0
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemporaryRoot = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath())
        if (-not $resolvedTemporaryRoot.StartsWith(
                $resolvedSystemTemporaryRoot,
                [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedTemporaryRoot) -notlike
                'qlhv-time-health-v6-*') {
            throw 'Refusing to remove an unexpected rehearsal directory.'
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
