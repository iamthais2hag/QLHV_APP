# Requirements - QLHV_APP

## Mục tiêu

Xây dựng web app nội bộ để quản lý:
- Học viên
- Khóa học
- Giáo viên
- Xe tập lái
- Kết quả tốt nghiệp
- Kỳ sát hạch
- Thi lại
- Thẻ học viên / phù hiệu giáo viên
- Nhập Excel AI
- Đọc PDF/OCR
- Đồng bộ V2 sang QLHV_APP
- Chuyển dữ liệu V1/V2 theo 4 chế độ

## Quy tắc dữ liệu

- V2 là nguồn gốc chính xác.
- QLHV_APP lưu đầy đủ dữ liệu quản lý.
- TTTC_WebSite chỉ đọc QLHV_APP.
- Không ghi nhầm vào V1/V2.
- Các thao tác chuyển dữ liệu phải có DryRun, Preview, Confirm, Transaction, Rollback.
