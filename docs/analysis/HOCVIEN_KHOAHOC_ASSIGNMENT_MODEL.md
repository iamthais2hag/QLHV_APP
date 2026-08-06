# Integrated learner/course/group assignment model

## Current fact

Fresh read-only catalog/data evidence on 2026-07-30 confirms `App_HocVien.HocVienId` is one course-registration row. It contains `MaDK`, `MaKhoa`, source identity and `RowVersion`; it has no `KhoaHocId` and no FK to `App_KhoaHoc`. All 169 active rows in that snapshot had a matching active `MaKhoa`, with no duplicate active `(MaDK, MaKhoa)` or source identity. Four learner identities appeared in multiple courses, so assignment must use `HocVienId`, never CCCD.

Counts are evidence only. Concurrent new learners are normal; writes revalidate `HocVienId`, source identity, course business key and RowVersion per record.

## Phase 1 tables

- `App_KhoaHoc_NhomDaoTao`: exact FK to `App_KhoaHoc`, unique `(KhoaHocId, MaNhom)`, default class teacher/training vehicle/figure-10 vehicle.
- `App_HocVien_PhanCong`: exact FK to `App_HocVien`, optional group/dossier receiver/class teacher/two vehicle FKs, full effective-dated snapshots and one current row per `HocVienId`.

No assignment FK is added to source-owned `App_HocVien`.

## Default and override precedence

Putting a learner in a group copies the group's three defaults into a current assignment snapshot. Three field-level override flags distinguish inherited values from individual changes; equality with current group defaults is not sufficient provenance.

Changing group defaults requires preview and one explicit mode: apply to unoverridden fields, replace all/reset overrides, or leave current assignments unchanged. There is no silent propagation.

## History

Every group/teacher/vehicle/receiver change closes the current row and inserts a full new snapshot in one transaction with `NguonGan=MANUAL|EXCEL|BULK|GROUP`, actor and timestamps. NO_CHANGE creates no history row. All FKs are `NO ACTION`; inactive/source-missing masters retain history and surface manual-review warnings.
