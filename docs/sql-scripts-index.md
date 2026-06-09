# Danh mục SQL scripts - QLHV_APP

Liệt kê toàn bộ file SQL trong thư mục [`database/`](../database) và mục đích của từng file.

> Tài liệu tham khảo. Không thực thi tự động. Mọi script chuyển/đổi mã dữ liệu mặc định
> chạy ở chế độ **DryRun** và chỉ chạy trên **database backup test**.

## Tổng quan nhóm

| Nhóm | File |
| --- | --- |
| Khởi tạo schema | `QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`, `QLHV_APP_DATABASE_PATCH_INDEX_JSON.sql` |
| Kiểm tra trước chuyển | `CHECK_CCCD_CONFLICT_V1_V2.sql` |
| Chuyển dữ liệu V1 → V2 | `TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql`, `FORCE_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN_IGNORE_CCCD.sql` |
| Đổi mã CSĐT | `REMAP_CSDT_CODE_IN_CSDT_V2.sql` |
| Kiểm tra sau chuyển | `VALIDATE_V1_TO_V2_CHUYEN_ALL_KHOA.sql`, `V2_POST_TRANSFER_HEALTH_CHECK.sql` |

---

## Chi tiết từng file

### `QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`
- **Loại:** Khởi tạo schema (bản tối ưu hiệu năng).
- **Tác động:** Tạo database `QLHV_APP` (nếu chưa có) và toàn bộ bảng `App_*`.
- **Đặc điểm:** Idempotent (`IF DB_ID ... IS NULL`, `IF OBJECT_ID ... IS NULL`); không DELETE vật lý
  (dùng `IsDeleted`); dùng `RowVersion` chống ghi đè đồng thời.
- **Khi dùng:** Bước đầu tiên khi dựng QLHV_APP.

### `QLHV_APP_DATABASE_PATCH_INDEX_JSON.sql`
- **Loại:** Patch nâng cấp.
- **Tác động:** Bổ sung index tra cứu, kiểm tra JSON, tối ưu lookup performance (ví dụ index trên
  `App_KhoaHoc_XeTap`: `MaKhoa`, `BienSoXe`, `MaGV` với filter `IsDeleted = 0`).
- **Đặc điểm:** Idempotent (`IF NOT EXISTS (... sys.indexes ...)` trước khi tạo).
- **Khi dùng:** Sau khi đã tạo schema, khi cần bổ sung index/JSON theo chuẩn hiệu năng.

### `CHECK_CCCD_CONFLICT_V1_V2.sql`
- **Loại:** Kiểm tra trước khi chuyển (read-only).
- **Tác động:** Chỉ `SELECT`, không ghi dữ liệu.
- **Mục đích:** Phát hiện các trường hợp **CCCD trùng nhưng MaDK khác** giữa `CSDT_V1` và `CSDT_V2`.
- **Tham số:** `@OnlyCoursesNotExistsInV2`, `@FromNgayKG`, `@ToNgayKG`.
- **Khi dùng:** Chạy **trước** khi chuyển ALL khóa để phát hiện xung đột.

### `TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql`
- **Loại:** Chuyển dữ liệu V1 → V2.
- **Source:** `CSDT_V1` → **Target:** `CSDT_V2`.
- **Mặc định:** `@DryRun = 1` (chạy thử + ROLLBACK, không ghi).
- **Mức chuyển mặc định:** `KhoaHoc`, `BaoCaoI`, `NguoiLX`, `NguoiLX_HoSo`, `NguoiLX_GPLX`, `NguoiLXHS_GiayTo`.
- **Tùy chọn:** `@CopyDaoTao = 1` (thêm `KhoaHoc_GiaoVien`, `LichHoc`); `@CreateKhoaHocXeTap = 1`
  (tạo `KhoaHoc_XeTap` từ `KhoaHoc_GiaoVien` nếu có `BienSoXe`); `@OnlyCoursesNotExistsInV2 = 1`
  (chỉ chuyển khóa chưa có trong V2).
- **Khi dùng:** Test chuyển toàn bộ khóa/học viên. Chỉ chạy trên backup.

### `FORCE_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN_IGNORE_CCCD.sql`
- **Loại:** Chuyển dữ liệu V1 → V2 (bản force).
- **Khác biệt:** **Tạm bỏ qua cảnh báo CCCD trùng nhưng MaDK khác.**
- **Mặc định:** `@DryRun = 1`.
- **Cảnh báo:** Chỉ chạy trên **database backup** `CSDT_V1`/`CSDT_V2`, **không** chạy trên DB gốc.
- **Khi dùng:** Khi đã xem xét và chấp nhận rủi ro xung đột CCCD (sau khi đã chạy `CHECK_CCCD_CONFLICT_V1_V2.sql`).

### `REMAP_CSDT_CODE_IN_CSDT_V2.sql`
- **Loại:** Đổi mã CSĐT trong `CSDT_V2`.
- **Mục đích:** Đổi mã CSĐT sau khi chuyển dữ liệu từ V1 (ví dụ `66016` → `66026`): cập nhật
  `MaDK`, `MaKH`/`MaKhoaHoc`, `MaCSDT`.
- **Mặc định:** `@DryRun = 1`; `@DisableConstraints = 1`; `@BlockOnCollision = 1`; `@UpdateFilePathText = 0`
  (không đổi đường dẫn ảnh/PDF để tránh mất link file thật).
- **Cảnh báo:** **Chỉ chạy trên database backup test trước.**

### `VALIDATE_V1_TO_V2_CHUYEN_ALL_KHOA.sql`
- **Loại:** Kiểm tra sau chuyển (read-only).
- **Tác động:** Chỉ `SELECT`.
- **Mục đích:** Đối chiếu số lượng khóa/hồ sơ giữa V1 và V2 sau khi chuyển; liệt kê các khóa V1 còn thiếu trong V2.
- **Khi dùng:** Ngay sau khi chuyển ALL khóa V1 → V2.

### `V2_POST_TRANSFER_HEALTH_CHECK.sql`
- **Loại:** Kiểm tra sức khỏe dữ liệu sau chuyển (read-only).
- **Tác động:** Chỉ kiểm tra, dùng bảng tạm `#CheckResult`.
- **Tham số:** `@SourceMaCSDT`, `@TargetMaCSDT` (để `@TargetMaCSDT = @SourceMaCSDT` nếu chưa đổi mã,
  hoặc đặt mã mới ví dụ `66016` → `66026`).
- **Mục đích:** Phát hiện lỗi sau khi chuyển dữ liệu từ `CSDT_V1` sang `CSDT_V2`.
- **Khi dùng:** Sau bước validate, để soát lỗi chi tiết.

---

## Tài liệu liên quan
- Hướng dẫn cài đặt database: [`database-setup.md`](./database-setup.md)
- Phân nhóm script trong thư mục database: [`../database/README.md`](../database/README.md)
