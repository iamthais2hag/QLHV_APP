# Task 5 B3W49-B3W54 - Moto Sync Update Existing Rows

## Scope

This task extends the TEST-only Moto sync workflow with a guarded update mode for existing rows.

Current endpoints remain:

- `GET /api/dong-bo-v2/moto/sync-plan`
- `POST /api/dong-bo-v2/moto/sync-test`

## Sync Mode

`POST /api/dong-bo-v2/moto/sync-test` now accepts:

- `syncMode = INSERT_ONLY`
- `syncMode = INSERT_AND_UPDATE`

Default behavior is still `INSERT_ONLY`.

## Confirmation

- `INSERT_ONLY` requires `confirmText = SYNC TEST DATABASE`
- `INSERT_AND_UPDATE` requires `confirmText = SYNC TEST DATABASE UPDATE`

## Update Scope

Update mode can update existing rows in:

- `dbo.NguoiLX`
- `dbo.NguoiLX_HoSo`

`dbo.NguoiLX` updates are scoped by `MaDK`.
`dbo.NguoiLX_HoSo` updates are scoped by both `MaDK` and selected `MaKhoaHoc`; `MaKhoaHoc` is not a writable update column.

`dbo.NguoiLXHS_GiayTo` remains insert-only in this task.

No delete behavior is introduced.

## Plan Behavior

The read-only plan compares exact overlapping `MaDK` rows for safe writable common columns.

Safe writable columns exclude:

- `MaDK`
- ID/key-style columns
- identity columns
- computed columns
- rowversion/timestamp columns
- binary columns

Plan response includes aggregate update counts and sanitized update samples:

- `plannedUpdate`
- `plannedUpdateNguoiLX`
- `plannedUpdateNguoiLXHoSo`
- `updateSamples[].maDK`
- `updateSamples[].tableName`
- `updateSamples[].changedColumnNames`

Samples do not return raw personal values.

## Blockers

The existing blockers remain active:

- short/full `MaDK` duplicate pairs
- duplicate business key groups
- missing `KhoaHoc` dependency
- unsupported/missing table metadata

String width checks are data-aware for planned update values. Wider source schema is only a warning when actual planned values fit the target. Actual values exceeding target width remain blockers. Data is never truncated silently.

## Safety

This workflow is TEST-only for `CSDT_V1` and `CSDT_V2`.

Do not run production sync.
Do not call execute without owner approval.
Do not enable production writes.
