[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('RT02B-OPERATOR-APPROVAL-20260727-01')]
    [string] $OwnerApprovalId,

    [Parameter(Mandatory = $true)]
    [switch] $Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serverIdentity = 'CSDLTTTC\QLHVRT02'
$sharedMemoryServer = 'lpc:CSDLTTTC\QLHVRT02'
$targetDatabase = 'QLHV_RT02_TARGET_TEST'
$environmentId = 'RT02B0-CSDLTTTC-QLHVRT02-20260727-01'
$approvalExpiresAtUtc = '2026-07-31T16:59:59Z'

if (-not $Execute.IsPresent)
{
    throw 'The explicit -Execute switch is required.'
}

function New-IsolatedConnection
{
    $connectionString =
        "Data Source=$sharedMemoryServer;" +
        "Initial Catalog=$targetDatabase;" +
        'Integrated Security=True;' +
        'Encrypt=False;' +
        'TrustServerCertificate=True;' +
        'Application Name=QLHV.RT02.RealDeadlockProbe;' +
        'Pooling=False;' +
        'Connect Timeout=15'
    $builder = New-Object `
        System.Data.SqlClient.SqlConnectionStringBuilder `
        -ArgumentList $connectionString
    return New-Object `
        System.Data.SqlClient.SqlConnection `
        -ArgumentList $builder.ConnectionString
}

function Add-IdentityParameter
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlCommand] $Command,

        [Parameter(Mandatory = $true)]
        [string] $Identity
    )

    $Command.Parameters.Add(
        '@IdentityHmac',
        [Data.SqlDbType]::Char,
        64
    ).Value = $Identity
}

function Assert-IsolatedRoute
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.CommandTimeout = 30
        $command.CommandText = @'
SET NOCOUNT ON;

IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> @ExpectedServer
   OR CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion')) <> 16
   OR CONVERT(nvarchar(128), SERVERPROPERTY(N'Edition')) NOT LIKE N'%Developer%'
    THROW 528100, 'ISOLATED_DATABASE_IDENTITY_REJECTED: server.', 1;

IF DB_NAME() <> N'QLHV_RT02_TARGET_TEST'
   OR DB_ID() <> 7
   OR
   (
       SELECT database_guid
       FROM sys.database_recovery_status
       WHERE database_id = DB_ID()
   ) <> CONVERT(uniqueidentifier, N'F7BAC56F-8329-47AB-A17C-A0D592ADD484')
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.databases AS databaseItem
       INNER JOIN sys.database_recovery_status AS recovery
           ON recovery.database_id = databaseItem.database_id
       WHERE
           (
               databaseItem.name = N'QLHV_RT02_OTO_TEST'
               AND databaseItem.database_id = 5
               AND recovery.database_guid =
                   CONVERT(
                       uniqueidentifier,
                       N'FEE7CD94-A717-4E73-89F0-0FBFF71D1789'
                   )
           )
           OR
           (
               databaseItem.name = N'QLHV_RT02_MOTO_TEST'
               AND databaseItem.database_id = 6
               AND recovery.database_guid =
                   CONVERT(
                       uniqueidentifier,
                       N'6D8101F9-07AB-4F0F-B378-29ED084F7B2A'
                   )
           )
           OR
           (
               databaseItem.name = N'QLHV_RT02_TARGET_TEST'
               AND databaseItem.database_id = 7
               AND recovery.database_guid =
                   CONVERT(
                       uniqueidentifier,
                       N'F7BAC56F-8329-47AB-A17C-A0D592ADD484'
                   )
           )
   ) <> 3
    THROW 528101, 'ISOLATED_DATABASE_IDENTITY_REJECTED: target.', 1;

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
   OR EXISTS (SELECT 1 FROM sys.synonyms)
   OR EXISTS (SELECT 1 FROM sys.external_data_sources)
   OR EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_OTO_TEST].sys.external_data_sources
   )
   OR EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_MOTO_TEST].sys.external_data_sources
   )
    THROW 528102, 'ISOLATED_DATABASE_IDENTITY_REJECTED: route.', 1;

IF
(
    SELECT COUNT_BIG(*)
    FROM sys.extended_properties
    WHERE class = 0
      AND
      (
          (name = N'RT02_ISOLATED_ENVIRONMENT_ID'
           AND CONVERT(nvarchar(128), value) = @EnvironmentId)
          OR
          (name = N'RT02_OWNER_APPROVAL_ID'
           AND CONVERT(nvarchar(128), value) = @ApprovalId)
          OR
          (name = N'RT02_DATASET_MODE'
           AND CONVERT(nvarchar(128), value) = N'SYNTHETIC')
          OR
          (name = N'RT02_PRODUCTION_ROUTE_ALLOWED'
           AND CONVERT(nvarchar(128), value) = N'FALSE')
          OR
          (name = N'RT02_EXPIRES_AT_UTC'
           AND CONVERT(nvarchar(128), value) = @ExpiresAtUtc)
      )
) <> 5
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.extended_properties
       WHERE class = 0
         AND name LIKE N'RT02[_]%'
   ) <> 5
   OR TRY_CONVERT(datetime2(0), @ExpiresAtUtc, 127) <= SYSUTCDATETIME()
    THROW 528103, 'ISOLATED_DATABASE_IDENTITY_REJECTED: markers.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE name IN
    (
        N'QLHV_RT02_OTO_TEST',
        N'QLHV_RT02_MOTO_TEST',
        N'QLHV_RT02_TARGET_TEST'
    )
      AND is_read_committed_snapshot_on <> 0
)
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.change_tracking_databases
   ) <> 2
   OR
   (
       SELECT COUNT_BIG(*)
       FROM sys.change_tracking_databases
       WHERE database_id IN (5, 6)
         AND retention_period = 2
         AND retention_period_units_desc = N'DAYS'
         AND is_auto_cleanup_on = 1
   ) <> 2
   OR
   (
       SELECT COUNT_BIG(*)
       FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables
   ) <> 2
   OR
   (
       SELECT COUNT_BIG(*)
       FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables
   ) <> 2
   OR EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_OTO_TEST].sys.change_tracking_tables
       WHERE object_id NOT IN
       (
           OBJECT_ID(N'QLHV_RT02_OTO_TEST.dbo.NguoiLX'),
           OBJECT_ID(N'QLHV_RT02_OTO_TEST.dbo.NguoiLX_HoSo')
       )
          OR is_track_columns_updated_on <> 1
   )
   OR EXISTS
   (
       SELECT 1
       FROM [QLHV_RT02_MOTO_TEST].sys.change_tracking_tables
       WHERE object_id NOT IN
       (
           OBJECT_ID(N'QLHV_RT02_MOTO_TEST.dbo.NguoiLX'),
           OBJECT_ID(N'QLHV_RT02_MOTO_TEST.dbo.NguoiLX_HoSo')
       )
          OR is_track_columns_updated_on <> 1
   )
   OR
   (
       SELECT COUNT_BIG(*)
       FROM [QLHV_RT02_TARGET_TEST].sys.change_tracking_tables
   ) <> 0
   OR
   (
       SELECT snapshot_isolation_state
       FROM sys.databases
       WHERE database_id = 5
   ) <> 1
   OR
   (
       SELECT snapshot_isolation_state
       FROM sys.databases
       WHERE database_id = 6
   ) <> 1
   OR
   (
       SELECT snapshot_isolation_state
       FROM sys.databases
       WHERE database_id = 7
   ) <> 0
    THROW 528104, 'RT02 feature gate is not active.', 1;

IF (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner) <> 1372
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
       WHERE Active = 1 AND SoftDeleted = 0) <> 1369
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02Learner
       WHERE Active = 0 AND SoftDeleted = 1) <> 3
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyMarker) <> 10
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02ApplyCheckpoint) <> 10
   OR (SELECT COUNT_BIG(*) FROM dbo.Rt02ManualReviewEvidence) <> 2
    THROW 528105, 'RT02 post-harness state is not exact.', 1;

SELECT @@SPID;
'@
        $command.Parameters.Add(
            '@ExpectedServer',
            [Data.SqlDbType]::NVarChar,
            128
        ).Value = $serverIdentity
        $command.Parameters.Add(
            '@EnvironmentId',
            [Data.SqlDbType]::NVarChar,
            128
        ).Value = $environmentId
        $command.Parameters.Add(
            '@ApprovalId',
            [Data.SqlDbType]::NVarChar,
            128
        ).Value = $OwnerApprovalId
        $command.Parameters.Add(
            '@ExpiresAtUtc',
            [Data.SqlDbType]::NVarChar,
            128
        ).Value = $approvalExpiresAtUtc
        return [int] $command.ExecuteScalar()
    }
    finally
    {
        $command.Dispose()
    }
}

function Get-DeadlockRows
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.CommandTimeout = 30
        $command.CommandText = @'
SELECT TOP (2) IdentityHmac
FROM dbo.Rt02Learner
WHERE ScenarioCode = 'CORE'
  AND DatasetRole = 'NO_CHANGE'
  AND Active = 1
  AND SoftDeleted = 0
ORDER BY IdentityHmac;
'@
        $reader = $command.ExecuteReader()
        try
        {
            $identities = New-Object 'System.Collections.Generic.List[string]'
            while ($reader.Read())
            {
                $identities.Add($reader.GetString(0))
            }
            if ($identities.Count -ne 2)
            {
                throw 'The deadlock probe did not resolve exactly two rows.'
            }
            return $identities.ToArray()
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

function Get-RowEvidence
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [string[]] $Identities
    )

    $command = $Connection.CreateCommand()
    try
    {
        $command.CommandTimeout = 30
        $command.CommandText = @'
SELECT
    IdentityHmac,
    HoTen,
    MappedHash,
    QlhvOwnedHash,
    WorkflowState,
    NotesHash,
    PhotoState,
    CONVERT(int, Active),
    CONVERT(int, SoftDeleted),
    COALESCE(CONVERT(nvarchar(33), UpdatedAtUtc, 126), N'<NULL>')
FROM dbo.Rt02Learner
WHERE IdentityHmac IN (@IdentityA, @IdentityB)
ORDER BY IdentityHmac;
'@
        $command.Parameters.Add(
            '@IdentityA',
            [Data.SqlDbType]::Char,
            64
        ).Value = $Identities[0]
        $command.Parameters.Add(
            '@IdentityB',
            [Data.SqlDbType]::Char,
            64
        ).Value = $Identities[1]
        $reader = $command.ExecuteReader()
        try
        {
            $lines = New-Object 'System.Collections.Generic.List[string]'
            while ($reader.Read())
            {
                $lines.Add(
                    (
                        0..9 |
                            ForEach-Object { [string] $reader.GetValue($_) }
                    ) -join '|'
                )
            }
            if ($lines.Count -ne 2)
            {
                throw 'The deadlock row evidence count changed.'
            }
            return [string]::Join("`n", $lines)
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

function Set-SessionLockPolicy
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [ValidateSet('HIGH', 'LOW', 'NORMAL')]
        [string] $Priority
    )

    $command = New-Object System.Data.SqlClient.SqlCommand (
        "SET DEADLOCK_PRIORITY $Priority; SET LOCK_TIMEOUT 10000;",
        $Connection,
        $Transaction
    )
    try
    {
        [void] $command.ExecuteNonQuery()
    }
    finally
    {
        $command.Dispose()
    }
}

function Lock-Row
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [string] $Identity
    )

    $command = New-Object System.Data.SqlClient.SqlCommand (
        @'
SELECT IdentityHmac
FROM dbo.Rt02Learner WITH (UPDLOCK, HOLDLOCK)
WHERE IdentityHmac = @IdentityHmac;
'@,
        $Connection,
        $Transaction
    )
    try
    {
        $command.CommandTimeout = 15
        Add-IdentityParameter -Command $command -Identity $Identity
        $observed = [string] $command.ExecuteScalar()
        if ($observed -cne $Identity)
        {
            throw 'The expected synthetic row lock was not acquired.'
        }
    }
    finally
    {
        $command.Dispose()
    }
}

function New-CrossLockCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlConnection] $Connection,

        [Parameter(Mandatory = $true)]
        [System.Data.SqlClient.SqlTransaction] $Transaction,

        [Parameter(Mandatory = $true)]
        [string] $Identity
    )

    $command = New-Object System.Data.SqlClient.SqlCommand (
        @'
SELECT IdentityHmac
FROM dbo.Rt02Learner WITH (UPDLOCK, HOLDLOCK)
WHERE IdentityHmac = @IdentityHmac;
'@,
        $Connection,
        $Transaction
    )
    $command.CommandTimeout = 15
    Add-IdentityParameter -Command $command -Identity $Identity
    return $command
}

function Close-Transaction
{
    param(
        [AllowNull()]
        [System.Data.SqlClient.SqlTransaction] $Transaction
    )

    if ($null -eq $Transaction)
    {
        return
    }
    try
    {
        if ($null -ne $Transaction.Connection)
        {
            $Transaction.Rollback()
        }
    }
    catch
    {
        # A SQL deadlock victim is already rolled back by the engine.
    }
    finally
    {
        $Transaction.Dispose()
    }
}

$connectionA = New-IsolatedConnection
$connectionB = New-IsolatedConnection
$transactionA = $null
$transactionB = $null
$crossA = $null
$crossB = $null
$timer = [Diagnostics.Stopwatch]::StartNew()
try
{
    $connectionA.Open()
    $connectionB.Open()
    $spidA = Assert-IsolatedRoute -Connection $connectionA
    $spidB = Assert-IsolatedRoute -Connection $connectionB
    if ($spidA -eq $spidB)
    {
        throw 'The real deadlock probe requires two distinct SQL sessions.'
    }

    $identities = Get-DeadlockRows -Connection $connectionA
    $beforeEvidence = Get-RowEvidence `
        -Connection $connectionA `
        -Identities $identities

    $transactionA = $connectionA.BeginTransaction(
        [Data.IsolationLevel]::Serializable
    )
    $transactionB = $connectionB.BeginTransaction(
        [Data.IsolationLevel]::Serializable
    )
    Set-SessionLockPolicy `
        -Connection $connectionA `
        -Transaction $transactionA `
        -Priority 'HIGH'
    Set-SessionLockPolicy `
        -Connection $connectionB `
        -Transaction $transactionB `
        -Priority 'LOW'

    Lock-Row `
        -Connection $connectionA `
        -Transaction $transactionA `
        -Identity $identities[0]
    Lock-Row `
        -Connection $connectionB `
        -Transaction $transactionB `
        -Identity $identities[1]

    $crossA = New-CrossLockCommand `
        -Connection $connectionA `
        -Transaction $transactionA `
        -Identity $identities[1]
    $crossB = New-CrossLockCommand `
        -Connection $connectionB `
        -Transaction $transactionB `
        -Identity $identities[0]
    $taskA = $crossA.ExecuteScalarAsync()
    $taskB = $crossB.ExecuteScalarAsync()

    try
    {
        [void] [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]] @($taskA, $taskB),
            20000
        )
    }
    catch [AggregateException]
    {
        # Individual task state is asserted below.
    }

    if (-not $taskA.IsCompleted -or -not $taskB.IsCompleted)
    {
        throw 'The real SQL deadlock probe exceeded its bounded timeout.'
    }

    $faultedTasks = @(
        @($taskA, $taskB) |
            Where-Object { $_.IsFaulted }
    )
    $completedTasks = @(
        @($taskA, $taskB) |
            Where-Object {
                $_.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion
            }
    )
    if ($faultedTasks.Count -ne 1 -or $completedTasks.Count -ne 1)
    {
        throw 'The real SQL deadlock did not produce one victim and one survivor.'
    }

    $deadlockErrors = @(
        $faultedTasks[0].Exception.Flatten().InnerExceptions |
            Where-Object {
                $_ -is [System.Data.SqlClient.SqlException] -and
                $_.Number -eq 1205
            }
    )
    if ($deadlockErrors.Count -ne 1)
    {
        throw 'The real SQL deadlock victim did not report SqlException 1205.'
    }

    $victimSession =
        if ($taskA.IsFaulted) { 'A' } else { 'B' }
    $survivorValue = [string] $completedTasks[0].Result
    if ($survivorValue -notin $identities)
    {
        throw 'The real SQL deadlock survivor did not acquire the cross-row lock.'
    }
}
finally
{
    if ($null -ne $crossA)
    {
        $crossA.Dispose()
    }
    if ($null -ne $crossB)
    {
        $crossB.Dispose()
    }
    Close-Transaction -Transaction $transactionA
    Close-Transaction -Transaction $transactionB
    $connectionA.Dispose()
    $connectionB.Dispose()
}

$retryConnection = New-IsolatedConnection
$retryTransaction = $null
try
{
    $retryConnection.Open()
    $retrySpid = Assert-IsolatedRoute -Connection $retryConnection
    $retryTransaction = $retryConnection.BeginTransaction(
        [Data.IsolationLevel]::Serializable
    )
    Set-SessionLockPolicy `
        -Connection $retryConnection `
        -Transaction $retryTransaction `
        -Priority 'NORMAL'
    Lock-Row `
        -Connection $retryConnection `
        -Transaction $retryTransaction `
        -Identity $identities[0]
    Lock-Row `
        -Connection $retryConnection `
        -Transaction $retryTransaction `
        -Identity $identities[1]
    $retryTransaction.Rollback()
    $retryTransaction.Dispose()
    $retryTransaction = $null

    $afterEvidence = Get-RowEvidence `
        -Connection $retryConnection `
        -Identities $identities
    if ($afterEvidence -cne $beforeEvidence)
    {
        throw 'The real SQL deadlock probe changed learner evidence.'
    }
}
finally
{
    Close-Transaction -Transaction $retryTransaction
    $retryConnection.Dispose()
}

$timer.Stop()
[pscustomobject] ([ordered] @{
    Status = 'REAL_SQL_DEADLOCK_1205_AND_RETRY_VERIFIED'
    ServerIdentity = $serverIdentity
    EnvironmentId = $environmentId
    SessionA = $spidA
    SessionB = $spidB
    VictimSession = $victimSession
    DeadlockErrorNumber = 1205
    RetrySession = $retrySpid
    RetrySucceeded = $true
    BusinessMutationCount = 0
    RowEvidencePreserved = $true
    DurationMs = $timer.Elapsed.TotalMilliseconds
}) | ConvertTo-Json -Compress
