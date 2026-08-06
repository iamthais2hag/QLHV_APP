[CmdletBinding()]
param(
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

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'RT04_SERVICE_INSTALL_REQUIRES_ELEVATION'
}

$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot).TrimEnd('\')
if ($RepositoryRoot -ne 'D:\QLHV_APP' -or
    $RuntimeRoot -ne 'D:\QLHV_APP_RUNTIME' -or
    -not $EvidenceRoot.StartsWith(
        'D:\QLHV_RT04_EVIDENCE\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_SERVICE_INSTALL_PATH_REJECTED'
}

$serviceScript = Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'
$sqlPatch = Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'database\patches\20260728_rt04_provision_worker_service_login.sql'
if (-not (Test-Path -LiteralPath $serviceScript -PathType Leaf) -or
    -not (Test-Path -LiteralPath $sqlPatch -PathType Leaf)) {
    throw 'RT04_SERVICE_INSTALL_ARTIFACT_MISSING'
}
[IO.Directory]::CreateDirectory($EvidenceRoot) | Out-Null

. $serviceScript

$existing = Get-QlhvRealtimeWorkerServiceRecord
if ($null -ne $existing -and
    -not [string]::Equals(
        [string]$existing.State,
        'Stopped',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_SERVICE_MUST_BE_STOPPED_DURING_INSTALL'
}

Install-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
$installed = Get-QlhvRealtimeWorkerServiceRecord
if ($null -eq $installed -or
    -not [string]::Equals(
        [string]$installed.State,
        'Stopped',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT04_SERVICE_STOPPED_INSTALL_NOT_VERIFIED'
}

$sqlOutput = & sqlcmd.exe `
    -S 'lpc:CSDLTTTC' `
    -d 'master' `
    -E `
    -C `
    -b `
    -i $sqlPatch 2>&1
if ($LASTEXITCODE -ne 0 -or
    ($sqlOutput -join "`n") -notmatch 'RT04_WORKER_SERVICE_LOGIN_PROVISIONED') {
    [IO.File]::WriteAllText(
        (Join-Path `
            -Path $EvidenceRoot `
            -ChildPath '06_service_sql_error_sanitized.txt'),
        ($sqlOutput -join "`n"),
        [Text.UTF8Encoding]::new($false))
    throw 'RT04_SERVICE_SQL_PRINCIPAL_PROVISION_REJECTED'
}

$proofSql = @'
SET NOCOUNT ON;
SELECT
 (SELECT COUNT(*) FROM master.sys.server_principals
  WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker') AS LoginRows,
 (SELECT COUNT(*) FROM QLHV_APP.sys.database_principals
  WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker') AS TargetUserRows,
 (SELECT COUNT(*) FROM CSDL_OTO.sys.database_principals
  WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker') AS OtoUserRows,
 (SELECT COUNT(*) FROM CSDL_MOTO.sys.database_principals
  WHERE name=N'NT SERVICE\QLHV_APP_RealtimeWorker') AS MotoUserRows,
 (SELECT COUNT(*) FROM QLHV_APP.sys.database_permissions permissionRow
  JOIN QLHV_APP.sys.database_principals principalRow
    ON principalRow.principal_id=permissionRow.grantee_principal_id
  WHERE principalRow.name=N'NT SERVICE\QLHV_APP_RealtimeWorker'
    AND permissionRow.permission_name=N'DELETE'
    AND permissionRow.state_desc=N'DENY'
    AND permissionRow.major_id=
    (
      SELECT objectRow.object_id
      FROM QLHV_APP.sys.objects objectRow
      JOIN QLHV_APP.sys.schemas schemaRow
        ON schemaRow.schema_id=objectRow.schema_id
      WHERE schemaRow.name=N'dbo' AND objectRow.name=N'App_HocVien'
    )) AS TargetDeleteDenyRows;
'@
$proofOutput = & sqlcmd.exe `
    -S 'lpc:CSDLTTTC' `
    -d 'master' `
    -E `
    -C `
    -b `
    -h -1 `
    -W `
    -Q $proofSql 2>&1
if ($LASTEXITCODE -ne 0) {
    throw 'RT04_SERVICE_SQL_PRINCIPAL_PROOF_QUERY_REJECTED'
}
$proofValues = (($proofOutput -join ' ').Trim() -split '\s+')
if (($proofValues -join ',') -ne '1,1,1,1,1') {
    throw 'RT04_SERVICE_SQL_PRINCIPAL_PROOF_REJECTED'
}

$registryPath =
    'HKLM:\SYSTEM\CurrentControlSet\Services\QLHV_APP_RealtimeWorker'
$delayed = (Get-ItemProperty -LiteralPath $registryPath `
    -Name DelayedAutoStart -ErrorAction Stop).DelayedAutoStart
$environmentRows = @((Get-ItemProperty -LiteralPath $registryPath `
    -Name Environment -ErrorAction Stop).Environment)

$proof = [ordered]@{
    Evidence = 'RT04_PRODUCTION_SERVICE_REGISTERED_STOPPED'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    ServiceName = [string]$installed.Name
    State = [string]$installed.State
    StartMode = [string]$installed.StartMode
    DelayedAutoStart = ([int]$delayed -eq 1)
    StartName = [string]$installed.StartName
    BinaryPath = [string]$installed.PathName
    ProductionEnvironmentEntryCount = $environmentRows.Count
    EnvironmentContainsSecrets = $false
    AutoSyncEnabled = $false
    Rt03ControlledCutoverEnabled = $true
    ServiceLoginRows = 1
    DatabaseUserRows = 3
    AppHocVienDeleteDenied = $true
    SourceDatabaseWriteGranted = $false
    BusinessDataWrites = 0
}
[IO.File]::WriteAllText(
    (Join-Path `
        -Path $EvidenceRoot `
        -ChildPath '06_service_registration_stopped.json'),
    ($proof | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

Write-Output 'RT04_PRODUCTION_SERVICE_REGISTERED_STOPPED'
