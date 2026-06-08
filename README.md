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
