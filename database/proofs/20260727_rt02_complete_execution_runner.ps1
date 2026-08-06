[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Validate')]
    [switch] $ValidateOnly,

    [Parameter(Mandatory = $true, ParameterSetName = 'Execute')]
    [switch] $Execute,

    [Parameter(Mandatory = $true, ParameterSetName = 'Resume')]
    [switch] $ResumeAfterReadOnlyPreflight,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeHarness')]
    [switch] $ResumeAfterHarnessIdentityPreflight,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeHarnessType')]
    [switch] $ResumeAfterHarnessIdentityTypePreflight,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeApprovalWindow')]
    [switch] $ResumeAfterApprovalWindowPreflight,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeFixtureTimeout')]
    [switch] $ResumeAfterTransientFixtureProofTimeout,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeTargetIdentityType')]
    [switch] $ResumeAfterTargetIdentityTypePreflight,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeFinalIntegrity')]
    [switch] $ResumeAfterFinalIntegritySyntaxProof,

    [Parameter(Mandatory = $true, ParameterSetName = 'ResumeProductionDrift')]
    [switch] $ResumeAfterExternalProductionTelemetryDrift,

    [Parameter(Mandatory = $true)]
    [ValidateSet('RT02B-OPERATOR-APPROVAL-20260727-01')]
    [string] $OwnerApprovalId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspaceRoot = 'D:\QLHV_APP'
$artifactRoot = 'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727'
$originalEvidenceRoot =
    'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE'
$evidenceRoot =
    if ($ResumeAfterExternalProductionTelemetryDrift.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_8'
    }
    elseif ($ResumeAfterFinalIntegritySyntaxProof.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_7'
    }
    elseif ($ResumeAfterTargetIdentityTypePreflight.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_6'
    }
    elseif ($ResumeAfterTransientFixtureProofTimeout.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_5'
    }
    elseif ($ResumeAfterApprovalWindowPreflight.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_4'
    }
    elseif ($ResumeAfterHarnessIdentityTypePreflight.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_3'
    }
    elseif ($ResumeAfterHarnessIdentityPreflight.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_2'
    }
    elseif ($ResumeAfterReadOnlyPreflight.IsPresent)
    {
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_1'
    }
    else
    {
        $originalEvidenceRoot
    }
$attemptMarkerPath = Join-Path $evidenceRoot 'RT02_COMPLETE_EXECUTION_STARTED.txt'
$timelinePath = Join-Path $evidenceRoot 'execution_timeline.log'
$summaryPath = Join-Path $evidenceRoot 'execution_summary.json'
$sqlcmdPath =
    'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$isolatedServer = 'lpc:CSDLTTTC\QLHVRT02'
$productionServer = 'lpc:CSDLTTTC'
$environmentId = 'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
$approvalExpiresAtUtc = [DateTime]::Parse(
    '2026-07-31T16:59:59Z',
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AdjustToUniversal
)
$repositoryHead = '383387e8456d1a61640eee190519ff3f28619218'
$expectedSchemaOutputHash =
    'D2F3AE13D51AFC838C7DE4B6CF04C5D9E474FC430582BBA44BE76DBE461F52DF'
$historicalProductionOutputHash =
    '42B7C7074AA95C66CCE36DFCF00931BAF26AB1E7B242713CA90057C390F04B74'
$expectedProductionOutputHash =
    '986362491865D895EECE8B4C678510C92590804D214E5D79784B25453B838848'
$externalProductionOutputHash =
    'FCC57A9AFDAE92A93C75D9E621C611EF22AC9C3B70B3F6D70A3558581E1BEBD7'
$expectedDatasetFingerprint =
    '864FDBC375868C1C6EC672794980C411C2269236829FDB86ECBBC129A77FFA8C'
$expectedSourceRowsFingerprint =
    '4BEC2240065A8F08EB971628DA88831485FA2B2BD08FEFA57AE275E69B77D36C'
$expectedTargetRowsFingerprint =
    '2AF3155D548FBEAA1849D75597557EF57EFB639BF67D1B95A23B8EAE0E4BC067'
$expectedSourceSchemaFingerprint =
    'DD02431B83E36736108A3083E1268D49EC9D7C9A3030CF9990C39B83CE7E5A1C'
$expectedMappingFingerprint =
    '9938DDC131D2C3DE91C1F35A7CBCAA5B69FE40577514CC4B38A43B1A667A0743'
$expectedOtoSchemaFingerprint =
    'D42001BF2752647360D2EB2397B9239908DCA80C10F037C79DA8C469C63348B2'
$expectedMotoSchemaFingerprint =
    '85E19B95E60E222C989FA1F222BB3A30C94A8788116CB521163907424F0EACC2'
$expectedTargetSchemaFingerprint =
    '3BDCF5C0C7CC5F0F17DA69709E03FB91C10E6CD8D1772533CF061752AEFE7634'
$runtimeConfigPath = 'D:\QLHV_APP_RUNTIME\app\appsettings.json'
$runtimeExecutablePath = 'D:\QLHV_APP_RUNTIME\app\QLHV.Api.exe'

$schemaProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02b2_schema_gate_read_only.sql'
$productionProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02b2_production_non_interference_read_only.sql'
$featuresOnProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_complete_features_on_read_only.sql'
$featuresOffProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_complete_features_off_read_only.sql'
$finalIntegrityProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_complete_final_integrity_read_only.sql'
$fixtureLoaderPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_complete_fixture_loader.ps1'
$deadlockProbePath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_complete_real_deadlock_probe.ps1'
$lockDiagnosticProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_complete_lock_diagnostic_read_only.sql'
$resume4LockDiagnosticOutputPath = Join-Path $artifactRoot `
    'resume4_lock_diagnostic_read_only.log'
$resume5TargetStateDiagnosticOutputPath = Join-Path $artifactRoot `
    'resume5_target_state_diagnostic_read_only.log'
$resume6CorrectedFinalIntegrityOffOutputPath = Join-Path $artifactRoot `
    'resume6_final_integrity_off_corrected_read_only.log'
$productionDriftProofPath = Join-Path $workspaceRoot `
    'database\proofs\20260727_rt02_production_autosync_drift_read_only.sql'
$resume7ProductionDriftOutputPath = Join-Path $artifactRoot `
    'resume7_production_autosync_drift_read_only.log'
$enableOtoPath = Join-Path $artifactRoot 'enable_oto_v2.sql'
$enableMotoPath = Join-Path $artifactRoot 'enable_moto_v2.sql'
$disableOtoPath = Join-Path $artifactRoot 'disable_oto_v2.sql'
$disableMotoPath = Join-Path $artifactRoot 'disable_moto_v2.sql'
$testProjectPath = Join-Path $workspaceRoot `
    'server\QLHV.Tests\QLHV.Tests.csproj'

$liveOptInVariables = @(
    'QLHV_RT02B2_APPROVAL_ID',
    'QLHV_RT02B2_READ_ONLY_PREFLIGHT_APPROVAL_ID',
    'QLHV_RT02B2_READ_ONLY_PREFLIGHT_RESULTS_PATH',
    'QLHV_RT02B2_RESULTS_PATH',
    'QLHV_RT02B2_PROCESS_HELPER_TOKEN',
    'QLHV_RT02B2_PROCESS_HELPER_INPUT_PATH',
    'QLHV_RT02B2_PROCESS_HELPER_SIGNAL_PATH',
    'QLHV_RT02B2_PROCESS_HELPER_MODE'
)
$postFixtureResume =
    $ResumeAfterHarnessIdentityPreflight.IsPresent -or
    $ResumeAfterHarnessIdentityTypePreflight.IsPresent -or
    $ResumeAfterApprovalWindowPreflight.IsPresent -or
    $ResumeAfterTransientFixtureProofTimeout.IsPresent -or
    $ResumeAfterTargetIdentityTypePreflight.IsPresent -or
    $ResumeAfterFinalIntegritySyntaxProof.IsPresent -or
    $ResumeAfterExternalProductionTelemetryDrift.IsPresent
$finalIntegrityOnlyResume =
    $ResumeAfterFinalIntegritySyntaxProof.IsPresent -or
    $ResumeAfterExternalProductionTelemetryDrift.IsPresent
$currentProductionOutputHash =
    if ($ResumeAfterExternalProductionTelemetryDrift.IsPresent)
    {
        $externalProductionOutputHash
    }
    else
    {
        $expectedProductionOutputHash
    }

$expectedArtifacts = @(
    [pscustomobject] @{
        Path = $schemaProofPath
        Hash = '3E757EC68C51A4246014705E0EB57A32F6F44662BE56FB7E9012618C0C5365D7'
    },
    [pscustomobject] @{
        Path = $productionProofPath
        Hash = '7CCCF1FA04E1DDC9292AB9BBD78659D44A015755671993148E17552DC3D2DC7F'
    },
    [pscustomobject] @{
        Path = $featuresOnProofPath
        Hash = '8D47EB36C920C96063D10C4DE7EE495FD9D02321F34928DB220103926A81D618'
    },
    [pscustomobject] @{
        Path = $featuresOffProofPath
        Hash = '83D490323711D1282701FE58C6BB0ACFCE6F6FD7E04F0BC913A9502316AD0216'
    },
    [pscustomobject] @{
        Path = $finalIntegrityProofPath
        Hash = '6FC4995731B14BF83946425C73F4BF15C5EB35710411B55F551DFB3784C1527F'
    },
    [pscustomobject] @{
        Path = $fixtureLoaderPath
        Hash = 'E5CD27E92533DD0A9AF7031A412EBE3B1104AFB5064ABF7C516B4A27662DAF61'
    },
    [pscustomobject] @{
        Path = $deadlockProbePath
        Hash = 'EAB7C9E3C15B8881127F4B83BC8582A91A54465A4312138D409D677CE1014252'
    },
    [pscustomobject] @{
        Path = $lockDiagnosticProofPath
        Hash = 'CAA7B2C1ED92B0AC840E0F7A3C54F331ED301824275EF5A929C0B0EDA90AE8D8'
    },
    [pscustomobject] @{
        Path = $productionDriftProofPath
        Hash = '8357D0672D003DE6986B7D010F3322ADA75EB347F5C249C40ACA048CE63CDE55'
    },
    [pscustomobject] @{
        Path = $enableOtoPath
        Hash = '6151FB3C02497D280441C7CE9566C811F543BDA3C52E8DD9126367C2E298B557'
    },
    [pscustomobject] @{
        Path = $enableMotoPath
        Hash = '755C869D0806FF33DB3588DAFF8E51991A8BB6AF56CF69E5DD9F079C8110D73B'
    },
    [pscustomobject] @{
        Path = $disableOtoPath
        Hash = '0594086AD8C420F2418145FED91F90B4D361A3CD9D50C9A1BB25BB0846E6D336'
    },
    [pscustomobject] @{
        Path = $disableMotoPath
        Hash = '157A2AFAF14E221AB6D199702AFFAF473EC4333EB37E6A6DD6FDD89410A03470'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\Sync\Rt02\Rt02b2AuthorizedSqlExecutionTests.cs')
        Hash = '06FE4399FA4D9E639E687B7050A6C9954A98AF716C52A6E160023433B7A6554B'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\Sync\Rt02\QlhvDirectRealtimeSqlIsolatedTestCompositionRoot.cs')
        Hash = '11728ECF0EAB66281EF8DF9A1FAE2616E98FA509B856151963C3C339847C2D36'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\Sync\Rt02\Rt02SqlTemplateSafetyTests.cs')
        Hash = '3040EECD82A6E3DCD5ACAE17BFFA96B1B09948B95E580B727FDA0F7D6A4BF8BB'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\bin\Release\net8.0\QLHV.Tests.dll')
        Hash = 'F8CAEDBA60477A38D790D331CB148A0EDDF7696C3244AF7A906CA2975F08AD64'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\bin\Release\net8.0\QLHV.Application.dll')
        Hash = '8CFEE3A49F92BA787035751C99AC36449C4FE5180A1EB2C73A2CDFBA1E8E3E43'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\bin\Release\net8.0\QLHV.Infrastructure.dll')
        Hash = 'F2CF5E21E5CC42883CF9776C3FC3C5289C7090E13B6E7B062BD7BD1D36AC7CDB'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Tests\bin\Release\net8.0\QLHV.Domain.dll')
        Hash = 'AFF53BCAA58FF39A3D9A58E37E3360B1D0613717576C511FD5EDC53CDF3CEA27'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Api\appsettings.Development.json')
        Hash = '12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E'
    },
    [pscustomobject] @{
        Path = (Join-Path $workspaceRoot `
            'server\QLHV.Worker\appsettings.Development.json')
        Hash = '12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E'
    },
    [pscustomobject] @{
        Path = $runtimeConfigPath
        Hash = '761220A8A466EE7943B1380C4551DB7F2296035F70905296563B4BB11DC62D48'
    }
)

function Assert-True
{
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition)
    {
        throw $Message
    }
}

function Write-Timeline
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    Add-Content -LiteralPath $timelinePath -Value (
        [DateTime]::UtcNow.ToString('o') + '|' + $Message
    ) -Encoding utf8
}

function Clear-LiveOptIns
{
    foreach ($variableName in $liveOptInVariables)
    {
        [Environment]::SetEnvironmentVariable(
            $variableName,
            $null,
            [EnvironmentVariableTarget]::Process
        )
    }
}

function Assert-ArtifactSet
{
    foreach ($artifact in $expectedArtifacts)
    {
        if (-not (Test-Path -LiteralPath $artifact.Path -PathType Leaf))
        {
            throw "Required RT02 artifact is absent: $($artifact.Path)"
        }

        $observedHash = (
            Get-FileHash -Algorithm SHA256 -LiteralPath $artifact.Path
        ).Hash
        if ($observedHash -cne $artifact.Hash)
        {
            throw "RT02 artifact hash mismatch: $($artifact.Path)"
        }
    }
}

function Assert-ReadOnlyProof
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $content = Get-Content -Raw -LiteralPath $Path
    if ($content -match
        '(?im)^\s*(CREATE|ALTER|DROP|TRUNCATE|MERGE|INSERT|UPDATE|DELETE|EXEC)\b')
    {
        throw "A read-only proof contains a mutation statement: $Path"
    }
}

function Assert-ReadOnlyPreflightResumeEvidence
{
    $expectedNames = @(
        'execution_timeline.log',
        'host_state_before.json',
        'phase_f_features_off_read_1.log',
        'phase_f_features_off_read_2.log',
        'production_before.log',
        'production_final.log',
        'RT02_COMPLETE_EXECUTION_STARTED.txt',
        'schema_gate_read_1.log',
        'schema_gate_read_2.log'
    ) | Sort-Object
    if (-not (Test-Path -LiteralPath $originalEvidenceRoot -PathType Container))
    {
        throw 'The original read-only preflight evidence root is absent.'
    }

    $actualNames = @(
        Get-ChildItem -LiteralPath $originalEvidenceRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The original preflight evidence file set is not exact.'

    $originalMarkerPath = Join-Path $originalEvidenceRoot `
        'RT02_COMPLETE_EXECUTION_STARTED.txt'
    $originalTimelinePath = Join-Path $originalEvidenceRoot `
        'execution_timeline.log'
    $schemaRead1Path = Join-Path $originalEvidenceRoot `
        'schema_gate_read_1.log'
    $schemaRead2Path = Join-Path $originalEvidenceRoot `
        'schema_gate_read_2.log'
    $productionBeforePath = Join-Path $originalEvidenceRoot `
        'production_before.log'
    $productionFinalPath = Join-Path $originalEvidenceRoot `
        'production_final.log'
    $featuresOff1Path = Join-Path $originalEvidenceRoot `
        'phase_f_features_off_read_1.log'
    $featuresOff2Path = Join-Path $originalEvidenceRoot `
        'phase_f_features_off_read_2.log'
    $hostBeforePath = Join-Path $originalEvidenceRoot `
        'host_state_before.json'

    Assert-FileHash `
        -Path $schemaRead1Path `
        -ExpectedHash $expectedSchemaOutputHash `
        -Label 'Original schema gate read 1'
    Assert-FileHash `
        -Path $schemaRead2Path `
        -ExpectedHash $expectedSchemaOutputHash `
        -Label 'Original schema gate read 2'
    Assert-FileHash `
        -Path $productionBeforePath `
        -ExpectedHash $expectedProductionOutputHash `
        -Label 'Original production before'
    Assert-FileHash `
        -Path $productionFinalPath `
        -ExpectedHash $expectedProductionOutputHash `
        -Label 'Original production final'
    Assert-SameFileHash `
        -FirstPath $featuresOff1Path `
        -SecondPath $featuresOff2Path `
        -Label 'Original features-off proof'
    Assert-FileHash `
        -Path $featuresOff1Path `
        -ExpectedHash `
            '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
        -Label 'Original features-off state'

    $timeline = Get-Content -Raw -LiteralPath $originalTimelinePath
    Assert-True `
        ($timeline -match
            'RT02_EXECUTION_BODY\|FAILED\|Production before output hash mismatch\.') `
        'The original execution did not stop on the read-only baseline mismatch.'
    Assert-True `
        ($timeline -notmatch
            'phase_a_enable_|phase_b_fixture_|MAIN_HARNESS|real_sql_deadlock') `
        'The original read-only preflight unexpectedly entered a mutation phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The original features-off proof did not complete.'
    Assert-True `
        ($timeline -match 'production_final\|SUCCEEDED') `
        'The original final production read did not complete.'

    $markerText = Get-Content -Raw -LiteralPath $originalMarkerPath
    $originalStartedAtUtc = [DateTime]::Parse(
        ($markerText -split '\|')[0],
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AdjustToUniversal
    )
    $productionText = Get-Content -Raw -LiteralPath $productionBeforePath
    $latestMatch = [regex]::Match(
        $productionText,
        '(?m)^EXISTING_AUTO_SYNC_STATE\|6\|0\|' +
        'E16F507F-4255-4D61-9383-AA5B30715E4E\|SUCCEEDED\|' +
        '([0-9\-:\. ]+)\r?$'
    )
    Assert-True $latestMatch.Success `
        'The rebased production Auto Sync evidence is not exact.'
    $latestProductionRunUtc = [DateTime]::SpecifyKind(
        [DateTime]::Parse(
            $latestMatch.Groups[1].Value,
            [Globalization.CultureInfo]::InvariantCulture
        ),
        [DateTimeKind]::Utc
    )
    Assert-True `
        ($latestProductionRunUtc -lt $originalStartedAtUtc) `
        'Production drift was not proven to predate the RT02 runner.'

    $hostBefore = Get-Content -Raw -LiteralPath $hostBeforePath |
        ConvertFrom-Json
    Assert-True (-not [bool]$hostBefore.QlhvAutoSyncEnabled) `
        'Original host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostBefore.CsdtRealtimeSyncEnabled) `
        'Original host evidence has realtime sync enabled.'
    Assert-True ($hostBefore.ProductionWorkerProcessCount -eq 0) `
        'Original host evidence has a production worker process.'
    Assert-True `
        ($historicalProductionOutputHash -cne $expectedProductionOutputHash) `
        'The production rebaseline did not record a distinct historical hash.'
}

function Assert-HarnessIdentityPreflightResumeEvidence
{
    Assert-ReadOnlyPreflightResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_1'
    $expectedNames = @(
        'execution_summary.json',
        'execution_timeline.log',
        'host_state_before.json',
        'host_state_final.json',
        'main_harness.stderr.log',
        'main_harness.stdout.log',
        'main_harness_result.json',
        'phase_a_enable_moto.log',
        'phase_a_enable_oto.log',
        'phase_a_features_on_read_1.log',
        'phase_a_features_on_read_2.log',
        'phase_b_fixture_execute.stderr.log',
        'phase_b_fixture_execute.stdout.json',
        'phase_b_fixture_verify_1.stderr.log',
        'phase_b_fixture_verify_1.stdout.json',
        'phase_b_fixture_verify_2.stderr.log',
        'phase_b_fixture_verify_2.stdout.json',
        'phase_f_disable_moto.log',
        'phase_f_disable_oto.log',
        'phase_f_features_off_read_1.log',
        'phase_f_features_off_read_2.log',
        'production_after_phase_a.log',
        'production_after_phase_b.log',
        'production_before.log',
        'production_final.log',
        'RT02_COMPLETE_EXECUTION_STARTED.txt',
        'rt02_complete_main_harness.trx',
        'schema_gate_read_1.log',
        'schema_gate_read_2.log'
    ) | Sort-Object
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The harness identity-preflight evidence root is absent.'
    }
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The harness identity-preflight evidence file set is not exact.'

    foreach ($schemaName in @('schema_gate_read_1.log', 'schema_gate_read_2.log'))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $schemaName) `
            -ExpectedHash $expectedSchemaOutputHash `
            -Label "Harness-preflight $schemaName"
    }
    foreach ($productionName in @(
            'production_before.log',
            'production_after_phase_a.log',
            'production_after_phase_b.log',
            'production_final.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $expectedProductionOutputHash `
            -Label "Harness-preflight $productionName"
    }
    foreach ($featuresOnName in @(
            'phase_a_features_on_read_1.log',
            'phase_a_features_on_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOnName) `
            -ExpectedHash `
                '32571E686502866A016076AD9BF3D75B1142E7A2760BEA7704517CE43C9E20E6' `
            -Label "Harness-preflight $featuresOnName"
    }
    foreach ($featuresOffName in @(
            'phase_f_features_off_read_1.log',
            'phase_f_features_off_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOffName) `
            -ExpectedHash `
                '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
            -Label "Harness-preflight $featuresOffName"
    }

    $fixtureExecutePath = Join-Path $previousRoot `
        'phase_b_fixture_execute.stdout.json'
    $fixtureVerify1Path = Join-Path $previousRoot `
        'phase_b_fixture_verify_1.stdout.json'
    $fixtureVerify2Path = Join-Path $previousRoot `
        'phase_b_fixture_verify_2.stdout.json'
    Assert-FileHash `
        -Path $fixtureExecutePath `
        -ExpectedHash `
            'BA8E114121207D225E697AB0E8E52D8DF29A4A3F7AB29FE8D2963D4C6FA1C8F0' `
        -Label 'Committed fixture evidence'
    Assert-FileHash `
        -Path $fixtureVerify1Path `
        -ExpectedHash `
            '76CEC167CD885B91288B0BA3FFB5DD828AAC2701EABD6A4108227544C2A158BB' `
        -Label 'Committed fixture verification 1'
    Assert-FileHash `
        -Path $fixtureVerify2Path `
        -ExpectedHash `
            '76CEC167CD885B91288B0BA3FFB5DD828AAC2701EABD6A4108227544C2A158BB' `
        -Label 'Committed fixture verification 2'
    Assert-FixtureResult `
        -Result (
            Get-Content -Raw -LiteralPath $fixtureExecutePath |
                ConvertFrom-Json
        ) `
        -IsVerifyResult $false
    Assert-FixtureResult `
        -Result (
            Get-Content -Raw -LiteralPath $fixtureVerify1Path |
                ConvertFrom-Json
        ) `
        -IsVerifyResult $true
    Assert-FixtureResult `
        -Result (
            Get-Content -Raw -LiteralPath $fixtureVerify2Path |
                ConvertFrom-Json
        ) `
        -IsVerifyResult $true

    $harnessResultPath = Join-Path $previousRoot 'main_harness_result.json'
    Assert-FileHash `
        -Path $harnessResultPath `
        -ExpectedHash `
            'CDEEF764B2E31C10F572E131E468A3B759C151919A8295D572977FF2AC788B93' `
        -Label 'Harness identity-preflight result'
    $harnessResult = Get-Content -Raw -LiteralPath $harnessResultPath |
        ConvertFrom-Json
    Assert-True ($harnessResult.Status -ceq 'BLOCKED') `
        'The prior harness result did not fail closed.'
    Assert-True `
        ($harnessResult.CurrentStep -ceq 'identity_and_metadata_preflight') `
        'The prior harness did not stop in identity preflight.'
    Assert-True (@($harnessResult.Scenarios).Count -eq 0) `
        'The prior harness entered a business scenario.'
    Assert-True `
        ($harnessResult.ErrorMessage -match
            'recovery\.database_guid.*could not be bound') `
        'The prior harness error is not the reviewed read-only alias failure.'

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            '9EEE557C9E185AE67FAF8010F2DC38583807A6193D8CD79572BEF38013C86B89' `
        -Label 'Harness-preflight execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The prior execution summary did not fail closed.'
    Assert-True (@($summary.CleanupFailures).Count -eq 0) `
        'The prior feature cleanup was not clean.'

    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True `
        ($timeline -match 'MAIN_HARNESS\|FAILED') `
        'The prior harness failure is absent from its timeline.'
    Assert-True `
        ($timeline -notmatch 'real_sql_deadlock|final_integrity') `
        'The prior harness unexpectedly entered a post-harness phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The prior harness cleanup did not prove features OFF.'
    Assert-True `
        ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The prior harness cleanup did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Prior final host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Prior final host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Prior final host evidence has a production worker.'
}

function Assert-HarnessIdentityTypePreflightResumeEvidence
{
    Assert-HarnessIdentityPreflightResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_2'
    $expectedNames = @(
        'execution_summary.json',
        'execution_timeline.log',
        'host_state_before.json',
        'host_state_final.json',
        'main_harness.stderr.log',
        'main_harness.stdout.log',
        'main_harness_result.json',
        'phase_a_enable_moto.log',
        'phase_a_enable_oto.log',
        'phase_a_features_on_read_1.log',
        'phase_a_features_on_read_2.log',
        'phase_f_disable_moto.log',
        'phase_f_disable_oto.log',
        'phase_f_features_off_read_1.log',
        'phase_f_features_off_read_2.log',
        'production_after_phase_a.log',
        'production_after_phase_b.log',
        'production_before.log',
        'production_final.log',
        'resume_2_existing_fixture_verify_1.stderr.log',
        'resume_2_existing_fixture_verify_1.stdout.json',
        'resume_2_existing_fixture_verify_2.stderr.log',
        'resume_2_existing_fixture_verify_2.stdout.json',
        'resume_2_features_off_pre_enable_read_1.log',
        'resume_2_features_off_pre_enable_read_2.log',
        'RT02_COMPLETE_EXECUTION_STARTED.txt',
        'rt02_complete_main_harness.trx'
    ) | Sort-Object
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The harness identity-type-preflight evidence root is absent.'
    }
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The harness identity-type-preflight evidence file set is not exact.'

    foreach ($productionName in @(
            'production_before.log',
            'production_after_phase_a.log',
            'production_after_phase_b.log',
            'production_final.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $expectedProductionOutputHash `
            -Label "Harness-type-preflight $productionName"
    }
    foreach ($featuresOnName in @(
            'phase_a_features_on_read_1.log',
            'phase_a_features_on_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOnName) `
            -ExpectedHash `
                '32571E686502866A016076AD9BF3D75B1142E7A2760BEA7704517CE43C9E20E6' `
            -Label "Harness-type-preflight $featuresOnName"
    }
    foreach ($featuresOffName in @(
            'resume_2_features_off_pre_enable_read_1.log',
            'resume_2_features_off_pre_enable_read_2.log',
            'phase_f_features_off_read_1.log',
            'phase_f_features_off_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOffName) `
            -ExpectedHash `
                '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
            -Label "Harness-type-preflight $featuresOffName"
    }
    foreach ($fixtureVerifyName in @(
            'resume_2_existing_fixture_verify_1.stdout.json',
            'resume_2_existing_fixture_verify_2.stdout.json'
        ))
    {
        $fixtureVerifyPath = Join-Path $previousRoot $fixtureVerifyName
        Assert-FileHash `
            -Path $fixtureVerifyPath `
            -ExpectedHash `
                '76CEC167CD885B91288B0BA3FFB5DD828AAC2701EABD6A4108227544C2A158BB' `
            -Label "Harness-type-preflight $fixtureVerifyName"
        Assert-FixtureResult `
            -Result (
                Get-Content -Raw -LiteralPath $fixtureVerifyPath |
                    ConvertFrom-Json
            ) `
            -IsVerifyResult $true
    }

    $harnessResultPath = Join-Path $previousRoot 'main_harness_result.json'
    Assert-FileHash `
        -Path $harnessResultPath `
        -ExpectedHash `
            '325F329A0FE425934525AAE0CF576EBD0CE633AB36FA220B3420245576AB8132' `
        -Label 'Harness identity-type-preflight result'
    $harnessResult = Get-Content -Raw -LiteralPath $harnessResultPath |
        ConvertFrom-Json
    Assert-True ($harnessResult.Status -ceq 'BLOCKED') `
        'The prior type-preflight harness result did not fail closed.'
    Assert-True `
        ($harnessResult.CurrentStep -ceq 'identity_and_metadata_preflight') `
        'The prior harness did not stop in identity type preflight.'
    Assert-True (@($harnessResult.Scenarios).Count -eq 0) `
        'The prior type-preflight harness entered a business scenario.'
    Assert-True `
        ($harnessResult.ErrorType -ceq 'System.InvalidCastException') `
        'The prior harness error type is not the reviewed read-only type failure.'
    Assert-True `
        ($harnessResult.ErrorMessage -ceq
            "Unable to cast object of type 'System.Int16' to type 'System.Int32'.") `
        'The prior harness error is not the reviewed database_id type failure.'

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            '41B7CE65C463B67FD6EC947A9D15E7A64AD0AF4BD681DC000267CD7E07F75597' `
        -Label 'Harness-type-preflight execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The prior type-preflight summary did not fail closed.'
    Assert-True (@($summary.CleanupFailures).Count -eq 0) `
        'The prior type-preflight cleanup was not clean.'

    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True `
        ($timeline -match 'MAIN_HARNESS\|FAILED\|EXIT=1') `
        'The prior type-preflight failure is absent from its timeline.'
    Assert-True `
        ($timeline -notmatch 'real_sql_deadlock|final_integrity') `
        'The prior type-preflight unexpectedly entered a post-harness phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The prior type-preflight cleanup did not prove features OFF.'
    Assert-True `
        ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The prior type-preflight cleanup did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Prior type-preflight host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Prior type-preflight host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Prior type-preflight host evidence has a production worker.'
}

function Assert-ApprovalWindowPreflightResumeEvidence
{
    Assert-HarnessIdentityTypePreflightResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_3'
    $expectedNames = @(
        'execution_summary.json',
        'execution_timeline.log',
        'host_state_before.json',
        'host_state_final.json',
        'phase_a_enable_moto.log',
        'phase_a_enable_oto.log',
        'phase_a_features_on_read_1.log',
        'phase_a_features_on_read_2.log',
        'phase_f_disable_moto.log',
        'phase_f_disable_oto.log',
        'phase_f_features_off_read_1.log',
        'phase_f_features_off_read_2.log',
        'production_after_phase_a.log',
        'production_after_phase_b.log',
        'production_before.log',
        'production_final.log',
        'read_only_harness_preflight.stderr.log',
        'read_only_harness_preflight.stdout.log',
        'resume_2_existing_fixture_verify_1.stderr.log',
        'resume_2_existing_fixture_verify_1.stdout.json',
        'resume_2_existing_fixture_verify_2.stderr.log',
        'resume_2_existing_fixture_verify_2.stdout.json',
        'resume_2_features_off_pre_enable_read_1.log',
        'resume_2_features_off_pre_enable_read_2.log',
        'RT02_COMPLETE_EXECUTION_STARTED.txt',
        'rt02_read_only_harness_preflight.trx'
    ) | Sort-Object
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The approval-window-preflight evidence root is absent.'
    }
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The approval-window-preflight evidence file set is not exact.'

    foreach ($productionName in @(
            'production_before.log',
            'production_after_phase_a.log',
            'production_after_phase_b.log',
            'production_final.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $expectedProductionOutputHash `
            -Label "Approval-window-preflight $productionName"
    }
    foreach ($featuresOnName in @(
            'phase_a_features_on_read_1.log',
            'phase_a_features_on_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOnName) `
            -ExpectedHash `
                '32571E686502866A016076AD9BF3D75B1142E7A2760BEA7704517CE43C9E20E6' `
            -Label "Approval-window-preflight $featuresOnName"
    }
    foreach ($featuresOffName in @(
            'resume_2_features_off_pre_enable_read_1.log',
            'resume_2_features_off_pre_enable_read_2.log',
            'phase_f_features_off_read_1.log',
            'phase_f_features_off_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOffName) `
            -ExpectedHash `
                '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
            -Label "Approval-window-preflight $featuresOffName"
    }
    foreach ($fixtureVerifyName in @(
            'resume_2_existing_fixture_verify_1.stdout.json',
            'resume_2_existing_fixture_verify_2.stdout.json'
        ))
    {
        $fixtureVerifyPath = Join-Path $previousRoot $fixtureVerifyName
        Assert-FileHash `
            -Path $fixtureVerifyPath `
            -ExpectedHash `
                '76CEC167CD885B91288B0BA3FFB5DD828AAC2701EABD6A4108227544C2A158BB' `
            -Label "Approval-window-preflight $fixtureVerifyName"
        Assert-FixtureResult `
            -Result (
                Get-Content -Raw -LiteralPath $fixtureVerifyPath |
                    ConvertFrom-Json
            ) `
            -IsVerifyResult $true
    }

    Assert-FileHash `
        -Path (Join-Path $previousRoot 'RT02_COMPLETE_EXECUTION_STARTED.txt') `
        -ExpectedHash `
            'F6F35C08A85513195161DB68FB573ADE043BD7EDE8451AB780D42CB1C7AD921A' `
        -Label 'Approval-window-preflight attempt marker'
    Assert-FileHash `
        -Path (Join-Path $previousRoot 'read_only_harness_preflight.stderr.log') `
        -ExpectedHash `
            '45C53DB2D5171172933F1A295E36C855099AE02EF10C2C0BB7304DB704B33981' `
        -Label 'Approval-window-preflight stderr'
    Assert-FileHash `
        -Path (Join-Path $previousRoot 'read_only_harness_preflight.stdout.log') `
        -ExpectedHash `
            '807D4FE7BCA06B56E58E7D94573677525B3EDFE77B514E014183ECE5AF704CD9' `
        -Label 'Approval-window-preflight stdout'
    Assert-FileHash `
        -Path (Join-Path $previousRoot 'rt02_read_only_harness_preflight.trx') `
        -ExpectedHash `
            'C10F0F3C2B9CAB317545B2CD79D96FFA90A5AC87ACBA45EBF5E2D67F24A98DC1' `
        -Label 'Approval-window-preflight TRX'

    $preflightOutput = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'read_only_harness_preflight.stdout.log'
    )
    Assert-True `
        ($preflightOutput -match
            'isolated environment approval window is invalid or expired') `
        'The prior preflight error is not the reviewed future-created timestamp failure.'
    $preflightTrx = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'rt02_read_only_harness_preflight.trx'
    )
    Assert-True `
        ($preflightTrx -match
            'testName="QLHV\.Tests\.Sync\.Rt02\.Rt02b2AuthorizedSqlExecutionTests\.' +
            'Authorized_isolated_SQL_read_only_preflight_passes_all_gates"') `
        'The prior preflight TRX does not identify the exact read-only gate.'
    Assert-True ($preflightTrx -match 'outcome="Failed"') `
        'The prior preflight TRX did not fail closed.'

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            '7DEA7F542AC38ACE53C371646E55754559B68AF53F61E80FCDFC6D87C24D5F51' `
        -Label 'Approval-window-preflight execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The prior approval-window-preflight summary did not fail closed.'
    Assert-True `
        ($summary.BodyError -ceq
            'The read-only harness preflight failed with exit code 1.') `
        'The prior approval-window-preflight body error is not exact.'
    Assert-True (@($summary.CleanupFailures).Count -eq 0) `
        'The prior approval-window-preflight cleanup was not clean.'

    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True `
        ($timeline -match 'READ_ONLY_HARNESS_PREFLIGHT\|FAILED\|EXIT=1') `
        'The prior approval-window preflight failure is absent from its timeline.'
    Assert-True `
        ($timeline -notmatch 'MAIN_HARNESS|real_sql_deadlock|final_integrity') `
        'The prior approval-window preflight entered a business or post-harness phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The prior approval-window preflight cleanup did not prove features OFF.'
    Assert-True `
        ($timeline -match 'production_final\|SUCCEEDED') `
        'The prior approval-window preflight cleanup did not prove production state.'
    Assert-True `
        ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The prior approval-window preflight cleanup did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Prior approval-window host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Prior approval-window host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Prior approval-window host evidence has a production worker.'
}

function Assert-TransientFixtureProofTimeoutResumeEvidence
{
    Assert-ApprovalWindowPreflightResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_4'
    $expectedNames = @(
        'execution_summary.json',
        'execution_timeline.log',
        'host_state_before.json',
        'host_state_final.json',
        'phase_a_enable_moto.log',
        'phase_a_enable_oto.log',
        'phase_a_features_on_read_1.log',
        'phase_a_features_on_read_2.log',
        'phase_f_disable_moto.log',
        'phase_f_disable_oto.log',
        'phase_f_features_off_read_1.log',
        'phase_f_features_off_read_2.log',
        'production_after_phase_a.log',
        'production_before.log',
        'production_final.log',
        'resume_2_existing_fixture_verify_1.stderr.log',
        'resume_2_existing_fixture_verify_1.stdout.json',
        'resume_2_features_off_pre_enable_read_1.log',
        'resume_2_features_off_pre_enable_read_2.log',
        'RT02_COMPLETE_EXECUTION_STARTED.txt'
    ) | Sort-Object
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The transient fixture-proof-timeout evidence root is absent.'
    }
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The transient fixture-proof-timeout evidence file set is not exact.'

    foreach ($productionName in @(
            'production_before.log',
            'production_after_phase_a.log',
            'production_final.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $expectedProductionOutputHash `
            -Label "Transient-fixture-timeout $productionName"
    }
    foreach ($featuresOnName in @(
            'phase_a_features_on_read_1.log',
            'phase_a_features_on_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOnName) `
            -ExpectedHash `
                '32571E686502866A016076AD9BF3D75B1142E7A2760BEA7704517CE43C9E20E6' `
            -Label "Transient-fixture-timeout $featuresOnName"
    }
    foreach ($featuresOffName in @(
            'resume_2_features_off_pre_enable_read_1.log',
            'resume_2_features_off_pre_enable_read_2.log',
            'phase_f_features_off_read_1.log',
            'phase_f_features_off_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOffName) `
            -ExpectedHash `
                '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
            -Label "Transient-fixture-timeout $featuresOffName"
    }
    Assert-FileHash `
        -Path (Join-Path $previousRoot 'RT02_COMPLETE_EXECUTION_STARTED.txt') `
        -ExpectedHash `
            'D8DB8FC1D0A213BACEB527E65C00F7C5B334311E4989487B47804AE9DDC95F0C' `
        -Label 'Transient-fixture-timeout attempt marker'
    foreach ($emptyFixtureOutputName in @(
            'resume_2_existing_fixture_verify_1.stderr.log',
            'resume_2_existing_fixture_verify_1.stdout.json'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $emptyFixtureOutputName) `
            -ExpectedHash `
                'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855' `
            -Label "Transient-fixture-timeout $emptyFixtureOutputName"
    }

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            '0923A4CB20FB664C6A059F2DE1EB468CC19F44B339DB8E63E05BB566AB5C9196' `
        -Label 'Transient-fixture-timeout execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The transient fixture-proof-timeout summary did not fail closed.'
    Assert-True `
        ($summary.BodyError -match
            '(?s)^Invoke-ReadOnlyFixtureGuard .*Execution Timeout Expired') `
        'The prior body error is not the reviewed read-only fixture timeout.'
    Assert-True (@($summary.CleanupFailures).Count -eq 0) `
        'The transient fixture-proof-timeout cleanup was not clean.'

    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True `
        ($timeline -match
            'resume_2_existing_fixture_verify_1\|STARTED') `
        'The prior fixture verification did not start.'
    Assert-True `
        ($timeline -match
            'RT02_EXECUTION_BODY\|FAILED\|Invoke-ReadOnlyFixtureGuard') `
        'The prior fixture timeout is absent from its timeline.'
    $laterPhasePattern =
        'production_after_phase_b|READ_ONLY_HARNESS_PREFLIGHT|' +
        'MAIN_HARNESS|real_sql_deadlock|final_integrity'
    Assert-True `
        ($timeline -notmatch $laterPhasePattern) `
        'The prior fixture timeout entered a later RT02 phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The prior fixture-timeout cleanup did not prove features OFF.'
    Assert-True `
        ($timeline -match 'production_final\|SUCCEEDED') `
        'The prior fixture-timeout cleanup did not prove production state.'
    Assert-True `
        ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The prior fixture-timeout cleanup did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Prior fixture-timeout host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Prior fixture-timeout host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Prior fixture-timeout host evidence has a production worker.'

    Assert-FileHash `
        -Path $resume4LockDiagnosticOutputPath `
        -ExpectedHash `
            '3017FC12AB598C21A9173539B45192EF31C78D2B935C18794AB8DC427EFD6093' `
        -Label 'Post-timeout read-only lock diagnostic'
    $diagnostic = Get-Content -Raw -LiteralPath $resume4LockDiagnosticOutputPath
    Assert-True ($diagnostic -notmatch '(?m)^LOCK\|') `
        'The post-timeout diagnostic found a surviving lock.'
    Assert-True ($diagnostic -notmatch '(?m)^ACTIVE_TRANSACTION\|') `
        'The post-timeout diagnostic found a surviving transaction.'
    Assert-True `
        ($diagnostic -match
            '(?m)^FEATURE_STATE\|QLHV_RT02_OTO_TEST\|0\|0\|0\|0\r?$') `
        'The post-timeout diagnostic did not prove OTO features OFF.'
    Assert-True `
        ($diagnostic -match
            '(?m)^FEATURE_STATE\|QLHV_RT02_MOTO_TEST\|0\|0\|0\|0\r?$') `
        'The post-timeout diagnostic did not prove MOTO features OFF.'
    $targetStatePattern =
        '(?m)^TARGET_STATE\|RT02B0-CSDLTTTC-QLHVRT02-20260727-01\|' +
        '[^|]+\|' + $expectedDatasetFingerprint + '\|0\|0\|0\r?$'
    Assert-True `
        ($diagnostic -match $targetStatePattern) `
        'The post-timeout diagnostic did not prove the untouched target state.'
}

function Assert-TargetIdentityTypePreflightResumeEvidence
{
    Assert-TransientFixtureProofTimeoutResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_5'
    $expectedNames = @(
        'execution_summary.json',
        'execution_timeline.log',
        'host_state_before.json',
        'host_state_final.json',
        'main_harness.stderr.log',
        'main_harness.stdout.log',
        'main_harness_result.json',
        'phase_a_enable_moto.log',
        'phase_a_enable_oto.log',
        'phase_a_features_on_read_1.log',
        'phase_a_features_on_read_2.log',
        'phase_f_disable_moto.log',
        'phase_f_disable_oto.log',
        'phase_f_features_off_read_1.log',
        'phase_f_features_off_read_2.log',
        'production_after_phase_a.log',
        'production_after_phase_b.log',
        'production_before.log',
        'production_final.log',
        'read_only_harness_preflight.stderr.log',
        'read_only_harness_preflight.stdout.log',
        'read_only_harness_preflight_result.json',
        'resume_2_existing_fixture_verify_1.stderr.log',
        'resume_2_existing_fixture_verify_1.stdout.json',
        'resume_2_existing_fixture_verify_2.stderr.log',
        'resume_2_existing_fixture_verify_2.stdout.json',
        'resume_2_features_off_pre_enable_read_1.log',
        'resume_2_features_off_pre_enable_read_2.log',
        'RT02_COMPLETE_EXECUTION_STARTED.txt',
        'rt02_complete_main_harness.trx',
        'rt02_read_only_harness_preflight.trx'
    ) | Sort-Object
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The target-identity-type-preflight evidence root is absent.'
    }
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The target-identity-type-preflight evidence file set is not exact.'

    foreach ($productionName in @(
            'production_before.log',
            'production_after_phase_a.log',
            'production_after_phase_b.log',
            'production_final.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $expectedProductionOutputHash `
            -Label "Target-identity-type-preflight $productionName"
    }
    foreach ($featuresOnName in @(
            'phase_a_features_on_read_1.log',
            'phase_a_features_on_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOnName) `
            -ExpectedHash `
                '32571E686502866A016076AD9BF3D75B1142E7A2760BEA7704517CE43C9E20E6' `
            -Label "Target-identity-type-preflight $featuresOnName"
    }
    foreach ($featuresOffName in @(
            'resume_2_features_off_pre_enable_read_1.log',
            'resume_2_features_off_pre_enable_read_2.log',
            'phase_f_features_off_read_1.log',
            'phase_f_features_off_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOffName) `
            -ExpectedHash `
                '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
            -Label "Target-identity-type-preflight $featuresOffName"
    }
    foreach ($fixtureVerifyName in @(
            'resume_2_existing_fixture_verify_1.stdout.json',
            'resume_2_existing_fixture_verify_2.stdout.json'
        ))
    {
        $fixtureVerifyPath = Join-Path $previousRoot $fixtureVerifyName
        Assert-FileHash `
            -Path $fixtureVerifyPath `
            -ExpectedHash `
                '76CEC167CD885B91288B0BA3FFB5DD828AAC2701EABD6A4108227544C2A158BB' `
            -Label "Target-identity-type-preflight $fixtureVerifyName"
        Assert-FixtureResult `
            -Result (
                Get-Content -Raw -LiteralPath $fixtureVerifyPath |
                    ConvertFrom-Json
            ) `
            -IsVerifyResult $true
    }

    foreach ($emptyName in @(
            'read_only_harness_preflight.stderr.log',
            'resume_2_existing_fixture_verify_1.stderr.log',
            'resume_2_existing_fixture_verify_2.stderr.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $emptyName) `
            -ExpectedHash `
                'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855' `
            -Label "Target-identity-type-preflight $emptyName"
    }
    $pinnedHarnessFiles = @(
        [pscustomobject] @{
            Name = 'read_only_harness_preflight.stdout.log'
            Hash = '842661A93F54B82FAC15A752C6AE38DF95BA2D70DEE1CBDB9A57A2ED148A641A'
        },
        [pscustomobject] @{
            Name = 'read_only_harness_preflight_result.json'
            Hash = '4EEB521C02D60F3D624F8C72C08201C6535CD5A011E03C41A9B3F5F098282B57'
        },
        [pscustomobject] @{
            Name = 'rt02_read_only_harness_preflight.trx'
            Hash = '380A3B052F822562992A52A18387517A9047273BB52A62E1AEF6A4D5420F572A'
        },
        [pscustomobject] @{
            Name = 'main_harness.stderr.log'
            Hash = '4E3C239B541FC699764A0A47862D3CB35CC605A43DB3C24DC8346C7895E315BE'
        },
        [pscustomobject] @{
            Name = 'main_harness.stdout.log'
            Hash = 'B133E5F4625F6EF7ACF53761B1B4E7852916DD45BB700781A9B373635BC764B1'
        },
        [pscustomobject] @{
            Name = 'main_harness_result.json'
            Hash = '9565DDD29396EEA38E031E390BE98364CB751560A51E41A98DB7D29EADA2C426'
        },
        [pscustomobject] @{
            Name = 'rt02_complete_main_harness.trx'
            Hash = '812CB8549AE827378EF6D3160ED27F7C3C0C1275644DB9CD492527FF367F3566'
        }
    )
    foreach ($pinnedFile in $pinnedHarnessFiles)
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $pinnedFile.Name) `
            -ExpectedHash $pinnedFile.Hash `
            -Label "Target-identity-type-preflight $($pinnedFile.Name)"
    }

    $preflightResult = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'read_only_harness_preflight_result.json'
    ) | ConvertFrom-Json
    Assert-True ($preflightResult.Status -ceq 'VERIFIED_READ_ONLY_PREFLIGHT') `
        'The prior dedicated read-only preflight did not pass.'
    Assert-True `
        ($preflightResult.EnvironmentId -ceq $environmentId -and
         $preflightResult.ApprovalId -ceq $OwnerApprovalId -and
         $preflightResult.DatasetFingerprint -ceq $expectedDatasetFingerprint -and
         $preflightResult.DatabaseIdentityCount -eq 3) `
        'The prior dedicated read-only preflight identity is invalid.'
    Assert-True `
        ($preflightResult.Snapshot.MarkerCount -eq 0 -and
         $preflightResult.Snapshot.CheckpointCount -eq 0 -and
         $preflightResult.Snapshot.DuplicateActiveGroups -eq 0 -and
         $preflightResult.Snapshot.PiiLikeRows -eq 0) `
        'The prior dedicated read-only preflight state is not clean.'

    $harnessResult = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'main_harness_result.json'
    ) | ConvertFrom-Json
    Assert-True ($harnessResult.Status -ceq 'BLOCKED') `
        'The prior target identity-type harness result did not fail closed.'
    Assert-True `
        ($harnessResult.CurrentStep -ceq 'minimal_insert_update_retained') `
        'The prior harness did not stop in the first measured cycle.'
    Assert-True (@($harnessResult.Scenarios).Count -eq 0) `
        'The prior target identity-type harness completed a scenario.'
    Assert-True `
        ($harnessResult.ErrorType -ceq 'System.InvalidCastException' -and
         $harnessResult.ErrorMessage -ceq
            "Unable to cast object of type 'System.Int16' to type 'System.Int32'.") `
        'The prior harness error is not the reviewed target DB_ID type failure.'

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            '5F7375DE80FDD860AFFF3F6EF4ADB66F3D37845B2CFADE38312080BE95E3E441' `
        -Label 'Target-identity-type-preflight execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The prior target identity-type summary did not fail closed.'
    Assert-True `
        ($summary.BodyError -ceq
            'The exact isolated SQL harness failed with exit code 1.') `
        'The prior target identity-type body error is not exact.'
    Assert-True (@($summary.CleanupFailures).Count -eq 0) `
        'The prior target identity-type cleanup was not clean.'

    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True `
        ($timeline -match 'READ_ONLY_HARNESS_PREFLIGHT\|SUCCEEDED') `
        'The prior dedicated read-only preflight success is absent.'
    Assert-True ($timeline -match 'MAIN_HARNESS\|FAILED\|EXIT=1') `
        'The prior target identity-type harness failure is absent.'
    Assert-True `
        ($timeline -notmatch 'real_sql_deadlock|final_integrity') `
        'The prior target identity-type failure entered a post-harness phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The prior target identity-type cleanup did not prove features OFF.'
    Assert-True `
        ($timeline -match 'production_final\|SUCCEEDED') `
        'The prior target identity-type cleanup did not prove production state.'
    Assert-True `
        ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The prior target identity-type cleanup did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Prior target identity-type host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Prior target identity-type host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Prior target identity-type host evidence has a production worker.'

    Assert-FileHash `
        -Path $resume5TargetStateDiagnosticOutputPath `
        -ExpectedHash `
            '72D2ABA4051A13EFA2D77CB4AE6D90F2850073250B9892F23D44CB4A89766783' `
        -Label 'Post-target-identity-failure read-only diagnostic'
    $diagnostic = Get-Content -Raw -LiteralPath (
        $resume5TargetStateDiagnosticOutputPath
    )
    Assert-True ($diagnostic -notmatch '(?m)^LOCK\|') `
        'The post-target-identity diagnostic found a surviving lock.'
    Assert-True ($diagnostic -notmatch '(?m)^ACTIVE_TRANSACTION\|') `
        'The post-target-identity diagnostic found a surviving transaction.'
    Assert-True `
        ($diagnostic -match
            '(?m)^FEATURE_STATE\|QLHV_RT02_OTO_TEST\|0\|0\|0\|0\r?$') `
        'The post-target-identity diagnostic did not prove OTO features OFF.'
    Assert-True `
        ($diagnostic -match
            '(?m)^FEATURE_STATE\|QLHV_RT02_MOTO_TEST\|0\|0\|0\|0\r?$') `
        'The post-target-identity diagnostic did not prove MOTO features OFF.'
    $targetStatePattern =
        '(?m)^TARGET_STATE\|RT02B0-CSDLTTTC-QLHVRT02-20260727-01\|' +
        '[^|]+\|' + $expectedDatasetFingerprint + '\|0\|0\|0\r?$'
    Assert-True ($diagnostic -match $targetStatePattern) `
        'The post-target-identity diagnostic did not prove untouched target state.'
}

function Assert-FinalIntegritySyntaxProofResumeEvidence
{
    Assert-TargetIdentityTypePreflightResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_6'
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The final-integrity-syntax evidence root is absent.'
    }

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            '7DD587E22E52A19BCFCE0B7B5E19B7D0BD0531FC88E6A489E4B33BBEA70ED471' `
        -Label 'Final-integrity-syntax execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The final-integrity-syntax summary did not fail closed.'
    Assert-True `
        ($summary.BodyError -ceq
            'final_integrity_features_on_read_1 failed with sqlcmd exit code 1.') `
        'The final-integrity-syntax body error is not exact.'
    Assert-True (@($summary.CleanupFailures).Count -eq 1) `
        'The final-integrity-syntax cleanup failure count is not exact.'
    $expectedCleanupFailure =
        'FINAL_INTEGRITY_FEATURES_OFF|' +
        'final_integrity_features_off_read_1 failed with sqlcmd exit code 1.'
    Assert-True `
        ($summary.CleanupFailures[0] -ceq $expectedCleanupFailure) `
        'The final-integrity-syntax cleanup failure is not exact.'

    $summaryNames = @(
        @($summary.EvidenceFiles | ForEach-Object { $_.Name }) +
        'execution_summary.json'
    ) | Sort-Object
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True ($actualNames.Count -eq 44) `
        'The final-integrity-syntax evidence file count is not exact.'
    Assert-True `
        ([string]::Join('|', $summaryNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The final-integrity-syntax evidence file set is not exact.'
    foreach ($evidenceFile in $summary.EvidenceFiles)
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $evidenceFile.Name) `
            -ExpectedHash $evidenceFile.Sha256 `
            -Label "Final-integrity-syntax $($evidenceFile.Name)"
    }

    foreach ($productionName in @(
            'production_before.log',
            'production_after_phase_a.log',
            'production_after_phase_b.log',
            'production_after_harness.log',
            'production_final.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $expectedProductionOutputHash `
            -Label "Final-integrity-syntax $productionName"
    }

    foreach ($fixtureVerifyName in @(
            'resume_2_existing_fixture_verify_1.stdout.json',
            'resume_2_existing_fixture_verify_2.stdout.json'
        ))
    {
        $fixtureVerifyPath = Join-Path $previousRoot $fixtureVerifyName
        Assert-FixtureResult `
            -Result (
                Get-Content -Raw -LiteralPath $fixtureVerifyPath |
                    ConvertFrom-Json
            ) `
            -IsVerifyResult $true
    }
    $preflightResult = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'read_only_harness_preflight_result.json'
    ) | ConvertFrom-Json
    Assert-True ($preflightResult.Status -ceq 'VERIFIED_READ_ONLY_PREFLIGHT') `
        'The final run dedicated read-only preflight did not pass.'
    Assert-True `
        ($preflightResult.EnvironmentId -ceq $environmentId -and
         $preflightResult.ApprovalId -ceq $OwnerApprovalId -and
         $preflightResult.DatasetFingerprint -ceq $expectedDatasetFingerprint -and
         $preflightResult.DatabaseIdentityCount -eq 3 -and
         $preflightResult.Snapshot.MarkerCount -eq 0 -and
         $preflightResult.Snapshot.CheckpointCount -eq 0) `
        'The final run dedicated read-only preflight state is invalid.'

    $harnessResult = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'main_harness_result.json'
    ) | ConvertFrom-Json
    Assert-HarnessResult -Result $harnessResult

    $deadlockResult = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'real_sql_deadlock_probe.stdout.json'
    ) | ConvertFrom-Json
    Assert-True `
        ($deadlockResult.Status -ceq
            'REAL_SQL_DEADLOCK_1205_AND_RETRY_VERIFIED') `
        'The final run real deadlock status is invalid.'
    Assert-True `
        ($deadlockResult.DeadlockErrorNumber -eq 1205 -and
         [bool]$deadlockResult.RetrySucceeded -and
         $deadlockResult.BusinessMutationCount -eq 0 -and
         [bool]$deadlockResult.RowEvidencePreserved -and
         $deadlockResult.SessionA -ne $deadlockResult.SessionB) `
        'The final run real deadlock evidence is invalid.'

    foreach ($failedProofName in @(
            'final_integrity_features_on_read_1.log',
            'final_integrity_features_off_read_1.log'
        ))
    {
        $failedProof = Get-Content -Raw -LiteralPath (
            Join-Path $previousRoot $failedProofName
        )
        Assert-True `
            ($failedProof -match
                "Incorrect syntax near the keyword 'RowCount'") `
            "The reviewed syntax error is absent from $failedProofName."
    }
    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True ($timeline -match 'MAIN_HARNESS\|SUCCEEDED') `
        'The final run main harness did not succeed.'
    Assert-True ($timeline -match 'real_sql_deadlock_probe\|SUCCEEDED') `
        'The final run real deadlock probe did not succeed.'
    Assert-True `
        ($timeline -match 'final_integrity_features_on_read_1\|FAILED\|EXIT=1') `
        'The final run integrity syntax failure is absent.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The final run feature cleanup did not prove features OFF.'
    Assert-True `
        ($timeline -match 'production_final\|SUCCEEDED') `
        'The final run cleanup did not prove production state.'
    Assert-True ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The final run cleanup did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Final run host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Final run host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Final run host evidence has a production worker.'

    Assert-FileHash `
        -Path $resume6CorrectedFinalIntegrityOffOutputPath `
        -ExpectedHash `
            'B87303626E3FB69395791ADCF60B0CBB18F1446489D685FA770B3641ED7386B1' `
        -Label 'Corrected final integrity proof with features OFF'
    $correctedProof = Get-Content -Raw -LiteralPath (
        $resume6CorrectedFinalIntegrityOffOutputPath
    )
    Assert-True ($correctedProof -notmatch '(?m)^Msg \d+') `
        'The corrected final integrity proof contains a SQL error.'
    $featuresOffPattern =
        '(?m)^RT02_FINAL_SERVER_AND_FEATURE_STATE\|' +
        'CSDLTTTC\\QLHVRT02\|' + $environmentId + '\|' +
        $OwnerApprovalId + '\|OFF\|0\|0\|0\|0\|0\|0\|0\|0\|0\r?$'
    Assert-True `
        ($correctedProof -match $featuresOffPattern) `
        'The corrected final integrity proof did not prove features OFF.'
    $targetIntegrityPattern =
        '(?m)^RT02_FINAL_TARGET_INTEGRITY\|' +
        '1372\|1369\|3\|160\|1212\|2\|10\|10\|' +
        '[0-9A-F]{64}\|' + $expectedDatasetFingerprint + '\r?$'
    Assert-True `
        ($correctedProof -match $targetIntegrityPattern) `
        'The corrected final integrity proof did not prove target invariants.'
}

function Assert-ExternalProductionTelemetryDriftResumeEvidence
{
    Assert-FinalIntegritySyntaxProofResumeEvidence

    $previousRoot =
        'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\EVIDENCE_RESUME_7'
    if (-not (Test-Path -LiteralPath $previousRoot -PathType Container))
    {
        throw 'The external production telemetry-drift evidence root is absent.'
    }

    $summaryPath = Join-Path $previousRoot 'execution_summary.json'
    Assert-FileHash `
        -Path $summaryPath `
        -ExpectedHash `
            'B9951B9442DCE06350CF93EF26BDC82999A8C49D27DCEB5C64FEBA09CA6C7717' `
        -Label 'External production telemetry-drift execution summary'
    $summary = Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json
    Assert-True ($summary.Status -ceq 'BLOCKED') `
        'The production telemetry-drift summary did not fail closed.'
    Assert-True `
        ($summary.BodyError -ceq 'Production before output hash mismatch.') `
        'The production telemetry-drift body error is not exact.'
    Assert-True (@($summary.CleanupFailures).Count -eq 1) `
        'The production telemetry-drift cleanup failure count is not exact.'
    Assert-True `
        ($summary.CleanupFailures[0] -ceq
            'PRODUCTION_FINAL_PROOF|Production final output hash mismatch.') `
        'The production telemetry-drift cleanup failure is not exact.'

    $summaryNames = @(
        @($summary.EvidenceFiles | ForEach-Object { $_.Name }) +
        'execution_summary.json'
    ) | Sort-Object
    $actualNames = @(
        Get-ChildItem -LiteralPath $previousRoot -File |
            ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True ($actualNames.Count -eq 11) `
        'The production telemetry-drift evidence file count is not exact.'
    Assert-True `
        ([string]::Join('|', $summaryNames) -ceq
            [string]::Join('|', $actualNames)) `
        'The production telemetry-drift evidence file set is not exact.'
    foreach ($evidenceFile in $summary.EvidenceFiles)
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $evidenceFile.Name) `
            -ExpectedHash $evidenceFile.Sha256 `
            -Label "Production telemetry-drift $($evidenceFile.Name)"
    }

    foreach ($productionName in @('production_before.log', 'production_final.log'))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $productionName) `
            -ExpectedHash $externalProductionOutputHash `
            -Label "Production telemetry-drift $productionName"
    }
    foreach ($featuresOffName in @(
            'resume_2_features_off_pre_enable_read_1.log',
            'resume_2_features_off_pre_enable_read_2.log',
            'phase_f_features_off_read_1.log',
            'phase_f_features_off_read_2.log'
        ))
    {
        Assert-FileHash `
            -Path (Join-Path $previousRoot $featuresOffName) `
            -ExpectedHash `
                '954EC20429F514D3F30858B55B904E3613AEC5871C2B8261D282D1D756AA9726' `
            -Label "Production telemetry-drift $featuresOffName"
    }
    $timeline = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'execution_timeline.log'
    )
    Assert-True `
        ($timeline -match
            'RT02_EXECUTION_BODY\|FAILED\|Production before output hash mismatch') `
        'The production telemetry drift is absent from the timeline.'
    Assert-True `
        ($timeline -notmatch
            'phase_a_enable_|final_integrity_features_on|MAIN_HARNESS') `
        'The production telemetry-drift attempt entered a mutation phase.'
    Assert-True `
        ($timeline -match 'phase_f_features_off_read_2\|SUCCEEDED') `
        'The production telemetry-drift attempt did not prove features OFF.'
    Assert-True ($timeline -match 'HOST_FINAL\|SUCCEEDED') `
        'The production telemetry-drift attempt did not prove final host state.'

    $hostFinal = Get-Content -Raw -LiteralPath (
        Join-Path $previousRoot 'host_state_final.json'
    ) | ConvertFrom-Json
    Assert-True (-not [bool]$hostFinal.QlhvAutoSyncEnabled) `
        'Production telemetry-drift host evidence has Auto Sync enabled.'
    Assert-True (-not [bool]$hostFinal.CsdtRealtimeSyncEnabled) `
        'Production telemetry-drift host evidence has realtime sync enabled.'
    Assert-True ($hostFinal.ProductionWorkerProcessCount -eq 0) `
        'Production telemetry-drift host evidence has a production worker.'

    Assert-FileHash `
        -Path $resume7ProductionDriftOutputPath `
        -ExpectedHash `
            '61389CAC903BAE8FC88B5C9EE51248F8EAD944486050E8C4B18956430B8F53B3' `
        -Label 'External production Auto Sync drift diagnostic'
    $diagnostic = Get-Content -Raw -LiteralPath (
        $resume7ProductionDriftOutputPath
    )
    Assert-True ($diagnostic -notmatch '(?m)^Msg \d+') `
        'The external production drift diagnostic contains a SQL error.'
    $appOpenPattern =
        '(?m)^PRODUCTION_AUTO_SYNC_DRIFT\|7\|' +
        'A09D89A5-932F-456B-AF50-7DB225EA17D0\|APP_OPEN\|' +
        'SYSTEM_APP_OPEN\|SUCCEEDED\|COMPLETED\|'
    Assert-True `
        ($diagnostic -match $appOpenPattern) `
        'The external APP_OPEN run evidence is not exact.'
    $manualPattern =
        '(?m)^PRODUCTION_AUTO_SYNC_DRIFT\|8\|' +
        'B1C0CDCC-3027-4744-8F98-4AF1AA96A252\|MANUAL\|' +
        'MANUAL_ADMIN\|SUCCEEDED\|COMPLETED\|'
    Assert-True `
        ($diagnostic -match $manualPattern) `
        'The external MANUAL run evidence is not exact.'
    Assert-True `
        ($diagnostic -match
            '(?m)^PRODUCTION_AUTO_SYNC_DRIFT_COUNTS\|8\|0\|5\r?$') `
        'The external production run aggregate is not exact.'
    $businessCountsPattern =
        '(?m)^PRODUCTION_AUTO_SYNC_DRIFT_BUSINESS_COUNTS\|' +
        '154\|154\|5\|5\|154\|5\|5\|0\r?$'
    Assert-True `
        ($diagnostic -match $businessCountsPattern) `
        'The external production business counts changed.'

    $run7Match = [regex]::Match(
        $diagnostic,
        '(?m)^PRODUCTION_AUTO_SYNC_DRIFT\|7\|[^|]+\|APP_OPEN\|' +
        'SYSTEM_APP_OPEN\|SUCCEEDED\|COMPLETED\|' +
        '([^|]+)\|[^|]+\|([^|]+)\|'
    )
    $run8Match = [regex]::Match(
        $diagnostic,
        '(?m)^PRODUCTION_AUTO_SYNC_DRIFT\|8\|[^|]+\|MANUAL\|' +
        'MANUAL_ADMIN\|SUCCEEDED\|COMPLETED\|' +
        '([^|]+)\|[^|]+\|([^|]+)\|'
    )
    Assert-True ($run7Match.Success -and $run8Match.Success) `
        'The external production run timestamps are absent.'
    $resume6CompletedAtUtc = [DateTime]::Parse(
        (
            Get-Content -Raw -LiteralPath (
                'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727\' +
                'EVIDENCE_RESUME_6\execution_summary.json'
            ) |
            ConvertFrom-Json
        ).CompletedAtUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AdjustToUniversal
    )
    $resume7StartedAtUtc = [DateTime]::Parse(
        (
            (
                Get-Content -Raw -LiteralPath (
                    Join-Path $previousRoot 'RT02_COMPLETE_EXECUTION_STARTED.txt'
                )
            ) -split '\|'
        )[0],
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AdjustToUniversal
    )
    $run7CreatedAtUtc = [DateTime]::SpecifyKind(
        [DateTime]::Parse(
            $run7Match.Groups[1].Value,
            [Globalization.CultureInfo]::InvariantCulture
        ),
        [DateTimeKind]::Utc
    )
    $run8CompletedAtUtc = [DateTime]::SpecifyKind(
        [DateTime]::Parse(
            $run8Match.Groups[2].Value,
            [Globalization.CultureInfo]::InvariantCulture
        ),
        [DateTimeKind]::Utc
    )
    Assert-True `
        ($resume6CompletedAtUtc -lt $run7CreatedAtUtc -and
         $run8CompletedAtUtc -lt $resume7StartedAtUtc) `
        'The external production runs were not bounded between RT02 attempts.'
}

function Get-ClientAliasCount
{
    $aliasPaths = @(
        'HKLM:\SOFTWARE\Microsoft\MSSQLServer\Client\ConnectTo',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\MSSQLServer\Client\ConnectTo',
        'HKCU:\SOFTWARE\Microsoft\MSSQLServer\Client\ConnectTo',
        'HKCU:\SOFTWARE\WOW6432Node\Microsoft\MSSQLServer\Client\ConnectTo'
    )
    $count = 0
    foreach ($aliasPath in $aliasPaths)
    {
        if (-not (Test-Path -LiteralPath $aliasPath))
        {
            continue
        }

        $properties = (Get-ItemProperty -LiteralPath $aliasPath).PSObject.Properties |
            Where-Object { $_.Name -notlike 'PS*' }
        $count += @($properties).Count
    }
    return $count
}

function Get-AndAssert-HostState
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $OutputPath
    )

    $databaseService = Get-Service -Name 'MSSQL$QLHVRT02'
    $agentService = Get-Service -Name 'SQLAgent$QLHVRT02'
    $browserService = Get-Service -Name 'SQLBrowser'
    $serviceDetails = @(
        Get-CimInstance Win32_Service |
            Where-Object {
                $_.Name -in @(
                    'MSSQL$QLHVRT02',
                    'SQLAgent$QLHVRT02',
                    'SQLBrowser'
                )
            }
    )
    $databaseServiceDetail = @(
        $serviceDetails |
            Where-Object { $_.Name -eq 'MSSQL$QLHVRT02' }
    )
    $agentServiceDetail = @(
        $serviceDetails |
            Where-Object { $_.Name -eq 'SQLAgent$QLHVRT02' }
    )
    $browserServiceDetail = @(
        $serviceDetails |
            Where-Object { $_.Name -eq 'SQLBrowser' }
    )
    $protocolRoot =
        'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.QLHVRT02\MSSQLServer\SuperSocketNetLib'
    $sharedMemory = Get-ItemProperty -LiteralPath (
        Join-Path $protocolRoot 'Sm'
    )
    $tcp = Get-ItemProperty -LiteralPath (Join-Path $protocolRoot 'Tcp')
    $namedPipes = Get-ItemProperty -LiteralPath (Join-Path $protocolRoot 'Np')
    $instanceMap = Get-ItemProperty -LiteralPath (
        'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL'
    )
    $aliasCount = Get-ClientAliasCount
    $apiProcesses = @(
        Get-CimInstance Win32_Process -Filter "Name='QLHV.Api.exe'"
    )
    $workerProcesses = @(
        Get-CimInstance Win32_Process -Filter "Name='QLHV.Worker.exe'"
    )
    $runtimeContent = Get-Content -Raw -LiteralPath $runtimeConfigPath
    $runtimeConfig = $runtimeContent | ConvertFrom-Json

    Assert-True `
        -Condition ($databaseService.Status -eq [ServiceProcess.ServiceControllerStatus]::Running) `
        -Message 'The isolated SQL Server service is not running.'
    Assert-True `
        -Condition ($databaseService.StartType -eq [ServiceProcess.ServiceStartMode]::Manual) `
        -Message 'The isolated SQL Server service is not Manual.'
    Assert-True `
        -Condition ($agentService.Status -eq [ServiceProcess.ServiceControllerStatus]::Stopped) `
        -Message 'The isolated SQL Agent service is not stopped.'
    Assert-True `
        -Condition ($agentService.StartType -eq [ServiceProcess.ServiceStartMode]::Disabled) `
        -Message 'The isolated SQL Agent service is not disabled.'
    Assert-True `
        -Condition ($browserService.Status -eq [ServiceProcess.ServiceControllerStatus]::Stopped) `
        -Message 'SQL Browser is not stopped.'
    Assert-True `
        -Condition ($browserService.StartType -eq [ServiceProcess.ServiceStartMode]::Disabled) `
        -Message 'SQL Browser is not disabled.'
    Assert-True `
        -Condition ($databaseServiceDetail.Count -eq 1) `
        -Message 'The isolated database service identity is ambiguous.'
    Assert-True `
        -Condition ($databaseServiceDetail[0].StartName -ceq 'NT Service\MSSQL$QLHVRT02') `
        -Message 'The isolated database service account changed.'
    Assert-True `
        -Condition ($agentServiceDetail.Count -eq 1) `
        -Message 'The isolated SQL Agent service identity is ambiguous.'
    Assert-True `
        -Condition ($browserServiceDetail.Count -eq 1) `
        -Message 'The SQL Browser service identity is ambiguous.'
    Assert-True `
        -Condition ([int]$sharedMemory.Enabled -eq 1) `
        -Message 'Shared Memory is not the enabled isolated SQL protocol.'
    Assert-True `
        -Condition ([int]$tcp.Enabled -eq 0) `
        -Message 'TCP is enabled on the isolated SQL instance.'
    Assert-True `
        -Condition ([int]$namedPipes.Enabled -eq 0) `
        -Message 'Named Pipes is enabled on the isolated SQL instance.'
    Assert-True `
        -Condition ($instanceMap.QLHVRT02 -ceq 'MSSQL16.QLHVRT02') `
        -Message 'The isolated SQL instance registration changed.'
    Assert-True `
        -Condition ($aliasCount -eq 0) `
        -Message 'A SQL client alias is configured.'
    Assert-True `
        -Condition ($apiProcesses.Count -eq 1) `
        -Message 'The production API process count is not exactly one.'
    Assert-True `
        -Condition ($apiProcesses[0].ExecutablePath -ceq $runtimeExecutablePath) `
        -Message 'The production API executable path changed.'
    Assert-True `
        -Condition ($workerProcesses.Count -eq 0) `
        -Message 'A production QLHV.Worker process is running.'
    Assert-True `
        -Condition (-not [bool]$runtimeConfig.QlhvAutoSync.Enabled) `
        -Message 'Production QlhvAutoSync is enabled.'
    Assert-True `
        -Condition (-not [bool]$runtimeConfig.CsdtRealtimeSync.Enabled) `
        -Message 'Production CsdtRealtimeSync is enabled.'
    Assert-True `
        -Condition ($runtimeContent -notmatch 'QLHVRT02|QLHV_RT02_') `
        -Message 'Production runtime configuration references the isolated route.'

    [pscustomobject] ([ordered] @{
        ObservedAtUtc = [DateTime]::UtcNow.ToString('o')
        IsolatedSqlServiceStatus = [string]$databaseService.Status
        IsolatedSqlServiceStartType = [string]$databaseService.StartType
        IsolatedSqlServiceAccount = $databaseServiceDetail[0].StartName
        IsolatedSqlAgentStatus = [string]$agentService.Status
        IsolatedSqlAgentStartType = [string]$agentService.StartType
        SqlBrowserStatus = [string]$browserService.Status
        SqlBrowserStartType = [string]$browserService.StartType
        SharedMemoryEnabled = [int]$sharedMemory.Enabled
        TcpEnabled = [int]$tcp.Enabled
        NamedPipesEnabled = [int]$namedPipes.Enabled
        ClientAliasCount = $aliasCount
        ProductionApiProcessCount = $apiProcesses.Count
        ProductionApiExecutablePath = $apiProcesses[0].ExecutablePath
        ProductionWorkerProcessCount = $workerProcesses.Count
        RuntimeConfigHash = (
            Get-FileHash -Algorithm SHA256 -LiteralPath $runtimeConfigPath
        ).Hash
        QlhvAutoSyncEnabled = [bool]$runtimeConfig.QlhvAutoSync.Enabled
        CsdtRealtimeSyncEnabled = [bool]$runtimeConfig.CsdtRealtimeSync.Enabled
    }) | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $OutputPath -Encoding utf8
}

function Invoke-SqlArtifact
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Server,

        [Parameter(Mandatory = $true)]
        [string] $InputPath,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $outputPath = Join-Path $evidenceRoot ($Label + '.log')
    if (Test-Path -LiteralPath $outputPath)
    {
        throw "An evidence output already exists: $outputPath"
    }

    Write-Timeline "$Label|STARTED"
    & $sqlcmdPath `
        -S $Server `
        -E `
        -b `
        -r 1 `
        -W `
        -s '|' `
        -l 15 `
        -t 120 `
        -i $InputPath `
        -o $outputPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        Write-Timeline "$Label|FAILED|EXIT=$exitCode"
        throw "$Label failed with sqlcmd exit code $exitCode."
    }

    Write-Timeline "$Label|SUCCEEDED"
    return $outputPath
}

function Assert-FileHash
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $ExpectedHash,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $observedHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $Path
    ).Hash
    if ($observedHash -cne $ExpectedHash)
    {
        throw "$Label output hash mismatch."
    }
}

function Assert-SameFileHash
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $FirstPath,

        [Parameter(Mandatory = $true)]
        [string] $SecondPath,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $firstHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $FirstPath
    ).Hash
    $secondHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $SecondPath
    ).Hash
    if ($firstHash -cne $secondHash)
    {
        throw "$Label outputs are not byte-stable."
    }
}

function Invoke-PowerShellJson
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ScriptPath,

        [Parameter(Mandatory = $true)]
        [string[]] $ScriptArguments,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $stdoutPath = Join-Path $evidenceRoot ($Label + '.stdout.json')
    $stderrPath = Join-Path $evidenceRoot ($Label + '.stderr.log')
    if ((Test-Path -LiteralPath $stdoutPath) -or
        (Test-Path -LiteralPath $stderrPath))
    {
        throw "A PowerShell evidence output already exists for $Label."
    }

    $arguments = @(
        '-NoProfile',
        '-NonInteractive',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $ScriptPath,
        '-OwnerApprovalId',
        $OwnerApprovalId
    ) + $ScriptArguments

    Write-Timeline "$Label|STARTED"
    & powershell.exe @arguments 1> $stdoutPath 2> $stderrPath
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        Write-Timeline "$Label|FAILED|EXIT=$exitCode"
        throw "$Label failed with PowerShell exit code $exitCode."
    }

    $jsonText = Get-Content -Raw -LiteralPath $stdoutPath
    try
    {
        $result = $jsonText | ConvertFrom-Json
    }
    catch
    {
        throw "$Label did not produce one valid JSON result."
    }

    Write-Timeline "$Label|SUCCEEDED"
    return $result
}

function Assert-FixtureResult
{
    param(
        [Parameter(Mandatory = $true)]
        [object] $Result,

        [Parameter(Mandatory = $true)]
        [bool] $IsVerifyResult
    )

    Assert-True ($Result.OtoNoChange -eq 150) `
        'Fixture OTO no-change count is invalid.'
    Assert-True ($Result.OtoInsertCandidate -eq 1) `
        'Fixture OTO insert count is invalid.'
    Assert-True ($Result.OtoUpdateCandidate -eq 1) `
        'Fixture OTO update count is invalid.'
    Assert-True ($Result.OtoTargetOnlyActive -eq 1) `
        'Fixture target-only count is invalid.'
    Assert-True ($Result.OtoSoftDeletedBaseline -eq 3) `
        'Fixture soft-deleted count is invalid.'
    Assert-True ($Result.MotoNoChange -eq 5) `
        'Fixture MOTO count is invalid.'
    Assert-True ($Result.DuplicateActiveGroups -eq 0) `
        'Fixture duplicate count is invalid.'
    Assert-True `
        ($Result.DatasetFingerprint -ceq $expectedDatasetFingerprint) `
        'Fixture dataset fingerprint is invalid.'
    Assert-True `
        ($Result.OtoSchemaFingerprint -ceq $expectedOtoSchemaFingerprint) `
        'Fixture OTO schema fingerprint is invalid.'
    Assert-True `
        ($Result.MotoSchemaFingerprint -ceq $expectedMotoSchemaFingerprint) `
        'Fixture MOTO schema fingerprint is invalid.'
    Assert-True `
        ($Result.TargetSchemaFingerprint -ceq $expectedTargetSchemaFingerprint) `
        'Fixture target schema fingerprint is invalid.'

    if ($IsVerifyResult)
    {
        Assert-True ($Result.StableReadCount -eq 2) `
            'Fixture stable read count is invalid.'
        Assert-True `
            ($Result.SourceRowsFingerprint -ceq $expectedSourceRowsFingerprint) `
            'Fixture source row fingerprint is invalid.'
        Assert-True `
            ($Result.TargetRowsFingerprint -ceq $expectedTargetRowsFingerprint) `
            'Fixture target row fingerprint is invalid.'
    }
}

function Stop-BoundedProcessTree
{
    param(
        [Parameter(Mandatory = $true)]
        [Diagnostics.Process] $TargetProcess,

        [Parameter(Mandatory = $true)]
        [string] $Label
    )

    $processSnapshot = @(Get-CimInstance Win32_Process)
    $treeIds = New-Object 'System.Collections.Generic.List[int]'
    $treeIds.Add([int]$TargetProcess.Id)
    $addedDescendant = $true
    while ($addedDescendant)
    {
        $addedDescendant = $false
        foreach ($candidate in $processSnapshot)
        {
            $candidateId = [int]$candidate.ProcessId
            $parentId = [int]$candidate.ParentProcessId
            if ($treeIds.Contains($parentId) -and
                -not $treeIds.Contains($candidateId))
            {
                $treeIds.Add($candidateId)
                $addedDescendant = $true
            }
        }
    }

    $taskkillPath = Join-Path $env:SystemRoot 'System32\taskkill.exe'
    $taskkillStdout = Join-Path $evidenceRoot `
        ($Label + '_timeout_taskkill.stdout.log')
    $taskkillStderr = Join-Path $evidenceRoot `
        ($Label + '_timeout_taskkill.stderr.log')
    & $taskkillPath `
        /PID $TargetProcess.Id `
        /T `
        /F `
        1> $taskkillStdout `
        2> $taskkillStderr
    $taskkillExitCode = $LASTEXITCODE
    [void]$TargetProcess.WaitForExit(10000)
    $survivors = @(
        $treeIds |
            Where-Object {
                $null -ne (
                    Get-Process -Id $_ -ErrorAction SilentlyContinue
                )
            }
    )
    if ($taskkillExitCode -ne 0 -or $survivors.Count -ne 0)
    {
        throw "The $Label process tree could not be proven terminated."
    }
}

function Invoke-ReadOnlyHarnessPreflight
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResultsPath
    )

    $stdoutPath = Join-Path $evidenceRoot `
        'read_only_harness_preflight.stdout.log'
    $stderrPath = Join-Path $evidenceRoot `
        'read_only_harness_preflight.stderr.log'
    $trxName = 'rt02_read_only_harness_preflight.trx'
    $filter =
        'FullyQualifiedName=QLHV.Tests.Sync.Rt02.Rt02b2AuthorizedSqlExecutionTests.Authorized_isolated_SQL_read_only_preflight_passes_all_gates'
    $arguments = @(
        'test',
        $testProjectPath,
        '-c',
        'Release',
        '--no-build',
        '--no-restore',
        '--filter',
        $filter,
        '--logger',
        "trx;LogFileName=$trxName",
        '--results-directory',
        $evidenceRoot
    )

    Write-Timeline 'READ_ONLY_HARNESS_PREFLIGHT|STARTED'
    $process = Start-Process `
        -FilePath 'dotnet.exe' `
        -ArgumentList $arguments `
        -WorkingDirectory $workspaceRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru
    if (-not $process.WaitForExit(300000))
    {
        try
        {
            Stop-BoundedProcessTree `
                -TargetProcess $process `
                -Label 'read_only_harness_preflight'
        }
        catch
        {
            throw (
                'The read-only harness preflight timed out and full process-' +
                "tree termination failed: $($_.Exception.Message)"
            )
        }
        throw 'The read-only harness preflight exceeded five minutes.'
    }
    $process.WaitForExit()

    $resolvedExitCode = $null
    try
    {
        $process.Refresh()
        $resolvedExitCode = $process.ExitCode
    }
    catch
    {
        # Windows PowerShell can lose ExitCode after redirected Start-Process.
    }
    if ($null -eq $resolvedExitCode)
    {
        if (Test-Path -LiteralPath $ResultsPath -PathType Leaf)
        {
            $resolvedResult = Get-Content -Raw -LiteralPath $ResultsPath |
                ConvertFrom-Json
            $resolvedExitCode =
                if ($resolvedResult.Status -ceq
                    'VERIFIED_READ_ONLY_PREFLIGHT') { 0 } else { 1 }
        }
        else
        {
            $resolvedExitCode = 1
        }
    }
    if ($resolvedExitCode -ne 0)
    {
        Write-Timeline `
            "READ_ONLY_HARNESS_PREFLIGHT|FAILED|EXIT=$resolvedExitCode"
        throw (
            'The read-only harness preflight failed with exit code ' +
            "$resolvedExitCode."
        )
    }

    Assert-True `
        (Test-Path -LiteralPath $ResultsPath -PathType Leaf) `
        'The read-only harness preflight did not write its result JSON.'
    Assert-True `
        (Test-Path -LiteralPath (Join-Path $evidenceRoot $trxName) -PathType Leaf) `
        'The read-only harness preflight did not write its TRX result.'
    Write-Timeline 'READ_ONLY_HARNESS_PREFLIGHT|SUCCEEDED'
}

function Invoke-MainHarness
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResultsPath
    )

    $stdoutPath = Join-Path $evidenceRoot 'main_harness.stdout.log'
    $stderrPath = Join-Path $evidenceRoot 'main_harness.stderr.log'
    $trxName = 'rt02_complete_main_harness.trx'
    $filter =
        'FullyQualifiedName=QLHV.Tests.Sync.Rt02.Rt02b2AuthorizedSqlExecutionTests.Authorized_isolated_SQL_apply_harness_passes_all_gates'
    $arguments = @(
        'test',
        $testProjectPath,
        '-c',
        'Release',
        '--no-build',
        '--no-restore',
        '--filter',
        $filter,
        '--logger',
        "trx;LogFileName=$trxName",
        '--results-directory',
        $evidenceRoot
    )

    Write-Timeline 'MAIN_HARNESS|STARTED'
    $process = Start-Process `
        -FilePath 'dotnet.exe' `
        -ArgumentList $arguments `
        -WorkingDirectory $workspaceRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru
    if (-not $process.WaitForExit(1200000))
    {
        $processSnapshot = @(Get-CimInstance Win32_Process)
        $treeIds = New-Object 'System.Collections.Generic.List[int]'
        $treeIds.Add([int]$process.Id)
        $addedDescendant = $true
        while ($addedDescendant)
        {
            $addedDescendant = $false
            foreach ($candidate in $processSnapshot)
            {
                $candidateId = [int]$candidate.ProcessId
                $parentId = [int]$candidate.ParentProcessId
                if ($treeIds.Contains($parentId) -and
                    -not $treeIds.Contains($candidateId))
                {
                    $treeIds.Add($candidateId)
                    $addedDescendant = $true
                }
            }
        }

        $taskkillPath = Join-Path $env:SystemRoot 'System32\taskkill.exe'
        $taskkillStdout = Join-Path $evidenceRoot `
            'main_harness_timeout_taskkill.stdout.log'
        $taskkillStderr = Join-Path $evidenceRoot `
            'main_harness_timeout_taskkill.stderr.log'
        try
        {
            & $taskkillPath `
                /PID $process.Id `
                /T `
                /F `
                1> $taskkillStdout `
                2> $taskkillStderr
            $taskkillExitCode = $LASTEXITCODE
            [void]$process.WaitForExit(10000)
            $survivors = @(
                $treeIds |
                    Where-Object {
                        $null -ne (
                            Get-Process -Id $_ -ErrorAction SilentlyContinue
                        )
                    }
            )
            if ($taskkillExitCode -ne 0 -or $survivors.Count -ne 0)
            {
                throw (
                    'The timed-out harness process tree could not be ' +
                    'proven terminated.'
                )
            }
        }
        catch
        {
            throw (
                'The exact isolated SQL harness timed out and full process-' +
                "tree termination failed: $($_.Exception.Message)"
            )
        }
        throw 'The exact isolated SQL harness exceeded 20 minutes.'
    }
    $process.WaitForExit()
    $resolvedExitCode = $null
    try
    {
        $process.Refresh()
        $resolvedExitCode = $process.ExitCode
    }
    catch
    {
        # Windows PowerShell can lose ExitCode after redirected Start-Process.
    }
    if ($null -eq $resolvedExitCode)
    {
        if (Test-Path -LiteralPath $ResultsPath -PathType Leaf)
        {
            $resolvedResult = Get-Content -Raw -LiteralPath $ResultsPath |
                ConvertFrom-Json
            $resolvedExitCode =
                if ($resolvedResult.Status -ceq 'VERIFIED') { 0 } else { 1 }
        }
        else
        {
            $resolvedExitCode = 1
        }
    }
    if ($resolvedExitCode -ne 0)
    {
        Write-Timeline "MAIN_HARNESS|FAILED|EXIT=$resolvedExitCode"
        throw "The exact isolated SQL harness failed with exit code $resolvedExitCode."
    }

    Assert-True `
        (Test-Path -LiteralPath $ResultsPath -PathType Leaf) `
        'The exact isolated SQL harness did not write its result JSON.'
    Assert-True `
        (Test-Path -LiteralPath (Join-Path $evidenceRoot $trxName) -PathType Leaf) `
        'The exact isolated SQL harness did not write its TRX result.'
    Write-Timeline 'MAIN_HARNESS|SUCCEEDED'
}

function Assert-HarnessResult
{
    param(
        [Parameter(Mandatory = $true)]
        [object] $Result
    )

    Assert-True ($Result.Status -ceq 'VERIFIED') `
        'Harness status is not VERIFIED.'
    Assert-True ($Result.EnvironmentId -ceq $environmentId) `
        'Harness environment identity is invalid.'
    Assert-True ($Result.ApprovalId -ceq $OwnerApprovalId) `
        'Harness approval identity is invalid.'
    Assert-True ($Result.ServerIdentity -ceq 'CSDLTTTC\QLHVRT02') `
        'Harness server identity is invalid.'
    Assert-True `
        ($Result.DatasetFingerprint -ceq $expectedDatasetFingerprint) `
        'Harness dataset fingerprint is invalid.'
    Assert-True `
        ($Result.MappingFingerprint -ceq $expectedMappingFingerprint) `
        'Harness mapping fingerprint is invalid.'
    Assert-True `
        ($Result.SourceSchemaFingerprint -ceq $expectedSourceSchemaFingerprint) `
        'Harness source schema fingerprint is invalid.'
    Assert-True `
        ($Result.TargetSchemaFingerprint -ceq $expectedTargetSchemaFingerprint) `
        'Harness target schema fingerprint is invalid.'
    Assert-True `
        ($Result.CoreQlhvOwnedHashBefore -ceq $Result.CoreQlhvOwnedHashAfter) `
        'Harness changed QLHV-owned core state.'
    Assert-True (@($Result.DatabaseIdentities).Count -eq 3) `
        'Harness database identity count is invalid.'
    Assert-True (@($Result.Scenarios).Count -eq 22) `
        'Harness scenario count is invalid.'

    Assert-True ($Result.Before.OtoNoChange -eq 150) `
        'Harness initial OTO no-change count is invalid.'
    Assert-True ($Result.Before.OtoInsertCandidates -eq 1) `
        'Harness initial insert candidate count is invalid.'
    Assert-True ($Result.Before.OtoUpdateCandidates -eq 1) `
        'Harness initial update candidate count is invalid.'
    Assert-True ($Result.Before.OtoTargetOnlyActive -eq 1) `
        'Harness initial target-only count is invalid.'
    Assert-True ($Result.Before.OtoSoftDeletedBaseline -eq 3) `
        'Harness initial soft-delete count is invalid.'
    Assert-True ($Result.Before.MotoNoChange -eq 5) `
        'Harness initial MOTO count is invalid.'
    Assert-True ($Result.Before.DuplicateActiveGroups -eq 0) `
        'Harness initial duplicate count is invalid.'
    Assert-True ($Result.Before.PiiLikeRows -eq 0) `
        'Harness initial PII-like count is invalid.'
    Assert-True ($Result.Before.MarkerCount -eq 0) `
        'Harness initial marker count is invalid.'
    Assert-True ($Result.Before.CheckpointCount -eq 0) `
        'Harness initial checkpoint count is invalid.'
    Assert-True ($Result.After.OtoSoftDeletedBaseline -eq 3) `
        'Harness final soft-delete count is invalid.'
    Assert-True ($Result.After.DuplicateActiveGroups -eq 0) `
        'Harness final duplicate count is invalid.'
    Assert-True ($Result.After.NonCoreInactiveOrDeletedRows -eq 0) `
        'Harness final noncore state is invalid.'
    Assert-True ($Result.After.PiiLikeRows -eq 0) `
        'Harness final PII-like count is invalid.'
    Assert-True ($Result.After.MarkerCount -eq 10) `
        'Harness final marker count is invalid.'
    Assert-True ($Result.After.CheckpointCount -eq 10) `
        'Harness final checkpoint count is invalid.'
    Assert-True ($Result.After.ChangeTrackingRows -gt 0) `
        'Harness did not observe Change Tracking rows.'

    $expectedScenarioNames = @(
        'minimal_insert_update_retained',
        'moto_five_no_change',
        'update_failure_rolls_back_insert',
        'final_verification_failure_rolls_back_transaction',
        'second_session_target_creation_blocks_apply_transaction',
        'second_session_target_change_blocks_stale_apply',
        'source_changed_since_shadow_blocks_apply',
        'cancellation_before_transaction_no_mutation',
        'checkpoint_conflict_before_transaction',
        'controlled_process_termination_inside_transaction_rolls_back',
        'controlled_process_termination_after_commit_recovers_checkpoint',
        'duplicate_event_replay_idempotent',
        'target_timeout_explicit_retry',
        'deadlock_explicit_retry',
        'mapping_fingerprint_drift_fail_closed',
        'source_schema_fingerprint_drift_fail_closed',
        'target_schema_fingerprint_drift_fail_closed',
        'incomplete_immutable_plan_fails_before_transaction',
        'injected_committed_marker_plan_hash_tamper_fails_closed',
        'load_100_inserts',
        'load_100_updates',
        'load_mixed_1000_operations'
    ) | Sort-Object
    $actualScenarioNames = @(
        $Result.Scenarios | ForEach-Object { $_.Name }
    ) | Sort-Object
    Assert-True `
        ([string]::Join('|', $expectedScenarioNames) -ceq
            [string]::Join('|', $actualScenarioNames)) `
        'Harness scenario set is invalid.'

    foreach ($scenario in $Result.Scenarios)
    {
        Assert-True ($scenario.DuplicateActiveCount -eq 0) `
            "Scenario $($scenario.Name) observed a duplicate active row."
        Assert-True ($scenario.MarkerCount -eq $scenario.CheckpointCount) `
            "Scenario $($scenario.Name) observed marker/checkpoint divergence."
    }

    $core = @(
        $Result.Scenarios |
            Where-Object { $_.Name -eq 'minimal_insert_update_retained' }
    )[0]
    $loadInsert = @(
        $Result.Scenarios |
            Where-Object { $_.Name -eq 'load_100_inserts' }
    )[0]
    $loadUpdate = @(
        $Result.Scenarios |
            Where-Object { $_.Name -eq 'load_100_updates' }
    )[0]
    $loadMixed = @(
        $Result.Scenarios |
            Where-Object { $_.Name -eq 'load_mixed_1000_operations' }
    )[0]
    Assert-True `
        ($core.InsertedRows -eq 1 -and
         $core.UpdatedRows -eq 1 -and
         $core.RetainedRows -eq 1) `
        'The minimal INSERT/HoTen-update/target-only result is invalid.'
    Assert-True ($loadInsert.InsertedRows -eq 100) `
        'The 100-row insert load result is invalid.'
    Assert-True ($loadUpdate.UpdatedRows -eq 100) `
        'The 100-row update load result is invalid.'
    Assert-True `
        ($loadMixed.InsertedRows -eq 500 -and
         $loadMixed.UpdatedRows -eq 499 -and
         $loadMixed.RetainedRows -eq 1) `
        'The mixed 1000-operation load result is invalid.'
    Assert-True `
        ($loadMixed.MarkerCount -eq 10 -and
         $loadMixed.CheckpointCount -eq 10 -and
         $loadMixed.ManualReviewCount -eq 2) `
        'The final scenario evidence counts are invalid.'
}

function Add-CleanupFailure
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[string]] $Failures,

        [Parameter(Mandatory = $true)]
        [string] $Label,

        [Parameter(Mandatory = $true)]
        [Management.Automation.ErrorRecord] $ErrorRecord
    )

    $message = "$Label|$($ErrorRecord.Exception.Message)"
    $Failures.Add($message)
    Write-Timeline "$Label|FAILED|$($ErrorRecord.Exception.Message)"
}

function Write-ExecutionSummary
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Status,

        [AllowNull()]
        [Management.Automation.ErrorRecord] $BodyError,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.Generic.List[string]] $CleanupFailures
    )

    $files = @(
        Get-ChildItem -LiteralPath $evidenceRoot -File |
            Where-Object { $_.FullName -cne $summaryPath } |
            Sort-Object Name |
            ForEach-Object {
                [pscustomobject] ([ordered] @{
                    Name = $_.Name
                    Length = $_.Length
                    Sha256 = (
                        Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
                    ).Hash
                })
            }
    )
    [pscustomobject] ([ordered] @{
        Status = $Status
        EnvironmentId = $environmentId
        ApprovalId = $OwnerApprovalId
        RepositoryHead = $repositoryHead
        CompletedAtUtc = [DateTime]::UtcNow.ToString('o')
        BodyError = if ($null -eq $BodyError) {
            $null
        } else {
            $BodyError.Exception.Message
        }
        CleanupFailures = @($CleanupFailures)
        EvidenceFiles = $files
    }) | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $summaryPath -Encoding utf8
}

if ($OwnerApprovalId -cne 'RT02B-OPERATOR-APPROVAL-20260727-01')
{
    throw 'The exact RT02 operator approval is required.'
}
if ([DateTime]::UtcNow -ge $approvalExpiresAtUtc)
{
    throw 'The exact RT02 operator approval has expired.'
}
if ((Resolve-Path -LiteralPath $workspaceRoot).Path -cne $workspaceRoot)
{
    throw 'The RT02 workspace identity changed.'
}
if (-not (Test-Path -LiteralPath $sqlcmdPath -PathType Leaf))
{
    throw 'The pinned SQLCMD executable is absent.'
}
if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container))
{
    throw 'The corrected RT02 artifact root is absent.'
}
if ($ResumeAfterExternalProductionTelemetryDrift.IsPresent)
{
    Assert-ExternalProductionTelemetryDriftResumeEvidence
}
elseif ($ResumeAfterFinalIntegritySyntaxProof.IsPresent)
{
    Assert-FinalIntegritySyntaxProofResumeEvidence
}
elseif ($ResumeAfterTargetIdentityTypePreflight.IsPresent)
{
    Assert-TargetIdentityTypePreflightResumeEvidence
}
elseif ($ResumeAfterTransientFixtureProofTimeout.IsPresent)
{
    Assert-TransientFixtureProofTimeoutResumeEvidence
}
elseif ($ResumeAfterApprovalWindowPreflight.IsPresent)
{
    Assert-ApprovalWindowPreflightResumeEvidence
}
elseif ($ResumeAfterHarnessIdentityTypePreflight.IsPresent)
{
    Assert-HarnessIdentityTypePreflightResumeEvidence
}
elseif ($ResumeAfterHarnessIdentityPreflight.IsPresent)
{
    Assert-HarnessIdentityPreflightResumeEvidence
}
elseif ($ResumeAfterReadOnlyPreflight.IsPresent)
{
    Assert-ReadOnlyPreflightResumeEvidence
}
elseif (Test-Path -LiteralPath $attemptMarkerPath)
{
    throw 'The complete RT02 execution has already been attempted.'
}

Assert-ArtifactSet
Assert-ReadOnlyProof -Path $featuresOnProofPath
Assert-ReadOnlyProof -Path $featuresOffProofPath
Assert-ReadOnlyProof -Path $finalIntegrityProofPath
Assert-ReadOnlyProof -Path $lockDiagnosticProofPath
Assert-ReadOnlyProof -Path $productionDriftProofPath

$observedHead = (& git -C $workspaceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $observedHead -cne $repositoryHead)
{
    throw 'The repository HEAD identity changed.'
}
$stagedPaths = @(& git -C $workspaceRoot diff --cached --name-only)
if ($LASTEXITCODE -ne 0 -or $stagedPaths.Count -ne 0)
{
    throw 'The repository contains staged changes.'
}

Clear-LiveOptIns

if ($ValidateOnly.IsPresent)
{
    $temporaryHostProof = Join-Path (
        [IO.Path]::GetTempPath()
    ) ('rt02_validate_host_' + [Guid]::NewGuid().ToString('N') + '.json')
    try
    {
        Get-AndAssert-HostState -OutputPath $temporaryHostProof
    }
    finally
    {
        if (Test-Path -LiteralPath $temporaryHostProof)
        {
            Remove-Item -LiteralPath $temporaryHostProof -Force
        }
    }

    [pscustomobject] ([ordered] @{
        Status = 'VALIDATED_NO_SQL_EXECUTED'
        EnvironmentId = $environmentId
        ApprovalId = $OwnerApprovalId
        ArtifactCount = $expectedArtifacts.Count
        RepositoryHead = $repositoryHead
    }) | ConvertTo-Json -Compress
    return
}

$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent()
)
if (-not $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator))
{
    throw 'The complete RT02 execution requires an elevated local process.'
}

[void](New-Item -ItemType Directory -Path $evidenceRoot -Force)
if (Test-Path -LiteralPath $attemptMarkerPath)
{
    throw 'The complete RT02 execution marker appeared concurrently.'
}
Set-Content -LiteralPath $attemptMarkerPath -Value (
    [DateTime]::UtcNow.ToString('o') +
    '|RT02_COMPLETE_EXECUTION|STARTED|ONE_ATTEMPT|NO_SCHEMA_DDL'
) -Encoding utf8
Set-Content -LiteralPath $timelinePath -Value (
    [DateTime]::UtcNow.ToString('o') +
    '|RT02_COMPLETE_EXECUTION|STARTED'
) -Encoding utf8

$cleanupRequired = $false
$finalStateExpected = $false
$bodyError = $null
$bodySucceeded = $false
$cleanupFailures = New-Object 'System.Collections.Generic.List[string]'

try
{
    Write-Timeline 'HOST_PREFLIGHT|STARTED'
    Get-AndAssert-HostState -OutputPath (
        Join-Path $evidenceRoot 'host_state_before.json'
    )
    Write-Timeline 'HOST_PREFLIGHT|SUCCEEDED'

    if ($postFixtureResume)
    {
        $preEnableOff1 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $featuresOffProofPath `
            -Label 'resume_2_features_off_pre_enable_read_1'
        $preEnableOff2 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $featuresOffProofPath `
            -Label 'resume_2_features_off_pre_enable_read_2'
        Assert-SameFileHash `
            -FirstPath $preEnableOff1 `
            -SecondPath $preEnableOff2 `
            -Label 'Resume 2 pre-enable features-off proof'
    }
    else
    {
        $schemaRead1 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $schemaProofPath `
            -Label 'schema_gate_read_1'
        Assert-FileHash `
            -Path $schemaRead1 `
            -ExpectedHash $expectedSchemaOutputHash `
            -Label 'Schema gate read 1'
        $schemaRead2 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $schemaProofPath `
            -Label 'schema_gate_read_2'
        Assert-FileHash `
            -Path $schemaRead2 `
            -ExpectedHash $expectedSchemaOutputHash `
            -Label 'Schema gate read 2'
    }

    $productionBefore = Invoke-SqlArtifact `
        -Server $productionServer `
        -InputPath $productionProofPath `
        -Label 'production_before'
    Assert-FileHash `
        -Path $productionBefore `
        -ExpectedHash $currentProductionOutputHash `
        -Label 'Production before'

    $cleanupRequired = $true
    [void](Invoke-SqlArtifact `
        -Server $isolatedServer `
        -InputPath $enableOtoPath `
        -Label 'phase_a_enable_oto')
    [void](Invoke-SqlArtifact `
        -Server $isolatedServer `
        -InputPath $enableMotoPath `
        -Label 'phase_a_enable_moto')
    $featuresOn1 = Invoke-SqlArtifact `
        -Server $isolatedServer `
        -InputPath $featuresOnProofPath `
        -Label 'phase_a_features_on_read_1'
    $featuresOn2 = Invoke-SqlArtifact `
        -Server $isolatedServer `
        -InputPath $featuresOnProofPath `
        -Label 'phase_a_features_on_read_2'
    Assert-SameFileHash `
        -FirstPath $featuresOn1 `
        -SecondPath $featuresOn2 `
        -Label 'Phase A feature proof'

    $productionAfterA = Invoke-SqlArtifact `
        -Server $productionServer `
        -InputPath $productionProofPath `
        -Label 'production_after_phase_a'
    Assert-FileHash `
        -Path $productionAfterA `
        -ExpectedHash $currentProductionOutputHash `
        -Label 'Production after Phase A'

    if ($finalIntegrityOnlyResume)
    {
        $finalStateExpected = $true
        $finalOn1 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $finalIntegrityProofPath `
            -Label 'final_integrity_features_on_read_1'
        $finalOn2 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $finalIntegrityProofPath `
            -Label 'final_integrity_features_on_read_2'
        Assert-SameFileHash `
            -FirstPath $finalOn1 `
            -SecondPath $finalOn2 `
            -Label 'Recovered final integrity proof with features ON'

        $productionAfterFinalOn = Invoke-SqlArtifact `
            -Server $productionServer `
            -InputPath $productionProofPath `
            -Label 'production_after_final_integrity_on'
        Assert-FileHash `
            -Path $productionAfterFinalOn `
            -ExpectedHash $currentProductionOutputHash `
            -Label 'Production after recovered final integrity ON proof'

        $bodySucceeded = $true
        Write-Timeline 'RT02_EXECUTION_BODY|SUCCEEDED'
    }
    else
    {
        if ($postFixtureResume)
        {
            $fixtureVerify1 = Invoke-PowerShellJson `
                -ScriptPath $fixtureLoaderPath `
                -ScriptArguments @('-VerifyOnly') `
                -Label 'resume_2_existing_fixture_verify_1'
            Assert-FixtureResult -Result $fixtureVerify1 -IsVerifyResult $true
            $fixtureVerify2 = Invoke-PowerShellJson `
                -ScriptPath $fixtureLoaderPath `
                -ScriptArguments @('-VerifyOnly') `
                -Label 'resume_2_existing_fixture_verify_2'
            Assert-FixtureResult -Result $fixtureVerify2 -IsVerifyResult $true
            Assert-SameFileHash `
                -FirstPath (Join-Path $evidenceRoot `
                    'resume_2_existing_fixture_verify_1.stdout.json') `
                -SecondPath (Join-Path $evidenceRoot `
                    'resume_2_existing_fixture_verify_2.stdout.json') `
                -Label 'Resume 2 existing fixture verification'
        }
        else
        {
            $fixtureExecute = Invoke-PowerShellJson `
                -ScriptPath $fixtureLoaderPath `
                -ScriptArguments @('-Execute') `
                -Label 'phase_b_fixture_execute'
            Assert-FixtureResult -Result $fixtureExecute -IsVerifyResult $false
            $fixtureVerify1 = Invoke-PowerShellJson `
                -ScriptPath $fixtureLoaderPath `
                -ScriptArguments @('-VerifyOnly') `
                -Label 'phase_b_fixture_verify_1'
            Assert-FixtureResult -Result $fixtureVerify1 -IsVerifyResult $true
            $fixtureVerify2 = Invoke-PowerShellJson `
                -ScriptPath $fixtureLoaderPath `
                -ScriptArguments @('-VerifyOnly') `
                -Label 'phase_b_fixture_verify_2'
            Assert-FixtureResult -Result $fixtureVerify2 -IsVerifyResult $true
            Assert-SameFileHash `
                -FirstPath (Join-Path $evidenceRoot `
                    'phase_b_fixture_verify_1.stdout.json') `
                -SecondPath (Join-Path $evidenceRoot `
                    'phase_b_fixture_verify_2.stdout.json') `
                -Label 'Phase B independent fixture verification'
        }

    $productionAfterB = Invoke-SqlArtifact `
        -Server $productionServer `
        -InputPath $productionProofPath `
        -Label 'production_after_phase_b'
    Assert-FileHash `
        -Path $productionAfterB `
        -ExpectedHash $currentProductionOutputHash `
        -Label 'Production after Phase B'

    $readOnlyPreflightResultsPath = Join-Path $evidenceRoot `
        'read_only_harness_preflight_result.json'
    Clear-LiveOptIns
    [Environment]::SetEnvironmentVariable(
        'QLHV_RT02B2_READ_ONLY_PREFLIGHT_APPROVAL_ID',
        $OwnerApprovalId,
        [EnvironmentVariableTarget]::Process
    )
    [Environment]::SetEnvironmentVariable(
        'QLHV_RT02B2_READ_ONLY_PREFLIGHT_RESULTS_PATH',
        $readOnlyPreflightResultsPath,
        [EnvironmentVariableTarget]::Process
    )
    try
    {
        Invoke-ReadOnlyHarnessPreflight `
            -ResultsPath $readOnlyPreflightResultsPath
    }
    finally
    {
        Clear-LiveOptIns
    }
    $readOnlyPreflightResult = Get-Content -Raw `
        -LiteralPath $readOnlyPreflightResultsPath |
        ConvertFrom-Json
    Assert-True `
        ($readOnlyPreflightResult.Status -ceq
            'VERIFIED_READ_ONLY_PREFLIGHT') `
        'The read-only harness preflight status is invalid.'
    Assert-True `
        ($readOnlyPreflightResult.EnvironmentId -ceq $environmentId) `
        'The read-only harness preflight environment is invalid.'
    Assert-True `
        ($readOnlyPreflightResult.ApprovalId -ceq $OwnerApprovalId) `
        'The read-only harness preflight approval is invalid.'
    Assert-True `
        ($readOnlyPreflightResult.DatasetFingerprint -ceq
            $expectedDatasetFingerprint) `
        'The read-only harness preflight dataset is invalid.'
    Assert-True `
        ($readOnlyPreflightResult.DatabaseIdentityCount -eq 3) `
        'The read-only harness preflight identity count is invalid.'
    Assert-True `
        ($readOnlyPreflightResult.Snapshot.MarkerCount -eq 0 -and
         $readOnlyPreflightResult.Snapshot.CheckpointCount -eq 0 -and
         $readOnlyPreflightResult.Snapshot.DuplicateActiveGroups -eq 0) `
        'The read-only harness preflight integrity snapshot is invalid.'

    $harnessResultsPath = Join-Path $evidenceRoot 'main_harness_result.json'
    Clear-LiveOptIns
    [Environment]::SetEnvironmentVariable(
        'QLHV_RT02B2_APPROVAL_ID',
        $OwnerApprovalId,
        [EnvironmentVariableTarget]::Process
    )
    [Environment]::SetEnvironmentVariable(
        'QLHV_RT02B2_RESULTS_PATH',
        $harnessResultsPath,
        [EnvironmentVariableTarget]::Process
    )
    try
    {
        Invoke-MainHarness -ResultsPath $harnessResultsPath
    }
    finally
    {
        Clear-LiveOptIns
    }

    $harnessResult = Get-Content -Raw -LiteralPath $harnessResultsPath |
        ConvertFrom-Json
    Assert-HarnessResult -Result $harnessResult
    $finalStateExpected = $true

    $productionAfterHarness = Invoke-SqlArtifact `
        -Server $productionServer `
        -InputPath $productionProofPath `
        -Label 'production_after_harness'
    Assert-FileHash `
        -Path $productionAfterHarness `
        -ExpectedHash $currentProductionOutputHash `
        -Label 'Production after harness'

    $deadlockResult = Invoke-PowerShellJson `
        -ScriptPath $deadlockProbePath `
        -ScriptArguments @('-Execute') `
        -Label 'real_sql_deadlock_probe'
    Assert-True `
        ($deadlockResult.Status -ceq
            'REAL_SQL_DEADLOCK_1205_AND_RETRY_VERIFIED') `
        'The real SQL deadlock probe status is invalid.'
    Assert-True ($deadlockResult.DeadlockErrorNumber -eq 1205) `
        'The real SQL deadlock victim number is invalid.'
    Assert-True ([bool]$deadlockResult.RetrySucceeded) `
        'The real SQL deadlock retry did not succeed.'
    Assert-True ($deadlockResult.BusinessMutationCount -eq 0) `
        'The real SQL deadlock probe reported a business mutation.'
    Assert-True ([bool]$deadlockResult.RowEvidencePreserved) `
        'The real SQL deadlock probe did not preserve row evidence.'
    Assert-True ($deadlockResult.SessionA -ne $deadlockResult.SessionB) `
        'The real SQL deadlock probe did not use distinct sessions.'

    $finalOn1 = Invoke-SqlArtifact `
        -Server $isolatedServer `
        -InputPath $finalIntegrityProofPath `
        -Label 'final_integrity_features_on_read_1'
    $finalOn2 = Invoke-SqlArtifact `
        -Server $isolatedServer `
        -InputPath $finalIntegrityProofPath `
        -Label 'final_integrity_features_on_read_2'
    Assert-SameFileHash `
        -FirstPath $finalOn1 `
        -SecondPath $finalOn2 `
        -Label 'Final integrity proof with features ON'

    $bodySucceeded = $true
    Write-Timeline 'RT02_EXECUTION_BODY|SUCCEEDED'
}
}
catch
{
    $bodyError = $_
    Write-Timeline "RT02_EXECUTION_BODY|FAILED|$($_.Exception.Message)"
}
finally
{
    Clear-LiveOptIns

    if ($cleanupRequired)
    {
        try
        {
            [void](Invoke-SqlArtifact `
                -Server $isolatedServer `
                -InputPath $disableMotoPath `
                -Label 'phase_f_disable_moto')
        }
        catch
        {
            Add-CleanupFailure `
                -Failures $cleanupFailures `
                -Label 'PHASE_F_DISABLE_MOTO' `
                -ErrorRecord $_
        }

        try
        {
            [void](Invoke-SqlArtifact `
                -Server $isolatedServer `
                -InputPath $disableOtoPath `
                -Label 'phase_f_disable_oto')
        }
        catch
        {
            Add-CleanupFailure `
                -Failures $cleanupFailures `
                -Label 'PHASE_F_DISABLE_OTO' `
                -ErrorRecord $_
        }
    }

    try
    {
        $featuresOff1 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $featuresOffProofPath `
            -Label 'phase_f_features_off_read_1'
        $featuresOff2 = Invoke-SqlArtifact `
            -Server $isolatedServer `
            -InputPath $featuresOffProofPath `
            -Label 'phase_f_features_off_read_2'
        Assert-SameFileHash `
            -FirstPath $featuresOff1 `
            -SecondPath $featuresOff2 `
            -Label 'Phase F feature proof'
    }
    catch
    {
        Add-CleanupFailure `
            -Failures $cleanupFailures `
            -Label 'PHASE_F_FEATURES_OFF_PROOF' `
            -ErrorRecord $_
    }

    if ($finalStateExpected)
    {
        try
        {
            $finalOff1 = Invoke-SqlArtifact `
                -Server $isolatedServer `
                -InputPath $finalIntegrityProofPath `
                -Label 'final_integrity_features_off_read_1'
            $finalOff2 = Invoke-SqlArtifact `
                -Server $isolatedServer `
                -InputPath $finalIntegrityProofPath `
                -Label 'final_integrity_features_off_read_2'
            Assert-SameFileHash `
                -FirstPath $finalOff1 `
                -SecondPath $finalOff2 `
                -Label 'Final integrity proof with features OFF'
        }
        catch
        {
            Add-CleanupFailure `
                -Failures $cleanupFailures `
                -Label 'FINAL_INTEGRITY_FEATURES_OFF' `
                -ErrorRecord $_
        }
    }

    try
    {
        $productionFinal = Invoke-SqlArtifact `
            -Server $productionServer `
            -InputPath $productionProofPath `
            -Label 'production_final'
        Assert-FileHash `
            -Path $productionFinal `
            -ExpectedHash $currentProductionOutputHash `
            -Label 'Production final'
    }
    catch
    {
        Add-CleanupFailure `
            -Failures $cleanupFailures `
            -Label 'PRODUCTION_FINAL_PROOF' `
            -ErrorRecord $_
    }

    try
    {
        Write-Timeline 'HOST_FINAL|STARTED'
        Get-AndAssert-HostState -OutputPath (
            Join-Path $evidenceRoot 'host_state_final.json'
        )
        Write-Timeline 'HOST_FINAL|SUCCEEDED'
    }
    catch
    {
        Add-CleanupFailure `
            -Failures $cleanupFailures `
            -Label 'HOST_FINAL_PROOF' `
            -ErrorRecord $_
    }
}

if ($bodySucceeded -and
    $null -eq $bodyError -and
    $cleanupFailures.Count -eq 0)
{
    Write-Timeline 'RT02_COMPLETE_EXECUTION|VERIFIED'
    Write-ExecutionSummary `
        -Status 'VERIFIED' `
        -BodyError $null `
        -CleanupFailures $cleanupFailures
    exit 0
}

Write-Timeline 'RT02_COMPLETE_EXECUTION|BLOCKED'
Write-ExecutionSummary `
    -Status 'BLOCKED' `
    -BodyError $bodyError `
    -CleanupFailures $cleanupFailures
exit 1
