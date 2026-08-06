# Kế hoạch migration production cho assignment

Ngày lập: 2026-07-30

Trạng thái: **PLAN ONLY — DO NOT EXECUTE IN THIS TASK**

## Điều kiện vào cửa

Không cần dừng nhập học viên. Không dùng count khóa/học viên/giáo viên/xe làm guard.
Trước cửa sổ migration phải xác minh lại:

- repository artifact/review pack đúng SHA-256 đã duyệt;
- hai protected development config giữ SHA-256
  `12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E`;
- target chính xác là `QLHV_APP`, GUID
  `9C44B304-8A84-4D0D-9A82-19C7233FF6BB`;
- metadata/type/length/collation/RowVersion còn đúng precondition;
- realtime healthy, Auto Sync OFF, active run/slot/operation bằng 0;
- không có migration assignment/vehicle một phần;
- đã xác định chính xác database user mà API production thực sự dùng; giá trị này phải
  tồn tại sẵn trong `QLHV_APP`, không phải `dbo`, principal hệ thống hay realtime writer;
- Release builds, tests và isolated rehearsals đều PASS;
- operator cấp phép riêng cho production migration.

Nếu bất kỳ identity/schema/runtime guard nào đổi, dừng fail-closed; baseline count thay đổi đơn
thuần không phải lỗi.

## Rehearsal bắt buộc trước production

Chạy `20260730_rehearse_integrated_course_assignment.ps1` trên database disposable có prefix
allow-list. Rehearsal phải chứng minh:

- migration tạo đúng 4 bảng, 14 FK NO ACTION, 4 RowVersion, 15 realtime deny permissions
  và role `QLHV_AssignmentApiRole` có đúng 20 grant, không có DELETE trên năm bảng write;
- rollback bị chặn khi assignment API role còn member; unbind đúng member rồi rollback
  rỗng phải xóa role và toàn bộ grant;
- rollback khi schema rỗng PASS;
- cross-profile group/course bị từ chối;
- one-current, invalid inheritance, wrong import entity bị từ chối;
- snapshot overwrite và hard delete bị từ chối;
- rollback khi có data/history bị block như thiết kế;
- database disposable được cleanup.

Vehicle target migration/rollback có rehearsal riêng theo
`VEHICLE_REALTIME_MAPPING_IMPLEMENTATION.md`. Không dùng `QLHV_APP` làm rehearsal target.

Trạng thái 2026-07-30: assignment isolated rehearsal đã **PASS** đầy đủ, gồm migration,
empty rollback, constraint rejection, populated rollback block và cleanup. Đây chỉ là bằng
chứng sẵn sàng; không phải authorization để chạy production.

## Thứ tự production đề xuất cho task có approval sau này

1. Lấy fresh read-only runtime/schema/config evidence; không seal count.
2. Đặt deployment artifact immutable và log hashes.
3. Apply target vehicle schema migration, nhưng chưa tạo baseline/checkpoint và chưa host writer.
4. Apply assignment schema migration `20260730_add_integrated_course_assignment.sql`.
5. Read-only verify tables, columns, checks, triggers, indexes, FK NO ACTION, realtime DENY
   và exact 20 grants của `QLHV_AssignmentApiRole`.
6. Dùng `20260730_bind_assignment_api_principal.sql` với SQLCMD variable bắt buộc
   `AssignmentApiPrincipal=<database-user-thật-của-API>`. Không log credential, không dùng
   login/service name suy đoán. Script phải xác minh exact DB GUID, reject principal rỗng/
   reserved/realtime, bind member và chứng minh 20 effective permissions cùng năm
   `DELETE=0` trước khi deploy API writer.
7. Deploy backend/frontend tương thích schema; health/read-only smoke test.
8. Chỉ trong một activation task riêng: enable CT đúng `dbo.XeTap` ở OTO/MOTO, lấy sealed
   per-profile baseline, xử lý collision/manual-review, tạo checkpoint và compose vehicle
   hosted cycle sau mọi mutual-exclusion guard.
9. Theo dõi audit, 409 conflicts, import sessions và manual-review; học viên có thể tiếp tục
   được nhập trong suốt quá trình.

Không chạy Auto Sync fallback, không thay checkpoint hiện hữu, không restart realtime ngoài
kế hoạch deployment được phê duyệt, và không chạy lại RT-02/RT-03/RT-04.

## Verification sau migration

- object/type/index/check/trigger/FK/permission exact;
- API catalog GET không có DML source path;
- assignment writes tạo close-and-insert history;
- two-user stale RowVersion trả 409 và không partial write;
- import READY/NO_CHANGE atomic smoke test bằng dữ liệu được operator cho phép;
- source profile `CSDT_OTO` không match `CSDT_MOTO`;
- realtime service/Auto Sync/mutex state đúng kế hoạch;
- không dùng total count làm success criterion.

## Rollback decision

- Nếu chưa có bất kỳ business data/history/import assignment: dừng writer rồi chạy exact
  unbind đúng API database user bằng `20260730_unbind_assignment_api_principal.sql`, xác minh
  role không còn member, rồi mới chạy exact reverse script
  `20260730_rollback_integrated_course_assignment.sql`.
- Nếu đã có data/history: không drop. Disable assignment writer/UI mutation, giữ evidence,
  sửa forward và deploy lại.
- Vehicle mapping dùng rollback riêng và cùng nguyên tắc: có source/control data thì roll
  forward, không drop.

Production migration **chưa được áp dụng** trong task hiện tại.

Hai script bind/unbind chỉ là artifact fail-closed, **chưa được thực thi**. Production task
sau phải truyền principal thật bằng SQLCMD variable không rỗng; migration không tự suy đoán
hay tự thêm member cho role.

Final read-only audit xác nhận production vẫn chưa có 4 bảng assignment, chưa có 6 cột import
hoặc vehicle mapping/control object; CT xe vẫn OFF và Auto Sync active `0/0/0`. Tuy nhiên
audit lúc `2026-07-30T15:16:08Z` thấy standalone realtime service `Stopped` và persisted
worker state `BLOCKED / RT03_UNSUPPORTED_DRIFT`. Đây là production safety blocker phải được
owner của realtime workflow xử lý/approve trong một task riêng trước bất kỳ migration nào;
task assignment này không restart service hoặc thay đổi production.
