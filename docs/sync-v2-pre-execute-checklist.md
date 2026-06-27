# Task 5 B3V2 - Pre-execute safety checklist for V2 HocVien sync

This checklist is for the first guarded execute test on local/test databases only.
It is not approval to run execute on production.

## Current approved test context

- `QLHV_APP` configuration key must point to `QLHV_APP_TEST` or another disposable app test database.
- `CSDT_V2` configuration key must point to `CSDT_V2_TEST` or another disposable V2 test database.
- `config-check` has passed in local/test.
- `dry-run` has passed with `sourceRecordCount = 1970`, `plannedSummary.totalRead = 1970`, and no issues/errors.
- `source-diagnostics` has passed with:
  - `sourceRows = 1970`
  - `duplicateMaDkCount = 0`
  - `missingMaDkCount = 0`
  - `missingHoTenCount = 0`
  - `GioiTinh`: `M = 1250`, `F = 720`
  - `SoCMT`: `1969` rows with 12 digits, `1` row with length 11
  - unmatched `DM_HangDT = 0`
  - unmatched `DM_DVHC = 0`
  - unmatched `KhoaHoc = 0`

The 1 row with `SoCMT` length 11 must be reviewed manually. The sync code must not invent or pad a CCCD value.

## Execute remains blocked by default

Execute is blocked unless all of these are true:

- `Sync:DryRun=false`
- `SyncExecution:EnableTargetWrites=true`
- request confirmation is explicit and exact
- `SyncExecution:RequireManualConfirmation=true` remains enabled

If `Sync:DryRun=true`, execute must be rejected even when `EnableTargetWrites=true` and the confirmation phrase is
correct. Do not enable target writes while `Sync:DryRun=true`.

Confirm the current switches through:

```http
GET /api/dong-bo-v2/hoc-vien/config-check
```

The response includes non-secret flags such as `dryRun`, `enableTargetWrites`,
`requireManualConfirmation`, and `allowHangfireSchedule`.

## Target diagnostics

Before enabling execute for a local test, call the read-only target diagnostics endpoint:

```http
GET /api/dong-bo-v2/hoc-vien/target-diagnostics
```

PowerShell example:

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5000/api/dong-bo-v2/hoc-vien/target-diagnostics" |
    ConvertTo-Json -Depth 10
```

The endpoint only reads `QLHV_APP`. It does not write `App_HocVien`, does not write `App_DongBoLog`, and does not
return connection strings, server names, database names, usernames, passwords, or tokens.

Confirm:

- `appHocVienExists = true`
- `appDongBoLogExists = true`
- required columns all exist:
  - `MaDK`
  - `HoTen`
  - `NgaySinh`
  - `GioiTinh`
  - `SoCCCD`
  - `DiaChiThuongTru`
  - `MaKhoa`
  - `TenKhoa`
  - `MaHangDT`
  - `HangGPLXHoc`
  - `V2RowHash`
- `targetRows` is the expected current target count.
- `soCccdLength` looks reasonable for the current test target state.

## Backup/snapshot requirement

Before the first execute test, create a backup/snapshot of `QLHV_APP_TEST`.
Do not proceed if rollback depends on memory or manual reconstruction.

The current guarded write path processes data by batch:

- each `UpsertBatchAsync` call uses a SQL transaction for its own staging/MERGE batch;
- on a batch error, that batch rolls back;
- the whole job is not currently an all-or-nothing transaction across every batch.

Because rollback is batch-scoped, a test database backup/snapshot is required before executing.

## Required read-only checks before execute

Run these checks in order before any local/test execute:

1. `GET /api/dong-bo-v2/hoc-vien/config-check`
2. `POST /api/dong-bo-v2/hoc-vien/dry-run`
3. `GET /api/dong-bo-v2/hoc-vien/source-diagnostics`
4. `GET /api/dong-bo-v2/hoc-vien/target-diagnostics`
5. `GET /api/dong-bo-v2/hoc-vien/pre-execute-plan`

All five checks are read-only. They must not write `App_HocVien`, must not write `App_DongBoLog`, and must not
return connection strings, server names, database names, usernames, passwords, tokens, CCCD raw values, or GPLX raw
values.

## Pre-execute plan

The pre-execute plan compares current CSDT_V2 source rows with the QLHV_APP target snapshot using `MaDK` and
`V2RowHash`. It is intended to answer: "If execute were allowed later, how many rows would be inserted, updated, or
skipped?"

Endpoint:

```http
GET /api/dong-bo-v2/hoc-vien/pre-execute-plan
```

PowerShell example:

```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:5000/api/dong-bo-v2/hoc-vien/pre-execute-plan" |
    ConvertTo-Json -Depth 10
```

Expected response shape:

```json
{
  "isReadOnly": true,
  "canPlan": true,
  "status": "SanSang",
  "issues": [],
  "errors": [],
  "plan": {
    "sourceRows": 1970,
    "targetRows": 1970,
    "wouldInsert": 0,
    "wouldUpdate": 0,
    "wouldSkip": 1970,
    "targetOnlyRows": 0,
    "warningCount": 1,
    "warnings": [
      {
        "code": "CCCD_LENGTH",
        "field": "SoCCCD",
        "count": 1,
        "message": "So CCCD khong du 12 chu so (giu nguyen gia tri goc)."
      }
    ]
  }
}
```

Classification rules:

- `wouldInsert`: source `MaDK` is not in target, or target row exists but is currently `IsDeleted = 1`.
- `wouldUpdate`: source `MaDK` exists in active target and `V2RowHash` differs.
- `wouldSkip`: source `MaDK` exists in active target and `V2RowHash` matches, or the source row is skipped by mapper
  validation.
- `targetOnlyRows`: active target rows whose `MaDK` no longer exists in source. This is report-only. The sync does
  not physically delete rows.

Warnings are aggregate only. They can include:

- source mapping warnings such as the known 1 row with non-12-digit `SoCMT`;
- row-count mismatch between source and active target;
- unusually large `wouldUpdate` volume.

Do not execute if the plan looks surprising. Review mapping, source diagnostics, and target diagnostics first.

## Absolute no-go list

- Do not run execute on production.
- Do not call execute until the user explicitly approves the local/test execute step.
- Do not set real secrets in repository files.
- Do not paste full connection strings into docs, logs, screenshots, or API responses.
- Do not schedule Hangfire.
