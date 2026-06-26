# Ánh xạ Học viên: CSDT_V2 → QLHV_APP (Phase B1)

Tài liệu xác nhận ánh xạ trường cho phân hệ **Học viên** khi đồng bộ một chiều
từ `CSDT_V2` sang `QLHV_APP`. Tài liệu, KHÔNG thực thi: không kết nối SQL Server,
không chạy script, không ghi dữ liệu, không chứa chuỗi kết nối/bí mật.

## Nguồn dữ liệu đã dùng để xác nhận

| Nguồn | Vai trò |
| --- | --- |
| `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql` | Schema đích QLHV_APP (chính xác, đã đọc). |
| `database/TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql` | Cột nguồn CSDT_V2 (suy ra từ danh sách cột `INSERT INTO CSDT_V2.dbo.*`). |
| `database/CHECK_CCCD_CONFLICT_V1_V2.sql` | Xác nhận `SoCMT` được dùng như CCCD trong nghiệp vụ. |

> **`V2_schema_full.sql` ĐÃ CÓ** tại `database/reference/V2_schema_full.sql`. Mapping dưới đây
> đã được đối chiếu trực tiếp với DDL thật của CSDT_V2 (độ tin cậy cao). Các điểm cần lưu ý về
> kiểu dữ liệu đã được ghi chú bên dưới.

## Bảng đích QLHV_APP

- `dbo.App_HocVien` — bản ghi học viên.
- `dbo.App_KhoaHoc` — khóa học (tra cứu Tên khóa).
- `dbo.App_DongBoLog` — nhật ký mỗi lần đồng bộ.

## Bảng nguồn CSDT_V2 và liên kết

- `CSDT_V2.dbo.NguoiLX` — thông tin nhân thân người lái xe. Khóa: `MaDK`.
- `CSDT_V2.dbo.NguoiLX_HoSo` — hồ sơ học viên theo khóa. Khóa: `MaDK`; gắn khóa học qua `MaKhoaHoc`.
- `CSDT_V2.dbo.NguoiLX_GPLX` — GPLX được cấp (đầu ra của khóa). Khóa: `MaDK`.
- `CSDT_V2.dbo.KhoaHoc` — khóa học. Khóa: `MaKH`.

Liên kết dự kiến:

```
NguoiLX (MaDK)
   1───1  NguoiLX_HoSo (MaDK)
                 │  MaKhoaHoc
                 └──► KhoaHoc (MaKH)
```

## Bảng ánh xạ đã xác nhận

| # | Cột hiển thị | Đích `App_HocVien` | Nguồn CSDT_V2 | Độ tin cậy |
| --- | --- | --- | --- | --- |
| 1 | STT | (không lưu) | số thứ tự hiển thị, không đồng bộ | — |
| 2 | Mã đăng ký | `MaDK` | `NguoiLX_HoSo.MaDK` (≡ `NguoiLX.MaDK`) | Cao |
| 3 | Họ và tên | `HoTen` | `NguoiLX.HoVaTen` | Cao |
| 4 | Ngày sinh | `NgaySinh` | `NguoiLX.NgaySinh` | Cao |
| 5 | Giới tính | `GioiTinh` | `NguoiLX.GioiTinh` | Cao |
| 6 | Số CCCD | `SoCCCD` | `NguoiLX.SoCMT` | Cao (xem ghi chú CCCD/CMT) |
| 7 | Địa chỉ thường trú | `DiaChiThuongTru` | `DM_DVHC.TenDayDu` qua `NguoiLX.NoiTT_MaDVQL + NguoiLX.NoiTT_MaDVHC = DM_DVHC.MaDV`; fallback `NguoiLX.NoiTT` | Cao (xem ghi chú địa chỉ) |
| 8 | Số GPLX đã có | `SoGPLXDaCo` | `NguoiLX_HoSo.SoGPLXDaCo` | Cao |
| 9 | Hạng GPLX đã có | `HangGPLXDaCo` | `NguoiLX_HoSo.HangGPLXDaCo` | Cao |
| 10 | Người nhận hồ sơ | `NguoiNhanHoSo` | `NguoiLX_HoSo.NguoiNhanHSo` | Cao |
| 11 | Tên khóa | `TenKhoa` | `KhoaHoc.TenKH` (join qua `MaKhoaHoc`) | Cao |
| 12 | Mã khóa | `MaKhoa` | `NguoiLX_HoSo.MaKhoaHoc` (≡ `KhoaHoc.MaKH`) | Cao |

Trường liên quan (không nằm trong 11 cột hiển thị nhưng nên đồng bộ kèm):

| Đích `App_HocVien` | Nguồn CSDT_V2 | Ghi chú |
| --- | --- | --- |
| `HangGPLXHoc` | `NguoiLX_HoSo.HangDaoTao` → `DM_HangDT.MaHangDT` → `DM_HangDT.TenHangDT` | Hạng đào tạo/hạng học của khóa hiện tại. Không lấy từ `NguoiLX_HoSo.HangGPLX`. |
| `SourceOfTruth` | hằng `'V2'` | Đánh dấu nguồn gốc. |
| `V2RowHash` | hash các cột nguồn | Phát hiện thay đổi để upsert ở Phase B2. |
| `LastSyncFromV2At` / `LastSyncStatus` / `LastSyncMessage` | sinh khi đồng bộ | Vết đồng bộ. |

## Phase B3R readiness note

Current code maps and writes `MaHangDT` explicitly:

- `App_HocVien.MaHangDT` <- `NguoiLX_HoSo.HangDaoTao`.
- `App_HocVien.HangGPLXHoc` <- `DM_HangDT.TenHangDT` through `DM_HangDT.MaHangDT = NguoiLX_HoSo.HangDaoTao`.
- Both `MaHangDT` and `HangGPLXHoc` participate in `V2RowHash`.
- The main schema file already contains `App_HocVien.MaHangDT` and `IX_App_HocVien_MaHangDT`.
- The requested patch file `database/patches/20260610_add_mahangdt_app_hocvien.sql` is not present in the current repository state; confirm whether an already-applied migration/patch exists before any new local execute test.

## Ghi chú quan trọng (đã đối chiếu schema thật)

- **Kiểu dữ liệu `NgaySinh`:** `NguoiLX.NgaySinh` là `varchar(8)` dạng `yyyyMMdd`, KHÔNG phải kiểu date.
  Truy vấn đọc dùng `TRY_CONVERT(date, nlx.NgaySinh, 112)` để chuyển sang ngày an toàn.
- **Kiểu dữ liệu `GioiTinh`:** `NguoiLX.GioiTinh` là `char(1)`. Cần quy đổi sang "Nam"/"Nữ" ở tầng
  hiển thị; quy ước giá trị ('1'/'0' hay 'M'/'F') còn cần xác nhận dữ liệu thực tế.
- **`NguoiLX_HoSo` là 1 dòng / `MaDK`** (PK clustered trên `MaDK`) → giải tỏa lo ngại nhiều hồ sơ
  trùng `MaDK`. Liên kết `NguoiLX 1—1 NguoiLX_HoSo` theo `MaDK` là an toàn.
- **CCCD vs CMT:** `NguoiLX.SoCMT` `varchar(20)` là số định danh hiện hành; `SO_CMND_CU` là CMND cũ.
  → Ánh xạ `SoCCCD ← SoCMT`, vẫn cần xác nhận dữ liệu đã chuẩn hóa CCCD 12 số.
- **Địa chỉ thường trú:** ưu tiên `DM_DVHC.TenDayDu` bằng cách ghép `NguoiLX.NoiTT_MaDVQL + NguoiLX.NoiTT_MaDVHC`
  để khớp `DM_DVHC.MaDV`. Nếu không join được danh mục hành chính thì fallback `NguoiLX.NoiTT`.
- **GPLX đã có vs GPLX được cấp:** "đã có" lấy từ `NguoiLX_HoSo.SoGPLXDaCo`/`HangGPLXDaCo`
  (đã xác nhận tồn tại). `NguoiLX_GPLX` là GPLX do khóa này cấp (đầu ra), KHÔNG dùng cho cột "đã có".

## Câu hỏi chưa giải quyết (cần chốt trước Phase B3)

1. ~~`V2_schema_full.sql` chưa có~~ → **Đã có**, mapping đã đối chiếu DDL thật.
2. Dữ liệu `SoCMT` hiện đã chuẩn hóa sang CCCD 12 số chưa, hay còn lẫn CMND 9 số? (cần kiểm tra dữ liệu)
3. ~~Quy tắc chọn 1 hồ sơ/khóa~~ → **Giải tỏa**: `NguoiLX_HoSo` PK là `MaDK` (1 dòng/MaDK).
4. Quy ước giá trị `GioiTinh char(1)` → "Nam"/"Nữ" ('1'/'0' hay 'M'/'F'?) cần xác nhận từ dữ liệu thực.
5. Có cần lọc theo `TrangThai` (bit) ở `NguoiLX`/`NguoiLX_HoSo`/`KhoaHoc` để bỏ bản ghi đã hủy không?
6. Bộ lọc "Hạng GPLX/Hạng học" cho nguồn V2 cần tiếp tục được rà soát với UI, nhưng dữ liệu hạng học đồng bộ đã chốt
   theo `NguoiLX_HoSo.HangDaoTao` → `DM_HangDT.TenHangDT`, không lấy từ `NguoiLX_HoSo.HangGPLX`.

## So khớp với QLHV_APP (đích)

Tất cả 11 cột hiển thị đều có cột đích tương ứng trong `dbo.App_HocVien` (đã xác nhận từ schema).
Không phát hiện cột hiển thị nào thiếu chỗ lưu ở đích. `dbo.App_DongBoLog` đủ cột để ghi
kết quả mỗi lần đồng bộ (`TotalRead/Inserted/Updated/Skipped/Error`, `Status`, thời gian, `DetailJson`).

## Liên quan
- **Quy tắc chuẩn hóa/lọc dữ liệu (Phase B2.5):** [`hoc-vien-data-rules.md`](./hoc-vien-data-rules.md)
- Thiết kế đồng bộ: [`sync-v2-design.md`](./sync-v2-design.md)
- Cài đặt database: [`database-setup.md`](./database-setup.md)
