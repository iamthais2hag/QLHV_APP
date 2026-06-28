# Task 5 B3W29-B3W31 - Moto CSDT_V1 vs CSDT_V2 read-only diagnostics

## Scope

This package prepares read-only diagnostics for the Moto old/new data flow before any sync implementation.

Current TEST meaning:

- `CSDT_V1`: Moto old 2025/GPLX test database.
- `CSDT_V2`: Moto new 2026 test database.
- `QLHV_APP_TEST`: QLHV_APP application test database.

Important concept:

- `CSDT_V1` and `CSDT_V2` are one Moto old/new business flow, not two parallel learner sources to duplicate in QLHV_APP.
- Evidence-backed direction for the next work is `CSDT_V1 -> CSDT_V2`.
- `CSDT_V2 -> CSDT_V1` is read-only analysis only for now.
- Oto is out of scope for this package.

No SQL was run by Codex for this task.

## Files inspected

- `docs/task5-b3w26-b3w28-moto-old-new-sync-design.md`
- `docs/task5-b3w26-b3w28-moto-old-new-sync-implementation-plan.md`
- `docs/sql-scripts-index.md`
- `database/TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql`
- `database/CHECK_CCCD_CONFLICT_V1_V2.sql`
- `database/V2_POST_TRANSFER_HEALTH_CHECK.sql`
- `database/reference/V1_schema_full.sql`
- `database/reference/V2_schema_full.sql`

## Scripts created

Run these manually only in SSMS against TEST/local databases:

1. `database/diagnostics/task5_b3w29_b3w31_moto_v1_v2_schema_compare.sql`
2. `database/diagnostics/task5_b3w29_b3w31_moto_v1_v2_row_key_diagnostics.sql`

Both scripts start with:

```sql
USE [CSDT_V2];
GO
```

The scripts read `CSDT_V1` and `CSDT_V2`. They do not modify either source database.

The row/key script uses a local `#KeyDiagnostics` temp table only to organize report rows. It does not create permanent
objects and does not write source data.

## Candidate tables

- `KhoaHoc`
- `BaoCaoI`
- `NguoiLX`
- `NguoiLX_HoSo`
- `NguoiLX_GPLX`
- `NguoiLXHS_GiayTo`
- `KhoaHoc_GiaoVien`
- `LichHoc`
- `KhoaHoc_XeTap`

## Safe SSMS run instructions

1. Open SSMS and connect only to the TEST/local SQL Server instance.
2. Confirm the target databases are the test databases:

   ```sql
   SELECT DB_NAME() AS CurrentDatabase;
   SELECT name FROM sys.databases WHERE name IN (N'CSDT_V1', N'CSDT_V2');
   ```

3. Open `database/diagnostics/task5_b3w29_b3w31_moto_v1_v2_schema_compare.sql`.
4. Confirm the file starts with `USE [CSDT_V2];`.
5. Execute only if you are not connected to production.
6. Copy all result sets into a review note or paste them back to Codex.
7. Repeat for `database/diagnostics/task5_b3w29_b3w31_moto_v1_v2_row_key_diagnostics.sql`.
8. Do not run any sync, transfer, execute endpoint, or write script in this phase.

## Expected output to paste back

### Schema comparison script

Paste these result sets:

1. Table existence: `TableName`, `ExistsInV1`, `ExistsInV2`.
2. Column metadata: database, table, column, data type, length, nullability, ordinal.
3. Columns only in V1.
4. Columns only in V2.
5. Same-name columns with type/length/nullability differences.
6. PK/UQ/index discovery for the candidate tables.

### Row/key diagnostics script

Paste these result sets:

1. Row counts by candidate table.
2. Key diagnostics:
   - missing key rows;
   - duplicate key groups;
   - duplicate rows;
   - overlap count;
   - key confidence notes.
3. Cross-table diagnostics:
   - same CCCD/SoCMT between V1 and V2 but different `MaDK`;
   - V2 `NguoiLX_HoSo` without matching `NguoiLX`;
   - V2 `NguoiLX_HoSo` without matching `KhoaHoc`;
   - V2 `NguoiLX_GPLX` without matching `NguoiLX`.

## Key assumptions

High-confidence keys from schema references:

- `KhoaHoc`: `MaKH`
- `BaoCaoI`: `MaBCI`
- `NguoiLX`: `MaDK`
- `NguoiLX_HoSo`: `MaDK`
- `NguoiLXHS_GiayTo`: `MaGT + MaDK`

Medium/uncertain keys that need review:

- `NguoiLX_GPLX`:
  - schema primary key is `MaDK`;
  - existing V1 -> V2 transfer script uses functional key `MaDK + SoGPLX + HangGPLX`.
- `KhoaHoc_GiaoVien`:
  - V1 schema key is `MaKH + MaGV`;
  - V2 schema uses identity `MaLichLV`;
  - existing transfer script uses `MaKH + MaGV + LoaiGV + BienSoXe`.
- `LichHoc`:
  - schema key is identity `MaLichHoc`;
  - existing transfer script uses `MaKH + Thang + Tuan + TuNgay + DenNgay`.
- `KhoaHoc_XeTap`:
  - appears as V2 course-vehicle assignment;
  - existing transfer script can generate it from V1 `KhoaHoc_GiaoVien.BienSoXe`;
  - no V1 `KhoaHoc_XeTap` table was found in the reference schema.

## Stop conditions

Stop and do not proceed to mapping/write design if any of these occur:

- The connection is production or cannot be confirmed as TEST/local.
- `CSDT_V1` or `CSDT_V2` is missing.
- Required candidate tables are missing unexpectedly.
- High-confidence keys show duplicate groups.
- High-confidence keys have missing key rows.
- Same CCCD/SoCMT appears in both databases with different `MaDK`.
- V2 has orphan `NguoiLX_HoSo` or `NguoiLX_GPLX` rows.
- Same-name key/status/date columns have incompatible type/length/nullability changes.
- Row counts are unexpectedly large, unexpectedly zero, or inconsistent with the owner's known test data.
- Any result implies destructive overwrite would be needed.

## How results feed the next phase

The result sets should drive the next implementation design:

1. Confirm which tables are safe for read-only mapping first.
2. Confirm exact keys per table before any dry-run plan endpoint.
3. Identify tables that should remain preview-only because their keys are uncertain.
4. Decide whether `KhoaHoc`, `BaoCaoI`, and training schedule tables can be planned before learner/person tables.
5. Create a future dry-run plan that reports inserts/updates/skips/conflicts per table, still with no writes.

## Open questions

- Is `CSDT_V1 -> CSDT_V2` the only write direction needed for Moto, or is any V2 -> V1 update truly required?
- For `BaoCaoI`, should business identity be `MaBCI`, `SoBaoCao`, or both?
- For `NguoiLX_GPLX`, should sync follow schema key `MaDK` or functional key `MaDK + SoGPLX + HangGPLX`?
- How should same CCCD/SoCMT with different `MaDK` be handled?
- Which `TrangThai`, `TT_XuLy`, and `TT_Xuly` values are authoritative?
- Should file/path fields in `NguoiLXHS_GiayTo` be copied, ignored, or rewritten?
- Should `KhoaHoc_XeTap` be generated from `KhoaHoc_GiaoVien`, or should it only sync when the source table exists?
- Does `MaCSDT` or other center code need remapping between V1 and V2?

## Safety reminder

Do not run production. Do not run sync. Do not call `/execute`. Do not enable `EnableTargetWrites`.

This package only prepares read-only diagnostics.
