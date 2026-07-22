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
$RollbackApp = Join-Path $RunDirectory 'rollback-app'
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$ClientDirectory = Join-Path $RepoRoot 'client'
$ClientDist = Join-Path $ClientDirectory 'dist'
$ApiProject = Join-Path $RepoRoot 'server\QLHV.Api\QLHV.Api.csproj'
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'
$StartScript = Join-Path $PSScriptRoot 'Start-QLHV-App.ps1'
$StageRoot = Join-Path $RuntimeRoot ("update-stage-" + [Guid]::NewGuid().ToString('N'))
$StageApp = Join-Path $StageRoot 'app'
$script:UpdateStage = 'initialization'
$script:UpdateFailureLogged = $false
$script:ExistingRuntimeWasStopped = $false
$script:RollbackPathEntered = $false

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

function Grant-RuntimeAppReadAccess {
    $grant = "${RuntimeAccount}:(OI)(CI)RX"
    & icacls.exe $AppDirectory '/grant:r' $grant '/T' '/C' '/Q' | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not grant read/execute access on $AppDirectory to the runtime account."
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
        throw "QLHV runtime failed its health check (launcher exit code $LASTEXITCODE)."
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
New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
Assert-ProductionConfiguration
$productionConfigHash = (Get-FileHash -LiteralPath $ProductionConfig -Algorithm SHA256).Hash

$newRuntimeInstalled = $false
$rollbackAvailable = $false
try {
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
    $script:UpdateStage = 'backup-current-runtime'
    Move-Item -LiteralPath $AppDirectory -Destination $RollbackApp
    $rollbackAvailable = $true

    try {
        $script:UpdateStage = 'activate-runtime'
        Move-Item -LiteralPath $StageApp -Destination $AppDirectory
        $newRuntimeInstalled = $true
        $script:UpdateStage = 'runtime-permissions'
        Grant-RuntimeAppReadAccess
        Remove-Item -LiteralPath $LegacyRuntimeMarker -Force -ErrorAction SilentlyContinue
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
        $script:UpdateStage = 'complete'
        Write-Host 'QLHV was updated and passed liveness/readiness checks.'
        Write-Host 'Runtime is stopped. Start it normally with the QLHV Thanh Cong shortcut.'
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
        if ($rollbackAvailable -and (Test-Path -LiteralPath $RollbackApp)) {
            Move-Item -LiteralPath $RollbackApp -Destination $AppDirectory
            $rollbackAvailable = $false
            $script:RollbackPathEntered = $true
            Grant-RuntimeAppReadAccess
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
        throw "Update transition failed. The previous runtime remains installed, was health-checked, and is stopped. Start it from the shortcut. Original error: $safeOuterError"
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
}
