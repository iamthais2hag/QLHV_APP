$ErrorActionPreference = 'Stop'

$sqlcmdPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$evidenceRoot = 'D:\QLHV_RT02_SQLDATA\RT02B2_SCHEMA_HOTFIX'
$schemaProofPath = 'D:\QLHV_APP\database\proofs\20260727_rt02b2_schema_gate_read_only.sql'
$productionProofPath = 'D:\QLHV_APP\database\proofs\20260727_rt02b2_production_non_interference_read_only.sql'
$expectedSchemaProofHash = '3E757EC68C51A4246014705E0EB57A32F6F44662BE56FB7E9012618C0C5365D7'
$expectedProductionProofHash = '7CCCF1FA04E1DDC9292AB9BBD78659D44A015755671993148E17552DC3D2DC7F'
$retryMarkerPath = Join-Path $evidenceRoot 'ONE_SCHEMA_GATE_RETRY_STARTED.txt'
$proofMarkerPath = Join-Path $evidenceRoot 'POST_SCHEMA_READ_ONLY_PROOFS_V2_STARTED.txt'
$timelinePath = Join-Path $evidenceRoot 'post_schema_read_only_v2_timeline.log'

if (-not (Test-Path -LiteralPath $retryMarkerPath))
{
    throw 'Schema retry marker is absent; post-schema proof is not authorized.'
}

if (Test-Path -LiteralPath $proofMarkerPath)
{
    throw 'Post-schema read-only proof V2 has already been attempted.'
}

if ((Get-FileHash -Algorithm SHA256 -LiteralPath $schemaProofPath).Hash -ne
    $expectedSchemaProofHash)
{
    throw 'Schema proof hash mismatch.'
}

if ((Get-FileHash -Algorithm SHA256 -LiteralPath $productionProofPath).Hash -ne
    $expectedProductionProofHash)
{
    throw 'Production proof hash mismatch.'
}

function Write-Timeline
{
    param([Parameter(Mandatory = $true)][string] $Message)

    Add-Content -LiteralPath $timelinePath -Value (
        [DateTime]::UtcNow.ToString('o') + '|' + $Message
    ) -Encoding utf8
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

Set-Content -LiteralPath $proofMarkerPath -Value (
    [DateTime]::UtcNow.ToString('o') +
    '|POST_SCHEMA_READ_ONLY_PROOFS_V2|STARTED|NO_SCHEMA_EXECUTION'
) -Encoding utf8
Set-Content -LiteralPath $timelinePath -Value (
    [DateTime]::UtcNow.ToString('o') +
    '|POST_SCHEMA_READ_ONLY_PROOFS_V2|STARTED'
) -Encoding utf8

try
{
    Invoke-ReadOnlyProof `
        -Server 'lpc:CSDLTTTC\QLHVRT02' `
        -InputPath $schemaProofPath `
        -OutputPath (Join-Path $evidenceRoot 'schema_gate_read_1_v2.log') `
        -Label 'SCHEMA_GATE_READ_1_V2'

    Invoke-ReadOnlyProof `
        -Server 'lpc:CSDLTTTC\QLHVRT02' `
        -InputPath $schemaProofPath `
        -OutputPath (Join-Path $evidenceRoot 'schema_gate_read_2_v2.log') `
        -Label 'SCHEMA_GATE_READ_2_V2'

    Invoke-ReadOnlyProof `
        -Server 'lpc:CSDLTTTC' `
        -InputPath $productionProofPath `
        -OutputPath (Join-Path $evidenceRoot 'post_retry_production_read_only.log') `
        -Label 'POST_RETRY_PRODUCTION_READ_ONLY'

    Write-Timeline 'POST_SCHEMA_READ_ONLY_PROOFS_V2|SUCCEEDED'
    exit 0
}
catch
{
    Write-Timeline "POST_SCHEMA_READ_ONLY_PROOFS_V2|FAILED|$($_.Exception.Message)"
    exit 1
}
