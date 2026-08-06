[CmdletBinding()]
param(
    [switch]$Execute,
    [string]$EvidenceRoot = '',
    [string]$PlanRoot =
        'D:\QLHV_RT03_EVIDENCE\RT03_FINAL_WINDOW_20260727_143220\POST_ENTRY_20260727_144304',
    [string]$ExpectedPlanHash =
        'E53707073181FEE254834B03505A4E22C59D52674B4DB4B3AFEC78729A27544E'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedBranch = 'codex/csdt-realtime-v2-to-v1-oto-moto'
$expectedHead = '383387e8456d1a61640eee190519ff3f28619218'
$expectedProtectedHash =
    '12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E'
$expectedServer = 'CSDLTTTC'
$expectedTargetDatabase = 'QLHV_APP'
$expectedTargetDatabaseId = 12
$expectedTargetDatabaseGuid = '9C44B304-8A84-4D0D-9A82-19C7233FF6BB'
$expectedSourceDatabase = 'CSDL_OTO'
$expectedSourceDatabaseId = 9
$expectedSourceDatabaseGuid = '9A8B9BC1-18F3-4823-8123-3DC197A9D540'
$expectedCycleId = 'F3F0B522-6AD9-4824-B990-D240BECB9718'
$expectedCandidateHash =
    '145fc91cbb5096fc28ece74b764c2b33add75dbb8d43023db3cbf468480dbcf1'
$expectedPlanFileHash =
    '4C29696B781E765E204C7676E535F37433D6A6D7B1CB682BA2B970919D1F14E2'
$targetConnectionString =
    'Server=lpc:CSDLTTTC;Database=QLHV_APP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Application Name=RT04 Exact Canary Rollback;'
$sourceConnectionString =
    'Server=lpc:CSDLTTTC;Database=CSDL_OTO;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Application Name=RT04 Exact Canary Rollback ReadOnly;'

function Assert-Exact([bool]$Condition, [string]$Code) {
    if (-not $Condition) {
        throw $Code
    }
}

function Convert-BytesToHex([byte[]]$Bytes) {
    return ([BitConverter]::ToString($Bytes)).Replace('-', '')
}

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return (Convert-BytesToHex ($sha.ComputeHash($Bytes)))
    }
    finally {
        $sha.Dispose()
    }
}

function Get-TextSha256([string]$Value) {
    return Get-Sha256Hex ([Text.Encoding]::UTF8.GetBytes($Value))
}

function Get-HmacHex([byte[]]$Key, [string]$Value) {
    $hmac = [Security.Cryptography.HMACSHA256]::new($Key)
    try {
        return (Convert-BytesToHex (
            $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))).ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
}

function Open-SqlConnection([string]$ConnectionString) {
    $connection = [Data.SqlClient.SqlConnection]::new($ConnectionString)
    $connection.Open()
    return $connection
}

function New-SqlCommand(
    [Data.SqlClient.SqlConnection]$Connection,
    [string]$Sql,
    [Data.SqlClient.SqlTransaction]$Transaction = $null
) {
    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 30
    if ($null -ne $Transaction) {
        $command.Transaction = $Transaction
    }
    return $command
}

function Add-Parameter(
    [Data.SqlClient.SqlCommand]$Command,
    [string]$Name,
    [Data.SqlDbType]$Type,
    [object]$Value,
    [int]$Size = 0
) {
    $parameter = if ($Size -ne 0) {
        $Command.Parameters.Add($Name, $Type, $Size)
    }
    else {
        $Command.Parameters.Add($Name, $Type)
    }
    if ($null -eq $Value) {
        $parameter.Value = [DBNull]::Value
    }
    else {
        $parameter.Value = $Value
    }
    return $parameter
}

function Read-Table(
    [Data.SqlClient.SqlConnection]$Connection,
    [string]$Sql,
    [hashtable]$Parameters = @{},
    [Data.SqlClient.SqlTransaction]$Transaction = $null
) {
    $command = New-SqlCommand $Connection $Sql $Transaction
    try {
        foreach ($name in $Parameters.Keys) {
            $value = $Parameters[$name]
            if ($value -is [object[]] -and $value.Count -eq 1 -and
                $value[0] -is [byte[]]) {
                $value = [byte[]]$value[0]
            }
            elseif ($value -is [object[]] -and
                @($value | Where-Object { $_ -isnot [byte] }).Count -eq 0) {
                $value = [byte[]]$value
            }
            if ($value -is [long]) {
                Add-Parameter $command $name ([Data.SqlDbType]::BigInt) $value | Out-Null
            }
            elseif ($value -is [byte[]]) {
                Add-Parameter $command $name ([Data.SqlDbType]::Binary) $value $value.Length | Out-Null
            }
            elseif ($value -is [Guid]) {
                Add-Parameter $command $name ([Data.SqlDbType]::UniqueIdentifier) $value | Out-Null
            }
            else {
                Add-Parameter $command $name ([Data.SqlDbType]::NVarChar) ([string]$value) 4000 | Out-Null
            }
        }
        $reader = $command.ExecuteReader()
        try {
            $table = [Data.DataTable]::new()
            $table.Load($reader)
            return ,$table
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $command.Dispose()
    }
}

function Read-Scalar(
    [Data.SqlClient.SqlConnection]$Connection,
    [string]$Sql,
    [hashtable]$Parameters = @{},
    [Data.SqlClient.SqlTransaction]$Transaction = $null
) {
    $table = Read-Table $Connection $Sql $Parameters $Transaction
    Assert-Exact ($table.Rows.Count -eq 1) 'SCALAR_QUERY_CARDINALITY_REJECTED'
    return $table.Rows[0][0]
}

function Get-CanonicalValue([object]$Value) {
    if ($null -eq $Value -or $Value -is [DBNull]) {
        return '<NULL>'
    }
    if ($Value -is [byte[]]) {
        return [Convert]::ToBase64String($Value)
    }
    if ($Value -is [DateTime]) {
        return $Value.ToString('yyyy-MM-ddTHH:mm:ss.fffffff',
            [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [bool]) {
        if ($Value) { return '1' }
        return '0'
    }
    if ($Value -is [IFormattable]) {
        return $Value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
    }
    return [string]$Value
}

function Get-TableHash([Data.DataTable]$Table) {
    $builder = [Text.StringBuilder]::new()
    foreach ($row in $Table.Rows) {
        foreach ($column in $Table.Columns) {
            $value = Get-CanonicalValue $row[$column.ColumnName]
            [void]$builder.Append($column.ColumnName)
            [void]$builder.Append('=')
            [void]$builder.Append($value.Length)
            [void]$builder.Append(':')
            [void]$builder.Append($value)
            [void]$builder.Append('|')
        }
        [void]$builder.Append("`n")
    }
    return Get-TextSha256 $builder.ToString()
}

function Convert-RowToBackupObject([Data.DataRow]$Row) {
    $result = [ordered]@{}
    foreach ($column in $Row.Table.Columns) {
        $value = $Row[$column.ColumnName]
        $result[$column.ColumnName] = if ($value -is [DBNull]) {
            $null
        }
        elseif ($value -is [byte[]]) {
            [Convert]::ToBase64String($value)
        }
        elseif ($value -is [DateTime]) {
            $value.ToString('yyyy-MM-ddTHH:mm:ss.fffffff',
                [Globalization.CultureInfo]::InvariantCulture)
        }
        else {
            $value
        }
    }
    return [pscustomobject]$result
}

function Get-SourceSnapshot(
    [Data.SqlClient.SqlConnection]$Source,
    [string]$SourceKey
) {
    $sql = @'
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerIdentity,
    DB_NAME() AS DatabaseName,
    DB_ID() AS DatabaseId,
    CONVERT(nvarchar(36), identityRow.database_guid) AS DatabaseGuid,
    CHANGE_TRACKING_CURRENT_VERSION() AS CurrentCtVersion,
    CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX')) AS NguoiLxMinCtVersion,
    CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo')) AS DossierMinCtVersion,
    (SELECT COUNT_BIG(*)
     FROM dbo.NguoiLX learner
     INNER JOIN dbo.NguoiLX_HoSo dossier ON dossier.MaDK=learner.MaDK
     LEFT JOIN dbo.KhoaHoc course ON course.MaKH=dossier.MaKhoaHoc
     WHERE LTRIM(RTRIM(learner.MaDK)) LIKE N'66029%'
        OR LTRIM(RTRIM(course.MaCSDT))=N'66029') AS ActiveRows,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX WHERE MaDK=@SourceKey) AS ExactLearnerRows,
    (SELECT COUNT_BIG(*) FROM dbo.NguoiLX_HoSo WHERE MaDK=@SourceKey) AS ExactDossierRows
FROM sys.database_recovery_status identityRow
WHERE identityRow.database_id=DB_ID();
'@
    $table = Read-Table $Source $sql @{ '@SourceKey' = $SourceKey }
    Assert-Exact ($table.Rows.Count -eq 1) 'SOURCE_IDENTITY_CARDINALITY_REJECTED'
    return $table.Rows[0]
}

function Assert-SourceSnapshot([Data.DataRow]$Snapshot) {
    Assert-Exact ([string]$Snapshot.ServerIdentity -eq $expectedServer) 'SOURCE_SERVER_REJECTED'
    Assert-Exact ([string]$Snapshot.DatabaseName -eq $expectedSourceDatabase) 'SOURCE_DATABASE_REJECTED'
    Assert-Exact ([int]$Snapshot.DatabaseId -eq $expectedSourceDatabaseId) 'SOURCE_DATABASE_ID_REJECTED'
    Assert-Exact ([string]$Snapshot.DatabaseGuid -eq $expectedSourceDatabaseGuid) 'SOURCE_DATABASE_GUID_REJECTED'
    Assert-Exact ([long]$Snapshot.ActiveRows -eq 156) 'SOURCE_COUNT_REJECTED'
    Assert-Exact ([long]$Snapshot.ExactLearnerRows -eq 0) 'CANARY_SOURCE_LEARNER_STILL_PRESENT'
    Assert-Exact ([long]$Snapshot.ExactDossierRows -eq 0) 'CANARY_SOURCE_DOSSIER_STILL_PRESENT'
    Assert-Exact ([long]$Snapshot.CurrentCtVersion -eq 1) 'SOURCE_CT_VERSION_REJECTED'
    Assert-Exact ([long]$Snapshot.NguoiLxMinCtVersion -le 1) 'SOURCE_CT_WINDOW_REJECTED'
    Assert-Exact ([long]$Snapshot.DossierMinCtVersion -le 1) 'SOURCE_DOSSIER_CT_WINDOW_REJECTED'
}

function Get-QlhvOwnedHash(
    [Data.SqlClient.SqlConnection]$Target,
    [byte[]]$Key,
    [long]$ExcludedId,
    [Data.SqlClient.SqlTransaction]$Transaction = $null
) {
    $sql = @'
SELECT HocVienId,SourceProfileCode,SourceMaDK,IsDeleted,GhiChuNoiBo,
       DaDoiChieuCCCD,DaInThe,DaTaoXML,CreatedBy,UpdatedBy,DeletedBy,DeleteReason
FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
WHERE SourceProfileCode=N'CSDT_OTO' AND HocVienId<>@ExcludedId
ORDER BY HocVienId;
'@
    $rows = Read-Table $Target $sql @{ '@ExcludedId' = $ExcludedId } $Transaction
    $lines = foreach ($row in $rows.Rows) {
        @(
            [string]$row.HocVienId,
            (Get-CanonicalValue $row.SourceProfileCode),
            (Get-CanonicalValue $row.SourceMaDK),
            (Get-CanonicalValue $row.IsDeleted),
            (Get-CanonicalValue $row.GhiChuNoiBo),
            (Get-CanonicalValue $row.DaDoiChieuCCCD),
            (Get-CanonicalValue $row.DaInThe),
            (Get-CanonicalValue $row.DaTaoXML),
            (Get-CanonicalValue $row.CreatedBy),
            (Get-CanonicalValue $row.UpdatedBy),
            (Get-CanonicalValue $row.DeletedBy),
            (Get-CanonicalValue $row.DeleteReason)
        ) -join '|'
    }
    $canonical = $lines -join "`n"
    return Get-HmacHex $Key ("RT03-QLHV-OWNED-v1|" + $canonical)
}

function Get-DownstreamSnapshot(
    [Data.SqlClient.SqlConnection]$Target,
    [long]$HocVienId,
    [string]$SourceKey,
    [string]$MaDk,
    [Data.SqlClient.SqlTransaction]$Transaction = $null
) {
    $sql = @'
SELECT
  (SELECT COUNT_BIG(*) FROM sys.foreign_key_columns
   WHERE referenced_object_id=OBJECT_ID(N'dbo.App_HocVien')) AS PhysicalFkDefinitions,
  (SELECT COUNT_BIG(*) FROM dbo.App_DangKyThiLai WHERE MaDK=@MaDK) AS RetryExamRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_KetQuaSatHach WHERE MaDK=@MaDK) AS ExamResultRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_KetQuaTotNghiep WHERE MaDK=@MaDK) AS GraduationResultRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_HocVienPhoto
   WHERE SourceProfileCode=N'CSDT_OTO' AND SourceMaDK=@SourceKey) AS PhotoRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_HocVienPhotoProcessingHistory
   WHERE SourceProfileCode=N'CSDT_OTO' AND SourceMaDK=@SourceKey) AS PhotoHistoryRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_AuditLog
   WHERE (EntityType LIKE N'%HocVien%' OR EntityType LIKE N'%Student%')
     AND (EntityId=@HocVienIdText OR EntityKey=@SourceKey OR EntityKey=@MaDK)) AS AuditEntityRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_TaiLieu
   WHERE (EntityType LIKE N'%HocVien%' OR EntityType LIKE N'%Student%')
     AND (EntityId=@HocVienIdText OR EntityKey=@SourceKey OR EntityKey=@MaDK)) AS DocumentRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_PrintExportLog
   WHERE (EntityType LIKE N'%HocVien%' OR EntityType LIKE N'%Student%')
     AND EntityKey IN(@HocVienIdText,@SourceKey,@MaDK)) AS PrintExportRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_AuditLog
   WHERE CHARINDEX(@SourceKey,COALESCE(DuLieuTruoc,N''))>0
      OR CHARINDEX(@SourceKey,COALESCE(DuLieuSau,N''))>0
      OR CHARINDEX(@SourceKey,COALESCE(Loi,N''))>0) AS AuditPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_DongBoLog
   WHERE CHARINDEX(@SourceKey,COALESCE(DetailJson,N''))>0
      OR CHARINDEX(@SourceKey,COALESCE(ErrorMessage,N''))>0) AS SyncPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_ImportBatch
   WHERE CHARINDEX(@SourceKey,COALESCE(KetQuaJson,N''))>0) AS ImportPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_PrintExportLog
   WHERE CHARINDEX(@SourceKey,COALESCE(FilterJson,N''))>0) AS PrintPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_TaiLieu
   WHERE CHARINDEX(@SourceKey,COALESCE(KetQuaDocJson,N''))>0) AS DocumentPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun
   WHERE CHARINDEX(@SourceKey,COALESCE(OtoResultJson,N''))>0
      OR CHARINDEX(@SourceKey,COALESCE(MotoResultJson,N''))>0) AS AutoSyncPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_QlhvSyncOperationHistory
   WHERE CHARINDEX(@SourceKey,COALESCE(DetailJson,N''))>0) AS OperationPayloadRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeManualReview
   WHERE PlanHash=@PlanHash) AS ManualReviewRows,
  (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeCycleHistory
   WHERE CycleId=@CycleId) AS CycleHistoryRows;
'@
    $parameters = @{
        '@HocVienIdText' = [string]$HocVienId
        '@SourceKey' = $SourceKey
        '@MaDK' = $MaDk
        '@PlanHash' = $ExpectedPlanHash
        '@CycleId' = [Guid]$expectedCycleId
    }
    $table = Read-Table $Target $sql $parameters $Transaction
    Assert-Exact ($table.Rows.Count -eq 1) 'DOWNSTREAM_QUERY_CARDINALITY_REJECTED'
    return $table.Rows[0]
}

function Assert-ZeroDownstream([Data.DataRow]$Snapshot) {
    foreach ($column in $Snapshot.Table.Columns) {
        Assert-Exact ([long]$Snapshot[$column.ColumnName] -eq 0) (
            'CANARY_TARGET_HAS_DOWNSTREAM_USE_' + $column.ColumnName.ToUpperInvariant())
    }
}

function Get-ControlSnapshot(
    [Data.SqlClient.SqlConnection]$Target,
    [Data.SqlClient.SqlTransaction]$Transaction = $null
) {
    $sql = @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun
  WHERE Status IN(N'QUEUED',N'RUNNING') OR ActiveSlot=1) AS ActiveRunOrSlot,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvSyncOperationHistory
  WHERE Status IN(N'QUEUED',N'RUNNING')) AS ActiveOperations,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvAutoSyncRun) AS AutoSyncHistoryRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeWorkerState
  WHERE CycleActive=1) AS ActiveRealtimeCycles,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeWorkerState
  WHERE Status=N'BLOCKED' AND CycleActive=0 AND LastErrorCode=N'RT03_UNSUPPORTED_DRIFT') AS ExpectedBlockedWorkerRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeFeatureState
  WHERE FeatureStateId=1 AND EnableProductionRealtime=1 AND EnableProductionShadow=1
    AND EnableProductionWrites=1 AND EnableProductionCanary=0
    AND EnableControlledCutover=1 AND EnableProductionDeletes=0) AS ExactFeatureRows;
'@
    $table = Read-Table $Target $sql @{} $Transaction
    Assert-Exact ($table.Rows.Count -eq 1) 'CONTROL_QUERY_CARDINALITY_REJECTED'
    return $table.Rows[0]
}

function Assert-ControlSnapshot([Data.DataRow]$Snapshot) {
    Assert-Exact ([long]$Snapshot.ActiveRunOrSlot -eq 0) 'AUTOSYNC_ACTIVE_REJECTED'
    Assert-Exact ([long]$Snapshot.ActiveOperations -eq 0) 'AUTOSYNC_OPERATION_ACTIVE_REJECTED'
    Assert-Exact ([long]$Snapshot.AutoSyncHistoryRows -eq 11) 'AUTOSYNC_HISTORY_CHANGED'
    Assert-Exact ([long]$Snapshot.ActiveRealtimeCycles -eq 0) 'REALTIME_CYCLE_ACTIVE_REJECTED'
    Assert-Exact ([long]$Snapshot.ExpectedBlockedWorkerRows -eq 1) 'WORKER_STATE_REJECTED'
    Assert-Exact ([long]$Snapshot.ExactFeatureRows -eq 1) 'CONTROLLED_CUTOVER_STATE_REJECTED'
}

function Get-TransactionGuard([Data.SqlClient.SqlConnection]$Target) {
    $sql = @'
SELECT
 (SELECT COUNT_BIG(*)
  FROM sys.dm_tran_database_transactions databaseTransaction
  INNER JOIN sys.dm_tran_session_transactions sessionTransaction
    ON sessionTransaction.transaction_id=databaseTransaction.transaction_id
  WHERE databaseTransaction.database_id=DB_ID()
    AND sessionTransaction.session_id<>@@SPID) AS OtherTransactions,
 (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests
  WHERE database_id=DB_ID() AND session_id<>@@SPID
    AND (blocking_session_id<>0 OR wait_type LIKE N'LCK%')) AS BlockedRequests,
 (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests
  WHERE database_id=DB_ID() AND session_id<>@@SPID
    AND command NOT IN(N'AWAITING COMMAND')) AS OtherActiveRequests,
 (SELECT COUNT_BIG(*) FROM sys.dm_tran_locks
  WHERE resource_database_id=DB_ID() AND request_session_id<>@@SPID
    AND resource_type IN(N'OBJECT',N'KEY',N'PAGE',N'RID')
    AND resource_associated_entity_id IN(
      OBJECT_ID(N'dbo.App_HocVien'),
      OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyMarker'),
      OBJECT_ID(N'dbo.App_QlhvDirectRealtimeApplyCheckpoint'))) AS RelevantLocks;
'@
    $table = Read-Table $Target $sql
    Assert-Exact ($table.Rows.Count -eq 1) 'TRANSACTION_GUARD_CARDINALITY_REJECTED'
    return $table.Rows[0]
}

function Assert-TransactionGuard([Data.DataRow]$Snapshot) {
    Assert-Exact ([long]$Snapshot.OtherTransactions -eq 0) 'OTHER_TRANSACTION_ACTIVE_REJECTED'
    Assert-Exact ([long]$Snapshot.BlockedRequests -eq 0) 'BLOCKED_REQUEST_REJECTED'
    Assert-Exact ([long]$Snapshot.RelevantLocks -eq 0) 'RELEVANT_LOCK_REJECTED'
}

function Write-PrivacySafeJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

Assert-Exact ((git branch --show-current).Trim() -eq $expectedBranch) 'REPOSITORY_BRANCH_REJECTED'
Assert-Exact ((git rev-parse HEAD).Trim() -eq $expectedHead) 'REPOSITORY_HEAD_REJECTED'
Assert-Exact (@(git diff --cached --name-only).Count -eq 0) 'STAGED_FILES_REJECTED'
foreach ($protected in @(
    'server\QLHV.Api\appsettings.Development.json',
    'server\QLHV.Worker\appsettings.Development.json')) {
    $hash = (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash
    Assert-Exact ($hash -eq $expectedProtectedHash) 'PROTECTED_CONFIG_HASH_REJECTED'
}

$planPath = Join-Path -Path $PlanRoot -ChildPath '04_sealed_canary_plan.json'
$keyPath = Join-Path -Path $PlanRoot -ChildPath '04_sealed_canary_plan.key'
$executionPath = Join-Path -Path $PlanRoot -ChildPath '08_canary_execution_result.json'
Assert-Exact (Test-Path -LiteralPath $planPath -PathType Leaf) 'SEALED_PLAN_MISSING'
Assert-Exact (Test-Path -LiteralPath $keyPath -PathType Leaf) 'SEALED_KEY_MISSING'
Assert-Exact (Test-Path -LiteralPath $executionPath -PathType Leaf) 'CANARY_EXECUTION_PROOF_MISSING'
$planBytesOne = [IO.File]::ReadAllBytes($planPath)
$planBytesTwo = [IO.File]::ReadAllBytes($planPath)
Assert-Exact ([Linq.Enumerable]::SequenceEqual($planBytesOne, $planBytesTwo)) 'SEALED_PLAN_NOT_BYTE_IDENTICAL'
Assert-Exact ((Get-Sha256Hex $planBytesOne) -eq $expectedPlanFileHash) 'SEALED_PLAN_FILE_HASH_REJECTED'
$key = [IO.File]::ReadAllBytes($keyPath)
Assert-Exact ($key.Length -eq 32) 'SEALED_KEY_LENGTH_REJECTED'
$plan = [Text.Encoding]::UTF8.GetString($planBytesOne) | ConvertFrom-Json
$execution = Get-Content -LiteralPath $executionPath -Raw | ConvertFrom-Json
$candidate = $plan.payload.plan.candidates[0]
Assert-Exact ($plan.payload.plan.planHash -eq $ExpectedPlanHash) 'PLAN_HASH_REJECTED'
Assert-Exact ($plan.payload.plan.mode -eq 'CANARY') 'PLAN_MODE_REJECTED'
Assert-Exact ($plan.payload.plan.environmentId -eq 'PRODUCTION') 'PLAN_ENVIRONMENT_REJECTED'
Assert-Exact (@($plan.payload.plan.candidates).Count -eq 1) 'PLAN_CANDIDATE_CARDINALITY_REJECTED'
Assert-Exact ($candidate.candidateId -eq 'OTO-INSERT-01') 'CANDIDATE_ID_REJECTED'
Assert-Exact ($candidate.sourceProfile -eq 'CSDT_OTO') 'CANDIDATE_PROFILE_REJECTED'
Assert-Exact ([int]$candidate.kind -eq 0) 'CANDIDATE_KIND_REJECTED'
Assert-Exact ($candidate.expectedMutation -eq 'INSERT_EXACT_ONE_APP_HOCVIEN') 'EXPECTED_MUTATION_REJECTED'
Assert-Exact ($candidate.beforeSourceOwnedHash -eq $expectedCandidateHash) 'CANDIDATE_SOURCE_HASH_REJECTED'
Assert-Exact ($candidate.beforeQlhvOwnedHash -eq $plan.payload.preexistingOtoQlhvOwnedHash) 'CANDIDATE_QLHV_HASH_REJECTED'
Assert-Exact ((Get-Sha256Hex $key) -eq ([string]$plan.payload.keySha256).ToUpperInvariant()) 'SEALED_KEY_HASH_REJECTED'
Assert-Exact ($execution.planHash -eq $ExpectedPlanHash) 'EXECUTION_PLAN_HASH_REJECTED'
Assert-Exact ($execution.cycleId -eq $expectedCycleId) 'EXECUTION_CYCLE_REJECTED'
Assert-Exact ($execution.candidateHmac -eq $candidate.identityHmac) 'EXECUTION_HMAC_REJECTED'
Assert-Exact ([int]$execution.insertedRows -eq 1) 'EXECUTION_INSERT_COUNT_REJECTED'
Assert-Exact ([int]$execution.updatedRows -eq 0) 'EXECUTION_UPDATE_COUNT_REJECTED'
Assert-Exact ([int]$execution.deletedOrDeactivatedRows -eq 0) 'EXECUTION_DELETE_COUNT_REJECTED'

$target = Open-SqlConnection $targetConnectionString
$source = Open-SqlConnection $sourceConnectionString
$transaction = $null
$appLockHeld = $false
try {
    $identitySql = @'
SELECT CONVERT(nvarchar(128),SERVERPROPERTY(N'ServerName')) ServerIdentity,
       DB_NAME() DatabaseName,DB_ID() DatabaseId,
       CONVERT(nvarchar(36),database_guid) DatabaseGuid
FROM sys.database_recovery_status WHERE database_id=DB_ID();
'@
    $identity = Read-Table $target $identitySql
    Assert-Exact ($identity.Rows.Count -eq 1) 'TARGET_IDENTITY_CARDINALITY_REJECTED'
    $identityRow = $identity.Rows[0]
    Assert-Exact ([string]$identityRow.ServerIdentity -eq $expectedServer) 'TARGET_SERVER_REJECTED'
    Assert-Exact ([string]$identityRow.DatabaseName -eq $expectedTargetDatabase) 'TARGET_DATABASE_REJECTED'
    Assert-Exact ([int]$identityRow.DatabaseId -eq $expectedTargetDatabaseId) 'TARGET_DATABASE_ID_REJECTED'
    Assert-Exact ([string]$identityRow.DatabaseGuid -eq $expectedTargetDatabaseGuid) 'TARGET_DATABASE_GUID_REJECTED'

    $candidateSql = @'
SELECT * FROM dbo.App_HocVien
WHERE SourceProfileCode=N'CSDT_OTO'
  AND CreatedBy=N'Rt03DirectRealtimeCanary'
  AND V2RowHash=@SourceHash
  AND IsDeleted=0;
'@
    $candidateRows = Read-Table $target $candidateSql @{ '@SourceHash' = $expectedCandidateHash }
    Assert-Exact ($candidateRows.Rows.Count -eq 1) 'EXACT_CANARY_TARGET_NOT_PROVEN'
    $candidateRow = $candidateRows.Rows[0]
    $hocVienId = [long]$candidateRow.HocVienId
    $sourceKey = [string]$candidateRow.SourceMaDK
    $maDk = [string]$candidateRow.MaDK
    $rowVersion = [byte[]]$candidateRow.RowVersion

    $secret = Convert-BytesToHex $key
    $hmacMessage = 'RT03-HMAC-SHA256-v1|candidate-identity|CSDT_OTO|' + $sourceKey.Trim()
    $identityHmac = 'RT03-HMAC-SHA256-v1:' +
        (Get-HmacHex ([Text.Encoding]::UTF8.GetBytes($secret)) $hmacMessage)
    Assert-Exact ($identityHmac -eq $candidate.identityHmac) 'CANDIDATE_IDENTITY_HMAC_REJECTED'
    $expectedRollbackImageHash = Get-TextSha256 ('ABSENT|CSDT_OTO|' + $identityHmac)
    Assert-Exact ($expectedRollbackImageHash -eq $candidate.rollbackImageHash) 'ROLLBACK_IMAGE_HASH_REJECTED'

    $markerMessage = $expectedCycleId.ToLowerInvariant() + '|' + $ExpectedPlanHash + '|' +
        $identityHmac + '|' + $expectedCandidateHash
    $markerSha = [Security.Cryptography.SHA256]::Create()
    try {
        $expectedMarkerHash = $markerSha.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($markerMessage))
    }
    finally {
        $markerSha.Dispose()
    }
    $markerSql = @'
SELECT MarkerHash,DispositionHash,InsertedRows,UpdatedRows,RetainedRows,
       SourceChangeTrackingVersion,SourceDatabaseGuid
FROM dbo.App_QlhvDirectRealtimeApplyMarker
WHERE CycleId=@CycleId AND PlanHash=@PlanHash;
'@
    $marker = Read-Table $target $markerSql @{
        '@CycleId' = [Guid]$expectedCycleId
        '@PlanHash' = $ExpectedPlanHash
    }
    Assert-Exact ($marker.Rows.Count -eq 1) 'CANARY_MARKER_REJECTED'
    $markerRow = $marker.Rows[0]
    Assert-Exact ([Linq.Enumerable]::SequenceEqual([byte[]]$markerRow.MarkerHash,$expectedMarkerHash)) 'CANARY_MARKER_HASH_REJECTED'
    Assert-Exact ([int]$markerRow.InsertedRows -eq 1) 'CANARY_MARKER_INSERT_REJECTED'
    Assert-Exact ([int]$markerRow.UpdatedRows -eq 0) 'CANARY_MARKER_UPDATE_REJECTED'
    Assert-Exact ([int]$markerRow.RetainedRows -eq 0) 'CANARY_MARKER_RETAIN_REJECTED'
    Assert-Exact ([long]$markerRow.SourceChangeTrackingVersion -eq 0) 'CANARY_MARKER_CT_REJECTED'
    Assert-Exact ([Guid]$markerRow.SourceDatabaseGuid -eq [Guid]$expectedSourceDatabaseGuid) 'CANARY_MARKER_SOURCE_REJECTED'

    $checkpointSql = @'
SELECT MarkerHash,SourceChangeTrackingVersion,SourceDatabaseGuid,CycleId,PlanHash,
       MappingFingerprint,EnvironmentId,Mode
FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
WHERE SourceProfileCode=N'CSDT_OTO';
'@
    $checkpoint = Read-Table $target $checkpointSql
    Assert-Exact ($checkpoint.Rows.Count -eq 1) 'OTO_CHECKPOINT_REJECTED'
    $checkpointRow = $checkpoint.Rows[0]
    Assert-Exact ([long]$checkpointRow.SourceChangeTrackingVersion -eq 0) 'OTO_CHECKPOINT_VERSION_REJECTED'
    Assert-Exact ([Guid]$checkpointRow.CycleId -eq [Guid]$expectedCycleId) 'OTO_CHECKPOINT_CYCLE_REJECTED'
    Assert-Exact ([string]$checkpointRow.PlanHash -eq $ExpectedPlanHash) 'OTO_CHECKPOINT_PLAN_REJECTED'
    Assert-Exact ([Linq.Enumerable]::SequenceEqual([byte[]]$checkpointRow.MarkerHash,$expectedMarkerHash)) 'OTO_CHECKPOINT_MARKER_REJECTED'

    $aliasSql = @'
SELECT
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE HocVienId<>@HocVienId AND IsDeleted=0
   AND SourceProfileCode=N'CSDT_OTO' AND SourceMaDK=@SourceKey) ExactIdentityAliases,
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE HocVienId<>@HocVienId AND IsDeleted=0
   AND (MaDK=@MaDK OR MaDK_V1=@MaDK OR MaDK_V2=@MaDK OR SourceMaDK=@MaDK)) RegistrationAliases,
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE HocVienId<>@HocVienId AND IsDeleted=0
   AND SoCCCD IS NOT NULL AND LTRIM(RTRIM(SoCCCD))<>N'' AND SoCCCD=@SoCCCD) NationalIdAliases,
 (SELECT COUNT_BIG(*) FROM
   (SELECT SourceProfileCode,SourceMaDK FROM dbo.App_HocVien WHERE IsDeleted=0
    GROUP BY SourceProfileCode,SourceMaDK HAVING COUNT_BIG(*)>1) duplicateRows) DuplicateActiveIdentities,
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0) OtoActiveRows,
 (SELECT COUNT_BIG(*) FROM dbo.App_HocVien
  WHERE SourceProfileCode=N'CSDT_MOTO' AND IsDeleted=0) MotoActiveRows;
'@
    $aliases = Read-Table $target $aliasSql @{
        '@HocVienId' = $hocVienId
        '@SourceKey' = $sourceKey
        '@MaDK' = $maDk
        '@SoCCCD' = if ($candidateRow.SoCCCD -is [DBNull]) { '' } else { [string]$candidateRow.SoCCCD }
    }
    Assert-Exact ($aliases.Rows.Count -eq 1) 'ALIAS_QUERY_REJECTED'
    foreach ($column in @(
        'ExactIdentityAliases','RegistrationAliases','NationalIdAliases',
        'DuplicateActiveIdentities')) {
        Assert-Exact ([long]$aliases.Rows[0][$column] -eq 0) ('IDENTITY_ALIAS_REJECTED_' + $column)
    }
    Assert-Exact ([long]$aliases.Rows[0].OtoActiveRows -eq 157) 'PREFLIGHT_OTO_COUNT_REJECTED'
    Assert-Exact ([long]$aliases.Rows[0].MotoActiveRows -eq 5) 'PREFLIGHT_MOTO_COUNT_REJECTED'

    for ($sample = 0; $sample -lt 3; $sample++) {
        Assert-SourceSnapshot (Get-SourceSnapshot $source $sourceKey)
        Assert-ControlSnapshot (Get-ControlSnapshot $target)
        Assert-TransactionGuard (Get-TransactionGuard $target)
    }
    $downstream = Get-DownstreamSnapshot $target $hocVienId $sourceKey $maDk
    Assert-ZeroDownstream $downstream
    $qlhvOwnedHashBefore = Get-QlhvOwnedHash $target $key $hocVienId
    Assert-Exact ($qlhvOwnedHashBefore -eq $plan.payload.preexistingOtoQlhvOwnedHash) 'QLHV_OWNED_HASH_REJECTED'
    $otherRowsBefore = Read-Table $target @'
SELECT * FROM dbo.App_HocVien WHERE HocVienId<>@HocVienId ORDER BY HocVienId;
'@ @{ '@HocVienId' = $hocVienId }
    $otherLearnersHashBefore = Get-TableHash $otherRowsBefore
    $candidateRowHashBefore = Get-TableHash $candidateRows

    $preflight = [ordered]@{
        Evidence = 'RT04_EXACT_CANARY_ROLLBACK_PREFLIGHT'
        CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
        Decision = 'ROLLBACK_CANARY_TARGET'
        ExecuteRequested = [bool]$Execute
        PlanHash = $ExpectedPlanHash
        PlanFileSha256 = $expectedPlanFileHash
        CycleId = $expectedCycleId
        CandidateHmac = $identityHmac
        CandidateInternalId = $hocVienId
        CandidateRowVersion = Convert-BytesToHex $rowVersion
        CandidateRowHash = $candidateRowHashBefore
        SourceRows = 156
        TargetActiveRows = 157
        MotoActiveRows = 5
        SourceExactRows = 0
        DownstreamReferenceRows = 0
        PhysicalForeignKeyDefinitions = 0
        QlhvOwnedHashExcludingCandidate = $qlhvOwnedHashBefore
        OtherLearnersHash = $otherLearnersHashBefore
        OtoCheckpoint = 0
        AutoSyncHistoryRows = 11
        ActiveRunSlotOperation = '0/0/0'
        ActiveRealtimeCycles = 0
        ProtectedConfigsVerified = $true
        StagedFiles = 0
        BusinessDataWrites = 0
    }

    if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
        $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss')
        $EvidenceRoot = "D:\QLHV_RT04_EVIDENCE\RT04_CANARY_TARGET_ROLLBACK_$stamp"
    }
    $EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
    Assert-Exact ($EvidenceRoot.StartsWith('D:\QLHV_RT04_EVIDENCE\',[StringComparison]::OrdinalIgnoreCase)) 'EVIDENCE_ROOT_REJECTED'
    [IO.Directory]::CreateDirectory($EvidenceRoot) | Out-Null
    Write-PrivacySafeJson `
        (Join-Path -Path $EvidenceRoot -ChildPath '01_preflight_privacy_safe.json') `
        $preflight

    if (-not $Execute) {
        Write-Output "RT04_EXACT_CANARY_ROLLBACK_PREFLIGHT_PASS evidence=$EvidenceRoot"
        return
    }

    $lockCommand = New-SqlCommand $target @'
DECLARE @Result int;
EXEC @Result=sys.sp_getapplock @Resource=N'QLHV:CSDT_AUTO_SYNC',
 @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public';
SELECT @Result;
'@
    try {
        $lockResult = [int]$lockCommand.ExecuteScalar()
    }
    finally {
        $lockCommand.Dispose()
    }
    Assert-Exact ($lockResult -ge 0) 'GLOBAL_MUTUAL_EXCLUSION_LOCK_REJECTED'
    $appLockHeld = $true
    Assert-ControlSnapshot (Get-ControlSnapshot $target)
    Assert-TransactionGuard (Get-TransactionGuard $target)

    $transaction = $target.BeginTransaction([Data.IsolationLevel]::Serializable)
    $lockedCandidates = Read-Table $target @'
SELECT * FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
WHERE HocVienId=@HocVienId AND SourceProfileCode=N'CSDT_OTO'
  AND SourceMaDK=@SourceKey AND MaDK=@MaDK
  AND CreatedBy=N'Rt03DirectRealtimeCanary' AND V2RowHash=@SourceHash
  AND RowVersion=@RowVersion AND IsDeleted=0
  AND GhiChuNoiBo IS NULL AND DaDoiChieuCCCD=0 AND DaInThe=0 AND DaTaoXML=0
  AND UpdatedBy IS NULL AND DeletedBy IS NULL AND DeleteReason IS NULL;
'@ @{
        '@HocVienId' = $hocVienId
        '@SourceKey' = $sourceKey
        '@MaDK' = $maDk
        '@SourceHash' = $expectedCandidateHash
        '@RowVersion' = $rowVersion
    } $transaction
    Assert-Exact ($lockedCandidates.Rows.Count -eq 1) 'LOCKED_CANARY_REVALIDATION_REJECTED'
    Assert-Exact ((Get-TableHash $lockedCandidates) -eq $candidateRowHashBefore) 'LOCKED_CANARY_HASH_REJECTED'
    Assert-ControlSnapshot (Get-ControlSnapshot $target $transaction)
    Assert-ZeroDownstream (Get-DownstreamSnapshot $target $hocVienId $sourceKey $maDk $transaction)
    Assert-Exact ((Get-QlhvOwnedHash $target $key $hocVienId $transaction) -eq $qlhvOwnedHashBefore) 'LOCKED_QLHV_HASH_REJECTED'
    $lockedOtherRows = Read-Table $target @'
SELECT * FROM dbo.App_HocVien WITH (UPDLOCK,HOLDLOCK)
WHERE HocVienId<>@HocVienId ORDER BY HocVienId;
'@ @{ '@HocVienId' = $hocVienId } $transaction
    Assert-Exact ((Get-TableHash $lockedOtherRows) -eq $otherLearnersHashBefore) 'LOCKED_OTHER_LEARNERS_HASH_REJECTED'

    Assert-SourceSnapshot (Get-SourceSnapshot $source $sourceKey)

    $backupPayload = [ordered]@{
        Contract = 'RT04_EXACT_CANARY_BACKUP_V1'
        CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
        Decision = 'ROLLBACK_CANARY_TARGET'
        PlanHash = $ExpectedPlanHash
        CycleId = $expectedCycleId
        CandidateHmac = $identityHmac
        CandidateRowHash = $candidateRowHashBefore
        QlhvOwnedHashExcludingCandidate = $qlhvOwnedHashBefore
        OtherLearnersHash = $otherLearnersHashBefore
        MarkerHash = Convert-BytesToHex $expectedMarkerHash
        CheckpointVersion = 0
        DependentDeleteAllowlist = @()
        ExactRowImage = Convert-RowToBackupObject $lockedCandidates.Rows[0]
    }
    $backupJson = $backupPayload | ConvertTo-Json -Depth 12 -Compress
    $backupPlainBytes = [Text.Encoding]::UTF8.GetBytes($backupJson)
    Add-Type -AssemblyName System.Security
    $backupEncrypted = [Security.Cryptography.ProtectedData]::Protect(
        $backupPlainBytes,
        [Text.Encoding]::UTF8.GetBytes('RT04_EXACT_CANARY_BACKUP_V1'),
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $backupPath = Join-Path `
        -Path $EvidenceRoot `
        -ChildPath '02_exact_row_image.dpapi'
    [IO.File]::WriteAllBytes($backupPath,$backupEncrypted)
    $backupCipherHash = Get-Sha256Hex $backupEncrypted
    [Array]::Clear($backupPlainBytes,0,$backupPlainBytes.Length)
    $backupJson = $null

    $delete = New-SqlCommand $target @'
DELETE FROM dbo.App_HocVien
WHERE HocVienId=@HocVienId AND SourceProfileCode=N'CSDT_OTO'
  AND SourceMaDK=@SourceKey AND MaDK=@MaDK
  AND CreatedBy=N'Rt03DirectRealtimeCanary' AND V2RowHash=@SourceHash
  AND RowVersion=@RowVersion AND IsDeleted=0
  AND GhiChuNoiBo IS NULL AND DaDoiChieuCCCD=0 AND DaInThe=0 AND DaTaoXML=0
  AND UpdatedBy IS NULL AND DeletedBy IS NULL AND DeleteReason IS NULL;
'@ $transaction
    try {
        Add-Parameter $delete '@HocVienId' ([Data.SqlDbType]::BigInt) $hocVienId | Out-Null
        Add-Parameter $delete '@SourceKey' ([Data.SqlDbType]::NVarChar) $sourceKey 50 | Out-Null
        Add-Parameter $delete '@MaDK' ([Data.SqlDbType]::NVarChar) $maDk 50 | Out-Null
        Add-Parameter $delete '@SourceHash' ([Data.SqlDbType]::NVarChar) $expectedCandidateHash 128 | Out-Null
        Add-Parameter $delete '@RowVersion' ([Data.SqlDbType]::Binary) $rowVersion 8 | Out-Null
        $affectedLearners = $delete.ExecuteNonQuery()
    }
    finally {
        $delete.Dispose()
    }
    Assert-Exact ($affectedLearners -eq 1) 'DELETE_AFFECTED_ROWS_REJECTED'

    $auditBefore = [ordered]@{
        PlanHash = $ExpectedPlanHash
        CycleId = $expectedCycleId
        CandidateHmac = $identityHmac
        CandidateRowHash = $candidateRowHashBefore
        BackupCipherSha256 = $backupCipherHash
        AffectedDependentRows = 0
    } | ConvertTo-Json -Compress
    $auditAfter = [ordered]@{
        ExactCanaryTargetDeleted = $true
        AffectedLearnerRows = 1
        CheckpointManuallyAdvanced = $false
        MarkerDeleted = $false
    } | ConvertTo-Json -Compress
    $audit = New-SqlCommand $target @'
INSERT INTO dbo.App_AuditLog
(ChucNang,HanhDong,EntityType,EntityId,EntityKey,DuLieuTruoc,DuLieuSau,
 KetQua,Loi,CreatedAt,CreatedBy,ClientIp,UserAgent)
VALUES
(N'CSDT_REALTIME',N'ROLLBACK_CANARY_TARGET',N'App_HocVien',@EntityId,
 @CandidateHmac,@Before,@After,N'SUCCESS',NULL,SYSUTCDATETIME(),
 N'CodexRt04Operator',NULL,N'RT04 exact operator-approved rollback');
'@ $transaction
    try {
        Add-Parameter $audit '@EntityId' ([Data.SqlDbType]::NVarChar) ([string]$hocVienId) 100 | Out-Null
        Add-Parameter $audit '@CandidateHmac' ([Data.SqlDbType]::NVarChar) $identityHmac 200 | Out-Null
        Add-Parameter $audit '@Before' ([Data.SqlDbType]::NVarChar) $auditBefore -1 | Out-Null
        Add-Parameter $audit '@After' ([Data.SqlDbType]::NVarChar) $auditAfter -1 | Out-Null
        $affectedAudit = $audit.ExecuteNonQuery()
    }
    finally {
        $audit.Dispose()
    }
    Assert-Exact ($affectedAudit -eq 1) 'ROLLBACK_AUDIT_AFFECTED_ROWS_REJECTED'

    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE HocVienId=@HocVienId;
'@ @{ '@HocVienId' = $hocVienId } $transaction) -eq 0) 'TRANSACTION_TARGET_STILL_PRESENT'
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0;
'@ @{} $transaction) -eq 156) 'TRANSACTION_OTO_COUNT_REJECTED'
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE SourceProfileCode=N'CSDT_MOTO' AND IsDeleted=0;
'@ @{} $transaction) -eq 5) 'TRANSACTION_MOTO_COUNT_REJECTED'
    $otherRowsAfterDelete = Read-Table $target @'
SELECT * FROM dbo.App_HocVien ORDER BY HocVienId;
'@ @{} $transaction
    Assert-Exact ((Get-TableHash $otherRowsAfterDelete) -eq $otherLearnersHashBefore) 'TRANSACTION_OTHER_LEARNERS_CHANGED'
    Assert-Exact ((Get-QlhvOwnedHash $target $key $hocVienId $transaction) -eq $qlhvOwnedHashBefore) 'TRANSACTION_QLHV_OWNED_HASH_CHANGED'
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
WHERE SourceProfileCode=N'CSDT_OTO' AND SourceChangeTrackingVersion=0
 AND CycleId=@CycleId AND PlanHash=@PlanHash;
'@ @{ '@CycleId'=[Guid]$expectedCycleId; '@PlanHash'=$ExpectedPlanHash } $transaction) -eq 1) 'TRANSACTION_CHECKPOINT_CHANGED'
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_QlhvDirectRealtimeApplyMarker
WHERE CycleId=@CycleId AND PlanHash=@PlanHash;
'@ @{ '@CycleId'=[Guid]$expectedCycleId; '@PlanHash'=$ExpectedPlanHash } $transaction) -eq 1) 'TRANSACTION_MARKER_CHANGED'
    Assert-ControlSnapshot (Get-ControlSnapshot $target $transaction)

    $transaction.Commit()
    $transaction.Dispose()
    $transaction = $null

    Assert-SourceSnapshot (Get-SourceSnapshot $source $sourceKey)
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE HocVienId=@HocVienId;
'@ @{ '@HocVienId'=$hocVienId }) -eq 0) 'POSTCOMMIT_TARGET_STILL_PRESENT'
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE SourceProfileCode=N'CSDT_OTO' AND IsDeleted=0;
'@) -eq 156) 'POSTCOMMIT_OTO_COUNT_REJECTED'
    Assert-Exact ([long](Read-Scalar $target @'
SELECT COUNT_BIG(*) FROM dbo.App_HocVien WHERE SourceProfileCode=N'CSDT_MOTO' AND IsDeleted=0;
'@) -eq 5) 'POSTCOMMIT_MOTO_COUNT_REJECTED'
    $postRows = Read-Table $target 'SELECT * FROM dbo.App_HocVien ORDER BY HocVienId;'
    $otherLearnersHashAfter = Get-TableHash $postRows
    Assert-Exact ($otherLearnersHashAfter -eq $otherLearnersHashBefore) 'POSTCOMMIT_OTHER_LEARNERS_CHANGED'
    $qlhvOwnedHashAfter = Get-QlhvOwnedHash $target $key $hocVienId
    Assert-Exact ($qlhvOwnedHashAfter -eq $qlhvOwnedHashBefore) 'POSTCOMMIT_QLHV_OWNED_HASH_CHANGED'
    Assert-ControlSnapshot (Get-ControlSnapshot $target)

    $post = [ordered]@{
        Evidence = 'RT04_EXACT_CANARY_ROLLBACK_COMMITTED'
        CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
        Decision = 'ROLLBACK_CANARY_TARGET'
        PlanHash = $ExpectedPlanHash
        CycleId = $expectedCycleId
        CandidateHmac = $identityHmac
        CandidateInternalId = $hocVienId
        CandidateRowHashBefore = $candidateRowHashBefore
        CandidateRowsAfter = 0
        AffectedLearnerRows = $affectedLearners
        AffectedDependentRows = 0
        AffectedAuditRows = $affectedAudit
        BackupFile = [IO.Path]::GetFileName($backupPath)
        BackupProtection = 'DPAPI_CURRENT_USER'
        BackupCipherSha256 = $backupCipherHash
        OtoSourceTarget = '156/156'
        MotoSourceTarget = '5/5'
        OtherLearnersHashBefore = $otherLearnersHashBefore
        OtherLearnersHashAfter = $otherLearnersHashAfter
        QlhvOwnedHashBefore = $qlhvOwnedHashBefore
        QlhvOwnedHashAfter = $qlhvOwnedHashAfter
        MarkerPreserved = $true
        CheckpointPreservedAt = 0
        AutoSyncHistoryRows = 11
        ActiveRunSlotOperation = '0/0/0'
        BusinessMutation = 'DELETE_EXACT_ONE_CANARY_TARGET_PLUS_ONE_PRIVACY_SAFE_AUDIT'
    }
    Write-PrivacySafeJson `
        (Join-Path -Path $EvidenceRoot -ChildPath '03_post_rollback_privacy_safe.json') `
        $post
    Write-Output "RT04_EXACT_CANARY_ROLLBACK_VERIFIED evidence=$EvidenceRoot"
}
catch {
    if ($null -ne $transaction) {
        try { $transaction.Rollback() } catch { }
        $transaction.Dispose()
        $transaction = $null
    }
    if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot) -and
        (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
        Write-PrivacySafeJson `
            (Join-Path -Path $EvidenceRoot -ChildPath '99_failure_privacy_safe.json') `
            ([ordered]@{
            Evidence = 'RT04_EXACT_CANARY_ROLLBACK_BLOCKED'
            FailedAtUtc = [DateTime]::UtcNow.ToString('O')
            PlanHash = $ExpectedPlanHash
            ErrorCode = $_.Exception.Message
            TransactionRolledBack = $true
            })
    }
    throw
}
finally {
    if ($appLockHeld -and $target.State -eq [Data.ConnectionState]::Open) {
        $release = New-SqlCommand $target @'
EXEC sys.sp_releaseapplock @Resource=N'QLHV:CSDT_AUTO_SYNC',
 @LockOwner=N'Session',@DbPrincipal=N'public';
'@
        try { [void]$release.ExecuteNonQuery() } catch { }
        $release.Dispose()
    }
    if ($null -ne $source) { $source.Dispose() }
    if ($null -ne $target) { $target.Dispose() }
    if ($null -ne $key) { [Array]::Clear($key,0,$key.Length) }
}
