# HocVien multi-source identity

Task 5 B3W6 defines the target identity design for importing learner data from multiple source profiles into
`QLHV_APP.dbo.App_HocVien`.

This is a design document only. It does not approve execute and does not change sync behavior.

## Problem

The current single-source HocVien sync foundation uses `MaDK` as the target key.
That is safe only while one source owns the target rows.

The multi-source model introduces separate import profiles:

- `DATA_V1`
- `DATA_V2`

Both profiles can import HocVien data into `QLHV_APP`.
If both sources contain the same `MaDK`, a merge keyed only by `MaDK` can accidentally update, hide, or overwrite data
from the other source.

Example risk:

| Existing target row | Incoming import | Bad single-key behavior |
| --- | --- | --- |
| `SourceProfileCode = DATA_V1`, `MaDK = A001` | `DATA_V2`, `MaDK = A001` | DATA_V2 updates the DATA_V1 row because `MaDK` matches. |
| `SourceProfileCode = DATA_V2`, `MaDK = B001` | `DATA_V1`, `MaDK = B001` | DATA_V1 updates the DATA_V2 row because `MaDK` matches. |

That violates the rule: importing `DATA_V1` must not make `DATA_V2` data disappear, and importing `DATA_V2` must not
make `DATA_V1` data disappear.

## New identity

The sync identity for imported HocVien rows must be:

```text
SourceProfileCode + MaDK
```

Where:

- `SourceProfileCode` is the fixed connection/import profile, initially `DATA_V1` or `DATA_V2`.
- `MaDK` is the source registration code preserved from the source row.

The pair identifies one source-owned learner record.

Recommended target fields:

| Field | Purpose |
| --- | --- |
| `SourceProfileCode` | Import profile that owns the row, for example `DATA_V1` or `DATA_V2`. |
| `SourceMaDK` | Source registration code exactly as imported, kept for clarity if display `MaDK` changes later. |
| `SourceSystem` | Logical source family, for example `V1` or `V2`. |
| `SourceVersion` | Optional source version/profile detail for future diagnostics. |
| `MaDK` | Current display/business registration code used by the existing UI. |

Phase 1 keeps `MaDK` unchanged for compatibility.
Phase 2 decides whether `MaDK` remains the display code while `SourceMaDK` becomes the sync key input.

## Import rules

`DATA_V1` import:

- only inserts rows with `SourceProfileCode = DATA_V1`;
- only updates rows with `SourceProfileCode = DATA_V1`;
- never updates rows with `SourceProfileCode = DATA_V2`;
- never deletes rows with `SourceProfileCode = DATA_V2`.

`DATA_V2` import:

- only inserts rows with `SourceProfileCode = DATA_V2`;
- only updates rows with `SourceProfileCode = DATA_V2`;
- never updates rows with `SourceProfileCode = DATA_V1`;
- never deletes rows with `SourceProfileCode = DATA_V1`.

All imports:

- must report target-only rows by `SourceProfileCode`;
- must not physically delete rows from another source;
- must calculate pre-execute plans per `SourceProfileCode`;
- must include `SourceProfileCode` in diagnostics, logs, and safe summaries;
- must not expose raw CCCD/GPLX or connection strings.

## Current B3V2 status

B3V2 is a work-in-progress single-source pre-execute plan.
It compares one source against `QLHV_APP` using `MaDK` and `V2RowHash`.

B3V2 must not be treated as execute readiness for the multi-source model.
Before execute/import is allowed, the code must be changed so target comparison and merge use:

```text
SourceProfileCode + MaDK
```

instead of `MaDK` alone.

## Phase plan

### Phase 1: non-breaking schema preparation

Patch:

```text
database/patches/20260627_add_hocvien_source_profile.sql
```

Design goals:

- add nullable source identity fields;
- add filtered indexes to support future source-scoped lookups;
- do not update existing data;
- do not drop `UQ_App_HocVien_MaDK`;
- do not enforce `NOT NULL`;
- do not change current sync behavior.

### Phase 2: backfill and code migration

After data review:

- run read-only source attribution diagnostics to decide whether current rows belong to `DATA_V1`, `DATA_V2`, or are
  ambiguous;
- backfill existing rows with the correct `SourceProfileCode`;
- update source readers/planners to carry `SourceProfileCode`;
- update staging table and merge SQL to include `SourceProfileCode`;
- change merge condition from `tgt.MaDK = src.MaDK` to source-scoped identity;
- update target diagnostics and pre-execute plan to compare within one `SourceProfileCode`;
- decide whether to keep or replace `UQ_App_HocVien_MaDK`;
- optionally add a unique composite index after backfill, for example `(SourceProfileCode, MaDK)`.

### Phase 3: multi-source conflict handling

After code migration:

- detect overlapping `MaDK` across `DATA_V1` and `DATA_V2`;
- decide whether UI shows source-specific rows or a canonical merged row;
- add human review for conflicts if needed.

## B3W8/B3W9 read-only source attribution diagnostics

Before backfilling existing `App_HocVien.SourceProfileCode`, use:

```http
GET /api/dong-bo-v2/hoc-vien/source-attribution-diagnostics
```

The endpoint is read-only. It reads:

- `QLHV_APP.dbo.App_HocVien`;
- the `DATA_V1` connection profile;
- the `DATA_V2` connection profile.

It does not write `App_HocVien`, does not write `App_DongBoLog`, does not run sync/execute, and does not return raw
CCCD/GPLX or connection details.

B3W8 compared `MaDK` only. The first local/test result was ambiguous:

- `targetRows = 1970`;
- `targetRowsWithSourceProfileCode = 0`;
- `targetRowsWithoutSourceProfileCode = 1970`;
- `DATA_V1 sourceRows = 1770`;
- `DATA_V1 matched target by MaDK = 1770`;
- `DATA_V2 sourceRows = 1970`;
- `DATA_V2 matched target by MaDK = 1970`;
- `matchedBoth = 1770`;
- `matchedNeither = 0`;
- `recommendation = Ambiguous`.

Because `1770` target rows matched both source profiles by `MaDK`, `MaDK` alone is not enough to backfill
`SourceProfileCode`.

B3W9 extends the same endpoint with stronger aggregate comparison. The response reports:

- `targetRows`;
- `targetRowsWithSourceProfileCode`;
- `targetRowsWithoutSourceProfileCode`;
- `matchedWithDataV1ByMaDk`;
- `matchedWithDataV2ByMaDk`;
- `dataV1SourceRows`;
- `dataV2SourceRows`;
- `dataV1DistinctSourceMaDk`;
- `dataV2DistinctSourceMaDk`;
- `dataV1DuplicateSourceMaDkCount`;
- `dataV2DuplicateSourceMaDkCount`;
- `matchedByMaDkDataV1`;
- `matchedByMaDkDataV2`;
- `exactFieldMatchDataV1`;
- `exactFieldMatchDataV2`;
- `v2RowHashMatchDataV1`;
- `v2RowHashMatchDataV2`;
- `strongerMatchDataV1`;
- `strongerMatchDataV2`;
- `dataV2OnlyMaDkCount`;
- `dataV1OnlyMaDkCount`;
- `overlappingMaDkCount`;
- `matchedBoth`;
- `matchedNeither`;
- `changedFieldSummary` with counts by field only;
- `recommendation`: `DATA_V1`, `DATA_V2`, `Ambiguous`, or `CannotDetermine`.
- `confidence`: `Low`, `Medium`, or `High`.

Deep diagnostics compares aggregate matches by:

- `MaDK`;
- normalized `HoTen`;
- `NgaySinh`;
- `GioiTinh`;
- `MaKhoa`;
- `TenKhoa`;
- `MaHangDT`;
- `HangGPLXHoc`;
- `V2RowHash` where available.

During B3W9 review, `DATA_V1` schema checks showed:

- `NguoiLX_HoSo JOIN NguoiLX = 1770` rows;
- adding `KhoaHoc` still returns `1770` rows;
- adding `DM_HangDT` still returns `1770` rows;
- adding `DM_DVHC` expands to `3564` rows.

That means `DM_DVHC` can multiply rows for this diagnostic path. Source attribution does not need address, so the
attribution source reader intentionally does not join `DM_DVHC` and does not compare address at this step.

The source reader for attribution is intentionally limited to:

- `hs.MaDK`;
- `nlx.HoVaTen`;
- `nlx.NgaySinh AS NgaySinhRaw`;
- `nlx.GioiTinh`;
- `hs.MaKhoaHoc`;
- `kh.TenKH`;
- `hs.HangDaoTao`;
- `hdt.TenHangDT`.

It also reports `sourceRows`, `distinctSourceMaDk`, and `duplicateSourceMaDkCount` for each source profile.
If duplicates exist, the endpoint uses the first row per `MaDK` only for aggregate comparison and returns a clear issue
instead of a generic SQL exception.

`DATA_V1` may run under an older SQL Server compatibility level where `TRY_CONVERT` is unavailable. The attribution
query therefore does not use `TRY_CONVERT`, `TRIM`, `CONCAT_WS`, or `STRING_AGG`. It reads `NgaySinhRaw` and parses
`yyyyMMdd` in C# with `DateTime.TryParseExact`. Invalid date values are treated as `null` for comparison and counted in
`invalidNgaySinhCount`.

The endpoint must not return raw CCCD/GPLX, connection strings, server names, database names, usernames, or passwords.
Because the attribution reader does not pull address/CCCD/GPLX, it cannot recompute the full production `V2RowHash`;
backfill must still wait until the endpoint can read both `DATA_V1` and `DATA_V2` cleanly and produce a clear enough
recommendation.

Recommendation rules:

- `DATA_V1`: target rows have a clearly stronger field/hash match to `DATA_V1`.
- `DATA_V2`: target rows have a clearly stronger field/hash match to `DATA_V2`.
- `Ambiguous`: both sources match too closely or the difference is not strong enough.
- `CannotDetermine`: there are no target rows, a source cannot be read, or the data is insufficient.

Only a `High` confidence recommendation can be considered for a later human-approved backfill plan.
The result is a proposal for human review only. It must not perform automatic backfill.

## B3W10 controlled DATA_V2 backfill script

B3W10 creates a controlled SQL patch for the existing 1970 `App_HocVien` rows after B3W9 returned:

- `targetRows = 1970`;
- `targetRowsWithSourceProfileCode = 0`;
- `targetRowsWithoutSourceProfileCode = 1970`;
- `DATA_V1 sourceRows = 1770`;
- `DATA_V2 sourceRows = 1970`;
- `strongerMatchDataV1 = 1770`;
- `strongerMatchDataV2 = 1970`;
- `dataV2OnlyMaDkCount = 200`;
- `recommendation = DATA_V2`;
- `confidence = High`.

Patch files:

- `database/patches/20260627_backfill_hocvien_source_profile_data_v2.sql`;
- `database/patches/20260627_rollback_hocvien_source_profile_data_v2.sql`.

B3W10 only creates scripts. It does not run them, does not backfill data, does not run sync, and does not approve
execute.

The backfill script is intentionally narrow:

- requires `dbo.App_HocVien` and source identity columns to exist;
- requires exactly `1970` rows;
- requires all `1970` rows to have empty `SourceProfileCode`;
- requires no empty `MaDK`;
- updates only empty `SourceProfileCode` rows;
- sets `SourceProfileCode = DATA_V2`;
- sets `SourceMaDK = MaDK`;
- sets `SourceSystem = V2`;
- leaves `SourceVersion = NULL` because no source version convention is approved.

The rollback script is also narrow. It clears source identity fields only if all `1970` rows are exactly in the
expected DATA_V2 backfilled state. Both scripts must be reviewed and run on `QLHV_APP_TEST` with a backup/snapshot
before any production consideration.

B3W11 hotfix adds `USE [QLHV_APP_TEST]` and required SQL Server SET options, including
`SET QUOTED_IDENTIFIER ON`, to both backfill and rollback scripts so they can run correctly through `sqlcmd`.
