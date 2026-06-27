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

## B3W8 read-only source attribution diagnostics

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

The response reports aggregate counts:

- `targetRows`;
- `targetRowsWithSourceProfileCode`;
- `targetRowsWithoutSourceProfileCode`;
- `matchedWithDataV1ByMaDk`;
- `matchedWithDataV2ByMaDk`;
- `matchedBoth`;
- `matchedNeither`;
- `recommendation`: `DATA_V1`, `DATA_V2`, `Ambiguous`, or `CannotDetermine`.

Recommendation rules:

- `DATA_V1`: all active target `MaDK` values match `DATA_V1` only.
- `DATA_V2`: all active target `MaDK` values match `DATA_V2` only.
- `Ambiguous`: at least one target `MaDK` matches both sources, or the target set is split across sources.
- `CannotDetermine`: there are target rows that do not match either source, a source cannot be read, or there are no
  target rows.

The result is a proposal for human review only. It must not perform automatic backfill.
