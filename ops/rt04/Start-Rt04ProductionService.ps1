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

foreach ($required in @($RepositoryRoot,$RuntimeRoot,$EvidenceRoot)) {
    if ([string]::IsNullOrWhiteSpace($required)) {
        throw 'RT04_ELEVATED_START_ARGUMENT_EMPTY'
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
    throw 'RT04_ELEVATED_START_ABSOLUTE_PATH_REJECTED'
}

$serviceScript = [IO.Path]::GetFullPath((Join-Path `
    -Path $RepositoryRoot `
    -ChildPath 'scripts\windows\qlhv-lan\RealtimeWorkerService.ps1'))
if ($serviceScript -ne
        'D:\QLHV_APP\scripts\windows\qlhv-lan\RealtimeWorkerService.ps1' -or
    -not (Test-Path -LiteralPath $serviceScript -PathType Leaf)) {
    throw 'RT04_ELEVATED_START_SERVICE_SCRIPT_REJECTED'
}

. $serviceScript
Assert-QlhvRealtimeWorkerAdministrator

$record = Get-QlhvRealtimeWorkerServiceRecord
if ($null -eq $record -or
    [string]$record.State -ne 'Stopped' -or
    [int]$record.ProcessId -ne 0) {
    throw 'RT04_ELEVATED_START_INITIAL_SERVICE_STATE_REJECTED'
}
Assert-QlhvRealtimeWorkerServiceIdentity `
    -ServiceRecord $record `
    -RuntimeRoot $RuntimeRoot

$approvedExecutable = Get-QlhvRealtimeWorkerExecutable -RuntimeRoot $RuntimeRoot
if (-not (Test-Path -LiteralPath $approvedExecutable -PathType Leaf)) {
    throw 'RT04_ELEVATED_START_EXECUTABLE_MISSING'
}
$productionConfig = [IO.Path]::GetFullPath((Join-Path `
    -Path $RuntimeRoot `
    -ChildPath 'config\appsettings.Production.Local.json'))
if (-not (Test-Path -LiteralPath $productionConfig -PathType Leaf)) {
    throw 'RT04_ELEVATED_START_PRODUCTION_CONFIG_MISSING'
}
$configHashBefore = (Get-FileHash `
    -LiteralPath $productionConfig `
    -Algorithm SHA256).Hash
$serviceAccount = 'NT SERVICE\QLHV_APP_RealtimeWorker'
& icacls.exe $productionConfig `
    /grant ($serviceAccount + ':(R)') `
    /c | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'RT04_ELEVATED_START_CONFIG_READ_ACL_REJECTED'
}
$configHashAfter = (Get-FileHash `
    -LiteralPath $productionConfig `
    -Algorithm SHA256).Hash
if (-not [string]::Equals(
    $configHashBefore,
    $configHashAfter,
    [StringComparison]::Ordinal)) {
    throw 'RT04_ELEVATED_START_CONFIG_CONTENT_CHANGED'
}
$configAcl = Get-Acl -LiteralPath $productionConfig
$serviceAces = @($configAcl.Access | Where-Object {
    [string]$_.IdentityReference -eq $serviceAccount -and
    [Security.AccessControl.AccessControlType]$_.AccessControlType -eq
        [Security.AccessControl.AccessControlType]::Allow
})
if ($serviceAces.Count -ne 1) {
    throw 'RT04_ELEVATED_START_CONFIG_ACL_CARDINALITY_REJECTED'
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
    throw 'RT04_ELEVATED_START_CONFIG_ACL_NOT_READ_ONLY'
}
$overlap = @(Get-CimInstance Win32_Process | Where-Object {
    [string]::Equals(
        [string]$_.ExecutablePath,
        $approvedExecutable,
        [StringComparison]::OrdinalIgnoreCase)
})
if ($overlap.Count -ne 0) {
    throw 'RT04_ELEVATED_START_STANDALONE_OVERLAP_REJECTED'
}

Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
$running = Get-QlhvRealtimeWorkerServiceRecord
$result = [ordered]@{
    Evidence = 'RT04_ELEVATED_SERVICE_START_PASS'
    CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
    ServiceName = [string]$running.Name
    State = [string]$running.State
    ProcessId = [int]$running.ProcessId
    StartMode = [string]$running.StartMode
    Account = [string]$running.StartName
    Executable = [string]$running.PathName
    ProductionConfigHashUnchanged = $configHashAfter
    ProductionConfigServiceAccess = 'READ_ONLY'
}
$resultPath = Join-Path `
    -Path $EvidenceRoot `
    -ChildPath '08_service_start_elevated.json'
[IO.File]::WriteAllText(
    $resultPath,
    ($result | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))
Write-Output 'RT04_ELEVATED_SERVICE_START_PASS'
