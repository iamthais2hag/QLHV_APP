$ErrorActionPreference = 'Stop'

$sqlcmdPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$workspaceRoot = 'D:\QLHV_APP'
$evidenceRoot = 'D:\QLHV_RT02_SQLDATA\RT02B2_SCHEMA_HOTFIX'
$isolatedServer = 'lpc:CSDLTTTC\QLHVRT02'
$productionServer = 'lpc:CSDLTTTC'

$schemaPath = Join-Path $workspaceRoot 'database\proofs\20260727_rt02b2_schema_set_options_hotfix.sql'
$schemaProofPath = Join-Path $workspaceRoot 'database\proofs\20260727_rt02b2_schema_gate_read_only.sql'
$identityProofPath = Join-Path $workspaceRoot 'database\proofs\20260727_rt02b1_post_provision_identity_read_only.sql'
$partialProofPath = Join-Path $workspaceRoot 'database\proofs\20260727_rt02b2_post_failure_read_only.sql'
$productionProofPath = Join-Path $workspaceRoot 'database\proofs\20260727_rt02b2_production_non_interference_read_only.sql'

$expectedHashes = @{
    $schemaPath = 'DBA8C569BD5C3C8EC8B9CC370C2AE52BB32458D9BCFF9AC450820908D28B3C86'
    $schemaProofPath = '054C65B3AAD1A88CD77F677044A3B1692E4605841FDA1D46580724B1F47AA7BD'
    $identityProofPath = '51BDCB6E6E157A123FF10A6576E9472B4759B4BCBB2B9690B55580FEF28CF539'
    $partialProofPath = '4920F92B1EAB92A620783D6A4E490A8CF79F5EE06494CDBFAB659796F4311F94'
    $productionProofPath = '7CCCF1FA04E1DDC9292AB9BBD78659D44A015755671993148E17552DC3D2DC7F'
}

$attemptMarkerPath = Join-Path $evidenceRoot 'ONE_SCHEMA_GATE_RETRY_STARTED.txt'
$timelinePath = Join-Path $evidenceRoot 'one_schema_gate_retry_timeline.log'

if (Test-Path -LiteralPath $attemptMarkerPath)
{
    throw 'RT02 schema-only retry has already been attempted; automatic rerun is forbidden.'
}

foreach ($entry in $expectedHashes.GetEnumerator())
{
    $observedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Key).Hash
    if ($observedHash -ne $entry.Value)
    {
        throw "Evidence hash mismatch: $($entry.Key)"
    }
}

function Write-Timeline
{
    param([Parameter(Mandatory = $true)][string] $Message)

    $timestamp = [DateTime]::UtcNow.ToString('o')
    Add-Content -LiteralPath $timelinePath -Value "$timestamp|$Message" -Encoding utf8
}

function Invoke-ReadOnlyProof
{
    param(
        [Parameter(Mandatory = $true)][string] $Server,
        [Parameter(Mandatory = $true)][string] $InputPath,
        [Parameter(Mandatory = $true)][string] $OutputPath,
        [Parameter(Mandatory = $true)][string] $Label
    )

    Write-Timeline "$Label|STARTED"
    & $sqlcmdPath `
        -S $Server `
        -E `
        -b `
        -r 1 `
        -W `
        -s '|' `
        -i $InputPath `
        -o $OutputPath
    if ($LASTEXITCODE -ne 0)
    {
        Write-Timeline "$Label|FAILED|EXIT=$LASTEXITCODE"
        throw "$Label failed with sqlcmd exit code $LASTEXITCODE."
    }

    Write-Timeline "$Label|SUCCEEDED"
}

Set-Content -LiteralPath $attemptMarkerPath -Value (
    [DateTime]::UtcNow.ToString('o') +
    '|RT02B_SCHEMA_ONLY_RETRY|STARTED|NO_AUTOMATIC_RETRY'
) -Encoding utf8
Set-Content -LiteralPath $timelinePath -Value (
    [DateTime]::UtcNow.ToString('o') +
    '|ONE_SCHEMA_GATE_RETRY|STARTED'
) -Encoding utf8

try
{
    Invoke-ReadOnlyProof `
        -Server $isolatedServer `
        -InputPath $identityProofPath `
        -OutputPath (Join-Path $evidenceRoot 'pre_retry_identity_read_only.log') `
        -Label 'PRE_RETRY_IDENTITY_READ_ONLY'

    Invoke-ReadOnlyProof `
        -Server $isolatedServer `
        -InputPath $partialProofPath `
        -OutputPath (Join-Path $evidenceRoot 'pre_retry_partial_schema_read_only.log') `
        -Label 'PRE_RETRY_PARTIAL_SCHEMA_READ_ONLY'

    Invoke-ReadOnlyProof `
        -Server $productionServer `
        -InputPath $productionProofPath `
        -OutputPath (Join-Path $evidenceRoot 'pre_retry_production_read_only.log') `
        -Label 'PRE_RETRY_PRODUCTION_READ_ONLY'

    Write-Timeline 'SCHEMA_ONLY_RETRY|STARTED'
    & $sqlcmdPath `
        -S $isolatedServer `
        -E `
        -b `
        -r 1 `
        -W `
        -s '|' `
        -i $schemaPath `
        -o (Join-Path $evidenceRoot 'schema_only_retry.log')
    if ($LASTEXITCODE -ne 0)
    {
        Write-Timeline "SCHEMA_ONLY_RETRY|FAILED|EXIT=$LASTEXITCODE"
        throw "Schema-only retry failed with sqlcmd exit code $LASTEXITCODE."
    }

    Write-Timeline 'SCHEMA_ONLY_RETRY|SUCCEEDED'

    Invoke-ReadOnlyProof `
        -Server $isolatedServer `
        -InputPath $schemaProofPath `
        -OutputPath (Join-Path $evidenceRoot 'schema_gate_read_1.log') `
        -Label 'SCHEMA_GATE_READ_1'

    Invoke-ReadOnlyProof `
        -Server $isolatedServer `
        -InputPath $schemaProofPath `
        -OutputPath (Join-Path $evidenceRoot 'schema_gate_read_2.log') `
        -Label 'SCHEMA_GATE_READ_2'

    Invoke-ReadOnlyProof `
        -Server $productionServer `
        -InputPath $productionProofPath `
        -OutputPath (Join-Path $evidenceRoot 'post_retry_production_read_only.log') `
        -Label 'POST_RETRY_PRODUCTION_READ_ONLY'

    Write-Timeline 'ONE_SCHEMA_GATE_RETRY|SUCCEEDED|EXECUTION_NOT_RESUMED'
    exit 0
}
catch
{
    Write-Timeline "ONE_SCHEMA_GATE_RETRY|FAILED|$($_.Exception.Message)"
    exit 1
}
