[CmdletBinding()]
param(
    [string]$ExpectedServerName = 'CSDLTTTC\QLHVRT02',
    [string]$ServiceName = 'MSSQL$QLHVRT02',
    [string]$WindowsLogin = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole(
    [System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'RT03_V7_DISPOSABLE_SQL_ADMIN_REQUIRED'
}

if ($ServiceName -ne 'MSSQL$QLHVRT02' -or
    $ExpectedServerName -ne 'CSDLTTTC\QLHVRT02') {
    throw 'RT03_V7_DISPOSABLE_SQL_IDENTITY_REJECTED'
}

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
if ($null -eq $service -or
    $service.PathName -notmatch '\\MSSQL16\.QLHVRT02\\') {
    throw 'RT03_V7_DISPOSABLE_SQL_SERVICE_PATH_REJECTED'
}

$escapedLogin = $WindowsLogin.Replace(']', ']]')
$escapedServer = $ExpectedServerName.Replace("'", "''")
$sql = @"
SET NOCOUNT ON;
IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'$escapedServer'
    THROW 528000, 'RT03_V7_DISPOSABLE_SQL_SERVER_REJECTED', 1;
DECLARE @EffectiveLogin sysname = SUSER_SNAME();
IF @EffectiveLogin IS NULL
    THROW 528002, 'RT03_V7_DISPOSABLE_SQL_LOGIN_RESOLUTION_FAILED', 1;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.server_principals
    WHERE name = @EffectiveLogin
)
BEGIN
    DECLARE @CreateSql nvarchar(max) =
        N'CREATE LOGIN ' + QUOTENAME(@EffectiveLogin) + N' FROM WINDOWS;';
    EXEC sys.sp_executesql @CreateSql;
END;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.server_role_members memberRow
    INNER JOIN sys.server_principals roleRow
      ON roleRow.principal_id = memberRow.role_principal_id
    INNER JOIN sys.server_principals memberPrincipal
      ON memberPrincipal.principal_id = memberRow.member_principal_id
    WHERE roleRow.name = N'sysadmin'
      AND memberPrincipal.name = @EffectiveLogin
)
BEGIN
    DECLARE @GrantSql nvarchar(max) =
        N'ALTER SERVER ROLE [sysadmin] ADD MEMBER ' +
        QUOTENAME(@EffectiveLogin) + N';';
    EXEC sys.sp_executesql @GrantSql;
END;
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerName,
    @EffectiveLogin AS EffectiveLogin,
    CONVERT(bit, CASE WHEN EXISTS
    (
        SELECT 1
        FROM sys.server_role_members memberRow
        INNER JOIN sys.server_principals roleRow
          ON roleRow.principal_id = memberRow.role_principal_id
        INNER JOIN sys.server_principals memberPrincipal
          ON memberPrincipal.principal_id = memberRow.member_principal_id
        WHERE roleRow.name = N'sysadmin'
          AND memberPrincipal.name = @EffectiveLogin
    ) THEN 1 ELSE 0 END) AS DisposableSysadmin;
"@

$singleUserStarted = $false
try {
    if ($service.State -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(30))
    }

    $startOutput = & sc.exe start $ServiceName '/m"SQLCMD"' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "RT03_V7_DISPOSABLE_SINGLE_USER_START_FAILED: $startOutput"
    }
    $singleUserStarted = $true
    (Get-Service -Name $ServiceName).WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(30))

    $result = & sqlcmd.exe `
        -S $ExpectedServerName `
        -E `
        -b `
        -r 1 `
        -Q $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "RT03_V7_DISPOSABLE_LOGIN_BOOTSTRAP_FAILED: $result"
    }
    $result
}
finally {
    if ($singleUserStarted) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        (Get-Service -Name $ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(30))
        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(30))
    }
}

$verification = & sqlcmd.exe `
    -S $ExpectedServerName `
    -E `
    -b `
    -r 1 `
    -Q @"
SET NOCOUNT ON;
IF CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) <> N'$escapedServer'
    THROW 528001, 'RT03_V7_DISPOSABLE_SQL_POSTSTART_REJECTED', 1;
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY(N'ServerName')) AS ServerName,
    IS_SRVROLEMEMBER(N'sysadmin', N'$($WindowsLogin.Replace("'", "''"))')
        AS DisposableSysadmin;
"@ 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "RT03_V7_DISPOSABLE_SQL_POSTSTART_VERIFY_FAILED: $verification"
}
$verification
