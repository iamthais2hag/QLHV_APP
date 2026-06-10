# BÀN GIAO PROJECT QLHV_APP CHO CODEX MỚI

## Tên project

QLHV_APP

## Repo GitHub

https://github.com/iamthais2hag/QLHV_APP.git

## Mục tiêu project

QLHV_APP là web app nội bộ quản lý trung tâm đào tạo lái xe Thành Công.

Các module chính:
- Học viên
- Khóa học
- Giáo viên
- Xe tập lái
- Kết quả tốt nghiệp
- Kỳ sát hạch
- Đăng ký thi lại
- Chuyển khóa
- Xuất Word/Excel
- JP2/XML/Thẻ/Phù hiệu
- Đồng bộ V2
- Tài liệu scan/PDF
- Nhật ký hệ thống
- Cấu hình

## Kiến trúc

Frontend:
- React
- TypeScript
- Vite
- Be Vietnam Pro
- Giao diện xanh/trắng Thành Công

Backend:
- ASP.NET Core .NET 8
- SQL Server
- Dapper
- EF Core Database-First sau này
- SqlBulkCopy
- Hangfire
- Polly
- IMemoryCache

Các project backend:

server/QLHV.Api
server/QLHV.Application
server/QLHV.Infrastructure
server/QLHV.Domain
server/QLHV.Shared
server/QLHV.Worker

## Luồng dữ liệu

SQL V1 / SQL V2 gốc
→ đồng bộ/chuyển dữ liệu có kiểm soát
→ QLHV_APP SQL
→ API
→ Frontend

Frontend tuyệt đối không kết nối trực tiếp SQL Server.

## Những việc đã làm xong

- Task 1: dựng khung backend/worker
- Task 2: tài liệu database
- Task 3: dựng giao diện frontend
- Task 4: module Học viên đọc dữ liệu
- Task 5 Phase A: nền đồng bộ V2
- Task 5 Phase B1: mapping Học viên từ V2
- Task 5 Phase B2: Dapper đọc V2 read-only
- Task 5 Phase B2.5: quy tắc dữ liệu Học viên
- Task 5 Phase B3A: nền upsert vào App_HocVien
- Task 5 Phase B3B: đường ghi có khóa bảo vệ

## Quy tắc an toàn bắt buộc

- Không dùng database production.
- Không chạy SQL script nếu chưa được yêu cầu.
- Không ghi SQL Server nếu chưa có kế hoạch test rõ ràng.
- Không đưa username/password/connection string thật vào repo.
- Frontend không chứa thông tin SQL.
- Không log mật khẩu, token, connection string.
- Không bật Hangfire recurring schedule.
- EnableTargetWrites mặc định phải là false.
- Dry-run không được ghi dữ liệu.
- Execute phải yêu cầu xác nhận chính xác.
- Không xóa vật lý dữ liệu khi đồng bộ.

## Endpoint hiện có

Dry-run:

POST /api/dong-bo-v2/hoc-vien/dry-run

Execute có bảo vệ:

POST /api/dong-bo-v2/hoc-vien/execute

Execute chỉ được chạy khi có:

{
  "confirmTargetWrites": true,
  "confirmationText": "EXECUTE_DONG_BO_V2_HOC_VIEN"
}

và cấu hình EnableTargetWrites=true trong môi trường test.

## Tài liệu cần đọc trước

1. docs/CURRENT_DEVELOPMENT_STATE.md
2. docs/sync-v2-design.md
3. docs/hoc-vien-v2-mapping.md
4. docs/hoc-vien-data-rules.md
5. docs/database-setup.md
6. docs/sql-scripts-index.md
7. database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql
8. database/reference/V2_schema_full.sql

## Việc tiếp theo

Task 5 Phase B3C:

Thêm test an toàn và tài liệu chạy thử local cho đường đồng bộ Học viên.

Chưa chạy đồng bộ thật.
Chưa kết nối production.
Chưa chạy SQL script thật.
Chưa thêm secret.

## Prompt cho Codex mới

Đọc file này trước, sau đó đọc docs/CURRENT_DEVELOPMENT_STATE.md.

Tiếp tục Task 5 Phase B3C only.

Không làm lại từ đầu.
Không ghi đè code đang có.
Không kết nối database production.
Không chạy SQL script.
Không thêm secret.
Không push GitHub nếu chưa hỏi.

Mục tiêu:
- Thêm test cho execution guard.
- Thêm test cho V2RowHash.
- Thêm test cho quy tắc dữ liệu Học viên.
- Tạo docs/sync-local-test-guide.md.
- Build backend.
- Nếu test không cần SQL thì chạy dotnet test.
