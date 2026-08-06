[CmdletBinding()]
param(
    [ValidateSet('Validate', 'ObservationOnly', 'Canary', 'ControlledCutover')]
    [string]$Mode = 'Validate',
    [string]$PlanPath = '',
    [string]$ArtifactManifestPath = '',
    [string]$RuntimeConfigPath = 'D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json',
    [string]$EvidenceRoot = '',
    [string]$SqlServer = 'lpc:CSDLTTTC',
    [switch]$Execute,
    [switch]$RunProductionReadOnlyProof,
    [string]$Confirmation = '',
    [string]$AutoSyncPollingDisabledProofPath = '',
    [string]$RenderedCanarySqlPath = '',
    [string]$RenderedCanarySqlSha256 = '',
    [string]$RenderedRollbackSqlPath = '',
    [string]$RenderedRollbackSqlSha256 = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($PlanPath)) {
    $PlanPath = Join-Path $repoRoot 'ops\rt03\rt03-production-observation-plan.json'
}
if ([string]::IsNullOrWhiteSpace($ArtifactManifestPath)) {
    $ArtifactManifestPath = Join-Path $repoRoot 'ops\rt03\rt03-artifact-manifest.json'
}
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path 'D:\QLHV_RT03_EVIDENCE' (
        'RT03_TASK2_' + [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss'))
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

function Get-TextSha256([string]$Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))
        return ([BitConverter]::ToString($bytes) -replace '-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Assert-ExactFile([string]$Path, [string]$ExpectedHash) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "RT03_REQUIRED_FILE_MISSING: $Path"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedHash) -and
        (Get-Sha256 $Path) -ne $ExpectedHash.ToUpperInvariant()) {
        throw "RT03_FILE_HASH_MISMATCH: $Path"
    }
}

function Invoke-CheckedSql(
    [string]$ScriptPath,
    [string]$OutputName,
    [string[]]$Variables = @()) {
    Assert-ExactFile $ScriptPath ''
    $outputPath = Join-Path $EvidenceRoot $OutputName
    $arguments = @('-S', $SqlServer, '-E', '-b', '-W', '-w', '65535', '-i', $ScriptPath)
    foreach ($variable in $Variables) {
        $arguments += @('-v', $variable)
    }
    & sqlcmd @arguments 2>&1 | Out-File -LiteralPath $outputPath -Encoding utf8 -Width 65535
    if ($LASTEXITCODE -ne 0) {
        throw "RT03_SQL_FAILED: $ScriptPath"
    }
    return $outputPath
}

function Assert-ReadOnlyProof([string]$Path) {
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -notmatch '(?im)^USE \[(QLHV_APP|CSDL_OTO|CSDL_MOTO)\];\s*\r?\nGO\s*$') {
        throw "RT03_SQL_DATABASE_CONTEXT_MISSING: $Path"
    }
    if ($text -match '(?im)^\s*(INSERT|UPDATE|DELETE|MERGE|ALTER|CREATE|DROP|TRUNCATE|EXEC(?:UTE)?|BACKUP|RESTORE)\b') {
        throw "RT03_READ_ONLY_PROOF_CONTAINS_WRITE: $Path"
    }
}

function Assert-RenderedCanaryBundle([string]$Path, [string]$ExpectedHash, [string]$PlanHash) {
    Assert-ExactFile $Path $ExpectedHash
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -notmatch '(?im)^USE \[QLHV_APP\];\s*\r?\nGO\s*$' -or
        $text -notmatch [Regex]::Escape($PlanHash) -or
        $text -notmatch '(?i)BEGIN\s+TRANSACTION' -or
        $text -notmatch '(?i)COMMIT\s+TRANSACTION' -or
        $text -notmatch 'App_QlhvDirectRealtimeApplyMarker' -or
        $text -notmatch 'App_QlhvDirectRealtimeApplyCheckpoint') {
        throw "RT03_RENDERED_CANARY_CONTRACT_REJECTED: $Path"
    }
    if ($text -match '(?i)\b(MERGE|TRUNCATE|DROP|ALTER)\b' -or
        $text -match '(?i)\bDELETE\s+FROM\s+dbo\.App_HocVien\b' -or
        $text -match '(?i)\bSET\s+IsDeleted\b' -or
        $text -match '(?i)\bSET\s+IsActive\b' -or
        $text -match '(?i)sp_executesql|EXEC\s*\(') {
        throw "RT03_RENDERED_CANARY_P0_SQL_REJECTED: $Path"
    }
}

function Assert-RenderedRollbackBundle([string]$Path, [string]$ExpectedHash, [string]$PlanHash) {
    Assert-ExactFile $Path $ExpectedHash
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -notmatch '(?im)^USE \[QLHV_APP\];\s*\r?\nGO\s*$' -or
        $text -notmatch [Regex]::Escape($PlanHash) -or
        $text -notmatch '@ExactInsertedHocVienId|@TargetHocVienId' -or
        $text -match '(?i)LIKE\s+N?''%|sp_executesql|EXEC\s*\(') {
        throw "RT03_RENDERED_ROLLBACK_CONTRACT_REJECTED: $Path"
    }
}

function Assert-AutoSyncPollingDisabled([string]$Path) {
    Assert-ExactFile $Path ''
    $proof = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($proof.enabled -ne $false -or $proof.isPolling -ne $false -or
        $proof.activeRunRows -ne 0 -or $proof.activeSlotRows -ne 0 -or
        $proof.activeOperationRows -ne 0) {
        throw 'RT03_AUTOSYNC_ACTIVE: polling proof did not show a fully inactive state.'
    }
}

Push-Location $repoRoot
try {
    if ((git branch --show-current).Trim() -ne 'codex/csdt-realtime-v2-to-v1-oto-moto') {
        throw 'RT03_REPOSITORY_BRANCH_REJECTED.'
    }
    if ((git rev-parse HEAD).Trim() -ne '383387e8456d1a61640eee190519ff3f28619218') {
        throw 'RT03_REPOSITORY_HEAD_REJECTED.'
    }
    if (@(git diff --cached --name-only).Count -ne 0) {
        throw 'RT03_REPOSITORY_STAGING_REJECTED.'
    }

    $protectedHash = '12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E'
    Assert-ExactFile (Join-Path $repoRoot 'server\QLHV.Api\appsettings.Development.json') $protectedHash
    Assert-ExactFile (Join-Path $repoRoot 'server\QLHV.Worker\appsettings.Development.json') $protectedHash
    Assert-ExactFile $PlanPath ''
    Assert-ExactFile $ArtifactManifestPath ''

    $artifactManifest = Get-Content -LiteralPath $ArtifactManifestPath -Raw | ConvertFrom-Json
    if ($artifactManifest.schemaVersion -ne 'RT03-ARTIFACT-MANIFEST-v1' -or
        $artifactManifest.executionStatus -ne 'PREPARED_NOT_EXECUTED') {
        throw 'RT03_ARTIFACT_MANIFEST_REJECTED.'
    }
    foreach ($artifact in @($artifactManifest.artifacts)) {
        $artifactPath = (Resolve-Path (Join-Path $repoRoot $artifact.path)).Path
        if (-not $artifactPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "RT03_ARTIFACT_OUTSIDE_REPOSITORY: $($artifact.path)"
        }
        Assert-ExactFile $artifactPath $artifact.sha256
    }

    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    if ($plan.schemaVersion -ne 'RT03-PRODUCTION-PLAN-v1' -or
        $plan.environmentId -ne 'PRODUCTION' -or
        $plan.serverIdentity -ne 'CSDLTTTC' -or
        ($plan.routeOrder -join ',') -ne 'CSDT_OTO,CSDT_MOTO') {
        throw 'RT03_PLAN_ROUTE_OR_SCHEMA_REJECTED.'
    }
    $canonical = @(
        $plan.planId,
        $plan.mode,
        $plan.environmentId,
        $plan.mappingFingerprint,
        $plan.otoSourceSchemaFingerprint,
        $plan.motoSourceSchemaFingerprint,
        $plan.targetSchemaFingerprint,
        $plan.otoStageHash,
        $plan.motoStageHash,
        $plan.otoTargetComparisonHash,
        $plan.motoTargetComparisonHash,
        $(if ($null -eq $plan.otoInitialChangeTrackingVersion) { '<NULL>' } else { [string]$plan.otoInitialChangeTrackingVersion }),
        $(if ($null -eq $plan.motoInitialChangeTrackingVersion) { '<NULL>' } else { [string]$plan.motoInitialChangeTrackingVersion }),
        $plan.otoCanaryResult,
        (@($plan.candidates | ForEach-Object { $_.candidateId }) -join ',')
    ) -join '|'
    if ((Get-TextSha256 $canonical) -ne $plan.planHash) {
        throw 'RT03_PLAN_HASH_MISMATCH.'
    }

    $readinessProof = Join-Path $repoRoot 'database\proofs\20260727_rt03_production_readiness_read_only.sql'
    $preflightProof = Join-Path $repoRoot 'database\proofs\20260727_rt03_task2_preflight_read_only.sql'
    $postCanaryProof = Join-Path $repoRoot 'database\proofs\20260727_rt03_post_canary_read_only.sql'
    Assert-ReadOnlyProof $readinessProof
    Assert-ReadOnlyProof $preflightProof
    Assert-ReadOnlyProof $postCanaryProof

    if ($Mode -eq 'Validate') {
        if ($Execute) {
            throw 'RT03_VALIDATION_MODE_FORBIDS_EXECUTE.'
        }
        if ($RunProductionReadOnlyProof) {
            New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
            Invoke-CheckedSql $readinessProof 'rt03_readiness_read_only.log' | Out-Null
        }
        [pscustomobject]@{
            Status = 'VALIDATED_NO_SQL_WRITE'
            Mode = $Mode
            PlanMode = $plan.mode
            PlanHash = $plan.planHash
            CandidateCount = @($plan.candidates).Count
            ProductionReadOnlyProofRun = [bool]$RunProductionReadOnlyProof
        } | ConvertTo-Json -Depth 5
        exit 0
    }

    if ($Mode -eq 'ObservationOnly') {
        if ($plan.mode -ne 'OBSERVATION_ONLY' -or @($plan.candidates).Count -ne 0) {
            throw 'RT03_OBSERVATION_PLAN_REJECTED.'
        }
        if ($Execute -or $Confirmation) {
            throw 'RT03_OBSERVATION_ONLY_FORBIDS_WRITE_CONFIRMATION.'
        }
        New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
        Invoke-CheckedSql $readinessProof 'rt03_observation_read_only.log' | Out-Null
        [pscustomobject]@{
            Status = 'OBSERVATION_ONLY_COMPLETE'
            BusinessDataWrites = 0
            CheckpointPublished = $false
            AutoSyncTouched = $false
            PlanHash = $plan.planHash
        } | ConvertTo-Json -Depth 5
        exit 0
    }

    if ($plan.mode -ne 'CANARY' -or @($plan.candidates).Count -eq 0) {
        throw 'RT03_MUTATION_CANARY_REQUIRES_A_NEW_NONEMPTY_SEALED_PLAN.'
    }
    if (-not $Execute -or $Confirmation -ne 'EXECUTE_RT03_PRODUCTION_CANARY') {
        throw 'RT03_MUTATION_CANARY_CONFIRMATION_REJECTED.'
    }
    if (-not (Test-Path -LiteralPath $RuntimeConfigPath -PathType Leaf)) {
        throw 'RT03_RUNTIME_CONFIG_MISSING.'
    }
    $runtime = Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json
    if ($runtime.QlhvAutoSync.Enabled -ne $false -or
        $runtime.QlhvAutoSync.RunOnServerStartup -ne $false) {
        throw 'RT03_AUTOSYNC_CONFIG_NOT_PAUSED.'
    }
    Assert-AutoSyncPollingDisabled $AutoSyncPollingDisabledProofPath
    Assert-RenderedCanaryBundle $RenderedCanarySqlPath $RenderedCanarySqlSha256 $plan.planHash
    Assert-RenderedRollbackBundle $RenderedRollbackSqlPath $RenderedRollbackSqlSha256 $plan.planHash

    New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
    Invoke-CheckedSql $preflightProof 'rt03_task2_preflight_read_only.log' | Out-Null
    $controlPlane = Join-Path $repoRoot 'database\patches\20260727_rt03_add_direct_realtime_control_plane.sql'
    $enableOto = Join-Path $repoRoot 'database\patches\20260727_rt03_enable_ct_snapshot_oto_production.sql'
    $enableCanary = Join-Path $repoRoot 'database\patches\20260727_rt03_feature_enable_canary.sql'
    $disableAll = Join-Path $repoRoot 'database\patches\20260727_rt03_feature_disable_all.sql'
    $applyStarted = $false
    $controlPlaneApplied = $false
    try {
        Invoke-CheckedSql $controlPlane 'rt03_control_plane_apply.log' | Out-Null
        $controlPlaneApplied = $true
        Invoke-CheckedSql $enableOto 'rt03_oto_ct_enable.log' | Out-Null
        Invoke-CheckedSql $enableCanary 'rt03_canary_feature_enable.log' @(
            'RT03_AUTOSYNC_POLLING_STATE=DISABLED_VERIFIED') | Out-Null
        $applyStarted = $true
        Invoke-CheckedSql $RenderedCanarySqlPath 'rt03_canary_apply.log' | Out-Null
        Invoke-CheckedSql $postCanaryProof 'rt03_post_canary_read_only.log' @(
            "RT03_PLAN_HASH=$($plan.planHash)") | Out-Null

        if ($Mode -eq 'ControlledCutover') {
            if ($plan.otoCanaryResult -ne 'PASSED') {
                throw 'RT03_OTO_MUST_PASS_FIRST.'
            }
            $cutover = Join-Path $repoRoot 'database\patches\20260727_rt03_feature_enable_controlled_cutover.sql'
            Invoke-CheckedSql $cutover 'rt03_controlled_cutover_enable.log' @(
                'RT03_OTO_CANARY_RESULT=PASSED',
                'RT03_AUTOSYNC_POLLING_STATE=DISABLED_VERIFIED',
                "RT03_PLAN_HASH=$($plan.planHash)") | Out-Null
        }
        else {
            Invoke-CheckedSql $disableAll 'rt03_canary_feature_disable.log' | Out-Null
        }
    }
    catch {
        $originalError = $_
        if ($applyStarted) {
            try {
                Invoke-CheckedSql $RenderedRollbackSqlPath 'rt03_exact_rollback.log' | Out-Null
            }
            catch {
                $_ | Out-String | Out-File -LiteralPath (
                    Join-Path $EvidenceRoot 'rt03_exact_rollback_error.log') -Encoding utf8
            }
        }
        if ($controlPlaneApplied) {
            try {
                Invoke-CheckedSql $disableAll 'rt03_failsafe_feature_disable.log' | Out-Null
            }
            catch {
                $_ | Out-String | Out-File -LiteralPath (
                    Join-Path $EvidenceRoot 'rt03_feature_disable_error.log') -Encoding utf8
            }
        }
        throw $originalError
    }

    [pscustomobject]@{
        Status = if ($Mode -eq 'ControlledCutover') {
            'CONTROLLED_CUTOVER_ENABLED'
        } else {
            'CANARY_APPLIED_VERIFIED_AND_DISABLED'
        }
        PlanHash = $plan.planHash
        CandidateCount = @($plan.candidates).Count
        EvidenceRoot = $EvidenceRoot
    } | ConvertTo-Json -Depth 5
}
finally {
    Pop-Location
}
