# RT03 unsupported-drift recovery audit — 2026-07-30

## Kết luận hiện tại

## 2026-07-31 schema prerequisite correction

The first approved deployment attempt stopped at the schema prerequisite before
any binary copy or service start. SQL Server returned Msg 1934 because the raw
`sqlcmd -E` session had `QUOTED_IDENTIFIER=0` while creating an index on
`dbo.App_KhoaHoc`, which already has a filtered index. The DDL transaction rolled
back fully and the production baseline remained unchanged.

The corrected prerequisite and matching rollback now establish and assert the
full required SET-option contract in the DDL batch, classify exact baseline versus
exact migrated state, and block every other schema definition. Same-path
disposable rehearsal passed 25/25, including negative session preflight, forced
mid-migration rollback, unsafe rollback blocks and exact final baseline cleanup.

The 20260730 handoff is superseded. A new explicit approval must use:

`handoff/RT03_KHOAHOC_BUSINESS_EVENT_20260731_V2/OPERATOR_RUNBOOK.md`

Full evidence:

`docs/analysis/RT03_SCHEMA_PREREQUISITE_CORRECTION_20260731.md`

`READY FOR OPERATOR RE-APPROVAL — RT03 SCHEMA PREREQUISITE CORRECTED AND REHEARSED`

Đây là kết luận pre-deployment. Production chưa được sửa: không DDL/DML, không copy
binary, không start/restart service, không đổi checkpoint, worker state, CT, clock
hay Auto Sync.

## Ownership đã được chốt

Operator xác nhận:

- `CSDL_OTO/CSDL_MOTO.dbo.KhoaHoc` là source-owned;
- `QLHV_APP.dbo.App_KhoaHoc` là mirror;
- course INSERT/UPDATE phải hội tụ, không được retain/skip rồi advance checkpoint;
- learner INSERT chỉ được commit khi course exact identity đã tồn tại, active và được
  xác minh.

Vì vậy CT26 `dbo.KhoaHoc/I` không còn là ownership chưa xác định và không được chuyển
thành manual review.

## Baseline production trước implementation

Fresh evidence của recovery task ngay trước implementation:

| Thuộc tính | Baseline |
|---|---|
| Service | `QLHV_APP_RealtimeWorker`, `Stopped`, PID 0 |
| Worker state | `BLOCKED / RT03_UNSUPPORTED_DRIFT` |
| OTO checkpoint | 25 |
| First pending event | CT26, `dbo.KhoaHoc`, `I`, `SYS_CHANGE_COLUMNS=NULL` |
| Source row | tồn tại |
| Exact target course | 0 |
| Learner phía sau | 6 source INSERT chưa có target match tại thời điểm audit |
| Photo retention | 8 marker đã commit, retained active, target not mutated |
| Auto Sync | OFF |

Baseline trên không được dùng làm fixed-count completion gate. Backlog/current CT có
thể tăng trước lúc operator deploy. Fresh production audit chỉ được chạy sau approval.

## Exact mapping audit

Hai source `CSDL_OTO.dbo.KhoaHoc` và `CSDL_MOTO.dbo.KhoaHoc` có cùng schema 29
columns. Implementation tái sử dụng
`QlhvImportCourseTeacherMapper.MapKhoaHoc`; không tạo mapping RT03 song song.

Projection source-owned đã chứng minh:

- identity: `SourceProfileCode + SourceMaKhoaHoc`, với `MaKH` được trim thành
  `SourceMaKhoaHoc` và `MaKhoa`;
- tên/hạng: `TenKH -> TenKhoa`, `HangDT -> HangDaoTao`,
  `HangGPLX -> HangGPLX`;
- tổ chức/quyết định: `MaCSDT`, `MaSoGTVT`, `SoQD_KhaiGiang`,
  `NgayQD_KhaiGiang`;
- ngày: `NgayKG -> NgayKhaiGiang`, `NgayBG -> NgayBeGiang`,
  `NgayThi`, `NgaySH -> NgaySatHach`;
- mục tiêu/số lượng/thời lượng: `MucTieuDT`, `TongSoHV`, các số tốt nghiệp/cấp
  GPLX và các số ngày;
- lifecycle: `TrangThai -> TrangThaiNguon`, `TT_Xuly -> TtXuLy`,
  `HTDaoTao -> HinhThucDaoTao`;
- source note: `GhiChu -> GhiChuV2`;
- canonical mapper hash được ghi đồng thời vào `SourceHash` và `V2RowHash`.

Không map `NgaySH` sang `NgayBatDauThucHanh`. Source không có course column
`LoaiHinhDaoTao` hoặc `LuuLuongDaoTao`; các target fields này không được suy đoán.
`NguoiTao/NguoiSua/NgayTao/NgaySua` là source operational metadata không có cột
mirror tương ứng; target created/updated audit dùng database/API UTC.

QLHV-owned fields được fingerprint trước apply và xác minh không đổi:
`GhiChuNoiBo`, `TrangThai`, `NgayBatDauThucHanh`, `LuuLuongDaoTao`,
created audit và assignment-owned data.

## Identity và schema prerequisite

Authoritative identity là `(CSDT_OTO|CSDT_MOTO, exact MaKH)`.

Business rules:

- 0 exact target: `INSERT`;
- 1 exact target, cùng hash và projection: `NO_CHANGE`;
- 1 exact target khác hash hoặc soft-deleted: source-owned `UPDATE`;
- hơn 1 exact target, legacy unpartitioned collision hoặc same-profile natural-key
  collision: `BLOCKED_AMBIGUOUS_IDENTITY`.

Production hiện có `UX_App_KhoaHoc_SourceIdentity` đúng contract nhưng còn
`UQ_App_KhoaHoc_MaKhoa` toàn cục. Unique này cản cùng `MaKhoa` ở OTO/MOTO.
Create-not-run patch:

`database/patches/20260730_rt03_support_khoahoc_business_identity.sql`

Patch:

- xác minh exact DB ID/GUID và source identity index;
- drop global `MaKhoa` unique;
- tạo non-unique profile/MaKhoa lookup index;
- bỏ giới hạn lịch sử cycle “tối đa một mutation” nhưng giữ nonnegative,
  delete=0, duplicate=0 và checkpoint monotonic.

Rollback fail closed nếu đã có cross-profile `MaKhoa` duplicate hoặc multi-row cycle
history. Hai script chưa được chạy.

## Classifier fail-closed

Supported:

- `KhoaHoc I` chỉ khi CT mask rỗng/NULL;
- `KhoaHoc U` chỉ khi mọi changed column thuộc catalog 29 columns đã review;
- explicit inactive qua `TrangThaiNguon=false`;
- replay idempotent.

Blocked:

- DELETE;
- source row biến mất/đổi hash sau immutable plan;
- ambiguous identity;
- bất kỳ forward column ngoài catalog:
  `UNCLASSIFIED_FORWARD_COLUMN`.

Next-change SQL đọc `sys.columns` theo mask nên cột mới không bị bỏ sót im lặng.
Photo-retention classifier cũ giữ nguyên.

## Transaction/checkpoint contract

Trong một serializable profile transaction:

1. revalidate target DB identity, feature state, profile lock, Auto Sync exclusion và
   current checkpoint;
2. re-read current source course và so immutable source hash;
3. khóa/re-read exact target identity;
4. plan lại `INSERT/UPDATE/NO_CHANGE`;
5. ghi fixed parameterized source-owned columns;
6. verify exact target projection/hash và QLHV-owned fingerprint;
7. resolve course trước learner INSERT;
8. verify duplicate/QLHV-owned learner invariants;
9. ghi apply marker;
10. commit.

Checkpoint được publish trong transaction riêng chỉ khi committed marker exact
cycle/plan/version tồn tại. Crash sau commit nhưng trước publish được recovery bằng
marker; không replay business mutation.

## Regression

Kết quả pre-deployment:

- focused KhoaHoc/classifier: nằm trong RT03 suite, PASS;
- RT03: `72/72 PASS`;
- clock authority: `8/8 PASS`;
- AssignmentFocused: `88/88 PASS`;
- backend Release build: PASS, 0 error;
- `git diff --check`: PASS; chỉ có cảnh báo line-ending sẵn có;
- RT-02 production harness: không chạy.

Release build còn advisory Magick.NET đã có từ trước; không phát sinh compile warning
từ implementation này.

## Deployment artifact

Package riêng:

`D:\QLHV_APP\handoff\RT03_KHOAHOC_BUSINESS_EVENT_20260730`

Không rebuild hoặc thay
`D:\QLHV_APP\handoff\HOCVIEN_ASSIGNMENT_IMPLEMENTATION_PACK.zip`.

| File | Deployed SHA-256 | Tested package SHA-256 |
|---|---|---|
| `QLHV.Worker.dll` | `8C192DA082DF625DAB193F4242DCF9E79BEFD71C7716CA823EEC8EEB56003AD1` | `8158D983B6D6FAFFF964FF32FD4312D5534A7FB13D02105433C78028B504E8F1` |
| `QLHV.Infrastructure.dll` | `AF9A13EE34570EE3E20805D1812E4ABCD75A63C5F4CA1566796739560603CE68` | `4F923BE76821EBFE573D168C11613BBEFF2B8406E4D8E08B338304D850396D0C` |
| `QLHV.Application.dll` | `B0A72C1064B6098AF7EC31D4EA37B6C04F63D4368BF5DC9C285CE7BEFAD0002C` | `13A027A5331F80C6493C93BC0C4767635A6A4D65722E07A69B271B2DA164841D` |

Schema patch hash:
`F68A4CA81D6D0F53437B72CF95096F5AE93532EF1E4C7DED6607B5D6515AC212`.

Rollback patch hash:
`A1E4F05F5B57F91A4BBB2C4F608309F40F9B53F80C42E2E0C65A1BB12F3D9DB0`.

Exact commands, rollback boundary, checkpoint expectation và post-restart checklist
nằm trong package `OPERATOR_RUNBOOK.md`.

## Production boundary

Task này chỉ chuẩn bị code/test/package. Chưa có quyền deploy/restart/DDL/DML.
Sau operator approval phải:

- backup exact deployed DLLs;
- apply exact schema prerequisite;
- copy đúng ba tested DLLs;
- start đúng service và xác minh một PID;
- fresh read-only audit course, learner, checkpoint, markers, duplicate, QLHV-owned,
  photo retention, time authority và Auto Sync;
- chỉ sau audit PASS mới rebuild assignment handoff pack và báo SHA-256 mới.

`READY FOR OPERATOR-APPROVED REALTIME DEPLOYMENT AND RESTART`
