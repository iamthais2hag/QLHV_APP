# Triển khai tích hợp khóa học, giáo viên và xe tập lái

Ngày lập: 2026-07-30

Trạng thái: **đã triển khai và xác minh trong workspace**

Production: **MIGRATION NOT APPLIED / WRITER NOT ACTIVATED**

## Phạm vi đã ghép thành một chức năng

Chức năng mới dùng chung một course context và một mô hình phân quyền cho các phần:

- khóa học nguồn `App_KhoaHoc`;
- học viên nguồn `App_HocVien`;
- giáo viên đào tạo nguồn `App_GiaoVien`;
- xe tập lái nguồn `App_XeTap`;
- người nhận hồ sơ nội bộ `App_GiaoVien_hs`;
- nhóm đào tạo `App_KhoaHoc_NhomDaoTao`;
- full-snapshot assignment `App_HocVien_PhanCong`;
- preview/confirm, lịch sử và Excel theo đúng một khóa.

Ba trang `/giao-vien`, `/xe-tap-lai`, `/khoa-hoc` đã được thay bằng màn hình thật và
được nối với chi tiết khóa `/khoa-hoc/:khoaHocId`. Chi tiết khóa có sáu phần: thông
tin khóa, học viên, nhóm, giáo viên và xe, Excel, lịch sử.

## Ownership

| Dữ liệu | Chủ sở hữu | Quy tắc triển khai |
|---|---|---|
| `App_HocVien`, `App_GiaoVien`, `App_XeTap`, `App_KhoaHoc` | CSDL nguồn/realtime | API assignment chỉ đọc; repository assignment không có DML vào các bảng này |
| `App_KhoaHoc_GiaoVien`, `App_KhoaHoc_XeTap` | CSDL nguồn/realtime | chỉ dùng làm evidence/usage, không sửa qua UI mới |
| `App_GiaoVien_hs` | QLHV | CRUD có RowVersion, reason, actor, soft delete và history |
| `App_KhoaHoc_NhomDaoTao` | QLHV | quản lý nhóm/default, không hard delete |
| `App_HocVien_PhanCong` | QLHV | close-and-insert full snapshot, không overwrite history |
| assignment import metadata trong `App_ImportBatch` | QLHV | file hash, version, expiry, idempotency và trạng thái confirm |
| operation ledger trong `App_AssignmentOperation` | QLHV | global idempotency hash, exact scope/payload, committed result và retention |

Migration assignment tạo explicit `DENY INSERT, UPDATE, DELETE` cho realtime service
trên các bảng QLHV-owned và phần assignment của `App_ImportBatch`. Writer vehicle chỉ
có allow-list field nguồn trên `App_XeTap`.

## Identity xuyên suốt

Identity bắt buộc là:

`KhoaHocId + SourceProfileCode + MaKhoa + HocVienId + MaDK + RowVersion`

`SourceProfileCode` dùng đúng mã hệ thống `CSDT_OTO` hoặc `CSDT_MOTO`. Không chấp nhận
alias `OTO`/`MOTO`, không match toàn hệ thống chỉ bằng `MaDangKy + MaKhoa`, và không
match theo tên. Giáo viên và xe cũng được lookup trong đúng source profile của khóa.

Xe nguồn dùng identity `(SourceProfileCode, SourceBienSoXe)`. Kết quả nghiên cứu,
mapping, collision/lifecycle và rehearsal riêng nằm trong
`VEHICLE_REALTIME_MAPPING_IMPLEMENTATION.md`.

## Nguyên tắc production đang nhập liên tục

Thiết kế không dùng count cố định làm guard. Preview materialize tối đa 5.000 target
theo exact identity. Confirm chỉ khóa và revalidate các target đã seal:

- course RowVersion;
- learner identity và learner RowVersion;
- current assignment id/RowVersion/full state;
- group defaults RowVersion/full state và exact member set khi có propagation;
- trạng thái active và source profile của từng reference.

Học viên mới được nhập sau preview không làm giao dịch cũ thất bại chỉ vì tổng số thay
đổi. Nếu chính target hoặc reference của target thay đổi, confirm fail-closed toàn bộ.
Nếu học viên được thêm/bớt/chuyển vào chính nhóm đang propagation sau preview, exact member
set không còn khớp và confirm cũng fail-closed để không silently omit thành viên.

## Thành phần chính

- Application: `server/QLHV.Application/Assignments/`
- SQL repository: `server/QLHV.Infrastructure/Assignments/`
- API: `server/QLHV.Api/Controllers/AssignmentControllers.cs`
- UI: `client/src/features/course-assignment/`
- assignment migration/rollback/rehearsal: `database/patches/20260730_*integrated_course_assignment*`
- vehicle realtime: `server/QLHV.Application/Sync/VehicleRealtime/` và
  `server/QLHV.Infrastructure/Sync/VehicleRealtime/`
- focused tests: `server/QLHV.Tests/Assignments/`

## Trạng thái xác minh

- focused assignment hiện tại: **85 PASS, 0 FAIL**;
- focused assignment + schema Debug: **90 PASS, 0 FAIL**;
- focused migration + vehicle: **27 PASS, 0 FAIL**;
- explicit 5.000-row import/set-based checks: **3 PASS, 0 FAIL**;
- broad backend, loại toàn bộ RT-02/RT-03 và opt-in suites: **1.200 PASS, 1 SKIP, 0 FAIL**;
- frontend lint: **PASS**;
- frontend production build: **PASS**;
- backend Release build: **PASS**, 0 error; còn một advisory `NU1902` có sẵn cho
  `Magick.NET-Q16-AnyCPU 14.14.0`;
- isolated assignment migration/rollback rehearsal: **PASS**, gồm global single-flight,
  rollback/retry idempotency và cleanup disposable database;
- isolated vehicle migration/rollback rehearsal: **PASS**, populated rollback bị chặn và data
  được giữ nguyên;
- final read-only production safety audit: **BLOCKED BY EXTERNAL REALTIME STATE** lúc
  `2026-07-30T15:16:08Z`: assignment objects `0/4`, import columns `0/6`, vehicle control
  objects `0/3`, vehicle mapping columns `0/23`, CT xe OFF, Auto Sync active
  run/slot/operation `0/0/0`, nhưng service đang `Stopped` và worker state là
  `BLOCKED / RT03_UNSUPPORTED_DRIFT`. Task không restart hoặc sửa production.

Focused suite bao phủ Excel, preview store, persistent concurrent idempotency, exact profile identity,
SQL ownership, snapshot/concurrency, routes/policies và UI. Broad run chủ động loại RT-02/RT-03,
production opt-in, photo benchmark và isolated SQL để không tác động production.

Không có migration assignment/vehicle nào được áp dụng lên `QLHV_APP`; không bật CT xe,
không tạo vehicle checkpoint, không khởi động writer, không chạy Auto Sync và không restart
realtime service trong phần triển khai này.
