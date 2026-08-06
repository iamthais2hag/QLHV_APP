# Course completion — data mapping and ownership

## 1. Mapping identity

Không có một khóa số dùng chung cho cả ba lớp dữ liệu. Mọi thao tác tương lai phải mang đủ profile và natural identity, không nối chỉ theo tên khóa hoặc số lượng bản ghi.

Operator Course Completion Contract V1 đã chọn **marker-only** trong QLHV_APP. V2 và V1 chỉ được đọc để tạo/verify snapshot; completion không mutation bất kỳ source/downstream table nào.

| Entity | V2 source | V1 target/downstream | QLHV_APP | Quy tắc |
|---|---|---|---|---|
| Khóa học | `KhoaHoc.MaKH`, kèm `MaCSDT`, `MaSoGTVT` | cùng natural keys sau forward sync | `App_KhoaHoc.KhoaHocId`, `SourceProfileCode`, `MaKhoa`/source identity | Exact profile + source key; không match theo tên. |
| Học viên/hồ sơ | `NguoiLX_HoSo.MaDK`; `MaKhoaHoc` | `MaDK`; downstream gắn `MaBC2`, exam/GPLX | `App_HocVien.HocVienId`, `MaDK`, `MaKhoa`, `SourceProfileCode` | `MaDK` unique trong source; confirm cần khóa học khớp. |
| Báo cáo I | `BaoCaoI.MaBCI`, `MaKH`; hồ sơ dùng `MaBC1` | giữ source identity | hiện không có completion entity | `MaBC1` là V2-owned; không tự tạo/sửa trong completion. |
| Báo cáo II | có object ở source package nhưng lifecycle downstream | V1-owned từ lúc tổng hợp/gửi/phê duyệt | không thuộc assignment domain | Snapshot read-only; completion không ghi. |
| Giáo viên/xe | bảng catalog + mapping khóa | được sync theo contract | `App_GiaoVien*`, `App_XeTap*`, nhóm/phân công QLHV-owned | Diagnostic/warning, không phải hard gate V1. |

## 2. Ownership matrix

| Data group | Owner | Hướng ghi hợp lệ | Completion được làm gì |
|---|---|---|---|
| Thông tin khóa nguồn, `KhoaHoc.TrangThai`, `HTDaoTao` | V2 | V2 → V1 và snapshot → QLHV | Chỉ ghi qua contract V2 được phê duyệt; không sửa trực tiếp V1. |
| `MaKhoaHoc`, `MaBC1` | V2 | V2 → V1 | Bảo toàn; đổi khóa là workflow riêng. |
| Kết quả đào tạo (`KQ*`, điểm, giờ/km, `TGBatDau/TGKetThuc`, `KetLuanCSDT`) | V2 | V2 → V1 khi lifecycle còn thuộc vùng đào tạo | Read-only snapshot/eligibility; completion không sửa. |
| `TT_XuLy` `01`-`10` | V2 training lifecycle | V2 → V1 | Read-only; `09/10` hợp lệ khi result đầy đủ, `01`-`08` block. |
| `TT_XuLy` `11`-`19`, `90`, `MaBC2`, exam/GPLX signals | V1 downstream | V1 local | Read để block/conflict; tuyệt đối không overwrite. |
| `SoGiayCNTN`/`SoCCN` | V2 source field nhưng có downstream side effect/consumer | Preserve once present | Không sinh, sửa, xóa hoặc tái tạo trong Completion V1. |
| Trạng thái completion nội bộ, preview, idempotency, audit | QLHV | QLHV only | Lưu ở entity riêng; server UTC; không dùng client clock. |
| Nhóm, phân công, manual override, `App_KhoaHoc.TrangThai` hiện tại | QLHV | QLHV only | Bảo toàn nguyên trạng; completion không được reuse mù quáng. |

Evidence của policy runtime:

- `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeColumnOwnershipPolicy.cs:205-214`: cột nguồn khóa học, gồm `TrangThai` và `HTDaoTao`, thuộc source V2.
- `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeColumnOwnershipPolicy.cs:254-293`: phân loại trường hồ sơ, lifecycle và downstream.
- `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeForwardWritePlanner.cs:8-24`: vùng trạng thái đào tạo `01`-`10` và downstream `11`-`19`/`90`.
- `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeForwardWritePlanner.cs:234-319,365-400`: preserve V1 lifecycle và phát hiện conflict.
- `server/QLHV.Infrastructure/Sync/QlhvCourseTeacherFullSnapshotSyncSql.cs:414-475`: full snapshot cập nhật source-owned fields nhưng bảo toàn trạng thái QLHV-owned của `App_KhoaHoc`.
- `server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeCycleProcessor.cs:1452-1463`: hash bảo toàn nhóm field QLHV-owned.

## 3. Mutation table — behavior thực tế của phần mềm cũ

Bảng này mô tả mutation **đã xác minh**, không phải đề xuất chạy.

| Database | Table | Column | Before | After | Condition | Owner | Evidence |
|---|---|---|---|---|---|---|---|
| V2 OTO/MOTO | `NguoiLX_HoSo` | `KQLyThuyet`, `KQThucHanh`, `TGBatDau`, `TGKetThuc`, `KetLuanCSDT`, điểm, giờ/km, `QDThucHanhHinh` | current/nullable | dữ liệu form của một học viên | exact `MaDK` + `MaKhoaHoc`; validation theo hạng ở UI | V2 | `usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat`, script 17029-17078 |
| V2 OTO/MOTO | `NguoiLX_HoSo` | `TT_XuLy` | current | giữ `90/13/14`; đạt → `09`; không đạt → `10`; khác → giữ | cùng procedure | shared, V2 trong training state | script 17029-17078 |
| V2 OTO/MOTO | `NguoiLX_HoSo` | `NgaySua` | current | `GETDATE()` | cùng procedure | V2 metadata | script 17029-17078 |
| V2 OTO/MOTO | `KhoaHoc` | `TrangThai` | thường `1` | giá trị truyền vào; UI khóa truyền `0` | `MaKH` | V2 | `usp_KhoaHoc_Update_TrangThai`, script 12242-12265; IL `tsbKhoa_Click` |
| V2 OTO/MOTO | `KhoaHoc` | `NguoiSua`, `NgaySua` | current | actor, `GETDATE()` | khóa/mở trạng thái chung | V2 | script 12242-12265 |
| V2/V1 theo workflow BCII cũ | `NguoiLX_HoSo` | `TT_XuLy`, `TT_XuLy_Old` | `03/04` hoặc pass `09` | `11`, lưu trạng thái cũ | học viên được chọn/tổng hợp | chuyển tiếp sang V1 lifecycle | `usp_NguoiLX_TongHop_By_MaKH2`, script 17614-17658 |
| V1/downstream | `NguoiLX_HoSo` | `MaBC2`, `TT_XuLy`, `SoGiayCNTN`, `NgayRaQDTN`, `NgaySua` | chưa gửi | mã BC2, `12`, số/ngày chứng nhận | học viên được chọn khi xuất XML | V1 downstream | `ucKetXuatKetQuaDaoTao.btnKetXuatXML_Click` IL |
| V1/downstream | `BaoCaoII` | `TrangThai`, `NgaySua` | chưa phê duyệt | `1`, `GETDATE()` | tiếp nhận kết quả phê duyệt | V1 | `usp_BaoCaoII_Update_PheDuyetKQDT`, script 4057-4075 |
| V1/downstream | `NguoiLX_HoSo` và `NguoiLX` | approval/identity fields, `TT_XuLy` | yêu cầu `12` | đạt → `13`, không đạt → `14` | per learner approval | V1 | `usp_CSDT_PheDuyetKQDT_TiepNhan`, script 4407-4503 |
| Không có mutation | report dataset | none | none | none | learner status `13` được chọn | read-only | `usp_NguoiLX_HoSo_RPT_CNTN`, script 14780-14855; `btnKetXuatHTKH_Click` |

Không có hàng “course completion” trong source/V1 vì không tìm thấy procedure/trigger/direct SQL nào thực hiện operation đó. Contract V1 bổ sung duy nhất mutation QLHV dưới đây:

| Database | Table | Column | Before | After | Condition | Owner | Evidence |
|---|---|---|---|---|---|---|---|
| QLHV_APP | `App_CourseCompletion` | identity, status, business date, snapshot hash, actor/UTC metadata, rowversion | chưa có active marker | một marker `COMPLETED` | sealed preview còn khớp; mọi learner thuộc `09`, `10` hoặc `11`-`19`; không blocker | QLHV | Operator Course Completion Contract V1 §1, §6, §9-10 |
| QLHV_APP | completion learner snapshot | exact learner identity, status/result/downstream classification, canonical row hash | chưa có | immutable snapshot cho toàn bộ exact scope | cùng transaction với marker | QLHV | Contract V1 §6, §10 |
| QLHV_APP | idempotency ledger | key hash, request fingerprint, result | chưa có hoặc same replay | durable result/NO_CHANGE | unique key/fingerprint contract | QLHV | Contract V1 §10 |
| QLHV_APP | `App_AuditLog`/completion audit detail | before/after summary | chưa có | actor, business date, SQL/API UTC, before/after hashes | chỉ sau revalidation | QLHV | Contract V1 §9-10 |

## 4. Schema/cột cốt lõi

### 4.1 `KhoaHoc`

Các trường liên quan quan sát được gồm identity, ngày khai giảng/bế giảng, tổng chỉ tiêu/thời lượng, `TrangThai`, `TT_Xuly`, `HTDaoTao`, metadata sửa. Đặc điểm an toàn:

- `TrangThai` là `bit NOT NULL DEFAULT 1` và được UI cũ dùng làm edit lock;
- các ngày có thể `NULL`;
- không có `CompletedAt`, `CompletedBy`, completion reason hay rowversion;
- không có trigger/check constraint completion;
- `HTDaoTao` tham chiếu hình thức đào tạo (`DM_HinhThucDT`: tập trung hoặc từ xa/tự học), không phải cờ hoàn thành.

### 4.2 `NguoiLX_HoSo`

- PK/unique `MaDK`; `MaKhoaHoc` nullable và FK cascade tới `KhoaHoc`;
- `TT_XuLy` bắt buộc, các result/certificate/downstream field phần lớn nullable;
- V2 có `QDThucHanhHinh`, V1 không có;
- không có rowversion, trigger hoặc check completion;
- không có cột authoritative đã xác minh cho bảo lưu/nghỉ học/chuyển khóa/ngừng đào tạo.

### 4.3 Báo cáo và chứng từ

- `BaoCaoI`: PK `MaBCI`, unique `SoBaoCao`, FK `MaKH` cascade.
- `BaoCaoII`: PK `MaBCII`; `MaBCI` nullable nhưng không có FK tới `BaoCaoI`.
- `NguoiLXHS_GiayTo`: PK `(MaGT, MaDK)`, FK học viên cascade; procedure result không dùng làm gate.

Thiếu FK `BaoCaoII.MaBCI` và thiếu check/trigger có nghĩa là preview phải kiểm tra bằng query explicit; không thể dựa vào database constraint để bảo đảm lifecycle.

## 5. Mapping trạng thái Contract V1

| Business stage | `NguoiLX_HoSo.TT_XuLy` | Authority | Completion handling đề xuất |
|---|---|---|---|
| Trước/đang đào tạo | `01`-`08` | V2 | `BLOCKED`; không ghi marker. |
| Đạt/không đạt tại CSDT | `09`/`10` | V2 | READY nếu exact result fields theo hạng đầy đủ; snapshot read-only. |
| Đã tổng hợp/tạo BCII | `11` | V1 lifecycle bắt đầu | READY read-only; không sửa BCII/source/downstream. |
| Đã gửi/chờ duyệt | `12` | V1 | READY read-only; không sửa. |
| Sở phê duyệt đạt/không đạt | `13`/`14` | V1 | READY read-only; không sửa. |
| Downstream/sát hạch/GPLX | `15`-`19` | V1 | READY read-only nếu exact status được phân loại; correction riêng nếu có sai sót. |
| Ngoại lệ/giải trình | `90` | shared/downstream-sensitive | `BLOCKED` và manual review. |

## 6. Mapping QLHV_APP hiện tại

`App_KhoaHoc` hiện chứa source identity, source-owned snapshot và các field QLHV-owned như `TrangThai`, `GhiChuNoiBo`, `RowVersion`. Không có field mang nghĩa completion business rõ ràng. `GetCourseDetailAsync` đọc các cột này tại `SqlAssignmentRepository.CatalogWrites.cs:273-290`.

Không nên đổi nghĩa `App_KhoaHoc.TrangThai`, vì:

- nó đã thuộc ownership/hashing của module assignment;
- full convergence cố tình bảo toàn field QLHV-owned;
- dùng lại sẽ trộn lifecycle phân công với lifecycle kết quả đào tạo;
- không lưu được snapshot, reason, idempotency hay correction state.

Nếu implementation được duyệt, dùng entity QLHV-owned riêng, tên cuối cùng phải qua migration review. Draft tối thiểu:

```text
App_CourseCompletion
  CourseCompletionId
  KhoaHocId
  SourceProfileCode
  SourceCourseKey
  Status
  SourceSnapshotHash
  CompletionOperationId / IdempotencyKeyHash
  CompletionBusinessDate
  CompletedAtUtc / CompletedBy / CompletionReason
  RowVersion
```

Cần unique completion marker theo `(SourceProfileCode, SourceCourseKey)` và ledger/audit detail theo từng learner. Đây chỉ là mapping plan, **không phải migration được phép chạy**.

## 7. Before/after contract Completion V1

| Layer | Before | After hợp lệ | Không được thay đổi |
|---|---|---|---|
| V2 course | snapshot exact | NO_CHANGE | toàn bộ source row |
| V2 learners | sorted sealed snapshot | NO_CHANGE | toàn bộ source rows/result/status |
| V1 | current lifecycle snapshot | NO_CHANGE | `MaBC2`, BCII, exam, GPLX và mọi V1-owned field |
| QLHV | no active completion | one completion record + immutable audit | assignment/groups/manual overrides |

Confirm phải kiểm tra exact identity, revalidate snapshot hash sát thời điểm commit và chỉ ghi marker/snapshot/ledger/audit trong một QLHV transaction. Nếu source đổi giữa preview và confirm thì trả `CONFLICT`; không sửa source để ép khớp.

## 8. Correction boundary; không có reopen V1

| Tình trạng | Completion V1 | Lý do |
|---|---|---|
| Chỉ có QLHV marker | Không có reopen endpoint/mutation | Sai sót chuyển correction workflow riêng, có audit mới. |
| V2 result thay đổi sau marker | Marker chuyển trạng thái cần review theo correction contract tương lai; không auto-update | Completion snapshot phải bất biến và chứng minh state tại thời điểm xác nhận. |
| BCII đã tạo/chưa phê duyệt | Không đảo trong completion | Downstream workflow độc lập. |
| BCII đã phê duyệt | Không đảo | UI cũ chặn hủy; status/data downstream đã đổi. |
| Có kỳ sát hạch/kết quả/GPLX | Không đảo | Authority thuộc V1/downstream. |

## 9. Kết luận

Contract V1 loại bỏ source mutation và reopen khỏi scope. Mapping đã rõ: source/V1 read-only, còn marker/snapshot/ledger/audit là QLHV-owned và atomic trong QLHV_APP.

**READY FOR COURSE COMPLETION IMPLEMENTATION APPROVAL**
