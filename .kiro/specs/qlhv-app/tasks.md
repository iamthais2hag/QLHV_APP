# Tasks - QLHV_APP

## Task 1 - Scaffold solution

Tạo cấu trúc:
- client
- server
- worker
- database
- docs

Backend:
- QLHV.Api
- QLHV.Application
- QLHV.Infrastructure
- QLHV.Domain
- QLHV.Worker
- QLHV.Shared

Chưa code nghiệp vụ chi tiết.

## Task 2 - Database

Đưa script QLHV_APP vào thư mục database.
Tạo hướng dẫn chạy SQL trên SQL Server.

## Task 3 - Frontend layout

Tạo layout React:
- Sidebar
- Header
- Menu chính
- Theme xanh trắng
- Font Be Vietnam Pro

## Task 4 - Module Học viên

Làm danh sách học viên, tìm kiếm, bộ lọc, copy MaDK, xuất Excel.

## Task 5 - Đồng bộ V2 sang QLHV_APP

Dùng Dapper + SqlBulkCopy + Hangfire + Polly.

## Task 6 - Chuyển dữ liệu V1/V2

Hỗ trợ 4 chế độ:
1. Chuyển từng khóa, không chuyển mã CSĐT
2. Chuyển từng khóa, có chuyển mã CSĐT
3. Chuyển All, không chuyển mã CSĐT
4. Chuyển All, có chuyển mã CSĐT
