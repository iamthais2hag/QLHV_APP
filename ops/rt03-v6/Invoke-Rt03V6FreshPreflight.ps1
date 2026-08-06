[CmdletBinding()]
param(
    [string]$SqlServer = 'CSDLTTTC',
    [string]$TargetDatabase = 'QLHV_APP',
    [string]$OtoDatabase = 'CSDL_OTO',
    [string]$MotoDatabase = 'CSDL_MOTO',
    [string]$ApprovedNtpPeer = 'time.windows.com,0x9',
    [int]$SampleCount = 5,
    [ValidateSet('Disabled', 'Diagnostic', 'Required')]
    [string]$ApiContractMode = 'Diagnostic',
    [string]$RuntimeStatusUri =
        'http://127.0.0.1:8088/api/system/runtime-status',
    [string]$TimePreflightExecutable,
    [string]$OutputPath,
    [switch]$ProbeWriterLease,
    [switch]$ExecutionReady
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedTargetDatabaseId = 12
$expectedTargetDatabaseGuid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
$expectedWorkerExecutable =
    'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe'
$workerServiceName = 'QLHV_APP_RealtimeWorker'
$warningSkewMilliseconds = 2000
$expectedTimeContractVersion = '1.0'
$packagedTimePreflight = Join-Path $PSScriptRoot `
    'tools\time-health-preflight\QLHV.TimeHealth.Preflight.exe'
$workspaceTimePreflight =
    'D:\QLHV_APP\server\QLHV.TimeHealth.Preflight\bin\Release\net8.0\QLHV.TimeHealth.Preflight.exe'
$writerResources = @(
    'QLHV:CSDT_AUTO_SYNC',
    'QLHV:CSDT_OPERATIONS:OTO',
    'QLHV:CSDT_OPERATIONS:MOTO'
)
$sourceTables = @(
    'DM_DVHC',
    'DM_HangDT',
    'GiaoVien',
    'KhoaHoc',
    'KhoaHoc_GiaoVien',
    'NguoiLX',
    'NguoiLX_HoSo',
    'XeTap'
)

if ($SampleCount -lt 3 -or $SampleCount -gt 10) {
    throw 'SampleCount must be between 3 and 10.'
}
if ([string]::IsNullOrWhiteSpace($TimePreflightExecutable)) {
    $TimePreflightExecutable = if (
        Test-Path -LiteralPath $packagedTimePreflight -PathType Leaf) {
        $packagedTimePreflight
    } else {
        $workspaceTimePreflight
    }
}
if (-not (Test-Path -LiteralPath $TimePreflightExecutable -PathType Leaf)) {
    throw "Shared TimeHealth preflight executable not found: $TimePreflightExecutable"
}

function New-SqlConnection {
    param([Parameter(Mandatory)][string]$Database)
    $builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = $SqlServer
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Application Name'] = 'QLHV RT03 V6 ReadOnly Preflight'
    $builder['Connect Timeout'] = 15
    $builder['Encrypt'] = $false
    $builder['Pooling'] = $false
    return [Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
}

function Invoke-SqlTable {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Sql
    )
    $connection = New-SqlConnection -Database $Database
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 60
        $command.CommandText =
            "SET NOCOUNT ON; SET TRANSACTION ISOLATION LEVEL READ COMMITTED;`n$Sql"
        $table = [Data.DataTable]::new()
        $reader = $command.ExecuteReader()
        try {
            $table.Load($reader)
        }
        finally {
            $reader.Dispose()
            $command.Dispose()
        }
        return ,$table
    }
    finally {
        $connection.Dispose()
    }
}

function Get-LineValue {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Name
    )
    $match = [regex]::Match(
        $Text,
        "(?m)^$([regex]::Escape($Name)):\s*(?<value>.+?)\s*$")
    if ($match.Success) {
        return $match.Groups['value'].Value.Trim()
    }
    return $null
}

function Get-PhaseMilliseconds {
    param([string]$Text)
    $match = [regex]::Match(
        $Text,
        '(?m)^Phase Offset:\s*(?<value>[+-]?\d+(?:\.\d+)?)s\s*$')
    if (-not $match.Success) {
        return $null
    }
    return [double]::Parse(
        $match.Groups['value'].Value,
        [Globalization.CultureInfo]::InvariantCulture) * 1000
}

function Get-LeadingInteger {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }
    $match = [regex]::Match($Value, '^\s*(?<value>\d+)')
    if (-not $match.Success) {
        return $null
    }
    return [int]$match.Groups['value'].Value
}

function Convert-LocalSyncTimeToUtc {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -eq 'unspecified') {
        return $null
    }
    foreach ($cultureName in @('vi-VN', 'en-US', 'en-GB')) {
        $parsed = [datetime]::MinValue
        $culture = [Globalization.CultureInfo]::GetCultureInfo($cultureName)
        if ([datetime]::TryParse(
                $Value,
                $culture,
                [Globalization.DateTimeStyles]::AllowWhiteSpaces,
                [ref]$parsed)) {
            return [DateTimeOffset]::new(
                [datetime]::SpecifyKind($parsed, [DateTimeKind]::Local)
            ).ToUniversalTime()
        }
    }
    return $null
}

function Get-CommandEvidence {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $output = & w32tm @Arguments 2>&1 | Out-String
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = $output.Trim()
    }
}

function Invoke-TimeHealthContract {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('standalone', 'api')]
        [string]$Mode
    )

    $arguments = @(
        '--mode', $Mode,
        '--sql-server', $SqlServer,
        '--database', $TargetDatabase,
        '--api-uri', $RuntimeStatusUri,
        '--timeout-seconds', '15',
        '--maximum-age-seconds', '30'
    )
    $raw = & $TimePreflightExecutable @arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $contract = $null
    $parseFailure = $null
    try {
        $contract = $raw | ConvertFrom-Json
    }
    catch {
        $parseFailure = $_.Exception.Message
    }
    return [pscustomobject]@{
        Mode = $Mode
        ExitCode = $exitCode
        Contract = $contract
        ParseFailure = $parseFailure
    }
}

function Test-WriterLease {
    $connection = New-SqlConnection -Database $TargetDatabase
    $acquired = [Collections.Generic.List[string]]::new()
    try {
        $connection.Open()
        foreach ($resource in $writerResources) {
            $command = $connection.CreateCommand()
            try {
                $command.CommandText = @'
DECLARE @result int;
EXEC @result=sys.sp_getapplock
    @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Session',
    @LockTimeout=0,@DbPrincipal=N'public';
SELECT @result;
'@
                [void]$command.Parameters.Add(
                    '@Resource',
                    [Data.SqlDbType]::NVarChar,
                    255)
                $command.Parameters['@Resource'].Value = $resource
                $result = [int]$command.ExecuteScalar()
                if ($result -lt 0) {
                    return [pscustomobject]@{
                        Available = $false
                        FailedResource = $resource
                        LockResult = $result
                    }
                }
                $acquired.Add($resource)
            }
            finally {
                $command.Dispose()
            }
        }
        return [pscustomobject]@{
            Available = $true
            FailedResource = $null
            LockResult = 0
        }
    }
    finally {
        for ($index = $acquired.Count - 1; $index -ge 0; $index--) {
            try {
                $release = $connection.CreateCommand()
                $release.CommandText = @'
EXEC sys.sp_releaseapplock
    @Resource=@Resource,@LockOwner=N'Session',@DbPrincipal=N'public';
'@
                [void]$release.Parameters.Add(
                    '@Resource',
                    [Data.SqlDbType]::NVarChar,
                    255)
                $release.Parameters['@Resource'].Value = $acquired[$index]
                [void]$release.ExecuteNonQuery()
                $release.Dispose()
            }
            catch {
                [Data.SqlClient.SqlConnection]::ClearPool($connection)
            }
        }
        $connection.Dispose()
    }
}

function Get-TargetSnapshot {
    return Invoke-SqlTable -Database $TargetDatabase -Sql @'
SELECT
    DB_NAME() DatabaseName,
    DB_ID() DatabaseId,
    CONVERT(nvarchar(36),database_guid) DatabaseGuid,
    CONVERT(datetime2(7),SYSUTCDATETIME()) SqlUtc,
    (SELECT MAX(valueRow.ObservedUtc)
     FROM
     (
        SELECT MAX(LastHeartbeatUtc) ObservedUtc
        FROM dbo.App_QlhvDirectRealtimeWorkerState
        UNION ALL
        SELECT MAX(StartedAtUtc)
        FROM dbo.App_QlhvDirectRealtimeWorkerState
        UNION ALL
        SELECT MAX(CompletedAtUtc)
        FROM dbo.App_QlhvDirectRealtimeCycleHistory
        UNION ALL
        SELECT MAX(CommittedAtUtc)
        FROM dbo.App_QlhvDirectRealtimeApplyMarker
        UNION ALL
        SELECT MAX(UpdatedAtUtc)
        FROM dbo.App_QlhvSyncOperationHistory
     ) valueRow) DurableUtc,
    (SELECT COUNT(1)
     FROM dbo.App_QlhvAutoSyncRun
     WHERE ActiveSlot=1
       AND Status IN(N'QUEUED',N'RUNNING')
       AND CompletedAtUtc IS NULL) ActiveAutoSyncRuns,
    (SELECT COUNT(1)
     FROM dbo.App_QlhvSyncOperationHistory
     WHERE Status IN(N'QUEUED',N'RUNNING')) ActiveOperations,
    CASE WHEN OBJECT_ID(N'dbo.App_AssignmentOperation',N'U') IS NULL
         THEN 0 ELSE 1 END AssignmentSchemaPresent,
    CASE WHEN COL_LENGTH(N'dbo.App_XeTap',N'SourceProfileCode') IS NULL
         THEN 0 ELSE 1 END VehicleMappingPresent,
    CASE WHEN OBJECT_ID(N'dbo.App_Rt03FullConvergenceSession',N'U') IS NULL
         THEN 0 ELSE 1 END RecoverySchemaPresent,
    CASE WHEN OBJECT_ID(N'dbo.UQ_App_KhoaHoc_MaKhoa',N'UQ') IS NULL
         THEN 0 ELSE 1 END GlobalCourseUniquePresent,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc')
          AND name=N'UX_App_KhoaHoc_SourceIdentity'
          AND is_unique=1 AND is_disabled=0
    ) THEN 1 ELSE 0 END CourseSourceIdentityIndexPresent
    ,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc')
          AND name=N'IX_App_KhoaHoc_SourceProfile_MaKhoa'
          AND is_disabled=0
    ) THEN 1 ELSE 0 END ProfileCourseLookupPresent
FROM sys.database_recovery_status
WHERE database_id=DB_ID();
'@
}

function Get-VehicleTargetDomains {
    return Invoke-SqlTable -Database $TargetDatabase -Sql @'
WITH Profiles AS
(
    SELECT value SourceProfileCode
    FROM (VALUES(N'CSDT_OTO'),(N'CSDT_MOTO')) item(value)
),
DuplicateGroups AS
(
    SELECT SourceProfileCode,COUNT_BIG(*) DuplicateGroups
    FROM
    (
        SELECT SourceProfileCode,SourceBienSoXe
        FROM dbo.App_XeTap
        WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')
          AND SourceBienSoXe IS NOT NULL
        GROUP BY SourceProfileCode,SourceBienSoXe
        HAVING COUNT_BIG(*)>1
    ) duplicateRow
    GROUP BY SourceProfileCode
)
SELECT profileRow.SourceProfileCode,N'VEHICLE' DomainCode,
       COUNT_BIG(vehicleRow.SourceBienSoXe) TargetRows,
       SUM(CASE WHEN vehicleRow.IsDeleted=0 THEN CONVERT(bigint,1) ELSE 0 END)
           ActiveRows,
       COALESCE(MAX(duplicateRow.DuplicateGroups),0) DuplicateGroups,
       N'EXACT_PROFILE_SOURCE_IDENTITY' IdentityContract,
       N'SOURCE_OWNED_FIELDS_ONLY; QLHV_ASSIGNMENT_FIELDS_PRESERVED'
           OwnershipContract,
       N'MISSING_OR_INACTIVE; ASSIGNED_VEHICLE_MANUAL_REVIEW; NO_HARD_DELETE'
           DeleteContract
FROM Profiles profileRow
LEFT JOIN dbo.App_XeTap vehicleRow
  ON vehicleRow.SourceProfileCode=profileRow.SourceProfileCode
 AND vehicleRow.SourceBienSoXe IS NOT NULL
LEFT JOIN DuplicateGroups duplicateRow
  ON duplicateRow.SourceProfileCode=profileRow.SourceProfileCode
GROUP BY profileRow.SourceProfileCode
ORDER BY profileRow.SourceProfileCode;
'@
}

function Get-Checkpoints {
    return Invoke-SqlTable -Database $TargetDatabase -Sql @'
SELECT SourceProfileCode,
       SourceChangeTrackingVersion CheckpointVersion,
       CONVERT(nvarchar(36),SourceDatabaseGuid) SourceDatabaseGuid,
       PublishedAtUtc
FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
WHERE Mode=N'DIRECT_REALTIME_APPLY'
  AND EnvironmentId=N'PRODUCTION'
ORDER BY SourceProfileCode;
'@
}

function Get-TargetDomains {
    return Invoke-SqlTable -Database $TargetDatabase -Sql @'
WITH DomainRows AS
(
    SELECT N'COURSE' DomainCode,SourceProfileCode,
           CONVERT(nvarchar(100),SourceMaKhoaHoc) ExactIdentity,
           IsDeleted
    FROM dbo.App_KhoaHoc
    WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')
      AND SourceMaKhoaHoc IS NOT NULL
    UNION ALL
    SELECT N'TEACHER',SourceProfileCode,
           CONVERT(nvarchar(100),SourceMaGV),IsDeleted
    FROM dbo.App_GiaoVien
    WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')
      AND SourceMaGV IS NOT NULL
    UNION ALL
    SELECT N'LEARNER',SourceProfileCode,
           CONVERT(nvarchar(100),SourceMaDK),IsDeleted
    FROM dbo.App_HocVien
    WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')
      AND SourceMaDK IS NOT NULL
    UNION ALL
    SELECT N'RELATION',SourceProfileCode,
           CONVERT(nvarchar(100),SourceMaLichLV),IsDeleted
    FROM dbo.App_KhoaHoc_GiaoVien
    WHERE SourceProfileCode IN(N'CSDT_OTO',N'CSDT_MOTO')
      AND SourceMaLichLV IS NOT NULL
),
DuplicateGroups AS
(
    SELECT DomainCode,SourceProfileCode,COUNT_BIG(*) DuplicateGroups
    FROM
    (
        SELECT DomainCode,SourceProfileCode,ExactIdentity
        FROM DomainRows
        GROUP BY DomainCode,SourceProfileCode,ExactIdentity
        HAVING COUNT_BIG(*)>1
    ) duplicateRow
    GROUP BY DomainCode,SourceProfileCode
),
Profiles AS
(
    SELECT value SourceProfileCode
    FROM (VALUES(N'CSDT_OTO'),(N'CSDT_MOTO')) item(value)
),
Domains AS
(
    SELECT value DomainCode
    FROM (VALUES(N'COURSE'),(N'TEACHER'),(N'LEARNER'),(N'RELATION')) item(value)
)
SELECT profileRow.SourceProfileCode,domainRow.DomainCode,
       COUNT_BIG(entityRow.ExactIdentity) TargetRows,
       SUM(CASE WHEN entityRow.IsDeleted=0 THEN CONVERT(bigint,1) ELSE 0 END)
           ActiveRows,
       COALESCE(MAX(duplicateRow.DuplicateGroups),0) DuplicateGroups,
       N'EXACT_PROFILE_SOURCE_IDENTITY' IdentityContract,
       CASE domainRow.DomainCode
         WHEN N'TEACHER' THEN N'TRAINING_SOURCE_OWNED_DOSSIER_QLHV_OWNED'
         WHEN N'LEARNER' THEN N'SOURCE_PROJECTION_ASSIGNMENT_QLHV_OWNED'
         ELSE N'SOURCE_OWNED_PROJECTION_QLHV_FIELDS_PRESERVED'
       END OwnershipContract,
       N'SOFT_DELETE_OR_INACTIVE_ONLY' DeleteContract
FROM Profiles profileRow
CROSS JOIN Domains domainRow
LEFT JOIN DomainRows entityRow
  ON entityRow.SourceProfileCode=profileRow.SourceProfileCode
 AND entityRow.DomainCode=domainRow.DomainCode
LEFT JOIN DuplicateGroups duplicateRow
  ON duplicateRow.SourceProfileCode=profileRow.SourceProfileCode
 AND duplicateRow.DomainCode=domainRow.DomainCode
GROUP BY profileRow.SourceProfileCode,domainRow.DomainCode
ORDER BY profileRow.SourceProfileCode,domainRow.DomainCode;
'@
}

function Get-SourceAudit {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Profile,
        [Parameter(Mandatory)][long]$Checkpoint
    )
    $values = ($sourceTables | ForEach-Object {
        "(N'$_')"
    }) -join ','
    $sql = @"
WITH Expected(TableName) AS
(
    SELECT value FROM (VALUES $values) item(value)
)
SELECT N'$Profile' SourceProfileCode,
       DB_NAME() DatabaseName,
       CONVERT(nvarchar(36),identityRow.database_guid) DatabaseGuid,
       expected.TableName,
       CONVERT(bit,CASE WHEN tableRow.object_id IS NULL THEN 0 ELSE 1 END)
           TableExists,
       CONVERT(bit,CASE WHEN tracking.object_id IS NULL THEN 0 ELSE 1 END)
           ChangeTrackingEnabled,
       CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) CurrentVersion,
       CONVERT(bigint,CASE WHEN tracking.object_id IS NULL THEN NULL
          ELSE CHANGE_TRACKING_MIN_VALID_VERSION(tableRow.object_id) END)
           MinimumValidVersion,
       CONVERT(bigint,$Checkpoint) CommittedCheckpoint,
       CONVERT(bigint,CASE expected.TableName
          WHEN N'DM_DVHC' THEN (SELECT COUNT_BIG(*) FROM dbo.DM_DVHC)
          WHEN N'DM_HangDT' THEN (SELECT COUNT_BIG(*) FROM dbo.DM_HangDT)
          WHEN N'GiaoVien' THEN (SELECT COUNT_BIG(*) FROM dbo.GiaoVien)
          WHEN N'KhoaHoc' THEN (SELECT COUNT_BIG(*) FROM dbo.KhoaHoc)
          WHEN N'KhoaHoc_GiaoVien' THEN
              (SELECT COUNT_BIG(*) FROM dbo.KhoaHoc_GiaoVien)
          WHEN N'NguoiLX' THEN (SELECT COUNT_BIG(*) FROM dbo.NguoiLX)
          WHEN N'NguoiLX_HoSo' THEN (SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo)
          WHEN N'XeTap' THEN (SELECT COUNT_BIG(*) FROM dbo.XeTap)
       END) SourceRowCount
FROM Expected expected
CROSS JOIN sys.database_recovery_status identityRow
LEFT JOIN sys.tables tableRow
  ON tableRow.schema_id=SCHEMA_ID(N'dbo')
 AND tableRow.name=expected.TableName
LEFT JOIN sys.change_tracking_tables tracking
  ON tracking.object_id=tableRow.object_id
WHERE identityRow.database_id=DB_ID()
ORDER BY expected.TableName;
"@
    return Invoke-SqlTable -Database $Database -Sql $sql
}

function Get-LastConvergence {
    return Invoke-SqlTable -Database $TargetDatabase -Sql @'
WITH Profiles AS
(
    SELECT value SourceProfileCode
    FROM (VALUES(N'CSDT_OTO'),(N'CSDT_MOTO')) item(value)
)
SELECT profileRow.SourceProfileCode,
       MAX(CASE WHEN historyRow.Status IN(N'COMPLETED',N'SUCCEEDED')
                THEN historyRow.CompletedAtUtc END) LastSuccessfulConvergenceUtc
FROM Profiles profileRow
LEFT JOIN dbo.App_QlhvSyncOperationHistory historyRow
  ON historyRow.SourceProfileCode=profileRow.SourceProfileCode
GROUP BY profileRow.SourceProfileCode
ORDER BY profileRow.SourceProfileCode;
'@
}

function Get-SourceVersion {
    param([Parameter(Mandatory)][string]$Database)
    $table = Invoke-SqlTable -Database $Database -Sql @'
SELECT CONVERT(bigint,CHANGE_TRACKING_CURRENT_VERSION()) CurrentVersion;
'@
    return [long]$table.Rows[0].CurrentVersion
}

function Convert-TableRows {
    param([Parameter(Mandatory)][Data.DataTable]$Table)
    return @($Table.Rows | ForEach-Object {
        $item = [ordered]@{}
        foreach ($column in $Table.Columns) {
            $value = $_[$column.ColumnName]
            $item[$column.ColumnName] = if ($value -is [DBNull]) {
                $null
            } else {
                $value
            }
        }
        [pscustomobject]$item
    })
}

$capturedAtUtc = [DateTimeOffset]::UtcNow
$worker = Get-CimInstance Win32_Service -Filter "Name='$workerServiceName'"
$w32Time = Get-CimInstance Win32_Service -Filter "Name='W32Time'"
$timeParameters = Get-ItemProperty -LiteralPath `
    'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Parameters'
$configuration = Get-CommandEvidence -Arguments @('/query', '/configuration')
$peers = Get-CommandEvidence -Arguments @('/query', '/peers')

$timeEvents = @(
    Get-WinEvent -FilterHashtable @{
        LogName = 'System'
        ProviderName = 'Microsoft-Windows-Time-Service'
        StartTime = (Get-Date).AddHours(-12)
    } -ErrorAction SilentlyContinue |
    Sort-Object TimeCreated |
    Select-Object -Last 80
)
$safeTimeEvents = @($timeEvents | ForEach-Object {
    [pscustomobject]@{
        EventId = $_.Id
        TimeCreatedUtc = $_.TimeCreated.ToUniversalTime().ToString('o')
        Level = $_.LevelDisplayName
        IsSuccessfulSyncEvidence = $_.Id -in @(35,37)
        IsLargeTimeCorrection = $_.Id -eq 52
        IsStaleDataError = $_.Message -match 'stale time data'
    }
})
$staleErrorEvent = @($timeEvents | Where-Object {
    $_.Message -match 'stale time data'
} | Select-Object -Last 1)
$lastErrorAtUtc = if ($staleErrorEvent.Count -eq 1) {
    [DateTimeOffset]$staleErrorEvent[0].TimeCreated.ToUniversalTime()
} else {
    $null
}

$targetBefore = Get-TargetSnapshot
$checkpointsBeforeTable = Get-Checkpoints
$checkpointsBefore = Convert-TableRows -Table $checkpointsBeforeTable
$timeSamples = [Collections.Generic.List[object]]::new()
$sampleContractFailure = $false
for ($index = 0; $index -lt $SampleCount; $index++) {
    $windowsStart = [DateTimeOffset]::UtcNow
    $probe = Invoke-TimeHealthContract -Mode 'standalone'
    $probeContract = $probe.Contract
    $time = $null
    $probeFailure = $probe.ParseFailure
    try {
        if ($null -ne $probeContract -and $null -ne $probeContract.standalone) {
            $time = $probeContract.standalone.time
        }
        if ($null -eq $time) {
            throw 'Standalone Time object is missing.'
        }
    }
    catch {
        $probeFailure = $_.Exception.Message
        $sampleContractFailure = $true
    }
    $windowsEnd = [DateTimeOffset]::UtcNow
    if ($probe.ExitCode -ne 0) {
        $sampleContractFailure = $true
    }
    $timeSamples.Add([pscustomobject]@{
        Sample = $index + 1
        WindowsUtcStart = $windowsStart.ToString('o')
        WindowsUtcEnd = $windowsEnd.ToString('o')
        ToolExitCode = $probe.ExitCode
        Classification = if ($null -eq $probeContract) {
            'INVALID_JSON'
        } else {
            [string]$probeContract.classification
        }
        ContractVersion = if ($null -eq $probeContract) {
            $null
        } else {
            [string]$probeContract.contractVersion
        }
        ProbeFailure = $probeFailure
        ServerUtcNow = if ($null -eq $time) { $null } else {
            [string]$time.serverUtcNow
        }
        DatabaseUtcNow = if ($null -eq $time) { $null } else {
            [string]$time.databaseUtcNow
        }
        DurableLastObservedUtc = if ($null -eq $time) { $null } else {
            [string]$time.durableLastObservedUtc
        }
        ClockSkewMilliseconds = if ($null -eq $time) { $null } else {
            $time.clockSkewMilliseconds
        }
        TimeZone = if ($null -eq $time) { $null } else {
            [string]$time.timeZone
        }
        WindowsTimeServiceState = if ($null -eq $time) { $null } else {
            [string]$time.windowsTimeServiceState
        }
        ConfiguredPeer = if ($null -eq $time) { $null } else {
            [string]$time.configuredPeer
        }
        CurrentSource = if ($null -eq $time) { $null } else {
            [string]$time.currentSource
        }
        LastSuccessfulSyncUtc = if ($null -eq $time) { $null } else {
            [string]$time.lastSuccessfulSyncUtc
        }
        LastSyncErrorCode = if ($null -eq $time) { $null } else {
            $time.lastSyncError
        }
        PhaseOffsetMilliseconds = if ($null -eq $time) { $null } else {
            $time.phaseOffsetMilliseconds
        }
        TimeHealth = if ($null -eq $time) { $null } else {
            [string]$time.health
        }
        ReasonCode = if ($null -eq $time) { $null } else {
            [string]$time.reasonCode
        }
        EvaluatedAtUtc = if ($null -eq $time) { $null } else {
            [string]$time.evaluatedAtUtc
        }
    })
    if ($index -lt ($SampleCount - 1)) {
        Start-Sleep -Seconds 1
    }
}
$apiContractProbe = if ($ApiContractMode -eq 'Disabled') {
    $null
} else {
    Invoke-TimeHealthContract -Mode 'api'
}

$targetAfter = Get-TargetSnapshot
$checkpointsAfter = Convert-TableRows -Table (Get-Checkpoints)
$targetDomains = Convert-TableRows -Table (Get-TargetDomains)
if ([bool][int]$targetAfter.Rows[0].VehicleMappingPresent) {
    $targetDomains += Convert-TableRows -Table (Get-VehicleTargetDomains)
}
$lastConvergence = Convert-TableRows -Table (Get-LastConvergence)
$checkpointMap = @{}
foreach ($checkpoint in $checkpointsBefore) {
    $checkpointMap[[string]$checkpoint.SourceProfileCode] =
        [long]$checkpoint.CheckpointVersion
}

$audits = [Collections.Generic.List[object]]::new()
$sourceVersionsBefore = [ordered]@{}
foreach ($profile in @(
    [pscustomobject]@{ Code='CSDT_OTO'; Database=$OtoDatabase },
    [pscustomobject]@{ Code='CSDT_MOTO'; Database=$MotoDatabase }
)) {
    if (-not $checkpointMap.ContainsKey($profile.Code)) {
        $checkpoint = -1
    } else {
        $checkpoint = [long]$checkpointMap[$profile.Code]
    }
    $sourceRows = Get-SourceAudit -Database $profile.Database `
        -Profile $profile.Code -Checkpoint $checkpoint
    $sourceVersionsBefore[$profile.Code] =
        [long]$sourceRows.Rows[0].CurrentVersion
    foreach ($row in $sourceRows.Rows) {
        $exists = [bool]$row.TableExists
        $tracking = [bool]$row.ChangeTrackingEnabled
        $minimum = if ($row.MinimumValidVersion -is [DBNull]) {
            $null
        } else {
            [long]$row.MinimumValidVersion
        }
        $deleteContractVerified = $exists
        $classification = if (-not $exists -or $checkpoint -lt 0) {
            'UNCLASSIFIED'
        } elseif (-not $deleteContractVerified) {
            'UNSAFE_DELETE_CONTRACT'
        } elseif (-not $tracking) {
            'CT_DISABLED_REQUIRES_SNAPSHOT'
        } elseif ($null -eq $minimum) {
            'UNCLASSIFIED'
        } elseif ($checkpoint -lt $minimum) {
            'EXPIRED_REQUIRES_FULL_CONVERGENCE'
        } else {
            'INCREMENTAL_VALID'
        }
        $domain = switch ([string]$row.TableName) {
            'KhoaHoc' { 'COURSE' }
            'GiaoVien' { 'TEACHER' }
            'XeTap' { 'VEHICLE' }
            'KhoaHoc_GiaoVien' { 'RELATION' }
            default { 'LEARNER' }
        }
        $target = @($targetDomains | Where-Object {
            $_.SourceProfileCode -eq $profile.Code -and
            $_.DomainCode -eq $domain
        } | Select-Object -First 1)
        $convergence = @($lastConvergence | Where-Object {
            $_.SourceProfileCode -eq $profile.Code
        } | Select-Object -First 1)
        $vehicleSchemaPresent =
            [bool][int]$targetAfter.Rows[0].VehicleMappingPresent
        $targetStatus = if ($domain -eq 'VEHICLE' -and -not $vehicleSchemaPresent) {
            [pscustomobject]@{
                TargetRows = $null
                ActiveRows = $null
                DuplicateGroups = $null
                IdentityStatus = 'TARGET_MAPPING_SCHEMA_NOT_INSTALLED'
                OwnershipContract =
                    'SOURCE_OWNED_FIELDS_ONLY; QLHV_ASSIGNMENT_FIELDS_PRESERVED'
                DeleteContract =
                    'MISSING_OR_INACTIVE; ASSIGNED_VEHICLE_MANUAL_REVIEW; NO_HARD_DELETE'
            }
        } else {
            [pscustomobject]@{
                TargetRows = [long]$target[0].TargetRows
                ActiveRows = [long]$target[0].ActiveRows
                DuplicateGroups = [long]$target[0].DuplicateGroups
                IdentityStatus = if ([long]$target[0].DuplicateGroups -eq 0) {
                    'EXACT_IDENTITY_NO_DUPLICATE'
                } else {
                    'BLOCKED_AMBIGUOUS'
                }
                OwnershipContract = [string]$target[0].OwnershipContract
                DeleteContract = [string]$target[0].DeleteContract
            }
        }
        $audits.Add([pscustomobject]@{
            SourceProfileCode = $profile.Code
            SourceDatabase = [string]$row.DatabaseName
            SourceDatabaseGuid = [string]$row.DatabaseGuid
            Table = "dbo.$([string]$row.TableName)"
            Domain = $domain
            ChangeTrackingEnabled = $tracking
            CurrentVersion = [long]$row.CurrentVersion
            MinimumValidVersion = $minimum
            CommittedCheckpoint = $checkpoint
            CheckpointValid = $tracking -and $null -ne $minimum -and
                $checkpoint -ge $minimum
            Classification = $classification
            SourceRowCountDiagnosticOnly = [long]$row.SourceRowCount
            LastSuccessfulConvergenceUtc = if ($convergence.Count -eq 0) {
                $null
            } else {
                $convergence[0].LastSuccessfulConvergenceUtc
            }
            TargetRowsDiagnosticOnly = $targetStatus.TargetRows
            TargetActiveRowsDiagnosticOnly = $targetStatus.ActiveRows
            TargetDuplicateGroups = $targetStatus.DuplicateGroups
            TargetExactIdentityStatus = $targetStatus.IdentityStatus
            OwnershipContract = $targetStatus.OwnershipContract
            DeleteInactiveContract = $targetStatus.DeleteContract
            DeleteContractVerified = $deleteContractVerified
        })
    }
}

$sourceVersionsAfter = [ordered]@{
    CSDT_OTO = (Get-SourceVersion -Database $OtoDatabase)
    CSDT_MOTO = (Get-SourceVersion -Database $MotoDatabase)
}
$checkpointsStable = ($checkpointsBefore | ConvertTo-Json -Compress -Depth 4) -eq
    ($checkpointsAfter | ConvertTo-Json -Compress -Depth 4)
$sourceVersionsStable =
    [long]$sourceVersionsBefore.CSDT_OTO -eq
        [long]$sourceVersionsAfter.CSDT_OTO -and
    [long]$sourceVersionsBefore.CSDT_MOTO -eq
        [long]$sourceVersionsAfter.CSDT_MOTO
$targetRowBefore = $targetBefore.Rows[0]
$targetRowAfter = $targetAfter.Rows[0]
$durableUtc = if ($targetRowAfter.DurableUtc -is [DBNull]) {
    $null
} else {
    [DateTimeOffset]::new([datetime]::SpecifyKind(
        [datetime]$targetRowAfter.DurableUtc,
        [DateTimeKind]::Utc))
}
$finalSqlUtc = [DateTimeOffset]::new([datetime]::SpecifyKind(
    [datetime]$targetRowAfter.SqlUtc,
    [DateTimeKind]::Utc))
$durableInPast = $null -eq $durableUtc -or
    $durableUtc -le $finalSqlUtc.AddSeconds(30)
$windowsMonotonic = $true
for ($index = 1; $index -lt $timeSamples.Count; $index++) {
    if ([DateTimeOffset]$timeSamples[$index].WindowsUtcStart -lt
        [DateTimeOffset]$timeSamples[$index - 1].WindowsUtcStart) {
        $windowsMonotonic = $false
    }
}
$allTimeContractsHealthy = -not $sampleContractFailure -and
    @($timeSamples | Where-Object {
        $_.ToolExitCode -ne 0 -or
        $_.Classification -ne 'TIME_HEALTHY' -or
        $_.ContractVersion -ne $expectedTimeContractVersion -or
        $_.TimeHealth -ne 'HEALTHY' -or
        $_.ReasonCode -ne 'NONE' -or
        $_.WindowsTimeServiceState -ne 'Running' -or
        $_.ConfiguredPeer -ne $ApprovedNtpPeer -or
        $_.CurrentSource -ne $ApprovedNtpPeer -or
        $null -eq $_.LastSyncErrorCode -or
        [int]$_.LastSyncErrorCode -ne 0
    }).Count -eq 0
$lastSample = $timeSamples[$timeSamples.Count - 1]
$lastErrorCode = $lastSample.LastSyncErrorCode
$lastSuccessUtc = if ($null -eq $lastSample.LastSuccessfulSyncUtc) {
    $null
} else {
    [DateTimeOffset]$lastSample.LastSuccessfulSyncUtc
}
$freshSuccessAfterTimestampedError =
    $null -ne $lastErrorAtUtc -and
    $null -ne $lastSuccessUtc -and
    $lastSuccessUtc -gt $lastErrorAtUtc
$diagnosticClassification = [string]$lastSample.Classification
$configuredPeerExact =
    [string]$timeParameters.NtpServer -eq $ApprovedNtpPeer -and
    [string]$timeParameters.Type -eq 'NTP'
$timeHealthy = $configuredPeerExact -and
    $w32Time.State -eq 'Running' -and
    $windowsMonotonic -and
    $timeSamples.Count -ge 3 -and
    $allTimeContractsHealthy
$apiContractHealthy = $null -ne $apiContractProbe -and
    $null -ne $apiContractProbe.Contract -and
    $apiContractProbe.ExitCode -eq 0 -and
    [string]$apiContractProbe.Contract.classification -eq 'TIME_HEALTHY'

$lease = if ($ProbeWriterLease) {
    Test-WriterLease
} else {
    [pscustomobject]@{
        Available = $null
        FailedResource = $null
        LockResult = $null
    }
}

$targetIdentityExact =
    [string]$targetRowAfter.DatabaseName -eq $TargetDatabase -and
    [int]$targetRowAfter.DatabaseId -eq $expectedTargetDatabaseId -and
    [string]$targetRowAfter.DatabaseGuid -eq $expectedTargetDatabaseGuid
$workerStopped = $worker.State -eq 'Stopped' -and
    [int]$worker.ProcessId -eq 0
$workerPathExact =
    ([string]$worker.PathName).Trim('"') -eq $expectedWorkerExecutable
$writersInactive =
    [int]$targetRowAfter.ActiveAutoSyncRuns -eq 0 -and
    [int]$targetRowAfter.ActiveOperations -eq 0
$domainsClassified = @($audits | Where-Object {
    $_.Classification -in @('UNCLASSIFIED','UNSAFE_DELETE_CONTRACT') -or
    $_.TargetExactIdentityStatus -eq 'BLOCKED_AMBIGUOUS'
}).Count -eq 0
$schemaStateExact = if ($ExecutionReady) {
    [int]$targetRowAfter.AssignmentSchemaPresent -eq 0 -and
    [int]$targetRowAfter.VehicleMappingPresent -eq 1 -and
    [int]$targetRowAfter.RecoverySchemaPresent -eq 1 -and
    [int]$targetRowAfter.GlobalCourseUniquePresent -eq 0 -and
    [int]$targetRowAfter.CourseSourceIdentityIndexPresent -eq 1 -and
    [int]$targetRowAfter.ProfileCourseLookupPresent -eq 1
} else {
    [int]$targetRowAfter.AssignmentSchemaPresent -eq 0 -and
    [int]$targetRowAfter.VehicleMappingPresent -eq 0 -and
    [int]$targetRowAfter.RecoverySchemaPresent -eq 0 -and
    [int]$targetRowAfter.GlobalCourseUniquePresent -eq 1 -and
    [int]$targetRowAfter.CourseSourceIdentityIndexPresent -eq 1 -and
    [int]$targetRowAfter.ProfileCourseLookupPresent -eq 0
}

$blockers = [Collections.Generic.List[string]]::new()
$emDash = [char]0x2014
if (-not $timeHealthy) {
    $blockers.Add("BLOCKED $emDash PRODUCTION TIME AUTHORITY NOT STABLE")
}
if ($ApiContractMode -eq 'Required' -and -not $apiContractHealthy) {
    $blockers.Add("BLOCKED $emDash TIME-HEALTH API CONTRACT NOT AVAILABLE")
}
if (-not $domainsClassified) {
    $blockers.Add(
        "BLOCKED $emDash FULL CONVERGENCE DELETE CONTRACT NOT VERIFIED")
}
if (-not $checkpointsStable -or -not $sourceVersionsStable) {
    $blockers.Add(
        "BLOCKED $emDash FRESH PRODUCTION BASELINE CHANGED DURING AUDIT")
}
if (-not $writersInactive -or
    ($ProbeWriterLease -and -not [bool]$lease.Available)) {
    $blockers.Add(
        "BLOCKED $emDash MULTIPLE WRITER COORDINATION REQUIRED")
}
if (-not $targetIdentityExact -or -not $workerStopped -or
    -not $workerPathExact -or -not $schemaStateExact) {
    $blockers.Add(
        "BLOCKED $emDash PRODUCTION DEPLOYMENT PRECONDITION FAILED")
}

$result = [ordered]@{
    ContractVersion = 'RT03_V6_20260731'
    PreflightMode = if ($ExecutionReady) {
        'EXECUTION_READY'
    } else {
        'UNCHANGED_PRODUCTION_BASELINE'
    }
    CapturedAtUtc = $capturedAtUtc.ToString('o')
    CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    ReadOnlyProductionAudit = $true
    ContainsRawBusinessIdentityOrPii = $false
    TimeAuthority = [ordered]@{
        TimeHealth = if ($timeHealthy) { 'HEALTHY' } else { 'BLOCKED' }
        DiagnosticClassification = $diagnosticClassification
        AuthoritativeMode = 'STANDALONE_SHARED_POLICY'
        TimeContractVersion = $expectedTimeContractVersion
        SharedPolicyExecutable = $TimePreflightExecutable
        ApprovedPeer = $ApprovedNtpPeer
        ConfiguredPeer = [string]$timeParameters.NtpServer
        ConfiguredType = [string]$timeParameters.Type
        W32TimeState = [string]$w32Time.State
        W32TimeStartMode = [string]$w32Time.StartMode
        ConfigurationCommandExitCode = $configuration.ExitCode
        ConfigurationCommandOutput = $configuration.Output
        PeersCommandExitCode = $peers.ExitCode
        PeersCommandOutput = $peers.Output
        LastSyncErrorCode = $lastErrorCode
        LastSyncErrorRaw = $null
        LastSyncErrorAtUtc = if ($null -eq $lastErrorAtUtc) {
            $null
        } else {
            $lastErrorAtUtc.ToString('o')
        }
        LastSuccessfulSyncUtc = if ($null -eq $lastSuccessUtc) {
            $null
        } else {
            $lastSuccessUtc.ToString('o')
        }
        FreshSuccessAfterTimestampedError = $freshSuccessAfterTimestampedError
        ConsecutiveSamples = $timeSamples
        DurableLastObservedUtc = if ($null -eq $durableUtc) {
            $null
        } else {
            $durableUtc.ToString('o')
        }
        DurableUtcInPast = $durableInPast
        ClockRollbackDetectedInSampleWindow = -not $windowsMonotonic
        HistoricalLargeTimeCorrectionEventsPresent =
            @($safeTimeEvents | Where-Object IsLargeTimeCorrection).Count -gt 0
        EventLog = $safeTimeEvents
        Policy = [ordered]@{
            RequiredConsecutiveSamples = 3
            WarningSkewMilliseconds = $warningSkewMilliseconds
            LastErrorConditionRemoved = $false
            ErrorTwoWithoutTimestampedNewerSuccessIsHealthy = $false
            DecisionSource = 'QLHV.Application.Runtime.TimeAuthorityPolicy'
        }
    }
    ApiContract = [ordered]@{
        Mode = $ApiContractMode
        Uri = $RuntimeStatusUri
        RequiredForThisRun = $ApiContractMode -eq 'Required'
        Healthy = if ($ApiContractMode -eq 'Disabled') {
            $null
        } else {
            $apiContractHealthy
        }
        ExitCode = if ($null -eq $apiContractProbe) {
            $null
        } else {
            $apiContractProbe.ExitCode
        }
        Classification = if ($null -eq $apiContractProbe -or
            $null -eq $apiContractProbe.Contract) {
            $null
        } else {
            [string]$apiContractProbe.Contract.classification
        }
        ParseFailure = if ($null -eq $apiContractProbe) {
            $null
        } else {
            $apiContractProbe.ParseFailure
        }
    }
    Runtime = [ordered]@{
        WorkerState = [string]$worker.State
        WorkerPid = [int]$worker.ProcessId
        WorkerPath = [string]$worker.PathName
        TargetDatabase = [string]$targetRowAfter.DatabaseName
        TargetDatabaseId = [int]$targetRowAfter.DatabaseId
        TargetDatabaseGuid = [string]$targetRowAfter.DatabaseGuid
        ActiveAutoSyncRuns = [int]$targetRowAfter.ActiveAutoSyncRuns
        ActiveFullSyncOperations = [int]$targetRowAfter.ActiveOperations
        AssignmentSchemaPresent = [bool][int]$targetRowAfter.AssignmentSchemaPresent
        CoursePrerequisitePresent =
            [int]$targetRowAfter.GlobalCourseUniquePresent -eq 0 -and
            [int]$targetRowAfter.ProfileCourseLookupPresent -eq 1
        VehicleMappingPresent = [bool][int]$targetRowAfter.VehicleMappingPresent
        RecoverySchemaPresent = [bool][int]$targetRowAfter.RecoverySchemaPresent
        SchemaStateExactForMode = $schemaStateExact
    }
    CheckpointsBefore = $checkpointsBefore
    CheckpointsAfter = $checkpointsAfter
    CheckpointsStableDuringAudit = $checkpointsStable
    SourceVersionsBefore = $sourceVersionsBefore
    SourceVersionsAfter = $sourceVersionsAfter
    SourceVersionsStableDuringAudit = $sourceVersionsStable
    PerTableChangeTrackingAudit = $audits
    WriterLease = [ordered]@{
        Probed = [bool]$ProbeWriterLease
        ResourcesInOrder = $writerResources
        Available = $lease.Available
        FailedResource = $lease.FailedResource
        LockResult = $lease.LockResult
    }
    Gates = [ordered]@{
        TimeHealthHealthy = $timeHealthy
        TimeContractSamplesHealthy = $allTimeContractsHealthy
        ApiContractHealthy = if ($ApiContractMode -eq 'Disabled') {
            $null
        } else {
            $apiContractHealthy
        }
        WorkerStoppedPidZero = $workerStopped
        WorkerPathExact = $workerPathExact
        TargetIdentityExact = $targetIdentityExact
        AutoSyncAndFullSyncInactive = $writersInactive
        WriterLeaseAvailable = if ($ProbeWriterLease) {
            [bool]$lease.Available
        } else {
            $null
        }
        CheckpointsStableDuringAudit = $checkpointsStable
        SourceVersionsStableDuringAudit = $sourceVersionsStable
        EveryDomainClassified = $domainsClassified
        SchemaStateExactForMode = $schemaStateExact
    }
    Blockers = @($blockers | Select-Object -Unique)
    ProductionRecoveryAllowed = $blockers.Count -eq 0
}

$json = $result | ConvertTo-Json -Depth 14
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText(
        $resolvedOutput,
        $json,
        [Text.UTF8Encoding]::new($false))
}
Write-Output $json
if (-not $result.ProductionRecoveryAllowed) {
    exit 20
}
