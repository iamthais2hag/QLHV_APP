[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('CSDT_OTO', 'CSDT_MOTO')]
    [string]$SourceProfileCode,

    [Parameter(Mandatory)]
    [ValidateNotNull()]
    [Guid]$RecoveryId,

    [Parameter(Mandatory)]
    [ValidateRange(0, [long]::MaxValue)]
    [long]$ExpectedCheckpoint,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$PackageRoot = $PSScriptRoot,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeRoot = 'D:\QLHV_APP_RUNTIME\app\worker',

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$EvidenceRoot,

    [Parameter(Mandatory)]
    [switch]$OperatorApprovedProductionRecovery
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = [IO.Path]::GetFullPath($PackageRoot)
$runtime = [IO.Path]::GetFullPath($RuntimeRoot)
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
$expectedRuntime = [IO.Path]::GetFullPath(
    'D:\QLHV_APP_RUNTIME\app\worker')
$preflightScript = Join-Path $package 'Invoke-Rt03V5FreshPreflight.ps1'
$hashManifest = Join-Path $package 'MANIFEST.sha256'
$deploymentManifestPath = Join-Path $package 'DEPLOYMENT_MANIFEST.json'
$workerExecutable = Join-Path $runtime 'QLHV.Worker.exe'
$workerServiceName = 'QLHV_APP_RealtimeWorker'
$emDash = [char]0x2014

if (-not $OperatorApprovedProductionRecovery) {
    throw "BLOCKED $emDash EXPLICIT OPERATOR APPROVAL FLAG REQUIRED"
}
if ($RecoveryId -eq [Guid]::Empty) {
    throw "BLOCKED $emDash RECOVERY ID MUST NOT BE EMPTY"
}
if ($runtime -ne $expectedRuntime) {
    throw "BLOCKED $emDash RUNTIME PATH IS NOT THE SEALED PRODUCTION PATH"
}
foreach ($required in @(
    $preflightScript,
    $hashManifest,
    $deploymentManifestPath,
    $workerExecutable
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "BLOCKED $emDash REQUIRED FILE MISSING: $required"
    }
}

[IO.Directory]::CreateDirectory($evidence) | Out-Null

$expectedHashes = @{}
Get-Content -LiteralPath $hashManifest -Encoding UTF8 | ForEach-Object {
    if ($_ -notmatch '^([0-9A-Fa-f]{64}) \*(.+)$') {
        throw "BLOCKED $emDash INVALID HASH MANIFEST LINE"
    }
    $expectedHashes[$Matches[2]] = $Matches[1].ToUpperInvariant()
}
foreach ($relativeName in $expectedHashes.Keys) {
    $path = Join-Path $package $relativeName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "BLOCKED $emDash PACKAGE FILE MISSING: $relativeName"
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $expectedHashes[$relativeName]) {
        throw "BLOCKED $emDash PACKAGE HASH MISMATCH: $relativeName"
    }
}

$deploymentManifest = Get-Content -LiteralPath $deploymentManifestPath `
    -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($binary in $deploymentManifest.DeploymentBinaries) {
    $runtimePath = Join-Path $runtime ([string]$binary.File)
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
        throw "BLOCKED $emDash RUNTIME BINARY MISSING: $($binary.File)"
    }
    $runtimeHash = (Get-FileHash -LiteralPath $runtimePath `
        -Algorithm SHA256).Hash
    if ($runtimeHash -ne [string]$binary.Sha256) {
        throw "BLOCKED $emDash RUNTIME BINARY HASH MISMATCH: $($binary.File)"
    }
}

$service = Get-CimInstance Win32_Service `
    -Filter "Name='$workerServiceName'"
if ($service.State -ne 'Stopped' -or [int]$service.ProcessId -ne 0) {
    throw "BLOCKED $emDash WORKER MUST BE STOPPED WITH PID 0"
}
$serviceExecutable = ([string]$service.PathName).Trim('"')
if ([IO.Path]::GetFullPath($serviceExecutable) -ne $workerExecutable) {
    throw "BLOCKED $emDash WORKER SERVICE PATH MISMATCH"
}

$preflightPath = Join-Path $evidence '01_execution_preflight.json'
$preflightConsole = Join-Path $evidence '01_execution_preflight_console.txt'
& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File $preflightScript `
    -ExecutionReady `
    -ProbeWriterLease `
    -OutputPath $preflightPath 2>&1 |
    Out-File -LiteralPath $preflightConsole -Encoding utf8 -Width 65535
if ($LASTEXITCODE -ne 0) {
    throw "BLOCKED $emDash PRODUCTION DEPLOYMENT PRECONDITION FAILED"
}
$preflight = Get-Content -LiteralPath $preflightPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if (-not [bool]$preflight.ProductionRecoveryAllowed -or
    [string]$preflight.PreflightMode -ne 'EXECUTION_READY') {
    throw "BLOCKED $emDash PRODUCTION DEPLOYMENT PRECONDITION FAILED"
}
$checkpoint = @($preflight.CheckpointsAfter | Where-Object {
    $_.SourceProfileCode -eq $SourceProfileCode
})
if ($checkpoint.Count -ne 1 -or
    [long]$checkpoint[0].CheckpointVersion -ne $ExpectedCheckpoint) {
    throw "BLOCKED $emDash FRESH CHECKPOINT DOES NOT MATCH OPERATOR INPUT"
}
$unsafeAudit = @($preflight.PerTableChangeTrackingAudit | Where-Object {
    $_.SourceProfileCode -eq $SourceProfileCode -and
    (
        $_.Classification -in @('UNCLASSIFIED','UNSAFE_DELETE_CONTRACT') -or
        $_.TargetExactIdentityStatus -eq 'BLOCKED_AMBIGUOUS'
    )
})
if ($unsafeAudit.Count -ne 0) {
    throw "BLOCKED $emDash RECOVERY DOMAIN IS NOT SAFELY CLASSIFIED"
}

$artifactSha256 =
    (Get-FileHash -LiteralPath $hashManifest -Algorithm SHA256).Hash.ToLowerInvariant()
$recoveryConsole = Join-Path $evidence '02_recovery_console.txt'
& $workerExecutable `
    '--rt03-v5-full-convergence-recovery' `
    "--profile=$SourceProfileCode" `
    "--recovery-id=$($RecoveryId.ToString('D'))" `
    "--expected-checkpoint=$ExpectedCheckpoint" `
    "--artifact-sha256=$artifactSha256" 2>&1 |
    Out-File -LiteralPath $recoveryConsole -Encoding utf8 -Width 65535
if ($LASTEXITCODE -ne 0) {
    throw "BLOCKED $emDash FULL CONVERGENCE RECOVERY EXECUTION FAILED"
}

$builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source'] = 'CSDLTTTC'
$builder['Initial Catalog'] = 'QLHV_APP'
$builder['Integrated Security'] = $true
$builder['Encrypt'] = $false
$builder['Pooling'] = $false
$builder['Application Name'] = 'QLHV RT03 V5 Post Recovery Verification'
$connection = [Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 60
    $command.CommandText = @'
SELECT sessionRow.RecoveryId,sessionRow.SourceProfileCode,
       sessionRow.CheckpointBefore,sessionRow.AnchorVersion,
       sessionRow.Status,sessionRow.VerificationPassed,
       sessionRow.CompletedAtUtc,
       checkpointRow.SourceChangeTrackingVersion CheckpointVersion,
       checkpointRow.CycleId,
       CONVERT(bit,CASE WHEN recoveryMarker.RecoveryId IS NULL
            THEN 0 ELSE 1 END) RecoveryMarkerExists,
       CONVERT(bit,CASE WHEN applyMarker.CycleId IS NULL
            THEN 0 ELSE 1 END) ApplyMarkerExists,
       (SELECT COUNT_BIG(*)
        FROM dbo.App_Rt03FullConvergenceDomain domainRow
        WHERE domainRow.RecoveryId=sessionRow.RecoveryId
          AND domainRow.Status=N'COMPLETED') CompletedDomains
FROM dbo.App_Rt03FullConvergenceSession sessionRow
INNER JOIN dbo.App_QlhvDirectRealtimeApplyCheckpoint checkpointRow
  ON checkpointRow.SourceProfileCode=sessionRow.SourceProfileCode
 AND checkpointRow.Mode=N'DIRECT_REALTIME_APPLY'
 AND checkpointRow.EnvironmentId=N'PRODUCTION'
LEFT JOIN dbo.App_Rt03FullConvergenceMarker recoveryMarker
  ON recoveryMarker.RecoveryId=sessionRow.RecoveryId
LEFT JOIN dbo.App_QlhvDirectRealtimeApplyMarker applyMarker
  ON applyMarker.CycleId=checkpointRow.CycleId
WHERE sessionRow.RecoveryId=@RecoveryId;
'@
    [void]$command.Parameters.Add(
        '@RecoveryId',
        [Data.SqlDbType]::UniqueIdentifier)
    $command.Parameters['@RecoveryId'].Value = $RecoveryId
    $table = [Data.DataTable]::new()
    $reader = $command.ExecuteReader()
    try {
        $table.Load($reader)
    }
    finally {
        $reader.Dispose()
        $command.Dispose()
    }
}
finally {
    $connection.Dispose()
}

if ($table.Rows.Count -ne 1) {
    throw "BLOCKED $emDash RECOVERY DURABLE EVIDENCE IS NOT EXACT"
}
$proof = $table.Rows[0]
if ([string]$proof.Status -ne 'COMPLETED' -or
    -not [bool]$proof.VerificationPassed -or
    [long]$proof.CheckpointVersion -ne [long]$proof.AnchorVersion -or
    [Guid]$proof.CycleId -ne $RecoveryId -or
    -not [bool]$proof.RecoveryMarkerExists -or
    -not [bool]$proof.ApplyMarkerExists -or
    [long]$proof.CompletedDomains -ne 5) {
    throw "BLOCKED $emDash RECOVERY MARKER OR CHECKPOINT VERIFICATION FAILED"
}

$postResult = [ordered]@{}
foreach ($column in $table.Columns) {
    $value = $proof[$column.ColumnName]
    $postResult[$column.ColumnName] = if ($value -is [DBNull]) {
        $null
    } else {
        $value
    }
}
$postResult['WorkerStartedByScript'] = $false
$postResult['AutoSyncEnabledByScript'] = $false
$postResult['ManualCheckpointWriteByScript'] = $false
$postResult['Result'] = 'RECOVERY_COMPLETED_PENDING_WORKER_START_APPROVAL'
$postResult | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $evidence '03_recovery_proof.json') `
        -Encoding UTF8

Write-Output 'RT03 V5 full convergence completed and verified.'
Write-Output 'Worker remains stopped; a separately approved start is required.'
