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
$PhotoSourceDirectory = 'D:\IM_GPLX'
$PhotoOutputDirectory = 'D:\QLHV_APP\IM_GPLX'
$LogDirectory = Join-Path $RuntimeRoot 'logs'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$LegacyRuntimeMarker = Join-Path $RunDirectory 'legacy-runtime.marker'
$RollbackApp = Join-Path $RunDirectory 'rollback-app'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$ClientDirectory = Join-Path $RepoRoot 'client'
$ClientDist = Join-Path $ClientDirectory 'dist'
$ApiProject = Join-Path $RepoRoot 'server\QLHV.Api\QLHV.Api.csproj'
$WorkerProject = Join-Path $RepoRoot 'server\QLHV.Worker\QLHV.Worker.csproj'
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'
$SourceStartScript = Join-Path $PSScriptRoot 'Start-QLHV-App.ps1'
$SourceLauncher = Join-Path $PSScriptRoot 'Start-QLHV-App.cmd'
$RealtimeWorkerServiceScript = Join-Path $PSScriptRoot 'RealtimeWorkerService.ps1'
. $RealtimeWorkerServiceScript
$StartScript = Join-Path $LauncherDirectory 'Start-QLHV-App.ps1'
$Launcher = Join-Path $LauncherDirectory 'Start-QLHV-App.cmd'
$ShortcutName = 'QLHV Th' + [char]0x00E0 + 'nh C' + [char]0x00F4 + 'ng.lnk'
$ShortcutPath = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) $ShortcutName
$StageRoot = Join-Path $RuntimeRoot ("update-stage-" + [Guid]::NewGuid().ToString('N'))
$StageApp = Join-Path $StageRoot 'app'
$StageWorker = Join-Path $StageApp 'worker'
$StageLauncher = Join-Path $StageRoot 'launcher'
$RollbackLauncher = Join-Path $RunDirectory 'rollback-launcher'
$ShortcutBackup = Join-Path $RunDirectory ("shortcut-backup-" + [Guid]::NewGuid().ToString('N') + '.lnk')
$script:UpdateStage = 'initialization'
$script:UpdateFailureLogged = $false
$script:ExistingRuntimeWasStopped = $false
$script:RollbackPathEntered = $false
$script:ProductionConfigBackupPath = $null

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Update-QLHV-App.ps1 must be run from PowerShell as Administrator.'
    }
}

function Assert-SafePaths {
    $actualRoot = [System.IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
    if (-not [string]::Equals($actualRoot, 'D:\QLHV_APP_RUNTIME', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify unexpected runtime root: $actualRoot"
    }
    if (-not (Test-Path -LiteralPath $AppDirectory -PathType Container)) {
        throw "QLHV runtime is not installed at $AppDirectory. Run Install-QLHV-App.ps1 first."
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
    # Fail closed for multiword/quoted values: once a sensitive marker is seen,
    # omit the complete exception message instead of risking a surviving suffix.
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
        $logPath = Join-Path $LogDirectory ('updater-' + (Get-Date -Format 'yyyyMMdd') + '.error.log')
        if ((Test-Path -LiteralPath $logPath -PathType Leaf) -and
            (Get-Item -LiteralPath $logPath).Length -ge 1MB) {
            $archive = Join-Path $LogDirectory ('updater-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.error.log')
            Move-Item -LiteralPath $logPath -Destination $archive
        }

        $safeMessage = Protect-DeploymentLogMessage -Message $Message
        Add-Content -LiteralPath $logPath -Encoding UTF8 -Value (
            "$(Get-Date -Format o) stage=$Stage message=$safeMessage")

        $cutoff = [DateTime]::UtcNow.AddDays(-30)
        $files = @(Get-ChildItem -LiteralPath $LogDirectory -File -Filter 'updater-*.error.log' |
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

function Assert-ProductionConfiguration {
    if (-not (Test-Path -LiteralPath $ProductionConfig -PathType Leaf)) {
        throw "Missing local production configuration: $ProductionConfig. Run the installer first."
    }
    try {
        $configuration = Get-Content -LiteralPath $ProductionConfig -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Local production configuration JSON is invalid: $ProductionConfig. The updater did not change it."
    }

    if ($null -eq $configuration) {
        throw "Local production configuration JSON is empty: $ProductionConfig. The updater did not change it."
    }
    $connectionStrings = $configuration.PSObject.Properties['ConnectionStrings']
    $qlhvApp = if ($null -eq $connectionStrings) {
        $null
    }
    else {
        $connectionStrings.Value.PSObject.Properties['QLHV_APP']
    }
    if ($null -eq $qlhvApp -or [string]::IsNullOrWhiteSpace([string]$qlhvApp.Value)) {
        throw "Local production configuration is missing ConnectionStrings:QLHV_APP: $ProductionConfig"
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
        throw "Local production configuration JSON is invalid: $Path. No configuration value was logged."
    }
    if ($null -eq $configuration) {
        throw "Local production configuration JSON is empty: $Path"
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

function Assert-ProductionWriteFlags {
    $configuration = Get-Content -LiteralPath $ProductionConfig -Raw -Encoding UTF8 | ConvertFrom-Json
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
        throw "Local production configuration must set Sync:DryRun to false: $ProductionConfig"
    }
    if ($null -eq $enableWrites -or
        $enableWrites.Value -isnot [bool] -or
        -not [bool]$enableWrites.Value) {
        throw "Local production configuration must set SyncExecution:EnableTargetWrites to true: $ProductionConfig"
    }
}

function Assert-ConfigurationUnchanged {
    param([Parameter(Mandatory = $true)][string]$ExpectedHash)

    if (-not (Test-Path -LiteralPath $ProductionConfig -PathType Leaf)) {
        throw 'Local production configuration disappeared during update; refusing to continue.'
    }
    $actualHash = (Get-FileHash -LiteralPath $ProductionConfig -Algorithm SHA256).Hash
    if ($actualHash -cne $ExpectedHash) {
        throw 'Local production configuration changed during update; refusing to continue.'
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

function Set-RestrictedConfigurationFileAcl {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$AccountName
    )

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

function Protect-ProductionConfigurationTree {
    Set-RestrictedConfigurationDirectoryAcl `
        -DirectoryPath $ConfigDirectory `
        -AccountName $RuntimeAccount

    $backupDirectory = Join-Path $ConfigDirectory 'backups'
    if (Test-Path -LiteralPath $backupDirectory -PathType Container) {
        Set-RestrictedConfigurationDirectoryAcl `
            -DirectoryPath $backupDirectory `
            -AccountName $RuntimeAccount
        Get-ChildItem -LiteralPath $backupDirectory -File -ErrorAction Stop |
            ForEach-Object {
                Set-RestrictedConfigurationFileAcl `
                    -FilePath $_.FullName `
                    -AccountName $RuntimeAccount
            }
    }

    if (Test-Path -LiteralPath $ProductionConfig -PathType Leaf) {
        Set-RestrictedConfigurationFileAcl `
            -FilePath $ProductionConfig `
            -AccountName $RuntimeAccount
    }
}

function Grant-RuntimeAppReadAccess {
    $grant = "${RuntimeAccount}:(OI)(CI)RX"
    & icacls.exe $AppDirectory '/grant:r' $grant '/T' '/C' '/Q' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant read/execute access on $AppDirectory to the runtime account."
    }

    if (Test-Path -LiteralPath $LauncherDirectory -PathType Container) {
        & icacls.exe $LauncherDirectory '/grant:r' $grant '/T' '/C' '/Q' | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not grant read/execute access on $LauncherDirectory to the runtime account."
        }
    }

    $photoSourceGrant = "${RuntimeAccount}:(OI)(CI)RX"
    & icacls.exe $PhotoSourceDirectory '/grant:r' $photoSourceGrant '/T' '/C' '/Q' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant read access on the photo source directory to the runtime account."
    }

    $photoOutputGrant = "${RuntimeAccount}:(OI)(CI)M"
    & icacls.exe $PhotoOutputDirectory '/grant:r' $photoOutputGrant '/T' '/C' '/Q' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant modify access on the derived photo directory to the runtime account."
    }
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

    Get-ChildItem -LiteralPath $StageApp -Recurse -File -Filter 'appsettings.Development*.json' |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $StageApp -Recurse -Directory |
        Where-Object { $_.Name -eq 'IM_GPLX' -or $_.Name -eq '.git' } |
        Sort-Object FullName -Descending |
        Remove-Item -Recurse -Force

    if (@(Get-ChildItem -LiteralPath $StageApp -Recurse -File -Filter 'appsettings.Development*.json').Count -gt 0) {
        throw 'Development appsettings were found in the update package.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $wwwroot 'index.html') -PathType Leaf)) {
        throw 'The update package does not contain wwwroot\index.html.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $StageWorker 'QLHV.Worker.exe') -PathType Leaf)) {
        throw 'The update package does not contain worker\QLHV.Worker.exe.'
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
        throw "QLHV runtime failed its health check (launcher exit code $LASTEXITCODE)."
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

function Invoke-ReadOnlySmokeTest {
    $checks = @(
        [pscustomobject]@{ Url = 'http://localhost:8088/health/live'; Expected = 200; Timeout = 5 },
        [pscustomobject]@{ Url = 'http://localhost:8088/health/ready'; Expected = 200; Timeout = 60 },
        [pscustomobject]@{ Url = 'http://localhost:8088/api/system/runtime-status'; Expected = 200; Timeout = 60 },
        [pscustomobject]@{ Url = 'http://localhost:8088/api/auth/me'; Expected = 401; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/'; Expected = 200; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/login'; Expected = 200; Timeout = 10 },
        [pscustomobject]@{ Url = 'http://localhost:8088/qlhv-import'; Expected = 200; Timeout = 10 }
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
            throw "Read-only update smoke test failed for $($check.Url) (expected $($check.Expected), received $statusCode)."
        }
    }
}

Assert-Administrator
Assert-SafePaths
if (-not (Test-Path -LiteralPath $WorkerProject -PathType Leaf)) {
    throw "QLHV.Worker project was not found: $WorkerProject"
}
New-Item -ItemType Directory -Path $RunDirectory, $ModelDirectory, $PhotoSourceDirectory, $PhotoOutputDirectory -Force | Out-Null
Assert-ProductionConfiguration

$workerServiceBefore = Get-QlhvRealtimeWorkerServiceSnapshot -RuntimeRoot $RuntimeRoot
$workerServiceInstalledThisRun = $false
$shortcutExistedBefore = Test-Path -LiteralPath $ShortcutPath -PathType Leaf
if ($shortcutExistedBefore) {
    Copy-Item -LiteralPath $ShortcutPath -Destination $ShortcutBackup
}
$newRuntimeInstalled = $false
$rollbackAvailable = $false
$newLauncherInstalled = $false
$launcherRollbackAvailable = $false
try {
    $script:UpdateStage = 'production-config'
    Protect-ProductionConfigurationTree
    [void](Set-QlhvProductionWriteFlags -Path $ProductionConfig)
    Protect-ProductionConfigurationTree
    Assert-ProductionWriteFlags
    Write-Host ("Photo processing model status: " + (Get-QlhvPhotoModelStatus -Path $ProductionConfig))
    $productionConfigHash = (Get-FileHash -LiteralPath $ProductionConfig -Algorithm SHA256).Hash

    # Build completely before touching the current runtime.
    $script:UpdateStage = 'build-publish'
    Build-PublishPackage
    $script:UpdateStage = 'config-integrity-before-transition'
    Assert-ConfigurationUnchanged -ExpectedHash $productionConfigHash

    $script:UpdateStage = 'stop-existing-runtime'
    & $StopScript -Quiet
    $script:ExistingRuntimeWasStopped = $true

    if (Test-Path -LiteralPath $RollbackApp) {
        $script:UpdateStage = 'remove-previous-rollback'
        Remove-Item -LiteralPath $RollbackApp -Recurse -Force
    }
    if (Test-Path -LiteralPath $RollbackLauncher) {
        $script:UpdateStage = 'remove-previous-launcher-rollback'
        Remove-Item -LiteralPath $RollbackLauncher -Recurse -Force
    }
    $script:UpdateStage = 'backup-current-runtime'
    Move-Item -LiteralPath $AppDirectory -Destination $RollbackApp
    $rollbackAvailable = $true
    if (Test-Path -LiteralPath $LauncherDirectory -PathType Container) {
        $script:UpdateStage = 'backup-current-launcher'
        Move-Item -LiteralPath $LauncherDirectory -Destination $RollbackLauncher
        $launcherRollbackAvailable = $true
    }

    try {
        $script:UpdateStage = 'activate-runtime'
        Move-Item -LiteralPath $StageApp -Destination $AppDirectory
        $newRuntimeInstalled = $true
        $script:UpdateStage = 'activate-launcher'
        Move-Item -LiteralPath $StageLauncher -Destination $LauncherDirectory
        $newLauncherInstalled = $true
        $script:UpdateStage = 'runtime-permissions'
        Grant-RuntimeAppReadAccess
        Remove-Item -LiteralPath $LegacyRuntimeMarker -Force -ErrorAction SilentlyContinue
        $script:UpdateStage = 'realtime-worker-service'
        Install-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
        $workerServiceInstalledThisRun = -not [bool]$workerServiceBefore.Exists
        $script:UpdateStage = 'launcher-readiness'
        Invoke-StartRuntime
        $script:UpdateStage = 'read-only-smoke'
        Invoke-ReadOnlySmokeTest
        $script:UpdateStage = 'config-integrity-after-smoke'
        Assert-ConfigurationUnchanged -ExpectedHash $productionConfigHash
        # The updater is elevated for atomic replacement/ACL work. Never leave the
        # LAN API running with that token; the operator starts it from the shortcut.
        $script:UpdateStage = 'stop-elevated-smoke-runtime'
        & $StopScript -Quiet
        $script:UpdateStage = 'start-realtime-worker-service'
        Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
        $script:UpdateStage = 'desktop-shortcut'
        Install-DesktopShortcut
        Remove-Item -LiteralPath $ShortcutBackup -Force -ErrorAction SilentlyContinue
        $script:UpdateStage = 'complete'
        Write-Host 'QLHV was updated and passed liveness/readiness checks.'
        Write-Host 'The API is stopped; start it normally with the QLHV Thanh Cong shortcut.'
        Write-Host 'Windows service QLHV_APP_RealtimeWorker is installed, automatic, and running.'
        Write-Host "Previous runtime backup: $RollbackApp"
    }
    catch {
        $updateError = $_
        $safeUpdateError = Protect-DeploymentLogMessage -Message ([string]$updateError.Exception.Message)
        # Persist the original activation/smoke failure before rollback changes context.
        Write-SafeDeploymentFailure -Stage $script:UpdateStage -Message ([string]$updateError.Exception.Message)
        $script:UpdateFailureLogged = $true
        try {
            & $StopScript -Quiet
        }
        catch {
            $safeStopError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
            Write-Warning "Could not stop the failed update cleanly: $safeStopError"
        }

        if (Test-Path -LiteralPath $AppDirectory) {
            $failedApp = Join-Path $RunDirectory ("failed-app-" + [Guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $AppDirectory -Destination $failedApp
            $newRuntimeInstalled = $false
        }
        if ($newLauncherInstalled -and (Test-Path -LiteralPath $LauncherDirectory -PathType Container)) {
            $failedLauncher = Join-Path $RunDirectory ("failed-launcher-" + [Guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $LauncherDirectory -Destination $failedLauncher
            $newLauncherInstalled = $false
        }
        if ($launcherRollbackAvailable -and (Test-Path -LiteralPath $RollbackLauncher -PathType Container)) {
            Move-Item -LiteralPath $RollbackLauncher -Destination $LauncherDirectory
            $launcherRollbackAvailable = $false
        }
        if ($rollbackAvailable -and (Test-Path -LiteralPath $RollbackApp)) {
            Move-Item -LiteralPath $RollbackApp -Destination $AppDirectory
            $rollbackAvailable = $false
            $script:RollbackPathEntered = $true
            Grant-RuntimeAppReadAccess
            if ([bool]$workerServiceBefore.Exists) {
                Install-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
            }
            Set-Content -LiteralPath $LegacyRuntimeMarker -Value 'legacy-health-compatible' -Encoding Ascii
        }

        try {
            Invoke-StartRuntime -AllowLegacyRollback
            & $StopScript -Quiet
        }
        catch {
            $safeVerificationError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
            throw "Update failed and the previous runtime was restored, but health verification also failed: $safeVerificationError. Original update error: $safeUpdateError"
        }
        if ($workerServiceInstalledThisRun -and -not [bool]$workerServiceBefore.Exists) {
            Remove-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
            $workerServiceInstalledThisRun = $false
        }
        Assert-ConfigurationUnchanged -ExpectedHash $productionConfigHash
        Write-Warning 'No SQL patch was run. If readiness reports missing schema, apply the documented patch separately before updating.'
        throw "Update failed. The previous runtime was restored, health-checked, and left stopped to avoid an elevated process. Start it from the shortcut. Original error: $safeUpdateError"
    }
}
catch {
    $outerUpdateError = $_
    $safeOuterError = Protect-DeploymentLogMessage -Message ([string]$outerUpdateError.Exception.Message)
    if (-not $script:UpdateFailureLogged) {
        Write-SafeDeploymentFailure -Stage $script:UpdateStage -Message ([string]$outerUpdateError.Exception.Message)
        $script:UpdateFailureLogged = $true
    }
    if ($shortcutExistedBefore -and (Test-Path -LiteralPath $ShortcutBackup -PathType Leaf)) {
        Copy-Item -LiteralPath $ShortcutBackup -Destination $ShortcutPath -Force
        Remove-Item -LiteralPath $ShortcutBackup -Force -ErrorAction SilentlyContinue
    }
    elseif (-not $shortcutExistedBefore -and (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        Remove-Item -LiteralPath $ShortcutPath -Force -ErrorAction SilentlyContinue
    }
    Restore-QlhvProductionConfigurationBackup
    if ($workerServiceInstalledThisRun -and -not [bool]$workerServiceBefore.Exists) {
        try {
            Remove-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
            $workerServiceInstalledThisRun = $false
        }
        catch {
            $safeWorkerRemoveError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
            Write-Warning "Could not remove the failed realtime worker service: $safeWorkerRemoveError"
        }
    }

    # If transition failed after Stop but before app->rollback completed, the prior
    # app is still installed. Health-check it with legacy compatibility, then stop
    # the elevated validation process and leave a durable marker for the shortcut.
    if ($script:ExistingRuntimeWasStopped -and -not $newRuntimeInstalled -and
        -not $rollbackAvailable -and -not $script:RollbackPathEntered -and
        (Test-Path -LiteralPath $AppDirectory -PathType Container)) {
        Set-Content -LiteralPath $LegacyRuntimeMarker -Value 'legacy-health-compatible' -Encoding Ascii
        try {
            Invoke-StartRuntime -AllowLegacyRollback
            & $StopScript -Quiet
        }
        catch {
            $safeRecoveryError = Protect-DeploymentLogMessage -Message ([string]$_.Exception.Message)
            throw "Update transition failed and the previous runtime could not be health-verified: $safeRecoveryError. Original error: $safeOuterError"
        }
        if ([bool]$workerServiceBefore.Exists -and [bool]$workerServiceBefore.WasRunning) {
            Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
        }
        throw "Update transition failed. The previous runtime remains installed, was health-checked, and is stopped. Start it from the shortcut. Original error: $safeOuterError"
    }
    if ([bool]$workerServiceBefore.Exists -and
        [bool]$workerServiceBefore.WasRunning -and
        (Test-Path -LiteralPath (Get-QlhvRealtimeWorkerExecutable -RuntimeRoot $RuntimeRoot) -PathType Leaf)) {
        Start-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $newRuntimeInstalled -and $rollbackAvailable -and
        -not (Test-Path -LiteralPath $AppDirectory) -and (Test-Path -LiteralPath $RollbackApp)) {
        Move-Item -LiteralPath $RollbackApp -Destination $AppDirectory -ErrorAction SilentlyContinue
    }
    if (-not $newLauncherInstalled -and $launcherRollbackAvailable -and
        -not (Test-Path -LiteralPath $LauncherDirectory) -and
        (Test-Path -LiteralPath $RollbackLauncher)) {
        Move-Item -LiteralPath $RollbackLauncher -Destination $LauncherDirectory -ErrorAction SilentlyContinue
    }
}
