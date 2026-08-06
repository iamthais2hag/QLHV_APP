[CmdletBinding()]
param(
    [string]$Server = 'CSDLTTTC',
    [string]$OutputPath =
        'D:\QLHV_APP\.runlogs\rt03-khoahoc-schema-rehearsal-v2.json'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$prerequisite = Join-Path $repoRoot `
    'database\patches\20260730_rt03_support_khoahoc_business_identity.sql'
$rollback = Join-Path $repoRoot `
    'database\patches\20260730_rollback_rt03_khoahoc_business_identity.sql'
$baseline = Join-Path $PSScriptRoot `
    '20260731_rt03_khoahoc_schema_baseline.sql'
$databaseName =
    'QLHV_RT03_KHOAHOC_REHEARSAL_' +
    (Get-Date -Format 'yyyyMMdd_HHmmss') +
    "_$PID"

if ($databaseName -notmatch
    '\AQLHV_RT03_KHOAHOC_REHEARSAL_[0-9]{8}_[0-9]{6}_[0-9]+\z') {
    throw "Unsafe rehearsal database name: $databaseName"
}

$steps = [System.Collections.Generic.List[object]]::new()
$databaseCreated = $false
$cleanupPassed = $false
$sessionFailureScript = $null

function Add-Step {
    param(
        [string]$Name,
        [string]$Result,
        [string]$Evidence
    )

    $steps.Add([pscustomobject]@{
        name = $Name
        result = $Result
        evidence = $Evidence.Trim()
    })
}

function Invoke-SqlcmdInline {
    param(
        [string]$Database,
        [string]$Query
    )

    $output = & sqlcmd -S $Server -d $Database -E -b -W -h -1 -Q $Query 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()
    if ($exitCode -ne 0) {
        throw "sqlcmd inline failed (database=$Database): $text"
    }

    return $text
}

function Invoke-Migration {
    param(
        [string]$InputFile,
        [string]$DatabaseId,
        [string]$DatabaseGuid,
        [string]$ForceFailureStep = 'NONE',
        [string]$ExpectedFailure
    )

    $arguments = @(
        '-S', $Server,
        '-d', $databaseName,
        '-E',
        '-b',
        '-W',
        '-i', $InputFile,
        '-v',
        "Rt03TargetDatabase=$databaseName",
        "Rt03ExpectedDatabaseId=$DatabaseId",
        "Rt03ExpectedDatabaseGuid=$DatabaseGuid",
        'Rt03ExecutionMode=REHEARSAL'
    )
    $arguments += "Rt03ForceFailureStep=$ForceFailureStep"

    $output = & sqlcmd @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($ExpectedFailure)) {
        if ($exitCode -ne 0) {
            throw "Migration unexpectedly failed: $text"
        }
    }
    else {
        if ($exitCode -eq 0 -or
            $text.IndexOf(
                $ExpectedFailure,
                [StringComparison]::Ordinal) -lt 0) {
            throw "Expected failure '$ExpectedFailure' was not observed: $text"
        }
    }

    return $text
}

function Get-SchemaState {
    $query = @'
SET NOCOUNT ON;
DECLARE @Cycle nvarchar(max) =
(
    SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        definition,N'[',N''),N']',N''),N'(',N''),N')',N''),N' ',N''),CHAR(10),N''))
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.App_QlhvDirectRealtimeCycleHistory')
      AND name=N'CK_App_QlhvDirectRealtimeCycleHistory_Mutations'
      AND is_disabled=0 AND is_not_trusted=0
);
SET @Cycle=REPLACE(COALESCE(@Cycle,N''),CHAR(13),N'');
SELECT CONCAT(
  'GLOBAL=',CASE WHEN OBJECT_ID(N'dbo.UQ_App_KhoaHoc_MaKhoa',N'UQ') IS NULL THEN 0 ELSE 1 END,
  '|LOOKUP=',(SELECT COUNT(1) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.App_KhoaHoc') AND name=N'IX_App_KhoaHoc_SourceProfile_MaKhoa'),
  '|LOOKUP_EXACT=',(SELECT COUNT(1) FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.App_KhoaHoc') AND i.name=N'IX_App_KhoaHoc_SourceProfile_MaKhoa' AND i.type=2 AND i.is_unique=0 AND i.is_disabled=0 AND i.has_filter=0
      AND (SELECT COUNT(1) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id)=6
      AND (SELECT COUNT(1) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=2
      AND (SELECT COUNT(1) FROM sys.index_columns ic INNER JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.is_included_column=1 AND c.name IN(N'SourceMaKhoaHoc',N'SourceHash',N'IsDeleted',N'TrangThaiNguon'))=4),
  '|BASELINE_CYCLE=',CASE WHEN @Cycle=N'insertedrows>=0andinsertedrows<=1andupdatedrows>=0andupdatedrows<=1andinsertedrows+updatedrows<=1anddeletedordeactivatedrows=0andduplicateactiverows=0andcheckpointafter>=checkpointbefore' THEN 1 ELSE 0 END,
  '|MIGRATED_CYCLE=',CASE WHEN @Cycle=N'insertedrows>=0andupdatedrows>=0anddeletedordeactivatedrows=0andduplicateactiverows=0andcheckpointafter>=checkpointbefore' THEN 1 ELSE 0 END,
  '|COURSES=',(SELECT COUNT(1) FROM dbo.App_KhoaHoc),
  '|HISTORY=',(SELECT COUNT(1) FROM dbo.App_QlhvDirectRealtimeCycleHistory)
);
'@
    return Invoke-SqlcmdInline -Database $databaseName -Query $query
}

function Assert-State {
    param(
        [string]$Expected,
        [string]$StepName
    )

    $state = Get-SchemaState
    if ($state.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$StepName state mismatch. Expected '$Expected'; actual '$state'."
    }
    Add-Step -Name $StepName -Result 'PASS' -Evidence $state
}

try {
    $serverEvidence = Invoke-SqlcmdInline -Database 'master' -Query @"
SET NOCOUNT ON;
SELECT CONCAT(
  'major=',CONVERT(nvarchar(20),SERVERPROPERTY('ProductMajorVersion')),
  '|version=',CONVERT(nvarchar(40),SERVERPROPERTY('ProductVersion')),
  '|auth=INTEGRATED',
  '|sysadmin=',IS_SRVROLEMEMBER('sysadmin')
);
"@
    Add-Step -Name 'server_and_login_model' -Result 'PASS' `
        -Evidence $serverEvidence

    $defaultSession = Invoke-SqlcmdInline -Database 'master' -Query @"
SET NOCOUNT ON;
SELECT CONCAT(
  'ANSI_NULLS=',CONVERT(int,SESSIONPROPERTY('ANSI_NULLS')),
  '|ANSI_PADDING=',CONVERT(int,SESSIONPROPERTY('ANSI_PADDING')),
  '|ANSI_WARNINGS=',CONVERT(int,SESSIONPROPERTY('ANSI_WARNINGS')),
  '|ARITHABORT=',CONVERT(int,SESSIONPROPERTY('ARITHABORT')),
  '|CONCAT_NULL_YIELDS_NULL=',CONVERT(int,SESSIONPROPERTY('CONCAT_NULL_YIELDS_NULL')),
  '|QUOTED_IDENTIFIER=',CONVERT(int,SESSIONPROPERTY('QUOTED_IDENTIFIER')),
  '|NUMERIC_ROUNDABORT=',CONVERT(int,SESSIONPROPERTY('NUMERIC_ROUNDABORT'))
);
"@
    Add-Step -Name 'raw_sqlcmd_session_options' -Result 'PASS' `
        -Evidence $defaultSession

    Invoke-SqlcmdInline -Database 'master' -Query @"
CREATE DATABASE [$databaseName] COLLATE SQL_Latin1_General_CP1_CI_AS;
ALTER DATABASE [$databaseName] SET COMPATIBILITY_LEVEL = 160;
"@ | Out-Null
    $databaseCreated = $true

    $identity = Invoke-SqlcmdInline -Database $databaseName -Query @"
SET NOCOUNT ON;
SELECT CONCAT(
  DB_ID(),'|',CONVERT(nvarchar(36),database_guid),'|',
  compatibility_level,'|',collation_name,'|',
  HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER'))
FROM sys.database_recovery_status r
INNER JOIN sys.databases d ON d.database_id=r.database_id
WHERE r.database_id=DB_ID();
"@
    $identityParts = $identity.Split('|')
    if ($identityParts.Count -ne 5 -or
        $identityParts[2] -ne '160' -or
        $identityParts[3] -ne 'SQL_Latin1_General_CP1_CI_AS' -or
        $identityParts[4] -ne '1') {
        throw "Disposable database identity/compatibility/permission mismatch: $identity"
    }
    $databaseId = $identityParts[0]
    $databaseGuid = $identityParts[1]
    Add-Step -Name 'disposable_database_identity' -Result 'PASS' `
        -Evidence "id=$databaseId|guid=$databaseGuid|compat=$($identityParts[2])|collation=$($identityParts[3])|alter_permission=$($identityParts[4])"

    $baselineOutput = & sqlcmd -S $Server -d $databaseName -E -b -W `
        -i $baseline -v "Rt03TargetDatabase=$databaseName" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Baseline creation failed: $(($baselineOutput | Out-String).Trim())"
    }
    Assert-State -Expected 'GLOBAL=1|LOOKUP=0|LOOKUP_EXACT=0|BASELINE_CYCLE=1|MIGRATED_CYCLE=0' `
        -StepName 'clean_baseline'

    $sessionFailureScript = Join-Path (Split-Path -Parent $OutputPath) `
        'rt03-session-option-negative-control.sql'
    $correctedSql = Get-Content -Raw -LiteralPath $prerequisite
    $negativeSql = $correctedSql.Replace(
        'SET QUOTED_IDENTIFIER ON;',
        'SET QUOTED_IDENTIFIER OFF;')
    if ($negativeSql -eq $correctedSql) {
        throw 'Could not construct the session-option negative control.'
    }
    Set-Content -LiteralPath $sessionFailureScript -Value $negativeSql `
        -Encoding utf8
    $sessionFailure = Invoke-Migration -InputFile $sessionFailureScript `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid `
        -ExpectedFailure '527625'
    Add-Step -Name 'sessionproperty_fail_fast' -Result 'PASS' `
        -Evidence $sessionFailure
    Assert-State -Expected 'GLOBAL=1|LOOKUP=0|LOOKUP_EXACT=0|BASELINE_CYCLE=1|MIGRATED_CYCLE=0' `
        -StepName 'session_failure_no_partial_ddl'

    Invoke-SqlcmdInline -Database $databaseName -Query @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
CREATE INDEX IX_App_KhoaHoc_SourceProfile_MaKhoa
ON dbo.App_KhoaHoc(MaKhoa);
"@ | Out-Null
    $driftFailure = Invoke-Migration -InputFile $prerequisite `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid `
        -ExpectedFailure '527627'
    Add-Step -Name 'schema_drift_fail_closed' -Result 'PASS' `
        -Evidence $driftFailure
    $wrongState = Get-SchemaState
    if ($wrongState.IndexOf(
        'GLOBAL=1|LOOKUP=1|LOOKUP_EXACT=0|BASELINE_CYCLE=1|MIGRATED_CYCLE=0',
        [StringComparison]::Ordinal) -lt 0) {
        throw "Schema drift failure changed state unexpectedly: $wrongState"
    }
    Add-Step -Name 'schema_drift_no_partial_ddl' -Result 'PASS' `
        -Evidence $wrongState
    Invoke-SqlcmdInline -Database $databaseName -Query @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
DROP INDEX IX_App_KhoaHoc_SourceProfile_MaKhoa ON dbo.App_KhoaHoc;
"@ | Out-Null

    $forcedFailure = Invoke-Migration -InputFile $prerequisite `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid `
        -ForceFailureStep 'AFTER_INDEX' -ExpectedFailure '527629'
    Add-Step -Name 'forced_mid_migration_failure' -Result 'PASS' `
        -Evidence $forcedFailure
    Assert-State -Expected 'GLOBAL=1|LOOKUP=0|LOOKUP_EXACT=0|BASELINE_CYCLE=1|MIGRATED_CYCLE=0' `
        -StepName 'forced_failure_full_rollback'

    $firstApply = Invoke-Migration -InputFile $prerequisite `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid
    if ($firstApply.IndexOf(
        'RT03_KHOAHOC_SCHEMA_PREREQUISITE_APPLIED_AND_VERIFIED',
        [StringComparison]::Ordinal) -lt 0) {
        throw "First prerequisite did not emit success evidence: $firstApply"
    }
    Add-Step -Name 'prerequisite_clean_baseline' -Result 'PASS' `
        -Evidence $firstApply
    Assert-State -Expected 'GLOBAL=0|LOOKUP=1|LOOKUP_EXACT=1|BASELINE_CYCLE=0|MIGRATED_CYCLE=1' `
        -StepName 'migrated_schema_definition'

    $secondApply = Invoke-Migration -InputFile $prerequisite `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid
    if ($secondApply.IndexOf(
        'RT03_KHOAHOC_SCHEMA_PREREQUISITE_ALREADY_APPLIED_EXACT',
        [StringComparison]::Ordinal) -lt 0) {
        throw "Second prerequisite did not prove deterministic rerun: $secondApply"
    }
    Add-Step -Name 'prerequisite_second_run' -Result 'PASS' `
        -Evidence $secondApply

    Invoke-SqlcmdInline -Database $databaseName -Query @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
INSERT dbo.App_KhoaHoc
  (MaKhoa,SourceProfileCode,SourceMaKhoaHoc,SourceHash,IsDeleted,TrangThaiNguon)
VALUES
  (N'REHEARSAL-SAME',N'CSDT_OTO',N'OTO-REHEARSAL',REPLICATE(N'a',64),0,1),
  (N'REHEARSAL-SAME',N'CSDT_MOTO',N'MOTO-REHEARSAL',REPLICATE(N'b',64),0,1);
"@ | Out-Null
    $unsafeCourseRollback = Invoke-Migration -InputFile $rollback `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid `
        -ExpectedFailure '527611'
    Add-Step -Name 'rollback_unsafe_cross_profile_data' -Result 'PASS' `
        -Evidence $unsafeCourseRollback
    Assert-State -Expected 'GLOBAL=0|LOOKUP=1|LOOKUP_EXACT=1|BASELINE_CYCLE=0|MIGRATED_CYCLE=1|COURSES=2' `
        -StepName 'unsafe_course_rollback_no_partial_ddl'
    Invoke-SqlcmdInline -Database $databaseName -Query @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
DELETE dbo.App_KhoaHoc WHERE MaKhoa=N'REHEARSAL-SAME';
"@ | Out-Null

    Invoke-SqlcmdInline -Database $databaseName -Query @"
INSERT dbo.App_QlhvDirectRealtimeCycleHistory
  (CycleId,InsertedRows,UpdatedRows,DeletedOrDeactivatedRows,
   DuplicateActiveRows,CheckpointBefore,CheckpointAfter)
VALUES (NEWID(),2,0,0,0,25,26);
"@ | Out-Null
    $unsafeHistoryRollback = Invoke-Migration -InputFile $rollback `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid `
        -ExpectedFailure '527612'
    Add-Step -Name 'rollback_unsafe_multirow_history' -Result 'PASS' `
        -Evidence $unsafeHistoryRollback
    Assert-State -Expected 'GLOBAL=0|LOOKUP=1|LOOKUP_EXACT=1|BASELINE_CYCLE=0|MIGRATED_CYCLE=1|COURSES=0|HISTORY=1' `
        -StepName 'unsafe_history_rollback_no_partial_ddl'
    Invoke-SqlcmdInline -Database $databaseName -Query @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
DELETE dbo.App_QlhvDirectRealtimeCycleHistory;
"@ | Out-Null

    $safeRollback = Invoke-Migration -InputFile $rollback `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid
    if ($safeRollback.IndexOf(
        'RT03_KHOAHOC_SCHEMA_ROLLBACK_APPLIED_AND_VERIFIED',
        [StringComparison]::Ordinal) -lt 0) {
        throw "Safe rollback did not emit success evidence: $safeRollback"
    }
    Add-Step -Name 'rollback_migrated_empty_state' -Result 'PASS' `
        -Evidence $safeRollback
    Assert-State -Expected 'GLOBAL=1|LOOKUP=0|LOOKUP_EXACT=0|BASELINE_CYCLE=1|MIGRATED_CYCLE=0' `
        -StepName 'rollback_exact_baseline'

    $baselineRollback = Invoke-Migration -InputFile $rollback `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid
    if ($baselineRollback.IndexOf(
        'RT03_KHOAHOC_SCHEMA_ROLLBACK_ALREADY_BASELINE_EXACT',
        [StringComparison]::Ordinal) -lt 0) {
        throw "Baseline rollback rerun was not deterministic: $baselineRollback"
    }
    Add-Step -Name 'rollback_second_run' -Result 'PASS' `
        -Evidence $baselineRollback

    $reapply = Invoke-Migration -InputFile $prerequisite `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid
    Add-Step -Name 'prerequisite_after_rollback' -Result 'PASS' `
        -Evidence $reapply
    Assert-State -Expected 'GLOBAL=0|LOOKUP=1|LOOKUP_EXACT=1|BASELINE_CYCLE=0|MIGRATED_CYCLE=1' `
        -StepName 'reapply_exact_migrated_state'

    $finalRollback = Invoke-Migration -InputFile $rollback `
        -DatabaseId $databaseId -DatabaseGuid $databaseGuid
    Add-Step -Name 'final_rehearsal_rollback' -Result 'PASS' `
        -Evidence $finalRollback
    Assert-State -Expected 'GLOBAL=1|LOOKUP=0|LOOKUP_EXACT=0|BASELINE_CYCLE=1|MIGRATED_CYCLE=0' `
        -StepName 'final_exact_baseline'
}
finally {
    if ($null -ne $sessionFailureScript -and
        (Test-Path -LiteralPath $sessionFailureScript)) {
        Remove-Item -LiteralPath $sessionFailureScript -Force
    }

    if ($databaseCreated) {
        try {
            Invoke-SqlcmdInline -Database 'master' -Query @"
ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [$databaseName];
"@ | Out-Null
            $remaining = Invoke-SqlcmdInline -Database 'master' -Query `
                "SET NOCOUNT ON;SELECT COUNT(1) FROM sys.databases WHERE name=N'$databaseName';"
            if ($remaining.Trim() -ne '0') {
                throw "Disposable database still exists: $databaseName"
            }
            $cleanupPassed = $true
            Add-Step -Name 'cleanup_disposable_database' -Result 'PASS' `
                -Evidence "$databaseName removed"
        }
        catch {
            Add-Step -Name 'cleanup_disposable_database' -Result 'FAIL' `
                -Evidence $_.Exception.Message
        }
    }

    $reportDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    }
    $report = [pscustomobject]@{
        contract = 'RT03_KHOAHOC_SCHEMA_REHEARSAL_V2'
        completedAtUtc = [DateTime]::UtcNow.ToString('O')
        server = $Server
        executionTool = 'sqlcmd -E -b -i'
        disposableDatabase = $databaseName
        productionDatabaseTouched = $false
        productionServiceTouched = $false
        cleanupPassed = $cleanupPassed
        stepCount = $steps.Count
        passedSteps = @($steps | Where-Object result -eq 'PASS').Count
        failedSteps = @($steps | Where-Object result -eq 'FAIL').Count
        steps = $steps
    }
    $report | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $OutputPath -Encoding utf8
}

if (-not $cleanupPassed -or @($steps | Where-Object result -eq 'FAIL').Count -ne 0) {
    throw "RT03 rehearsal or cleanup failed. See $OutputPath"
}

Write-Output "RT03_SCHEMA_REHEARSAL_PASS|REPORT=$OutputPath"
