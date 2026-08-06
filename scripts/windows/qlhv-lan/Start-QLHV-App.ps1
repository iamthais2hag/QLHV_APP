[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$SuppressErrorDialog,
    [switch]$AllowLegacyRollback,
    [switch]$DisableAutoSync,
    [ValidateSet('ProductionService', 'DevelopmentLocalHost')]
    [string]$StartupMode = 'ProductionService',
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
$LauncherEventLog = Join-Path $LogDirectory ('launcher-' + (Get-Date -Format 'yyyyMMdd') + '.event.log')
$script:StartedProcessId = $null
$script:ConnectedProcessId = $null
$script:StartedThisRun = $false
$script:StdOutLog = $null
$script:StdErrLog = $null
$script:LauncherMutex = [System.Threading.Mutex]::new($false, 'Local\QLHV-App-LAN-8088-Launcher')
$script:LauncherMutexAcquired = $false
$script:LauncherLockStream = $null
$script:ProgressForm = $null
$script:ProgressLabel = $null
$script:ProgressBar = $null
$script:UseLegacyRuntime = $AllowLegacyRollback -or (Test-Path -LiteralPath $LegacyRuntimeMarker -PathType Leaf)

function Initialize-LauncherProgress {
    if ($NoBrowser -or $SuppressErrorDialog) {
        return
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing

        $form = [System.Windows.Forms.Form]::new()
        $form.Text = 'QLHV Thanh Cong'
        $form.ClientSize = [System.Drawing.Size]::new(460, 118)
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
        $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        $form.ControlBox = $false
        $form.TopMost = $true

        $label = [System.Windows.Forms.Label]::new()
        $label.Location = [System.Drawing.Point]::new(22, 18)
        $label.Size = [System.Drawing.Size]::new(416, 42)
        $label.Font = [System.Drawing.Font]::new('Segoe UI', 11)
        $label.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
        $label.Text = 'Dang ket noi may chu...'

        $progress = [System.Windows.Forms.ProgressBar]::new()
        $progress.Location = [System.Drawing.Point]::new(22, 72)
        $progress.Size = [System.Drawing.Size]::new(416, 18)
        $progress.Style = [System.Windows.Forms.ProgressBarStyle]::Marquee
        $progress.MarqueeAnimationSpeed = 28

        [void]$form.Controls.Add($label)
        [void]$form.Controls.Add($progress)
        $script:ProgressForm = $form
        $script:ProgressLabel = $label
        $script:ProgressBar = $progress
        $form.Show()
        [System.Windows.Forms.Application]::DoEvents()
    }
    catch {
        # The launcher can still run safely when an interactive desktop is unavailable.
        $script:ProgressForm = $null
        $script:ProgressLabel = $null
        $script:ProgressBar = $null
    }
}

function Set-LauncherProgress {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [switch]$Completed
    )

    Write-Host $Message
    if ($null -eq $script:ProgressForm -or $script:ProgressForm.IsDisposed) {
        return
    }

    $script:ProgressLabel.Text = $Message
    if ($Completed) {
        $script:ProgressBar.Style = [System.Windows.Forms.ProgressBarStyle]::Continuous
        $script:ProgressBar.Value = 100
    }
    [System.Windows.Forms.Application]::DoEvents()
}

function Close-LauncherProgress {
    try {
        if ($null -ne $script:ProgressForm -and -not $script:ProgressForm.IsDisposed) {
            $script:ProgressForm.Close()
            $script:ProgressForm.Dispose()
        }
    }
    catch {
        # A progress-window cleanup failure must not stop or restart the server.
    }
    finally {
        $script:ProgressForm = $null
        $script:ProgressLabel = $null
        $script:ProgressBar = $null
    }
}

function Get-UiText {
    param([Parameter(Mandatory = $true)][string]$Base64)

    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Base64))
}

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

function Exit-LauncherCoordinationLocks {
    if ($null -ne $script:LauncherLockStream) {
        $script:LauncherLockStream.Dispose()
        $script:LauncherLockStream = $null
    }
    if ($script:LauncherMutexAcquired) {
        $script:LauncherMutex.ReleaseMutex()
        $script:LauncherMutexAcquired = $false
    }
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
        'DataProtection', 'FileStorage', 'Sync', 'SyncExecution', 'QlhvAutoSync',
        'PhotoProcessing', 'Authentication'
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

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $false)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Invoke-QlhvApiIdentityProbe {
    param([ValidateRange(2, 30)][int]$TimeoutSeconds = 4)

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $RuntimeStatusUrl -Method Get -TimeoutSec $TimeoutSeconds
        if ([int]$response.StatusCode -ne 200) {
            return [pscustomobject]@{
                Success = $false
                StatusCode = [int]$response.StatusCode
                ErrorCode = 'HTTP_STATUS_MISMATCH'
                Reason = "Expected HTTP 200 but received $([int]$response.StatusCode)."
            }
        }

        try {
            $payload = ([string]$response.Content) | ConvertFrom-Json
        }
        catch {
            return [pscustomobject]@{
                Success = $false
                StatusCode = 200
                ErrorCode = 'INVALID_RUNTIME_STATUS_JSON'
                Reason = 'The runtime status response is not valid JSON.'
            }
        }

        $build = Get-JsonPropertyValue -Object $payload -Name 'build'
        $isReady = Get-JsonPropertyValue -Object $payload -Name 'isReady'
        $version = [string](Get-JsonPropertyValue -Object $payload -Name 'version')
        $timeContractVersion = [string](Get-JsonPropertyValue -Object $payload -Name 'timeContractVersion')
        $hostProcess = [string](Get-JsonPropertyValue -Object $build -Name 'hostProcess')
        $applicationVersion = [string](Get-JsonPropertyValue -Object $build -Name 'applicationVersion')
        $frontendBuildId = [string](Get-JsonPropertyValue -Object $build -Name 'frontendBuildId')

        $failures = @()
        if ($isReady -ne $true) { $failures += 'isReady is not true' }
        if ($version -notmatch '^1\.') { $failures += 'runtime version is incompatible' }
        if ($timeContractVersion -ne '2.0') { $failures += 'time contract is incompatible' }
        if ($hostProcess -ne 'QLHV.Api') { $failures += 'host process identity is not QLHV.Api' }
        if ($applicationVersion -notmatch '^1\.') { $failures += 'application version is incompatible' }
        if ($frontendBuildId -notmatch '^qlhv-ui-') { $failures += 'frontend build identity is invalid' }

        if ($failures.Count -gt 0) {
            return [pscustomobject]@{
                Success = $false
                StatusCode = 200
                ErrorCode = 'QLHV_API_IDENTITY_MISMATCH'
                Reason = ($failures -join '; ')
            }
        }

        return [pscustomobject]@{
            Success = $true
            StatusCode = 200
            ErrorCode = 'NONE'
            Reason = 'HTTP 200; QLHV.Api identity and contract 2.0 verified.'
        }
    }
    catch {
        $message = Get-SafeLauncherMessage -Message ([string]$_.Exception.Message)
        if ($message.Length -gt 300) {
            $message = $message.Substring(0, 300) + '...'
        }
        return [pscustomobject]@{
            Success = $false
            StatusCode = 0
            ErrorCode = 'RUNTIME_STATUS_UNAVAILABLE'
            Reason = $message
        }
    }
}

function Write-LauncherEvent {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $false)][string]$Details = ''
    )

    try {
        New-Item -ItemType Directory -Path $LogDirectory -Force -ErrorAction SilentlyContinue | Out-Null
        $safeDetails = Get-SafeLauncherMessage -Message $Details
        Add-Content -LiteralPath $LauncherEventLog -Encoding UTF8 -Value (
            "$(Get-Date -Format o) code=$Code mode=$StartupMode api=$ApplicationUrl $safeDetails".Trim())
    }
    catch {
        # Logging failure must not alter API ownership or launcher decisions.
    }
}

function Wait-ForQlhvApiIdentity {
    param(
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [switch]$WaitForListener
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastProbe = $null
    $lastOwners = @()
    do {
        $lastOwners = @(Get-PortOwnerIds)
        if ($lastOwners.Count -gt 0) {
            if ($lastOwners.Count -ne 1) {
                throw "PORT_IN_USE_BY_UNKNOWN_PROCESS port=8088 pid=$($lastOwners -join ',') healthFailure=multiple_listeners logs=$LogDirectory"
            }

            $lastProbe = Invoke-QlhvApiIdentityProbe -TimeoutSeconds ([Math]::Min(4, $TimeoutSeconds))
            Write-LauncherEvent -Code 'API_IDENTITY_PROBE' -Details (
                "pid=$($lastOwners[0]) http=$($lastProbe.StatusCode) result=$($lastProbe.Success) reason=$($lastProbe.ErrorCode)")
            if ($lastProbe.Success) {
                return [pscustomobject]@{
                    ProcessId = [int]$lastOwners[0]
                    Probe = $lastProbe
                }
            }
        }
        elseif (-not $WaitForListener) {
            break
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($lastOwners.Count -gt 0) {
        $reason = if ($null -eq $lastProbe) { 'health_not_checked' } else { $lastProbe.ErrorCode + ':' + $lastProbe.Reason }
        throw "PORT_IN_USE_BY_UNKNOWN_PROCESS port=8088 pid=$($lastOwners -join ',') healthFailure=$reason logs=$LogDirectory"
    }

    throw "SERVER_UNAVAILABLE port=8088 timeoutSeconds=$TimeoutSeconds logs=$LogDirectory"
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
        'QlhvAutoSync__Enabled',
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
        if ($DisableAutoSync) {
            $env:QlhvAutoSync__Enabled = 'false'
        }
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
        return 'Exit'
    }

    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing

        $form = [System.Windows.Forms.Form]::new()
        $form.Text = 'QLHV Thanh Cong'
        $form.ClientSize = [System.Drawing.Size]::new(620, 230)
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
        $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
        $form.MaximizeBox = $false
        $form.MinimizeBox = $false
        $form.TopMost = $true
        $form.Tag = 'Exit'

        $label = [System.Windows.Forms.Label]::new()
        $label.Location = [System.Drawing.Point]::new(18, 14)
        $label.Size = [System.Drawing.Size]::new(584, 24)
        $label.Font = [System.Drawing.Font]::new('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
        $label.Text = Get-UiText -Base64 'S2jDtG5nIHRo4buDIGvhur90IG7hu5FpIG3DoXkgY2jhu6cgUUxIVg=='

        $detailsBox = [System.Windows.Forms.TextBox]::new()
        $detailsBox.Location = [System.Drawing.Point]::new(18, 46)
        $detailsBox.Size = [System.Drawing.Size]::new(584, 116)
        $detailsBox.Multiline = $true
        $detailsBox.ReadOnly = $true
        $detailsBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
        $detailsBox.Text = $details

        $retryButton = [System.Windows.Forms.Button]::new()
        $retryButton.Location = [System.Drawing.Point]::new(296, 180)
        $retryButton.Size = [System.Drawing.Size]::new(96, 32)
        $retryButton.Text = Get-UiText -Base64 'VGjhu60gbOG6oWk='
        $retryButton.Add_Click({ $form.Tag = 'Retry'; $form.Close() })

        $detailsButton = [System.Windows.Forms.Button]::new()
        $detailsButton.Location = [System.Drawing.Point]::new(398, 180)
        $detailsButton.Size = [System.Drawing.Size]::new(100, 32)
        $detailsButton.Text = Get-UiText -Base64 'WGVtIGNoaSB0aeG6v3Q='
        $detailsButton.Add_Click({ Start-Process explorer.exe -ArgumentList ('"' + $LogDirectory + '"') })

        $exitButton = [System.Windows.Forms.Button]::new()
        $exitButton.Location = [System.Drawing.Point]::new(504, 180)
        $exitButton.Size = [System.Drawing.Size]::new(98, 32)
        $exitButton.Text = Get-UiText -Base64 'VGhvw6F0'
        $exitButton.Add_Click({ $form.Tag = 'Exit'; $form.Close() })

        [void]$form.Controls.Add($label)
        [void]$form.Controls.Add($detailsBox)
        [void]$form.Controls.Add($retryButton)
        [void]$form.Controls.Add($detailsButton)
        [void]$form.Controls.Add($exitButton)
        [void]$form.ShowDialog()
        return [string]$form.Tag
    }
    catch {
        Write-Error $details
        return 'Exit'
    }
}

function Start-LauncherRetry {
    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden',
        '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
        '-StartupMode', $StartupMode,
        '-HealthTimeoutSeconds', [string]$HealthTimeoutSeconds
    )
    if ($AllowLegacyRollback) { $arguments += '-AllowLegacyRollback' }
    if ($DisableAutoSync) { $arguments += '-DisableAutoSync' }
    Start-Process powershell.exe -ArgumentList $arguments -WindowStyle Hidden | Out-Null
}

# Main launcher lifecycle: connect to the one official API, then open the browser.
# Auto Sync is an independent hosted/Admin operation. Desktop startup never
# reads data-freshness status, creates a session operation, or waits for one.
try {
    Initialize-LauncherProgress
    Set-LauncherProgress -Message (Get-UiText -Base64 'xJBhbmcga+G6v3QgbuG7kWkgbcOheSBjaOG7pyBRTEhWLi4u')
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

    foreach ($requiredDirectory in @($RuntimeRoot, $AppDirectory, $LogDirectory, $RunDirectory)) {
        if (-not (Test-Path -LiteralPath $requiredDirectory -PathType Container)) {
            throw "Runtime is not installed correctly: missing $requiredDirectory. Run Install-QLHV-App.ps1 as Administrator."
        }
    }
    Enter-CrossSessionLauncherLock -TimeoutSeconds ($HealthTimeoutSeconds + 30)
    Remove-ExpiredLauncherLogs
    Write-LauncherEvent -Code 'LAUNCHER_START' -Details 'retryPolicy=bounded identityEndpoint=/api/system/runtime-status'

    $portOwners = @(Get-PortOwnerIds)
    if ($portOwners.Count -gt 0) {
        # Endpoint identity is authoritative. Process-path metadata may be unavailable
        # to a standard desktop token even when the official API service is healthy.
        $identity = Wait-ForQlhvApiIdentity -TimeoutSeconds ([Math]::Min(15, $HealthTimeoutSeconds))
        $script:ConnectedProcessId = [int]$identity.ProcessId
    }
    elseif ($StartupMode -eq 'ProductionService') {
        # ProductionService is connect-only. A Desktop launch never owns API startup.
        $identity = Wait-ForQlhvApiIdentity -TimeoutSeconds $HealthTimeoutSeconds -WaitForListener
        $script:ConnectedProcessId = [int]$identity.ProcessId
    }
    else {
        # DevelopmentLocalHost is explicit opt-in and is the only mode allowed to
        # create one local API process when port 8088 is empty.
        $runtimeIds = @(Get-QlhvRuntimeProcessIds)
        if ($runtimeIds.Count -gt 1) {
            throw "Multiple QLHV runtime processes already exist (PID: $($runtimeIds -join ', ')). No process was stopped or started."
        }
        if ($runtimeIds.Count -eq 1) {
            # The existing process may still be binding the port. Join it and wait;
            # never start a second process and never use restart as a readiness fix.
            $script:StartedProcessId = [int]$runtimeIds[0]
            if ($script:UseLegacyRuntime) {
                Wait-ForLegacyRollbackHealth -ProcessId $script:StartedProcessId -TimeoutSeconds $HealthTimeoutSeconds
            }
            else {
                $liveTimeout = [Math]::Min(30, $HealthTimeoutSeconds)
                Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $LiveUrl -DisplayName 'liveness' -TimeoutSeconds $liveTimeout
                Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $ReadyUrl -DisplayName 'readiness' -TimeoutSeconds $HealthTimeoutSeconds
            }
            $identity = Wait-ForQlhvApiIdentity -TimeoutSeconds ([Math]::Min(15, $HealthTimeoutSeconds)) -WaitForListener
            $script:ConnectedProcessId = [int]$identity.ProcessId
        }
        else {
            # A stale PID file is metadata only; removing it does not stop a process.
            Remove-Item -LiteralPath $PidFile -Force -ErrorAction SilentlyContinue
            Assert-ProductionConfiguration
            $publishedExe = Join-Path $AppDirectory 'QLHV.Api.exe'
            $publishedDll = Join-Path $AppDirectory 'QLHV.Api.dll'
            if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf) -and
                -not (Test-Path -LiteralPath $publishedDll -PathType Leaf)) {
                throw "QLHV.Api executable was not found in $AppDirectory. Run Install-QLHV-App.ps1 as Administrator."
            }

            Start-QlhvRuntime
            if ($script:UseLegacyRuntime) {
                Wait-ForLegacyRollbackHealth -ProcessId $script:StartedProcessId -TimeoutSeconds $HealthTimeoutSeconds
            }
            else {
                $liveTimeout = [Math]::Min(30, $HealthTimeoutSeconds)
                Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $LiveUrl -DisplayName 'liveness' -TimeoutSeconds $liveTimeout
                Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $ReadyUrl -DisplayName 'readiness' -TimeoutSeconds $HealthTimeoutSeconds
            }
            $identity = Wait-ForQlhvApiIdentity -TimeoutSeconds ([Math]::Min(15, $HealthTimeoutSeconds)) -WaitForListener
            $script:ConnectedProcessId = [int]$identity.ProcessId
        }
    }

    if ($null -eq $script:ConnectedProcessId) {
        throw 'SERVER_UNAVAILABLE: no verified QLHV API listener was selected.'
    }

    # Server identity is settled. Release launcher-only locks before
    # opening the application so another icon invocation is never held behind
    # browser or data-operation work.
    Exit-LauncherCoordinationLocks

    Write-LauncherEvent -Code 'QLHV_API_READY' -Details (
        "pid=$script:ConnectedProcessId http=200 identity=QLHV.Api contract=2.0 startedThisRun=$script:StartedThisRun")
    Set-LauncherProgress -Message (Get-UiText -Base64 'xJDDoyBr4bq/dCBu4buRaSBtw6F5IGNo4bunIFFMSFY=') -Completed
    $browserUrl = "$ApplicationUrl/qlhv-import"

    if (-not $NoBrowser) {
        Start-Process $browserUrl
    }
    Close-LauncherProgress

    Write-Host "QLHV is ready at $ApplicationUrl (PID $script:ConnectedProcessId; mode $StartupMode; startedThisRun=$script:StartedThisRun)."
}
catch {
    Close-LauncherProgress
    $message = Get-SafeLauncherMessage -Message $_.Exception.Message
    try {
        New-Item -ItemType Directory -Path $LogDirectory -Force -ErrorAction SilentlyContinue | Out-Null
        $launcherErrorLog = Join-Path $LogDirectory ('launcher-' + (Get-Date -Format 'yyyyMMdd') + '.error.log')
        Add-Content -LiteralPath $launcherErrorLog -Value "$(Get-Date -Format o) $message"
        Write-LauncherEvent -Code 'LAUNCHER_FAILED' -Details ("error=" + $message)
    }
    catch {
        # Keep the original startup error.
    }

    # A failed identity check never opens an unknown listener and never stops it.
    $action = Show-LauncherError -Message $message
    if ($action -eq 'Retry') {
        Start-LauncherRetry
    }
    throw $message
}
finally {
    Close-LauncherProgress
    Exit-LauncherCoordinationLocks
    $script:LauncherMutex.Dispose()
}
