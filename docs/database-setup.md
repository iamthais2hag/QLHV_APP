# Hướng dẫn cài đặt Database - QLHV_APP

Tài liệu này mô tả cách dựng các database cần thiết cho QLHV_APP trên môi trường nội bộ.

> **Tài liệu, không thực thi.** File này chỉ hướng dẫn. Không chứa connection string,
> tài khoản, mật khẩu hay secret thật. Không tự động chạy script nào.

## 1. Phiên bản SQL Server yêu cầu

- **SQL Server 2019** trở lên (khuyến nghị 2019/2022).
- Lý do: schema dùng `DATETIME2`, filtered index (`WHERE IsDeleted = 0`), `RowVersion`,
  và các kiểm tra JSON (`ISJSON`/`JSON_VALUE`) vốn ổn định từ SQL Server 2016+ và được
  khuyến nghị từ 2019 để có hiệu năng tốt nhất.
- Công cụ: **SQL Server Management Studio (SSMS)** hoặc **Azure Data Studio**.
- Collation khuyến nghị: `Vietnamese_CI_AS` hoặc một Unicode collation phù hợp với dữ liệu tiếng Việt.

## 2. Tên database khuyến nghị

| Database | Vai trò |
| --- | --- |
| `QLHV_APP` | Database trung tâm của phần mềm quản lý mới. |
| `CSDT_V1` | Database nguồn V1 (chỉ đọc / dùng để chuyển dữ liệu, dùng bản backup test). |
| `CSDT_V2` | Database nguồn V2 - nguồn gốc chính xác (chỉ đọc / đồng bộ một chiều, dùng bản backup test). |

> Quy tắc dữ liệu: **V2 là nguồn gốc chính xác**, đồng bộ một chiều sang `QLHV_APP`.
> Không ghi trực tiếp vào V1/V2 từ website/frontend.

## 3. Tạo database QLHV_APP

Dùng script schema chính:

```text
database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql
```

Các bước:

1. Mở SSMS / Azure Data Studio, kết nối tới SQL Server nội bộ bằng tài khoản **không phải `sa`**
   (theo Security Rules). Tài khoản cần quyền tạo database.
2. Mở file `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`.
3. Chạy toàn bộ script. Script sẽ:
   - Tự tạo database `QLHV_APP` nếu chưa tồn tại (`IF DB_ID(N'QLHV_APP') IS NULL ... CREATE DATABASE`).
   - `USE QLHV_APP` rồi tạo các bảng theo cơ chế `IF OBJECT_ID(...) IS NULL` (idempotent - chạy lại an toàn).
4. Kiểm tra: trong Object Explorer, database `QLHV_APP` xuất hiện cùng các bảng `App_*`.

> Script được viết theo dạng idempotent (kiểm tra tồn tại trước khi tạo), nên chạy lại
> không làm hỏng dữ liệu đã có. Tuy vậy vẫn nên chạy trên môi trường test trước.

## 4. Áp dụng patch index/JSON (khi cần)

File:

```text
database/QLHV_APP_DATABASE_PATCH_INDEX_JSON.sql
```

Khi nào cần:
- Sau khi đã tạo schema `QLHV_APP`.
- Khi cần bổ sung index tra cứu / kiểm tra JSON / tối ưu lookup performance mà bản schema
  gốc chưa có, hoặc khi nâng cấp lên chuẩn hiệu năng mới.

Cách áp dụng:

1. Đảm bảo `QLHV_APP` đã được tạo (mục 3).
2. Mở và chạy `database/QLHV_APP_DATABASE_PATCH_INDEX_JSON.sql`.
3. Script kiểm tra `IF NOT EXISTS (... sys.indexes ...)` trước khi tạo từng index nên an toàn khi chạy lại.

## 5. Khôi phục database test CSDT_V1 và CSDT_V2

Dùng cho việc test chuyển/đồng bộ dữ liệu. **Luôn khôi phục từ bản backup**, không thao tác trên DB gốc production.

> File `.bak` thật **không** được lưu trong repo (xem `.gitignore`). Lấy file backup từ
> nguồn nội bộ an toàn (ổ LAN/NAS, USB...).

Các bước (RESTORE):

1. Copy file backup test vào máy SQL Server, ví dụ `CSDT_V1_test.bak`, `CSDT_V2_test.bak`.
2. Trong SSMS: chuột phải **Databases → Restore Database... → Device** → chọn file `.bak`.
3. Đặt tên database đích đúng chuẩn: `CSDT_V1` và `CSDT_V2`.
4. Kiểm tra phần **Files** để trỏ đường dẫn `.mdf`/`.ldf` về thư mục data hợp lệ trên máy test.
5. Restore và xác nhận hai database `CSDT_V1`, `CSDT_V2` xuất hiện.

Ví dụ tham khảo bằng T-SQL (thay đường dẫn cho phù hợp, chạy thủ công trong SSMS):

```sql
-- Ví dụ minh hoạ, KHÔNG chứa thông tin thật. Chỉnh đường dẫn theo máy test của bạn.
RESTORE DATABASE CSDT_V1
FROM DISK = N'<DUONG_DAN>\CSDT_V1_test.bak'
WITH MOVE N'CSDT_V1'     TO N'<DUONG_DAN_DATA>\CSDT_V1.mdf',
     MOVE N'CSDT_V1_log' TO N'<DUONG_DAN_DATA>\CSDT_V1_log.ldf',
     RECOVERY, REPLACE;

RESTORE DATABASE CSDT_V2
FROM DISK = N'<DUONG_DAN>\CSDT_V2_test.bak'
WITH MOVE N'CSDT_V2'     TO N'<DUONG_DAN_DATA>\CSDT_V2.mdf',
     MOVE N'CSDT_V2_log' TO N'<DUONG_DAN_DATA>\CSDT_V2_log.ldf',
     RECOVERY, REPLACE;
```

## 6. Thứ tự chạy script khi test chuyển dữ liệu

1. Restore `CSDT_V1`, `CSDT_V2` từ backup (mục 5).
2. `CHECK_CCCD_CONFLICT_V1_V2.sql` - kiểm tra trùng CCCD/MaDK trước.
3. `TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql` với `@DryRun = 1` để chạy thử.
4. Xem kết quả DryRun, nếu ổn mới cân nhắc `@DryRun = 0` (vẫn trên DB backup).
5. (Tùy chọn) `REMAP_CSDT_CODE_IN_CSDT_V2.sql` để đổi mã CSĐT, cũng `@DryRun = 1` trước.
6. `VALIDATE_V1_TO_V2_CHUYEN_ALL_KHOA.sql` và `V2_POST_TRANSFER_HEALTH_CHECK.sql` để kiểm tra sau chuyển.

Xem mô tả từng file tại [`sql-scripts-index.md`](./sql-scripts-index.md).

## 7. Quy tắc an toàn (bắt buộc)

- **KHÔNG chạy script chuyển dữ liệu trên database production trước.** Luôn chạy trên backup test trước.
- **Luôn test trên database backup** (`CSDT_V1`, `CSDT_V2` phục hồi từ `.bak`), không thao tác DB gốc.
- **DryRun phải được dùng trước Commit.** Mọi script chuyển/đổi mã mặc định `@DryRun = 1`
  (chạy thử + ROLLBACK). Chỉ đặt `@DryRun = 0` khi đã kiểm tra kỹ và vẫn trên DB test.
- **Không bao giờ lưu mật khẩu SQL trong repository.** Cung cấp connection string thật qua
  user-secrets hoặc biến môi trường (xem `server/README.md`).
- Không dùng tài khoản `sa` cho ứng dụng. Frontend không chứa connection string; chỉ backend kết nối SQL.
- Không log password/token/connection string/API key.
- Không lưu file `.bak`, `.mdf`, `.ldf` hay dữ liệu thật (CCCD...) trong repo code.

## 8. Liên quan
- Tổng quan kiến trúc: [`../README.md`](../README.md)
- Backend & cấu hình secret: [`../server/README.md`](../server/README.md)
- Quy trình không dùng GitHub: [`./NO_GITHUB_WORKFLOW.md`](./NO_GITHUB_WORKFLOW.md)

## Cau hinh ket noi cho dong bo V2

Phase A chi thiet ke nen tang, khong chay dong bo va khong ghi SQL Server.

- Ket noi bootstrap `QLHV_APP` phai duoc cap tu bien moi truong, user-secrets, hoac cau hinh server duoc bao ve.
- `CSDT_V1` va `CSDT_V2` ve sau se duoc cau hinh trong man hinh Admin **Cau hinh ket noi du lieu**.
- Chi vai tro `Admin` va `Giam doc trung tam` duoc phep tao/sua/test/bat/tat cau hinh ket noi nguon.
- Mat khau/connection string nguon phai duoc ma hoa khi luu tru.
- Frontend chi hien thi mat khau dang mask, vi du `********`.
- Test Connection phai tra ve ket qua da lam sach, khong lo server/user/password/full connection string.
- Moi thao tac create/update/test/enable/disable phai ghi audit log noi bo voi du lieu da lam sach.

Xem thiet ke chi tiet tai [`sync-v2-design.md`](./sync-v2-design.md).
