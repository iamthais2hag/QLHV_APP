[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $ServerInstance
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..\..'))
$migrationPath = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $repositoryRoot -ChildPath 'database\patches\20260730_add_vehicle_realtime_mapping.sql'))
$rollbackPath = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $repositoryRoot -ChildPath 'database\patches\20260730_rollback_vehicle_realtime_mapping.sql'))
if (-not (Test-Path -LiteralPath $migrationPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $rollbackPath -PathType Leaf)) {
    throw 'Vehicle migration/rollback artifact is missing.'
}

$databaseName = 'QLHV_VEHICLE_REHEARSAL_{0}_{1}' -f (
    [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')),
    $PID
if ($databaseName -notmatch '^QLHV_VEHICLE_REHEARSAL_[0-9]{14}_[0-9]+$') {
    throw 'Generated rehearsal database name failed its exact safety allowlist.'
}

$masterConnectionString = 'Data Source={0};Initial Catalog=master;Integrated Security=True;TrustServerCertificate=True;Application Name=QLHV Vehicle Migration Rehearsal' -f $ServerInstance

function Invoke-SqlNonQuery {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConnectionString,
        [Parameter(Mandatory = $true)]
        [string] $Sql
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConnectionString,
        [Parameter(Mandatory = $true)]
        [string] $Sql
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SqlBatches {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ConnectionString,
        [Parameter(Mandatory = $true)]
        [string] $Sql
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $batches = [System.Text.RegularExpressions.Regex]::Split(
            $Sql,
            '(?im)^\s*GO\s*(?:--.*)?$')
        foreach ($batch in $batches) {
            if (-not [string]::IsNullOrWhiteSpace($batch)) {
                $command = $connection.CreateCommand()
                $command.CommandTimeout = 120
                $command.CommandText = $batch
                [void]$command.ExecuteNonQuery()
            }
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Convert-ToRehearsalSql {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Sql,
        [Parameter(Mandatory = $true)]
        [Guid] $DatabaseGuid
    )

    return $Sql.
        Replace('USE [QLHV_APP];', ('USE [{0}];' -f $databaseName)).
        Replace("DB_NAME()<>N'QLHV_APP'", ("DB_NAME()<>N'{0}'" -f $databaseName)).
        Replace(
            '9C44B304-8A84-4D0D-9A82-19C7233FF6BB',
            $DatabaseGuid.ToString('D').ToUpperInvariant())
}

$created = $false
$emptyRollbackPassed = $false
$populatedRollbackBlocked = $false
$populatedRowRetained = $false
$databaseGuid = [Guid]::Empty
try {
    Invoke-SqlNonQuery -ConnectionString $masterConnectionString -Sql (
        'CREATE DATABASE [{0}];' -f $databaseName)
    $created = $true
    $databaseGuid = [Guid](Invoke-SqlScalar -ConnectionString $masterConnectionString -Sql (
        "SELECT database_guid FROM sys.database_recovery_status WHERE database_id=DB_ID(N'$databaseName');"))
    if ($databaseGuid -eq [Guid]::Empty) {
        throw 'Disposable rehearsal database GUID could not be resolved.'
    }

    Invoke-SqlNonQuery -ConnectionString $masterConnectionString -Sql @"
USE [$databaseName];
CREATE TABLE dbo.App_XeTap
(
    XeTapId bigint IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_App_XeTap_Rehearsal PRIMARY KEY,
    BienSoXe nvarchar(20) NOT NULL,
    IsDeleted bit NOT NULL
        CONSTRAINT DF_App_XeTap_Rehearsal_IsDeleted DEFAULT(0),
    RowVersion rowversion NOT NULL
);
"@

    $migrationSql = Convert-ToRehearsalSql -Sql (
        Get-Content -LiteralPath $migrationPath -Raw) -DatabaseGuid $databaseGuid
    $rollbackSql = Convert-ToRehearsalSql -Sql (
        Get-Content -LiteralPath $rollbackPath -Raw) -DatabaseGuid $databaseGuid

    Invoke-SqlBatches -ConnectionString $masterConnectionString -Sql $migrationSql
    $schemaReady = [int](Invoke-SqlScalar -ConnectionString $masterConnectionString -Sql @"
USE [$databaseName];
SELECT CASE WHEN
    COL_LENGTH(N'dbo.App_XeTap',N'SourceProfileCode') IS NOT NULL
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeCheckpoint',N'U') IS NOT NULL
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeEvent',N'U') IS NOT NULL
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeManualReview',N'U') IS NOT NULL
    AND NOT EXISTS(SELECT 1 FROM dbo.App_XeTap_RealtimeCheckpoint)
THEN 1 ELSE 0 END;
"@)
    if ($schemaReady -ne 1) {
        throw 'Vehicle migration rehearsal did not create the exact empty schema.'
    }

    Invoke-SqlBatches -ConnectionString $masterConnectionString -Sql $rollbackSql
    $emptyRollbackPassed = [int](Invoke-SqlScalar -ConnectionString $masterConnectionString -Sql @"
USE [$databaseName];
SELECT CASE WHEN
    OBJECT_ID(N'dbo.App_XeTap',N'U') IS NOT NULL
    AND COL_LENGTH(N'dbo.App_XeTap',N'SourceProfileCode') IS NULL
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeCheckpoint',N'U') IS NULL
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeEvent',N'U') IS NULL
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeManualReview',N'U') IS NULL
THEN 1 ELSE 0 END;
"@) -eq 1
    if (-not $emptyRollbackPassed) {
        throw 'Empty vehicle rollback rehearsal failed.'
    }

    Invoke-SqlBatches -ConnectionString $masterConnectionString -Sql $migrationSql
    Invoke-SqlNonQuery -ConnectionString $masterConnectionString -Sql @"
USE [$databaseName];
INSERT INTO dbo.App_XeTap
(
    BienSoXe,IsDeleted,SourceProfileCode,SourceBienSoXe,
    NormalizedBienSoXe,SourceRowHash,SourceTrangThai,SourceLifecycle,
    SourceCtVersion,SourceLastSeenAt,MaCSDT,MaSoGTVT
)
VALUES
(
    N'REHEARSAL-01',0,N'CSDT_OTO',N'REHEARSAL-01',
    N'REHEARSAL01',REPLICATE('a',64),1,N'ACTIVE',
    1,SYSUTCDATETIME(),N'66029',N'66000'
);
"@

    try {
        Invoke-SqlBatches -ConnectionString $masterConnectionString -Sql $rollbackSql
    }
    catch {
        if ($_.Exception.ToString().IndexOf(
                'VEHICLE_ROLLBACK_HAS_SOURCE_DATA_ROLL_FORWARD_REQUIRED',
                [System.StringComparison]::Ordinal) -ge 0) {
            $populatedRollbackBlocked = $true
        }
        else {
            throw
        }
    }

    $populatedRowRetained = [int](Invoke-SqlScalar -ConnectionString $masterConnectionString -Sql @"
USE [$databaseName];
SELECT CASE WHEN
    EXISTS
    (
        SELECT 1 FROM dbo.App_XeTap
        WHERE SourceProfileCode=N'CSDT_OTO'
          AND SourceBienSoXe=N'REHEARSAL-01'
    )
    AND OBJECT_ID(N'dbo.App_XeTap_RealtimeCheckpoint',N'U') IS NOT NULL
THEN 1 ELSE 0 END;
"@) -eq 1
    if (-not $populatedRollbackBlocked -or -not $populatedRowRetained) {
        throw 'Populated vehicle rollback did not fail closed with data retained.'
    }
}
finally {
    if ($created) {
        if ($databaseName -notmatch '^QLHV_VEHICLE_REHEARSAL_[0-9]{14}_[0-9]+$') {
            throw 'Refusing to drop a database outside the rehearsal allowlist.'
        }

        $exists = [int](Invoke-SqlScalar -ConnectionString $masterConnectionString -Sql (
            "SELECT COUNT(1) FROM sys.databases WHERE name=N'$databaseName';"))
        if ($exists -eq 1) {
            Invoke-SqlNonQuery -ConnectionString $masterConnectionString -Sql @"
ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [$databaseName];
"@
        }
    }
}

$remaining = [int](Invoke-SqlScalar -ConnectionString $masterConnectionString -Sql (
    "SELECT COUNT(1) FROM sys.databases WHERE name=N'$databaseName';"))
if ($remaining -ne 0) {
    throw 'Disposable rehearsal database cleanup failed.'
}

[pscustomobject]@{
    ServerInstance = $ServerInstance
    DatabaseName = $databaseName
    DatabaseGuid = $databaseGuid.ToString('D').ToUpperInvariant()
    EmptyMigration = 'PASS'
    EmptyRollback = $(if ($emptyRollbackPassed) { 'PASS' } else { 'FAIL' })
    PopulatedRollback = $(if ($populatedRollbackBlocked) { 'BLOCKED_AS_REQUIRED' } else { 'FAIL' })
    PopulatedDataRetained = $(if ($populatedRowRetained) { 'PASS' } else { 'FAIL' })
    Cleanup = 'PASS'
}
