# Task 5 B3W35-B3W37 - A1K03 short MaDK duplicate diagnostics

## Scope

This is a read-only diagnostics/design package for the suspected A1K03 duplicate issue in Moto `CSDT_V2`.

No SQL was run by Codex. No sync was run. `/execute` was not called. `EnableTargetWrites` was not enabled.

## Problem statement

Manual UI evidence suggests the A1K03 course count differs sharply between old/GPLX and new/V2 views:

- GPLX/old app searching A1K03 shows about 204 records.
- CSDT new/V2 searching A1K03 shows about 404 records.
- Some learners appear duplicated by business identity.
- Suspected cause: `MaDK` was shortened/truncated for some rows.

Example:

- Full `MaDK`: `66016-20260305-112014417`
- Short `MaDK`: `66016-20260305-112014`

If this hypothesis is true, the 200 V2-only rows found in B3W32-B3W34 may be duplicate/error rows, not valid new data.

## Why V2 -> V1 sync is blocked

`CSDT_V2 -> CSDT_V1` must remain blocked because back-propagating the suspected V2-only rows could copy duplicate or
incorrect learner records into the old/GPLX database.

Before any write design, we need to know:

- whether V2 A1K03 has about 404 rows because of true new records or duplicated short/full `MaDK` pairs;
- whether duplicate groups share the same business identity:
  - normalized name;
  - date of birth;
  - CCCD/SoCMT;
  - course;
- whether dependent records such as `NguoiLXHS_GiayTo` also duplicated under both short and full `MaDK`;
- whether V1 already has the canonical/full `MaDK` rows.

## Files inspected

- `docs/task5-b3w32-b3w34-moto-v2-to-v1-readonly-feasibility.md`
- `database/diagnostics/task5_b3w32_b3w34_moto_v2_to_v1_feasibility.sql`
- `database/diagnostics/task5_b3w29_b3w31_moto_v1_v2_row_key_diagnostics.sql`
- `database/reference/V1_schema_full.sql`
- `database/reference/V2_schema_full.sql`

## SQL diagnostic script

Created:

- `database/diagnostics/task5_b3w35_b3w37_a1k03_short_madk_duplicates.sql`

The script starts with:

```sql
USE [CSDT_V2];
GO
```

The script reads `CSDT_V2` and `CSDT_V1` only. It does not create permanent objects and does not modify data.

## What diagnostics will prove

The script returns these result sets:

1. Course discovery for exact `MaKH = 66016K26A1003` and UI label search `A1K03`.
2. A1K03 V2 HoSo summary:
   - total rows;
   - distinct `MaDK`;
   - distinct SoCMT/CCCD;
   - min/max `NgayNhanHSo`.
3. `TT_XuLy` distribution for A1K03 in V2.
4. Duplicate business identity groups in V2 A1K03:
   - normalized name;
   - date of birth;
   - masked SoCMT;
   - `MaKhoaHoc`;
   - row count;
   - list of `MaDK` values.
5. Short/full `MaDK` prefix pairs:
   - one `MaDK` is a prefix of another;
   - length difference is 3;
   - same normalized name, date of birth, and SoCMT.
6. V1 vs V2 A1K03 overlap:
   - V1 rows for exact `MaKhoaHoc`;
   - V2 rows;
   - exact `MaDK` overlap;
   - business-key overlap.
7. Dependency impact by short/full pair:
   - duplicated `NguoiLX`;
   - duplicated `NguoiLX_HoSo`;
   - duplicated `NguoiLXHS_GiayTo` counts.
8. Sanitized suspected duplicate sample:
   - shows `MaDK`;
   - masks name;
   - masks SoCMT except last 4 characters;
   - shows SoCMT length;
   - shows course/status/date context.

## Possible remediation options

These are discussion options only. Do not auto-fix.

1. Mark duplicates for review
   - Keep data unchanged.
   - Produce a list of short/full pairs and suspected canonical records.

2. Choose canonical `MaDK`
   - Usually the full-length `MaDK` may be canonical, but this must be confirmed by owner/business evidence.
   - Dependent tables must be reviewed before any change.

3. Repair V2 data
   - Only after backup, owner approval, and a controlled TEST-only script.
   - Must include rollback strategy.

4. Rebuild V2 from clean source
   - If duplicates came from a bad import/conversion, rebuilding may be safer than manual repair.

5. Block V2 -> V1 sync
   - Recommended until duplicate cause is confirmed.
   - Prevents bad V2 rows from contaminating old/GPLX data.

## Stop conditions

Stop and do not design any write plan if:

- A1K03 V2 has duplicate business identity groups.
- Short/full `MaDK` pairs are present.
- V2-only rows mostly match V1 by business key but not by exact `MaDK`.
- Dependent `NguoiLXHS_GiayTo` rows exist for both short and full `MaDK`.
- It is unclear which `MaDK` is canonical.
- The owner has not approved a precise TEST-only repair or sync scenario.

## Safe SSMS run instructions

1. Open SSMS against TEST/local SQL Server only.
2. Confirm you are not connected to production.
3. Open `database/diagnostics/task5_b3w35_b3w37_a1k03_short_madk_duplicates.sql`.
4. Confirm the first lines are:

   ```sql
   USE [CSDT_V2];
   GO
   ```

5. Execute only this read-only diagnostics script.
6. Paste all result sets back for review.
7. Do not run sync, `/execute`, transfer scripts, or repair scripts.

## Next recommended step

Run the diagnostics manually on TEST/local and paste the result sets back.

Only after the results are reviewed should the next task decide whether to:

- continue blocking V2 -> V1;
- design a duplicate repair proposal;
- design a read-only canonicalization report;
- or prepare a TEST-only repair runbook.
