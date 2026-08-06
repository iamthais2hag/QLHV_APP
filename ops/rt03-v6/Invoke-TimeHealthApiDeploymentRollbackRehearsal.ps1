[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$candidateRoot = [IO.Path]::GetFullPath($CandidateDirectory)
if (-not (Test-Path -LiteralPath $candidateRoot -PathType Container)) {
    throw "Candidate API directory not found: $candidateRoot"
}

$candidateFiles = @(Get-ChildItem -LiteralPath $candidateRoot -Recurse -File)
if ($candidateFiles.Count -eq 0) {
    throw 'Candidate API directory is empty.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'qlhv-api-v6-rollback-' + [guid]::NewGuid().ToString('N'))
$runtimeRoot = Join-Path $temporaryRoot 'runtime'
$backupRoot = Join-Path $temporaryRoot 'backup'
[IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
[IO.Directory]::CreateDirectory($backupRoot) | Out-Null

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith(
            $resolvedRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected root: $resolvedPath"
    }
    return $resolvedPath.Substring($resolvedRoot.Length)
}

function Copy-ExactFile {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )
    $parent = Split-Path -Parent $Destination
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-HashMap {
    param([Parameter(Mandatory)][string]$Root)
    $map = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File |
        Sort-Object FullName) {
        $relative = Get-RelativePath -Root $Root -Path $file.FullName
        $map[$relative] = (Get-FileHash -LiteralPath $file.FullName `
            -Algorithm SHA256).Hash
    }
    return $map
}

function Test-HashMapsEqual {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual
    )
    $expectedJson = $Expected | ConvertTo-Json -Compress
    $actualJson = $Actual | ConvertTo-Json -Compress
    return $expectedJson -eq $actualJson
}

try {
    foreach ($candidate in $candidateFiles) {
        $relative = Get-RelativePath -Root $candidateRoot `
            -Path $candidate.FullName
        $baselinePath = Join-Path $runtimeRoot $relative
        [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $baselinePath)) | Out-Null
        [IO.File]::WriteAllText(
            $baselinePath,
            "DISPOSABLE_BASELINE::$relative",
            [Text.UTF8Encoding]::new($false))
    }

    $baselineHashes = Get-HashMap -Root $runtimeRoot
    foreach ($runtimeFile in Get-ChildItem -LiteralPath $runtimeRoot `
        -Recurse -File) {
        $relative = Get-RelativePath -Root $runtimeRoot `
            -Path $runtimeFile.FullName
        Copy-ExactFile -Source $runtimeFile.FullName `
            -Destination (Join-Path $backupRoot $relative)
    }
    $backupHashes = Get-HashMap -Root $backupRoot
    $backupVerified = Test-HashMapsEqual `
        -Expected $baselineHashes -Actual $backupHashes

    foreach ($candidate in $candidateFiles) {
        $relative = Get-RelativePath -Root $candidateRoot `
            -Path $candidate.FullName
        Copy-ExactFile -Source $candidate.FullName `
            -Destination (Join-Path $runtimeRoot $relative)
    }
    $candidateHashes = Get-HashMap -Root $candidateRoot
    $deployedHashes = Get-HashMap -Root $runtimeRoot
    $deploymentVerified = Test-HashMapsEqual `
        -Expected $candidateHashes -Actual $deployedHashes

    foreach ($backup in Get-ChildItem -LiteralPath $backupRoot -Recurse -File) {
        $relative = Get-RelativePath -Root $backupRoot -Path $backup.FullName
        Copy-ExactFile -Source $backup.FullName `
            -Destination (Join-Path $runtimeRoot $relative)
    }
    $rollbackHashes = Get-HashMap -Root $runtimeRoot
    $rollbackVerified = Test-HashMapsEqual `
        -Expected $baselineHashes -Actual $rollbackHashes

    $passed = $backupVerified -and $deploymentVerified -and $rollbackVerified
    $report = [ordered]@{
        ContractVersion = 'RT03_API_DEPLOYMENT_ROLLBACK_REHEARSAL_V6'
        CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Disposable = $true
        ProductionRuntimeTouched = $false
        CandidateFileCount = $candidateFiles.Count
        BackupHashesVerified = $backupVerified
        CandidateHashesVerifiedAfterCopy = $deploymentVerified
        BaselineHashesRestoredAfterRollback = $rollbackVerified
        WorkerRemainedStoppedSimulation = $true
        CheckpointMutation = $false
        SchemaMutation = $false
        MarkerMutation = $false
        Passed = $passed
        CandidateManifest = @($candidateHashes.GetEnumerator() | ForEach-Object {
            [ordered]@{ Path = $_.Key; Sha256 = $_.Value }
        })
    }
    $json = $report | ConvertTo-Json -Depth 8
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory(
            (Split-Path -Parent $resolvedOutput)) | Out-Null
        [IO.File]::WriteAllText(
            $resolvedOutput,
            $json,
            [Text.UTF8Encoding]::new($false))
    }
    Write-Output $json
    if (-not $passed) {
        exit 31
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
                'qlhv-api-v6-rollback-*') {
            throw 'Refusing to remove an unexpected rehearsal directory.'
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
