[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$SuppressErrorDialog,
    [switch]$AllowLegacyRollback,
    [ValidateRange(10, 300)]
    [int]$HealthTimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RuntimeRoot = 'D:\QLHV_APP_RUNTIME'
$AppDirectory = Join-Path $RuntimeRoot 'app'
$ConfigDirectory = Join-Path $RuntimeRoot 'config'
$ProductionConfig = Join-Path $ConfigDirectory 'appsettings.Production.Local.json'
$LogDirectory = Join-Path $RuntimeRoot 'logs'
$RunDirectory = Join-Path $RuntimeRoot 'run'
$PidFile = Join-Path $RunDirectory 'qlhv.pid'
$LegacyRuntimeMarker = Join-Path $RunDirectory 'legacy-runtime.marker'
$LauncherLockFile = Join-Path $RunDirectory 'launcher.lock'
$LiveUrl = 'http://localhost:8088/health/live'
$ReadyUrl = 'http://localhost:8088/health/ready'
$LegacyHealthUrl = 'http://localhost:8088/health'
$RuntimeStatusUrl = 'http://localhost:8088/api/system/runtime-status'
$ApplicationUrl = 'http://localhost:8088'
$script:StartedProcessId = $null
$script:StartedThisRun = $false
$script:StdOutLog = $null
$script:StdErrLog = $null
$script:LauncherMutex = [System.Threading.Mutex]::new($false, 'Local\QLHV-App-LAN-8088-Launcher')
$script:LauncherMutexAcquired = $false
$script:LauncherLockStream = $null
$script:UseLegacyRuntime = $AllowLegacyRollback -or (Test-Path -LiteralPath $LegacyRuntimeMarker -PathType Leaf)

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Enter-CrossSessionLauncherLock {
    param([Parameter(Mandatory = $true)][int]$TimeoutSeconds)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            # FileShare.None serializes launchers across Windows sessions/users;
            # the in-process named mutex alone would only cover the current session.
            $script:LauncherLockStream = [IO.File]::Open(
                $LauncherLockFile,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None)
            return
        }
        catch [IO.IOException] {
            Start-Sleep -Milliseconds 250
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'Another QLHV launcher is active in a different Windows session. Try again in a moment.'
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

function Get-QlhvRuntimeProcessIds {
    $records = @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue)
    return @($records | Where-Object {
        Test-IsQlhvRuntimeProcess -ProcessRecord $_
    } | Select-Object -ExpandProperty ProcessId -Unique)
}

function Assert-ProductionConfiguration {
    if (-not (Test-Path -LiteralPath $ProductionConfig -PathType Leaf)) {
        throw "Thiếu hoặc sai cấu hình QLHV_APP. Kiểm tra: $ProductionConfig"
    }

    try {
        $configuration = Get-Content -LiteralPath $ProductionConfig -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Thiếu hoặc sai cấu hình QLHV_APP. Kiểm tra JSON tại: $ProductionConfig"
    }

    if ($null -eq $configuration) {
        throw "Thiếu hoặc sai cấu hình QLHV_APP. JSON rỗng tại: $ProductionConfig"
    }
    $connectionStringsProperty = $configuration.PSObject.Properties['ConnectionStrings']
    if ($null -eq $connectionStringsProperty) {
        throw "Thiếu hoặc sai cấu hình QLHV_APP. Thiếu section ConnectionStrings tại: $ProductionConfig"
    }
    $qlhvProperty = $connectionStringsProperty.Value.PSObject.Properties['QLHV_APP']
    if ($null -eq $qlhvProperty -or [string]::IsNullOrWhiteSpace([string]$qlhvProperty.Value)) {
        throw "Thiếu hoặc sai cấu hình QLHV_APP. Thiếu ConnectionStrings:QLHV_APP tại: $ProductionConfig"
    }
}

function Get-LegacyConfigurationEnvironment {
    $configuration = Get-Content -LiteralPath $ProductionConfig -Raw -Encoding UTF8 | ConvertFrom-Json
    $result = @{}
    $allowedSections = @(
        'ConnectionStrings', 'ConnectionProfileEncryption', 'ConnectionProfileProtection',
        'DataProtection', 'FileStorage', 'Sync', 'SyncExecution', 'Authentication'
    )

    function Add-ConfigurationNode {
        param(
            [Parameter(Mandatory = $false)]$Node,
            [Parameter(Mandatory = $true)][string]$Prefix
        )
        if ($null -eq $Node) { return }
        if ($Node -is [pscustomobject]) {
            foreach ($property in $Node.PSObject.Properties) {
                Add-ConfigurationNode -Node $property.Value -Prefix ($Prefix + '__' + $property.Name)
            }
            return
        }
        if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
            $index = 0
            foreach ($item in $Node) {
                Add-ConfigurationNode -Node $item -Prefix ($Prefix + '__' + $index)
                $index++
            }
            return
        }
        $result[$Prefix] = [string]$Node
    }

    foreach ($sectionName in $allowedSections) {
        $property = $configuration.PSObject.Properties[$sectionName]
        if ($null -ne $property) {
            Add-ConfigurationNode -Node $property.Value -Prefix $sectionName
        }
    }
    return $result
}

function Get-SafeProbeMessage {
    param([Parameter(Mandatory = $false)][string]$Json)

    if ([string]::IsNullOrWhiteSpace($Json)) {
        return $null
    }

    try {
        $payload = $Json | ConvertFrom-Json
        $messagesProperty = $payload.PSObject.Properties['messages']
        if ($null -eq $messagesProperty) {
            return $null
        }
        $message = (@($messagesProperty.Value) | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_)
        }) -join '; '
        if ($message.Length -gt 700) {
            $message = $message.Substring(0, 700) + '...'
        }
        if ($message -match '(?i)(passwordhash|set-cookie|authorization\s*:|(?:password|pwd|user\s*id|data\s*source|server|initial\s*catalog)\s*=)') {
            return 'Runtime readiness failed; sensitive diagnostic details were omitted.'
        }
        return $message
    }
    catch {
        return $null
    }
}

function Invoke-HealthProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [ValidateRange(2, 90)][int]$TimeoutSeconds = 4
    )

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -Method Get -TimeoutSec $TimeoutSeconds
        return [pscustomobject]@{
            Success = $response.StatusCode -ge 200 -and $response.StatusCode -lt 300
            StatusCode = [int]$response.StatusCode
            Message = Get-SafeProbeMessage -Json ([string]$response.Content)
        }
    }
    catch {
        $statusCode = 0
        $errorDetailsJson = $null
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
            $statusProperty = $responseProperty.Value.PSObject.Properties['StatusCode']
            if ($null -ne $statusProperty) {
                $statusCode = [int]$statusProperty.Value
            }
        }
        # ErrorDetails is optional for connection failures and some HTTP responses.
        # StrictMode must never turn a transient readiness response into a launcher crash.
        $errorDetailsProperty = $_.PSObject.Properties['ErrorDetails']
        if ($null -ne $errorDetailsProperty -and $null -ne $errorDetailsProperty.Value) {
            $messageProperty = $errorDetailsProperty.Value.PSObject.Properties['Message']
            if ($null -ne $messageProperty) {
                $errorDetailsJson = [string]$messageProperty.Value
            }
        }
        return [pscustomobject]@{
            Success = $false
            StatusCode = $statusCode
            Message = Get-SafeProbeMessage -Json $errorDetailsJson
        }
    }
}

function Wait-ForEndpoint {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$DisplayName,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastMessage = $null
    do {
        $record = Get-ProcessRecord -ProcessId $ProcessId
        if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
            throw "QLHV runtime exited before $DisplayName succeeded. Review logs in $LogDirectory."
        }

        $requestTimeout = if ($DisplayName -eq 'readiness') { 45 } else { 4 }
        $probe = Invoke-HealthProbe -Url $Url -TimeoutSeconds $requestTimeout
        if ($probe.Success) {
            $owners = @(Get-PortOwnerIds)
            if ($owners -contains $ProcessId) {
                return
            }
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$probe.Message)) {
            $lastMessage = [string]$probe.Message
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($DisplayName -eq 'readiness') {
        $statusProbe = Invoke-HealthProbe -Url $RuntimeStatusUrl -TimeoutSeconds 45
        if (-not [string]::IsNullOrWhiteSpace([string]$statusProbe.Message)) {
            $lastMessage = [string]$statusProbe.Message
        }
    }
    $reason = if ([string]::IsNullOrWhiteSpace($lastMessage)) { '' } else { " Reason: $lastMessage" }
    throw "QLHV did not pass $DisplayName at $Url within $TimeoutSeconds seconds.$reason Review logs in $LogDirectory."
}

function Wait-ForLegacyRollbackHealth {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $record = Get-ProcessRecord -ProcessId $ProcessId
        if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
            throw "Restored QLHV runtime exited before its health check succeeded. Review $LogDirectory."
        }

        $live = Invoke-HealthProbe -Url $LiveUrl -TimeoutSeconds 4
        if ($live.Success) {
            $ready = Invoke-HealthProbe -Url $ReadyUrl -TimeoutSeconds 45
            if ($ready.Success) { return }
        }
        elseif ($live.StatusCode -eq 404) {
            $legacy = Invoke-HealthProbe -Url $LegacyHealthUrl -TimeoutSeconds 10
            if ($legacy.Success) { return }
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Restored QLHV runtime did not pass legacy or current health checks within $TimeoutSeconds seconds."
}

function Stop-QlhvProcessById {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $record = Get-ProcessRecord -ProcessId $ProcessId
    if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
        throw "PID $ProcessId is not the QLHV runtime in $AppDirectory. No process was stopped."
    }

    # Validate identity again immediately before stopping; never stop all dotnet processes.
    $record = Get-ProcessRecord -ProcessId $ProcessId
    if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
        throw "QLHV process identity changed before stop. No process was stopped."
    }
    Stop-Process -Id $ProcessId -Force -ErrorAction Stop
    try {
        Wait-Process -Id $ProcessId -Timeout 15 -ErrorAction Stop
    }
    catch {
        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
            throw "QLHV PID $ProcessId could not be stopped."
        }
    }
    Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
}

function Remove-ExpiredLauncherLogs {
    $cutoff = [DateTime]::UtcNow.AddDays(-30)
    $files = @(Get-ChildItem -LiteralPath $LogDirectory -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'launcher-*' } |
        Sort-Object LastWriteTimeUtc -Descending)
    for ($index = 0; $index -lt $files.Count; $index++) {
        if ($index -ge 30 -or $files[$index].LastWriteTimeUtc -lt $cutoff) {
            Remove-Item -LiteralPath $files[$index].FullName -Force -ErrorAction SilentlyContinue
        }
    }
}

function Start-QlhvRuntime {
    $legacyEnvironment = if ($script:UseLegacyRuntime) {
        Get-LegacyConfigurationEnvironment
    }
    else {
        @{}
    }
    $environmentNames = @(@(
        'ASPNETCORE_ENVIRONMENT',
        'ASPNETCORE_URLS',
        'HttpsRedirection__Enabled',
        'Authentication__Cookie__SecurePolicy',
        'QlhvRuntime__ProductionLocalConfigPath',
        'QlhvRuntime__Root',
        'Logging__Console__LogLevel__Default'
    ) + @($legacyEnvironment.Keys) | Select-Object -Unique)
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
        $env:QlhvRuntime__ProductionLocalConfigPath = $ProductionConfig
        $env:QlhvRuntime__Root = $RuntimeRoot
        # The Production rolling file logger is the durable sink. Disable raw console
        # application logs so redirected bootstrap files stay bounded and cannot expose values.
        $env:Logging__Console__LogLevel__Default = 'None'
        foreach ($entry in $legacyEnvironment.GetEnumerator()) {
            Set-Item -LiteralPath ("Env:" + $entry.Key) -Value ([string]$entry.Value)
        }

        $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $script:StdOutLog = Join-Path $LogDirectory "launcher-$timestamp.out.log"
        $script:StdErrLog = Join-Path $LogDirectory "launcher-$timestamp.err.log"
        $publishedExe = Join-Path $AppDirectory 'QLHV.Api.exe'
        $publishedDll = Join-Path $AppDirectory 'QLHV.Api.dll'
        if (Test-Path -LiteralPath $publishedExe -PathType Leaf) {
            $filePath = $publishedExe
            $argumentList = @()
        }
        else {
            $filePath = (Get-Command 'dotnet.exe' -ErrorAction Stop).Source
            $argumentList = @('"' + $publishedDll + '"')
        }

        $startParameters = @{
            FilePath = $filePath
            WorkingDirectory = $AppDirectory
            WindowStyle = 'Hidden'
            RedirectStandardOutput = $script:StdOutLog
            RedirectStandardError = $script:StdErrLog
            PassThru = $true
        }
        if ($argumentList.Count -gt 0) {
            $startParameters.ArgumentList = $argumentList
        }
        $process = Start-Process @startParameters
        $script:StartedProcessId = [int]$process.Id
        $script:StartedThisRun = $true
        Set-Content -LiteralPath $PidFile -Value $script:StartedProcessId -Encoding Ascii
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

function Get-SafeLauncherMessage {
    param([Parameter(Mandatory = $true)][string]$Message)

    if ($Message -match '(?i)(passwordhash|set-cookie|authorization\s*:|(?:password|pwd|user\s*id|data\s*source|server|initial\s*catalog)\s*=)') {
        return 'QLHV startup failed. Sensitive diagnostic details were omitted.'
    }
    return $Message
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
            'QLHV Thành Công',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error)
    }
    catch {
        Write-Error $details
    }
}

try {
    try {
        $mutexWait = [TimeSpan]::FromSeconds($HealthTimeoutSeconds + 30)
        $script:LauncherMutexAcquired = $script:LauncherMutex.WaitOne($mutexWait)
    }
    catch [System.Threading.AbandonedMutexException] {
        $script:LauncherMutexAcquired = $true
    }
    if (-not $script:LauncherMutexAcquired) {
        throw 'Another QLHV launcher is still checking or starting the runtime. Try again in a moment.'
    }

    foreach ($requiredDirectory in @($RuntimeRoot, $AppDirectory, $ConfigDirectory, $LogDirectory, $RunDirectory)) {
        if (-not (Test-Path -LiteralPath $requiredDirectory -PathType Container)) {
            throw "Runtime is not installed correctly: missing $requiredDirectory. Run Install-QLHV-App.ps1 as Administrator."
        }
    }
    Enter-CrossSessionLauncherLock -TimeoutSeconds ($HealthTimeoutSeconds + 30)
    Assert-ProductionConfiguration
    Remove-ExpiredLauncherLogs

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
            throw 'Unexpected QLHV listener state on TCP port 8088. QLHV was not started.'
        }

        # Reconcile exact runtime executables even when their PID file is missing/stale.
        # This prevents a listener plus an orphaned QLHV process from coexisting.
        $orphanedRuntimeIds = @(Get-QlhvRuntimeProcessIds | Where-Object { $qlhvOwners -notcontains [int]$_ })
        foreach ($orphanedRuntimeId in $orphanedRuntimeIds) {
            Stop-QlhvProcessById -ProcessId ([int]$orphanedRuntimeId)
        }

        $existingId = $qlhvOwners[0]
        $liveProbe = Invoke-HealthProbe -Url $LiveUrl -TimeoutSeconds 4
        $readyProbe = Invoke-HealthProbe -Url $ReadyUrl -TimeoutSeconds 45
        $legacyReady = $false
        if ($script:UseLegacyRuntime -and $liveProbe.StatusCode -eq 404) {
            $legacyProbe = Invoke-HealthProbe -Url $LegacyHealthUrl -TimeoutSeconds 10
            $legacyReady = $legacyProbe.Success
        }
        if (($liveProbe.Success -and $readyProbe.Success) -or $legacyReady) {
            $script:StartedProcessId = $existingId
            Set-Content -LiteralPath $PidFile -Value $existingId -Encoding Ascii
        }
        else {
            # A hung or stale QLHV instance is restarted by exact, verified PID only.
            Stop-QlhvProcessById -ProcessId $existingId
        }
    }
    else {
        # A QLHV process can be hung before binding its port and its PID file may be
        # absent. Enumerate only the exact published executable/dll and stop by PID.
        foreach ($orphanedRuntimeId in @(Get-QlhvRuntimeProcessIds)) {
            Stop-QlhvProcessById -ProcessId ([int]$orphanedRuntimeId)
        }
        if (Test-Path -LiteralPath $PidFile -PathType Leaf) {
            $rawSavedId = (Get-Content -LiteralPath $PidFile -Raw).Trim()
            $savedId = 0
            if ([int]::TryParse($rawSavedId, [ref]$savedId) -and $savedId -gt 0) {
                $savedRecord = Get-ProcessRecord -ProcessId $savedId
                if (Test-IsQlhvRuntimeProcess -ProcessRecord $savedRecord) {
                    Stop-QlhvProcessById -ProcessId $savedId
                }
            }
            Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
        }
    }

    if ($null -eq $script:StartedProcessId) {
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
        Start-QlhvRuntime
        if ($script:UseLegacyRuntime) {
            Wait-ForLegacyRollbackHealth -ProcessId $script:StartedProcessId -TimeoutSeconds $HealthTimeoutSeconds
        }
        else {
            $liveTimeout = [Math]::Min(30, $HealthTimeoutSeconds)
            Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $LiveUrl -DisplayName 'liveness' -TimeoutSeconds $liveTimeout
            Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $ReadyUrl -DisplayName 'readiness' -TimeoutSeconds $HealthTimeoutSeconds
        }
    }

    if (-not $NoBrowser) {
        Start-Process $ApplicationUrl
    }

    Write-Host "QLHV is ready at $ApplicationUrl (PID $script:StartedProcessId)."
}
catch {
    $message = Get-SafeLauncherMessage -Message $_.Exception.Message
    try {
        New-Item -ItemType Directory -Path $LogDirectory -Force -ErrorAction SilentlyContinue | Out-Null
        $launcherErrorLog = Join-Path $LogDirectory ('launcher-' + (Get-Date -Format 'yyyyMMdd') + '.error.log')
        Add-Content -LiteralPath $launcherErrorLog -Value "$(Get-Date -Format o) $message"
    }
    catch {
        # Keep the original startup error.
    }

    if ($script:StartedThisRun -and $null -ne $script:StartedProcessId) {
        $failedRecord = Get-ProcessRecord -ProcessId ([int]$script:StartedProcessId)
        if (Test-IsQlhvRuntimeProcess -ProcessRecord $failedRecord) {
            try { Stop-QlhvProcessById -ProcessId ([int]$script:StartedProcessId) } catch { }
        }
        Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
    }
    Show-LauncherError -Message $message
    throw $message
}
finally {
    if ($null -ne $script:LauncherLockStream) {
        $script:LauncherLockStream.Dispose()
    }
    if ($script:LauncherMutexAcquired) {
        $script:LauncherMutex.ReleaseMutex()
    }
    $script:LauncherMutex.Dispose()
}
