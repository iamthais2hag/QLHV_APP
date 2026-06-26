# Task 5 B3S - Local dry-run checklist for V2 HocVien sync

This checklist prepares a later local dry-run from `CSDT_V2_TEST` to `QLHV_APP_TEST`.
Do not use it with production databases. Do not call the execute endpoint during dry-run preparation.

## 1. Required test databases

Use local/test databases only:

- `QLHV_APP_TEST`: target database, created from the current QLHV_APP schema.
- `CSDT_V2_TEST`: source database, restored from a safe non-production backup.

Before starting the API, manually confirm the configured connection strings point only to these databases or to
equivalent disposable test databases. Never paste real connection strings into this repository.

## 2. Target schema readiness

Confirm `QLHV_APP_TEST` has the required sync target objects before dry-run review:

- `dbo.App_HocVien` exists.
- `dbo.App_HocVien.MaHangDT` exists.
- `dbo.App_HocVien.V2RowHash` exists.
- `dbo.App_DongBoLog` exists.

Reference SQL for later manual checking against `QLHV_APP_TEST` only:

```sql
SELECT
    CASE WHEN OBJECT_ID(N'dbo.App_HocVien', N'U') IS NULL THEN 0 ELSE 1 END AS AppHocVienExists,
    CASE WHEN COL_LENGTH(N'dbo.App_HocVien', N'MaHangDT') IS NULL THEN 0 ELSE 1 END AS AppHocVienMaHangDTExists,
    CASE WHEN COL_LENGTH(N'dbo.App_HocVien', N'V2RowHash') IS NULL THEN 0 ELSE 1 END AS AppHocVienV2RowHashExists,
    CASE WHEN OBJECT_ID(N'dbo.App_DongBoLog', N'U') IS NULL THEN 0 ELSE 1 END AS AppDongBoLogExists;
```

This SQL is documentation only in B3S. Do not run it in this task.

## 3. Source mapping readiness

Use [`sync-v2-mapping-readiness.md`](./sync-v2-mapping-readiness.md) before dry-run to check:

- `GioiTinh` raw values.
- `SoCMT` shape: 12-digit CCCD, 9-digit CMND, blank, and other.
- `TrangThai` value distribution.
- invalid `NgaySinh` count.
- unmatched `DM_DVHC` joins.
- unmatched `DM_HangDT` joins.
- duplicate effective rows by `MaDK` after current joins.

Run those reference queries only against `CSDT_V2_TEST` or another approved disposable test database.

## 4. Local configuration

Use `dotnet user-secrets` or environment variables. The examples below are placeholders only.

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

Equivalent environment variable names:

```text
ConnectionStrings__QLHV_APP
ConnectionStrings__CSDT_V2
SyncExecution__EnableTargetWrites
SyncExecution__RequireManualConfirmation
SyncExecution__AllowHangfireSchedule
SyncExecution__ConfirmationPhrase
```

Required safety values:

- `SyncExecution:EnableTargetWrites=false`
- `SyncExecution:RequireManualConfirmation=true`
- `SyncExecution:AllowHangfireSchedule=false`

Do not set `EnableTargetWrites=true` for dry-run.

## 5. Config-check before dry-run

Start the API locally, then call:

```http
GET /api/dong-bo-v2/hoc-vien/config-check
```

PowerShell example:

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5000/api/dong-bo-v2/hoc-vien/config-check"
```

Expected safe response:

```json
{
  "qlhvAppConfigured": true,
  "csdtV2Configured": true,
  "enableTargetWrites": false,
  "requireManualConfirmation": true,
  "allowHangfireSchedule": false
}
```

The response confirms the API sees non-placeholder configuration. It does not prove the databases are non-production;
the operator must verify database names separately.

## 6. Dry-run endpoint for the next task

Do not run dry-run in B3S. In the next approved task, after config-check passes and database names are confirmed,
call:

```http
POST /api/dong-bo-v2/hoc-vien/dry-run
```

PowerShell example:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/dong-bo-v2/hoc-vien/dry-run" |
    ConvertTo-Json -Depth 10
```

Expected behavior:

- Opens a read-only connection to configured `CSDT_V2_TEST`.
- Runs source `COUNT`.
- Returns safe summary and mapping information.
- Does not write `App_HocVien`.
- Does not write `App_DongBoLog`.
- Does not schedule Hangfire.
- Does not return connection strings, usernames, passwords, tokens, or server/database details.

## 7. Capture after dry-run

Record these values from the dry-run output and readiness checks:

- `canRun`
- `status`
- `sourceRecordCount`
- `plannedSummary.totalRead`
- `plannedSummary.totalError`
- any `issues`
- any `errors.code` / `errors.message`
- mapping rows for `MaHangDT`, `HangGPLXHoc`, `DiaChiThuongTru`, `GioiTinh`, and `SoCCCD`
- warning counts or data-quality findings, if present
- unmatched `DM_DVHC` count from the mapping-readiness SQL
- unmatched `DM_HangDT` count from the mapping-readiness SQL
- duplicate `MaDK` count from the mapping-readiness SQL

If dry-run reports configuration or source read failure, stop and fix local/test configuration first. Do not switch to
execute as a workaround.

## 8. Read-only source diagnostics before execute

After config-check and dry-run pass on `CSDT_V2_TEST`, run the read-only diagnostics endpoint before any future execute
test:

```http
GET /api/dong-bo-v2/hoc-vien/source-diagnostics
```

PowerShell example:

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5000/api/dong-bo-v2/hoc-vien/source-diagnostics" |
    ConvertTo-Json -Depth 10
```

The endpoint only reads `CSDT_V2`; it does not write `QLHV_APP`, does not write `App_DongBoLog`, and does not return
connection strings, server names, database names, usernames, passwords, tokens, CCCD raw values, or GPLX raw values.

Capture these aggregate counts:

- duplicate `MaDK` groups and duplicate rows.
- missing `MaDK`.
- missing `HoTen`.
- `GioiTinh` distribution.
- `SoCMT` length buckets: 9 digits, 12 digits, other, null/empty.
- missing `NgaySinh` and `NgaySinh` parse issues.
- missing `HangDaoTao` and `HangDaoTao` values that do not join `DM_HangDT`.
- missing `NoiTT` administrative codes and `NoiTT` rows that do not join `DM_DVHC`.
- missing `MaKhoaHoc` and `MaKhoaHoc` values that do not join `KhoaHoc`.

Treat any unexpected non-zero count as a mapping/data-quality question to review before a guarded local execute test.

Example safe response shape:

```json
{
  "isReadOnly": true,
  "canRead": true,
  "status": "SanSang",
  "issues": [],
  "errors": [],
  "diagnostics": {
    "sourceRows": 1970,
    "duplicateMaDkCount": 0,
    "duplicateMaDkRowCount": 0,
    "missingMaDkCount": 0,
    "missingHoTenCount": 0,
    "gioiTinhDistribution": [
      { "value": "M", "total": 1000 },
      { "value": "F", "total": 970 }
    ],
    "soCmtLength": {
      "nineDigits": 0,
      "twelveDigits": 1970,
      "other": 0,
      "nullOrEmpty": 0
    },
    "missingNgaySinhCount": 0,
    "ngaySinhParseIssueCount": 0,
    "missingHangDaoTaoCount": 0,
    "hangDaoTaoUnmatchedDmHangDtCount": 0,
    "missingNoiTTCodesCount": 0,
    "noiTTUnmatchedDmDvhcCount": 0,
    "missingMaKhoaHocCount": 0,
    "maKhoaHocUnmatchedKhoaHocCount": 0
  }
}
```

## 9. Absolute no-go list

- Do not call `POST /api/dong-bo-v2/hoc-vien/execute`.
- Do not set `SyncExecution:EnableTargetWrites=true`.
- Do not use production databases.
- Do not run SQL scripts automatically.
- Do not paste real connection strings, usernames, passwords, tokens, or secrets into docs/source/logs/screenshots.
- Do not schedule Hangfire.
