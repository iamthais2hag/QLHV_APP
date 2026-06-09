# QLHV_APP - Local Starter

Dự án phần mềm nội bộ quản lý học viên, khóa học, giáo viên, xe tập lái, kết quả, thẻ/phù hiệu, Excel/PDF/OCR và đồng bộ dữ liệu V1/V2.

## Không dùng GitHub

Dự án này được tổ chức để làm việc hoàn toàn trên máy nội bộ hoặc server nội bộ.

Có thể dùng:
- Thư mục local: `D:\QLHV_APP`
- Ổ LAN/NAS nội bộ
- USB/ổ cứng backup
- OneDrive/Google Drive nội bộ nếu trung tâm muốn
- Git local nếu muốn quản lý lịch sử code, không cần GitHub

## Kiến trúc chốt

```text
SQL V1 / SQL V2 gốc
        ↓ đồng bộ một chiều / chuyển dữ liệu có kiểm soát
QLHV_APP - SQL mới của phần mềm quản lý
        ↓ API an toàn
TTTC_WebSite
```

## Công nghệ

- Frontend: React + TypeScript
- Backend: ASP.NET Core
- Database: SQL Server
- ORM: EF Core Database-First
- Hiệu năng cao: Dapper + SqlBulkCopy
- Background job: Hangfire
- Retry: Polly
- Cache: IMemoryCache
- Font UI: Be Vietnam Pro

## Database

QLHV_APP dùng SQL Server. Các database khuyến nghị: `QLHV_APP` (trung tâm),
`CSDT_V1` và `CSDT_V2` (nguồn, chỉ đọc / đồng bộ một chiều, dùng bản backup test).

- Hướng dẫn cài đặt & khôi phục database: [`docs/database-setup.md`](docs/database-setup.md)
- Mô tả từng script SQL: [`docs/sql-scripts-index.md`](docs/sql-scripts-index.md)
- Phân nhóm script trong thư mục database: [`database/README.md`](database/README.md)

> An toàn: không lưu mật khẩu/connection string trong repo; luôn DryRun trước khi commit;
> chỉ chạy script chuyển dữ liệu trên database backup test, không chạy trên production.
