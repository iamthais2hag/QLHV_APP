# Task 5 B3W39-B3W45 - Moto sync workflow for current TEST CSDT_V1/CSDT_V2

## Scope

This task adds a practical guarded workflow for the current disposable TEST databases:

- `CSDT_V1`: Moto old 2025/GPLX TEST database.
- `CSDT_V2`: Moto new 2026 TEST database.

These databases are test databases with separate `.bak` backups. The workflow is intended to prove that the app can
plan and execute a controlled sync on TEST databases. It is not a production sync workflow.

No SQL was run by Codex for this task. No sync endpoint was called by Codex. `/execute` was not called.

## Endpoints

### Read-only plan

```http
GET /api/dong-bo-v2/moto/sync-plan?direction=V1_TO_V2&sourceProfileCode=CSDT_V1&targetProfileCode=CSDT_V2&maKhoaHoc=
```

Supported `direction` values:

- `V1_TO_V2`
- `V2_TO_V1`

Supported profile pairs in this task:

- `V1_TO_V2`: `sourceProfileCode=CSDT_V1`, `targetProfileCode=CSDT_V2`
- `V2_TO_V1`: `sourceProfileCode=CSDT_V2`, `targetProfileCode=CSDT_V1`

`allowDirtyData` may be sent to preview, but dirty duplicate blockers still block execution in this task.

### Guarded TEST execute

```http
POST /api/dong-bo-v2/moto/sync-test
Content-Type: application/json

{
  "direction": "V1_TO_V2",
  "sourceProfileCode": "CSDT_V1",
  "targetProfileCode": "CSDT_V2",
  "maKhoaHoc": null,
  "confirmText": "SYNC TEST DATABASE"
}
```

The endpoint recomputes the plan immediately before write. If the recomputed plan has any blocker, execution is refused.

## Guard Model

The TEST execute endpoint is separate from the existing HocVien `/execute` endpoint.

Hard guards:

- Only `CSDT_V1` and `CSDT_V2` are accepted.
- The source/target pair must match the direction.
- `confirmText` must exactly equal `SYNC TEST DATABASE`.
- The plan is recomputed immediately before writing.
- Any blocker refuses execution.
- All writes are wrapped in one target transaction.
- Errors roll back the target transaction.

The endpoint does not require `SyncExecution:EnableTargetWrites`, because this is a separate TEST-only workflow and the
owner explicitly clarified that current CSDT_V1/CSDT_V2 databases are disposable TEST databases. Production remains
blocked by profile guard and by the absence of production profile support.

## Insert-Only Scope

The first implementation is insert-only:

- Inserts missing `NguoiLX` by `MaDK`.
- Inserts missing `NguoiLX_HoSo` by `MaDK`.
- Inserts missing `NguoiLXHS_GiayTo` by `MaGT + MaDK`.
- Checks `KhoaHoc` dependency only.
- Does not auto-create `KhoaHoc`.
- Does not update existing rows.
- Does not delete rows.
- Does not repair A1K03 duplicate rows.

## What The Plan Returns

The plan returns aggregate counts only:

- `sourceRows`
- `targetRows`
- `exactMaDkOverlap`
- `sourceOnly`
- `targetOnly`
- `duplicateBusinessKeyGroups`
- `shortFullMaDkPairs`
- `plannedInsertNguoiLX`
- `plannedInsertNguoiLXHoSo`
- `plannedInsertGiayTo`
- `plannedUpdate = 0`
- `blockers`
- `executable`

It does not return raw CCCD/GPLX values or connection strings.

## Blockers

Execution is refused when any of these are detected:

- Source has short/full `MaDK` duplicate pairs.
- Source has duplicate business-key groups.
- Target is missing `KhoaHoc` dependencies.
- Required target columns cannot be mapped from source columns.
- Source string column is wider than target for a column being copied.
- Source/target profile is not one of the current TEST profiles.
- Direction does not match the requested source/target pair.

Known A1K03 issue:

- A1K03 / `MaKhoaHoc = 66016K26A1003` has systematic short/full `MaDK` pairs.
- This dirty case remains blocked.
- No repair mode is included in this task.

## Verification Commands

```powershell
cd D:\QLHV_APP\server
dotnet build QLHV.sln -c Release
dotnet test QLHV.Tests\QLHV.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~QLHV.Tests.Sync"
```

## Safety

- Do not use production databases.
- Do not commit real connection strings, usernames, passwords, or secrets.
- Do not call the existing `/api/dong-bo-v2/hoc-vien/execute`.
- Do not run this TEST execute endpoint unless the owner explicitly approves the exact TEST scenario.
- Restore from `.bak` if a TEST write has to be discarded.
