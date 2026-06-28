# Task 5 B3W32-B3W34 - Moto CSDT_V2 -> CSDT_V1 read-only feasibility

## Scope

This document and the paired SQL script prepare a read-only feasibility review for a possible Moto `CSDT_V2 -> CSDT_V1`
sync of learner/person and dossier data.

No SQL was run by Codex. No sync was run. `/execute` was not called. `EnableTargetWrites` was not enabled.

## Current context

Current TEST database meaning:

- `CSDT_V1`: Moto old 2025/GPLX test database.
- `CSDT_V2`: Moto new 2026 test database.
- `QLHV_APP_TEST`: QLHV_APP application test database.

Known current diagnostics from owner-provided result:

- `NguoiLX_HoSo` V1 has 1770 rows.
- `NguoiLX_HoSo` V2 has 1970 rows.
- Overlap is 1770 rows.
- V2 has 200 V2-only `NguoiLX_HoSo` rows.
- V2-only sample belongs to `MaKhoaHoc = 66016K26A1003`, `HangDaoTao = A1m`.
- V1-only `NguoiLX` has `MaDK = 66016-20260416-113556247` and `HasV1HoSo = 0`.

Important concept:

- `CSDT_V1` and `CSDT_V2` are one Moto old/new business flow, not duplicate student sources.
- Evidence-backed direction is still `CSDT_V1 -> CSDT_V2`.
- `CSDT_V2 -> CSDT_V1` is not approved for writes. It is only being analyzed read-only.

## Files inspected

- `docs/task5-b3w26-b3w28-moto-old-new-sync-design.md`
- `docs/task5-b3w29-b3w31-moto-v1-v2-readonly-diagnostics.md`
- `database/TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql`
- `database/reference/V1_schema_full.sql`
- `database/reference/V2_schema_full.sql`

## SQL diagnostic script

Created:

- `database/diagnostics/task5_b3w32_b3w34_moto_v2_to_v1_feasibility.sql`

The script starts with:

```sql
USE [CSDT_V2];
GO
```

The script reads `CSDT_V1` and `CSDT_V2` only. It does not create permanent objects and does not modify data.

## Expected result sets

The script returns:

1. V2-only `NguoiLX_HoSo` rows grouped by:
   - `MaKhoaHoc`;
   - `HangDaoTao`;
   - `TT_XuLy`;
   - min/max `NgayNhanHSo`.
2. V2-only `NguoiLX` summary.
3. Sanitized V2-only `NguoiLX` sample:
   - `MaDK`;
   - masked/preview name only;
   - `GioiTinh`;
   - `NgaySinh`;
   - `SoCMTLength`, not raw `SoCMT`.
4. Dependency summary for V2-only `HoSo`:
   - whether `MaDK` exists in V1 `NguoiLX`;
   - whether `MaKhoaHoc` exists in V1 `KhoaHoc`;
   - whether related V1 `BaoCaoI` exists by `MaKH`;
   - whether V2 `NguoiLXHS_GiayTo` exists and may need review.
5. Dependency details grouped by course/rank/status.
6. Same-name field compatibility for required tables.
7. V2 fields with greater declared length than V1.
8. V2-only fields that cannot be written to V1 without explicit mapping.
9. Read-only would-add/review counts for a possible V2 -> V1 plan.
10. Same-key rows that would need change review, not automatic write.

## Feasibility conclusion

Current conclusion: V2 -> V1 is not yet approved and not yet safe to implement as a write path.

It may be technically feasible for a narrow TEST-only scenario if diagnostics show:

- the 200 V2-only `HoSo` rows have all required V1 dependencies;
- missing V1 courses/reports can be explained;
- V2-only fields can be ignored or mapped safely;
- values longer than V1 columns are absent or manually handled;
- status semantics are confirmed.

Until those are confirmed, the only safe next action is to run the diagnostic script manually on TEST/local and review
the result sets.

## Tables likely required if owner approves later

Minimum likely set for V2-only learner/HoSo backflow:

- `KhoaHoc`, only if V2-only `HoSo.MaKhoaHoc` is missing in V1.
- `BaoCaoI`, if V1 workflow requires a report row for the course.
- `NguoiLX`, when V2 `MaDK` is missing in V1.
- `NguoiLX_HoSo`, when V2 `MaDK` is missing in V1 `HoSo`.
- `NguoiLXHS_GiayTo`, if dossier document metadata must follow the HoSo.

Tables not included in the first feasibility write scope:

- `NguoiLX_GPLX`: GPLX/sat hach semantics are higher risk and need separate review.
- `KhoaHoc_GiaoVien`, `LichHoc`, `KhoaHoc_XeTap`: training schedule/vehicle data needs its own mapping decision.

## Main risks

- V2-only fields cannot fit V1 because V1 has no matching columns.
- V2 column lengths may be larger than V1 column lengths.
- Known important example: `NguoiLX_HoSo.SoGiayCNTN` is `nvarchar(20)` in V1 and `nvarchar(30)` in V2.
- Status fields may differ in meaning:
  - `TT_XuLy`;
  - `TT_XuLy_Old`;
  - `TrangThai`;
  - V2-specific fields such as DAT/KetLuanCSDT/GiaiTrinh.
- GPLX/sat hach workflow may require fields that are not obvious from the learner/HoSo tables.
- File/photo path fields may point to locations that V1 cannot read.
- Same `MaDK` rows may have changed stable fields and should not be overwritten automatically.
- Back-propagating 2026 data into the old 2025/GPLX database can create operational confusion if V1 is historical.

## Stop conditions

Stop and do not design a write plan if any of these occur:

- The connection is production or cannot be confirmed as TEST/local.
- V2-only rows require V1 `KhoaHoc` rows that are missing and not approved for creation.
- V2-only rows require V1 `BaoCaoI` rows that are missing and not approved for creation.
- Any V2 value exceeds V1 length, especially `SoGiayCNTN`.
- Required V2 fields are V2-only and have no approved V1 destination.
- `TT_XuLy` values are not understood.
- The result indicates same-key rows need updates rather than only new-row handling.
- Document/file/photo path behavior is unclear.
- Owner has not approved an exact TEST-only scenario.

## How to run safely in SSMS

1. Open SSMS on TEST/local SQL Server only.
2. Confirm you are not connected to production.
3. Open `database/diagnostics/task5_b3w32_b3w34_moto_v2_to_v1_feasibility.sql`.
4. Confirm the first lines are:

   ```sql
   USE [CSDT_V2];
   GO
   ```

5. Execute only this read-only diagnostic script.
6. Paste all result sets back for review.
7. Do not run transfer scripts, sync endpoints, or write SQL.

## Next recommended step

Run the feasibility script manually on TEST/local and paste the result sets back.

Only after review should the next task design a read-only plan endpoint or a narrow TEST-only write plan. No write plan
should be created until the owner approves the exact scenario.
