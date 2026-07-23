[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$SuppressErrorDialog,
    [switch]$AllowLegacyRollback,
    [switch]$DisableAutoSync,
    [ValidateRange(10, 300)]
    [int]$HealthTimeoutSeconds = 90,
    [ValidateRange(60, 14400)]
    [int]$SyncTimeoutSeconds = 7200
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
$SessionStartSyncUrl = 'http://localhost:8088/api/dong-bo-v2/qlhv/operations/session-start-sync'
$SessionStartStatusUrl = 'http://localhost:8088/api/dong-bo-v2/qlhv/operations/session-start-sync/status'
$NeedSyncRequestTimeoutSeconds = 120
$ApplicationUrl = 'http://localhost:8088'
$script:StartedProcessId = $null
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

function Invoke-SessionStartSync {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $body = @{
        serverStartedByLauncher = [bool]$script:StartedThisRun
    } | ConvertTo-Json -Compress
    $headers = @{
        'X-QLHV-Local-Launcher' = 'session-start-v1'
    }

    $postFailure = $null
    try {
        # The POST is intentionally attempted once. A timeout is ambiguous: the
        # server may already have accepted it, so recovery uses GET and never reposts.
        $response = Invoke-RestMethod `
            -UseBasicParsing `
            -Uri $SessionStartSyncUrl `
            -Method Post `
            -Headers $headers `
            -ContentType 'application/json' `
            -Body $body `
            -TimeoutSec $NeedSyncRequestTimeoutSeconds

        $runIdProperty = $response.PSObject.Properties['runId']
        $acceptedProperty = $response.PSObject.Properties['accepted']
        if ($null -ne $runIdProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$runIdProperty.Value) -and
            $null -ne $acceptedProperty -and
            [bool]$acceptedProperty.Value) {
            return [string]$runIdProperty.Value
        }
        $postFailure = 'The local QLHV session-start response was ambiguous.'
    }
    catch {
        $postFailure = Get-SafeLauncherMessage -Message ([string]$_.Exception.Message)
    }

    # Reconcile an ambiguous POST against authoritative status. The loop is
    # bounded, uses GET only, and either joins the discovered run, waits for a
    # manual operation that won the race, or observes that no sync is needed.
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempt = 0
    $waitingForManualOperation = $false
    do {
        $record = Get-ProcessRecord -ProcessId $ProcessId
        if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
            throw 'QLHV runtime da dung trong khi xac minh phien Auto Sync.'
        }

        $attempt++
        try {
            $status = Get-SessionStartStatus -AttemptCount 3
            $operationActiveProperty = $status.PSObject.Properties['operationActive']
            $activeRunIdProperty = $status.PSObject.Properties['activeRunId']
            $runIdProperty = $status.PSObject.Properties['runId']
            $needSyncProperty = $status.PSObject.Properties['needSync']
            if ($null -eq $operationActiveProperty -or $null -eq $needSyncProperty) {
                throw 'Phan hoi trang thai Auto Sync thieu field bat buoc.'
            }

            $activeRunId = if ($null -ne $activeRunIdProperty) {
                [string]$activeRunIdProperty.Value
            }
            else {
                $null
            }
            if ([string]::IsNullOrWhiteSpace($activeRunId) -and
                $null -ne $operationActiveProperty -and
                [bool]$operationActiveProperty.Value -and
                $null -ne $runIdProperty) {
                $activeRunId = [string]$runIdProperty.Value
            }
            if (-not [string]::IsNullOrWhiteSpace($activeRunId)) {
                return $activeRunId
            }
            if ([bool]$operationActiveProperty.Value) {
                $waitingForManualOperation = $true
                Set-LauncherProgress -Message 'Dang cho thao tac cap nhat hien tai ket thuc...'
                Start-Sleep -Seconds 2
                continue
            }
            if (-not [bool]$needSyncProperty.Value) {
                return $null
            }

            if ($waitingForManualOperation) {
                $postFailure =
                    'Thao tac truoc da ket thuc nhung du lieu van can cap nhat; hay bam lai icon.'
                break
            }
        }
        catch {
            $safeReconcileFailure = Get-SafeLauncherMessage -Message ([string]$_.Exception.Message)
            if ($waitingForManualOperation) {
                $postFailure = $safeReconcileFailure
            }
            elseif ($attempt -ge 10) {
                throw "Khong the xac minh phien Auto Sync sau POST: $postFailure; $safeReconcileFailure"
            }
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    if ([DateTime]::UtcNow -ge $deadline) {
        throw "Khong the xac minh phien Auto Sync sau POST trong $TimeoutSeconds giay: $postFailure"
    }
    throw "Khong the xac minh phien Auto Sync sau POST: $postFailure"
}

function Get-SessionStartStatus {
    param(
        [Parameter(Mandatory = $false)][string]$RunId,
        [ValidateRange(1, 10)][int]$AttemptCount = 4
    )

    $startedByLauncher = ([bool]$script:StartedThisRun).ToString().ToLowerInvariant()
    $url = "${SessionStartStatusUrl}?serverStartedByLauncher=$startedByLauncher"
    if (-not [string]::IsNullOrWhiteSpace($RunId)) {
        $url += '&runId=' + [Uri]::EscapeDataString($RunId)
    }
    # The initial NeedSync snapshot reads and hashes both Live and BAK
    # partitions. Give that legitimate read-only work more time than the
    # lightweight exact-run poll while retaining a bounded request timeout.
    $requestTimeoutSeconds = if ([string]::IsNullOrWhiteSpace($RunId)) {
        $NeedSyncRequestTimeoutSeconds
    }
    else {
        20
    }

    $lastFailure = $null
    for ($attempt = 1; $attempt -le $AttemptCount; $attempt++) {
        try {
            return Invoke-RestMethod `
                -UseBasicParsing `
                -Uri $url `
                -Method Get `
                -Headers @{ 'X-QLHV-Local-Launcher' = 'session-start-v1' } `
                -TimeoutSec $requestTimeoutSeconds
        }
        catch {
            $lastFailure = Get-SafeLauncherMessage -Message ([string]$_.Exception.Message)
            if ($attempt -lt $AttemptCount) {
                Start-Sleep -Seconds ([Math]::Min(4, $attempt))
            }
        }
    }
    throw "Khong doc duoc trang thai Auto Sync sau $AttemptCount lan thu: $lastFailure"
}

function Write-SessionSyncStage {
    param([Parameter(Mandatory = $false)][string]$Stage)

    $message = switch ($Stage) {
        'REFRESH_OTO' { 'Dang lam moi du lieu O to...' }
        'SYNC_OTO' { 'Dang dong bo du lieu O to...' }
        'REFRESH_MOTO' { 'Dang lam moi du lieu Mo to...' }
        'SYNC_MOTO' { 'Dang dong bo du lieu Mo to...' }
        'LOADING_DATA' { 'Dang tai du lieu moi...' }
        'COMPLETED' { 'Hoan tat.' }
        'FAILED' { 'Dong bo du lieu khong thanh cong.' }
        default { 'Dang ket noi may chu va cap nhat du lieu...' }
    }
    Set-LauncherProgress -Message $message -Completed:($Stage -eq 'COMPLETED')
}

function Wait-ForSessionSync {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastStage = $null
    $lastStatusFailure = $null
    do {
        $record = Get-ProcessRecord -ProcessId $ProcessId
        if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
            throw 'QLHV runtime da dung trong khi dang cap nhat du lieu.'
        }

        try {
            $status = Get-SessionStartStatus -RunId $RunId
            $lastStatusFailure = $null
        }
        catch {
            $lastStatusFailure = Get-SafeLauncherMessage -Message ([string]$_.Exception.Message)
            Set-LauncherProgress -Message 'Tam thoi chua doc duoc trang thai; dang thu lai...'
            Start-Sleep -Seconds 2
            continue
        }

        $foundProperty = $status.PSObject.Properties['found']
        $terminalProperty = $status.PSObject.Properties['isTerminal']
        $succeededProperty = $status.PSObject.Properties['succeeded']
        if ($null -eq $foundProperty -or
            $null -eq $terminalProperty -or
            $null -eq $succeededProperty) {
            throw 'Phan hoi trang thai Auto Sync khong hop le.'
        }
        if (-not [bool]$foundProperty.Value) {
            throw "Khong tim thay phien Auto Sync $RunId."
        }

        $stageProperty = $status.PSObject.Properties['currentStage']
        $stage = if ($null -eq $stageProperty) { $null } else { [string]$stageProperty.Value }
        if (-not [string]::Equals($stage, $lastStage, [StringComparison]::Ordinal)) {
            Write-SessionSyncStage -Stage $stage
            $lastStage = $stage
        }

        if ([bool]$terminalProperty.Value) {
            if ([bool]$succeededProperty.Value) {
                if (-not [string]::Equals($stage, 'COMPLETED', [StringComparison]::Ordinal)) {
                    Write-SessionSyncStage -Stage 'COMPLETED'
                }
                return $status
            }

            $errorProperty = $status.PSObject.Properties['errorMessage']
            $safeError = if ($null -eq $errorProperty -or
                [string]::IsNullOrWhiteSpace([string]$errorProperty.Value)) {
                'Auto Sync ket thuc nhung khong thanh cong.'
            }
            else {
                Get-SafeLauncherMessage -Message ([string]$errorProperty.Value)
            }
            throw $safeError
        }

        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    if (-not [string]::IsNullOrWhiteSpace($lastStatusFailure)) {
        throw "Auto Sync chua hoan tat sau $TimeoutSeconds giay; loi doc trang thai gan nhat: $lastStatusFailure"
    }
    throw "Auto Sync chua hoan tat sau $TimeoutSeconds giay."
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

# Main launcher lifecycle: settle one server, coordinate one data session, then open the browser.
try {
    Initialize-LauncherProgress
    Set-LauncherProgress -Message 'Dang ket noi may chu...'
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

        # A daily Desktop launch must never stop or replace an existing server.
        # If another exact QLHV process exists without owning the listener, report
        # the ambiguous state and let an operator inspect it separately.
        $orphanedRuntimeIds = @(Get-QlhvRuntimeProcessIds | Where-Object { $qlhvOwners -notcontains [int]$_ })
        if ($orphanedRuntimeIds.Count -gt 0) {
            throw "Another QLHV runtime process already exists (PID: $($orphanedRuntimeIds -join ', ')). No process was stopped or started."
        }

        $existingId = $qlhvOwners[0]
        $script:StartedProcessId = $existingId
        Set-Content -LiteralPath $PidFile -Value $existingId -Encoding Ascii
        if ($script:UseLegacyRuntime) {
            Wait-ForLegacyRollbackHealth -ProcessId $existingId -TimeoutSeconds $HealthTimeoutSeconds
        }
        else {
            $liveTimeout = [Math]::Min(30, $HealthTimeoutSeconds)
            Wait-ForEndpoint -ProcessId $existingId -Url $LiveUrl -DisplayName 'liveness' -TimeoutSeconds $liveTimeout
            Wait-ForEndpoint -ProcessId $existingId -Url $ReadyUrl -DisplayName 'readiness' -TimeoutSeconds $HealthTimeoutSeconds
        }
    }
    else {
        $runtimeIds = @(Get-QlhvRuntimeProcessIds)
        if ($runtimeIds.Count -gt 1) {
            throw "Multiple QLHV runtime processes already exist (PID: $($runtimeIds -join ', ')). No process was stopped or started."
        }
        if ($runtimeIds.Count -eq 1) {
            # The existing process may still be binding the port. Join it and wait;
            # never start a second process and never use restart as a readiness fix.
            $script:StartedProcessId = [int]$runtimeIds[0]
            Set-Content -LiteralPath $PidFile -Value $script:StartedProcessId -Encoding Ascii
            if ($script:UseLegacyRuntime) {
                Wait-ForLegacyRollbackHealth -ProcessId $script:StartedProcessId -TimeoutSeconds $HealthTimeoutSeconds
            }
            else {
                $liveTimeout = [Math]::Min(30, $HealthTimeoutSeconds)
                Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $LiveUrl -DisplayName 'liveness' -TimeoutSeconds $liveTimeout
                Wait-ForEndpoint -ProcessId $script:StartedProcessId -Url $ReadyUrl -DisplayName 'readiness' -TimeoutSeconds $HealthTimeoutSeconds
            }
        }
        elseif (Test-Path -LiteralPath $PidFile -PathType Leaf) {
            # A stale PID file is metadata only; removing it does not stop a process.
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

    # Server identity/startup is now settled. Release launcher-only locks before
    # waiting for Auto Sync so another icon invocation can join the same run.
    Exit-LauncherCoordinationLocks

    $browserUrl = $ApplicationUrl
    $sessionStartError = $false
    $sessionStartFailureMessage = $null
    $sessionRunId = $null
    if (-not $DisableAutoSync -and -not $script:UseLegacyRuntime) {
        try {
            Write-SessionSyncStage -Stage 'CONNECTING'
            $sessionStatus = $null
            $operationActive = $false
            $activeRunId = $null
            $initialStatusFailure = $null
            $initialStatusDeadline = [DateTime]::UtcNow.AddSeconds($SyncTimeoutSeconds)
            do {
                $record = Get-ProcessRecord -ProcessId $script:StartedProcessId
                if (-not (Test-IsQlhvRuntimeProcess -ProcessRecord $record)) {
                    throw 'QLHV runtime da dung trong khi dang cho phien cap nhat hien tai.'
                }

                try {
                    $sessionStatus = Get-SessionStartStatus
                    $initialStatusFailure = $null
                }
                catch {
                    $sessionStatus = $null
                    $initialStatusFailure = Get-SafeLauncherMessage -Message ([string]$_.Exception.Message)
                    Set-LauncherProgress -Message 'Tam thoi chua doc duoc trang thai; dang thu lai...'
                    Start-Sleep -Seconds 2
                    continue
                }

                $operationActiveProperty = $sessionStatus.PSObject.Properties['operationActive']
                $serverReadyProperty = $sessionStatus.PSObject.Properties['serverReady']
                $needSyncProperty = $sessionStatus.PSObject.Properties['needSync']
                $canStartProperty = $sessionStatus.PSObject.Properties['canStart']
                if ($null -eq $operationActiveProperty -or
                    $null -eq $serverReadyProperty -or
                    $null -eq $needSyncProperty -or
                    $null -eq $canStartProperty) {
                    throw 'Phan hoi kiem tra NeedSync thieu field bat buoc.'
                }

                $activeRunIdProperty = $sessionStatus.PSObject.Properties['activeRunId']
                $legacyRunIdProperty = $sessionStatus.PSObject.Properties['runId']
                $operationActive = [bool]$operationActiveProperty.Value
                $activeRunId = if ($null -ne $activeRunIdProperty) {
                    [string]$activeRunIdProperty.Value
                }
                else {
                    $null
                }
                if ([string]::IsNullOrWhiteSpace($activeRunId) -and
                    $operationActive -and
                    $null -ne $legacyRunIdProperty) {
                    $activeRunId = [string]$legacyRunIdProperty.Value
                }
                if ($operationActive -and [string]::IsNullOrWhiteSpace($activeRunId)) {
                    Set-LauncherProgress -Message 'Dang cho phien cap nhat hien tai san sang...'
                    Start-Sleep -Seconds 2
                    continue
                }
                break
            } while ([DateTime]::UtcNow -lt $initialStatusDeadline)

            if ($null -eq $sessionStatus) {
                throw "Khong doc duoc trang thai Auto Sync trong thoi gian cho: $initialStatusFailure"
            }
            if ($operationActive -and [string]::IsNullOrWhiteSpace($activeRunId)) {
                throw "Operation hien tai chua ket thuc sau $SyncTimeoutSeconds giay."
            }

            if ($operationActive) {
                $sessionRunId = $activeRunId
            }
            elseif ([bool]$needSyncProperty.Value) {
                if (-not [bool]$canStartProperty.Value) {
                    $messageProperty = $sessionStatus.PSObject.Properties['message']
                    $blockedMessage = if ($null -eq $messageProperty -or
                        [string]::IsNullOrWhiteSpace([string]$messageProperty.Value)) {
                        'Cau hinh hien tai dang chan Auto Sync.'
                    }
                    else {
                        [string]$messageProperty.Value
                    }
                    throw $blockedMessage
                }

                $sessionRunId = Invoke-SessionStartSync `
                    -ProcessId $script:StartedProcessId `
                    -TimeoutSeconds $SyncTimeoutSeconds
            }

            if (-not [string]::IsNullOrWhiteSpace($sessionRunId)) {
                [void](Wait-ForSessionSync `
                    -ProcessId $script:StartedProcessId `
                    -RunId $sessionRunId `
                    -TimeoutSeconds $SyncTimeoutSeconds)
                $browserUrl = "$ApplicationUrl/qlhv-import?sessionStartState=succeeded&sessionStartRunId=$([Uri]::EscapeDataString($sessionRunId))"
            }
            else {
                Set-LauncherProgress -Message 'Du lieu da la ban moi nhat.' -Completed
                $browserUrl = "$ApplicationUrl/qlhv-import?sessionStartState=succeeded"
            }
        }
        catch {
            # The application remains available with its last committed data. The
            # import page will expose runtime/Auto Sync blockers after login.
            $sessionStartError = $true
            $sessionStartFailureMessage = Get-SafeLauncherMessage -Message $_.Exception.Message
            Set-LauncherProgress -Message 'Cap nhat du lieu co loi; dang mo ung dung...'
            $browserUrl = "$ApplicationUrl/qlhv-import?sessionStartState=failed"
            if (-not [string]::IsNullOrWhiteSpace($sessionRunId)) {
                $browserUrl += '&sessionStartRunId=' + [Uri]::EscapeDataString($sessionRunId)
            }
        }
    }

    if (-not $NoBrowser) {
        Start-Process $browserUrl
    }
    Close-LauncherProgress

    Write-Host "QLHV is ready at $ApplicationUrl (PID $script:StartedProcessId)."
    if ($sessionStartError) {
        Write-Warning "QLHV da mo nhung phien cap nhat du lieu khong thanh cong: $sessionStartFailureMessage"
    }
}
catch {
    Close-LauncherProgress
    $message = Get-SafeLauncherMessage -Message $_.Exception.Message
    try {
        New-Item -ItemType Directory -Path $LogDirectory -Force -ErrorAction SilentlyContinue | Out-Null
        $launcherErrorLog = Join-Path $LogDirectory ('launcher-' + (Get-Date -Format 'yyyyMMdd') + '.error.log')
        Add-Content -LiteralPath $launcherErrorLog -Value "$(Get-Date -Format o) $message"
    }
    catch {
        # Keep the original startup error.
    }

    # Daily launch failures never stop an existing or newly started server.
    # Deployment/rollback scripts retain their separate, explicit PID lifecycle.
    if (-not $NoBrowser -and $null -ne $script:StartedProcessId) {
        $liveAfterFailure = Invoke-HealthProbe -Url $LiveUrl -TimeoutSeconds 4
        if ($liveAfterFailure.Success) {
            Start-Process "$ApplicationUrl/qlhv-import?sessionStartState=failed"
        }
    }
    Show-LauncherError -Message $message
    throw $message
}
finally {
    Close-LauncherProgress
    Exit-LauncherCoordinationLocks
    $script:LauncherMutex.Dispose()
}
