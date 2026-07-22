[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$SuppressErrorDialog,
    [ValidateRange(5, 300)]
    [int]$HealthTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$LogDirectory = Join-Path $RuntimeRoot 'logs'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$PidFile = Join-Path $RunDirectory 'qlhv.pid'
$HealthUrl = 'http://localhost:8088/health'
$ApplicationUrl = 'http://localhost:8088'
$script:StartedProcessId = $null
$script:StartedThisRun = $false
$script:StdOutLog = $null
$script:StdErrLog = $null
$script:LauncherMutex = [System.Threading.Mutex]::new($false, 'Local\QLHV-App-LAN-8088-Launcher')
$script:LauncherMutexAcquired = $false

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Get-ProcessRecord {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    return Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction SilentlyContinue
}

function Test-IsQlhvRuntimeProcess {
    param([Parameter(Mandatory = $false)]$ProcessRecord)

    if ($null -eq $ProcessRecord -or [string]::IsNullOrWhiteSpace([string]$ProcessRecord.ExecutablePath)) {
        return $false
    }

    $actualExecutable = Get-NormalizedPath ([string]$ProcessRecord.ExecutablePath)
    $publishedExecutable = Get-NormalizedPath (Join-Path $AppDirectory 'QLHV.Api.exe')
    if ([string]::Equals($actualExecutable, $publishedExecutable, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $publishedDll = Get-NormalizedPath (Join-Path $AppDirectory 'QLHV.Api.dll')
    $commandLine = [string]$ProcessRecord.CommandLine
    $dllArgumentPattern = '(?i)(?:^|\s)"?' + [Regex]::Escape($publishedDll) + '"?(?:\s|$)'
    return [string]::Equals(
            [System.IO.Path]::GetFileName($actualExecutable),
            'dotnet.exe',
            [System.StringComparison]::OrdinalIgnoreCase) -and
        [Regex]::IsMatch($commandLine, $dllArgumentPattern)
}

function Get-PortOwnerIds {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 8088 -ErrorAction SilentlyContinue)
    return @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
}

function Test-Health {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $HealthUrl -Method Get -TimeoutSec 3
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 300
    }
    catch {
        return $false
    }
}

function Wait-ForHealth {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $record = Get-ProcessRecord -ProcessId $ProcessId
        if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
            throw "QLHV runtime exited before it became healthy. Review logs in $LogDirectory."
        }

        if (Test-Health) {
            $owners = @(Get-PortOwnerIds)
            if ($owners -contains $ProcessId) {
                return
            }
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "QLHV did not pass GET /health within $TimeoutSeconds seconds. Review logs in $LogDirectory."
}

function Set-ProcessEnvironmentAndStart {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    $environmentNames = @(
        'ASPNETCORE_ENVIRONMENT',
        'ASPNETCORE_URLS',
        'HttpsRedirection__Enabled',
        'Authentication__Cookie__SecurePolicy',
        'FileStorage__Root'
    )
    $previousValues = @{}
    foreach ($name in $environmentNames) {
        $item = Get-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            $previousValues[$name] = [string]$item.Value
        }
    }

    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Production'
        $env:ASPNETCORE_URLS = 'http://0.0.0.0:8088'
        $env:HttpsRedirection__Enabled = 'false'
        $env:Authentication__Cookie__SecurePolicy = 'SameAsRequest'
        if ([string]::IsNullOrWhiteSpace($env:FileStorage__Root)) {
            # Keep real student photos outside the disposable publish/runtime directory.
            $env:FileStorage__Root = 'D:\QLHV_APP'
        }

        $startParameters = @{
            FilePath = $FilePath
            WorkingDirectory = $AppDirectory
            RedirectStandardOutput = $script:StdOutLog
            RedirectStandardError = $script:StdErrLog
            WindowStyle = 'Hidden'
            PassThru = $true
        }
        if ($ArgumentList.Count -gt 0) {
            $startParameters.ArgumentList = $ArgumentList
        }
        return Start-Process @startParameters
    }
    finally {
        foreach ($name in $environmentNames) {
            if ($previousValues.ContainsKey($name)) {
                Set-Item -LiteralPath "Env:$name" -Value $previousValues[$name]
            }
            else {
                Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
            }
        }
    }
}

function Show-LauncherError {
    param([Parameter(Mandatory = $true)][string]$Message)

    $details = "$Message`r`n`r`nLogs: $LogDirectory"
    if ($SuppressErrorDialog) {
        return
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.MessageBox]::Show(
            $details,
            'QLHV Thanh Cong',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error)
    }
    catch {
        Write-Error $details
    }
}

try {
    try {
        $mutexWait = [TimeSpan]::FromSeconds($HealthTimeoutSeconds + 15)
        $script:LauncherMutexAcquired = $script:LauncherMutex.WaitOne($mutexWait)
    }
    catch [System.Threading.AbandonedMutexException] {
        # The previous launcher exited unexpectedly; this process now owns the mutex.
        $script:LauncherMutexAcquired = $true
    }
    if (-not $script:LauncherMutexAcquired) {
        throw 'Another QLHV launcher is still checking or starting the runtime. Try again in a moment.'
    }

    foreach ($requiredDirectory in @($RuntimeRoot, $AppDirectory, $LogDirectory, $RunDirectory)) {
        if (-not (Test-Path -LiteralPath $requiredDirectory -PathType Container)) {
            throw "Runtime is not installed correctly: missing $requiredDirectory. Run Install-QLHV-App.ps1 as Administrator."
        }
    }

    $publishedExe = Join-Path $AppDirectory 'QLHV.Api.exe'
    $publishedDll = Join-Path $AppDirectory 'QLHV.Api.dll'
    if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf) -and
        -not (Test-Path -LiteralPath $publishedDll -PathType Leaf)) {
        throw "QLHV.Api executable was not found in $AppDirectory. Run Install-QLHV-App.ps1 as Administrator."
    }

    $portOwners = @(Get-PortOwnerIds)
    if ($portOwners.Count -gt 0) {
        $qlhvOwners = @()
        $otherOwners = @()
        foreach ($ownerId in $portOwners) {
            $record = Get-ProcessRecord -ProcessId ([int]$ownerId)
            if (Test-IsQlhvRuntimeProcess -ProcessRecord $record) {
                $qlhvOwners += [int]$ownerId
            }
            else {
                $otherOwners += [int]$ownerId
            }
        }

        if ($otherOwners.Count -gt 0) {
            throw "TCP port 8088 is already used by another process (PID: $($otherOwners -join ', ')). QLHV was not started."
        }
        if ($qlhvOwners.Count -ne 1) {
            throw "Unexpected QLHV listener state on TCP port 8088. QLHV was not started."
        }

        $script:StartedProcessId = $qlhvOwners[0]
        Set-Content -LiteralPath $PidFile -Value $script:StartedProcessId -Encoding Ascii
        Wait-ForHealth -ProcessId $script:StartedProcessId -TimeoutSeconds $HealthTimeoutSeconds
    }
    else {
        if (Test-Path -LiteralPath $PidFile -PathType Leaf) {
            $rawPid = (Get-Content -Raw -LiteralPath $PidFile).Trim()
            $savedProcessId = 0
            if ([int]::TryParse($rawPid, [ref]$savedProcessId)) {
                $savedRecord = Get-ProcessRecord -ProcessId $savedProcessId
                if (Test-IsQlhvRuntimeProcess -ProcessRecord $savedRecord) {
                    throw "QLHV runtime PID $savedProcessId exists but is not healthy on port 8088. Run Stop-QLHV-App.ps1, then try again."
                }
            }
            Remove-Item -LiteralPath $PidFile -Force
        }

        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $script:StdOutLog = Join-Path $LogDirectory "qlhv-$timestamp.out.log"
        $script:StdErrLog = Join-Path $LogDirectory "qlhv-$timestamp.err.log"

        if (Test-Path -LiteralPath $publishedExe -PathType Leaf) {
            $process = Set-ProcessEnvironmentAndStart -FilePath $publishedExe
        }
        else {
            $dotnetCommand = Get-Command 'dotnet.exe' -ErrorAction Stop
            $quotedDll = '"' + $publishedDll + '"'
            $process = Set-ProcessEnvironmentAndStart -FilePath $dotnetCommand.Source -ArgumentList @($quotedDll)
        }

        $script:StartedProcessId = [int]$process.Id
        $script:StartedThisRun = $true
        Set-Content -LiteralPath $PidFile -Value $script:StartedProcessId -Encoding Ascii
        Wait-ForHealth -ProcessId $script:StartedProcessId -TimeoutSeconds $HealthTimeoutSeconds
    }

    if (-not $NoBrowser) {
        Start-Process $ApplicationUrl
    }

    Write-Host "QLHV is ready at $ApplicationUrl (PID $script:StartedProcessId)."
}
catch {
    $message = $_.Exception.Message
    try {
        New-Item -ItemType Directory -Path $LogDirectory -Force -ErrorAction SilentlyContinue | Out-Null
        Add-Content -LiteralPath (Join-Path $LogDirectory 'launcher-error.log') -Value "$(Get-Date -Format o) $message"
    }
    catch {
        # Keep the original startup error.
    }

    if ($script:StartedThisRun -and $null -ne $script:StartedProcessId) {
        $failedRecord = Get-ProcessRecord -ProcessId ([int]$script:StartedProcessId)
        if (Test-IsQlhvRuntimeProcess -ProcessRecord $failedRecord) {
            Stop-Process -Id ([int]$script:StartedProcessId) -Force -ErrorAction SilentlyContinue
            try {
                Wait-Process -Id ([int]$script:StartedProcessId) -Timeout 15 -ErrorAction Stop
            }
            catch {
                # The process may already be gone; startup logs still retain the original error.
            }
        }
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
    }

    Show-LauncherError -Message $message
    throw
}
finally {
    if ($script:LauncherMutexAcquired) {
        $script:LauncherMutex.ReleaseMutex()
    }
    $script:LauncherMutex.Dispose()
}
