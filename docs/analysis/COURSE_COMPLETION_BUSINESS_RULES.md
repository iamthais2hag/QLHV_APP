# Course completion — business rules discovery

## 1. Nguyên tắc phân loại

Tài liệu này tách rõ ba loại:

- **VERIFIED**: có mutation hoặc validation cụ thể trong code/SQL cũ.
- **NOT VERIFIED**: có dữ liệu liên quan nhưng không tìm thấy gate completion.
- **CONTRACT V1**: operator đã quyết định behavior mới trong phạm vi marker-only.

Việc có cột trong schema không tự động biến cột đó thành điều kiện nghiệp vụ. Dữ liệu production quan sát được cũng không được dùng làm quy tắc hoặc ngưỡng đếm.

## 2. Định nghĩa “hoàn thành” hiện có và contract đã chọn

Có ba khái niệm khác nhau:

| Khái niệm | Đối tượng | Bằng chứng | Kết luận |
|---|---|---|---|
| Kết quả đào tạo tại CSDT | Từng `NguoiLX_HoSo` | `usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat`, script dòng 17029-17078 | Ghi kết quả và đưa hồ sơ tới `09`/`10`; không hoàn thành khóa. |
| Khóa dữ liệu khóa | `KhoaHoc` | `usp_KhoaHoc_Update_TrangThai`, dòng 12242-12265 | `TrangThai=0` chỉ khóa sửa theo thông báo UI; không cập nhật học viên. |
| Hoàn thành đào tạo sau phê duyệt | Từng `NguoiLX_HoSo` | `usp_CSDT_PheDuyetKQDT_TiepNhan`, dòng 4407-4503 | Trạng thái `13` sau Báo cáo II/Sở phê duyệt; không phải course status. |

Operator Course Completion Contract V1 đã chọn nghĩa thứ tư: một marker QLHV độc lập xác nhận **kết quả đào tạo của khóa đã được chốt tại thời điểm xác nhận**. Marker không sửa kết quả, không khóa `KhoaHoc`, không kích hoạt downstream và không ghi V1.

### 2.1 State matrix V1 đã duyệt

| Learner state | Preview classification | Mutation |
|---|---|---|
| `09` | Kết quả đạt; READY nếu dữ liệu kết quả bắt buộc theo hạng không thiếu | Snapshot read-only |
| `10` | Kết quả không đạt; READY nếu dữ liệu kết quả bắt buộc theo hạng không thiếu | Snapshot read-only |
| `11`-`19` | Downstream đã active; vẫn được phân loại rõ | Snapshot read-only, bảo toàn toàn bộ V1-owned fields |
| `01`-`08` | `STUDENT_STATUS_INVALID` | Không ghi marker |
| `90` | `STUDENT_STATUS_INVALID`/manual review | Không ghi marker |
| `NULL`, mã khác hoặc identity không rõ | `BLOCKED` | Không ghi marker |

Không yêu cầu mọi học viên đạt. Một khóa có thể có cả `09` và `10`. Phạm vi snapshot là mọi hồ sơ có exact `MaKhoaHoc` của khóa trong source profile; khóa rỗng, duplicate hoặc ambiguous identity phải fail closed.

## 3. Điều kiện cấp khóa học

| Điều kiện được hỏi | Trạng thái bằng chứng | Bằng chứng/nhận định |
|---|---|---|
| Có ngày bắt đầu | NOT VERIFIED cho course completion | `KhoaHoc.NgayKG`/ngày tương ứng nullable. Form kết quả chỉ bắt buộc `NguoiLX_HoSo.TGBatDau`, không kiểm tra ngày khóa. |
| Có ngày kết thúc | NOT VERIFIED cho course completion | Form kết quả bắt buộc thời gian kết thúc của từng học viên; report completion trả ngày khóa là `NULL`. |
| Kết thúc không trước bắt đầu | VERIFIED chỉ ở learner form | `frmCapNhatCCN_ByKH.UpdateKQDT` kiểm tra `TGKetThuc >= TGBatDau`; schema không có check constraint. |
| Đầy đủ giáo viên | NOT VERIFIED | Không có check trong procedure/form kết quả. Quan hệ `KhoaHoc_GiaoVien` chỉ chứng minh dữ liệu tồn tại. |
| Đầy đủ xe tập lái | NOT VERIFIED | Không có check trong procedure/form kết quả. |
| Đầy đủ chương trình đào tạo | NOT VERIFIED | Không tìm thấy completion gate tương ứng. |
| Đủ thời gian đào tạo | VERIFIED một phần theo hạng | Với hạng không phải `A*`/`B1m`, form yêu cầu thời gian thực hành; không có ngưỡng tối thiểu được kiểm tra trong mutation. |
| Đủ kilomet | VERIFIED một phần theo hạng | Form yêu cầu dữ liệu quãng đường với một số hạng; không kiểm tra chuẩn tối thiểu. |
| Có Báo cáo I | VERIFIED cho màn hình danh sách kết quả thường | Query `usp_NguoiLX_KetQuaDaoTao_CSDT` dùng `EXISTS BaoCaoI` (dòng 16915-16918); chưa chứng minh đây là gate của mọi hạng hoặc completion mới. |
| Danh sách học viên đã khóa | NOT VERIFIED | `KhoaHoc.TrangThai=0` khóa sửa khóa theo UI, nhưng không thấy procedure completion yêu cầu trạng thái này. |
| Không còn trạng thái học viên bất hợp lệ | CONTRACT V1 | Chỉ `09`, `10`, `11`-`19` được phân loại hợp lệ; `01`-`08`, `90`, missing/unclassified block. |

Theo Contract V1, Báo cáo I, giáo viên, xe và chương trình chỉ là diagnostic/warning, không phải hard blocker. Thời gian/km vẫn dùng để xác định “thiếu kết quả” cho học viên `09/10` theo validation legacy đã xác minh theo hạng; chúng không trở thành course-level minimum threshold. Ngày khóa chỉ là diagnostic; ngày hoàn thành nghiệp vụ là field bắt buộc do người dùng chọn.

## 4. Điều kiện từng học viên

### 4.1 Điều kiện đã được chứng minh cho cập nhật kết quả

Áp dụng cho `frmCapNhatCCN_ByKH.UpdateKQDT`, không tự động đồng nghĩa với completion cả khóa:

- `MaDK` và khóa học xác định được;
- `KetLuanCSDT` có giá trị;
- `TGBatDau` và `TGKetThuc` có giá trị;
- `TGKetThuc >= TGBatDau`;
- với hạng không phải `B1m` và không chứa `A`: có kết quả/điểm lý thuyết, thực hành, thời gian và quãng đường hình/đường, đồng thời parse được dạng số;
- với `A*`/`B1m`: phần mềm cũ chấp nhận để các trường bổ sung là `NULL`.

Procedure cập nhật theo cặp `MaDK` + `MaKhoaHoc`, nhưng không kiểm tra `@@ROWCOUNT`. Thiết kế mới phải yêu cầu exact identity và đúng một dòng.

### 4.2 Các trường không có bằng chứng là gate completion

| Dữ liệu | Kết quả |
|---|---|
| Ảnh học viên | Không thấy form/procedure kết quả kiểm tra. |
| Giấy tờ bắt buộc | `NguoiLXHS_GiayTo` tồn tại nhưng không được procedure completion/result tham chiếu làm gate. |
| `SoGiayCNTN` | Được sinh ở luồng xuất XML/Báo cáo II, không phải khi cập nhật kết quả tại CSDT. |
| Giáo viên/xe phân công cá nhân | Không được save handler kết quả kiểm tra. |
| Ngưỡng giờ/km theo chương trình | Không có validation định lượng trong mutation đã xác minh. |

### 4.3 Phân loại học viên theo Contract V1

| Nhóm | Bằng chứng cũ | Contract V1 |
|---|---|---|
| Đạt | `KetLuanCSDT` đạt thường đưa `TT_XuLy` tới `09`. | Hợp lệ; không yêu cầu tất cả học viên đạt. |
| Không đạt | Kết luận không đạt đưa tới `10`. | Hợp lệ cùng với học viên đạt. |
| Bảo lưu/nghỉ học/chuyển khóa/xóa-ngừng | Không có model authoritative riêng. | Nếu còn trong khóa với status `01`-`08`, `90` hoặc không phân loại được thì block; không exclude ngầm. |
| Chưa đủ kết quả | Có thể nhận diện theo field bắt buộc của workflow kết quả. | Block cả khóa; V1 không cho phép override/exclude. |
| Downstream `11`-`19` | V1 lifecycle active. | Phân loại read-only, snapshot và không sửa downstream. |
| `90` | Ngoại lệ/giải trình. | Block và manual review. |

Contract xác nhận không được yêu cầu “tất cả học viên phải Đạt”; điều kiện là tất cả hồ sơ trong scope được phân loại rõ theo matrix trên.

## 5. Các preview status đề xuất

Đây là contract kỹ thuật fail-closed theo Operator Contract V1.

| Status | Khi nào dùng |
|---|---|
| `READY` | Mọi học viên exact scope thuộc `09`, `10` hoặc `11`-`19`; học viên `09/10` không thiếu result field; không có identity/conflict blocker. |
| `ALREADY_COMPLETED` | Có completion record QLHV chính thức, cùng snapshot/operation identity; không suy ra từ chữ hiển thị status. |
| `COURSE_NOT_FOUND` | Không tìm thấy exact `SourceProfileCode` + `MaKH`/identity. |
| `INVALID_COURSE_STATUS` | Không dùng làm gate V1; giữ reserved cho contract tương lai. |
| `MISSING_START_DATE` | Diagnostic/warning, không chặn marker V1. |
| `MISSING_END_DATE` | Diagnostic/warning, không chặn marker V1. |
| `INVALID_DATE_RANGE` | Diagnostic/warning ở cấp khóa; vẫn block nếu result time của học viên `09/10` sai. |
| `STUDENT_RESULT_INCOMPLETE` | Thiếu trường kết quả bắt buộc theo hạng đã được phê duyệt. |
| `STUDENT_STATUS_INVALID` | `TT_XuLy` không thuộc ma trận được phép. |
| `REPORT_I_REQUIRED` | Không dùng làm hard blocker V1; trả diagnostic/warning. |
| `REPORT_II_ALREADY_EXISTS` | Không block completion marker nếu hồ sơ thuộc `11`-`19`; chỉ báo downstream read-only. |
| `EXAM_LIFECYCLE_ACTIVE` | Không block completion marker nếu status thuộc `11`-`19`; không có reopen/mutation downstream. |
| `CONFLICT` | Dữ liệu/rowversion/hash thay đổi giữa preview và confirm. |
| `CORRECTION_REQUIRED` | Marker đã tồn tại nhưng current source snapshot khác snapshot đã chốt; không auto-update/reopen. |
| `BLOCKED` | Mapping/ownership không phân loại, duplicate, ambiguity, time/audit authority không an toàn hoặc lỗi không thuộc status cụ thể. |

Giáo viên, xe, chương trình và Báo cáo I được trả diagnostic/warning. Warning này không làm `canConfirm=false`, nhưng phải hiển thị rõ; mọi blocker về learner state/result/identity vẫn fail closed.

## 6. Mutation khi hoàn thành — điều đã biết và điều chưa được phép suy diễn

| Câu hỏi | Kết quả discovery |
|---|---|
| Cập nhật `KhoaHoc.TrangThai`? | Không. |
| Ghi ngày hoàn thành khóa? | Chỉ ghi business date do người dùng chọn trong marker QLHV; audit timestamp dùng SQL/API UTC. |
| Cập nhật từng học viên? | Không; chỉ chụp immutable snapshot. |
| Sinh/sửa số giấy chứng nhận? | Không. |
| Khóa thêm/xóa/chuyển học viên? | Không; đây là workflow quyền chỉnh sửa khóa riêng. |
| Khóa sửa kết quả? | Không. Thay đổi sau marker phải được phát hiện và đưa correction/manual review, không auto-update marker. |
| Tạo audit? | Có: before/after, actor, business date, SQL/API UTC, snapshot hash và idempotency identity. |
| Kích hoạt BCII/XML/downstream? | Không. |

## 7. Quy tắc ownership bắt buộc

- V2 (`CSDL_OTO`/`CSDL_MOTO`) là nguồn dữ liệu đào tạo và kết quả đào tạo.
- Luồng forward chỉ là **V2 → V1**. Không được tạo write-back từ V1 sang V2.
- `MaCSDT`, `MaKH`, `MaDK`, `MaKhoaHoc`, `MaBC1` phải giữ đúng identity/source contract.
- `MaBC2`, kỳ/kết quả sát hạch, GPLX và các tín hiệu downstream đã active là V1-owned; completion không được ghi đè.
- `SoGiayCNTN` phải được bảo toàn nếu đã tồn tại; không sinh lại, sửa hoặc xóa khi completion/correction handoff.
- Trạng thái `TT_XuLy` là shared theo lifecycle: V2 điều khiển vùng đào tạo (`01`-`10`), V1 điều khiển downstream (`11`-`19`, `90`). Conflict phải block.
- Trạng thái completion cục bộ của QLHV, nếu được duyệt, phải ở bảng/cột QLHV-owned riêng; không overloading `App_KhoaHoc.TrangThai` khi realtime đang bảo toàn trường này như dữ liệu QLHV-owned cho nghiệp vụ phân công.

Bằng chứng ownership hiện tại: `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeColumnOwnershipPolicy.cs:205-293`, `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeForwardWritePlanner.cs:8-24,234-319,365-400`, và `server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeCycleProcessor.cs:1452-1463`.

## 8. Mở lại khóa

Contract V1 **không hỗ trợ mở lại khóa** ở mọi trạng thái. Không có endpoint/nút/capability reopen. Sai sót được chuyển sang correction workflow riêng; workflow đó không được đảo Báo cáo II, sát hạch hoặc GPLX và nằm ngoài completion V1.

## 9. Phân quyền đề xuất

Tách ba capability V1, không kế thừa ngầm từ quyền sửa khóa/phân công:

- `Courses.ViewCompletionStatus`: xem preview/status/audit, không mutation;
- `Courses.PreviewCompletion`: tạo sealed preview;
- `Courses.Complete`: confirm completion.

API phải xác định actor từ authenticated server claim như mẫu `AssignmentControllerBase.Actor` (`AssignmentControllers.cs:9-13`) và tiếp tục chặn tài khoản còn buộc đổi mật khẩu theo pattern `Program.cs:105-131`.

## 10. Kết luận

Contract V1 đã chốt marker-only, exact learner-state matrix, warning boundary, business date, UTC audit và không hỗ trợ reopen. Các business blocker của discovery đã được đóng; implementation vẫn phải chứng minh concurrency, idempotency, authorization và field-level preservation bằng tests/rehearsal.

**READY FOR COURSE COMPLETION IMPLEMENTATION APPROVAL**
