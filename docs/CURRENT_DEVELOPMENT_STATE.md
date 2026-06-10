# TRẠNG THÁI HIỆN TẠI CỦA QLHV_APP

## Repo

https://github.com/iamthais2hag/QLHV_APP.git

## Kiểm tra đầu tiên

Chạy:

cd D:\QLHV_APP
git status
git log --oneline -12

Yêu cầu:

working tree clean

## Backend

Solution:

server/QLHV.sln

Các project:

server/QLHV.Api
server/QLHV.Application
server/QLHV.Infrastructure
server/QLHV.Domain
server/QLHV.Shared
server/QLHV.Worker

## Frontend

client/

Có giao diện layout và module Học viên.

## Database reference

database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql
database/QLHV_APP_DATABASE_PATCH_INDEX_JSON.sql
database/reference/V1_schema_full.sql
database/reference/V2_schema_full.sql

## Mapping Học viên từ V2

Nguồn:

NguoiLX nlx
NguoiLX_HoSo hs
KhoaHoc kh

Join:

NguoiLX.MaDK = NguoiLX_HoSo.MaDK
NguoiLX_HoSo.MaKhoaHoc = KhoaHoc.MaKH

Mapping chính:

MaDK              ← nlx.MaDK / hs.MaDK
HoTen             ← nlx.HoVaTen
NgaySinh          ← nlx.NgaySinh
GioiTinh          ← nlx.GioiTinh
SoCCCD            ← nlx.SoCMT
DiaChiThuongTru   ← nlx.NoiTT
SoGPLXDaCo        ← hs.SoGPLXDaCo
HangGPLXDaCo      ← hs.HangGPLXDaCo
NguoiNhanHoSo     ← hs.NguoiNhanHSo
TenKhoa           ← kh.TenKH
MaKhoa            ← hs.MaKhoaHoc / kh.MaKH

## Quy tắc dữ liệu

- Giữ nguyên dữ liệu nguồn, chỉ trim.
- SoCMT map sang SoCCCD.
- Không tự đổi CMND 9 số thành CCCD 12 số.
- CCCD không đủ 12 số thì cảnh báo sau, không chặn.
- GioiTinh giữ raw value, chưa đoán Nam/Nữ.
- TrangThai chưa lọc mặc định.
- Không xóa vật lý.
- SourceOfTruth = V2.

## Đồng bộ Học viên

Đã có:
- Dry-run
- Read-only Dapper query
- Guarded execute endpoint
- Upsert design bằng SqlBulkCopy + MERGE
- V2RowHash
- App_DongBoLog writer
- EnableTargetWrites mặc định false

## Chưa làm

- Chưa test tự động đầy đủ.
- Chưa chạy thử SQL local.
- Chưa bật ghi thật.
- Chưa schedule Hangfire.
- Chưa có UI chạy đồng bộ.

## Việc tiếp theo

Task 5 Phase B3C:

- Test execute guard.
- Test hash.
- Test data rules.
- Tạo docs/sync-local-test-guide.md.
- Build backend.
- Chạy dotnet test nếu test không cần SQL.

## Lệnh build

Backend:

cd D:\QLHV_APP\server
dotnet build QLHV.sln

Frontend:

cd D:\QLHV_APP\client
npm install
npm run build
