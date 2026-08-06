[CmdletBinding(DefaultParameterSetName = 'Load')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('RT02B-OPERATOR-APPROVAL-20260727-01')]
    [string] $OwnerApprovalId,

    [Parameter(Mandatory = $true, ParameterSetName = 'Load')]
    [switch] $Execute,

    [Parameter(Mandatory = $true, ParameterSetName = 'Verify')]
    [switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This is an intentionally one-shot, isolated-test fixture loader.
# It contains no schema DDL, cleanup, production route, or retry loop.
$workspaceRoot = 'D:\QLHV_APP'
$operatorArtifactRoot =
    'D:\QLHV_RT02_SQLDATA\RT02_COMPLETE_EXECUTION_20260727'
$schemaProofPath = Join-Path $workspaceRoot (
    'database\proofs\20260727_rt02b2_schema_gate_read_only.sql'
)

$serverIdentity = 'CSDLTTTC\QLHVRT02'
$sharedMemoryServer = 'lpc:CSDLTTTC\QLHVRT02'
$otoDatabase = 'QLHV_RT02_OTO_TEST'
$motoDatabase = 'QLHV_RT02_MOTO_TEST'
$targetDatabase = 'QLHV_RT02_TARGET_TEST'
$environmentId = 'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
$approvalExpiresAtUtc = '2026-07-31T16:59:59Z'
$expectedRepositoryHead =
    '383387e8456d1a61640eee190519ff3f28619218'
$generatorVersion = 'RT02-COMPLETE-FIXTURE-LOADER-V1'
$identityPurpose = 'RT02B2-ISOLATED-HARNESS'
$identityVersion = 'V1'
$identitySecret = 'RT02B2-SYNTHETIC-ONLY-KEY'
$identityNormalizationVersion =
    'HMACSHA256-UTF8-RT02B2-ISOLATED-HARNESS-V1'

$expectedOtoSchemaFingerprint =
    'D42001BF2752647360D2EB2397B9239908DCA80C10F037C79DA8C469C63348B2'
$expectedMotoSchemaFingerprint =
    '85E19B95E60E222C989FA1F222BB3A30C94A8788116CB521163907424F0EACC2'
$expectedTargetSchemaFingerprint =
    '3BDCF5C0C7CC5F0F17DA69709E03FB91C10E6CD8D1772533CF061752AEFE7634'
$expectedSchemaProofHash =
    '3E757EC68C51A4246014705E0EB57A32F6F44662BE56FB7E9012618C0C5365D7'

$artifactExpectations = [ordered] @{
    'ENABLE_OTO' = [pscustomobject] @{
        Path = Join-Path $operatorArtifactRoot 'enable_oto_v2.sql'
        Hash =
            '6151FB3C02497D280441C7CE9566C811F543BDA3C52E8DD9126367C2E298B557'
    }
    'ENABLE_MOTO' = [pscustomobject] @{
        Path = Join-Path $operatorArtifactRoot 'enable_moto_v2.sql'
        Hash =
            '755C869D0806FF33DB3588DAFF8E51991A8BB6AF56CF69E5DD9F079C8110D73B'
    }
    'DISABLE_OTO' = [pscustomobject] @{
        Path = Join-Path $operatorArtifactRoot 'disable_oto_v2.sql'
        Hash =
            '0594086AD8C420F2418145FED91F90B4D361A3CD9D50C9A1BB25BB0846E6D336'
    }
    'DISABLE_MOTO' = [pscustomobject] @{
        Path = Join-Path $operatorArtifactRoot 'disable_moto_v2.sql'
        Hash =
            '157A2AFAF14E221AB6D199702AFFAF473EC4333EB37E6A6DD6FDD89410A03470'
    }
}

function Convert-ToUpperHex
{
    param(
        [Parameter(Mandatory = $true)]
        [byte[]] $Bytes
    )

    return ([BitConverter]::ToString($Bytes)).Replace('-', '')
}

function Get-Sha256Hex
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Value
    )

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try
    {
        return Convert-ToUpperHex -Bytes $algorithm.ComputeHash($bytes)
    }
    finally
    {
        $algorithm.Dispose()
    }
}

function Get-KeyedIdentityHmac
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    # Exact QlhvDirectRealtimeHash.KeyedDiagnosticHmac contract:
    # HMACSHA256(UTF8(secret), UTF8($"{purpose}|{version}|{value}")).
    $keyBytes = [Text.Encoding]::UTF8.GetBytes($identitySecret)
    $payload = [Text.Encoding]::UTF8.GetBytes(
        "$identityPurpose|$identityVersion|$Value"
    )
    $algorithm = New-Object Security.Cryptography.HMACSHA256 (
        , $keyBytes
    )
    try
    {
        return Convert-ToUpperHex -Bytes $algorithm.ComputeHash($payload)
    }
    finally
    {
        $algorithm.Dispose()
        [Array]::Clear($keyBytes, 0, $keyBytes.Length)
    }
}

function Add-SqlParameter
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlCommand] $Command,

        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlDbType] $Type,

        [Parameter(Mandatory = $true)]
        [int] $Size,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [object] $Value
    )

    $parameter = if ($Size -gt 0)
    {
        $Command.Parameters.Add($Name, $Type, $Size)
    }
    else
    {
        $Command.Parameters.Add($Name, $Type)
    }
    $parameter.Value = $Value
}

function New-SourceRow
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('OTO', 'MOTO')]
        [string] $DatabaseRole,

        [Parameter(Mandatory = $true)]
        [string] $IdentityValue,

        [Parameter(Mandatory = $true)]
        [string] $DatasetRole,

        [Parameter(Mandatory = $true)]
        [string] $HoTen
    )

    $identity = Get-KeyedIdentityHmac -Value $IdentityValue
    return [pscustomobject] @{
        DatabaseRole = $DatabaseRole
        IdentityHmac = $identity
        ScenarioCode = 'CORE'
        DatasetRole = $DatasetRole
        HoTen = $HoTen
        SourceRowHash = Get-Sha256Hex -Value (
            "RT02B2|SOURCE|$identity|$HoTen"
        )
        PayloadHash = Get-Sha256Hex -Value "RT02B2|HOSO|$identity"
        IsActive = $true
    }
}

function New-TargetRow
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $IdentityHmac,

        [Parameter(Mandatory = $true)]
        [ValidateSet('CSDT_OTO', 'CSDT_MOTO')]
        [string] $SourceProfile,

        [Parameter(Mandatory = $true)]
        [string] $DatasetRole,

        [Parameter(Mandatory = $true)]
        [string] $HoTen,

        [Parameter(Mandatory = $true)]
        [string] $MappedHash,

        [Parameter(Mandatory = $true)]
        [ValidateSet('READY', 'CLOSED')]
        [string] $WorkflowState,

        [Parameter(Mandatory = $true)]
        [bool] $Active,

        [Parameter(Mandatory = $true)]
        [bool] $SoftDeleted
    )

    return [pscustomobject] @{
        IdentityHmac = $IdentityHmac
        SourceProfile = $SourceProfile
        ScenarioCode = 'CORE'
        DatasetRole = $DatasetRole
        HoTen = $HoTen
        MappedHash = $MappedHash
        QlhvOwnedHash = Get-Sha256Hex -Value (
            "RT02B2|QLHV|$IdentityHmac|$WorkflowState|" +
            'NOTES|PHOTO_DISABLED'
        )
        WorkflowState = $WorkflowState
        NotesHash = Get-Sha256Hex -Value (
            "RT02B2|NOTES|$IdentityHmac"
        )
        PhotoState = 'PHOTO_DISABLED'
        Active = $Active
        SoftDeleted = $SoftDeleted
    }
}

function New-DeterministicFixture
{
    $sourceRows =
        New-Object 'System.Collections.Generic.List[object]'
    $targetRows =
        New-Object 'System.Collections.Generic.List[object]'

    for ($index = 1; $index -le 150; $index++)
    {
        $ordinal = $index.ToString('D4')
        $source = New-SourceRow `
            -DatabaseRole 'OTO' `
            -IdentityValue "OTO|NO_CHANGE|$ordinal" `
            -DatasetRole 'NO_CHANGE' `
            -HoTen "SYNTHETIC OTO NOCHANGE $ordinal"
        $sourceRows.Add($source)
        $targetRows.Add(
            (New-TargetRow `
                -IdentityHmac $source.IdentityHmac `
                -SourceProfile 'CSDT_OTO' `
                -DatasetRole 'NO_CHANGE' `
                -HoTen $source.HoTen `
                -MappedHash $source.SourceRowHash `
                -WorkflowState 'READY' `
                -Active $true `
                -SoftDeleted $false)
        )
    }

    $insertSource = New-SourceRow `
        -DatabaseRole 'OTO' `
        -IdentityValue 'OTO|SOURCE_ONLY_NEW_ROW' `
        -DatasetRole 'SOURCE_ONLY_NEW_ROW' `
        -HoTen 'SYNTHETIC OTO INSERT'
    $sourceRows.Add($insertSource)

    $updateSource = New-SourceRow `
        -DatabaseRole 'OTO' `
        -IdentityValue 'OTO|STALE_IMPORTED_VALUE' `
        -DatasetRole 'STALE_IMPORTED_VALUE' `
        -HoTen 'SYNTHETIC OTO UPDATED'
    $sourceRows.Add($updateSource)
    $targetRows.Add(
        (New-TargetRow `
            -IdentityHmac $updateSource.IdentityHmac `
            -SourceProfile 'CSDT_OTO' `
            -DatasetRole 'STALE_IMPORTED_VALUE' `
            -HoTen 'SYNTHETIC OTO OLD' `
            -MappedHash (
                Get-Sha256Hex -Value (
                    "RT02B2|SOURCE|$($updateSource.IdentityHmac)|" +
                    'SYNTHETIC OTO OLD'
                )
            ) `
            -WorkflowState 'READY' `
            -Active $true `
            -SoftDeleted $false)
    )

    for ($index = 1; $index -le 5; $index++)
    {
        $ordinal = $index.ToString('D4')
        $source = New-SourceRow `
            -DatabaseRole 'MOTO' `
            -IdentityValue "MOTO|NO_CHANGE|$ordinal" `
            -DatasetRole 'NO_CHANGE' `
            -HoTen "SYNTHETIC MOTO NOCHANGE $ordinal"
        $sourceRows.Add($source)
        $targetRows.Add(
            (New-TargetRow `
                -IdentityHmac $source.IdentityHmac `
                -SourceProfile 'CSDT_MOTO' `
                -DatasetRole 'NO_CHANGE' `
                -HoTen $source.HoTen `
                -MappedHash $source.SourceRowHash `
                -WorkflowState 'READY' `
                -Active $true `
                -SoftDeleted $false)
        )
    }

    $targetOnlyIdentity =
        Get-KeyedIdentityHmac -Value 'OTO|SOURCE_ROW_REMOVED'
    $targetRows.Add(
        (New-TargetRow `
            -IdentityHmac $targetOnlyIdentity `
            -SourceProfile 'CSDT_OTO' `
            -DatasetRole 'SOURCE_ROW_REMOVED' `
            -HoTen 'SYNTHETIC OTO TARGET ONLY' `
            -MappedHash (
                Get-Sha256Hex -Value (
                    "RT02B2|TARGETONLY|$targetOnlyIdentity"
                )
            ) `
            -WorkflowState 'READY' `
            -Active $true `
            -SoftDeleted $false)
    )

    for ($index = 1; $index -le 3; $index++)
    {
        $ordinal = $index.ToString('D2')
        $identity = Get-KeyedIdentityHmac -Value (
            "OTO|SOFT_DELETED_BASELINE|$ordinal"
        )
        $targetRows.Add(
            (New-TargetRow `
                -IdentityHmac $identity `
                -SourceProfile 'CSDT_OTO' `
                -DatasetRole 'SOFT_DELETED_BASELINE' `
                -HoTen "SYNTHETIC OTO SOFTDELETED $ordinal" `
                -MappedHash (
                    Get-Sha256Hex -Value (
                        "RT02B2|SOFTDELETED|$identity"
                    )
                ) `
                -WorkflowState 'CLOSED' `
                -Active $false `
                -SoftDeleted $true)
        )
    }

    return [pscustomobject] @{
        SourceRows = $sourceRows
        TargetRows = $targetRows
    }
}

function Get-DatasetManifest
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[object]] $SourceRows,

        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[object]] $TargetRows,

        [Parameter(Mandatory = $true)]
        [string] $RepositoryHead,

        [Parameter(Mandatory = $true)]
        [string] $OtoSchemaFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $MotoSchemaFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $TargetSchemaFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $LoaderArtifactHash
    )

    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('RT02_DATASET_MANIFEST_V2')
    $lines.Add("ENVIRONMENT_ID=$environmentId")
    $lines.Add("OWNER_APPROVAL_ID=$OwnerApprovalId")
    $lines.Add("APPROVAL_EXPIRES_AT_UTC=$approvalExpiresAtUtc")
    $lines.Add('DATASET_MODE=SYNTHETIC')
    $lines.Add("GENERATOR_VERSION=$generatorVersion")
    $lines.Add("REPOSITORY_HEAD=$RepositoryHead")
    $lines.Add("SCHEMA_OTO=$OtoSchemaFingerprint")
    $lines.Add("SCHEMA_MOTO=$MotoSchemaFingerprint")
    $lines.Add("SCHEMA_TARGET=$TargetSchemaFingerprint")
    $lines.Add('COUNT_OTO_NO_CHANGE=150')
    $lines.Add('COUNT_OTO_INSERT=1')
    $lines.Add('COUNT_OTO_UPDATE=1')
    $lines.Add('COUNT_OTO_TARGET_ONLY=1')
    $lines.Add('COUNT_OTO_SOFT_DELETED=3')
    $lines.Add('COUNT_MOTO_NO_CHANGE=5')
    $lines.Add('COUNT_DUPLICATE_ACTIVE=0')
    $lines.Add("IDENTITY_PURPOSE=$identityPurpose")
    $lines.Add("IDENTITY_VERSION=$identityVersion")
    foreach ($artifactName in $artifactExpectations.Keys)
    {
        $lines.Add(
            "ARTIFACT_$artifactName=" +
            $artifactExpectations[$artifactName].Hash
        )
    }
    $lines.Add("ARTIFACT_SCHEMA_PROOF=$expectedSchemaProofHash")
    $lines.Add("ARTIFACT_FIXTURE_LOADER=$LoaderArtifactHash")
    $lines.Add('PII_ROWS=0')

    $sourceLines =
        New-Object 'System.Collections.Generic.List[string]'
    foreach ($row in $SourceRows)
    {
        $sourceLines.Add(
            "SOURCE|$($row.DatabaseRole)|$($row.IdentityHmac)|" +
            "$($row.ScenarioCode)|$($row.DatasetRole)|" +
            "$($row.SourceRowHash)|" +
            "$($row.PayloadHash)|HOTEN=" +
            (Get-Sha256Hex -Value $row.HoTen) +
            "|$([int] $row.IsActive)"
        )
    }
    $sourceLines.Sort([StringComparer]::Ordinal)
    foreach ($line in $sourceLines)
    {
        $lines.Add($line)
    }

    $targetLines =
        New-Object 'System.Collections.Generic.List[string]'
    foreach ($row in $TargetRows)
    {
        $targetLines.Add(
            "TARGET|$($row.SourceProfile)|$($row.IdentityHmac)|" +
            "$($row.ScenarioCode)|$($row.DatasetRole)|" +
            "$($row.MappedHash)|$($row.QlhvOwnedHash)|" +
            "$($row.WorkflowState)|$($row.NotesHash)|" +
            "$($row.PhotoState)|" +
            'HOTEN=' + (Get-Sha256Hex -Value $row.HoTen) + '|' +
            "$([int] $row.Active)|$([int] $row.SoftDeleted)"
        )
    }
    $targetLines.Sort([StringComparer]::Ordinal)
    foreach ($line in $targetLines)
    {
        $lines.Add($line)
    }

    return [string]::Join("`n", $lines)
}

function Get-CatalogFingerprintQuery
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProofPath
    )

    $proofText = [IO.File]::ReadAllText($ProofPath)
    $startToken = ';WITH SchemaMetadata AS'
    $endToken = 'FROM SchemaMetadata;'
    $start = $proofText.IndexOf(
        $startToken,
        [StringComparison]::Ordinal
    )
    if ($start -lt 0)
    {
        throw 'Catalog fingerprint query start token is absent.'
    }

    $end = $proofText.IndexOf(
        $endToken,
        $start,
        [StringComparison]::Ordinal
    )
    if ($end -lt 0)
    {
        throw 'Catalog fingerprint query end token is absent.'
    }

    return $proofText.Substring(
        $start,
        $end - $start + $endToken.Length
    )
}

function Get-CatalogFingerprint
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter()]
        [AllowNull()]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [ValidateSet(
            'QLHV_RT02_OTO_TEST',
            'QLHV_RT02_MOTO_TEST',
            'QLHV_RT02_TARGET_TEST'
        )]
        [string] $Database,

        [Parameter(Mandatory = $true)]
        [string] $CatalogQuery
    )

    $command = $Connection.CreateCommand()
    try
    {
        if ($null -ne $Transaction)
        {
            $command.Transaction = $Transaction
        }
        $command.CommandTimeout = 30
        $command.CommandText = "USE [$Database];`n$CatalogQuery"
        $reader = $command.ExecuteReader()
        try
        {
            if (-not $reader.Read())
            {
                throw "Catalog fingerprint returned no row for $Database."
            }
            return $reader.GetString(1)
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $command.Dispose()
    }
}

function Invoke-GuardCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.Transaction = $Transaction
        $command.CommandTimeout = 30
        $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @LockResult int;
EXEC @LockResult = sys.sp_getapplock
    @Resource = N'RT02-COMPLETE-FIXTURE-LOAD',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 0;
IF @LockResult < 0
    THROW 528000, 'RT02 fixture application lock was not acquired.', 1;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> @ExpectedServer
   OR CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition'))
      NOT LIKE N'%Developer%'
    THROW 528001, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
        N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
    )
)
    THROW 528002, 'ISOLATED_DATABASE_IDENTITY_REJECTED: denylist.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_OTO_TEST'
      AND databaseItem.database_id = 5
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_MOTO_TEST'
      AND databaseItem.database_id = 6
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_TARGET_TEST'
      AND databaseItem.database_id = 7
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
)
    THROW 528003, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR (name = N'RT02_OWNER_APPROVAL_ID'
              AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR (name = N'RT02_DATASET_MODE'
              AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
              AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR (name = N'RT02_EXPIRES_AT_UTC'
              AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR (name = N'RT02_OWNER_APPROVAL_ID'
              AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR (name = N'RT02_DATASET_MODE'
              AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
              AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR (name = N'RT02_EXPIRES_AT_UTC'
              AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR (name = N'RT02_OWNER_APPROVAL_ID'
              AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR (name = N'RT02_DATASET_MODE'
              AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
              AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR (name = N'RT02_EXPIRES_AT_UTC'
              AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
OR TRY_CONVERT(datetime2(0), @ExpiresAtUtc, 127) <= SYSUTCDATETIME()
    THROW 528004, 'ISOLATED_DATABASE_IDENTITY_REJECTED: markers.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'QLHV_RT02_OTO_TEST')
      AND retention_period = 2
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'QLHV_RT02_MOTO_TEST')
      AND retention_period = 2
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
)
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables AS trackingItem
    INNER JOIN [QLHV_RT02_OTO_TEST].sys.tables AS tableItem
        ON tableItem.object_id = trackingItem.object_id
    INNER JOIN [QLHV_RT02_OTO_TEST].sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE schemaItem.name = N'dbo'
      AND tableItem.name IN (N'NguoiLX', N'NguoiLX_HoSo')
      AND trackingItem.is_track_columns_updated_on = 1
) <> 2
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables
) <> 2
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables AS trackingItem
    INNER JOIN [QLHV_RT02_MOTO_TEST].sys.tables AS tableItem
        ON tableItem.object_id = trackingItem.object_id
    INNER JOIN [QLHV_RT02_MOTO_TEST].sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE schemaItem.name = N'dbo'
      AND tableItem.name IN (N'NguoiLX', N'NguoiLX_HoSo')
      AND trackingItem.is_track_columns_updated_on = 1
) <> 2
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables
) <> 2
OR EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE
        (name IN (N'QLHV_RT02_OTO_TEST', N'QLHV_RT02_MOTO_TEST')
         AND
         (snapshot_isolation_state <> 1
          OR is_read_committed_snapshot_on <> 0))
        OR
        (name = N'QLHV_RT02_TARGET_TEST'
         AND
         (snapshot_isolation_state <> 0
          OR is_read_committed_snapshot_on <> 0
          OR is_auto_close_on <> 0))
)
OR EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'QLHV_RT02_TARGET_TEST')
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].sys.change_tracking_tables
)
    THROW 528005, 'RT02 source feature gate failed.', 1;

IF EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
OR EXISTS (SELECT 1 FROM [QLHV_RT02_OTO_TEST].sys.synonyms)
OR EXISTS (SELECT 1 FROM [QLHV_RT02_MOTO_TEST].sys.synonyms)
OR EXISTS (SELECT 1 FROM [QLHV_RT02_TARGET_TEST].sys.synonyms)
OR EXISTS
(
    SELECT 1
    FROM sys.dm_exec_sessions
    WHERE session_id <> @@SPID
      AND
      (
          program_name LIKE N'QLHV.Api%'
          OR program_name LIKE N'QLHV.Worker%'
      )
)
    THROW 528006, 'ISOLATED_DATABASE_IDENTITY_REJECTED: route.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX_HoSo
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX_HoSo
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ManualReviewEvidence
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyMarker
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyCheckpoint
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState
        WITH (UPDLOCK, HOLDLOCK)
) <> 0
    THROW 528007, 'RT02 fixture zero-row gate failed.', 1;
'@
        Add-SqlParameter `
            -Command $command `
            -Name '@ExpectedServer' `
            -Type ([Data.SqlDbType]::NVarChar) `
            -Size 128 `
            -Value $serverIdentity
        Add-SqlParameter `
            -Command $command `
            -Name '@EnvironmentId' `
            -Type ([Data.SqlDbType]::NVarChar) `
            -Size 128 `
            -Value $environmentId
        Add-SqlParameter `
            -Command $command `
            -Name '@ApprovalId' `
            -Type ([Data.SqlDbType]::NVarChar) `
            -Size 128 `
            -Value $OwnerApprovalId
        Add-SqlParameter `
            -Command $command `
            -Name '@ExpiresAtUtc' `
            -Type ([Data.SqlDbType]::NVarChar) `
            -Size 128 `
            -Value $approvalExpiresAtUtc
        [void] $command.ExecuteNonQuery()
    }
    finally
    {
        $command.Dispose()
    }
}

function Add-SourceRow
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [pscustomobject] $Row
    )

    $database = switch ($Row.DatabaseRole)
    {
        'OTO' { $otoDatabase }
        'MOTO' { $motoDatabase }
        default { throw 'Source database role is not allowlisted.' }
    }

    $command = $Connection.CreateCommand()
    try
    {
        $command.Transaction = $Transaction
        $command.CommandTimeout = 30
        $command.CommandText = @"
SET NOCOUNT OFF;
INSERT [$database].dbo.NguoiLX
(
    IdentityHmac, ScenarioCode, DatasetRole, HoTen, SourceRowHash,
    IsActive
)
VALUES
(
    @IdentityHmac, @ScenarioCode, @DatasetRole, @HoTen, @SourceRowHash,
    @IsActive
);
INSERT [$database].dbo.NguoiLX_HoSo
(
    IdentityHmac, PayloadHash
)
VALUES
(
    @IdentityHmac, @PayloadHash
);
"@
        Add-SqlParameter $command '@IdentityHmac' `
            ([Data.SqlDbType]::Char) 64 $Row.IdentityHmac
        Add-SqlParameter $command '@ScenarioCode' `
            ([Data.SqlDbType]::VarChar) 40 $Row.ScenarioCode
        Add-SqlParameter $command '@DatasetRole' `
            ([Data.SqlDbType]::VarChar) 40 $Row.DatasetRole
        Add-SqlParameter $command '@HoTen' `
            ([Data.SqlDbType]::NVarChar) 200 $Row.HoTen
        Add-SqlParameter $command '@SourceRowHash' `
            ([Data.SqlDbType]::Char) 64 $Row.SourceRowHash
        Add-SqlParameter $command '@IsActive' `
            ([Data.SqlDbType]::Bit) 0 $Row.IsActive
        Add-SqlParameter $command '@PayloadHash' `
            ([Data.SqlDbType]::Char) 64 $Row.PayloadHash
        $affected = $command.ExecuteNonQuery()
        if ($affected -ne 2)
        {
            throw 'RT02 source fixture insert affected an unexpected count.'
        }
    }
    finally
    {
        $command.Dispose()
    }
}

function Add-TargetRow
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [pscustomobject] $Row
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.Transaction = $Transaction
        $command.CommandTimeout = 30
        $command.CommandText = @'
SET NOCOUNT OFF;
INSERT [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
(
    IdentityHmac, SourceProfile, ScenarioCode, DatasetRole, HoTen,
    MappedHash, QlhvOwnedHash, WorkflowState, NotesHash, PhotoState,
    Active, SoftDeleted
)
VALUES
(
    @IdentityHmac, @SourceProfile, @ScenarioCode, @DatasetRole, @HoTen,
    @MappedHash, @QlhvOwnedHash, @WorkflowState, @NotesHash, @PhotoState,
    @Active, @SoftDeleted
);
'@
        Add-SqlParameter $command '@IdentityHmac' `
            ([Data.SqlDbType]::Char) 64 $Row.IdentityHmac
        Add-SqlParameter $command '@SourceProfile' `
            ([Data.SqlDbType]::VarChar) 20 $Row.SourceProfile
        Add-SqlParameter $command '@ScenarioCode' `
            ([Data.SqlDbType]::VarChar) 40 $Row.ScenarioCode
        Add-SqlParameter $command '@DatasetRole' `
            ([Data.SqlDbType]::VarChar) 40 $Row.DatasetRole
        Add-SqlParameter $command '@HoTen' `
            ([Data.SqlDbType]::NVarChar) 200 $Row.HoTen
        Add-SqlParameter $command '@MappedHash' `
            ([Data.SqlDbType]::Char) 64 $Row.MappedHash
        Add-SqlParameter $command '@QlhvOwnedHash' `
            ([Data.SqlDbType]::Char) 64 $Row.QlhvOwnedHash
        Add-SqlParameter $command '@WorkflowState' `
            ([Data.SqlDbType]::VarChar) 40 $Row.WorkflowState
        Add-SqlParameter $command '@NotesHash' `
            ([Data.SqlDbType]::Char) 64 $Row.NotesHash
        Add-SqlParameter $command '@PhotoState' `
            ([Data.SqlDbType]::VarChar) 40 $Row.PhotoState
        Add-SqlParameter $command '@Active' `
            ([Data.SqlDbType]::Bit) 0 $Row.Active
        Add-SqlParameter $command '@SoftDeleted' `
            ([Data.SqlDbType]::Bit) 0 $Row.SoftDeleted
        $affected = $command.ExecuteNonQuery()
        if ($affected -ne 1)
        {
            throw 'RT02 target fixture insert affected an unexpected count.'
        }
    }
    finally
    {
        $command.Dispose()
    }
}

function Add-EnvironmentState
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [string] $DatasetFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $MappingFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $SourceSchemaFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $TargetSchemaFingerprint
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.Transaction = $Transaction
        $command.CommandTimeout = 30
        $command.CommandText = @'
SET NOCOUNT OFF;
INSERT [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState
(
    EnvironmentId, DatasetFingerprint, MappingFingerprint,
    SourceSchemaFingerprint, TargetSchemaFingerprint,
    IdentityNormalizationVersion, DatasetMode, PiiRows, CreatedAtUtc
)
VALUES
(
    @EnvironmentId, @DatasetFingerprint, @MappingFingerprint,
    @SourceSchemaFingerprint, @TargetSchemaFingerprint,
    @IdentityNormalizationVersion, 'SYNTHETIC', 0, SYSUTCDATETIME()
);
'@
        Add-SqlParameter $command '@EnvironmentId' `
            ([Data.SqlDbType]::VarChar) 128 $environmentId
        Add-SqlParameter $command '@DatasetFingerprint' `
            ([Data.SqlDbType]::Char) 64 $DatasetFingerprint
        Add-SqlParameter $command '@MappingFingerprint' `
            ([Data.SqlDbType]::Char) 64 $MappingFingerprint
        Add-SqlParameter $command '@SourceSchemaFingerprint' `
            ([Data.SqlDbType]::Char) 64 $SourceSchemaFingerprint
        Add-SqlParameter $command '@TargetSchemaFingerprint' `
            ([Data.SqlDbType]::Char) 64 $TargetSchemaFingerprint
        Add-SqlParameter $command '@IdentityNormalizationVersion' `
            ([Data.SqlDbType]::VarChar) 60 $identityNormalizationVersion
        $affected = $command.ExecuteNonQuery()
        if ($affected -ne 1)
        {
            throw 'RT02 environment-state insert affected an unexpected count.'
        }
    }
    finally
    {
        $command.Dispose()
    }
}

function Assert-PostInsertState
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [string] $DatasetFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $MappingFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $SourceSchemaFingerprint,

        [Parameter(Mandatory = $true)]
        [string] $TargetSchemaFingerprint
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.Transaction = $Transaction
        $command.CommandTimeout = 30
        $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
    WHERE ScenarioCode = 'CORE' AND DatasetRole = 'NO_CHANGE'
) <> 150
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
    WHERE ScenarioCode = 'CORE'
      AND DatasetRole = 'SOURCE_ONLY_NEW_ROW'
) <> 1
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
    WHERE ScenarioCode = 'CORE'
      AND DatasetRole = 'STALE_IMPORTED_VALUE'
) <> 1
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
) <> 152
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX_HoSo
) <> 152
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
    WHERE ScenarioCode = 'CORE' AND DatasetRole = 'NO_CHANGE'
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX_HoSo
) <> 5
    THROW 528010, 'RT02 source fixture count assertion failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE SourceProfile = 'CSDT_OTO'
      AND ScenarioCode = 'CORE'
      AND DatasetRole = 'NO_CHANGE'
      AND Active = 1
      AND SoftDeleted = 0
) <> 150
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE SourceProfile = 'CSDT_OTO'
      AND ScenarioCode = 'CORE'
      AND DatasetRole = 'STALE_IMPORTED_VALUE'
      AND HoTen = N'SYNTHETIC OTO OLD'
      AND Active = 1
      AND SoftDeleted = 0
) <> 1
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE SourceProfile = 'CSDT_OTO'
      AND ScenarioCode = 'CORE'
      AND DatasetRole = 'SOURCE_ROW_REMOVED'
      AND Active = 1
      AND SoftDeleted = 0
) <> 1
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE SourceProfile = 'CSDT_OTO'
      AND ScenarioCode = 'CORE'
      AND DatasetRole = 'SOFT_DELETED_BASELINE'
      AND Active = 0
      AND SoftDeleted = 1
) <> 3
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE SourceProfile = 'CSDT_MOTO'
      AND ScenarioCode = 'CORE'
      AND DatasetRole = 'NO_CHANGE'
      AND Active = 1
      AND SoftDeleted = 0
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
) <> 160
    THROW 528011, 'RT02 target fixture count assertion failed.', 1;

IF EXISTS
(
    SELECT SourceProfile, IdentityHmac
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE Active = 1 AND SoftDeleted = 0
    GROUP BY SourceProfile, IdentityHmac
    HAVING COUNT_BIG(*) > 1
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
    WHERE IdentityHmac COLLATE Latin1_General_100_BIN2
              LIKE '%[^0-9A-F]%'
       OR LEN(IdentityHmac) <> 64
       OR HoTen NOT LIKE N'SYNTHETIC %'
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
    WHERE IdentityHmac COLLATE Latin1_General_100_BIN2
              LIKE '%[^0-9A-F]%'
       OR LEN(IdentityHmac) <> 64
       OR HoTen NOT LIKE N'SYNTHETIC %'
       OR IsActive <> 1
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
    WHERE IdentityHmac COLLATE Latin1_General_100_BIN2
              LIKE '%[^0-9A-F]%'
       OR LEN(IdentityHmac) <> 64
       OR HoTen NOT LIKE N'SYNTHETIC %'
       OR IsActive <> 1
)
    THROW 528012, 'RT02 fixture identity/privacy assertion failed.', 1;

IF EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
    INNER JOIN [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner AS targetItem
        ON targetItem.IdentityHmac = sourceItem.IdentityHmac
    WHERE sourceItem.DatasetRole = 'SOURCE_ONLY_NEW_ROW'
)
OR NOT EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner AS targetItem
    WHERE targetItem.DatasetRole = 'SOURCE_ROW_REMOVED'
      AND targetItem.Active = 1
      AND targetItem.SoftDeleted = 0
      AND NOT EXISTS
      (
          SELECT 1
          FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS sourceItem
          WHERE sourceItem.IdentityHmac = targetItem.IdentityHmac
      )
)
    THROW 528013, 'RT02 insert/target-only relationship assertion failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState
    WHERE EnvironmentId = @EnvironmentId
      AND DatasetFingerprint = @DatasetFingerprint
      AND MappingFingerprint = @MappingFingerprint
      AND SourceSchemaFingerprint = @SourceSchemaFingerprint
      AND TargetSchemaFingerprint = @TargetSchemaFingerprint
      AND IdentityNormalizationVersion = @IdentityNormalizationVersion
      AND DatasetMode = 'SYNTHETIC'
      AND PiiRows = 0
) <> 1
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState
) <> 1
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ManualReviewEvidence
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyMarker
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyCheckpoint
)
    THROW 528014, 'RT02 environment/marker assertion failed.', 1;
'@
        Add-SqlParameter $command '@EnvironmentId' `
            ([Data.SqlDbType]::VarChar) 128 $environmentId
        Add-SqlParameter $command '@DatasetFingerprint' `
            ([Data.SqlDbType]::Char) 64 $DatasetFingerprint
        Add-SqlParameter $command '@MappingFingerprint' `
            ([Data.SqlDbType]::Char) 64 $MappingFingerprint
        Add-SqlParameter $command '@SourceSchemaFingerprint' `
            ([Data.SqlDbType]::Char) 64 $SourceSchemaFingerprint
        Add-SqlParameter $command '@TargetSchemaFingerprint' `
            ([Data.SqlDbType]::Char) 64 $TargetSchemaFingerprint
        Add-SqlParameter $command '@IdentityNormalizationVersion' `
            ([Data.SqlDbType]::VarChar) 60 $identityNormalizationVersion
        [void] $command.ExecuteNonQuery()
    }
    finally
    {
        $command.Dispose()
    }
}

function Read-ObservedFixtureState
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction
    )

    $sourceRows =
        New-Object 'System.Collections.Generic.List[object]'
    $targetRows =
        New-Object 'System.Collections.Generic.List[object]'

    $sourceCommand = $Connection.CreateCommand()
    try
    {
        $sourceCommand.Transaction = $Transaction
        $sourceCommand.CommandTimeout = 30
        $sourceCommand.CommandText = @'
SELECT
    CONVERT(varchar(4), 'OTO') AS DatabaseRole,
    learner.IdentityHmac,
    learner.ScenarioCode,
    learner.DatasetRole,
    learner.HoTen,
    learner.SourceRowHash,
    dossier.PayloadHash,
    learner.IsActive
FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX AS learner
INNER JOIN [QLHV_RT02_OTO_TEST].dbo.NguoiLX_HoSo AS dossier
    ON dossier.IdentityHmac = learner.IdentityHmac
UNION ALL
SELECT
    CONVERT(varchar(4), 'MOTO') AS DatabaseRole,
    learner.IdentityHmac,
    learner.ScenarioCode,
    learner.DatasetRole,
    learner.HoTen,
    learner.SourceRowHash,
    dossier.PayloadHash,
    learner.IsActive
FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX AS learner
INNER JOIN [QLHV_RT02_MOTO_TEST].dbo.NguoiLX_HoSo AS dossier
    ON dossier.IdentityHmac = learner.IdentityHmac
ORDER BY DatabaseRole, IdentityHmac;
'@
        $reader = $sourceCommand.ExecuteReader()
        try
        {
            while ($reader.Read())
            {
                $sourceRows.Add(
                    [pscustomobject] @{
                        DatabaseRole = $reader.GetString(0)
                        IdentityHmac = $reader.GetString(1)
                        ScenarioCode = $reader.GetString(2)
                        DatasetRole = $reader.GetString(3)
                        HoTen = $reader.GetString(4)
                        SourceRowHash = $reader.GetString(5)
                        PayloadHash = $reader.GetString(6)
                        IsActive = $reader.GetBoolean(7)
                    }
                )
            }
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $sourceCommand.Dispose()
    }

    $targetCommand = $Connection.CreateCommand()
    try
    {
        $targetCommand.Transaction = $Transaction
        $targetCommand.CommandTimeout = 30
        $targetCommand.CommandText = @'
SELECT
    IdentityHmac,
    SourceProfile,
    ScenarioCode,
    DatasetRole,
    HoTen,
    MappedHash,
    QlhvOwnedHash,
    WorkflowState,
    NotesHash,
    PhotoState,
    Active,
    SoftDeleted
FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
ORDER BY SourceProfile, IdentityHmac;
'@
        $reader = $targetCommand.ExecuteReader()
        try
        {
            while ($reader.Read())
            {
                $targetRows.Add(
                    [pscustomobject] @{
                        IdentityHmac = $reader.GetString(0)
                        SourceProfile = $reader.GetString(1)
                        ScenarioCode = $reader.GetString(2)
                        DatasetRole = $reader.GetString(3)
                        HoTen = $reader.GetString(4)
                        MappedHash = $reader.GetString(5)
                        QlhvOwnedHash = $reader.GetString(6)
                        WorkflowState = $reader.GetString(7)
                        NotesHash = $reader.GetString(8)
                        PhotoState = $reader.GetString(9)
                        Active = $reader.GetBoolean(10)
                        SoftDeleted = $reader.GetBoolean(11)
                    }
                )
            }
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $targetCommand.Dispose()
    }

    $stateCommand = $Connection.CreateCommand()
    try
    {
        $stateCommand.Transaction = $Transaction
        $stateCommand.CommandTimeout = 30
        $stateCommand.CommandText = @'
SELECT
    EnvironmentId,
    DatasetFingerprint,
    MappingFingerprint,
    SourceSchemaFingerprint,
    TargetSchemaFingerprint,
    IdentityNormalizationVersion,
    DatasetMode,
    PiiRows
FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState;
'@
        $reader = $stateCommand.ExecuteReader()
        try
        {
            if (-not $reader.Read())
            {
                throw 'The RT02 environment-state row is absent.'
            }
            $state = [pscustomobject] @{
                EnvironmentId = $reader.GetString(0)
                DatasetFingerprint = $reader.GetString(1)
                MappingFingerprint = $reader.GetString(2)
                SourceSchemaFingerprint = $reader.GetString(3)
                TargetSchemaFingerprint = $reader.GetString(4)
                IdentityNormalizationVersion = $reader.GetString(5)
                DatasetMode = $reader.GetString(6)
                PiiRows = $reader.GetInt32(7)
            }
            if ($reader.Read())
            {
                throw 'More than one RT02 environment-state row exists.'
            }
        }
        finally
        {
            $reader.Dispose()
        }
    }
    finally
    {
        $stateCommand.Dispose()
    }

    return [pscustomobject] @{
        SourceRows = $sourceRows
        TargetRows = $targetRows
        State = $state
    }
}

function Invoke-ReadOnlyFixtureGuard
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.Transaction = $Transaction
        $command.CommandTimeout = 30
        $command.CommandText = @'
SET NOCOUNT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> @ExpectedServer
   OR CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition'))
      NOT LIKE N'%Developer%'
    THROW 528020, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'CSDL_OTO', N'CSDL_MOTO', N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
        N'QLHV_APP', N'CSDL_OTO_V1', N'CSDL_MOTO_V1'
    )
)
OR EXISTS (SELECT 1 FROM sys.servers WHERE is_linked = 1)
OR EXISTS (SELECT 1 FROM [QLHV_RT02_OTO_TEST].sys.synonyms)
OR EXISTS (SELECT 1 FROM [QLHV_RT02_MOTO_TEST].sys.synonyms)
OR EXISTS (SELECT 1 FROM [QLHV_RT02_TARGET_TEST].sys.synonyms)
OR EXISTS
(
    SELECT 1
    FROM sys.dm_exec_sessions
    WHERE session_id <> @@SPID
      AND
      (
          program_name LIKE N'QLHV.Api%'
          OR program_name LIKE N'QLHV.Worker%'
      )
)
    THROW 528025, 'ISOLATED_DATABASE_IDENTITY_REJECTED: route.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_OTO_TEST'
      AND databaseItem.database_id = 5
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_MOTO_TEST'
      AND databaseItem.database_id = 6
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.databases AS databaseItem
    INNER JOIN sys.database_recovery_status AS recoveryItem
        ON recoveryItem.database_id = databaseItem.database_id
    WHERE databaseItem.name = N'QLHV_RT02_TARGET_TEST'
      AND databaseItem.database_id = 7
      AND recoveryItem.database_guid =
          CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
      AND databaseItem.state_desc = N'ONLINE'
      AND databaseItem.is_read_only = 0
      AND databaseItem.source_database_id IS NULL
)
    THROW 528021, 'ISOLATED_DATABASE_IDENTITY_REJECTED: database.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR (name = N'RT02_OWNER_APPROVAL_ID'
              AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR (name = N'RT02_DATASET_MODE'
              AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
              AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR (name = N'RT02_EXPIRES_AT_UTC'
              AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR (name = N'RT02_OWNER_APPROVAL_ID'
              AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR (name = N'RT02_DATASET_MODE'
              AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
              AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR (name = N'RT02_EXPIRES_AT_UTC'
              AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR (name = N'RT02_OWNER_APPROVAL_ID'
              AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR (name = N'RT02_DATASET_MODE'
              AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
              AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR (name = N'RT02_EXPIRES_AT_UTC'
              AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
    THROW 528022, 'ISOLATED_DATABASE_IDENTITY_REJECTED: markers.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'QLHV_RT02_OTO_TEST')
      AND retention_period = 2
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
)
OR NOT EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'QLHV_RT02_MOTO_TEST')
      AND retention_period = 2
      AND retention_period_units_desc = N'DAYS'
      AND is_auto_cleanup_on = 1
)
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables
) <> 2
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables AS trackingItem
    INNER JOIN [QLHV_RT02_OTO_TEST].sys.tables AS tableItem
        ON tableItem.object_id = trackingItem.object_id
    INNER JOIN [QLHV_RT02_OTO_TEST].sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE schemaItem.name = N'dbo'
      AND tableItem.name IN (N'NguoiLX', N'NguoiLX_HoSo')
      AND trackingItem.is_track_columns_updated_on = 1
) <> 2
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables
) <> 2
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables AS trackingItem
    INNER JOIN [QLHV_RT02_MOTO_TEST].sys.tables AS tableItem
        ON tableItem.object_id = trackingItem.object_id
    INNER JOIN [QLHV_RT02_MOTO_TEST].sys.schemas AS schemaItem
        ON schemaItem.schema_id = tableItem.schema_id
    WHERE schemaItem.name = N'dbo'
      AND tableItem.name IN (N'NguoiLX', N'NguoiLX_HoSo')
      AND trackingItem.is_track_columns_updated_on = 1
) <> 2
OR EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE
        (name IN (N'QLHV_RT02_OTO_TEST', N'QLHV_RT02_MOTO_TEST')
         AND
         (snapshot_isolation_state <> 1
          OR is_read_committed_snapshot_on <> 0))
        OR
        (name = N'QLHV_RT02_TARGET_TEST'
         AND
         (snapshot_isolation_state <> 0
          OR is_read_committed_snapshot_on <> 0))
)
OR EXISTS
(
    SELECT 1
    FROM sys.change_tracking_databases
    WHERE database_id = DB_ID(N'QLHV_RT02_TARGET_TEST')
)
    THROW 528023, 'RT02 fixture feature proof failed.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX
) <> 152
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_OTO_TEST].dbo.NguoiLX_HoSo
) <> 152
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_MOTO_TEST].dbo.NguoiLX_HoSo
) <> 5
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02Learner
) <> 160
OR
(
    SELECT COUNT_BIG(*)
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02EnvironmentState
) <> 1
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ManualReviewEvidence
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyMarker
)
OR EXISTS
(
    SELECT 1
    FROM [QLHV_RT02_TARGET_TEST].dbo.Rt02ApplyCheckpoint
)
    THROW 528024, 'RT02 fixture read-only count proof failed.', 1;
'@
        Add-SqlParameter $command '@ExpectedServer' `
            ([Data.SqlDbType]::NVarChar) 128 $serverIdentity
        Add-SqlParameter $command '@EnvironmentId' `
            ([Data.SqlDbType]::NVarChar) 128 $environmentId
        Add-SqlParameter $command '@ApprovalId' `
            ([Data.SqlDbType]::NVarChar) 128 $OwnerApprovalId
        Add-SqlParameter $command '@ExpiresAtUtc' `
            ([Data.SqlDbType]::NVarChar) 128 $approvalExpiresAtUtc
        [void] $command.ExecuteNonQuery()
    }
    finally
    {
        $command.Dispose()
    }
}

function Get-ManifestSectionFingerprint
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $Manifest,

        [Parameter(Mandatory = $true)]
        [ValidateSet('SOURCE|', 'TARGET|')]
        [string] $Prefix
    )

    $lines = @(
        $Manifest.Split("`n") |
            Where-Object { $_.StartsWith($Prefix, [StringComparison]::Ordinal) }
    )
    return Get-Sha256Hex -Value ([string]::Join("`n", $lines))
}

function Read-FixtureProofOnce
{
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConnectionString,

        [Parameter(Mandatory = $true)]
        [string] $CatalogQuery,

        [Parameter(Mandatory = $true)]
        [string] $RepositoryHead,

        [Parameter(Mandatory = $true)]
        [string] $LoaderArtifactHash,

        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[object]] $ExpectedSourceRows,

        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.List[object]] $ExpectedTargetRows
    )

    $connection =
        New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    $transaction = $null
    try
    {
        $connection.Open()
        $transaction = $connection.BeginTransaction(
            [Data.IsolationLevel]::Serializable
        )

        Invoke-ReadOnlyFixtureGuard `
            -Connection $connection `
            -Transaction $transaction

        $otoSchemaFingerprint = Get-CatalogFingerprint `
            -Connection $connection `
            -Transaction $transaction `
            -Database $otoDatabase `
            -CatalogQuery $CatalogQuery
        $motoSchemaFingerprint = Get-CatalogFingerprint `
            -Connection $connection `
            -Transaction $transaction `
            -Database $motoDatabase `
            -CatalogQuery $CatalogQuery
        $targetSchemaFingerprint = Get-CatalogFingerprint `
            -Connection $connection `
            -Transaction $transaction `
            -Database $targetDatabase `
            -CatalogQuery $CatalogQuery

        if ($otoSchemaFingerprint -cne $expectedOtoSchemaFingerprint -or
            $motoSchemaFingerprint -cne $expectedMotoSchemaFingerprint -or
            $targetSchemaFingerprint -cne $expectedTargetSchemaFingerprint)
        {
            throw 'The fixture proof observed catalog drift.'
        }

        $observed = Read-ObservedFixtureState `
            -Connection $connection `
            -Transaction $transaction
        if ($observed.SourceRows.Count -ne 157 -or
            $observed.TargetRows.Count -ne 160)
        {
            throw 'The fixture proof observed a partial dataset.'
        }

        $expectedManifest = Get-DatasetManifest `
            -SourceRows $ExpectedSourceRows `
            -TargetRows $ExpectedTargetRows `
            -RepositoryHead $RepositoryHead `
            -OtoSchemaFingerprint $otoSchemaFingerprint `
            -MotoSchemaFingerprint $motoSchemaFingerprint `
            -TargetSchemaFingerprint $targetSchemaFingerprint `
            -LoaderArtifactHash $LoaderArtifactHash
        $observedManifest = Get-DatasetManifest `
            -SourceRows $observed.SourceRows `
            -TargetRows $observed.TargetRows `
            -RepositoryHead $RepositoryHead `
            -OtoSchemaFingerprint $otoSchemaFingerprint `
            -MotoSchemaFingerprint $motoSchemaFingerprint `
            -TargetSchemaFingerprint $targetSchemaFingerprint `
            -LoaderArtifactHash $LoaderArtifactHash
        $expectedFingerprint = Get-Sha256Hex -Value $expectedManifest
        $observedFingerprint = Get-Sha256Hex -Value $observedManifest
        $mappingFingerprint = Get-Sha256Hex -Value (
            'RT02B2-MAPPING-V1-HOTEN-ONLY'
        )
        $sourceSchemaFingerprint = Get-Sha256Hex -Value (
            "OTO=$otoSchemaFingerprint|MOTO=$motoSchemaFingerprint"
        )

        if ($observedFingerprint -cne $expectedFingerprint -or
            $observed.State.EnvironmentId -cne $environmentId -or
            $observed.State.DatasetFingerprint -cne $expectedFingerprint -or
            $observed.State.MappingFingerprint -cne $mappingFingerprint -or
            $observed.State.SourceSchemaFingerprint -cne
                $sourceSchemaFingerprint -or
            $observed.State.TargetSchemaFingerprint -cne
                $targetSchemaFingerprint -or
            $observed.State.IdentityNormalizationVersion -cne
                $identityNormalizationVersion -or
            $observed.State.DatasetMode -cne 'SYNTHETIC' -or
            $observed.State.PiiRows -ne 0)
        {
            throw 'The persisted fixture fingerprint/state is inconsistent.'
        }

        $transaction.Commit()
        return [pscustomobject] @{
            OtoNoChange = 150
            OtoInsertCandidate = 1
            OtoUpdateCandidate = 1
            OtoTargetOnlyActive = 1
            OtoSoftDeletedBaseline = 3
            MotoNoChange = 5
            DuplicateActiveGroups = 0
            DatasetFingerprint = $observedFingerprint
            SourceRowsFingerprint = Get-ManifestSectionFingerprint `
                -Manifest $observedManifest `
                -Prefix 'SOURCE|'
            TargetRowsFingerprint = Get-ManifestSectionFingerprint `
                -Manifest $observedManifest `
                -Prefix 'TARGET|'
            OtoSchemaFingerprint = $otoSchemaFingerprint
            MotoSchemaFingerprint = $motoSchemaFingerprint
            TargetSchemaFingerprint = $targetSchemaFingerprint
        }
    }
    catch
    {
        if ($null -ne $transaction -and
            $null -ne $transaction.Connection)
        {
            try
            {
                $transaction.Rollback()
            }
            catch
            {
                throw 'The read-only fixture-proof transaction did not close.'
            }
        }
        throw
    }
    finally
    {
        if ($null -ne $transaction)
        {
            $transaction.Dispose()
        }
        $connection.Dispose()
    }
}

if (-not $Execute.IsPresent -and -not $VerifyOnly.IsPresent)
{
    throw 'The explicit -Execute or -VerifyOnly switch is required.'
}

if (-not (Test-Path -LiteralPath $schemaProofPath -PathType Leaf))
{
    throw 'The hash-pinned schema proof is absent.'
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $schemaProofPath).Hash -cne
    $expectedSchemaProofHash)
{
    throw 'The schema-proof hash does not match the approved artifact.'
}

foreach ($artifactName in $artifactExpectations.Keys)
{
    $artifact = $artifactExpectations[$artifactName]
    if (-not (Test-Path -LiteralPath $artifact.Path -PathType Leaf))
    {
        throw "The corrected $artifactName artifact is absent."
    }
    $observedHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $artifact.Path
    ).Hash
    if ($observedHash -cne $artifact.Hash)
    {
        throw "The corrected $artifactName artifact hash is rejected."
    }
}

$repositoryHead = (
    & git -C $workspaceRoot rev-parse HEAD
)
if ($LASTEXITCODE -ne 0)
{
    throw 'Unable to resolve the repository HEAD.'
}
$repositoryHead = ([string] $repositoryHead).Trim()
if ($repositoryHead -cne $expectedRepositoryHead)
{
    throw 'The repository HEAD does not match the approved RT-02 baseline.'
}

$loaderArtifactHash = (
    Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath
).Hash
$catalogQuery = Get-CatalogFingerprintQuery -ProofPath $schemaProofPath
$fixture = New-DeterministicFixture

if ($fixture.SourceRows.Count -ne 157 -or
    $fixture.TargetRows.Count -ne 160)
{
    throw 'The in-memory deterministic fixture count is invalid.'
}

$loadConnectionStringText =
    "Data Source=$sharedMemoryServer;" +
    "Initial Catalog=$targetDatabase;" +
    'Integrated Security=True;' +
    'Encrypt=False;' +
    'TrustServerCertificate=True;' +
    'Application Name=QLHV.RT02.CompleteFixtureLoader;' +
    'Pooling=False;' +
    'Connect Timeout=15'
$connectionString =
    New-Object System.Data.SqlClient.SqlConnectionStringBuilder `
        -ArgumentList $loadConnectionStringText

if ($VerifyOnly.IsPresent)
{
    $proofConnectionStringText =
        "Data Source=$sharedMemoryServer;" +
        "Initial Catalog=$targetDatabase;" +
        'Integrated Security=True;' +
        'Encrypt=False;' +
        'TrustServerCertificate=True;' +
        'Application Name=QLHV.RT02.CompleteFixtureProof;' +
        'ApplicationIntent=ReadOnly;' +
        'Pooling=False;' +
        'Connect Timeout=15'
    $proofConnectionString =
        New-Object System.Data.SqlClient.SqlConnectionStringBuilder `
            -ArgumentList $proofConnectionStringText

    $proofRead1 = Read-FixtureProofOnce `
        -ConnectionString $proofConnectionString.ConnectionString `
        -CatalogQuery $catalogQuery `
        -RepositoryHead $repositoryHead `
        -LoaderArtifactHash $loaderArtifactHash `
        -ExpectedSourceRows $fixture.SourceRows `
        -ExpectedTargetRows $fixture.TargetRows
    $proofRead2 = Read-FixtureProofOnce `
        -ConnectionString $proofConnectionString.ConnectionString `
        -CatalogQuery $catalogQuery `
        -RepositoryHead $repositoryHead `
        -LoaderArtifactHash $loaderArtifactHash `
        -ExpectedSourceRows $fixture.SourceRows `
        -ExpectedTargetRows $fixture.TargetRows

    $proofJson1 = $proofRead1 | ConvertTo-Json -Compress
    $proofJson2 = $proofRead2 | ConvertTo-Json -Compress
    if ($proofJson1 -cne $proofJson2)
    {
        throw 'The two read-only fixture proofs are not byte-stable.'
    }

    [pscustomobject] ([ordered] @{
        StableReadCount = 2
        OtoNoChange = $proofRead1.OtoNoChange
        OtoInsertCandidate = $proofRead1.OtoInsertCandidate
        OtoUpdateCandidate = $proofRead1.OtoUpdateCandidate
        OtoTargetOnlyActive = $proofRead1.OtoTargetOnlyActive
        OtoSoftDeletedBaseline = $proofRead1.OtoSoftDeletedBaseline
        MotoNoChange = $proofRead1.MotoNoChange
        DuplicateActiveGroups = $proofRead1.DuplicateActiveGroups
        DatasetFingerprint = $proofRead1.DatasetFingerprint
        SourceRowsFingerprint = $proofRead1.SourceRowsFingerprint
        TargetRowsFingerprint = $proofRead1.TargetRowsFingerprint
        OtoSchemaFingerprint = $proofRead1.OtoSchemaFingerprint
        MotoSchemaFingerprint = $proofRead1.MotoSchemaFingerprint
        TargetSchemaFingerprint = $proofRead1.TargetSchemaFingerprint
    }) | ConvertTo-Json -Compress
    return
}

$connection = New-Object System.Data.SqlClient.SqlConnection (
    $connectionString.ConnectionString
)
$transaction = $null
$committed = $false
try
{
    $connection.Open()
    $transaction = $connection.BeginTransaction(
        [Data.IsolationLevel]::Serializable
    )

    Invoke-GuardCommand `
        -Connection $connection `
        -Transaction $transaction

    $otoSchemaFingerprint = Get-CatalogFingerprint `
        -Connection $connection `
        -Transaction $transaction `
        -Database $otoDatabase `
        -CatalogQuery $catalogQuery
    $motoSchemaFingerprint = Get-CatalogFingerprint `
        -Connection $connection `
        -Transaction $transaction `
        -Database $motoDatabase `
        -CatalogQuery $catalogQuery
    $targetSchemaFingerprint = Get-CatalogFingerprint `
        -Connection $connection `
        -Transaction $transaction `
        -Database $targetDatabase `
        -CatalogQuery $catalogQuery

    if ($otoSchemaFingerprint -cne $expectedOtoSchemaFingerprint -or
        $motoSchemaFingerprint -cne $expectedMotoSchemaFingerprint -or
        $targetSchemaFingerprint -cne $expectedTargetSchemaFingerprint)
    {
        throw 'The live catalog fingerprint does not match the schema gate.'
    }

    $manifest = Get-DatasetManifest `
        -SourceRows $fixture.SourceRows `
        -TargetRows $fixture.TargetRows `
        -RepositoryHead $repositoryHead `
        -OtoSchemaFingerprint $otoSchemaFingerprint `
        -MotoSchemaFingerprint $motoSchemaFingerprint `
        -TargetSchemaFingerprint $targetSchemaFingerprint `
        -LoaderArtifactHash $loaderArtifactHash
    $datasetFingerprint = Get-Sha256Hex -Value $manifest
    $mappingFingerprint = Get-Sha256Hex -Value (
        'RT02B2-MAPPING-V1-HOTEN-ONLY'
    )
    $sourceSchemaFingerprint = Get-Sha256Hex -Value (
        "OTO=$otoSchemaFingerprint|MOTO=$motoSchemaFingerprint"
    )

    foreach ($row in $fixture.SourceRows)
    {
        Add-SourceRow `
            -Connection $connection `
            -Transaction $transaction `
            -Row $row
    }
    foreach ($row in $fixture.TargetRows)
    {
        Add-TargetRow `
            -Connection $connection `
            -Transaction $transaction `
            -Row $row
    }
    Add-EnvironmentState `
        -Connection $connection `
        -Transaction $transaction `
        -DatasetFingerprint $datasetFingerprint `
        -MappingFingerprint $mappingFingerprint `
        -SourceSchemaFingerprint $sourceSchemaFingerprint `
        -TargetSchemaFingerprint $targetSchemaFingerprint

    Assert-PostInsertState `
        -Connection $connection `
        -Transaction $transaction `
        -DatasetFingerprint $datasetFingerprint `
        -MappingFingerprint $mappingFingerprint `
        -SourceSchemaFingerprint $sourceSchemaFingerprint `
        -TargetSchemaFingerprint $targetSchemaFingerprint

    if ((Get-CatalogFingerprint `
            -Connection $connection `
            -Transaction $transaction `
            -Database $otoDatabase `
            -CatalogQuery $catalogQuery) -cne $otoSchemaFingerprint -or
        (Get-CatalogFingerprint `
            -Connection $connection `
            -Transaction $transaction `
            -Database $motoDatabase `
            -CatalogQuery $catalogQuery) -cne $motoSchemaFingerprint -or
        (Get-CatalogFingerprint `
            -Connection $connection `
            -Transaction $transaction `
            -Database $targetDatabase `
            -CatalogQuery $catalogQuery) -cne $targetSchemaFingerprint)
    {
        throw 'A catalog fingerprint changed inside the fixture transaction.'
    }

    $transaction.Commit()
    $committed = $true

    [pscustomobject] @{
        OtoNoChange = 150
        OtoInsertCandidate = 1
        OtoUpdateCandidate = 1
        OtoTargetOnlyActive = 1
        OtoSoftDeletedBaseline = 3
        MotoNoChange = 5
        DuplicateActiveGroups = 0
        DatasetFingerprint = $datasetFingerprint
        OtoSchemaFingerprint = $otoSchemaFingerprint
        MotoSchemaFingerprint = $motoSchemaFingerprint
        TargetSchemaFingerprint = $targetSchemaFingerprint
    } | ConvertTo-Json -Compress
}
catch
{
    if (-not $committed -and
        $null -ne $transaction -and
        $null -ne $transaction.Connection)
    {
        try
        {
            $transaction.Rollback()
        }
        catch
        {
            throw 'RT02 fixture transaction rollback could not be proven.'
        }
    }
    throw
}
finally
{
    if ($null -ne $transaction)
    {
        $transaction.Dispose()
    }
    $connection.Dispose()
}
