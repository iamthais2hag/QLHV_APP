# Course completion — implementation plan (not approved for implementation)

## 1. Trạng thái kế hoạch

Đây là thiết kế kỹ thuật để operator xem xét. Task discovery **không** tạo code, migration, API, UI, deployment artifact hoặc production mutation.

Operator Course Completion Contract V1 đã đóng các quyết định nghiệp vụ P0:

1. Completion là marker QLHV-owned “Đã chốt kết quả đào tạo của khóa tại thời điểm xác nhận”.
2. Source V2/V1 và kết quả học viên là read-only trong operation.
3. Exact learner-state matrix là `09`, `10`, `11`-`19` hợp lệ; `01`-`08`, `90`, thiếu/unclassified block.
4. Không có reopen trong V1; sai sót dùng correction workflow riêng.
5. Báo cáo I, giáo viên, xe và chương trình chỉ là diagnostic/warning.

Implementation vẫn cần một approval riêng; tài liệu này không tự cấp quyền code, migration hoặc deployment.

## 2. Kiến trúc đề xuất

```text
Course detail UI
   |
   +-- GET completion status
   +-- POST completion preview -------- read/validate/seal snapshot
   +-- POST completion confirm -------- idempotent atomic command
                                                |
                                                v
                                   QLHV completion orchestrator
                                     |                 |
                                     |                 +-- QLHV marker/audit/ledger
                                     v
                           read-only sealed source snapshot
                              CSDL_OTO or CSDL_MOTO
                           (V1 is never written or required)
```

Không gọi procedure per-learner cũ. Không gọi luồng XML/Báo cáo II. Không ghi V2 hoặc V1. Transaction mutation chỉ nằm trong QLHV_APP.

## 3. Phase thiết kế và triển khai

### Phase 0 — operator contract (completed)

- Marker-only, không sửa kết quả hoặc `KhoaHoc.TrangThai`.
- Business completion date do người dùng chọn; audit timestamp dùng SQL/API UTC.
- Learner matrix `09`, `10`, `11`-`19` versus blocked `01`-`08`, `90`, missing/unclassified.
- Không yêu cầu all-pass và không exclude ngầm học viên chưa phân loại.
- Báo cáo I/giáo viên/xe/chương trình là warning.
- Downstream read-only; không reopen trong V1.

Output: Operator Course Completion Contract V1. Implementation artifact phải ghi contract version này vào preview, marker và audit.

### Phase 1 — schema/migration draft trong disposable database

Đề xuất entity QLHV-owned riêng:

- `App_CourseCompletion`: marker `COMPLETED`, exact source identity, business date, snapshot hash, timestamps UTC, actor/reason, rowversion.
- `App_CourseCompletionLearnerSnapshot`: immutable observed data/classification theo `MaDK`; không lưu PII không cần thiết.
- `App_CourseCompletionOperation`: idempotency key hash, request fingerprint, status/result, retention.
- `App_AuditLog` hiện có có thể nhận summary; snapshot chi tiết nên ở bảng chuyên biệt với access control/retention rõ ràng.

Constraints cần có:

- unique completion marker cho source profile + source course key;
- FK tới `App_KhoaHoc` nếu lifecycle sync/delete contract cho phép;
- rowversion trên mutable completion row;
- check constraint cho state enum và timestamp ordering;
- index phục vụ status/history, không tạo uniqueness dựa vào row count.

Không tạo stored procedure mutation trên V2/V1. Repository source chỉ được cấp `SELECT` trên exact object cần preview/revalidation; permission tests phải chứng minh không có DML.

### Phase 2 — application contract

Đề xuất service riêng, không nhét vào `IAssignmentService` nếu làm mờ boundary:

- `GetCompletionStatusAsync`
- `PreviewCompletionAsync`
- `ConfirmCompletionAsync`

DTO preview tối thiểu:

- exact course identity/profile;
- contract version;
- preview token + expiry;
- sealed snapshot hash;
- course-level diagnostics;
- learner rows với observed status/result classification và blocker;
- counts chỉ để hiển thị, không làm safety identity;
- downstream flags;
- `canConfirm` và exact reason codes.

DTO confirm phải có preview token, idempotency key, business completion date, reason và expected rowversion nếu marker đã tồn tại. Không nhận result/status mutation hoặc arbitrary SQL field map từ client.

### Phase 3 — API và phân quyền

Route đề xuất:

```text
GET  /api/khoa-hoc/{id}/hoan-thanh
POST /api/khoa-hoc/{id}/hoan-thanh/preview
POST /api/khoa-hoc/{id}/hoan-thanh/confirm
```

Capability riêng:

- `Courses.ViewCompletionStatus`
- `Courses.PreviewCompletion`
- `Courses.Complete`

Viewer chỉ xem; Employee/Admin mapping phải được operator duyệt trước deployment. Không mặc định Employee có complete. V1 không đăng ký capability/route reopen.

Áp dụng pattern actor hiện tại tại `AssignmentControllers.cs:9-13`, ProblemDetails code/trace tại `AssignmentControllers.cs:27-43`, và chính sách must-change-password tại `Program.cs:105-131`.

### Phase 4 — UI

Vị trí: **Khóa học → Chi tiết khóa học → Hoàn thành khóa học**.

Luồng:

1. Trang chi tiết hiển thị completion status và downstream warning.
2. Người có quyền chọn “Kiểm tra điều kiện”.
3. Preview hiển thị tổng hợp, nhưng vẫn liệt kê từng học viên/blocker và planned mutation.
4. Nếu `canConfirm=false`, không render đường bypass.
5. Confirm dialog ghi rõ profile, mã khóa, số dòng sẽ đổi, dữ liệu không đổi, warning và yêu cầu lý do.
6. Confirm dùng preview token + idempotency key; disable double submit.
7. Kết quả trả `COMPLETED`, `NO_CHANGE`, `CONFLICT` hoặc exact block code; UI tải lại dữ liệu từ server.
8. Không có action “Mở lại” trong V1. UI dẫn người dùng sang correction workflow/help khi phát hiện sai sót sau marker.

Client clock chỉ dùng hiển thị. Mọi timestamp/audit authority lấy từ SQL/API server UTC theo time policy hiện hành.

## 4. Transaction contract

### 4.1 Preview

Preview là read-only nhưng phải tạo sealed token server-side:

1. Resolve exact `App_KhoaHoc` → `SourceProfileCode` → database allowlist.
2. Đọc course và toàn bộ learner scope từ V2; đọc downstream signals cần thiết để phân loại `11`-`19`, nhưng không mutation.
3. Phân loại tất cả cột/status; unclassified → `BLOCKED`.
4. Sort theo stable keys và tính canonical SHA-256 snapshot.
5. Ghi/giữ preview token ngắn hạn gắn actor, contract version và request fingerprint.

Vì V2 tables không có rowversion, snapshot hash phải bao phủ tất cả field có thể làm thay đổi eligibility/outcome. Change Tracking version có thể dùng làm diagnostic/anchor nhưng không thay thế revalidation row-level.

### 4.2 Confirm

Một confirm an toàn phải:

1. Kiểm tra TimeAuthority/writesAllowed theo shared production policy.
2. Lấy application lock QLHV theo ordered key `CourseCompletion:{profile}:{courseKey}` để chống concurrent confirm của cùng khóa.
3. Re-read exact source scope, phân loại lại và tính canonical snapshot ngay trước QLHV mutation.
4. Snapshot khác preview → `CONFLICT`, không ghi gì.
5. Duplicate/ambiguous identity/unclassified status/missing result → `BLOCKED`, không ghi gì.
6. Begin QLHV SQL transaction với `XACT_ABORT ON` và isolation/locking đã rehearsal.
7. Revalidate active marker/idempotency state dưới lock.
8. Ghi `App_CourseCompletion`, immutable learner snapshot, idempotency result và audit.
9. Verify marker/snapshot counts và hashes trong transaction.
10. Commit một lần. Chỉ sau commit mới trả thành công.

Vì source là read-only, không cần distributed mutation/saga giữa QLHV và V2. Source vẫn có thể đổi ngay sau lần đọc cuối; implementation phải lưu exact observed snapshot/version và coi marker là sự kiện “đã chốt snapshot quan sát tại thời điểm xác nhận”, sau đó detect drift để chuyển correction/manual review. Nếu business yêu cầu khóa vật lý source tại đúng instant commit, đó là contract khác và không được tự mở rộng V1.

### 4.3 Idempotency

- Idempotency key bắt buộc, chuẩn hóa và hash trước khi lưu.
- Ledger unique theo actor/scope/key hoặc policy đã duyệt.
- Cùng key + cùng request fingerprint: replay exact result.
- Cùng key + khác fingerprint: `CONFLICT`.
- Khóa đã có active completion với cùng exact after-snapshot: `NO_CHANGE`.
- Có marker nhưng source state khác: `CONFLICT`/manual review, không auto-heal.

Pattern tham khảo đã tồn tại tại `AssignmentPreviewStore.cs:91-155`, `SqlAssignmentRepository.Idempotency.cs`, và set-based RowVersion/revalidation tại `SqlAssignmentRepository.SetBased.cs:332-510`.

## 5. Audit contract

Mỗi operation phải ghi:

- operation/correlation/idempotency identity;
- actor từ authenticated claim, capability và client context;
- `SourceProfileCode`, `MaKH`, QLHV course id;
- contract version, preview hash, before/after hash;
- per-learner before/after cho các field thực sự thay đổi;
- reason và result (`COMPLETED`, `NO_CHANGE`, `CONFLICT`, `BLOCKED`, `CORRECTION_REQUIRED`);
- SQL/server `SYSUTCDATETIME()` hoặc verified shared UTC authority;
- downstream flags quan sát tại commit.

Không dùng giờ trình duyệt. Không dùng `DateTime.Now` như phần mềm cũ. Audit summary có thể theo pattern `App_AuditLog` tại `SqlAssignmentRepository.SetBased.cs:672-705`, nhưng completion detail phải không bị truncate và cần retention/access policy.

## 6. Realtime contract

- Completion không ghi nguồn đào tạo; RT03 tiếp tục độc lập theo ownership hiện hành.
- Không start/stop Worker, sửa checkpoint hoặc tạo marker realtime trong workflow completion.
- Preview/confirm phải phát hiện source drift; không yêu cầu realtime checkpoint đứng yên và không lấy checkpoint làm completion identity.
- Worker tiếp tục preserve V1 downstream theo planner hiện tại; completion chỉ đọc status để snapshot.
- QLHV completion marker là QLHV-owned; full convergence không được xóa/overwrite.
- Không lấy CT row count/version làm bằng chứng completion; CT chỉ vận chuyển mutation đã commit.

RT03/V7/V8 production readiness là prerequisite triển khai riêng. Discovery này không thay đổi hay tiếp tục các phase đó.

## 7. Test plan bắt buộc

### 7.1 Business/behavioral

- OTO/MOTO, từng nhóm hạng `A*`, `B1m`, non-A/B1m.
- Course missing/invalid dates: diagnostic/warning, không hard block marker.
- Learner `09`, `10`, `11`-`19`, incomplete, `01`-`08`, `90`, NULL/unclassified theo exact Contract V1.
- Khóa rỗng, duplicate hoặc ambiguous identity → fail closed.
- Có/không Báo cáo I: warning only.
- Giáo viên/xe/chương trình: warning only; giờ/km block chỉ khi làm result `09/10` incomplete theo hạng.
- `ALREADY_COMPLETED` exact snapshot → `NO_CHANGE`.
- Status `11`-`19` → READY read-only; `90`/unclassified → fail closed; `MaBC2`, exam/GPLX không đổi.

### 7.2 Transaction/concurrency

- Forced exception ở từng QLHV marker/snapshot/ledger/audit mutation → rollback toàn bộ.
- Một row thay đổi giữa preview/confirm → `CONFLICT`, zero mutation.
- Learner thêm/xóa/chuyển khóa giữa hai bước → `CONFLICT`.
- Concurrent confirm cùng/different idempotency key.
- Concurrent source update/realtime cycle và completion: snapshot drift được phát hiện/ghi nhận; không lost update vì completion không source-write.
- Exact affected/verified counts, không phụ thuộc tổng số học viên hardcode.
- Connection loss sau commit: retry trả ledger result, không duplicate.

### 7.3 Ownership/regression

- Hash/field comparison trước-sau chứng minh giữ nguyên nhóm, phân công, manual override và QLHV-owned course fields.
- V1 downstream fields (`MaBC2`, exam, GPLX, `SoGiayCNTN`) không đổi.
- Không có V1 → V2 write path.
- RT03 course/teacher/vehicle/learner regression; completion không thay source/checkpoint.
- Assignment, Excel, Báo cáo I/XML regression: completion không gọi/chạm các workflow này.
- TimeAuthority safe warning/blocked cases dùng cùng policy API/worker/preflight.

### 7.4 Authorization/audit

- Ba capability V1 độc lập; view/preview không mutation; không tồn tại route/capability reopen.
- Must-change-password và unauthenticated fail closed.
- Actor/UTC/idempotency/before-after/correlation được ghi đầy đủ.
- Không log secrets hoặc PII không cần thiết.

## 8. Migration và rollout plan

Chỉ thực hiện sau approval mới:

1. Viết migration forward + verification + rollback script, chưa chạy production.
2. Build disposable databases từ exact QLHV/V2/V1 schema; V2/V1 credentials của completion chỉ read-only; seed các lifecycle state.
3. Rehearse marker transaction, forced rollback, source drift, idempotent rerun và absence of reopen.
4. Niêm phong artifact/manifest/hashes và operator runbook riêng; không sửa V6/V7/V8.
5. Production preflight read-only: TimeHealth, writers, exact schema/hash, duplicates, permission baseline, backup.
6. Apply additive QLHV schema và least-privilege source `SELECT` grant; verify zero source/V1 DML.
7. Deploy API, verify preview-only trước khi enable confirm route/feature flag.
8. Canary trên course không có downstream và được operator chọn; verify field-level before/after.
9. Enable có kiểm soát; post-deploy audit.

Không hardcode course id, CT version hoặc learner count.

## 9. Rollback plan

### 9.1 Deployment rollback

- Tắt feature flag/route mutation trước.
- Roll back binaries theo manifest backup.
- Chỉ drop additive schema khi không có completion record; nếu có record thì preserve data và dùng compatible binary.
- Không sửa checkpoint/realtime state để “rollback completion”.

### 9.2 Business correction; không rollback/reopen

- V1 không có business rollback hoặc reopen endpoint.
- Sai marker/source discrepancy được giữ audit và chuyển correction workflow riêng.
- Không xóa marker thủ công, không xóa certificate/`MaBC2`/exam/GPLX và không hạ `TT_XuLy`.

## 10. Risk register

| Risk | Severity | Control bắt buộc |
|---|---|---|
| Đồng nhất nhầm edit lock với completion | Critical | Entity completion riêng; operator phê duyệt mutation table. |
| Snapshot thiếu/một phần học viên | Critical | Exact source scope, canonical hash, snapshot-count/hash verification và forced rollback tests. |
| Ghi đè lifecycle V1/BCII/exam/GPLX | Critical | Ownership planner + downstream locks/checks; không có direct V1 write. |
| Cố bổ sung reopen làm sai hồ sơ pháp lý | Critical | Không có route/capability trong V1; correction workflow riêng. |
| Dữ liệu đổi sau preview | High | Sealed canonical hash, ordered locks và revalidation trong transaction. |
| Retry tạo mutation/audit trùng | High | Durable idempotency ledger + request fingerprint. |
| Client/server clock lệch | High | Shared TimeAuthority policy và server/SQL UTC; business date tách riêng. |
| Quyền completion quá rộng | High | Ba capability V1 riêng, least privilege và effective-permission tests. |
| Rule OTO áp sai cho MOTO/hạng đặc thù | High | Versioned matrix theo profile/hạng/hình thức; unclassified → block. |
| Source đổi quanh thời điểm confirm | High | Re-read sát commit, sealed observed snapshot, drift detection/manual review; không source mutation. |

## 11. Stop conditions

Implementation/deployment tương lai phải dừng nếu:

- contract version/manifest không khớp Operator Contract V1;
- có status/cột/unclassified learner;
- identity duplicate/ambiguous;
- preview hash không khớp;
- source identity/status không phân loại hoặc snapshot drift giữa preview/confirm;
- không lấy được QLHV course completion lock;
- transaction không thể bảo đảm atomic/verified outcome;
- TimeAuthority không cho phép writes;
- permission rộng hơn matrix;
- yêu cầu mở rộng sang reopen hoặc sửa BCII/sát hạch/GPLX.

## 12. Kết luận

Contract V1 đã được chốt marker-only và không có reopen. Kế hoạch implementation giữ source/V1 read-only, transaction atomic trong QLHV_APP, exact snapshot, idempotency và audit UTC. Không còn P0 business blocker trong phạm vi V1.

**READY FOR COURSE COMPLETION IMPLEMENTATION APPROVAL**
