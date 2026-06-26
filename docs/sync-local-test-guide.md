# Sync V2 HocVien local test guide

This guide is only for a controlled local/test run of the guarded HocVien sync path.
Task 5 has not been approved to run against production. Never use production databases, and do not commit real
connection strings, usernames, passwords, tokens, or secrets.

## Required test databases

Use disposable or restored backup test databases only:

- `QLHV_APP_TEST`: target database with the QLHV_APP schema.
- `CSDT_V2_TEST`: source database restored from a safe non-production backup.
- Optional `HANGFIRE_TEST`: reserved for Phase B4 or later. No Hangfire schedule exists yet.

Normal build and unit tests do not require SQL Server.

## Local dev domains

Recommended local URLs:

- Frontend: `http://qlhv.local:5173`
- Backend API: `http://api.qlhv.local:5000`

Set them up with [`local-dev-hosts-guide.md`](./local-dev-hosts-guide.md). The hosts file entries are:

```text
127.0.0.1 qlhv.local
127.0.0.1 api.qlhv.local
```

Before any dry-run, verify both connections point to disposable local/test databases and keep:

```text
SyncExecution:EnableTargetWrites=false
```

Do not call the execute endpoint as part of the normal dry-run workflow.

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
Do not paste real values into this file or any committed config file.

From `server/QLHV.Api`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:QLHV_APP" "<local/test-only connection string for QLHV_APP_TEST>"
dotnet user-secrets set "ConnectionStrings:CSDT_V2" "<local/test-only connection string for CSDT_V2_TEST>"
dotnet user-secrets set "SyncExecution:EnableTargetWrites" "false"
dotnet user-secrets set "SyncExecution:RequireManualConfirmation" "true"
dotnet user-secrets set "SyncExecution:AllowHangfireSchedule" "false"
dotnet user-secrets set "SyncExecution:ConfirmationPhrase" "EXECUTE_DONG_BO_V2_HOC_VIEN"
```

The `ConnectionStrings:QLHV_APP` value must point to `QLHV_APP_TEST`.
The `ConnectionStrings:CSDT_V2` value must point to `CSDT_V2_TEST`.
Keep both values in user-secrets or environment variables only.

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

## Verify safe config before dry-run

Start the API locally, then call config-check:

```http
GET /api/dong-bo-v2/hoc-vien/config-check
```

PowerShell:

```powershell
Invoke-RestMethod -Method Get -Uri "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/config-check"
```

curl:

```powershell
curl.exe -X GET "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/config-check"
```

Expected safe response shape:

```json
{
  "qlhvAppConfigured": true,
  "csdtV2Configured": true,
  "enableTargetWrites": false,
  "requireManualConfirmation": true,
  "allowHangfireSchedule": false
}
```

`qlhvAppConfigured` and `csdtV2Configured` mean the backend sees non-placeholder local/test configuration.
They do not prove that the configured database is non-production; the operator must verify that separately.
The response must never contain server name, database name, username, password, or a connection string.
Do not run dry-run until both configured flags are `true` and `enableTargetWrites` is still `false`.

## Run dry-run first

Start the API locally, then call:

```http
POST /api/dong-bo-v2/hoc-vien/dry-run
```

PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/dry-run"
```

curl:

```powershell
curl.exe -X POST "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/dry-run"
```

Expected safe behavior:

- Opens a read-only connection to the configured `CSDT_V2` database and runs `SELECT COUNT`.
- Must therefore be used only with `CSDT_V2_TEST` or another approved disposable local/test source.
- No target writes.
- No `App_DongBoLog` write.
- No Hangfire job scheduling.
- Response contains summary/configuration information only.
- No connection strings or passwords are returned.

## Manual execute body for controlled local testing only

Execute is a real write path. It is not approved or authorized for production. The application currently has no
authentication/role pipeline protecting this endpoint, so the enable switch and confirmation phrase must not be
treated as user authorization.

Do not call execute during a dry-run review. This reference is retained only for a separately approved test against
disposable local databases. Only after that approval and review of the dry-run may writes be intentionally enabled
in protected local configuration:

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
This confirmation protects against accidental invocation; it does not make the endpoint suitable for production.

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

## Mapping questions requiring confirmation

Use [`sync-v2-mapping-readiness.md`](./sync-v2-mapping-readiness.md) as the checklist before the first local dry-run.
The SQL snippets in that document are reference only and must be run manually only against approved test databases.

Before any broader test or production design review, confirm:

- the actual `NguoiLX.GioiTinh` values and their display mapping;
- the ratio of 12-digit CCCD values to legacy 9-digit CMND values in `NguoiLX.SoCMT`;
- the business meaning of `TrangThai` in `NguoiLX`, `NguoiLX_HoSo`, and `KhoaHoc`;
- how invalid/unparseable `NgaySinh` values should be reported;
- join coverage for `DM_DVHC` addresses and `DM_HangDT` training categories;
- whether the current joins can return more than one row for the same `MaDK` in local/test data.

## SQL integration tests

SQL integration tests use an opt-in test attribute and are skipped by default. They may only run when all of the following are true:

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
