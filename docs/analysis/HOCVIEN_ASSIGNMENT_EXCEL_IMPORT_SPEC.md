# Final Excel import contract

## Key headers

`MaDangKy`, `MaKhoa`, `MaNhom`, `MaGiaoVienHoSo`, `MaGiaoVienDungLop`, `BienSoXeTap`, `BienSoXeBaiSo10` must exist in the V2 template. `MaDangKy` and `MaKhoa` require values; assignment keys are conditional according to explicit KEEP/SET/CLEAR/INHERIT actions.

Matching is exact and read-only:

- registration: `MaDangKy+MaKhoa`;
- group: `MaKhoa+MaNhom`;
- dossier receiver: `MaGiaoVienHs`;
- class teacher: `MaGV`;
- vehicles: unique version-normalized plate.

No learner/teacher/vehicle is matched by name only, and text never auto-creates `App_GiaoVien`, `App_GiaoVien_hs` or `App_XeTap`.

Preview statuses are `READY`, `NO_CHANGE`, `NOT_FOUND`, `AMBIGUOUS`, `INACTIVE_REFERENCE`, `INVALID`, `CONFLICT`. Confirm requires operator authorization, unchanged file SHA/template/normalization version, unexpired preview, per-record identity/course/group/RowVersion revalidation, one transaction, exact affected rows and idempotency. NO_CHANGE writes no snapshot.

The existing QLHV-owned `App_ImportBatch` is extended as the import-session header; `App_HocVien_PhanCong.ImportSessionId` references its `ImportBatchId`. Audit records file SHA, actor, time and status counts without raw PII/file content.

Machine contracts: `handoff/HOCVIEN_ASSIGNMENT_REVIEW/05_IMPORT_TEMPLATE_COLUMNS.csv` and `06_IMPORT_VALIDATION_RULES.json`.
