# RT03 KhoaHoc business event implementation

Ngày: 2026-07-30  
Trạng thái: workspace implementation, tests và deployment package đã xác minh;
production chưa deploy.

## Mục tiêu

Giải quyết exact blocker `CSDT_OTO / CT26 / dbo.KhoaHoc / INSERT` bằng hội tụ
business row thực sự. Không dùng “operator approve drift” chung chung, không retain
manual review và không advance checkpoint nếu course chưa commit/verify.

## Mapping được dùng

Implementation dùng mapper full-sync hiện hữu làm nguồn chân lý duy nhất:

`QlhvImportCourseTeacherMapper.MapKhoaHoc`

| Source `dbo.KhoaHoc` | Target `dbo.App_KhoaHoc` | Ghi chú |
|---|---|---|
| profile route | `SourceProfileCode` | chỉ `CSDT_OTO`/`CSDT_MOTO` |
| `MaKH` | `SourceMaKhoaHoc`, `MaKhoa` | trim; identity partitioned |
| `TenKH` | `TenKhoa` | source-owned |
| `MaCSDT` | `MaCSDT` | source-owned |
| `MaSoGTVT` | `MaSoGTVT` | source-owned |
| `HangGPLX` | `HangGPLX` | không trộn với HangDT |
| `HangDT` | `HangDaoTao` | source-owned |
| `SoQD_KhaiGiang` | `SoQuyetDinhKhaiGiang` | source-owned |
| `NgayQD_KhaiGiang` | `NgayQuyetDinhKhaiGiang` | target DATE |
| `NgayKG` | `NgayKhaiGiang` | target DATE |
| `NgayBG` | `NgayBeGiang` | target DATE |
| `MucTieuDT` | `MucTieuDaoTao` | source-owned |
| `NgayThi` | `NgayThi` | target DATE |
| `NgaySH` | `NgaySatHach` | không phải NgayBatDauThucHanh |
| `TongSoHV` | `TongSoHocVien` | source-owned |
| `SoHVTotNghiep` | `SoHocVienTotNghiep` | source-owned |
| `SoHVDuocCapGPLX` | `SoHocVienDuocCapGPLX` | source-owned |
| `ThoiGianDT` | `ThoiGianDaoTao` | source-owned |
| `SoNgayOnKT` | `SoNgayOnKiemTra` | source-owned |
| `SoNgayThucHoc` | `SoNgayThucHoc` | source-owned |
| `SoNgayNghiLe` | `SoNgayNghiLe` | source-owned |
| `TongSoNgay` | `TongSoNgay` | source-owned |
| `GhiChu` | `GhiChuV2` | source note |
| `TrangThai` | `TrangThaiNguon` | false được hỗ trợ rõ ràng |
| `TT_Xuly` | `TtXuLy` | source-owned |
| `HTDaoTao` | `HinhThucDaoTao` | source-owned |
| canonical projection | `SourceHash`, `V2RowHash` | cùng SHA-256 |

Source metadata `NguoiTao`, `NguoiSua`, `NgayTao`, `NgaySua` được nhận diện trong CT
catalog nhưng không ghi vào target audit. `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy`
của target theo SQL/API server UTC.

Target-only được giữ nguyên khi update:

- `GhiChuNoiBo`;
- `TrangThai`;
- `NgayBatDauThucHanh`;
- `LuuLuongDaoTao`;
- created audit;
- group/student/teacher/vehicle assignments.

Source không có course field `LoaiHinhDaoTao`; không tạo mapping suy đoán.

## Identity và action

Identity duy nhất:

`(SourceProfileCode, SourceMaKhoaHoc)`

`MaKhoa` không phải global identity. SQL collation xử lý equality tại query target;
mapper trim source key trước khi plan.

| Exact target | Action |
|---|---|
| 0 | INSERT |
| 1, same hash và exact projection | NO_CHANGE |
| 1, different hash hoặc soft-deleted | UPDATE source-owned + reactivate existence |
| >1 | `BLOCKED_AMBIGUOUS_IDENTITY` |

Legacy unpartitioned collision và same-profile `MaKhoa` collision cũng block.
Cùng `MaKhoa` ở OTO và MOTO được phép qua composite source identity.

## Schema prerequisite

File:

`database/patches/20260730_rt03_support_khoahoc_business_identity.sql`

Patch chưa chạy. Nó:

1. xác minh production DB ID/GUID;
2. xác minh `UX_App_KhoaHoc_SourceIdentity` và không có duplicate;
3. drop `UQ_App_KhoaHoc_MaKhoa` toàn cục;
4. tạo lookup index `(SourceProfileCode, MaKhoa)`;
5. thay fixed one-row cycle-history check bằng nonnegative counts, delete=0,
   duplicate=0, checkpoint monotonic.

Rollback:

`database/patches/20260730_rollback_rt03_khoahoc_business_identity.sql`

Rollback không xóa data. Nó block nếu cross-profile duplicate hoặc multi-row cycle
history khiến schema cũ không thể khôi phục.

## Classifier

Course INSERT chỉ supported khi `SYS_CHANGE_COLUMNS=NULL`/empty.

Course UPDATE chỉ supported khi mask khác rỗng và mọi column thuộc exact 29-column
catalog. CT query duyệt `sys.columns`; một cột mới được phát thành sentinel
`__UNCLASSIFIED_FORWARD_COLUMN__` và classifier trả:

`UNCLASSIFIED_FORWARD_COLUMN`

DELETE vẫn `UNKNOWN_UNSAFE` và block. Explicit inactive dùng source
`TrangThai=false`, ghi `TrangThaiNguon=false` nhưng không biến source row đang tồn tại
thành hard delete.

## Processor

Processor hỗ trợ một hoặc nhiều course events trong exact next CT version, kể cả
course + learner trong cùng batch:

1. seal next CT version;
2. classify từng course event;
3. read exact current source row;
4. map bằng shared mapper;
5. read exact target + same-MaKhoa collision evidence;
6. lập immutable action/hash/rowversion/QLHV-owned fingerprint;
7. mở serializable profile transaction;
8. revalidate source hash và target rowversion;
9. apply course operations trước;
10. verify exact projection/hash/QLHV-owned fingerprint;
11. trước learner INSERT, require đúng một active course exact identity;
12. apply learner, preserve assignment-owned fields;
13. verify duplicate/owned invariants;
14. insert apply marker và commit;
15. publish checkpoint sau committed marker.

Source biến mất hoặc thay đổi sau plan trả
`RT03_SOURCE_CHANGED_DURING_PLAN`; worker retry cycle mà không publish checkpoint.
Một event source mới sau sealed version vẫn là backlog của cycle sau.

## Learner replay

Learner INSERT vẫn match bằng `(profile, SourceMaDK)`, không theo tên hoặc CCCD.
Nếu exact source/target identity đã tồn tại, active và cùng mapped hash, replay là
`NO_CHANGE` và checkpoint có thể advance mà không tạo row thứ hai. Hash drift không
được silently advance.

Dependency error:

`BLOCKED_LEARNER_COURSE_IDENTITY`

Nó xảy ra khi course missing, ambiguous, inactive, soft-deleted hoặc raw `MaKhoa`
không khớp `SourceMaKhoaHoc`.

## Behavioral coverage

Các behavior bắt buộc đã được kiểm thử:

1. OTO course INSERT;
2. MOTO course INSERT;
3. INSERT NULL mask;
4. replay NO_CHANGE;
5. different hash UPDATE;
6. cùng MaKhoa xuyên profile không collision;
7. duplicate exact identity block;
8. source disappear/change block;
9. QLHV-owned fingerprint preserved;
10. checkpoint recovery chỉ publish từ verified committed marker;
11. course trước learner;
12. learner missing/inactive course block;
13. sáu learner replay không duplicate;
14. new source hash invalidates sealed plan để cycle sau xử lý;
15. planner không dùng fixed historical counts;
16. photo retention regression;
17. clock-skew fail closed regression;
18. AssignmentFocused regression.

Kết quả:

- RT03: 72/72 PASS;
- TimeAuthority: 8/8 PASS;
- AssignmentFocused: 88/88 PASS;
- Release build: PASS;
- diff check: PASS.

## File implementation chính

- `server/QLHV.Application/Sync/Rt03/Rt03CourseBusinessRules.cs`
- `server/QLHV.Application/Sync/Rt03/Rt03ChangeTrackingEventClassifier.cs`
- `server/QLHV.Application/Sync/Rt03/Rt03ProductionContracts.cs`
- `server/QLHV.Application/Sync/Rt03/Rt03ProductionSql.cs`
- `server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeCycleProcessor.cs`
- `server/QLHV.Tests/Sync/Rt03/Rt03CourseBusinessEventTests.cs`
- `server/QLHV.Tests/Sync/Rt03/Rt03ChangeTrackingEventClassifierTests.cs`
- `server/QLHV.Tests/Sync/Rt03/Rt03SqlTemplateSafetyTests.cs`

## Production boundary

Exact binary/SQL hashes, operator commands, rollback và verification checklist:

`handoff/RT03_KHOAHOC_BUSINESS_EVENT_20260730/OPERATOR_RUNBOOK.md`

Production chưa được deploy/restart/migrate.

## 2026-07-31 deployment prerequisite correction

The first production attempt did not deploy runtime code. It stopped at the
schema prerequisite with SQL Server Msg 1934 because the raw `sqlcmd -E` session
had `QUOTED_IDENTIFIER=0`. The transaction rolled back completely.

The prerequisite and rollback now declare and assert the full SQL SET-option
contract in the DDL batch, fail closed on non-exact schema, and have passed a
25-step disposable rehearsal using the production `sqlcmd -E -b -i` execution
path. The 20260730 package is superseded by:

`handoff/RT03_KHOAHOC_BUSINESS_EVENT_20260731_V2/OPERATOR_RUNBOOK.md`

No production DDL, binary copy, service start, checkpoint/state change or
assignment migration was performed during the correction task.

`READY FOR OPERATOR RE-APPROVAL — RT03 SCHEMA PREREQUISITE CORRECTED AND REHEARSED`
