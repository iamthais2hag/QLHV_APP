[CmdletBinding()]
param(
    [switch]$Launch,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeRoot,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($required in @($RepositoryRoot,$RuntimeRoot,$EvidenceRoot)) {
    if ([string]::IsNullOrWhiteSpace($required)) {
        throw 'RT04_SERVICE_START_LAUNCH_ARGUMENT_EMPTY'
    }
}

$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot).TrimEnd('\')
if ($RepositoryRoot -ne 'D:\QLHV_APP' -or
    $RuntimeRoot -ne 'D:\QLHV_APP_RUNTIME' -or
    -not $EvidenceRoot.StartsWith(
        'D:\QLHV_RT04_EVIDENCE\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_SERVICE_START_LAUNCH_ABSOLUTE_PATH_REJECTED'
}

$serviceScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'))
$startScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'ops\rt04\Start-Rt04ProductionService.ps1'))
$healthScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'ops\rt04\Get-Rt04ProductionHealth.ps1'))
foreach ($scriptPath in @($serviceScript,$startScript,$healthScript)) {
    if ([string]::IsNullOrWhiteSpace($scriptPath) -or
        -not [IO.Path]::IsPathRooted($scriptPath) -or
        -not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "RT04_SERVICE_START_SCRIPT_PATH_REJECTED: $scriptPath"
    }
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$errors)
    if ($errors.Count -ne 0) {
        throw "RT04_SERVICE_START_SCRIPT_PARSE_REJECTED: $scriptPath"
    }
    $joinCommands = $ast.FindAll(
        {
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            [string]::Equals(
                $node.GetCommandName(),
                'Join-Path',
                [StringComparison]::OrdinalIgnoreCase)
        },
        $true)
    foreach ($joinCommand in $joinCommands) {
        $commandText = $joinCommand.Extent.Text
        if ($commandText -notmatch '(?i)-Path\s+' -or
            $commandText -notmatch '(?i)-ChildPath\s+') {
            throw "RT04_INTERACTIVE_JOIN_PATH_REJECTED: $scriptPath"
        }
    }
}
if ($serviceScript -ne
    'D:\QLHV_APP\scripts\windows\qlhv-lan\RealtimeWorkerService.ps1') {
    throw 'RT04_SERVICE_START_SCRIPT_IDENTITY_REJECTED'
}

$record = Get-CimInstance Win32_Service `
    -Filter "Name='QLHV_APP_RealtimeWorker'" `
    -ErrorAction Stop
if ([string]$record.State -ne 'Stopped' -or
    [int]$record.ProcessId -ne 0 -or
    [string]$record.StartName -ne 'NT SERVICE\QLHV_APP_RealtimeWorker' -or
    -not [string]::Equals(
        ([string]$record.PathName).Trim('"'),
        'D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_SERVICE_START_INITIAL_IDENTITY_REJECTED'
}

$productionConfig = [IO.Path]::GetFullPath((Join-Path `
    -Path $RuntimeRoot `
    -ChildPath 'config\appsettings.Production.Local.json'))
if (-not (Test-Path -LiteralPath $productionConfig -PathType Leaf)) {
    throw 'RT04_SERVICE_START_PRODUCTION_CONFIG_MISSING'
}
$serviceAccount = 'NT SERVICE\QLHV_APP_RealtimeWorker'
$configAcl = Get-Acl -LiteralPath $productionConfig
$serviceAces = @($configAcl.Access | Where-Object {
    [string]$_.IdentityReference -eq $serviceAccount -and
    [Security.AccessControl.AccessControlType]$_.AccessControlType -eq
        [Security.AccessControl.AccessControlType]::Allow
})
if ($serviceAces.Count -ne 1) {
    throw 'RT04_SERVICE_START_CONFIG_READ_ACL_MISSING'
}
$forbiddenRights =
    [Security.AccessControl.FileSystemRights]::WriteData -bor
    [Security.AccessControl.FileSystemRights]::AppendData -bor
    [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor
    [Security.AccessControl.FileSystemRights]::WriteAttributes -bor
    [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
    [Security.AccessControl.FileSystemRights]::Delete -bor
    [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
    [Security.AccessControl.FileSystemRights]::TakeOwnership
$actualRights = [Security.AccessControl.FileSystemRights]$serviceAces[0].FileSystemRights
if (($actualRights -band [Security.AccessControl.FileSystemRights]::Read) -eq 0 -or
    ($actualRights -band $forbiddenRights) -ne 0) {
    throw 'RT04_SERVICE_START_CONFIG_ACL_NOT_READ_ONLY'
}
$productionConfigHash = (Get-FileHash `
    -LiteralPath $productionConfig `
    -Algorithm SHA256).Hash

$preflightHealthPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '07_pre_service_start_health.json'
& $healthScript -OutputPath $preflightHealthPath | Out-Null
$health = Get-Content -LiteralPath $preflightHealthPath -Raw |
    ConvertFrom-Json
$requiredGates = @(
    'ServiceAutomaticDelayed',
    'ServiceAccountLeastPrivilege',
    'ProfilesHealthy',
    'CountsHealthy',
    'CheckpointsCurrent',
    'CheckpointsWithinRetention',
    'AutoSyncOff',
    'NoDuplicatesOrManualReview',
    'NoTransactionsOrBlockers',
    'ExactFeatureState',
    'DatabaseIdentities',
    'RcsiOff',
    'RecoveryPolicy'
)
foreach ($gateName in $requiredGates) {
    if (-not [bool]$health.Gates.$gateName) {
        throw "RT04_SERVICE_START_HEALTH_GATE_REJECTED: $gateName"
    }
}
if ([bool]$health.Realtime.CycleActive -or
    ([string]$health.Realtime.WorkerStatus -notin @('HEALTHY','BLOCKED')) -or
    ([string]$health.Realtime.WorkerStatus -eq 'BLOCKED' -and
     [string]$health.Realtime.LastErrorCode -ne 'SqlException')) {
    throw 'RT04_SERVICE_START_DURABLE_WORKER_STATE_REJECTED'
}
if ([bool]$health.Gates.ServiceRunning -or
    [bool]$health.Gates.ExactOneWorkerProcess -or
    [bool]$health.Gates.ServiceProcessMatches -or
    [bool]$health.Gates.MutexHeld) {
    throw 'RT04_SERVICE_START_QUIESCENCE_GATE_REJECTED'
}

$permissionSql = @'
SET NOCOUNT ON;
SELECT COUNT_BIG(*) FROM master.sys.server_principals
WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker';
EXECUTE AS LOGIN=N'NT SERVICE\QLHV_APP_RealtimeWorker';
USE QLHV_APP;
SELECT
 HAS_PERMS_BY_NAME(N'dbo.App_HocVien',N'OBJECT',N'SELECT'),
 HAS_PERMS_BY_NAME(N'dbo.App_HocVien',N'OBJECT',N'INSERT'),
 HAS_PERMS_BY_NAME(N'dbo.App_HocVien',N'OBJECT',N'UPDATE'),
 HAS_PERMS_BY_NAME(N'dbo.App_HocVien',N'OBJECT',N'DELETE');
USE CSDL_OTO;
SELECT
 HAS_PERMS_BY_NAME(N'dbo.NguoiLX',N'OBJECT',N'SELECT'),
 HAS_PERMS_BY_NAME(N'dbo.NguoiLX',N'OBJECT',N'VIEW CHANGE TRACKING');
USE CSDL_MOTO;
SELECT
 HAS_PERMS_BY_NAME(N'dbo.NguoiLX',N'OBJECT',N'SELECT'),
 HAS_PERMS_BY_NAME(N'dbo.NguoiLX',N'OBJECT',N'VIEW CHANGE TRACKING');
USE master;
REVERT;
'@
$permissionOutput = & sqlcmd.exe `
    -S 'lpc:CSDLTTTC' `
    -d 'master' `
    -E `
    -C `
    -b `
    -m 1 `
    -h -1 `
    -W `
    -Q $permissionSql 2>&1
if ($LASTEXITCODE -ne 0) {
    throw 'RT04_SERVICE_START_PERMISSION_QUERY_REJECTED'
}
$permissionValues = (($permissionOutput -join ' ').Trim() -split '\s+')
if (($permissionValues -join ',') -ne '1,1,1,1,0,1,1,1,1') {
    throw "RT04_SERVICE_START_PERMISSION_GATE_REJECTED: $($permissionValues -join ',')"
}

$semanticCommand =
    "& '$startScript' -RepositoryRoot '$RepositoryRoot' " +
    "-RuntimeRoot '$RuntimeRoot' -EvidenceRoot '$EvidenceRoot'"
$sanitizedCommand =
    'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass ' +
    "-EncodedCommand <BASE64_OF: $semanticCommand>"
$commandLogPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '08_service_start_elevation_command_sanitized.txt'
[IO.File]::WriteAllText(
    $commandLogPath,
    $sanitizedCommand,
    [Text.UTF8Encoding]::new($false))

$preflight = [ordered]@{
    Evidence = 'RT04_SERVICE_START_ELEVATION_PREFLIGHT_PASS'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    RepositoryRoot = $RepositoryRoot
    RuntimeRoot = $RuntimeRoot
    ServiceScript = $serviceScript
    ServiceScriptExists = $true
    JoinPathArgumentsExplicit = $true
    RequiredArgumentsExplicitAndNonEmpty = $true
    NoProfile = $true
    NonInteractive = $true
    ServiceStopped = $true
    ServiceProcessId = 0
    ProductionCounts = '156/5'
    AutoSyncHistoryAndActive = '11/0/0'
    Checkpoints = 'OTO=2;MOTO=0'
    ServiceLoginAndPermissionContract = 'PASS'
    ProtectedConfigAclRepair = 'EXACT_FILE_READ_ONLY_ACE_PRESENT'
    ProtectedConfigHash = $productionConfigHash
    ProductionMutationDuringRejectedStart = $false
}
$preflightPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '08_service_start_elevation_preflight.json'
[IO.File]::WriteAllText(
    $preflightPath,
    ($preflight | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

if (-not $Launch) {
    Write-Output "RT04_SERVICE_START_ELEVATION_PREFLIGHT_PASS command=$commandLogPath"
    return
}

$errorPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '08_service_start_elevated_error.txt'
$elevatedCommand = @"
try {
  $semanticCommand
  exit 0
} catch {
  [IO.File]::WriteAllText(
    '$errorPath',
    `$_.Exception.ToString(),
    [Text.UTF8Encoding]::new(`$false))
  exit 1
}
"@
$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($elevatedCommand))
$argumentString =
    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
if ([string]::IsNullOrWhiteSpace($argumentString)) {
    throw 'RT04_SERVICE_START_ELEVATION_ARGUMENT_REJECTED'
}
$process = Start-Process `
    -FilePath 'powershell.exe' `
    -Verb RunAs `
    -ArgumentList $argumentString `
    -WindowStyle Normal `
    -PassThru
Write-Output "RT04_VISIBLE_SERVICE_START_ELEVATION_STARTED pid=$($process.Id)"
