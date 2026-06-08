# Quy trình làm việc không dùng GitHub

## 1. Thư mục làm việc

Đặt dự án tại:

```text
D:\QLHV_APP
```

hoặc:

```text
D:\ThanhCong\QLHV_APP
```

## 2. Mở bằng Kiro

Mở trực tiếp thư mục local trong Kiro:

```text
File → Open Folder → D:\QLHV_APP
```

Sau đó yêu cầu Kiro đọc:

```text
.kiro/specs/qlhv-app/
```

và chỉ làm từng task nhỏ.

## 3. Backup

Sau mỗi mốc quan trọng, nén toàn bộ thư mục thành:

```text
QLHV_APP_backup_yyyyMMdd_HHmm.zip
```

Lưu ít nhất 3 nơi:

```text
Ổ D trên máy làm việc
Ổ cứng rời/USB
Ổ LAN/NAS nội bộ
```

## 4. Có nên dùng Git local không?

Có thể dùng Git local để quay lại phiên bản cũ mà không cần GitHub:

```bash
git init
git add .
git commit -m "Khoi tao du an QLHV_APP"
```

Không cần chạy `git remote add`, không cần push.

## 5. Quy tắc an toàn

- Không lưu mật khẩu SQL trong repo.
- Không lưu file `.bak` thật trong repo code.
- Không lưu CCCD/file dữ liệu thật trong thư mục code nếu không cần.
- SQL V1/V2 gốc chỉ đọc, không để frontend kết nối trực tiếp.
