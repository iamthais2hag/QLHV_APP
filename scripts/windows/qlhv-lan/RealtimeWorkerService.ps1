Set-StrictMode -Version Latest

$script:QlhvRealtimeWorkerServiceName = 'QLHV_APP_RealtimeWorker'
$script:QlhvRealtimeWorkerDisplayName = 'QLHV_APP Realtime CSDT Worker'
$script:QlhvRealtimeWorkerDescription =
    'Synchronizes the fixed OTO and MOTO CSDT production profiles to QLHV_APP.'
$script:QlhvRealtimeWorkerServiceAccount =
    'NT SERVICE\QLHV_APP_RealtimeWorker'
$script:QlhvRealtimeWorkerEnvironment = @(
    'DOTNET_ENVIRONMENT=Production',
    'QlhvAutoSync__Enabled=false',
    'QlhvAutoSync__RunOnServerStartup=false',
    'CsdtRealtimeSync__Enabled=false',
    'Rt03Production__EnableRt03ProductionRealtime=true',
    'Rt03Production__EnableRt03ProductionShadow=true',
    'Rt03Production__EnableRt03ProductionWrites=true',
    'Rt03Production__EnableRt03ProductionCanary=false',
    'Rt03Production__EnableRt03ControlledCutover=true',
    'Rt03Production__EnableRt03ProductionDeletes=false',
    'Rt03Production__ValidationOnly=false',
    'Rt03Production__EnableOto=true',
    'Rt03Production__EnableMoto=true',
    'Rt03Production__PollIntervalSeconds=2',
    'Logging__EventLog__SourceName=QLHV_APP_RealtimeWorker',
    'Logging__EventLog__LogName=Application'
)

function Get-QlhvRealtimeWorkerExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$RuntimeRoot
    )

    $normalizedRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\')
    if (-not [string]::Equals(
        $normalizedRoot,
        'D:\QLHV_APP_RUNTIME',
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to manage the realtime worker outside D:\QLHV_APP_RUNTIME.'
    }

    return [IO.Path]::GetFullPath(
        (Join-Path `
            -Path $normalizedRoot `
            -ChildPath 'app\worker\QLHV.Worker.exe'))
}

function Get-QlhvRealtimeWorkerServiceRecord {
    return Get-CimInstance `
        -ClassName Win32_Service `
        -Filter "Name = '$script:QlhvRealtimeWorkerServiceName'" `
        -ErrorAction SilentlyContinue
}

function Get-QlhvRealtimeWorkerRegistryPath {
    return 'HKLM:\SYSTEM\CurrentControlSet\Services\' +
        $script:QlhvRealtimeWorkerServiceName
}

function Assert-QlhvRealtimeWorkerAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Realtime worker SCM configuration requires an elevated Administrator token.'
    }
}

function Assert-QlhvRealtimeWorkerEnvironment {
    $registryPath = Get-QlhvRealtimeWorkerRegistryPath
    $property = Get-ItemProperty `
        -LiteralPath $registryPath `
        -Name Environment `
        -ErrorAction Stop
    $actual = @($property.Environment)
    if ($actual.Count -ne $script:QlhvRealtimeWorkerEnvironment.Count) {
        throw 'Service REG_MULTI_SZ environment does not have the exact approved entry count.'
    }
    for ($index = 0; $index -lt $actual.Count; $index++) {
        if (-not [string]::Equals(
            [string]$actual[$index],
            [string]$script:QlhvRealtimeWorkerEnvironment[$index],
            [StringComparison]::Ordinal)) {
            throw 'Service REG_MULTI_SZ environment does not match the exact production contract.'
        }
    }
}

function Assert-QlhvRealtimeWorkerServiceIdentity {
    param(
        [Parameter(Mandatory = $true)]$ServiceRecord,
        [Parameter(Mandatory = $true)][string]$RuntimeRoot
    )

    $expectedExecutable = Get-QlhvRealtimeWorkerExecutable -RuntimeRoot $RuntimeRoot
    $expectedPathName = '"' + $expectedExecutable + '"'
    $actualPathName = ([string]$ServiceRecord.PathName).Trim()
    if (-not [string]::Equals(
            $actualPathName,
            $expectedPathName,
            [StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals(
            $actualPathName,
            $expectedExecutable,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Service $script:QlhvRealtimeWorkerServiceName does not use the exact approved worker path. No service action was taken."
    }
    if (-not [string]::Equals(
        ([string]$ServiceRecord.StartName).Trim(),
        $script:QlhvRealtimeWorkerServiceAccount,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Service $script:QlhvRealtimeWorkerServiceName does not use the approved virtual service account. No service action was taken."
    }
    Assert-QlhvRealtimeWorkerEnvironment
}

function Get-QlhvRealtimeWorkerServiceSnapshot {
    param([Parameter(Mandatory = $true)][string]$RuntimeRoot)

    $record = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $record) {
        return [pscustomobject]@{
            Exists = $false
            WasRunning = $false
            ProcessId = 0
        }
    }

    Assert-QlhvRealtimeWorkerServiceIdentity `
        -ServiceRecord $record `
        -RuntimeRoot $RuntimeRoot
    return [pscustomobject]@{
        Exists = $true
        WasRunning = [string]::Equals(
            [string]$record.State,
            'Running',
            [StringComparison]::OrdinalIgnoreCase)
        ProcessId = [int]$record.ProcessId
    }
}

function Invoke-QlhvSc {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "SCM command failed: sc.exe $($Arguments[0]) $($Arguments[1])."
    }
    return $output
}

function Install-QlhvRealtimeWorkerService {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$RuntimeRoot
    )

    Assert-QlhvRealtimeWorkerAdministrator
    $workerExecutable = Get-QlhvRealtimeWorkerExecutable -RuntimeRoot $RuntimeRoot
    if (-not (Test-Path -LiteralPath $workerExecutable -PathType Leaf)) {
        throw "Published realtime worker was not found: $workerExecutable"
    }
    $productionConfig = Join-Path `
        -Path $RuntimeRoot `
        -ChildPath 'config\appsettings.Production.Local.json'
    if (-not (Test-Path -LiteralPath $productionConfig -PathType Leaf)) {
        throw 'Production Local configuration is required before service registration.'
    }

    $record = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $record) {
        # sc.exe create is required so the built-in virtual account can be used
        # without storing or transmitting a password.
        Invoke-QlhvSc @(
            'create', $script:QlhvRealtimeWorkerServiceName,
            'binPath=', ('"' + $workerExecutable + '"'),
            'start=', 'delayed-auto',
            'obj=', $script:QlhvRealtimeWorkerServiceAccount,
            'DisplayName=', $script:QlhvRealtimeWorkerDisplayName) | Out-Null
    }
    else {
        Assert-QlhvRealtimeWorkerServiceIdentity `
            -ServiceRecord $record `
            -RuntimeRoot $RuntimeRoot
    }

    $registryPath = Get-QlhvRealtimeWorkerRegistryPath
    New-ItemProperty `
        -LiteralPath $registryPath `
        -Name Environment `
        -PropertyType MultiString `
        -Value $script:QlhvRealtimeWorkerEnvironment `
        -Force | Out-Null

    Invoke-QlhvSc @(
        'config', $script:QlhvRealtimeWorkerServiceName,
        'binPath=', ('"' + $workerExecutable + '"'),
        'start=', 'delayed-auto',
        'obj=', $script:QlhvRealtimeWorkerServiceAccount,
        'DisplayName=', $script:QlhvRealtimeWorkerDisplayName) | Out-Null
    Invoke-QlhvSc @(
        'sidtype', $script:QlhvRealtimeWorkerServiceName, 'unrestricted') | Out-Null
    Invoke-QlhvSc @(
        'failure', $script:QlhvRealtimeWorkerServiceName,
        'reset=', '86400',
        'actions=', 'restart/5000/restart/15000/restart/60000') | Out-Null
    Invoke-QlhvSc @(
        'failureflag', $script:QlhvRealtimeWorkerServiceName, '1') | Out-Null
    Invoke-QlhvSc @(
        'description', $script:QlhvRealtimeWorkerServiceName,
        $script:QlhvRealtimeWorkerDescription) | Out-Null

    # Explicit read-only access to binaries/config. Database rights are provisioned
    # separately and do not grant filesystem write access.
    & icacls.exe (Join-Path -Path $RuntimeRoot -ChildPath 'app\worker') `
        /grant ($script:QlhvRealtimeWorkerServiceAccount + ':(OI)(CI)RX') `
        /t /c | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not grant the service SID read access to the worker directory.'
    }
    & icacls.exe (Join-Path -Path $RuntimeRoot -ChildPath 'config') `
        /grant ($script:QlhvRealtimeWorkerServiceAccount + ':(OI)(CI)RX') `
        /t /c | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not grant the service SID read access to production configuration.'
    }

    # The production file intentionally has inheritance disabled. Grant its
    # service SID an explicit read-only ACE without changing file contents.
    & icacls.exe $productionConfig `
        /grant ($script:QlhvRealtimeWorkerServiceAccount + ':(R)') `
        /c | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not grant the service SID explicit read access to the protected production configuration file.'
    }

    if (-not [Diagnostics.EventLog]::SourceExists($script:QlhvRealtimeWorkerServiceName)) {
        [Diagnostics.EventLog]::CreateEventSource(
            $script:QlhvRealtimeWorkerServiceName,
            'Application')
    }

    $verified = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $verified) {
        throw "Service $script:QlhvRealtimeWorkerServiceName was not created."
    }
    Assert-QlhvRealtimeWorkerServiceIdentity `
        -ServiceRecord $verified `
        -RuntimeRoot $RuntimeRoot
    if (-not [string]::Equals(
        [string]$verified.StartMode,
        'Auto',
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Service $script:QlhvRealtimeWorkerServiceName is not configured for automatic startup."
    }
    $delayed = (Get-ItemProperty `
        -LiteralPath $registryPath `
        -Name DelayedAutoStart `
        -ErrorAction Stop).DelayedAutoStart
    if ([int]$delayed -ne 1) {
        throw "Service $script:QlhvRealtimeWorkerServiceName is not delayed automatic."
    }
}

function Stop-QlhvRealtimeWorkerService {
    param([Parameter(Mandatory = $true)][string]$RuntimeRoot)

    $record = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $record) {
        return
    }
    Assert-QlhvRealtimeWorkerServiceIdentity `
        -ServiceRecord $record `
        -RuntimeRoot $RuntimeRoot

    if (-not [string]::Equals(
        [string]$record.State,
        'Stopped',
        [StringComparison]::OrdinalIgnoreCase)) {
        # No -Force: the WindowsServiceLifetime cancellation token must complete
        # graceful state recording and mutex release.
        Stop-Service `
            -Name $script:QlhvRealtimeWorkerServiceName `
            -ErrorAction Stop
        $service = Get-Service -Name $script:QlhvRealtimeWorkerServiceName -ErrorAction Stop
        $service.WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(30))
    }
}

function Start-QlhvRealtimeWorkerService {
    param([Parameter(Mandatory = $true)][string]$RuntimeRoot)

    $record = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $record) {
        throw "Service $script:QlhvRealtimeWorkerServiceName is not installed."
    }
    Assert-QlhvRealtimeWorkerServiceIdentity `
        -ServiceRecord $record `
        -RuntimeRoot $RuntimeRoot

    $workerExecutable = Get-QlhvRealtimeWorkerExecutable -RuntimeRoot $RuntimeRoot
    $outsideProcesses = @(Get-CimInstance Win32_Process | Where-Object {
        [string]::Equals(
            [string]$_.ExecutablePath,
            $workerExecutable,
            [StringComparison]::OrdinalIgnoreCase) -and
        [int]$_.ProcessId -ne [int]$record.ProcessId
    })
    if ($outsideProcesses.Count -ne 0) {
        throw 'A standalone worker process still exists; refusing SCM/standalone overlap.'
    }

    $service = Get-Service -Name $script:QlhvRealtimeWorkerServiceName -ErrorAction Stop
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $script:QlhvRealtimeWorkerServiceName -ErrorAction Stop
        $service = Get-Service -Name $script:QlhvRealtimeWorkerServiceName -ErrorAction Stop
        $service.WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(30))
    }

    Start-Sleep -Seconds 2
    $runningRecord = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $runningRecord -or
        -not [string]::Equals(
            [string]$runningRecord.State,
            'Running',
            [StringComparison]::OrdinalIgnoreCase) -or
        [int]$runningRecord.ProcessId -le 0) {
        throw "Service $script:QlhvRealtimeWorkerServiceName did not remain running after startup."
    }
    $process = Get-CimInstance Win32_Process `
        -Filter "ProcessId = $([int]$runningRecord.ProcessId)" `
        -ErrorAction Stop
    if ($null -eq $process -or -not [string]::Equals(
        [string]$process.ExecutablePath,
        $workerExecutable,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'SCM process identity does not match the approved worker executable.'
    }
}

function Remove-QlhvRealtimeWorkerService {
    param([Parameter(Mandatory = $true)][string]$RuntimeRoot)

    Assert-QlhvRealtimeWorkerAdministrator
    $record = Get-QlhvRealtimeWorkerServiceRecord
    if ($null -eq $record) {
        return
    }
    Assert-QlhvRealtimeWorkerServiceIdentity `
        -ServiceRecord $record `
        -RuntimeRoot $RuntimeRoot
    Stop-QlhvRealtimeWorkerService -RuntimeRoot $RuntimeRoot

    Invoke-QlhvSc @('delete', $script:QlhvRealtimeWorkerServiceName) | Out-Null
}
