[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = 'D:\QLHV_APP'
$evidenceRoot =
    'D:\QLHV_RT04_EVIDENCE\RT04_CANARY_TARGET_ROLLBACK_EXEC_20260727_230638001'
$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not $fullOutputPath.StartsWith(
    $evidenceRoot + '\',
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_FINAL_GUARD_OUTPUT_PATH_REJECTED'
}

$serviceScript = Join-Path `
    -Path $repositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'
$healthScript = Join-Path `
    -Path $repositoryRoot `
    -ChildPath 'ops\rt04\Get-Rt04ProductionHealth.ps1'
. $serviceScript
$service = Get-QlhvRealtimeWorkerServiceRecord
if ($null -eq $service) {
    throw 'RT04_FINAL_GUARD_SERVICE_MISSING'
}
Assert-QlhvRealtimeWorkerServiceIdentity `
    -ServiceRecord $service `
    -RuntimeRoot 'D:\QLHV_APP_RUNTIME'
Assert-QlhvRealtimeWorkerEnvironment

$healthPath = Join-Path `
    -Path $evidenceRoot `
    -ChildPath '11_final_handoff_health.json'
& $healthScript -OutputPath $healthPath | Out-Null
$health = Get-Content -LiteralPath $healthPath -Raw | ConvertFrom-Json

$branch = (& git -C $repositoryRoot branch --show-current).Trim()
$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$staged = @(& git -C $repositoryRoot diff --cached --name-only)
$protectedPaths = @(
    (Join-Path -Path $repositoryRoot -ChildPath 'server\QLHV.Api\appsettings.Development.json'),
    (Join-Path -Path $repositoryRoot -ChildPath 'server\QLHV.Worker\appsettings.Development.json')
)
$protectedHashes = @($protectedPaths | ForEach-Object {
    (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
})
$runtimeConfig =
    'D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json'
$runtimeConfigHash = (Get-FileHash `
    -LiteralPath $runtimeConfig `
    -Algorithm SHA256).Hash

$requiredReports = @(
    'RT03_FINAL_OPERATIONAL_STATE.md',
    'RT03_FINAL_SUMMARY.md',
    'RT04_FINAL_HANDOFF.md',
    'RT04_PRODUCTION_OPERATIONS_RUNBOOK.md',
    'RT04_CANARY_TARGET_ROLLBACK_PROOF.md',
    'RT04_PRODUCTION_SERVICE_REGISTRATION.md',
    'RT04_RESTART_AND_RECOVERY_RESULTS.md',
    'RT04_FINAL_OPERATIONAL_STATE.md'
)
$reportDirectory = Join-Path `
    -Path $repositoryRoot `
    -ChildPath 'docs\analysis'
$missingReports = @($requiredReports | Where-Object {
    -not (Test-Path `
        -LiteralPath (Join-Path -Path $reportDirectory -ChildPath $_) `
        -PathType Leaf)
})

$trxPath = Join-Path `
    -Path $evidenceRoot `
    -ChildPath '10_full_server_regression.trx'
[xml]$trx = Get-Content -LiteralPath $trxPath -Raw
$counters = $trx.TestRun.ResultSummary.Counters
$skippedTests = [int]$counters.total - [int]$counters.executed

$expectedProtectedHash =
    '12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E'
$pass =
    [bool]$health.Healthy -and
    $branch -eq 'codex/csdt-realtime-v2-to-v1-oto-moto' -and
    $head -eq '383387e8456d1a61640eee190519ff3f28619218' -and
    $staged.Count -eq 0 -and
    @($protectedHashes | Where-Object { $_ -ne $expectedProtectedHash }).Count -eq 0 -and
    $runtimeConfigHash -eq
        '9847629CE2D576BB72C23F34AF8B50E8E3F65002DC805C3AF339DDCA8FB5F632' -and
    $missingReports.Count -eq 0 -and
    [int]$counters.failed -eq 0 -and
    [int]$counters.passed -eq 1201 -and
    [int]$counters.total -eq 1203

$result = [ordered]@{
    Evidence = 'RT04_FINAL_HANDOFF_GUARDS'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    Pass = $pass
    Service = [ordered]@{
        Name = [string]$service.Name
        State = [string]$service.State
        ProcessId = [int]$service.ProcessId
        StartMode = [string]$service.StartMode
        Account = [string]$service.StartName
        ExactIdentityAndEnvironment = $true
    }
    ProductionHealth = [bool]$health.Healthy
    ProductionCounts =
        "$($health.Integrity.OtoTargetActiveRows)/$($health.Integrity.MotoTargetActiveRows)"
    Profiles = "$($health.Realtime.OtoStatus)/$($health.Realtime.MotoStatus)"
    Checkpoints =
        "OTO=$($health.Checkpoints[0].Checkpoint);MOTO=$($health.Checkpoints[1].Checkpoint)"
    AutoSyncHistoryAndActive =
        "$($health.Integrity.AutoSyncHistoryRows)/$($health.Integrity.ActiveRunSlotOperation)"
    Branch = $branch
    Head = $head
    StagedFiles = $staged.Count
    ProtectedDevelopmentConfigHash = $expectedProtectedHash
    RuntimeProductionConfigHash = $runtimeConfigHash
    RequiredReportsPresent = $missingReports.Count -eq 0
    ServerRegression =
        "$($counters.passed) passed/$skippedTests skipped/$($counters.failed) failed"
    RebootPerformed = $false
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutputPath)) |
    Out-Null
[IO.File]::WriteAllText(
    $fullOutputPath,
    ($result | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))
if (-not $pass) {
    throw 'RT04_FINAL_HANDOFF_GUARDS_REJECTED'
}
Write-Output 'RT04_FINAL_HANDOFF_GUARDS_PASS'
