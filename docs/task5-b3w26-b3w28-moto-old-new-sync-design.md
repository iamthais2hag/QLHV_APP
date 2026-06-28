# Task 5 B3W26-B3W28 - Moto old/new CSDT_V1 <-> CSDT_V2 sync design

## Scope and safety

This document is design/audit only.

No SQL was run. No sync was run. `/execute` was not called. `EnableTargetWrites` was not enabled. No production
database is approved.

## Sources inspected

Requested docs:

- `docs/requirements.md`: not present in the repository.
- `docs/sync-v1-v2-spec.md`: not present in the repository.
- `docs/task5-b3w22-sync-source-concept-correction.md`
- `docs/task5-b3w24-sync-checkpoint-roadmap.md`
- `docs/task5-b3w25-data-v2-write-sync-decision.md`

Supporting docs/scripts:

- `docs/sql-scripts-index.md`
- `docs/sync-data-flow-architecture.md`
- `docs/csdt-connection-profiles.md`
- `database/TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql`
- `database/CHECK_CCCD_CONFLICT_V1_V2.sql`
- `database/VALIDATE_V1_TO_V2_CHUYEN_ALL_KHOA.sql`
- `database/V2_POST_TRANSFER_HEALTH_CHECK.sql`
- `database/reference/V1_schema_full.sql`
- `database/reference/V2_schema_full.sql`

Code was inspected only for reference:

- `server/QLHV.Application/Sync`
- `server/QLHV.Infrastructure/Sync`

## 1. Product framing

`QLHV_APP` is the business management app for Trung tam dao tao lai xe co gioi duong bo Thanh Cong.

Sync/import is one important capability inside the product. It is not the whole product.

Moto old/new sync supports training and GPLX/sat hach workflows by helping reconcile controlled test/staging data from
old Moto data into the new Moto data flow, then into QLHV_APP only after review.

## 2. Current known databases

| Name | Meaning |
| --- | --- |
| `CSDT_V1` | Moto old 2025/GPLX test database. |
| `CSDT_V2` | Moto new 2026 test database. |
| `QLHV_APP_TEST` | QLHV_APP application test database. |

Important concept:

- `CSDT_V1` and `CSDT_V2` represent one Moto old/new business flow under review.
- They are not two permanent parallel learner sources to duplicate in QLHV_APP.
- `DATA_V1`, `DATA_V2`, and `SourceProfileCode` are technical staging/sync identities.
- `SourceProfileCode + SourceMaDK` is a safe sync/upsert/audit identity, not final learner/person business identity.

## 3. Sync directions

### Direction A: `CSDT_V1 -> CSDT_V2`

This is the direction already represented by existing SQL reference scripts.

Current evidence:

- `TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql` is a V1 to V2 transfer script.
- It defaults to dry-run/rollback.
- It handles courses, learners, learner records, GPLX, learner documents, optional teacher/course schedule data, and
  optional course-vehicle generation.
- It has explicit CCCD conflict checking.

Recommended first work:

1. Read-only schema/table comparison.
2. Read-only row-count/key diagnostics.
3. Read-only transfer plan.
4. Only then consider TEST-only write.

### Direction B: `CSDT_V2 -> CSDT_V1`

This direction is not yet justified by repository evidence.

It should be analyzed read-only first and treated as higher risk because it may back-propagate newer 2026 data into the
old 2025/GPLX source.

Do not implement V2 -> V1 write until the owner states a concrete business use case.

Possible read-only questions:

- Does V1 need status updates from V2?
- Does V1 need GPLX/sat hach results from V2?
- Is V1 only a historical source that should not be modified?
- Is the old system still operational, or only a backup/reference database?

## 4. Candidate tables

The candidate tables come from the existing V1 -> V2 script and schema references.

| Table | Purpose | Likely key | Risk | Likely direction | Dry-run first? | Open questions |
| --- | --- | --- | --- | --- | --- | --- |
| `KhoaHoc` | Course header/training cohort. | `MaKH` | Medium | V1 -> V2 first | Yes | Does `MaKH` need CSĐT code remap? Which course statuses should transfer? |
| `BaoCaoI` | Report/intake/training report data linked to course. | `MaBCI` | Medium-High | V1 -> V2 first | Yes | Is `MaBCI` globally unique? Which status fields are authoritative? |
| `NguoiLX` | Learner/person identity and demographic data. | `MaDK` | High | V1 -> V2 first | Yes | How to handle same CCCD with different `MaDK`? Which personal fields win? |
| `NguoiLX_HoSo` | Learner dossier/course registration. | `MaDK` | High | V1 -> V2 first | Yes | Which `TrangThai` / `TT_XuLy` values are safe? Can one learner have multiple records? |
| `NguoiLX_GPLX` | Existing/issued license data. | `MaDK + SoGPLX + HangGPLX` from script | High | V1 -> V2 first | Yes | Should historical GPLX rows ever update existing V2 rows, or insert-only? |
| `NguoiLXHS_GiayTo` | Learner dossier documents. | `MaGT + MaDK` | Medium-High | V1 -> V2 first | Yes | Are files/paths portable? Should physical file paths be rewritten or only metadata copied? |
| `KhoaHoc_GiaoVien` | Course teacher/vehicle assignment source. | `MaKH + MaGV + LoaiGV + BienSoXe` from script | Medium | Optional V1 -> V2 | Yes | Does V2 already manage teachers differently? Is `BienSoXe` reliable? |
| `LichHoc` | Course schedule. | `MaKH + Thang + Tuan + TuNgay + DenNgay` from script | Medium | Optional V1 -> V2 | Yes | Should duplicate/overlapping schedule ranges be merged or skipped? |
| `KhoaHoc_XeTap` | Course vehicle assignment in V2. | `MaKH + BienSoXe + MaGV` from generated logic | Medium-High | Generated in V2 from V1 `KhoaHoc_GiaoVien` if needed | Yes | Does generated data match V2 business model? What if vehicle does not exist in `XeTap`? |

## 5. Field mapping strategy

Use a conservative mapping strategy:

1. Map only fields with the same name and confirmed meaning first.
2. Treat renamed fields as unknown until validated by schema comparison and sample data.
3. Never guess destructive mappings.
4. Prefer insert-only for initial TEST plans unless update rules are explicitly approved.
5. Compare but do not overwrite fields that may be independently maintained in V2.
6. Keep path/file fields read-only until physical file storage rules are confirmed.
7. Do not auto-correct identity values such as CCCD/CMND/GPLX.
8. Use read-only diagnostics for conflicts:
   - duplicate keys;
   - same CCCD with different `MaDK`;
   - same `MaDK` with different personal fields;
   - missing parent rows;
   - invalid date/status/code values.

Existing scripts suggest these high-value checks:

- `CHECK_CCCD_CONFLICT_V1_V2.sql`: same CCCD, different `MaDK`.
- `VALIDATE_V1_TO_V2_CHUYEN_ALL_KHOA.sql`: missing courses/records after transfer.
- `V2_POST_TRANSFER_HEALTH_CHECK.sql`: referential and format checks after transfer.

These scripts remain references only. Do not run them in this task.

## 6. Safety model

Required model before any write:

### DryRun

- Must be default.
- Reads source and target.
- Produces counts, conflicts, and planned actions.
- Does not write either database.

### Preview

- Shows exact table-level planned inserts/updates/skips.
- Shows conflict counts and sanitized sample identifiers.
- Does not expose secrets or raw sensitive values unless explicitly approved for local-only review.

### Confirm

- Requires explicit owner approval.
- Requires exact scenario:
  - source DB;
  - target DB;
  - table set;
  - filters;
  - expected counts;
  - backup/snapshot confirmation.

### Transaction

- TEST write must run inside a transaction.
- Table order must respect dependencies:
  1. `KhoaHoc`
  2. `BaoCaoI`
  3. `NguoiLX`
  4. `NguoiLX_HoSo`
  5. `NguoiLX_GPLX`
  6. `NguoiLXHS_GiayTo`
  7. optional `KhoaHoc_GiaoVien`
  8. optional `LichHoc`
  9. optional `KhoaHoc_XeTap`

### Rollback

- Dry-run must rollback automatically.
- Write execution must have a clear rollback/restore plan.
- For multi-table V1 -> V2 transfers, database backup/snapshot is the primary rollback strategy.

### Audit log

- Future app-managed execution should record:
  - source;
  - target;
  - table set;
  - filters;
  - planned counts;
  - executed counts;
  - warnings;
  - operator;
  - timestamps.
- Do not audit raw passwords, connection strings, CCCD/GPLX full values, or physical file paths unnecessarily.

### Backup/snapshot

- Required before any write.
- TEST first only.
- Production is explicitly out of scope.

## 7. Explicit exclusions

- No production database.
- No execute now.
- No `EnableTargetWrites` now.
- No SQL script execution in this task.
- No Oto implementation in this task.
- No frontend changes in this task.
- No connection string/secret changes.

## 8. Oto note

Oto is future work and likely more complex than Moto.

Do not assume Moto mapping applies directly to Oto.

Oto needs its own:

- schema comparison;
- GPLX pairing analysis;
- table mapping;
- conflict policy;
- dry-run diagnostics;
- owner approval.

