[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$LogDirectory = Join-Path $RuntimeRoot 'logs'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$FirewallDisplayName = 'QLHV App LAN - TCP 8088 (Private)'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$ClientDirectory = Join-Path $RepoRoot 'client'
$ClientDist = Join-Path $ClientDirectory 'dist'
$ApiProject = Join-Path $RepoRoot 'server\QLHV.Api\QLHV.Api.csproj'
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'
$Launcher = Join-Path $PSScriptRoot 'Start-QLHV-App.cmd'
$StageRoot = Join-Path $RunDirectory ("install-stage-" + [Guid]::NewGuid().ToString('N'))
$StageApp = Join-Path $StageRoot 'app'
$InstallBackup = Join-Path $RunDirectory ("install-backup-" + [Guid]::NewGuid().ToString('N'))

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
    # Recreate only the exact QLHV rule name so reruns cannot accumulate duplicates or broaden profiles.
    $ruleByName = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue)
    if ($ruleByName.Count -gt 0) {
        $ruleByName | Remove-NetFirewallRule
    }
    $existingRules = @(Get-NetFirewallRule -DisplayName $FirewallDisplayName -ErrorAction SilentlyContinue)
    if ($existingRules.Count -gt 0) {
        $existingRules | Remove-NetFirewallRule
    }
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

function Install-DesktopShortcut {
    if (-not (Test-Path -LiteralPath $Launcher -PathType Leaf)) {
        throw "Launcher was not found: $Launcher"
    }

    $shortcutName = 'QLHV Th' + [char]0x00E0 + 'nh C' + [char]0x00F4 + 'ng.lnk'
    $desktopDirectory = [Environment]::GetFolderPath('CommonDesktopDirectory')
    $shortcutPath = Join-Path $desktopDirectory $shortcutName
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
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

Assert-Administrator
Assert-SafeRuntimeRoot

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'server\QLHV.sln') -PathType Leaf) -or
    -not (Test-Path -LiteralPath $ApiProject -PathType Leaf)) {
    throw "QLHV source repository was not found at $RepoRoot."
}

New-Item -ItemType Directory -Path $RuntimeRoot, $LogDirectory, $RunDirectory -Force | Out-Null

$deployed = $false
$oldRuntimeMoved = $false
try {
    Build-PublishPackage

    & $StopScript -Quiet

    if (Test-Path -LiteralPath $AppDirectory) {
        Move-Item -LiteralPath $AppDirectory -Destination $InstallBackup
        $oldRuntimeMoved = $true
    }

    try {
        Move-Item -LiteralPath $StageApp -Destination $AppDirectory
        $deployed = $true
    }
    catch {
        if ($oldRuntimeMoved -and -not (Test-Path -LiteralPath $AppDirectory)) {
            Move-Item -LiteralPath $InstallBackup -Destination $AppDirectory
            $oldRuntimeMoved = $false
        }
        throw
    }

    if ($oldRuntimeMoved -and (Test-Path -LiteralPath $InstallBackup)) {
        Remove-Item -LiteralPath $InstallBackup -Recurse -Force
        $oldRuntimeMoved = $false
    }

    Install-FirewallRule
    Install-DesktopShortcut

    Write-Host 'QLHV LAN runtime installed successfully.'
    Write-Host "Runtime: $AppDirectory"
    Write-Host "Logs:    $LogDirectory"
    Write-Host 'Shortcut: QLHV Thanh Cong (Public Desktop)'
}
finally {
    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $deployed -and $oldRuntimeMoved -and -not (Test-Path -LiteralPath $AppDirectory) -and
        (Test-Path -LiteralPath $InstallBackup)) {
        Move-Item -LiteralPath $InstallBackup -Destination $AppDirectory -ErrorAction SilentlyContinue
    }
}
