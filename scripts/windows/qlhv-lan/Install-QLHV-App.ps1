[CmdletBinding()]
param(
    [string]$RuntimeAccount = [Security.Principal.WindowsIdentity]::GetCurrent().Name
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$ConfigDirectory = Join-Path $RuntimeRoot 'config'
$ProductionConfig = Join-Path $ConfigDirectory 'appsettings.Production.Local.json'
$LogDirectory = Join-Path $RuntimeRoot 'logs'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$LegacyRuntimeMarker = Join-Path $RunDirectory 'legacy-runtime.marker'
$FirewallDisplayName = 'QLHV App LAN - TCP 8088 (Private)'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$ClientDirectory = Join-Path $RepoRoot 'client'
$ClientDist = Join-Path $ClientDirectory 'dist'
$ApiProject = Join-Path $RepoRoot 'server\QLHV.Api\QLHV.Api.csproj'
$DevelopmentSettings = Join-Path $RepoRoot 'server\QLHV.Api\appsettings.Development.json'
# The Development file is guarded by Test-Path and parsed only for allow-listed extraction;
# it is never copied into the staging or runtime directory.
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'
$StartScript = Join-Path $PSScriptRoot 'Start-QLHV-App.ps1'
$Launcher = Join-Path $PSScriptRoot 'Start-QLHV-App.cmd'
$ShortcutName = 'QLHV Th' + [char]0x00E0 + 'nh C' + [char]0x00F4 + 'ng.lnk'
$ShortcutPath = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) $ShortcutName
$StageRoot = Join-Path $RuntimeRoot ("install-stage-" + [Guid]::NewGuid().ToString('N'))
$StageApp = Join-Path $StageRoot 'app'
$InstallBackup = Join-Path $RunDirectory ("install-backup-" + [Guid]::NewGuid().ToString('N'))
$script:InstallStage = 'initialization'
$script:ExistingRuntimeWasStopped = $false
$script:RollbackPathEntered = $false

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

function Assert-QlhvProductionConfiguration {
    param([Parameter(Mandatory = $true)][string]$Path)

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
    Grant-RuntimeDirectoryAccess -Path $LogDirectory -AccountName $RuntimeAccount -Access Modify
    Grant-RuntimeDirectoryAccess -Path $RunDirectory -AccountName $RuntimeAccount -Access Modify
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
        # An existing valid local production file is never rewritten by install/update.
        Assert-QlhvProductionConfiguration -Path $ProductionConfig
        Set-RestrictedConfigurationAcl `
            -DirectoryPath $ConfigDirectory `
            -FilePath $ProductionConfig `
            -AccountName $RuntimeAccount
        Write-Host "Existing local production configuration was preserved: $ProductionConfig"
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
        Assert-QlhvProductionConfiguration -Path $temporaryConfig
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
    New-Item -ItemType Directory -Path $StageApp -Force | Out-Null

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
    $shortcut.WorkingDirectory = $RepoRoot
    $shortcut.Description = 'Start QLHV Thanh Cong and open the browser.'
    $shortcut.WindowStyle = 7
    $iconPath = Join-Path $AppDirectory 'QLHV.Api.exe'
    if (Test-Path -LiteralPath $iconPath -PathType Leaf) {
        $shortcut.IconLocation = "$iconPath,0"
    }
    $shortcut.Save()
}

function Invoke-StartRuntime {
    param([switch]$AllowLegacyRollback)

    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', $StartScript, '-NoBrowser', '-SuppressErrorDialog'
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
    -not (Test-Path -LiteralPath $ApiProject -PathType Leaf)) {
    throw "QLHV source repository was not found at $RepoRoot."
}

New-Item -ItemType Directory -Path $RuntimeRoot, $ConfigDirectory, $LogDirectory, $RunDirectory -Force | Out-Null

$installationSucceeded = $false
$newRuntimeInstalled = $false
$oldRuntimeMoved = $false
$firewallExistedBefore = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue).Count -gt 0
$shortcutExistedBefore = Test-Path -LiteralPath $ShortcutPath -PathType Leaf
try {
    $script:InstallStage = 'production-config'
    Initialize-ProductionConfiguration
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

    $script:InstallStage = 'activate-runtime'
    Move-Item -LiteralPath $StageApp -Destination $AppDirectory
    $newRuntimeInstalled = $true
    $script:InstallStage = 'runtime-permissions'
    Set-RuntimeDirectoryAccess
    Remove-Item -LiteralPath $LegacyRuntimeMarker -Force -ErrorAction SilentlyContinue

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

    Write-Host 'QLHV LAN runtime installed successfully.'
    Write-Host "Runtime: $AppDirectory"
    Write-Host "Logs:    $LogDirectory"
    Write-Host "Config:  $ProductionConfig (protected; values were not logged)"
    Write-Host 'Shortcut: QLHV Thanh Cong (Public Desktop)'
}
catch {
    $installError = $_
    $safeInstallError = Protect-DeploymentLogMessage -Message ([string]$installError.Exception.Message)
    # Persist the original failure before rollback can replace its context.
    Write-SafeDeploymentFailure `
        -Stage $script:InstallStage `
        -Message ([string]$installError.Exception.Message)
    if (-not $firewallExistedBefore) {
        $newFirewallRules = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue)
        foreach ($newFirewallRule in $newFirewallRules) {
            Remove-NetFirewallRule -Name ([string]$newFirewallRule.Name) -ErrorAction SilentlyContinue
        }
    }
    if (-not $shortcutExistedBefore -and (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        Remove-Item -LiteralPath $ShortcutPath -Force -ErrorAction SilentlyContinue
    }
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
