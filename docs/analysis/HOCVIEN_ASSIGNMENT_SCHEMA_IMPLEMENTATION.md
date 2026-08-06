# Triển khai schema phân công học viên

Ngày lập: 2026-07-30

Trạng thái: **code/migration/rehearsal verified; production migration not applied**

## Metadata dùng để thiết kế

Migration không suy type từ proposal. Precondition kiểm tra trực tiếp database identity
`9C44B304-8A84-4D0D-9A82-19C7233FF6BB`, collation
`SQL_Latin1_General_CP1_CI_AS` và metadata hiện hữu:

| Contract | Type được pin |
|---|---|
| `App_HocVien.HocVienId` | `bigint` |
| `App_KhoaHoc.KhoaHocId` | `bigint` |
| `App_GiaoVien.GiaoVienId` | `bigint` |
| `App_XeTap.XeTapId` | `bigint` |
| `App_ImportBatch.ImportBatchId` | `bigint` |
| `App_ImportBatch.EntityType` | `nvarchar(50)` |
| source/business codes liên quan | `nvarchar` đúng length/collation hiện hữu |
| RowVersion các bảng liên quan | SQL Server `rowversion/timestamp(8)` |

Precondition fail trước DDL nếu database, object, type, length, collation hoặc
RowVersion khác contract.

## `dbo.App_GiaoVien_hs`

Schema gồm identity `GiaoVienHsId`, mã, họ tên, `HoTenSearch`, ngày sinh, CCCD,
trạng thái, ghi chú, soft-delete, audit và RowVersion.

Guard chính:

- unique `MaGiaoVienHs`;
- filtered unique `SoCCCD` khi có giá trị và chưa xóa;
- check code/name normalization và không có leading/trailing whitespace;
- CCCD chỉ 9 hoặc 12 chữ số khi có giá trị;
- trạng thái/soft-delete/audit nhất quán;
- hard delete bị chặn; ứng dụng chuyển inactive hoặc soft-delete có kiểm tra reference.

## `dbo.App_KhoaHoc_NhomDaoTao`

Nhóm dùng `bigint` identity và FK `bigint` đến khóa, giáo viên đứng lớp và hai xe mặc
định. Unique key là `(KhoaHocId, MaNhom)`. `ThuTu`, trạng thái, ghi chú, audit và
RowVersion được lưu cùng nhóm.

Tất cả bốn FK dùng `ON DELETE NO ACTION` và `ON UPDATE NO ACTION`. Trigger giữ
`KhoaHocId` bất biến và chặn hard delete; reference inactive vẫn được giữ trong lịch sử.

## `dbo.App_HocVien_PhanCong`

Mỗi row là một full snapshot, gồm:

- exact `HocVienId`;
- nhóm, người nhận hồ sơ, giáo viên đứng lớp, xe tập, xe bài số 10;
- ba cờ override giáo viên/xe;
- `NguonGan` (`MANUAL`, `EXCEL`, `BULK`, `GROUP`);
- optional `ImportSessionId`;
- effective-from/effective-to, `IsCurrent`;
- reason/audit và RowVersion.

Filtered unique index `UX_App_HVPC_OneCurrentPerHocVien` bảo đảm tối đa một snapshot
current. Check/trigger bảo vệ:

- snapshot không rỗng;
- khi không có nhóm, cả ba cờ override giáo viên/xe bắt buộc bằng `1`; khi có nhóm,
  `INHERIT`/override tiếp tục được kiểm tra theo default đã seal;
- không INHERIT khi không có nhóm;
- inherited values phải bằng default đã seal của nhóm;
- group phải thuộc đúng course/profile của learner;
- current/effective dates nhất quán;
- `EXCEL` bắt buộc import session loại `HOCVIEN_ASSIGNMENT`;
- non-Excel không mang import session;
- snapshot cũ không được overwrite hoặc hard-delete.

Tất cả bảy FK của assignment và bốn FK của group là `NO ACTION`.

## `App_ImportBatch`

Migration bổ sung metadata nullable, chỉ bắt buộc cho entity assignment:

- `FileSha256 char(64)`;
- `TemplateVersion varchar(40)`;
- `NormalizationVersion varchar(40)`;
- `PreviewExpiresAtUtc datetime2(7)`;
- `ConfirmedAtUtc datetime2(7)`;
- `IdempotencyKey nvarchar(100)`;
- filtered unique idempotency index.

`IdempotencyKey` chỉ lưu SHA-256 viết hoa của key do client gửi, không lưu raw key.

## `dbo.App_AssignmentOperation`

Ledger bền vững này là điểm single-flight chung cho assignment, group-default propagation
và Excel confirm. Unique constraint toàn cục trên `IdempotencyKeySha256 char(64)` ngăn cùng
key bị dùng lại bởi actor, operation type hoặc scope khác.

Mỗi row giữ exact operation type, course/profile/scope, actor, payload hash, optional preview
token hash, operation/import result, số changed/no-change, bulk-permission marker, thời điểm
commit và `RetainUntilUtc`. Raw key, file content và learner PII không được lưu.

Repository lấy transaction-owned `sp_getapplock`, đọc ledger bằng `UPDLOCK/HOLDLOCK`, chạy
business mutation và insert ledger trong cùng transaction. Cùng logical request replay kết quả
đã commit sau process restart; khác actor/type/course/profile/scope/payload trả conflict.
Retention tối thiểu là 180 ngày; API role không có UPDATE/DELETE trên ledger.

## Quyền database tối thiểu

Migration tạo `QLHV_AssignmentApiRole` nhưng không tự suy đoán hay tự bind API principal.
Role có đúng 20 object permissions: SELECT/INSERT/UPDATE trên bốn bảng write hiện hữu,
SELECT/INSERT trên operation ledger, SELECT trên bốn source-owned catalog và SELECT/INSERT
trên `App_AuditLog`; không có DELETE trên năm bảng write. Realtime principal vẫn có 15
explicit DENY DML trên năm object.

Hai artifact fail-closed tách riêng việc vận hành principal:

- `database/patches/20260730_bind_assignment_api_principal.sql`;
- `database/patches/20260730_unbind_assignment_api_principal.sql`.

Cả hai yêu cầu SQLCMD variable `AssignmentApiPrincipal` không rỗng, pin exact production DB
GUID, reject `dbo`/principal hệ thống/realtime writer và chỉ ADD/DROP member của role. Bind
xác minh 20 effective permissions cùng năm `DELETE=0`. Các script này chưa được thực thi.

## Artifacts và rollback

- apply: `database/patches/20260730_add_integrated_course_assignment.sql`
- rollback: `database/patches/20260730_rollback_integrated_course_assignment.sql`
- bind API principal: `database/patches/20260730_bind_assignment_api_principal.sql`
- unbind API principal: `database/patches/20260730_unbind_assignment_api_principal.sql`
- isolated rehearsal: `database/patches/20260730_rehearse_integrated_course_assignment.ps1`

Rollback drop theo reverse dependency chỉ khi chưa có business data/history. Khi đã có
dữ liệu, script trả `ROLLBACK_BLOCKED_DATA_OR_HISTORY`; hướng xử lý bắt buộc là disable
writer và roll forward, không drop lịch sử.
Rollback rỗng cũng fail-closed khi `QLHV_AssignmentApiRole` còn bất kỳ member nào; operator
phải unbind đúng API database user trước, sau đó script revoke 20 grants và drop role.

Script có `USE [QLHV_APP]; GO`, nhưng **chưa được chạy trên production**.

## Kết quả isolated rehearsal

Rehearsal trên database disposable đã PASS toàn bộ:

- migration shape/type/FK/permission verification: 4 primary keys, 4 RowVersion, 14
  `NO ACTION` FK, 15 realtime DML denies, exact 20 API-role grants và no DELETE;
- role-member rollback guard, unbind, revoke/drop role;
- empty rollback;
- concurrent single-flight ledger, rollback-before-ledger then retry và global key conflict;
- expected rejection cho cross-profile, duplicate current, partial no-group override,
  invalid inheritance, wrong
  import entity, snapshot overwrite và hard delete;
- snapshot close/history;
- populated rollback bị block đúng thiết kế;
- cleanup database rehearsal.

Không dùng `QLHV_APP` làm rehearsal target.
