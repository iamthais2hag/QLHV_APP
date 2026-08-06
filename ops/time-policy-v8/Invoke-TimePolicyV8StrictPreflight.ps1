[CmdletBinding()]
param(
    [ValidateSet('api', 'standalone', 'both')]
    [string]$Mode = 'both',

    [string]$PreflightExecutable = 'QLHV.TimeHealth.Preflight.exe',

    [string]$ApiUri = 'http://127.0.0.1:8088/api/system/runtime-status',

    [string]$SqlServer = 'CSDLTTTC',

    [string]$Database = 'QLHV_APP',

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$ContractFile,

    [string]$ObservationFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedExecutable = (Resolve-Path -LiteralPath $PreflightExecutable).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$toolOutput = Join-Path $outputDirectory 'time-policy-v8-shared-policy.json'
$arguments = @(
    '--mode', $Mode,
    '--api-uri', $ApiUri,
    '--sql-server', $SqlServer,
    '--database', $Database,
    '--output', $toolOutput
)
if (-not [string]::IsNullOrWhiteSpace($ContractFile)) {
    $arguments += @('--contract-file', (Resolve-Path -LiteralPath $ContractFile).Path)
}
if (-not [string]::IsNullOrWhiteSpace($ObservationFile)) {
    $arguments += @('--observation-file', (Resolve-Path -LiteralPath $ObservationFile).Path)
}

& $resolvedExecutable @arguments | Out-Null
$toolExitCode = $LASTEXITCODE
if (-not (Test-Path -LiteralPath $toolOutput)) {
    throw 'TIME_POLICY_V8_PREFLIGHT_OUTPUT_MISSING'
}

$result = Get-Content -LiteralPath $toolOutput -Raw | ConvertFrom-Json
$classification = [string]$result.classification
$approvedClassification = $classification -in @(
    'TIME_HEALTHY',
    'TIME_SAFE_WARNING'
)
if (
    $toolExitCode -ne 0 -or
    [int]$result.exitCode -ne 0 -or
    [string]$result.contractVersion -ne '1.1' -or
    -not $approvedClassification
) {
    throw "TIME_POLICY_V8_STRICT_PREFLIGHT_BLOCKED: exit=$toolExitCode classification=$classification"
}

$standaloneDecision = $null
if ($null -ne $result.standalone) {
    $time = $result.standalone.time
    $standaloneDecision = [pscustomobject]@{
        Health = [string]$time.health
        ReasonCode = [string]$time.reasonCode
        WritesAllowed = [bool]$time.writesAllowed
    }
    $standaloneApproved = $standaloneDecision.WritesAllowed -and (
        ($standaloneDecision.Health -eq 'HEALTHY' -and
         $standaloneDecision.ReasonCode -eq 'NONE') -or
        ($standaloneDecision.Health -eq 'WARNING' -and
         $standaloneDecision.ReasonCode -eq 'TRANSIENT_W32TIME_DIAGNOSTIC')
    )
    if (-not $standaloneApproved) {
        throw 'TIME_POLICY_V8_STANDALONE_DECISION_BLOCKED'
    }
}

$strictResult = [pscustomobject]@{
    Contract = 'QLHV_TIME_POLICY_STRICT_PREFLIGHT_1.1'
    EvaluatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Mode = $Mode
    ExitCode = 0
    Classification = $classification
    ApprovedSafeWarning = $classification -eq 'TIME_SAFE_WARNING'
    StandaloneDecision = $standaloneDecision
    SharedPolicyOutput = $toolOutput
    ReadOnly = $true
    ProductionMutation = $false
    W32TimeMutation = $false
}
$strictResult |
    ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $resolvedOutput -Encoding UTF8
$strictResult | ConvertTo-Json -Depth 8
exit 0
