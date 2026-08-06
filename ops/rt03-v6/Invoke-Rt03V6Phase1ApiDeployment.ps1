[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupDirectory,

    [Parameter(Mandatory = $true)]
    [string]$BackupEvidencePath,

    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = 'D:\QLHV_APP\handoff\RT03_FULL_CONVERGENCE_RECOVERY_20260731_V6'
$runtime = 'D:\QLHV_APP_RUNTIME\app'
$apiExecutable = 'D:\QLHV_APP_RUNTIME\app\QLHV.Api.exe'
$launcher = 'D:\QLHV_APP_RUNTIME\launcher\Start-QLHV-App.ps1'
$backupRoot = 'D:\QLHV_APP_RUNTIME\backups\rt03-v6-api-contract'
$candidateRoot = Join-Path $package 'phase1-api'
$preflight = Join-Path $package 'Invoke-Rt03V6FreshPreflight.ps1'
$deploymentManifestPath = Join-Path $package 'DEPLOYMENT_MANIFEST.json'
$packageManifestPath = Join-Path $package 'MANIFEST.sha256'
$immediateGateEvidence = Join-Path $EvidenceDirectory '07_immediate_stop_gate.json'
$deploymentEvidence = Join-Path $EvidenceDirectory '07_api_deployment.json'
$rollbackEvidence = Join-Path $EvidenceDirectory '07_api_deployment_rollback.json'
$postDeploymentEvidence = Join-Path $EvidenceDirectory '08_phase1_postdeploy_preflight.json'

$deploymentManifest = Get-Content -LiteralPath $deploymentManifestPath -Raw |
    ConvertFrom-Json
$backupManifest = Get-Content -LiteralPath $BackupEvidencePath -Raw |
    ConvertFrom-Json
$deploymentStarted = $false

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not $fullPath.StartsWith(
            $fullRoot + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped its fixed root: $Path"
    }
}

function Assert-WorkerStopped {
    $service = Get-CimInstance Win32_Service `
        -Filter "Name='QLHV_APP_RealtimeWorker'"
    if ($null -eq $service -or
        $service.State -ne 'Stopped' -or
        [int]$service.ProcessId -ne 0) {
        throw 'Worker is not Stopped/PID 0.'
    }

    return $service
}

function Get-ExactApiProcess {
    $matches = @(
        Get-CimInstance Win32_Process -Filter "Name='QLHV.Api.exe'" |
            Where-Object {
                $_.ExecutablePath -ieq $apiExecutable
            }
    )
    if ($matches.Count -ne 1) {
        throw "Expected exactly one API process at the fixed path; found $($matches.Count)."
    }

    $live = Get-Process -Id ([int]$matches[0].ProcessId) -ErrorAction Stop
    if ($live.Path -ine $apiExecutable) {
        throw "Live API path mismatch for PID $($matches[0].ProcessId)."
    }

    return [pscustomobject]@{
        Pid = [int]$matches[0].ProcessId
        CimPath = [string]$matches[0].ExecutablePath
        LivePath = [string]$live.Path
    }
}

function Stop-ExactApiIfPresent {
    $matches = @(
        Get-CimInstance Win32_Process -Filter "Name='QLHV.Api.exe'" |
            Where-Object {
                $_.ExecutablePath -ieq $apiExecutable
            }
    )
    if ($matches.Count -gt 1) {
        throw "Unsafe API multiplicity during rollback: $($matches.Count)."
    }
    if ($matches.Count -eq 0) {
        return
    }

    $processId = [int]$matches[0].ProcessId
    $live = Get-Process -Id $processId -ErrorAction Stop
    if ($live.Path -ine $apiExecutable) {
        throw "Rollback API path mismatch for PID $processId."
    }

    Stop-Process -Id $processId -Force -ErrorAction Stop
    try {
        Wait-Process -Id $processId -Timeout 20 -ErrorAction Stop
    }
    catch {
        if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
            throw "API PID $processId did not exit."
        }
    }
}

function Invoke-ApiLauncher {
    $output = (& powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $launcher `
        -NoBrowser `
        -DisableAutoSync `
        -SuppressErrorDialog 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "API launcher failed with exit $exitCode. $($output.Trim())"
    }

    return $output.Trim()
}

function Assert-PackageManifest {
    $failures = @()
    $lines = @(Get-Content -LiteralPath $packageManifestPath)
    foreach ($line in $lines) {
        if ($line -notmatch '^([0-9A-Fa-f]{64}) \*(.+)$') {
            $failures += "FORMAT:$line"
            continue
        }

        $expected = $Matches[1].ToUpperInvariant()
        $relativePath = $Matches[2]
        $path = Join-Path $package $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $failures += "MISSING:$relativePath"
            continue
        }

        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).
            Hash.ToUpperInvariant()
        if ($actual -ne $expected) {
            $failures += "HASH:$relativePath"
        }
    }

    if ($lines.Count -ne 189 -or $failures.Count -ne 0) {
        throw "Package manifest validation failed: files=$($lines.Count), failures=$($failures.Count)."
    }
}

function Assert-ProductionAndCandidateBaseline {
    $files = @($deploymentManifest.Phase1ApiFiles)
    if ($files.Count -ne 12) {
        throw "Phase1ApiFiles count is $($files.Count), expected 12."
    }

    foreach ($entry in $files) {
        $relativePath = [string]$entry.File
        $productionPath = Join-Path $runtime $relativePath
        $candidatePath = Join-Path $candidateRoot $relativePath
        $productionExists = Test-Path -LiteralPath $productionPath -PathType Leaf
        if ($productionExists -ne [bool]$entry.ProductionFileExisted) {
            throw "Production existence baseline changed: $relativePath"
        }

        $productionHash = if ($productionExists) {
            (Get-FileHash -LiteralPath $productionPath -Algorithm SHA256).
                Hash.ToUpperInvariant()
        }
        else {
            $null
        }
        $expectedProductionHash = if ([string]::IsNullOrWhiteSpace(
                [string]$entry.ProductionBaselineSha256)) {
            $null
        }
        else {
            ([string]$entry.ProductionBaselineSha256).ToUpperInvariant()
        }

        $productionHashMatches =
            ($null -eq $expectedProductionHash -and $null -eq $productionHash) -or
            ($null -ne $expectedProductionHash -and
                $productionHash -eq $expectedProductionHash)
        if (-not $productionHashMatches) {
            throw "Production hash baseline changed: $relativePath"
        }

        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            throw "Candidate file missing: $relativePath"
        }
        $candidateHash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).
            Hash.ToUpperInvariant()
        if ($candidateHash -ne ([string]$entry.Sha256).ToUpperInvariant()) {
            throw "Candidate hash mismatch: $relativePath"
        }
    }
}

function Invoke-Preflight {
    param(
        [Parameter(Mandatory = $true)][int]$SampleCount,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Disabled', 'Required')][string]$ApiContractMode,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    $output = (& powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $preflight `
        -SampleCount $SampleCount `
        -ApiContractMode $ApiContractMode `
        -OutputPath $OutputPath 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Preflight did not create evidence. Exit=$exitCode. $($output.Trim())"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Evidence = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
        Output = $output.Trim()
    }
}

function Invoke-ApiRollback {
    param([Parameter(Mandatory = $true)][string]$Cause)

    Stop-ExactApiIfPresent
    foreach ($record in $backupManifest.Files) {
        $relativePath = [string]$record.RelativePath
        $destination = Join-Path $runtime $relativePath
        Assert-PathUnderRoot -Path $destination -Root $runtime

        if ([bool]$record.ProductionFileExisted) {
            $source = Join-Path $BackupDirectory $relativePath
            Assert-PathUnderRoot -Path $source -Root $BackupDirectory
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Rollback backup missing: $relativePath"
            }
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        elseif (Test-Path -LiteralPath $destination -PathType Leaf) {
            Remove-Item -LiteralPath $destination -Force
        }
    }

    $checks = @()
    foreach ($record in $backupManifest.Files) {
        $relativePath = [string]$record.RelativePath
        $destination = Join-Path $runtime $relativePath
        $exists = Test-Path -LiteralPath $destination -PathType Leaf
        $hash = if ($exists) {
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).
                Hash.ToUpperInvariant()
        }
        else {
            $null
        }
        $expectedHash = if ([string]::IsNullOrWhiteSpace(
                [string]$record.OriginalSha256)) {
            $null
        }
        else {
            ([string]$record.OriginalSha256).ToUpperInvariant()
        }
        $hashMatches =
            ($null -eq $expectedHash -and $null -eq $hash) -or
            ($null -ne $expectedHash -and $hash -eq $expectedHash)
        $restored =
            $exists -eq [bool]$record.ProductionFileExisted -and $hashMatches
        $checks += [pscustomobject]@{
            File = $relativePath
            Restored = $restored
            Sha256 = $hash
        }
    }
    if (@($checks | Where-Object { -not $_.Restored }).Count -ne 0) {
        throw 'Rollback baseline hash verification failed.'
    }

    $launcherOutput = Invoke-ApiLauncher
    Start-Sleep -Seconds 1
    $api = Get-ExactApiProcess
    $status = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri 'http://127.0.0.1:8088/api/system/runtime-status' `
        -TimeoutSec 30
    $service = Assert-WorkerStopped

    [ordered]@{
        Contract = 'RT03_V6_PHASE1_API_ROLLBACK'
        CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Cause = $Cause
        Succeeded = $true
        ApiPid = $api.Pid
        RuntimeStatusHttp = [int]$status.StatusCode
        WorkerState = $service.State
        WorkerPid = [int]$service.ProcessId
        LauncherOutput = $launcherOutput
        Files = $checks
    } | ConvertTo-Json -Depth 7 |
        Set-Content -LiteralPath $rollbackEvidence -Encoding UTF8
}

Assert-PathUnderRoot -Path $BackupDirectory -Root $backupRoot
Assert-PathUnderRoot -Path $BackupEvidencePath -Root 'D:\QLHV_APP_RUNTIME\evidence'
Assert-PathUnderRoot -Path $EvidenceDirectory -Root 'D:\QLHV_APP_RUNTIME\evidence'
if (-not (Test-Path -LiteralPath $BackupDirectory -PathType Container)) {
    throw "Backup directory missing: $BackupDirectory"
}
if (-not (Test-Path -LiteralPath $BackupEvidencePath -PathType Leaf)) {
    throw "Backup evidence missing: $BackupEvidencePath"
}
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null

try {
    Assert-PackageManifest
    Assert-ProductionAndCandidateBaseline

    $immediateGate = Invoke-Preflight `
        -SampleCount 3 `
        -ApiContractMode Disabled `
        -OutputPath $immediateGateEvidence
    if ($immediateGate.ExitCode -ne 0 -or
        -not [bool]$immediateGate.Evidence.ProductionRecoveryAllowed -or
        [int]$immediateGate.Evidence.TimeAuthority.LastSyncErrorCode -ne 0 -or
        $immediateGate.Evidence.TimeAuthority.TimeHealth -ne 'HEALTHY') {
        throw "Immediate Time gate failed (exit=$($immediateGate.ExitCode), health=$($immediateGate.Evidence.TimeAuthority.TimeHealth), error=$($immediateGate.Evidence.TimeAuthority.LastSyncErrorCode))."
    }

    $serviceBefore = Assert-WorkerStopped
    $apiBefore = Get-ExactApiProcess
    $oldPid = $apiBefore.Pid

    Stop-Process -Id $oldPid -Force -ErrorAction Stop
    try {
        Wait-Process -Id $oldPid -Timeout 20 -ErrorAction Stop
    }
    catch {
        if (Get-Process -Id $oldPid -ErrorAction SilentlyContinue) {
            throw "API PID $oldPid did not exit."
        }
    }
    $deploymentStarted = $true

    $oldListeners = @(
        Get-NetTCPConnection -LocalPort 8088 -State Listen -ErrorAction SilentlyContinue |
            Where-Object {
                $_.OwningProcess -eq $oldPid
            }
    )
    if ($oldListeners.Count -ne 0) {
        throw "Old API PID $oldPid still owns TCP 8088."
    }
    $serviceAfterStop = Assert-WorkerStopped

    foreach ($entry in $deploymentManifest.Phase1ApiFiles) {
        $relativePath = [string]$entry.File
        $source = Join-Path $candidateRoot $relativePath
        $destination = Join-Path $runtime $relativePath
        Assert-PathUnderRoot -Path $destination -Root $runtime
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force |
            Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }

    $deployedFiles = @()
    foreach ($entry in $deploymentManifest.Phase1ApiFiles) {
        $relativePath = [string]$entry.File
        $destination = Join-Path $runtime $relativePath
        $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).
            Hash.ToUpperInvariant()
        $expected = ([string]$entry.Sha256).ToUpperInvariant()
        if ($actual -ne $expected) {
            throw "Deployed hash mismatch: $relativePath"
        }
        $deployedFiles += [pscustomobject]@{
            File = $relativePath
            Sha256 = $actual
            Verified = $true
        }
    }

    $launcherOutput = Invoke-ApiLauncher
    Start-Sleep -Seconds 1
    $apiAfter = Get-ExactApiProcess
    $listeners = @(
        Get-NetTCPConnection -LocalPort 8088 -State Listen -ErrorAction Stop
    )
    if ($listeners.Count -ne 1 -or
        [int]$listeners[0].OwningProcess -ne $apiAfter.Pid) {
        throw 'TCP 8088 listener identity mismatch after API start.'
    }

    $liveResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri 'http://127.0.0.1:8088/health/live' `
        -TimeoutSec 30
    $runtimeResponse = Invoke-WebRequest `
        -UseBasicParsing `
        -Uri 'http://127.0.0.1:8088/api/system/runtime-status' `
        -TimeoutSec 45
    if ([int]$liveResponse.StatusCode -ne 200 -or
        [int]$runtimeResponse.StatusCode -ne 200) {
        throw 'API health endpoints did not return HTTP 200.'
    }

    $runtimeStatus = $runtimeResponse.Content | ConvertFrom-Json
    if ([string]$runtimeStatus.timeContractVersion -ne '1.0' -or
        $null -eq $runtimeStatus.time) {
        throw 'Time contract 1.0 is missing from runtime-status.'
    }
    if ($null -eq $runtimeStatus.time.lastSyncError -or
        [string]$runtimeStatus.time.health -ne 'HEALTHY' -or
        [int]$runtimeStatus.time.lastSyncError -ne 0) {
        throw 'API Time contract is not HEALTHY/Error 0.'
    }

    $postDeployment = Invoke-Preflight `
        -SampleCount 5 `
        -ApiContractMode Required `
        -OutputPath $postDeploymentEvidence
    $post = $postDeployment.Evidence
    $otoCheckpoint = @(
        $post.CheckpointsAfter |
            Where-Object SourceProfileCode -eq 'CSDT_OTO'
    )
    $motoCheckpoint = @(
        $post.CheckpointsAfter |
            Where-Object SourceProfileCode -eq 'CSDT_MOTO'
    )
    $badTimeSamples = @(
        $post.TimeAuthority.ConsecutiveSamples |
            Where-Object {
                [int]$_.LastSyncErrorCode -ne 0 -or
                $_.Classification -ne 'TIME_HEALTHY'
            }
    )

    if ($postDeployment.ExitCode -ne 0 -or
        -not [bool]$post.ProductionRecoveryAllowed) {
        throw "Required post-deployment preflight failed (exit=$($postDeployment.ExitCode))."
    }
    if ($post.ApiContract.Healthy -ne $true -or
        $post.ApiContract.Classification -ne 'TIME_HEALTHY' -or
        [int]$post.ApiContract.ExitCode -ne 0) {
        throw "Strict API Time contract validation failed: $($post.ApiContract.Classification)."
    }
    if ($badTimeSamples.Count -ne 0 -or
        [int]$post.TimeAuthority.LastSyncErrorCode -ne 0) {
        throw 'Time Error 2 or a non-healthy sample appeared after deployment.'
    }
    if ($post.Runtime.WorkerState -ne 'Stopped' -or
        [int]$post.Runtime.WorkerPid -ne 0 -or
        [int]$post.Runtime.ActiveAutoSyncRuns -ne 0 -or
        [int]$post.Runtime.ActiveFullSyncOperations -ne 0) {
        throw 'Runtime safety boundary changed during Phase 1.'
    }
    if ($otoCheckpoint.Count -ne 1 -or
        [long]$otoCheckpoint[0].CheckpointVersion -ne 25 -or
        $motoCheckpoint.Count -ne 1 -or
        [long]$motoCheckpoint[0].CheckpointVersion -ne 0) {
        throw 'Checkpoint changed during Phase 1.'
    }
    if (-not [bool]$post.Runtime.SchemaStateExactForMode) {
        throw 'Production schema baseline changed during Phase 1.'
    }

    $serviceFinal = Assert-WorkerStopped
    [ordered]@{
        Contract = 'RT03_V6_PHASE1_API_ONLY_DEPLOYMENT'
        CapturedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Passed = $true
        OldApiPid = $oldPid
        NewApiPid = $apiAfter.Pid
        ApiExecutablePath = $apiAfter.CimPath
        Tcp8088Owner = [int]$listeners[0].OwningProcess
        HealthLiveHttp = [int]$liveResponse.StatusCode
        RuntimeStatusHttp = [int]$runtimeResponse.StatusCode
        TimeContractVersion = [string]$runtimeStatus.timeContractVersion
        ApiTimeHealth = [string]$runtimeStatus.time.health
        ApiLastSyncError = [int]$runtimeStatus.time.lastSyncError
        StrictModeClassification = [string]$post.ApiContract.Classification
        StrictModeExitCode = [int]$post.ApiContract.ExitCode
        StandaloneSamples = @($post.TimeAuthority.ConsecutiveSamples).Count
        StandaloneAllHealthy = $badTimeSamples.Count -eq 0
        WorkerState = $serviceFinal.State
        WorkerPid = [int]$serviceFinal.ProcessId
        CheckpointOTO = [long]$otoCheckpoint[0].CheckpointVersion
        CheckpointMOTO = [long]$motoCheckpoint[0].CheckpointVersion
        ActiveAutoSyncRuns = [int]$post.Runtime.ActiveAutoSyncRuns
        ActiveFullSyncOperations = [int]$post.Runtime.ActiveFullSyncOperations
        SchemaUnchanged = [bool]$post.Runtime.SchemaStateExactForMode
        BackupDirectory = $BackupDirectory
        LauncherOutput = $launcherOutput
        DeployedFiles = $deployedFiles
    } | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $deploymentEvidence -Encoding UTF8

    Write-Output (
        'PHASE1_API_ONLY_PASS ' +
        "OLD_PID=$oldPid " +
        "NEW_PID=$($apiAfter.Pid) " +
        "TIME=$($runtimeStatus.time.health) " +
        "ERROR=$($runtimeStatus.time.lastSyncError) " +
        "STRICT=$($post.ApiContract.Classification) " +
        "SAMPLES=$(@($post.TimeAuthority.ConsecutiveSamples).Count) " +
        "WORKER=$($serviceFinal.State)/$($serviceFinal.ProcessId) " +
        "CHECKPOINTS=$($otoCheckpoint[0].CheckpointVersion)/$($motoCheckpoint[0].CheckpointVersion) " +
        "AUTOSYNC=$($post.Runtime.ActiveAutoSyncRuns)/$($post.Runtime.ActiveFullSyncOperations)"
    )
    Write-Output "DEPLOY_EVIDENCE=$deploymentEvidence"
    Write-Output "POSTDEPLOY_EVIDENCE=$postDeploymentEvidence"
}
catch {
    $cause = $_.Exception.Message
    if ($deploymentStarted) {
        $rollbackFailure = $null
        try {
            Invoke-ApiRollback -Cause $cause
        }
        catch {
            $rollbackFailure = $_.Exception.Message
        }

        if ($null -eq $rollbackFailure) {
            throw "Phase 1 failed; rollback succeeded. Cause: $cause"
        }
        throw (
            'Phase 1 failed and rollback was not safely completed. ' +
            "Original: $cause Rollback: $rollbackFailure"
        )
    }
    throw
}
