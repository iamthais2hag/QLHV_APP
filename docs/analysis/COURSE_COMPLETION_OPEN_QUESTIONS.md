# Course completion — Contract V1 decision register

## 1. Readiness result

Operator Course Completion Contract V1 đã đóng các câu hỏi P0 của discovery bằng cách chọn marker QLHV-only và loại bỏ reopen khỏi phạm vi. Không còn yêu cầu thiết kế inverse mutation cho Báo cáo II, sát hạch hoặc GPLX.

Contract này là phê duyệt nghiệp vụ, **không phải** phê duyệt code, migration hay production deployment.

## 2. Quyết định operator đã chốt

| ID | Quyết định V1 | Hệ quả implementation |
|---|---|---|
| V1-01 | Completion là marker “Đã chốt kết quả đào tạo của khóa tại thời điểm xác nhận”. | Entity QLHV-owned riêng; không suy từ `KhoaHoc.TrangThai` hay learner `13`. |
| V1-02 | Không cập nhật kết quả học viên. | V2/V1 read-only; snapshot ghi current observed values. |
| V1-03 | Không cập nhật `KhoaHoc.TrangThai`. | Edit-lock workflow độc lập và không bị completion ảnh hưởng. |
| V1-04 | Không sinh/sửa chứng nhận, BCII, XML, `MaBC2`, sát hạch, GPLX. | Zero DML trên các object/field downstream; regression/effective-permission tests bắt buộc. |
| V1-05 | Không ghi trực tiếp V1. | Không có V1 repository mutation/credential DML. |
| V1-06 | `09`, `10`, `11`-`19` được phân loại rõ; `01`-`08`, `90`, missing/unclassified block. | Exact state classifier, không manual override. |
| V1-07 | Không yêu cầu tất cả học viên đạt. | Khóa có thể READY với hỗn hợp `09` và `10`. |
| V1-08 | Báo cáo I, giáo viên, xe, chương trình là diagnostic/warning. | Hiển thị rõ nhưng không làm `canConfirm=false`. |
| V1-09 | Business completion date do người dùng chọn; audit dùng SQL/API UTC. | Hai field/khái niệm tách biệt; không dùng client clock cho audit. |
| V1-10 | Confirm tạo marker, learner snapshot, idempotency ledger và audit trước/sau. | Một QLHV transaction, verify count/hash trước commit. |
| V1-11 | Không hỗ trợ reopen; sai sót dùng correction workflow riêng. | Không endpoint/nút/capability reopen trong V1; không inverse downstream. |
| V1-12 | Completion không khóa thêm/xóa/chuyển học viên. | Không sửa `KhoaHoc.TrangThai`; các quyền chỉnh sửa khóa giữ nguyên. |

## 3. Diễn giải fail-closed của contract

Các diễn giải sau làm rõ cách triển khai an toàn mà không mở rộng business scope:

- **Learner scope**: mọi `NguoiLX_HoSo` có exact `MaKhoaHoc` của course và đúng source profile tại lần đọc snapshot. Không exclude ngầm theo trạng thái.
- **Khóa không có học viên**: `BLOCKED`; không thể chứng minh đã chốt kết quả đào tạo của khóa.
- **Duplicate/ambiguous identity**: `BLOCKED`.
- **`09`/`10` thiếu kết quả**: dùng validation đã xác minh của workflow cũ — luôn cần `MaDK`, `MaKhoaHoc`, `KetLuanCSDT`, `TGBatDau`, `TGKetThuc`, range hợp lệ; với hạng non-`A*`/non-`B1m` cần thêm các result/điểm/giờ/km mà form cũ bắt buộc.
- **`11`-`19`**: downstream đã active nên status được coi là classification đủ; snapshot current data nhưng không yêu cầu completion sửa/bù field.
- **Status `90`, `NULL`, mã ngoài matrix**: block/manual review.
- **Source thay đổi giữa preview và confirm**: `CONFLICT`; user phải preview lại.
- **Marker đã tồn tại và snapshot/request khớp**: `NO_CHANGE`/idempotent replay.
- **Marker đã tồn tại nhưng source snapshot khác**: `CORRECTION_REQUIRED`; không auto-reopen hoặc auto-update marker.

## 4. Business date V1

Contract chỉ yêu cầu business date do người dùng chọn. Do đó V1:

- yêu cầu một giá trị ngày hợp lệ;
- không dùng giờ/ngày trình duyệt làm authority;
- lưu business date đúng giá trị người dùng chọn;
- lưu `CreatedAtUtc`/audit timestamp từ SQL/API server UTC;
- ngày khóa, ngày bắt đầu/kết thúc source chỉ hiển thị diagnostic;
- không tự thêm rule “không được quá khứ/tương lai” hoặc ép bằng ngày bế giảng nếu chưa có amendment của operator.

## 5. Không còn câu hỏi P0 nghiệp vụ trong V1

Các nhóm câu hỏi trước đây đã được đóng:

| Nhóm cũ | Resolution |
|---|---|
| Ý nghĩa completion | Marker QLHV-only. |
| Course/source mutation | Không có. |
| Learner mutation/pass requirement | Không mutation; không all-pass. |
| State eligibility | Exact matrix V1. |
| BCI/teacher/vehicle/program | Warning only. |
| Certificate/BCII/XML/exam/GPLX | Không chạm. |
| Date authority | Business date user-selected; audit UTC. |
| Reopen/reversal | Không hỗ trợ; correction riêng. |
| Lock edit/add/remove/transfer | Không thuộc completion. |

## 6. Quyết định P1 trước deployment

Các mục này không thay đổi business behavior và không chặn bắt đầu implementation, nhưng phải chốt trước production approval:

| ID | Quyết định còn lại | Safe default cho implementation/rehearsal |
|---|---|---|
| P1-01 | Role nào có `Courses.Complete`? | Admin-only; không kế thừa quyền sửa khóa/phân công. |
| P1-02 | Employee/Viewer có được preview không? | Viewer chỉ xem status; Admin preview/confirm cho tới khi matrix được duyệt. |
| P1-03 | Reason có bắt buộc và giới hạn độ dài? | Bắt buộc, normalized/bounded, ghi audit. |
| P1-04 | Retention snapshot/idempotency/audit? | Không purge trong release đầu; lập retention migration riêng sau legal/data-owner approval. |
| P1-05 | Có cần four-eyes approval? | Không triển khai ngầm; feature flag/canary Admin-only. |
| P1-06 | Correction workflow cụ thể? | Ngoài V1; UI chỉ trả `CORRECTION_REQUIRED` và hướng dẫn liên hệ người có thẩm quyền. |
| P1-07 | Rollout profile/hạng nào trước? | Canary do operator chọn sau rehearsal đủ OTO/MOTO và các nhóm hạng. |

Nếu operator chọn policy khác safe default, phải cập nhật security/runbook và tests trước deployment; không sửa behavior âm thầm.

## 7. Acceptance questions kỹ thuật cho implementation review

Đây là các bằng chứng đội triển khai phải trả lời bằng code/test/rehearsal, không phải câu hỏi business P0:

1. Canonical snapshot bao phủ exact field nào và ổn định giữa API/repository/test?
2. Làm sao detect learner thêm/xóa/chuyển hoặc source status/result đổi giữa preview và confirm?
3. QLHV course lock/idempotency unique constraint chống hai confirm đồng thời thế nào?
4. Forced failure ở từng insert có rollback marker, toàn bộ learner snapshot, ledger và audit không?
5. Source/V1 credentials có được chứng minh zero DML không?
6. Full convergence/RT03 có bảo toàn marker QLHV-owned không?
7. API/UI có hoàn toàn không expose reopen và không gọi Báo cáo I/XML/BCII không?
8. SQL/API UTC có đi qua shared TimeAuthority policy và fail closed khi writes không được phép không?
9. Marker/source drift sau commit có được đưa `CORRECTION_REQUIRED` mà không auto-mutation không?
10. Logs/audit có tránh PII/secrets không cần thiết và vẫn đủ before/after evidence không?

## 8. Điều kiện implementation approval

Một approval triển khai riêng nên cho phép đúng:

- code/migration additive cho marker, snapshot, ledger, audit;
- API/UI preview + confirm marker-only;
- read-only V2/V1 inspection;
- tests/build/disposable rehearsal và artifact/runbook mới.

Approval đó không nên cho phép production deploy, V2/V1 DML, reopen, correction mutation, RT03 state/checkpoint changes hoặc Báo cáo I/XML/BCII changes nếu chưa có chỉ thị riêng.

## 9. Kết luận

Contract V1 đã giải quyết toàn bộ blocker nghiệp vụ P0 trong phạm vi marker-only. Các mục còn lại là acceptance evidence và deployment policy, có safe default rõ ràng.

**READY FOR COURSE COMPLETION IMPLEMENTATION APPROVAL**
