[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$RollbackApp = Join-Path $RunDirectory 'rollback-app'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$ClientDirectory = Join-Path $RepoRoot 'client'
$ClientDist = Join-Path $ClientDirectory 'dist'
$ApiProject = Join-Path $RepoRoot 'server\QLHV.Api\QLHV.Api.csproj'
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'
$StartScript = Join-Path $PSScriptRoot 'Start-QLHV-App.ps1'
$StageRoot = Join-Path $RunDirectory ("update-stage-" + [Guid]::NewGuid().ToString('N'))
$StageApp = Join-Path $StageRoot 'app'

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

function Build-PublishPackage {
    New-Item -ItemType Directory -Path $StageApp -Force | Out-Null

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
    & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass `
        -File $StartScript -NoBrowser -SuppressErrorDialog
    if ($LASTEXITCODE -ne 0) {
        throw "QLHV runtime failed its health check (launcher exit code $LASTEXITCODE)."
    }
}

Assert-Administrator
Assert-SafePaths
New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null

$newRuntimeInstalled = $false
$rollbackAvailable = $false
try {
    # Build completely before touching the current runtime.
    Build-PublishPackage

    & $StopScript -Quiet

    if (Test-Path -LiteralPath $RollbackApp) {
        Remove-Item -LiteralPath $RollbackApp -Recurse -Force
    }
    Move-Item -LiteralPath $AppDirectory -Destination $RollbackApp
    $rollbackAvailable = $true

    try {
        Move-Item -LiteralPath $StageApp -Destination $AppDirectory
        $newRuntimeInstalled = $true
        Invoke-StartRuntime
        Write-Host 'QLHV was updated and passed GET /health.'
        Write-Host "Previous runtime backup: $RollbackApp"
    }
    catch {
        $updateError = $_
        try {
            & $StopScript -Quiet
        }
        catch {
            Write-Warning "Could not stop the failed update cleanly: $($_.Exception.Message)"
        }

        if (Test-Path -LiteralPath $AppDirectory) {
            $failedApp = Join-Path $RunDirectory ("failed-app-" + [Guid]::NewGuid().ToString('N'))
            Move-Item -LiteralPath $AppDirectory -Destination $failedApp
        }
        if ($rollbackAvailable -and (Test-Path -LiteralPath $RollbackApp)) {
            Move-Item -LiteralPath $RollbackApp -Destination $AppDirectory
            $rollbackAvailable = $false
        }

        try {
            Invoke-StartRuntime
        }
        catch {
            throw "Update failed and the previous runtime was restored, but restart also failed: $($_.Exception.Message). Original update error: $($updateError.Exception.Message)"
        }
        throw "Update failed. The previous runtime was restored and restarted successfully. Original error: $($updateError.Exception.Message)"
    }
}
finally {
    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $newRuntimeInstalled -and $rollbackAvailable -and
        -not (Test-Path -LiteralPath $AppDirectory) -and (Test-Path -LiteralPath $RollbackApp)) {
        Move-Item -LiteralPath $RollbackApp -Destination $AppDirectory -ErrorAction SilentlyContinue
    }
}
