[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ModelPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string]$ModelSha256,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Apache-2.0', 'MIT', 'BSD-2-Clause', 'BSD-3-Clause')]
    [string]$LicenseId,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$LicenseManifestPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{64}$')]
    [string]$LicenseManifestSha256,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$InputRoot,

    [ValidateRange(1, 200)]
    [int]$MaxImages = 20,

    [ValidateRange(0.0, 1.0)]
    [double]$MinimumConfidence = 0.85
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedModel = (Resolve-Path -LiteralPath $ModelPath).Path
$resolvedManifest = (Resolve-Path -LiteralPath $LicenseManifestPath).Path
$resolvedInput = (Resolve-Path -LiteralPath $InputRoot).Path

if ([IO.Path]::GetExtension($resolvedModel) -ine '.onnx') {
    throw 'ModelPath must point to an ONNX file.'
}

if ([IO.Path]::GetExtension($resolvedManifest) -ine '.json') {
    throw 'LicenseManifestPath must point to a JSON file.'
}

$actualModelHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedModel).Hash
if ($actualModelHash -ine $ModelSha256) {
    throw 'The ONNX model SHA-256 does not match ModelSha256.'
}

$actualManifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedManifest).Hash
if ($actualManifestHash -ine $LicenseManifestSha256) {
    throw 'The license manifest SHA-256 does not match LicenseManifestSha256.'
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$testProject = Join-Path $repoRoot 'server\QLHV.Tests\QLHV.Tests.csproj'
if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
    throw "Cannot find benchmark test project: $testProject"
}

$variables = @{
    'QLHV_PHOTO_BENCHMARK_OPT_IN' = 'RUN_LOCAL_READ_ONLY_PHOTO_BENCHMARK'
    'QLHV_PHOTO_BENCHMARK_MODEL_PATH' = $resolvedModel
    'QLHV_PHOTO_BENCHMARK_MODEL_SHA256' = $ModelSha256.ToLowerInvariant()
    'QLHV_PHOTO_BENCHMARK_LICENSE_ID' = $LicenseId
    'QLHV_PHOTO_BENCHMARK_LICENSE_MANIFEST_PATH' = $resolvedManifest
    'QLHV_PHOTO_BENCHMARK_LICENSE_MANIFEST_SHA256' = $LicenseManifestSha256.ToLowerInvariant()
    'QLHV_PHOTO_BENCHMARK_INPUT_ROOT' = $resolvedInput
    'QLHV_PHOTO_BENCHMARK_MAX_IMAGES' = $MaxImages.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
    'QLHV_PHOTO_BENCHMARK_MINIMUM_CONFIDENCE' = $MinimumConfidence.ToString(
        [Globalization.CultureInfo]::InvariantCulture)
}

$previous = @{}
try {
    foreach ($entry in $variables.GetEnumerator()) {
        $previous[$entry.Key] = [Environment]::GetEnvironmentVariable(
            $entry.Key,
            [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            [EnvironmentVariableTarget]::Process)
    }

    & dotnet test $testProject `
        -c Release `
        --filter 'FullyQualifiedName~PhotoProcessingOptInBenchmarkTests' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw "Photo benchmark failed with exit code $LASTEXITCODE."
    }
}
finally {
    foreach ($entry in $previous.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            [EnvironmentVariableTarget]::Process)
    }
}
