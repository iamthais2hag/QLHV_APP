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

> **`V2_schema_full.sql` KHÔNG có trong repo.** Các cột nguồn V2 dưới đây được lấy từ
> danh sách cột thật mà script chuyển dữ liệu ghi vào `CSDT_V2.dbo.*`, nên độ tin cậy cao.
> Tuy nhiên việc **chốt cuối cùng** vẫn cần một bản xuất schema V2 chỉ đọc
> (`V2_schema_full.sql`) hoặc xác nhận của người quản trị CSDT.

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
| 7 | Địa chỉ thường trú | `DiaChiThuongTru` | `NguoiLX.NoiTT` | Cao (xem ghi chú địa chỉ) |
| 8 | Số GPLX đã có | `SoGPLXDaCo` | `NguoiLX_HoSo.SoGPLXDaCo` | Cao |
| 9 | Hạng GPLX đã có | `HangGPLXDaCo` | `NguoiLX_HoSo.HangGPLXDaCo` | Cao |
| 10 | Người nhận hồ sơ | `NguoiNhanHoSo` | `NguoiLX_HoSo.NguoiNhanHSo` | Cao |
| 11 | Tên khóa | `TenKhoa` | `KhoaHoc.TenKH` (join qua `MaKhoaHoc`) | Cao |
| 12 | Mã khóa | `MaKhoa` | `NguoiLX_HoSo.MaKhoaHoc` (≡ `KhoaHoc.MaKH`) | Cao |

Trường liên quan (không nằm trong 11 cột hiển thị nhưng nên đồng bộ kèm):

| Đích `App_HocVien` | Nguồn CSDT_V2 | Ghi chú |
| --- | --- | --- |
| `HangGPLXHoc` | `NguoiLX_HoSo.HangGPLX` | Hạng đào tạo của khóa hiện tại. |
| `SourceOfTruth` | hằng `'V2'` | Đánh dấu nguồn gốc. |
| `V2RowHash` | hash các cột nguồn | Phát hiện thay đổi để upsert ở Phase B2. |
| `LastSyncFromV2At` / `LastSyncStatus` / `LastSyncMessage` | sinh khi đồng bộ | Vết đồng bộ. |

## Ghi chú quan trọng

- **CCCD vs CMT:** `NguoiLX.SoCMT` là cột chứa số định danh hiện hành; `NguoiLX.SO_CMND_CU`
  là số CMND cũ. Script kiểm tra trùng (`CHECK_CCCD_CONFLICT_V1_V2.sql`) dùng `SoCMT` như CCCD.
  → Ánh xạ `SoCCCD ← SoCMT`, nhưng cần xác nhận dữ liệu thực tế đã là CCCD 12 số.
- **Địa chỉ thường trú:** dùng cột văn bản `NguoiLX.NoiTT`. Các cột mã (`NoiTT_MaDVHC`,
  `NoiTT_MaDVQL`) là mã đơn vị hành chính, không dùng để hiển thị địa chỉ.
- **GPLX đã có vs GPLX được cấp:** "đã có" lấy từ `NguoiLX_HoSo.SoGPLXDaCo`/`HangGPLXDaCo`
  (giấy phép học viên đã sở hữu trước). `NguoiLX_GPLX` là GPLX do khóa này cấp (đầu ra),
  KHÔNG dùng cho cột "đã có".
- **Nhiều dòng theo `MaDK`:** một `MaDK` có thể có nhiều dòng ở `NguoiLX_GPLX`/`NguoiLX_HoSo`.
  Khi đồng bộ cần chọn 1 dòng hồ sơ hiện hành (theo khóa đang xét / dòng mới nhất).

## Câu hỏi chưa giải quyết (cần chốt trước Phase B2)

1. `V2_schema_full.sql` chưa có — cần bản xuất schema V2 chỉ đọc để chốt kiểu dữ liệu/độ dài cột.
2. Dữ liệu `SoCMT` hiện đã chuẩn hóa sang CCCD 12 số chưa, hay còn lẫn CMND 9 số?
3. Quy tắc chọn 1 hồ sơ/khóa hiện hành khi 1 `MaDK` thuộc nhiều khóa.
4. `NguoiNhanHSo` lưu tên người hay mã người dùng? Ảnh hưởng cách hiển thị "Người nhận hồ sơ".
5. Có cần lọc `TrangThai`/bản ghi đã hủy ở nguồn V2 trước khi đồng bộ không?

## So khớp với QLHV_APP (đích)

Tất cả 11 cột hiển thị đều có cột đích tương ứng trong `dbo.App_HocVien` (đã xác nhận từ schema).
Không phát hiện cột hiển thị nào thiếu chỗ lưu ở đích. `dbo.App_DongBoLog` đủ cột để ghi
kết quả mỗi lần đồng bộ (`TotalRead/Inserted/Updated/Skipped/Error`, `Status`, thời gian, `DetailJson`).

## Liên quan
- Thiết kế đồng bộ: [`sync-v2-design.md`](./sync-v2-design.md)
- Cài đặt database: [`database-setup.md`](./database-setup.md)
