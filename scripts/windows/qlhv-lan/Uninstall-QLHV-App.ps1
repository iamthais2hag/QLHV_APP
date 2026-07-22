[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$FirewallDisplayName = 'QLHV App LAN - TCP 8088 (Private)'
$StopScript = Join-Path $PSScriptRoot 'Stop-QLHV-App.ps1'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Uninstall-QLHV-App.ps1 must be run from PowerShell as Administrator.'
    }
}

Assert-Administrator
$normalizedRoot = [System.IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
if (-not [string]::Equals($normalizedRoot, 'D:\QLHV_APP_RUNTIME', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove unexpected runtime root: $normalizedRoot"
}

if (-not $Force) {
    $answer = Read-Host 'Remove the QLHV runtime, runtime logs, firewall rule and Desktop shortcut? Type UNINSTALL to continue'
    if ($answer -cne 'UNINSTALL') {
        Write-Host 'Uninstall cancelled.'
        return
    }
}

# This validates the saved PID and executable path; it never stops all dotnet/node processes.
& $StopScript -Quiet

$rulesByName = @(Get-NetFirewallRule -Name 'QLHV-App-LAN-TCP-8088-Private' -ErrorAction SilentlyContinue)
if ($rulesByName.Count -gt 0) {
    $rulesByName | Remove-NetFirewallRule
}
$rules = @(Get-NetFirewallRule -DisplayName $FirewallDisplayName -ErrorAction SilentlyContinue)
if ($rules.Count -gt 0) {
    $rules | Remove-NetFirewallRule
}

$shortcutName = 'QLHV Th' + [char]0x00E0 + 'nh C' + [char]0x00F4 + 'ng.lnk'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) $shortcutName
if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

if (Test-Path -LiteralPath $RuntimeRoot -PathType Container) {
    Remove-Item -LiteralPath $RuntimeRoot -Recurse -Force
}

Write-Host 'QLHV LAN runtime was removed. Source code and SQL databases were not changed.'
