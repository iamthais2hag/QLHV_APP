# Chất lượng dữ liệu xe và giáo viên CSDL_OTO

## Snapshot quan sát

Thời điểm nghiên cứu: 2026-07-28, chỉ đọc `CSDL_OTO`. Không xuất raw CCCD/CMND, họ tên, địa chỉ, điện thoại hoặc đường dẫn file. Các script tái lập nằm trong `scripts/sql/analysis`.

### Xe tập lái

| Metric | Giá trị | Đánh giá |
|---|---:|---|
| Tổng / active / inactive | `29 / 29 / 0` | Đủ trạng thái bit hiện hành |
| Thiếu biển số, số khung, số máy, đăng ký, hạng, nhãn hiệu | `0` cho mỗi nhóm | PASS |
| Duplicate group theo biển số chuẩn hóa, đăng ký, số khung, số máy | `0` cho mỗi nhóm | PASS tại snapshot |
| Giấy phép tập lái hết hạn / kiểm định hết hạn | `0 / 0` | PASS tại snapshot |
| Bảo hiểm `false` | `4` | Cảnh báo nghiệp vụ; không tự inactive |
| Thiếu ảnh | `0` | Có giá trị nguồn |
| Ảnh absolute/rooted | `29` | Không thể dùng trực tiếp làm URL/client path |
| Xe có quan hệ active | `0` | Không có dữ liệu phân xe hiện tại |
| Orphan | `0` | PASS, nhưng bảng quan hệ đang rỗng |

Các trạng thái thanh lý, chờ duyệt và xóa mềm không tồn tại trong schema. Báo cáo phải hiển thị `N/A`, không chuyển thành `0`.

### Giáo viên

| Metric | Giá trị | Đánh giá |
|---|---:|---|
| Tổng / active / inactive | `48 / 48 / 0` | Đủ trạng thái bit hiện hành |
| Thiếu mã, GPLX, cơ sở, ảnh, ngày hết hạn GPLX | `0` cho mỗi nhóm | PASS |
| Thiếu số/ngày GCN và hạng được đào tạo | `2 / 2 / 2` | Cần review trước khi dùng cho eligibility |
| GPLX hết hạn | `0` | PASS tại snapshot |
| Duplicate giấy tờ / họ tên+ngày sinh | `0 / 0` | PASS tại snapshot |
| `NgaySinh` hợp lệ `YYYYMMDD` | `0` | Contract hiện hành sai với data |
| `NgaySinh` hợp lệ `DDMMYYYY` | `48` | Data nhất quán theo định dạng thực tế |
| Ảnh absolute/rooted | `48` | Cần ingestion/copy có allow-list |
| Được phân khóa / không có quan hệ | `8 / 40` | Quan hệ có dữ liệu nhưng còn mỏng |
| Được phân xe | `0` | Chưa có dữ liệu |
| Orphan | `0` | PASS |

### Tính tin cậy của audit time

- Xe: `NgayTao` từ `2026-07-27T16:38:29.823` đến `16:38:30.003`.
- Giáo viên: `NgayTao` từ `2026-07-27T16:38:51.383` đến `16:38:51.690`.

Các cửa sổ dưới một giây cho toàn bộ danh mục cho thấy timestamp là một sự kiện nạp hàng loạt, không chứng minh thời điểm đối tượng thực sự trở thành “mới”. Procedure update cũng cho phép cập nhật `NgayTao`. Vì vậy newness phải dựa membership/source identity và secondary collision guard.

## Rủi ro theo mức độ

| Mức | Rủi ro | Guard bắt buộc |
|---|---|---|
| P0 | Parser `NgaySinh` QLHV dùng `yyyyMMdd` trong khi 48/48 nguồn là `DDMMYYYY` | Contract theo profile, validate 100%, backfill có preview |
| P0 | Ảnh nguồn đều absolute; target hiện mất toàn bộ ảnh | Copy qua service allow-listed, content validation, relative managed key |
| P1 | Nguồn không unique CCCD, số khung, số máy, đăng ký | Preflight duplicate/conflict; manual review |
| P1 | Liên kết logic `KhoaHoc_XeTap.MaGV` và `KhoaHoc_GiaoVien.BienSoXe` không FK | Orphan guard trước write và target FK/source identity |
| P1 | Snapshot absence procedures có thể inactive/delete diện rộng | Không tái dùng cho realtime; empty-partition guard; không hard-delete |
| P1 | Source có physical-delete procedures | CT/tombstone reconciliation và audit độc lập |
| P2 | 4 xe không còn bảo hiểm hiệu lực | Cảnh báo/eligibility riêng, không tự sửa nguồn |
| P2 | 2 giáo viên thiếu bộ GCN/hạng được dạy | Manual review; không xếp lịch theo dữ liệu thiếu |
| P2 | `MAX+1` sinh `MaGV` có race | QLHV không gọi generator cho source identity; target key theo profile |

## Trạng thái QLHV_APP quan sát

| Đối chiếu | Kết quả |
|---|---:|
| Giáo viên nguồn có target active | `48/48` |
| Quan hệ giáo viên–khóa có target active | `8/8` |
| Giáo viên target thiếu ngày sinh / ảnh | `48 / 48` |
| Xe nguồn có target active | `0/29` |

Đây chỉ là quan sát, không phải xác nhận pipeline được phép chạy. Task này không bật Auto Sync và không ghi target.

## Acceptance gate cho task triển khai

1. Snapshot ổn định ít nhất ba mẫu; source/target identity fingerprint không đổi trong lúc seal.
2. Duplicate group bằng 0 hoặc mọi collision có quyết định review rõ ràng.
3. Parser ngày đạt 100% với contract duy nhất; path ảnh được classify và copy an toàn.
4. FK/logical orphan bằng 0; quan hệ chỉ nạp sau master.
5. Empty source partition không được làm inactive/delete target.
6. Missing source chỉ chuyển review/soft-inactive; tuyệt đối không hard-delete tự động.
7. Writer mutex loại trừ Auto Sync và learner realtime; checkpoint riêng từng domain.
