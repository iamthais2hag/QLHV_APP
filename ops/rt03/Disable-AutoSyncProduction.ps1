[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$ExpectedConfigPath = 'D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($requiredValue in @($ConfigPath, $ExpectedConfigPath)) {
    if ([string]::IsNullOrWhiteSpace($requiredValue)) {
        throw 'RT03_AUTOSYNC_DISABLE_REQUIRED_ARGUMENT_EMPTY'
    }
}

$resolvedConfig = [System.IO.Path]::GetFullPath($ConfigPath)
$resolvedExpected = [System.IO.Path]::GetFullPath($ExpectedConfigPath)
if (-not [string]::Equals(
        $resolvedConfig,
        $resolvedExpected,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'RT03_AUTOSYNC_DISABLE_CONFIG_PATH_REJECTED'
}
if (-not (Test-Path -LiteralPath $resolvedConfig -PathType Leaf)) {
    throw 'RT03_AUTOSYNC_DISABLE_CONFIG_NOT_FOUND'
}

$configuration = Get-Content -LiteralPath $resolvedConfig -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($null -eq $configuration -or
    $null -eq $configuration.PSObject.Properties['QlhvAutoSync']) {
    throw 'RT03_AUTOSYNC_DISABLE_SECTION_MISSING'
}

$autoSync = $configuration.QlhvAutoSync
foreach ($setting in @(
        @{ Name = 'Enabled'; Value = $false },
        @{ Name = 'RunOnServerStartup'; Value = $false },
        @{ Name = 'PollingEnabled'; Value = $false },
        @{ Name = 'IsFallbackOnly'; Value = $true },
        @{ Name = 'FallbackModeEnabled'; Value = $false }
    )) {
    $property = $autoSync.PSObject.Properties[$setting.Name]
    if ($null -eq $property) {
        $autoSync | Add-Member -NotePropertyName $setting.Name -NotePropertyValue $setting.Value
    }
    else {
        $property.Value = $setting.Value
    }
}

$backupPath = $resolvedConfig + '.pre-rt03-autosync-disable'
if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
    Copy-Item -LiteralPath $resolvedConfig -Destination $backupPath -ErrorAction Stop
}

$temporaryPath = $resolvedConfig + '.rt03.tmp'
try {
    $json = $configuration | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText(
        $temporaryPath,
        $json,
        [System.Text.UTF8Encoding]::new($false))
    $roundTrip = Get-Content -LiteralPath $temporaryPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    if ($roundTrip.QlhvAutoSync.Enabled -ne $false -or
        $roundTrip.QlhvAutoSync.RunOnServerStartup -ne $false -or
        $roundTrip.QlhvAutoSync.PollingEnabled -ne $false -or
        $roundTrip.QlhvAutoSync.FallbackModeEnabled -ne $false) {
        throw 'RT03_AUTOSYNC_DISABLE_ROUNDTRIP_REJECTED'
    }
    Move-Item -LiteralPath $temporaryPath -Destination $resolvedConfig -Force
}
finally {
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
}

Write-Output 'RT03_AUTOSYNC_CONFIGURATION_DISABLED'
