[CmdletBinding()]
param(
    [string]$RuntimeAccount = [Security.Principal.WindowsIdentity]::GetCurrent().Name
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$LauncherDirectory = Join-Path $RuntimeRoot 'launcher'
$ConfigDirectory = Join-Path $RuntimeRoot 'config'
$ProductionConfig = Join-Path $ConfigDirectory 'appsettings.Production.Local.json'
$ModelDirectory = Join-Path $RuntimeRoot 'models'
# IM_GPLX is an external runtime data directory; Build-PublishPackage explicitly excludes it.
$PhotoSourceDirectory = 'D:\IM_GPLX'
$PhotoOutputDirectory = 'D:\QLHV_APP\IM_GPLX'
$LogDirectory = Join-Path $RuntimeRoot 'logs'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$LegacyRuntimeMarker = Join-Path $RunDirectory 'legacy-runtime.marker'
$FirewallDisplayName = 'QLHV App LAN - TCP 8088 (Private)'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$ClientDirectory = Join-Path $RepoRoot 'client'
$ClientDist = Join-Path $ClientDirectory 'dist'
$ApiProject = Join-Path $RepoRoot 'server\QLHV.Api\QLHV.Api.csproj'
$WorkerProject = Join-Path $RepoRoot 'server\QLHV.Worker\QLHV.Worker.csproj'
$DevelopmentSettings = Join-Path $RepoRoot 'server\QLHV.Api\appsettings.Development.json'
# The Development file is guarded by Test-Path and parsed only for allow-listed extraction;
# it is never copied into the staging or runtime directory.
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'
$SourceStartScript = Join-Path $PSScriptRoot 'Start-QLHV-App.ps1'
$SourceLauncher = Join-Path $PSScriptRoot 'Start-QLHV-App.cmd'
$RealtimeWorkerServiceScript = Join-Path $PSScriptRoot 'RealtimeWorkerService.ps1'
. $RealtimeWorkerServiceScript
$StartScript = Join-Path $LauncherDirectory 'Start-QLHV-App.ps1'
$Launcher = Join-Path $LauncherDirectory 'Start-QLHV-App.cmd'
$ShortcutName = 'QLHV Th' + [char]0x00E0 + 'nh C' + [char]0x00F4 + 'ng.lnk'
$ShortcutPath = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) $ShortcutName
$StageRoot = Join-Path $RuntimeRoot ("install-stage-" + [Guid]::NewGuid().ToString('N'))
$StageApp = Join-Path $StageRoot 'app'
$StageWorker = Join-Path $StageApp 'worker'
$StageLauncher = Join-Path $StageRoot 'launcher'
$InstallBackup = Join-Path $RunDirectory ("install-backup-" + [Guid]::NewGuid().ToString('N'))
$LauncherBackup = Join-Path $RunDirectory ("launcher-backup-" + [Guid]::NewGuid().ToString('N'))
$ShortcutBackup = Join-Path $RunDirectory ("shortcut-backup-" + [Guid]::NewGuid().ToString('N') + '.lnk')
$script:InstallStage = 'initialization'
$script:ExistingRuntimeWasStopped = $false
$script:RollbackPathEntered = $false
$script:ProductionConfigBackupPath = $null

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Install-QLHV-App.ps1 must be run from PowerShell as Administrator.'
    }
}

function Assert-SafeRuntimeRoot {
    $actual = [System.IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
    if (-not [string]::Equals($actual, 'D:\QLHV_APP_RUNTIME', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify unexpected runtime root: $actual"
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Protect-DeploymentLogMessage {
    param([Parameter(Mandatory = $false)][string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return 'No safe exception message was available.'
    }

    $safe = [Regex]::Replace($Message, '[\r\n]+', ' ').Trim()
    # Fail closed: a value can contain spaces or delimiters that token-by-token
    # redaction cannot safely bound. If any sensitive marker is present, omit the
    # complete message so no unparsed suffix can survive.
    if ($safe -match '(?i)(passwordhash|\bpassword\b|\bpwd\b|\b[A-Za-z0-9_]*(?:token|secret)[A-Za-z0-9_]*\b|\bset-cookie\b|\bauthorization\s*:|\bcookie\s*:|\bconnectionstrings(?::|__)|\b(?:data\s*source|server|initial\s*catalog|user\s*id|uid)\s*=)') {
        return 'Sensitive deployment failure details were omitted.'
    }
    $safe = [Regex]::Replace(
        $safe,
        '(?i)(\b(?:authorization|cookie|set-cookie)\s*:\s*)[^\r\n]+',
        '$1[REDACTED]')
    $safe = [Regex]::Replace(
        $safe,
        '(?i)(\bConnectionStrings(?::|__)[A-Za-z0-9_]+\s*=\s*)("[^"]*"|''[^'']*''|[^;\s,}]+)',
        '$1[REDACTED]')
    $safe = [Regex]::Replace(
        $safe,
        '(?i)(\b(?:password|pwd|user\s*id|uid|data\s*source|server|initial\s*catalog|[A-Za-z0-9_]*(?:secret|token)[A-Za-z0-9_]*)\s*[:=]\s*)("[^"]*"|''[^'']*''|[^;\s,}]+)',
        '$1[REDACTED]')
    if ($safe -match '(?i)(passwordhash|set-cookie|authorization\s*:|operations?.{0,24}secret)') {
        return 'Sensitive deployment failure details were omitted.'
    }
    if ($safe.Length -gt 1200) {
        $safe = $safe.Substring(0, 1200) + '...'
    }
    return $safe
}

function Write-SafeDeploymentFailure {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $false)][string]$Message
    )

    try {
        New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
        $logPath = Join-Path $LogDirectory ('installer-' + (Get-Date -Format 'yyyyMMdd') + '.error.log')
        if ((Test-Path -LiteralPath $logPath -PathType Leaf) -and
            (Get-Item -LiteralPath $logPath).Length -ge 1MB) {
            $archive = Join-Path $LogDirectory ('installer-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.error.log')
            Move-Item -LiteralPath $logPath -Destination $archive
        }

        $safeMessage = Protect-DeploymentLogMessage -Message $Message
        Add-Content -LiteralPath $logPath -Encoding UTF8 -Value (
            "$(Get-Date -Format o) stage=$Stage message=$safeMessage")

        $cutoff = [DateTime]::UtcNow.AddDays(-30)
        $files = @(Get-ChildItem -LiteralPath $LogDirectory -File -Filter 'installer-*.error.log' |
            Sort-Object LastWriteTimeUtc -Descending)
        for ($index = 0; $index -lt $files.Count; $index++) {
            if ($index -ge 14 -or $files[$index].LastWriteTimeUtc -lt $cutoff) {
                Remove-Item -LiteralPath $files[$index].FullName -Force -ErrorAction SilentlyContinue
            }
        }
    }
    catch {
        # Failure logging must not replace the original deployment exception.
    }
}

function ConvertFrom-SafeJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Configuration JSON is missing or invalid: $Path. No configuration value was logged."
    }
}

function Set-QlhvProductionWriteFlags {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$SkipDurableBackup
    )

    try {
        $configuration = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Configuration JSON is missing or invalid: $Path. No configuration value was logged."
    }
    if ($null -eq $configuration) {
        throw "Configuration JSON is empty: $Path"
    }

    $syncProperty = $configuration.PSObject.Properties['Sync']
    if ($null -eq $syncProperty) {
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name 'Sync' -Value ([pscustomobject]@{})
        $sync = $configuration.PSObject.Properties['Sync'].Value
    }
    elseif ($syncProperty.Value -isnot [pscustomobject]) {
        throw "Production configuration section Sync must be a JSON object."
    }
    else {
        $sync = $syncProperty.Value
    }

    $syncExecutionProperty = $configuration.PSObject.Properties['SyncExecution']
    if ($null -eq $syncExecutionProperty) {
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name 'SyncExecution' -Value ([pscustomobject]@{})
        $syncExecution = $configuration.PSObject.Properties['SyncExecution'].Value
    }
    elseif ($syncExecutionProperty.Value -isnot [pscustomobject]) {
        throw "Production configuration section SyncExecution must be a JSON object."
    }
    else {
        $syncExecution = $syncExecutionProperty.Value
    }

    $autoSyncProperty = $configuration.PSObject.Properties['QlhvAutoSync']
    if ($null -eq $autoSyncProperty) {
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name 'QlhvAutoSync' -Value ([pscustomobject]@{})
        $autoSync = $configuration.PSObject.Properties['QlhvAutoSync'].Value
        $autoSyncChanged = $true
    }
    elseif ($autoSyncProperty.Value -isnot [pscustomobject]) {
        throw "Production configuration section QlhvAutoSync must be a JSON object."
    }
    else {
        $autoSync = $autoSyncProperty.Value
        $autoSyncChanged = $false
    }

    $autoSyncDefaults = [ordered]@{
        Enabled = $false
        RunOnServerStartup = $false
        PollingEnabled = $false
        IsFallbackOnly = $true
        FallbackModeEnabled = $false
        RefreshBackupBeforeSync = $true
        ActiveRunHeartbeatTimeoutSeconds = 120
        HeartbeatIntervalSeconds = 15
        SourceOrder = @('OTO', 'MOTO')
        StartupDedupeWindowSeconds = 300
        SessionStartDedupeWindowSeconds = 30
    }
    foreach ($entry in $autoSyncDefaults.GetEnumerator()) {
        if ($null -eq $autoSync.PSObject.Properties[$entry.Key]) {
            Add-Member -InputObject $autoSync -MemberType NoteProperty -Name $entry.Key -Value $entry.Value
            $autoSyncChanged = $true
        }
    }
    $safeAutoSyncFlags = [ordered]@{
        Enabled = $false
        RunOnServerStartup = $false
        PollingEnabled = $false
        IsFallbackOnly = $true
        FallbackModeEnabled = $false
        RefreshBackupBeforeSync = $true
    }
    foreach ($entry in $safeAutoSyncFlags.GetEnumerator()) {
        $property = $autoSync.PSObject.Properties[$entry.Key]
        if ($null -eq $property -or $property.Value -isnot [bool] -or
            [bool]$property.Value -ne [bool]$entry.Value) {
            Add-Member -InputObject $autoSync -MemberType NoteProperty -Name $entry.Key -Value $entry.Value -Force
            $autoSyncChanged = $true
        }
    }
    $sourceOrderProperty = $autoSync.PSObject.Properties['SourceOrder']
    $sourceOrder = if ($null -eq $sourceOrderProperty -or
        $sourceOrderProperty.Value -is [string]) {
        @()
    }
    else {
        @($sourceOrderProperty.Value)
    }
    if ($sourceOrder.Count -ne 2 -or
        -not [string]::Equals([string]$sourceOrder[0], 'OTO', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals([string]$sourceOrder[1], 'MOTO', [StringComparison]::OrdinalIgnoreCase)) {
        Add-Member -InputObject $autoSync -MemberType NoteProperty -Name 'SourceOrder' -Value @('OTO', 'MOTO') -Force
        $autoSyncChanged = $true
    }

    $realtimeProperty = $configuration.PSObject.Properties['CsdtRealtimeSync']
    if ($null -eq $realtimeProperty) {
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name 'CsdtRealtimeSync' -Value ([pscustomobject]@{})
        $realtime = $configuration.PSObject.Properties['CsdtRealtimeSync'].Value
        $realtimeChanged = $true
    }
    elseif ($realtimeProperty.Value -isnot [pscustomobject]) {
        throw "Production configuration section CsdtRealtimeSync must be a JSON object."
    }
    else {
        $realtime = $realtimeProperty.Value
        $realtimeChanged = $false
    }

    $realtimeDefaults = [ordered]@{
        Enabled = $false
        PollIntervalSeconds = 1
        ReconcileIntervalMinutes = 5
        ChangeRetentionDays = 7
        UseBackupProfiles = $false
    }
    foreach ($entry in $realtimeDefaults.GetEnumerator()) {
        if ($null -eq $realtime.PSObject.Properties[$entry.Key]) {
            Add-Member -InputObject $realtime -MemberType NoteProperty -Name $entry.Key -Value $entry.Value
            $realtimeChanged = $true
        }
    }

    $streamsProperty = $realtime.PSObject.Properties['Streams']
    if ($null -eq $streamsProperty) {
        Add-Member -InputObject $realtime -MemberType NoteProperty -Name 'Streams' -Value ([pscustomobject]@{})
        $streams = $realtime.PSObject.Properties['Streams'].Value
        $realtimeChanged = $true
    }
    elseif ($streamsProperty.Value -isnot [pscustomobject]) {
        throw "Production configuration section CsdtRealtimeSync:Streams must be a JSON object."
    }
    else {
        $streams = $streamsProperty.Value
    }
    $useBackupProfilesProperty = $realtime.PSObject.Properties['UseBackupProfiles']
    $useBackupProfiles = $null -ne $useBackupProfilesProperty -and
        $useBackupProfilesProperty.Value -is [bool] -and
        [bool]$useBackupProfilesProperty.Value
    $otoSourceProfile = if ($useBackupProfiles) { 'OTO_V2_BAK' } else { 'OTO_V2' }
    $otoTargetProfile = if ($useBackupProfiles) { 'OTO_V1_BAK' } else { 'OTO_V1' }
    $motoSourceProfile = if ($useBackupProfiles) { 'MOTO_V2_BAK' } else { 'MOTO_V2' }
    $motoTargetProfile = if ($useBackupProfiles) { 'MOTO_V1_BAK' } else { 'MOTO_V1' }

    $fixedStreams = [ordered]@{
        Oto = [ordered]@{
            Enabled = $false
            StreamCode = 'OTO_V2_TO_V1'
            SourceProfile = $otoSourceProfile
            TargetProfile = $otoTargetProfile
            MaCSDT = '66029'
        }
        Moto = [ordered]@{
            Enabled = $false
            StreamCode = 'MOTO_V2_TO_V1'
            SourceProfile = $motoSourceProfile
            TargetProfile = $motoTargetProfile
            MaCSDT = '66030'
        }
    }
    foreach ($streamEntry in $fixedStreams.GetEnumerator()) {
        $streamProperty = $streams.PSObject.Properties[$streamEntry.Key]
        if ($null -eq $streamProperty) {
            Add-Member -InputObject $streams -MemberType NoteProperty -Name $streamEntry.Key -Value ([pscustomobject]@{})
            $stream = $streams.PSObject.Properties[$streamEntry.Key].Value
            $realtimeChanged = $true
        }
        elseif ($streamProperty.Value -isnot [pscustomobject]) {
            throw "Production configuration stream $($streamEntry.Key) must be a JSON object."
        }
        else {
            $stream = $streamProperty.Value
        }
        foreach ($streamDefault in $streamEntry.Value.GetEnumerator()) {
            $streamValue = $stream.PSObject.Properties[$streamDefault.Key]
            if ($null -eq $streamValue) {
                Add-Member -InputObject $stream -MemberType NoteProperty -Name $streamDefault.Key -Value $streamDefault.Value
                $realtimeChanged = $true
            }
        }
    }

    $photoProperty = $configuration.PSObject.Properties['PhotoProcessing']
    if ($null -eq $photoProperty) {
        Add-Member -InputObject $configuration -MemberType NoteProperty -Name 'PhotoProcessing' -Value ([pscustomobject]@{})
        $photo = $configuration.PSObject.Properties['PhotoProcessing'].Value
        $photoChanged = $true
    }
    elseif ($photoProperty.Value -isnot [pscustomobject]) {
        throw "Production configuration section PhotoProcessing must be a JSON object."
    }
    else {
        $photo = $photoProperty.Value
        $photoChanged = $false
    }

    $photoDefaults = [ordered]@{
        Enabled = $false
        SourceRoot = 'D:\IM_GPLX'
        OutputRoot = 'D:\QLHV_APP\IM_GPLX'
        ModelPath = 'D:\QLHV_APP_RUNTIME\models\person-segmentation.onnx'
        ModelSha256 = ''
        ModelLicense = ''
        ModelLicenseManifestPath = 'D:\QLHV_APP_RUNTIME\models\person-segmentation.license.json'
        ModelLicenseManifestSha256 = ''
        BackgroundColor = '#0067B1'
        AutoProcessAfterSync = $false
        MinimumAutoApprovalConfidence = 0.85
    }
    foreach ($entry in $photoDefaults.GetEnumerator()) {
        if ($null -eq $photo.PSObject.Properties[$entry.Key]) {
            Add-Member -InputObject $photo -MemberType NoteProperty -Name $entry.Key -Value $entry.Value
            $photoChanged = $true
        }
    }
    $enabledProperty = $photo.PSObject.Properties['Enabled']
    if ($null -ne $enabledProperty -and
        $enabledProperty.Value -is [bool] -and
        [bool]$enabledProperty.Value) {
        $acceptedLicenses = @('MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause')
        $modelPathProperty = $photo.PSObject.Properties['ModelPath']
        $modelHashProperty = $photo.PSObject.Properties['ModelSha256']
        $licenseProperty = $photo.PSObject.Properties['ModelLicense']
        $manifestPathProperty = $photo.PSObject.Properties['ModelLicenseManifestPath']
        $manifestHashProperty = $photo.PSObject.Properties['ModelLicenseManifestSha256']
        $photoReady = $null -ne $modelPathProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$modelPathProperty.Value) -and
            [IO.Path]::IsPathRooted(([string]$modelPathProperty.Value).Trim()) -and
            [string]::Equals(
                [IO.Path]::GetExtension(([string]$modelPathProperty.Value).Trim()),
                '.onnx',
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath ([string]$modelPathProperty.Value) -PathType Leaf) -and
            $null -ne $modelHashProperty -and
            ([string]$modelHashProperty.Value).Trim() -match '^[A-Fa-f0-9]{64}$' -and
            $null -ne $licenseProperty -and
            $acceptedLicenses -contains ([string]$licenseProperty.Value).Trim() -and
            $null -ne $manifestPathProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$manifestPathProperty.Value) -and
            [IO.Path]::IsPathRooted(([string]$manifestPathProperty.Value).Trim()) -and
            [string]::Equals(
                [IO.Path]::GetExtension(([string]$manifestPathProperty.Value).Trim()),
                '.json',
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath ([string]$manifestPathProperty.Value) -PathType Leaf) -and
            $null -ne $manifestHashProperty -and
            ([string]$manifestHashProperty.Value).Trim() -match '^[A-Fa-f0-9]{64}$'
        if ($photoReady) {
            $actualModelHash = (Get-FileHash -LiteralPath ([string]$modelPathProperty.Value) -Algorithm SHA256).Hash
            $actualManifestHash = (Get-FileHash -LiteralPath ([string]$manifestPathProperty.Value) -Algorithm SHA256).Hash
            try {
                $manifestInfo = Get-Item -LiteralPath ([string]$manifestPathProperty.Value)
                $manifest = Get-Content -LiteralPath ([string]$manifestPathProperty.Value) -Raw -Encoding UTF8 |
                    ConvertFrom-Json
                $manifestSchemaProperty = $manifest.PSObject.Properties['schemaVersion']
                $manifestLicenseProperty = $manifest.PSObject.Properties['licenseId']
                $manifestModelHashProperty = $manifest.PSObject.Properties['modelSha256']
                $manifestSourceProperty = $manifest.PSObject.Properties['modelSource']
                $manifestReviewerProperty = $manifest.PSObject.Properties['reviewedBy']
                $manifestReviewedAtProperty = $manifest.PSObject.Properties['reviewedAtUtc']
                $reviewedAtUtc = [DateTimeOffset]::MinValue
                $reviewedAtValid = $null -ne $manifestReviewedAtProperty -and
                    [DateTimeOffset]::TryParse(
                        [string]$manifestReviewedAtProperty.Value,
                        [ref]$reviewedAtUtc) -and
                    $reviewedAtUtc -le [DateTimeOffset]::UtcNow.AddMinutes(5)
                $photoReady = [string]::Equals(
                        $actualModelHash,
                        ([string]$modelHashProperty.Value).Trim(),
                        [StringComparison]::OrdinalIgnoreCase) -and
                    [string]::Equals(
                        $actualManifestHash,
                        ([string]$manifestHashProperty.Value).Trim(),
                        [StringComparison]::OrdinalIgnoreCase) -and
                    $manifestInfo.Length -gt 0 -and
                    $manifestInfo.Length -le 1MB -and
                    $null -ne $manifestSchemaProperty -and
                    [int]$manifestSchemaProperty.Value -eq 1 -and
                    $null -ne $manifestLicenseProperty -and
                    [string]::Equals(
                        ([string]$manifestLicenseProperty.Value).Trim(),
                        ([string]$licenseProperty.Value).Trim(),
                        [StringComparison]::OrdinalIgnoreCase) -and
                    $null -ne $manifestModelHashProperty -and
                    [string]::Equals(
                        ([string]$manifestModelHashProperty.Value).Trim(),
                        ([string]$modelHashProperty.Value).Trim(),
                        [StringComparison]::OrdinalIgnoreCase) -and
                    $null -ne $manifestSourceProperty -and
                    -not [string]::IsNullOrWhiteSpace([string]$manifestSourceProperty.Value) -and
                    $null -ne $manifestReviewerProperty -and
                    -not [string]::IsNullOrWhiteSpace([string]$manifestReviewerProperty.Value) -and
                    $reviewedAtValid
            }
            catch {
                $photoReady = $false
            }
        }
        if (-not $photoReady) {
            Add-Member -InputObject $photo -MemberType NoteProperty -Name 'Enabled' -Value $false -Force
            Add-Member -InputObject $photo -MemberType NoteProperty -Name 'AutoProcessAfterSync' -Value $false -Force
            $photoChanged = $true
        }
    }
    $finalEnabledProperty = $photo.PSObject.Properties['Enabled']
    $enabledNeedsNormalization = $null -eq $finalEnabledProperty -or
        $finalEnabledProperty.Value -isnot [bool]
    if ($enabledNeedsNormalization -or -not [bool]$finalEnabledProperty.Value) {
        if ($enabledNeedsNormalization) {
            Add-Member -InputObject $photo -MemberType NoteProperty -Name 'Enabled' -Value $false -Force
            $photoChanged = $true
        }
        $autoProcessProperty = $photo.PSObject.Properties['AutoProcessAfterSync']
        if ($null -eq $autoProcessProperty -or
            $autoProcessProperty.Value -isnot [bool] -or
            [bool]$autoProcessProperty.Value) {
            Add-Member -InputObject $photo -MemberType NoteProperty -Name 'AutoProcessAfterSync' -Value $false -Force
            $photoChanged = $true
        }
    }

    $dryRunProperty = $sync.PSObject.Properties['DryRun']
    $enableWritesProperty = $syncExecution.PSObject.Properties['EnableTargetWrites']
    $dryRunChanged = $null -eq $dryRunProperty -or
        $dryRunProperty.Value -isnot [bool] -or
        [bool]$dryRunProperty.Value
    $enableWritesChanged = $null -eq $enableWritesProperty -or
        $enableWritesProperty.Value -isnot [bool] -or
        -not [bool]$enableWritesProperty.Value
    $changed = $dryRunChanged -or $enableWritesChanged -or $autoSyncChanged -or $realtimeChanged -or $photoChanged
    if (-not $changed) {
        return $false
    }
    Add-Member -InputObject $sync -MemberType NoteProperty -Name 'DryRun' -Value $false -Force
    Add-Member -InputObject $syncExecution -MemberType NoteProperty -Name 'EnableTargetWrites' -Value $true -Force
    if ([bool]$configuration.Sync.DryRun -or -not [bool]$configuration.SyncExecution.EnableTargetWrites) {
        throw "Production operational write flags could not be normalized."
    }

    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    $temporaryPath = Join-Path $directory ('.appsettings.flags.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $backupDirectory = if ($SkipDurableBackup) {
        $directory
    }
    else {
        Join-Path $directory 'backups'
    }
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backupName = if ($SkipDurableBackup) {
        '.appsettings.flags.' + [Guid]::NewGuid().ToString('N') + '.bak'
    }
    else {
        'appsettings.Production.Local.' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') +
            '-' + [Guid]::NewGuid().ToString('N') + '.json.bak'
    }
    $backupPath = Join-Path $backupDirectory $backupName
    $replacementCompleted = $false
    try {
        $json = $configuration | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
        $written = Get-Content -LiteralPath $temporaryPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([bool]$written.Sync.DryRun -or -not [bool]$written.SyncExecution.EnableTargetWrites) {
            throw "Production operational write flags could not be serialized."
        }
        # File.Replace atomically swaps the content while retaining the destination ACL.
        [IO.File]::Replace($temporaryPath, $Path, $backupPath, $true)
        $replacementCompleted = $true
        $replaced = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([bool]$replaced.Sync.DryRun -or -not [bool]$replaced.SyncExecution.EnableTargetWrites) {
            throw "Production operational write flags could not be published."
        }
    }
    catch {
        if ($replacementCompleted -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            $restoreTemporaryPath = Join-Path $directory (
                '.appsettings.restore.' + [Guid]::NewGuid().ToString('N') + '.tmp')
            try {
                Copy-Item -LiteralPath $backupPath -Destination $restoreTemporaryPath
                [IO.File]::Replace($restoreTemporaryPath, $Path, $null, $true)
            }
            finally {
                Remove-Item -LiteralPath $restoreTemporaryPath -Force -ErrorAction SilentlyContinue
            }
        }
        throw
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        if ($SkipDurableBackup) {
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        }
    }
    $verified = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([bool]$verified.Sync.DryRun -or -not [bool]$verified.SyncExecution.EnableTargetWrites) {
        throw "Production operational write flags did not remain published."
    }
    if (-not $SkipDurableBackup) {
        $script:ProductionConfigBackupPath = $backupPath
    }
    return $true
}

function Restore-QlhvProductionConfigurationBackup {
    if ([string]::IsNullOrWhiteSpace([string]$script:ProductionConfigBackupPath) -or
        -not (Test-Path -LiteralPath $script:ProductionConfigBackupPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $ProductionConfig -PathType Leaf)) {
        return
    }

    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ProductionConfig))
    $temporaryPath = Join-Path $directory (
        '.appsettings.deployment-restore.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $failedBackup = Join-Path (Split-Path -Parent $script:ProductionConfigBackupPath) (
        'appsettings.Production.Local.failed-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') +
            '-' + [Guid]::NewGuid().ToString('N') + '.json.bak')
    try {
        Copy-Item -LiteralPath $script:ProductionConfigBackupPath -Destination $temporaryPath
        [IO.File]::Replace($temporaryPath, $ProductionConfig, $failedBackup, $true)
        $script:ProductionConfigBackupPath = $null
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-QlhvPhotoModelStatus {
    param([Parameter(Mandatory = $true)][string]$Path)

    $configuration = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $photoProperty = $configuration.PSObject.Properties['PhotoProcessing']
    if ($null -eq $photoProperty -or $photoProperty.Value -isnot [pscustomobject]) {
        return 'NotConfigured'
    }
    $photo = $photoProperty.Value
    $enabled = $photo.PSObject.Properties['Enabled']
    if ($null -eq $enabled -or $enabled.Value -isnot [bool] -or -not [bool]$enabled.Value) {
        return 'Disabled'
    }
    $modelLicense = $photo.PSObject.Properties['ModelLicense']
    if ($null -eq $modelLicense -or [string]::IsNullOrWhiteSpace([string]$modelLicense.Value)) {
        return 'NotReady:LicenseMissing'
    }
    $acceptedLicenses = @('MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause')
    if ($acceptedLicenses -notcontains ([string]$modelLicense.Value).Trim()) {
        return 'NotReady:LicenseNotAccepted'
    }
    $modelPath = $photo.PSObject.Properties['ModelPath']
    if ($null -eq $modelPath -or [string]::IsNullOrWhiteSpace([string]$modelPath.Value) -or
        -not [IO.Path]::IsPathRooted(([string]$modelPath.Value).Trim()) -or
        -not [string]::Equals(
            [IO.Path]::GetExtension(([string]$modelPath.Value).Trim()),
            '.onnx',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath ([string]$modelPath.Value) -PathType Leaf)) {
        return 'NotReady:ModelMissing'
    }
    $configuredHash = $photo.PSObject.Properties['ModelSha256']
    if ($null -eq $configuredHash -or [string]::IsNullOrWhiteSpace([string]$configuredHash.Value)) {
        return 'NotReady:ChecksumMissing'
    }
    $actualHash = (Get-FileHash -LiteralPath ([string]$modelPath.Value) -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actualHash,
            ([string]$configuredHash.Value).Trim(),
            [StringComparison]::OrdinalIgnoreCase)) {
        return 'NotReady:ChecksumMismatch'
    }
    $manifestPath = $photo.PSObject.Properties['ModelLicenseManifestPath']
    if ($null -eq $manifestPath -or [string]::IsNullOrWhiteSpace([string]$manifestPath.Value) -or
        -not [IO.Path]::IsPathRooted(([string]$manifestPath.Value).Trim()) -or
        -not [string]::Equals(
            [IO.Path]::GetExtension(([string]$manifestPath.Value).Trim()),
            '.json',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath ([string]$manifestPath.Value) -PathType Leaf)) {
        return 'NotReady:LicenseManifestMissing'
    }
    $manifestHash = $photo.PSObject.Properties['ModelLicenseManifestSha256']
    if ($null -eq $manifestHash -or
        ([string]$manifestHash.Value).Trim() -notmatch '^[A-Fa-f0-9]{64}$') {
        return 'NotReady:LicenseManifestChecksumMissing'
    }
    $actualManifestHash = (Get-FileHash -LiteralPath ([string]$manifestPath.Value) -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actualManifestHash,
            ([string]$manifestHash.Value).Trim(),
            [StringComparison]::OrdinalIgnoreCase)) {
        return 'NotReady:LicenseManifestChecksumMismatch'
    }
    try {
        $manifestInfo = Get-Item -LiteralPath ([string]$manifestPath.Value)
        $manifest = Get-Content -LiteralPath ([string]$manifestPath.Value) -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $schemaVersion = $manifest.PSObject.Properties['schemaVersion']
        $manifestLicense = $manifest.PSObject.Properties['licenseId']
        $manifestModelHash = $manifest.PSObject.Properties['modelSha256']
        $modelSource = $manifest.PSObject.Properties['modelSource']
        $reviewedBy = $manifest.PSObject.Properties['reviewedBy']
        $reviewedAt = $manifest.PSObject.Properties['reviewedAtUtc']
        $reviewedAtUtc = [DateTimeOffset]::MinValue
        if ($manifestInfo.Length -le 0 -or $manifestInfo.Length -gt 1MB -or
            $null -eq $schemaVersion -or [int]$schemaVersion.Value -ne 1 -or
            $null -eq $manifestLicense -or
            -not [string]::Equals(
                ([string]$manifestLicense.Value).Trim(),
                ([string]$modelLicense.Value).Trim(),
                [StringComparison]::OrdinalIgnoreCase) -or
            $null -eq $manifestModelHash -or
            -not [string]::Equals(
                ([string]$manifestModelHash.Value).Trim(),
                ([string]$configuredHash.Value).Trim(),
                [StringComparison]::OrdinalIgnoreCase) -or
            $null -eq $modelSource -or
            [string]::IsNullOrWhiteSpace([string]$modelSource.Value) -or
            $null -eq $reviewedBy -or
            [string]::IsNullOrWhiteSpace([string]$reviewedBy.Value) -or
            $null -eq $reviewedAt -or
            -not [DateTimeOffset]::TryParse([string]$reviewedAt.Value, [ref]$reviewedAtUtc) -or
            $reviewedAtUtc -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
            return 'NotReady:LicenseManifestInvalid'
        }
    }
    catch {
        return 'NotReady:LicenseManifestInvalid'
    }
    return 'Ready'
}

function Assert-QlhvProductionConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$RequireOperationalWriteFlags
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing QLHV production configuration: $Path"
    }
    $configuration = ConvertFrom-SafeJsonFile -Path $Path
    if ($null -eq $configuration) {
        throw "Configuration JSON is empty: $Path"
    }
    $connectionStrings = $configuration.PSObject.Properties['ConnectionStrings']
    if ($null -eq $connectionStrings) {
        throw "Production configuration is missing the ConnectionStrings section: $Path"
    }
    $qlhvApp = $connectionStrings.Value.PSObject.Properties['QLHV_APP']
    if ($null -eq $qlhvApp -or [string]::IsNullOrWhiteSpace([string]$qlhvApp.Value)) {
        throw "Production configuration is missing ConnectionStrings:QLHV_APP: $Path"
    }
    if ($RequireOperationalWriteFlags) {
        $sync = $configuration.PSObject.Properties['Sync']
        $syncExecution = $configuration.PSObject.Properties['SyncExecution']
        $dryRun = if ($null -eq $sync) { $null } else { $sync.Value.PSObject.Properties['DryRun'] }
        $enableWrites = if ($null -eq $syncExecution) {
            $null
        }
        else {
            $syncExecution.Value.PSObject.Properties['EnableTargetWrites']
        }
        if ($null -eq $dryRun -or $dryRun.Value -isnot [bool] -or [bool]$dryRun.Value) {
            throw "Production configuration must set Sync:DryRun to false: $Path"
        }
        if ($null -eq $enableWrites -or
            $enableWrites.Value -isnot [bool] -or
            -not [bool]$enableWrites.Value) {
            throw "Production configuration must set SyncExecution:EnableTargetWrites to true: $Path"
        }
    }
}

function Set-RestrictedConfigurationDirectoryAcl {
    param(
        [Parameter(Mandatory = $true)][string]$DirectoryPath,
        [Parameter(Mandatory = $true)][string]$AccountName
    )

    try {
        $runtimeIdentity = ([Security.Principal.NTAccount]$AccountName).Translate(
            [Security.Principal.SecurityIdentifier])
    }
    catch {
        throw "Runtime account '$AccountName' could not be resolved. Configuration ACL was not changed."
    }

    $systemIdentity = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $administratorsIdentity = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow

    $directoryAcl = [Security.AccessControl.DirectorySecurity]::new()
    $directoryAcl.SetAccessRuleProtection($true, $false)
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $systemIdentity, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, $allow))
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $administratorsIdentity, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, $allow))
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $runtimeIdentity, [Security.AccessControl.FileSystemRights]::ReadAndExecute, $inheritance, $propagation, $allow))
    Set-Acl -LiteralPath $DirectoryPath -AclObject $directoryAcl
}

function Set-RestrictedConfigurationAcl {
    param(
        [Parameter(Mandatory = $true)][string]$DirectoryPath,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$AccountName
    )

    Set-RestrictedConfigurationDirectoryAcl -DirectoryPath $DirectoryPath -AccountName $AccountName

    $runtimeIdentity = ([Security.Principal.NTAccount]$AccountName).Translate(
        [Security.Principal.SecurityIdentifier])
    $systemIdentity = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $administratorsIdentity = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $allow = [Security.AccessControl.AccessControlType]::Allow

    $fileAcl = [Security.AccessControl.FileSecurity]::new()
    $fileAcl.SetAccessRuleProtection($true, $false)
    $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $systemIdentity, [Security.AccessControl.FileSystemRights]::FullControl, $allow))
    $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $administratorsIdentity, [Security.AccessControl.FileSystemRights]::FullControl, $allow))
    $fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $runtimeIdentity, [Security.AccessControl.FileSystemRights]::Read, $allow))
    Set-Acl -LiteralPath $FilePath -AclObject $fileAcl
}

function Grant-RuntimeDirectoryAccess {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$AccountName,
        [Parameter(Mandatory = $true)][ValidateSet('ReadAndExecute', 'Modify')][string]$Access
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Runtime directory is missing: $Path"
    }
    $permission = if ($Access -eq 'Modify') { 'M' } else { 'RX' }
    $grant = "${AccountName}:(OI)(CI)$permission"
    & icacls.exe $Path '/grant:r' $grant '/T' '/C' '/Q' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant $Access access on $Path to the runtime account."
    }
}

function Set-RuntimeDirectoryAccess {
    Grant-RuntimeDirectoryAccess -Path $RuntimeRoot -AccountName $RuntimeAccount -Access ReadAndExecute
    Grant-RuntimeDirectoryAccess -Path $AppDirectory -AccountName $RuntimeAccount -Access ReadAndExecute
    Grant-RuntimeDirectoryAccess -Path $LauncherDirectory -AccountName $RuntimeAccount -Access ReadAndExecute
    Grant-RuntimeDirectoryAccess -Path $LogDirectory -AccountName $RuntimeAccount -Access Modify
    Grant-RuntimeDirectoryAccess -Path $RunDirectory -AccountName $RuntimeAccount -Access Modify
    Grant-RuntimeDirectoryAccess -Path $PhotoSourceDirectory -AccountName $RuntimeAccount -Access ReadAndExecute
    Grant-RuntimeDirectoryAccess -Path $PhotoOutputDirectory -AccountName $RuntimeAccount -Access Modify
    # Config uses a protected ACL and is intentionally not included in the writable paths.
}

function Initialize-ProductionConfiguration {
    New-Item -ItemType Directory -Path $ConfigDirectory -Force | Out-Null
    # Restrict the directory before any temporary file can contain extracted secrets.
    # The temp file inherits this protected ACL at creation time.
    Set-RestrictedConfigurationDirectoryAcl `
        -DirectoryPath $ConfigDirectory `
        -AccountName $RuntimeAccount

    if (Test-Path -LiteralPath $ProductionConfig -PathType Leaf) {
        Assert-QlhvProductionConfiguration -Path $ProductionConfig
        [void](Set-QlhvProductionWriteFlags -Path $ProductionConfig)
        Assert-QlhvProductionConfiguration -Path $ProductionConfig -RequireOperationalWriteFlags
        Set-RestrictedConfigurationAcl `
            -DirectoryPath $ConfigDirectory `
            -FilePath $ProductionConfig `
            -AccountName $RuntimeAccount
        Write-Host "Existing local production configuration was preserved and operational write flags were normalized: $ProductionConfig"
        return
    }

    if (-not (Test-Path -LiteralPath $DevelopmentSettings -PathType Leaf)) {
        throw "Cannot initialize local production configuration because the local source file is missing: $DevelopmentSettings"
    }

    $source = ConvertFrom-SafeJsonFile -Path $DevelopmentSettings
    $allowedSections = @(
        'ConnectionStrings',
        'ConnectionProfileEncryption',
        'ConnectionProfileProtection',
        'DataProtection',
        'FileStorage',
        'Sync',
        'SyncExecution',
        'QlhvAutoSync',
        'PhotoProcessing',
        'Authentication'
    )
    $target = [ordered]@{}
    foreach ($sectionName in $allowedSections) {
        $property = $source.PSObject.Properties[$sectionName]
        if ($null -ne $property) {
            $target[$sectionName] = $property.Value
        }
    }

    # Development FileStorage.Root is relative to the API project. Resolve it now;
    # the same relative text would otherwise point at a different folder in runtime\app.
    if ($target.Contains('FileStorage')) {
        $fileStorageRoot = $target['FileStorage'].PSObject.Properties['Root']
        if ($null -ne $fileStorageRoot -and
            -not [string]::IsNullOrWhiteSpace([string]$fileStorageRoot.Value) -and
            -not [IO.Path]::IsPathRooted([string]$fileStorageRoot.Value)) {
            $apiContentRoot = Split-Path -Parent $ApiProject
            $fileStorageRoot.Value = [IO.Path]::GetFullPath(
                (Join-Path $apiContentRoot ([string]$fileStorageRoot.Value)))
        }
    }

    if (-not $target.Contains('ConnectionStrings')) {
        throw "Local source configuration does not contain the required ConnectionStrings section. No value was logged."
    }
    $sourceQlhvApp = $target['ConnectionStrings'].PSObject.Properties['QLHV_APP']
    if ($null -eq $sourceQlhvApp -or [string]::IsNullOrWhiteSpace([string]$sourceQlhvApp.Value)) {
        throw "Local source configuration does not contain ConnectionStrings:QLHV_APP. No value was logged."
    }

    # Write only the allow-listed sections, validate the result, and atomically publish it.
    # Never echo the JSON document because it can contain connection credentials.
    $temporaryConfig = Join-Path $ConfigDirectory ('.appsettings.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $json = $target | ConvertTo-Json -Depth 100
        [IO.File]::WriteAllText($temporaryConfig, $json, [Text.UTF8Encoding]::new($false))
        [void](Set-QlhvProductionWriteFlags -Path $temporaryConfig -SkipDurableBackup)
        Assert-QlhvProductionConfiguration -Path $temporaryConfig -RequireOperationalWriteFlags
        Move-Item -LiteralPath $temporaryConfig -Destination $ProductionConfig
        Set-RestrictedConfigurationAcl `
            -DirectoryPath $ConfigDirectory `
            -FilePath $ProductionConfig `
            -AccountName $RuntimeAccount
    }
    finally {
        Remove-Item -LiteralPath $temporaryConfig -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Created protected local production configuration: $ProductionConfig"
}

function Build-PublishPackage {
    New-Item -ItemType Directory -Path $StageApp, $StageWorker, $StageLauncher -Force | Out-Null

    foreach ($sourcePath in @($SourceStartScript, $SourceLauncher)) {
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Launcher source file was not found: $sourcePath"
        }
        Copy-Item -LiteralPath $sourcePath -Destination $StageLauncher
    }
    if (-not (Test-Path -LiteralPath (Join-Path $StageLauncher 'Start-QLHV-App.ps1') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $StageLauncher 'Start-QLHV-App.cmd') -PathType Leaf)) {
        throw 'The staged runtime launcher is incomplete.'
    }

    $previousApiBase = Get-Item -LiteralPath 'Env:VITE_API_BASE_URL' -ErrorAction SilentlyContinue
    try {
        # A production bundle is always same-origin, even when a developer has a local .env.local file.
        $env:VITE_API_BASE_URL = '/api'
        Push-Location $ClientDirectory
        try {
            Invoke-CheckedCommand -Command 'npm.cmd' -Arguments @('run', 'build') -FailureMessage 'Frontend production build failed'
        }
        finally {
            Pop-Location
        }
    }
    finally {
        if ($null -ne $previousApiBase) {
            $env:VITE_API_BASE_URL = [string]$previousApiBase.Value
        }
        else {
            Remove-Item -LiteralPath 'Env:VITE_API_BASE_URL' -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $ClientDist 'index.html') -PathType Leaf)) {
        throw "Frontend build did not create $ClientDist\index.html."
    }

    # The API project may invoke the client build again during publish. Keep the same-origin
    # override active for that build as well, then restore the caller's environment.
    $previousApiBaseDuringPublish = Get-Item -LiteralPath 'Env:VITE_API_BASE_URL' -ErrorAction SilentlyContinue
    try {
        $env:VITE_API_BASE_URL = '/api'
        Invoke-CheckedCommand -Command 'dotnet' -Arguments @(
            'publish', $ApiProject,
            '--configuration', 'Release',
            '--output', $StageApp,
            '/p:SkipClientBuild=true'
        ) -FailureMessage 'QLHV.Api publish failed'
    }
    finally {
        if ($null -ne $previousApiBaseDuringPublish) {
            $env:VITE_API_BASE_URL = [string]$previousApiBaseDuringPublish.Value
        }
        else {
            Remove-Item -LiteralPath 'Env:VITE_API_BASE_URL' -ErrorAction SilentlyContinue
        }
    }

    Invoke-CheckedCommand -Command 'dotnet' -Arguments @(
        'publish', $WorkerProject,
        '--configuration', 'Release',
        '--output', $StageWorker
    ) -FailureMessage 'QLHV.Worker publish failed'

    $wwwroot = Join-Path $StageApp 'wwwroot'
    if (Test-Path -LiteralPath $wwwroot) {
        Remove-Item -LiteralPath $wwwroot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
    Get-ChildItem -LiteralPath $ClientDist -Force | Copy-Item -Destination $wwwroot -Recurse -Force

    # Development settings and local student images must never enter the runtime package.
    Get-ChildItem -LiteralPath $StageApp -Recurse -File -Filter 'appsettings.Development*.json' |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $StageApp -Recurse -Directory |
        Where-Object { $_.Name -eq 'IM_GPLX' -or $_.Name -eq '.git' } |
        Sort-Object FullName -Descending |
        Remove-Item -Recurse -Force

    $forbiddenSettings = @(Get-ChildItem -LiteralPath $StageApp -Recurse -File -Filter 'appsettings.Development*.json')
    if ($forbiddenSettings.Count -gt 0) {
        throw 'Development appsettings were found in the publish package.'
    }
    if (Test-Path -LiteralPath (Join-Path $StageApp 'IM_GPLX')) {
        throw 'IM_GPLX must not be included in the publish package.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $StageWorker 'QLHV.Worker.exe') -PathType Leaf)) {
        throw 'The publish package does not contain worker\QLHV.Worker.exe.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $wwwroot 'index.html') -PathType Leaf)) {
        throw 'The publish package does not contain wwwroot\index.html.'
    }

    $webFiles = @(Get-ChildItem -LiteralPath $wwwroot -Recurse -File | Where-Object {
        $_.Extension -in @('.html', '.js', '.css', '.json', '.map')
    })
    if ($webFiles.Count -gt 0) {
        $devUrls = @($webFiles | Select-String -SimpleMatch -Pattern 'localhost:5130', '127.0.0.1:5130')
        if ($devUrls.Count -gt 0) {
            throw 'The production frontend contains a development API URL (localhost/127.0.0.1:5130).'
        }
    }
}

function Install-FirewallRule {
    # Keep/update the exact rule in place. A failed rerun must not remove a previously
    # working firewall rule before its replacement exists.
    $ruleByName = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue)
    if ($ruleByName.Count -gt 0) {
        Set-NetFirewallRule `
            -Name 'QLHV-App-LAN-TCP-8088-Private' `
            -NewDisplayName $FirewallDisplayName `
            -Description 'Allow QLHV ASP.NET Core host on TCP 8088 for Private networks only.' `
            -Enabled True `
            -Direction Inbound `
            -Action Allow `
            -Profile Private `
            -Protocol TCP `
            -LocalPort 8088 | Out-Null
    }
    else {
        New-NetFirewallRule `
            -Name 'QLHV-App-LAN-TCP-8088-Private' `
            -DisplayName $FirewallDisplayName `
            -Description 'Allow QLHV ASP.NET Core host on TCP 8088 for Private networks only.' `
            -Enabled True `
            -Direction Inbound `
            -Action Allow `
            -Profile Private `
            -Protocol TCP `
            -LocalPort 8088 | Out-Null
    }

    # Remove only older duplicate QLHV display-name rules after the canonical rule exists.
    $displayNameRules = @(Get-NetFirewallRule -DisplayName $FirewallDisplayName -ErrorAction SilentlyContinue)
    foreach ($duplicateRule in $displayNameRules) {
        if ([string]$duplicateRule.Name -ne 'QLHV-App-LAN-TCP-8088-Private') {
            Remove-NetFirewallRule -Name ([string]$duplicateRule.Name) -ErrorAction Stop
        }
    }
}

function Install-DesktopShortcut {
    if (-not (Test-Path -LiteralPath $Launcher -PathType Leaf)) {
        throw "Launcher was not found: $Launcher"
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $Launcher
    $shortcut.WorkingDirectory = $LauncherDirectory
    $shortcut.Description = 'Start or join QLHV, initialize a fresh data session, and open the browser.'
    $shortcut.WindowStyle = 7
    $iconPath = Join-Path $AppDirectory 'QLHV.Api.exe'
    if (Test-Path -LiteralPath $iconPath -PathType Leaf) {
        $shortcut.IconLocation = "$iconPath,0"
    }
    $shortcut.Save()
}

function Invoke-StartRuntime {
    param([switch]$AllowLegacyRollback)

    $effectiveStartScript = if (Test-Path -LiteralPath $StartScript -PathType Leaf) {
        $StartScript
    }
    else {
        $SourceStartScript
    }
    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', $effectiveStartScript, '-NoBrowser', '-SuppressErrorDialog', '-DisableAutoSync'
    )
    if ($AllowLegacyRollback) {
        $arguments += '-AllowLegacyRollback'
    }
    & powershell.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "QLHV runtime did not pass liveness/readiness checks (launcher exit code $LASTEXITCODE)."
    }
}

function Invoke-ReadOnlySmokeTest {
    $checks = @(
        [pscustomobject]@{ Url = 'http://localhost:8088/health/live'; Expected = 200; Timeout = 5 },
        [pscustomobject]@{ Url = 'http://localhost:8088/health/ready'; Expected = 200; Timeout = 60 },
        [pscustomobject]@{ Url = 'http://localhost:8088/api/system/runtime-status'; Expected = 200; Timeout = 60 },
        [pscustomobject]@{ Url = 'http://localhost:8088/api/auth/me'; Expected = 401; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/'; Expected = 200; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/login'; Expected = 200; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/qlhv-import'; Expected = 200; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/trang-thai-he-thong'; Expected = 200; Timeout = 10 }
    )
    foreach ($check in $checks) {
        $statusCode = 0
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $check.Url -Method Get -TimeoutSec $check.Timeout
            $statusCode = [int]$response.StatusCode
        }
        catch {
            $responseProperty = $_.Exception.PSObject.Properties['Response']
            if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
                $statusProperty = $responseProperty.Value.PSObject.Properties['StatusCode']
                if ($null -ne $statusProperty) {
                    $statusCode = [int]$statusProperty.Value
                }
            }
        }
        if ($statusCode -ne $check.Expected) {
            throw "Read-only installation smoke test failed for $($check.Url) (expected $($check.Expected), received $statusCode). Review logs in $LogDirectory."
        }
    }
}

Assert-Administrator
Assert-SafeRuntimeRoot

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'server\QLHV.sln') -PathType Leaf) -or
    -not (Test-Path -LiteralPath $ApiProject -PathType Leaf) -or
    -not (Test-Path -LiteralPath $WorkerProject -PathType Leaf)) {
    throw "QLHV source repository was not found at $RepoRoot."
}

New-Item -ItemType Directory -Path $RuntimeRoot, $ConfigDirectory, $ModelDirectory, $LogDirectory, $RunDirectory, $PhotoSourceDirectory, $PhotoOutputDirectory -Force | Out-Null

$installationSucceeded = $false
$newRuntimeInstalled = $false
$oldRuntimeMoved = $false
$newLauncherInstalled = $false
$oldLauncherMoved = $false
$workerServiceInstalledThisRun = $false
$workerServiceBefore = Get-QlhvRealtimeWorkerServiceSnapshot -RuntimeRoot $RuntimeRoot
$firewallExistedBefore = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue).Count -gt 0
$shortcutExistedBefore = Test-Path -LiteralPath $ShortcutPath -PathType Leaf
if ($shortcutExistedBefore) {
    Copy-Item -LiteralPath $ShortcutPath -Destination $ShortcutBackup
}
try {
    $script:InstallStage = 'production-config'
    Initialize-ProductionConfiguration
    Write-Host ("Photo processing model status: " + (Get-QlhvPhotoModelStatus -Path $ProductionConfig))
    $configHashBefore = (Get-FileHash -LiteralPath $ProductionConfig -Algorithm SHA256).Hash
    $script:InstallStage = 'build-publish'
    Build-PublishPackage

    $script:InstallStage = 'stop-existing-runtime'
    & $StopScript -Quiet
    $script:ExistingRuntimeWasStopped = $true

    if (Test-Path -LiteralPath $AppDirectory) {
        $script:InstallStage = 'backup-current-runtime'
        Move-Item -LiteralPath $AppDirectory -Destination $InstallBackup
        $oldRuntimeMoved = $true
    }
    if (Test-Path -LiteralPath $LauncherDirectory) {
        $script:InstallStage = 'backup-current-launcher'
        Move-Item -LiteralPath $LauncherDirectory -Destination $LauncherBackup
        $oldLauncherMoved = $true
    }

    $script:InstallStage = 'activate-runtime'
    Move-Item -LiteralPath $StageApp -Destination $AppDirectory
    $newRuntimeInstalled = $true
    $script:InstallStage = 'activate-launcher'
    Move-Item -LiteralPath $StageLauncher -Destination $LauncherDirectory
    $newLauncherInstalled = $true
    $script:InstallStage = 'runtime-permissions'
    Set-RuntimeDirectoryAccess
    Remove-Item -LiteralPath $LegacyRuntimeMarker -Force -ErrorAction SilentlyContinue
    $script:InstallStage = 'realtime-worker-service'
    Install-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
    $workerServiceInstalledThisRun = -not [bool]$workerServiceBefore.Exists

    # Readiness is a read-only schema/auth/environment gate. No SQL patch, refresh,
    # backup/restore, or synchronization endpoint is called by the installer.
    $script:InstallStage = 'launcher-readiness'
    Invoke-StartRuntime
    $script:InstallStage = 'read-only-smoke'
    Invoke-ReadOnlySmokeTest

    $configHashAfterSmoke = (Get-FileHash -LiteralPath $ProductionConfig -Algorithm SHA256).Hash
    if ($configHashAfterSmoke -cne $configHashBefore) {
        throw 'Local production configuration changed during install; refusing to complete.'
    }

    # Installer runs elevated only to deploy/ACL/firewall. Do not leave the LAN API
    # running with that elevated token; the normal operator starts it via the shortcut.
    $script:InstallStage = 'stop-elevated-smoke-runtime'
    & $StopScript -Quiet
    $script:InstallStage = 'start-realtime-worker-service'
    Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot

    $script:InstallStage = 'desktop-shortcut'
    Install-DesktopShortcut
    $script:InstallStage = 'private-firewall'
    Install-FirewallRule

    $script:InstallStage = 'complete'
    $installationSucceeded = $true
    if ($oldRuntimeMoved -and (Test-Path -LiteralPath $InstallBackup)) {
        Remove-Item -LiteralPath $InstallBackup -Recurse -Force -ErrorAction SilentlyContinue
        $oldRuntimeMoved = $false
    }
    if ($oldLauncherMoved -and (Test-Path -LiteralPath $LauncherBackup)) {
        Remove-Item -LiteralPath $LauncherBackup -Recurse -Force -ErrorAction SilentlyContinue
        $oldLauncherMoved = $false
    }
    Remove-Item -LiteralPath $ShortcutBackup -Force -ErrorAction SilentlyContinue

    Write-Host 'QLHV LAN runtime installed successfully.'
    Write-Host "Runtime: $AppDirectory"
    Write-Host "Logs:    $LogDirectory"
    Write-Host "Config:  $ProductionConfig (protected; values were not logged)"
    Write-Host 'Worker:  Windows service QLHV_APP_RealtimeWorker (Automatic)'
    Write-Host 'Shortcut: QLHV Thanh Cong (Public Desktop)'
}
catch {
    $installError = $_
    $safeInstallError = Protect-DeploymentLogMessage -Message ([string]$installError.Exception.Message)
    # Persist the original failure before rollback can replace its context.
    Write-SafeDeploymentFailure `
        -Stage $script:InstallStage `
        -Message ([string]$installError.Exception.Message)
    try {
        Stop-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
    }
    catch {
        $safeWorkerStopError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
        Write-Warning "Could not stop the realtime worker during rollback: $safeWorkerStopError"
    }
    if (-not $firewallExistedBefore) {
        $newFirewallRules = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue)
        foreach ($newFirewallRule in $newFirewallRules) {
            Remove-NetFirewallRule -Name ([string]$newFirewallRule.Name) -ErrorAction SilentlyContinue
        }
    }
    if ($shortcutExistedBefore -and (Test-Path -LiteralPath $ShortcutBackup -PathType Leaf)) {
        Copy-Item -LiteralPath $ShortcutBackup -Destination $ShortcutPath -Force
        Remove-Item -LiteralPath $ShortcutBackup -Force -ErrorAction SilentlyContinue
    }
    elseif (Test-Path -LiteralPath $ShortcutPath -PathType Leaf) {
        Remove-Item -LiteralPath $ShortcutPath -Force -ErrorAction SilentlyContinue
    }
    if ($newLauncherInstalled -and (Test-Path -LiteralPath $LauncherDirectory -PathType Container)) {
        $failedLauncher = Join-Path $RunDirectory ("failed-launcher-" + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $LauncherDirectory -Destination $failedLauncher -ErrorAction SilentlyContinue
        $newLauncherInstalled = $false
    }
    if ($oldLauncherMoved -and (Test-Path -LiteralPath $LauncherBackup -PathType Container)) {
        Move-Item -LiteralPath $LauncherBackup -Destination $LauncherDirectory
        $oldLauncherMoved = $false
    }
    Restore-QlhvProductionConfigurationBackup
    if ($newRuntimeInstalled) {
        try { & $StopScript -Quiet } catch { }
        if (Test-Path -LiteralPath $AppDirectory -PathType Container) {
            $failedRuntime = Join-Path $RunDirectory ("failed-install-" + [Guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $AppDirectory -Destination $failedRuntime -ErrorAction SilentlyContinue
        }
        $newRuntimeInstalled = $false
    }

    if ($oldRuntimeMoved -and (Test-Path -LiteralPath $InstallBackup -PathType Container)) {
        Move-Item -LiteralPath $InstallBackup -Destination $AppDirectory
        $oldRuntimeMoved = $false
        $script:RollbackPathEntered = $true
        if ([bool]$workerServiceBefore.Exists) {
            Install-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
        }
        Set-Content -LiteralPath $LegacyRuntimeMarker -Value 'legacy-health-compatible' -Encoding Ascii
        try {
            Invoke-StartRuntime -AllowLegacyRollback
            & $StopScript -Quiet
        }
        catch {
            $safeVerificationError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
            throw "Installation failed and the previous runtime was restored, but health verification also failed: $safeVerificationError. Review $LogDirectory. Original error: $safeInstallError"
        }
    }

    if ($workerServiceInstalledThisRun -and -not [bool]$workerServiceBefore.Exists) {
        Remove-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
        $workerServiceInstalledThisRun = $false
    }
    elseif ([bool]$workerServiceBefore.Exists -and
        [bool]$workerServiceBefore.WasRunning -and
        (Test-Path -LiteralPath (Get-QlhvRealtimeWorkerExecutable -RuntimeRoot $RuntimeRoot) -PathType Leaf)) {
        Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
    }

    if ($script:ExistingRuntimeWasStopped -and -not $newRuntimeInstalled -and
        -not $oldRuntimeMoved -and -not $script:RollbackPathEntered -and
        (Test-Path -LiteralPath $AppDirectory -PathType Container)) {
        Set-Content -LiteralPath $LegacyRuntimeMarker -Value 'legacy-health-compatible' -Encoding Ascii
        try {
            Invoke-StartRuntime -AllowLegacyRollback
            & $StopScript -Quiet
        }
        catch {
            $safeRecoveryError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
            throw "Installation transition failed and the previous runtime could not be health-verified: $safeRecoveryError. Original error: $safeInstallError"
        }
        throw "Installation transition failed. The previous runtime remains installed, was health-checked, and is stopped. Start it from the shortcut. Original error: $safeInstallError"
    }
    throw "Installation failed; the previous runtime was restored when available. $safeInstallError"
}
finally {
    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $installationSucceeded -and $oldRuntimeMoved -and -not (Test-Path -LiteralPath $AppDirectory) -and
        (Test-Path -LiteralPath $InstallBackup)) {
        Move-Item -LiteralPath $InstallBackup -Destination $AppDirectory -ErrorAction SilentlyContinue
    }
}
