# Quy tắc dữ liệu Học viên khi đồng bộ CSDT_V2 → QLHV_APP (Phase B2.5)

Tài liệu chốt các quy tắc chuẩn hóa/lọc dữ liệu **trước khi** có bất kỳ thao tác ghi/upsert
nào vào `QLHV_APP`. Đây là tài liệu, KHÔNG thực thi: không kết nối SQL, không chạy script,
không ghi dữ liệu, không chứa bí mật.

Nguyên tắc nền: **V2 là nguồn gốc chính xác, đồng bộ một chiều, chỉ đọc ở phía nguồn.**
Phase đồng bộ phải bảo toàn giá trị gốc, KHÔNG tự ý biến đổi dữ liệu.

---

## 1. Số CCCD / Số CMT (`SoCCCD` ← `NguoiLX.SoCMT`)

**Quy tắc chốt:**
- **Lưu nguyên giá trị gốc** từ `NguoiLX.SoCMT` (varchar(20)) vào `App_HocVien.SoCCCD`.
  Chỉ `TRIM` khoảng trắng thừa; KHÔNG thay đổi nội dung số.
- **KHÔNG tự động chuyển** CMND 9 số sang CCCD 12 số. Không suy đoán, không "độn" số 0,
  không tra cứu chuyển đổi. Việc chuyển đổi (nếu cần) là nghiệp vụ riêng, có kiểm soát, không
  nằm trong luồng đồng bộ.
- **Đánh dấu cảnh báo chất lượng dữ liệu (về sau):** giá trị không đúng 12 chữ số (sau khi trim)
  sẽ được gắn cờ cảnh báo ở `App_HocVien.CanhBaoDuLieu` / `TrangThaiKiemTra` ở Phase sau —
  **chỉ cảnh báo, KHÔNG chặn và KHÔNG sửa** giá trị.
- Giá trị rỗng/null ở nguồn → lưu null, gắn cảnh báo "thiếu CCCD" về sau.

**Hằng số tham chiếu:** độ dài CCCD kỳ vọng = **12** chữ số (xem `HocVienDataRules.CccdExpectedLength`).

---

## 2. Giới tính (`GioiTinh` ← `NguoiLX.GioiTinh`)

Schema thật: `NguoiLX.GioiTinh` kiểu **`char(1)`**.

**Quy tắc chốt:**
- **Tầng đồng bộ giữ NGUYÊN giá trị gốc** `char(1)` (chỉ trim). Không quy đổi ở bước đọc/đồng bộ.
- **KHÔNG đoán/không hardcode** quy đổi "Nam"/"Nữ" khi chưa xác nhận tập giá trị thật ở V2.
- Việc hiển thị "Nam"/"Nữ" chỉ áp dụng **sau khi xác nhận** tập giá trị thật (ví dụ '1'/'0',
  'M'/'F', hoặc 'N'/'Nu'...). Bảng quy đổi sẽ được bổ sung vào mục 5 khi có dữ liệu xác nhận.
- Trước khi xác nhận: tầng hiển thị hiện giá trị gốc, không tự diễn giải.

---

## 3. Trạng thái / bản ghi đã hủy (`TrangThai`)

Schema thật: `NguoiLX.TrangThai`, `NguoiLX_HoSo.TrangThai`, `KhoaHoc.TrangThai` đều kiểu **`bit`**.
Ý nghĩa nghiệp vụ của bit (1 = đang dùng? 0 = đã hủy/ẩn?) **chưa được xác nhận**.

**Quy tắc chốt cho Phase B2/B3:**
- **Mặc định ĐỌC TẤT CẢ bản ghi**, không lọc theo `TrangThai`, cho tới khi có quy tắc hủy/trạng thái
  được xác nhận chính thức.
- Lý do: lọc nhầm có thể bỏ sót học viên hợp lệ. An toàn hơn là đọc đủ rồi gắn cảnh báo sau.
- **Bộ lọc tùy chọn trong tương lai (chưa bật):** khi xác nhận ý nghĩa `TrangThai`, có thể thêm
  điều kiện `WHERE` tùy chọn (ví dụ `hs.TrangThai = 1`) — phải là tùy chọn cấu hình, mặc định tắt,
  và được ghi rõ trong nhật ký đồng bộ.

---

## 4. Quy tắc chung khi đồng bộ

- **Bảo toàn giá trị gốc:** chỉ `TRIM`; không đổi hoa/thường, không bỏ dấu, không định dạng lại.
- **Ngày sinh:** `NguoiLX.NgaySinh` là `varchar(8)` `yyyyMMdd`; chuyển sang `date` bằng
  `TRY_CONVERT(date, …, 112)`. Giá trị không hợp lệ → null + cảnh báo về sau (không chặn).
- **SourceOfTruth:** mọi bản ghi đồng bộ đánh dấu `App_HocVien.SourceOfTruth = N'V2'`.
- **Không xóa vật lý:** đồng bộ không xóa; dùng `IsDeleted` theo chuẩn QLHV_APP nếu cần ở Phase sau.
- **Cảnh báo, không chặn:** mọi vấn đề chất lượng dữ liệu được ghi nhận dạng cảnh báo
  (`CanhBaoDuLieu`/`TrangThaiKiemTra`), không làm dừng đồng bộ và không tự sửa dữ liệu.

---

## 5. Bảng quy đổi cần xác nhận (chưa chốt)

| Trường | Cần xác nhận | Trạng thái |
| --- | --- | --- |
| `GioiTinh` (char(1)) | Tập giá trị thật ở V2 và quy đổi sang "Nam"/"Nữ" | Chưa xác nhận |
| `SoCMT` | Tỷ lệ dữ liệu đã là CCCD 12 số vs CMND 9 số | Chưa xác nhận |
| `TrangThai` (bit) | Ý nghĩa 0/1 và bản ghi nào coi là "đã hủy" | Chưa xác nhận |

---

## 6. Câu hỏi chưa giải quyết

1. Tập giá trị thật của `GioiTinh char(1)` ở CSDT_V2?
2. Tỷ lệ/thực trạng `SoCMT` (CCCD 12 số vs CMND 9 số) để định cỡ cảnh báo chất lượng?
3. Ý nghĩa `TrangThai` bit ở `NguoiLX` / `NguoiLX_HoSo` / `KhoaHoc`; bản ghi nào là "đã hủy"?
4. Có cần chuẩn hóa/cảnh báo thêm cho `NgaySinh` ngoài việc parse thất bại?

## Liên quan
- Ánh xạ trường: [`hoc-vien-v2-mapping.md`](./hoc-vien-v2-mapping.md)
- Thiết kế đồng bộ: [`sync-v2-design.md`](./sync-v2-design.md)
