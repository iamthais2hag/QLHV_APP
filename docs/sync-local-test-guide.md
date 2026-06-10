# Sync V2 HocVien local test guide

This guide is for a controlled local test run of the guarded HocVien sync path.
Do not use production databases. Do not commit real connection strings, usernames, passwords, tokens, or secrets.

## Required test databases

Use disposable or restored backup test databases only:

- `QLHV_APP_TEST`: target database with the QLHV_APP schema.
- `CSDT_V2_TEST`: source database restored from a safe non-production backup.
- Optional `HANGFIRE_TEST`: only for later phases. Phase B3C does not schedule Hangfire.

Normal build and unit tests do not require SQL Server.

## Safe defaults

Keep target writes disabled by default:

```json
"SyncExecution": {
  "EnableTargetWrites": false,
  "RequireManualConfirmation": true,
  "AllowHangfireSchedule": false,
  "ConfirmationPhrase": "EXECUTE_DONG_BO_V2_HOC_VIEN"
}
```

`EnableTargetWrites=false` means the manual execute endpoint must reject before reading or writing.

## Configure local secrets

Use user-secrets or environment variables. The examples below use placeholders only.

From `server/QLHV.Api`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:QLHV_APP" "<LOCAL_QLHV_APP_TEST_CONNECTION>"
dotnet user-secrets set "ConnectionStrings:CSDT_V2" "<LOCAL_CSDT_V2_TEST_CONNECTION>"
dotnet user-secrets set "SyncExecution:EnableTargetWrites" "false"
dotnet user-secrets set "SyncExecution:RequireManualConfirmation" "true"
dotnet user-secrets set "SyncExecution:AllowHangfireSchedule" "false"
dotnet user-secrets set "SyncExecution:ConfirmationPhrase" "EXECUTE_DONG_BO_V2_HOC_VIEN"
```

Equivalent environment variable names:

```text
ConnectionStrings__QLHV_APP
ConnectionStrings__CSDT_V2
SyncExecution__EnableTargetWrites
SyncExecution__RequireManualConfirmation
SyncExecution__AllowHangfireSchedule
SyncExecution__ConfirmationPhrase
```

Never paste real values into docs, source, commit messages, logs, screenshots, or API responses.

## Run dry-run first

Start the API locally, then call:

```http
POST /api/dong-bo-v2/hoc-vien/dry-run
```

Expected safe behavior:

- No target writes.
- No `App_DongBoLog` write.
- No Hangfire job scheduling.
- Response contains summary/configuration information only.
- No connection strings or passwords are returned.

## Manual execute body

Only after dry-run is reviewed on local test databases, and only after intentionally enabling writes in protected local config:

```powershell
dotnet user-secrets set "SyncExecution:EnableTargetWrites" "true"
```

Manual execute endpoint:

```http
POST /api/dong-bo-v2/hoc-vien/execute
Content-Type: application/json

{
  "confirm": true,
  "confirmationText": "EXECUTE_DONG_BO_V2_HOC_VIEN"
}
```

Swagger should not run this accidentally because `confirm` defaults to `false` and the confirmation text must match exactly.

After the test, immediately lock writes again:

```powershell
dotnet user-secrets set "SyncExecution:EnableTargetWrites" "false"
```

## Rollback and failure-check checklist

Before execute:

- Confirm database names point to local/test databases only.
- Confirm `AllowHangfireSchedule=false`.
- Confirm no real production connection strings are present in appsettings files.
- Back up or snapshot the disposable target database if you need before/after comparison.
- Run dry-run and review counts.

During execute:

- Use a small test data set first.
- Watch for safe summary counts only.
- Do not log raw CCCD/GPLX values.
- Do not log full connection strings or passwords.

After execute:

- Confirm `dbo.App_HocVien` has inserted/updated rows expected by `MaDK`.
- Confirm unchanged rows are skipped by matching `V2RowHash`.
- Confirm no rows are physically deleted.
- Confirm `dbo.App_DongBoLog` has one safe summary row.
- If an induced failure is tested, confirm the failing batch rolled back.
- Reset `SyncExecution:EnableTargetWrites=false`.

## SQL integration tests

SQL integration tests must be skipped by default. They may only run when all of the following are true:

- `QLHV_RUN_SQL_INTEGRATION_TESTS=true`.
- Connections point to disposable local/test databases.
- No production database is reachable from the test process.
- Test setup is reviewed before running.

Normal command:

```powershell
cd server
dotnet test QLHV.sln
```

This normal command must not require SQL Server.
