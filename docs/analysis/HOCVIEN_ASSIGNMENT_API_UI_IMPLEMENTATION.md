# Triển khai API và UI phân công học viên

Ngày lập: 2026-07-30

Production migration: **NOT APPLIED**

## Backend API

`AssignmentControllers.cs` cung cấp các route:

| Nhóm | Route chính | Khả năng |
|---|---|---|
| Giáo viên nguồn | `GET /api/giao-vien` | search/filter, chỉ đọc |
| Người nhận hồ sơ | `/api/giao-vien-ho-so` | list/create/update/inactive/soft-delete/history |
| Xe nguồn | `GET /api/xe-tap-lai` | search profile/trạng thái/usage, chỉ đọc |
| Khóa | `/api/khoa-hoc` | list/filter và chi tiết assignment |
| Nhóm | `/api/khoa-hoc/{id}/nhom-dao-tao` | create/update/inactive |
| Default nhóm | `/api/nhom-dao-tao/{id}/defaults/{preview|confirm}` | ba propagation mode |
| Assignment | `/api/phan-cong/{preview|confirm}` | single, bulk, group, override, inherit, clear |
| Lịch sử | course/student history routes | audit và full snapshots |
| Excel | course-scoped export/template/preview/confirm/result | all-or-nothing import |

Mọi mutation nhận actor từ authenticated identity và reason từ request. Update trực tiếp
dùng expected RowVersion. Mutation nhiều dòng dùng sealed preview token, target fingerprint,
expiry và idempotency key. Confirm chạy transaction `SERIALIZABLE`, lock/re-read và assert
affected rows; conflict trả HTTP 409.

Idempotency không chỉ nằm trong preview RAM: repository lấy global transaction-owned
application lock và dùng `App_AssignmentOperation` để lưu hash key, exact scope/payload và
committed result trong cùng transaction với mutation. Replay sau API restart trả prior result;
cùng key cho logical request khác trả HTTP 409.

Confirm suy lại yêu cầu quyền bulk từ sealed plan, nên không thể dùng preview bulk sau khi
quyền đã bị thu hồi. Mọi READY/NO_CHANGE target được khóa và revalidate trước mutation.
Propagation `UNOVERRIDDEN_ONLY`/`REPLACE_ALL` còn seal toàn bộ member set của nhóm và so sánh
lại sorted `HocVienId` dưới `SERIALIZABLE + UPDLOCK/HOLDLOCK`; thành viên thêm, bớt hoặc chuyển
nhóm sau preview làm confirm fail-closed. `NO_CURRENT_CHANGE` không propagation nên không cần
seal member set.

## Policies

Chín policy độc lập đã được đăng ký:

1. `Assignment.ViewCatalogs`
2. `Assignment.ManageDossierReceivers`
3. `Assignment.ManageGroups`
4. `Assignment.AssignSingle`
5. `Assignment.AssignBulk`
6. `Assignment.ImportPreview`
7. `Assignment.ImportConfirm`
8. `Assignment.Export`
9. `Assignment.ViewHistory`

Client mirror các capability bằng permission riêng. Viewer chỉ xem catalog/history;
Employee có nghiệp vụ assignment và preview/export; import confirm được giữ cho Admin.
Bulk preview thực hiện thêm authorization check, không suy quyền từ nút UI.

## Frontend

### Giáo viên

Trang `/giao-vien` có hai tab:

- giáo viên đào tạo nguồn: read-only, filter profile/trạng thái, usage và manual-review badge;
- giáo viên hồ sơ: CRUD QLHV-owned, reason, RowVersion, reference count và history.

### Xe tập lái

Trang `/xe-tap-lai` tìm theo biển số/mã hiển thị/số khung, filter exact
`CSDT_OTO`/`CSDT_MOTO`, hiển thị trạng thái, manual-review và số khóa/nhóm/học viên đang
dùng. Không có thao tác sửa source master.

### Khóa học

Trang `/khoa-hoc` filter mã, tên, hạng, hình thức, ngày, trạng thái và source profile.
Source fields được đánh dấu chỉ đọc. Chi tiết khóa gồm sáu section:

1. thông tin khóa;
2. danh sách học viên;
3. nhóm đào tạo;
4. giáo viên và xe;
5. nhập/xuất Excel;
6. lịch sử.

Danh sách học viên có multi-select, select toàn bộ filter, unassigned filter, đưa vào nhóm,
bulk assignment, per-student override và history. Dialog hỗ trợ `KEEP`, `SET`, `CLEAR`,
`INHERIT`; confirm bị khóa nếu preview hết hạn, có conflict/invalid hoặc không có READY.

## Continuous-entry behavior

UI cảnh báo rõ học viên mới không phải lỗi. FILTER selection được server materialize khi
preview. Confirm không so tổng count; chỉ revalidate exact target set và RowVersion.
Thay đổi trên target dẫn tới conflict và yêu cầu refresh/preview lại.
Riêng preview propagation nhóm seal exact membership; thay đổi thành viên của chính nhóm đó
là conflict có chủ đích để không bỏ sót học viên.

## Files

- contracts/rules/service: `server/QLHV.Application/Assignments/`
- repository: `server/QLHV.Infrastructure/Assignments/`
- controllers: `server/QLHV.Api/Controllers/AssignmentControllers.cs`
- pages/components: `client/src/features/course-assignment/`
- route/menu/permissions: `client/src/App.tsx`, `client/src/navigation/menu.ts`,
  `client/src/features/auth/permissions.ts`

Xác minh hoàn tất: source-contract/focused tests PASS, frontend lint PASS, frontend production
build PASS, backend Release build PASS và broad backend regression PASS. Broad command loại
RT-02/RT-03 cùng mọi production/benchmark/isolated opt-in test. Tài liệu này không tuyên bố
production activation; schema production vẫn chưa được migrate.

Rendered audit trên local read-only mock API đã kiểm tra `/khoa-hoc`, đủ sáu section chi tiết,
`/giao-vien` với hai ownership tab và `/xe-tap-lai` với active/inactive manual-review usage.
Không click mutation; browser console không có warning/error.
