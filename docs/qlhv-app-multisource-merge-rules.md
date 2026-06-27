# QLHV_APP multi-source merge rules

This document captures the initial merge rules for importing multiple staged sources into QLHV_APP.
It is documentation only and does not change application behavior.

## Core principle

`DATA_V1` and `DATA_V2` are separate source scopes.
An import from one source must not destroy or silently replace data imported from the other source.

Required rule:

- Import `DATA_V1` must not make `DATA_V2` data disappear.
- Import `DATA_V2` must not make `DATA_V1` data disappear.

## Source identity

Every imported target row should be traceable to its source.
The target design should preserve enough identity to answer:

- Which profile produced this row?
- Which source record key produced this row?
- When was this source last imported?
- Did this import insert, update, skip, or warn?

Required fields or concepts for HocVien:

- `SourceProfileCode`: `DATA_V1` or `DATA_V2`.
- `MaDK`: source registration code used by current UI/business display.
- `SourceMaDK`: optional explicit copy of source registration code, used if display `MaDK` later diverges.
- `SourceSystem`: logical source name, for example `V1` or `V2`.
- `SourceRecordKey`: stable source primary/business key.
- `V2RowHash` or future source-specific row hash.
- `LastSyncStatus`.
- `LastSyncFromSourceAt`.

For `App_HocVien`, the sync identity is:

```text
SourceProfileCode + MaDK
```

Do not use `MaDK` alone for sync target matching once multi-source import is introduced.

## Target uniqueness

The current single-source V2 path keys `App_HocVien` by `MaDK`.
That is acceptable only for a single-source experiment.
For multi-source HocVien import, `MaDK` alone is not an approved target identity because `DATA_V1` and `DATA_V2` can
contain overlapping registration codes.

Approved direction:

1. Keep source-owned imported rows by `(SourceProfileCode, MaDK)` for sync/import.
2. Preserve V1 and V2 rows independently unless a later canonical merge design is approved.
3. Add a separate canonical/person-merge layer only after conflict rules are reviewed.

Phase 1 schema adds nullable source identity columns and indexes only.
Phase 2 must update code and backfill data before enforcing composite uniqueness.

## Import behavior by source

For each source-scoped import:

- Insert rows that exist in the selected source but not in the selected target scope.
- Update rows in the selected target scope only when that source row hash differs.
- Skip rows whose hash matches.
- Never physically delete rows from another source scope.
- Source-only deletion handling must be explicit. Default is report-only, not delete.

Additional HocVien rules:

- `DATA_V1` import may insert/update only rows where `SourceProfileCode = DATA_V1`.
- `DATA_V2` import may insert/update only rows where `SourceProfileCode = DATA_V2`.
- `DATA_V1` import must not match target rows where `SourceProfileCode = DATA_V2`.
- `DATA_V2` import must not match target rows where `SourceProfileCode = DATA_V1`.
- Any code path still using `MaDK` alone for `MERGE`, target snapshot, existing-key lookup, or pre-execute plan is
  single-source only and must stay blocked for execute.

Example:

| Existing target data | Import action | Allowed result |
| --- | --- | --- |
| Rows from `DATA_V1` exist | Import `DATA_V2` | `DATA_V1` rows remain. `DATA_V2` rows insert/update in V2 scope. |
| Rows from `DATA_V2` exist | Import `DATA_V1` | `DATA_V2` rows remain. `DATA_V1` rows insert/update in V1 scope. |
| Same `MaDK` appears in both sources | Import either source | Apply approved conflict rule; do not silently overwrite the other source. |

## Conflict policy to decide

These questions must be answered before production execute:

- If `DATA_V1` and `DATA_V2` have the same `MaDK`, are they the same learner or separate source records?
- Which source wins for display fields such as name, address, gender, course, and license class?
- Should QLHV_APP show source-specific rows or a merged canonical row?
- How are conflicts reviewed by humans?
- Should conflicting rows block execute, warn only, or create review tasks?

## Diagnostics needed before execute

Before any multi-source execute, diagnostics should report:

- source row count by profile;
- duplicate key count within each profile;
- overlapping key count across `DATA_V1` and `DATA_V2`;
- rows with missing business keys;
- rows that would insert/update/skip by source scope;
- target-only rows by source scope;
- conflict counts by field category.

Diagnostics must be aggregate and safe.
Do not expose raw CCCD, GPLX, passwords, full connection strings, server names, database names, or usernames.

## Current B3V2 status

B3V2 is a work-in-progress single-source pre-execute plan.
It compares one source against QLHV_APP using `MaDK` and `V2RowHash`.

Do not merge or treat B3V2 as final multi-source behavior until the source identity and conflict policy above are
approved and the code uses `SourceProfileCode + MaDK`.
