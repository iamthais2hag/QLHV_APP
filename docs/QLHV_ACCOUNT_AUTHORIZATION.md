# QLHV account authorization setup

Account setup has two explicit, separate steps. The API never applies the database patch automatically.

## 1. Apply the account-table patch

Review the target server and database, then run:

```powershell
sqlcmd -S CSDLTTTC -E -b -d QLHV_APP -i "D:\QLHV_APP\database\patches\20260722_add_app_user_authorization.sql"
```

The patch is transactional and idempotent. It creates or validates `dbo.App_User`, `dbo.App_Role`, and `dbo.App_UserRole`, then seeds only the `Admin` and `Viewer` role definitions. It does not create a user or contain a password/hash.

## 2. Create the first Admin once

Use a fresh PowerShell session. Supply the password through a process environment variable, never as a command-line argument:

```powershell
$env:QLHV_SEED_ADMIN_USERNAME = Read-Host "Admin username"
$env:QLHV_SEED_ADMIN_DISPLAY_NAME = Read-Host "Display name"
$seedSecret = Read-Host "Admin password (minimum 12 characters)" -AsSecureString
$seedSecretPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seedSecret)

try {
    $env:QLHV_SEED_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($seedSecretPtr)
    dotnet run --project server/QLHV.Api/QLHV.Api.csproj -- --seed-admin
}
finally {
    Remove-Item Env:QLHV_SEED_ADMIN_USERNAME -ErrorAction SilentlyContinue
    Remove-Item Env:QLHV_SEED_ADMIN_DISPLAY_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:QLHV_SEED_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($seedSecretPtr)
    $seedSecret = $null
    $seedSecretPtr = [IntPtr]::Zero
}
```

The command hashes the password with ASP.NET Core `PasswordHasher` (PBKDF2) before the repository writes it. It never prints the password or hash. Existing legacy/Viewer accounts do not block initial Admin creation; an existing non-deleted Admin assignment or username collision makes the command refuse safely.

Do not put an account password in arguments, `appsettings*.json`, source files, documentation, Git, or logs. Remove the three environment variables immediately after the one-time command, as shown above.
