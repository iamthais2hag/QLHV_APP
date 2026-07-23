# QLHV Thành Công — chạy một nút trong mạng LAN

Bản Production phục vụ frontend và API từ **một process ASP.NET Core**. Khi vận hành không cần mở Vite, npm hay API riêng.

- Máy chủ: [http://localhost:8088](http://localhost:8088)
- Máy trạm: [http://192.168.100.101:8088](http://192.168.100.101:8088)
- Runtime: `D:\QLHV_APP_RUNTIME\app`
- Cấu hình riêng của máy: `D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json`
- Log: `D:\QLHV_APP_RUNTIME\logs`

## Cài đặt lần đầu

Máy chủ build cần source tại `D:\QLHV_APP`, .NET SDK 8 và Node.js/npm. Đăng nhập Windows bằng đúng tài khoản sẽ vận hành ứng dụng, mở **PowerShell as Administrator**, rồi chạy:

```powershell
Set-Location D:\QLHV_APP
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\qlhv-lan\Install-QLHV-App.ps1
```

Installer:

1. tạo/preserve cấu hình Production riêng của máy và chuẩn hóa hai cờ vận hành ghi;
2. build frontend cùng-origin `/api` và publish API Release;
3. cài vào `D:\QLHV_APP_RUNTIME\app`;
4. khởi động ẩn, chờ `/health/live`, rồi chờ `/health/ready`;
5. chỉ khi ready mới chạy các smoke test GET;
6. dừng process smoke đang mang token Administrator, rồi tạo firewall Private TCP 8088 và shortcut Desktop **QLHV Thành Công**.

Installer không chạy SQL patch, backup/restore, refresh BAK hoặc full sync. Nếu runtime mới không ready, installer dừng đúng PID vừa tạo và khôi phục runtime cũ. `appsettings.Development.json`, `.git`, source và `IM_GPLX` không được đưa vào publish package.

### Cấu hình Production riêng của máy

Không cần và không được copy thủ công toàn bộ `appsettings.Development.json` vào runtime. Ở lần cài đầu, nếu file Production local chưa tồn tại, installer đọc file Development local trên chính máy và chỉ trích xuất các section được phép:

- `ConnectionStrings`;
- cấu hình bảo vệ/mã hóa connection profile nếu có;
- `DataProtection` nếu có;
- `FileStorage`;
- `Sync` và `SyncExecution`;
- `Authentication` nếu có.

`Logging`, `Cors`, debug và các setting chỉ dành cho Development không được sao chép. `FileStorage.Root` tương đối được chuẩn hóa theo API source, để `IM_GPLX` không bị trỏ nhầm sau khi publish. JSON được kiểm tra trước khi dùng và nội dung/connection string không được in ra console hay log.

File cấu hình nằm ngoài Git. Thư mục config được khóa ACL **trước khi** file tạm có secret được ghi. ACL của thư mục/file chỉ cho phép:

- `SYSTEM` và nhóm local `Administrators`: toàn quyền;
- tài khoản Windows chạy ứng dụng: chỉ đọc/traverse.

Có thể chỉ rõ tài khoản vận hành khi cài. Installer đồng thời cấp read/execute cho runtime app và quyền ghi cần thiết cho `logs/run`, nhưng config vẫn chỉ đọc:

```powershell
.\scripts\windows\qlhv-lan\Install-QLHV-App.ps1 -RuntimeAccount 'MAYCHU\TaiKhoanVanHanh'
```

Nếu file Production local đã tồn tại và JSON hợp lệ, installer/updater chỉ chuẩn hóa `Sync:DryRun=false` và `SyncExecution:EnableTargetWrites=true`; mọi section/giá trị local khác được giữ nguyên. Việc thay nội dung được thực hiện atomically, giữ ACL của file, không in JSON hay secret. Updater lấy SHA-256 sau bước chuẩn hóa rồi bảo vệ file khỏi mọi thay đổi ngoài ý muốn trong phần còn lại của quá trình. Không đặt password/secret trong Git, command line hoặc tài liệu.

## Sử dụng hằng ngày

Bấm shortcut Desktop **QLHV Thành Công**. Launcher:

1. kiểm tra runtime, JSON Production local và `ConnectionStrings:QLHV_APP` mà không in giá trị;
2. lấy mutex cùng file lock cross-session, rồi kiểm tra process/PID và port 8088;
3. nếu QLHV đang ready, chỉ mở trình duyệt;
4. nếu đúng QLHV bị treo hoặc là runtime cũ, dừng đúng PID đã xác minh rồi khởi động lại — không dùng `taskkill` và không dừng toàn bộ `dotnet`;
5. chờ `GET /health/live`, sau đó `GET /health/ready`;
6. chỉ mở trình duyệt khi cả hai đạt.

Nếu port 8088 thuộc process khác, launcher báo rõ PID và không khởi động QLHV. Nếu cấu hình/readiness lỗi, process vừa tạo bị dừng, trình duyệt không mở, và thông báo chỉ ra file cần kiểm tra:

```text
D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json
```

Thông tin chi tiết an toàn nằm trong `D:\QLHV_APP_RUNTIME\logs`. Backend ghi log xoay theo ngày/kích thước và giới hạn số file. Console application logging bị tắt trong Production để các file bootstrap stdout/stderr không tăng vô hạn hoặc lặp dữ liệu nhạy cảm; launcher dọn các file bootstrap cũ theo tuổi/số lượng. Password, cookie, operations secret, connection string và `PasswordHash` không được log.

Nếu install/update thất bại, nguyên nhân gốc và giai đoạn được ghi **trước rollback** vào `installer-YYYYMMDD.error.log` hoặc `updater-YYYYMMDD.error.log`. Log chỉ giữ exception message một dòng đã redaction, tối đa 1 MB mỗi segment, 14 file/30 ngày; không ghi stack, JSON config hay giá trị bí mật.

Runtime đặt:

- `ASPNETCORE_ENVIRONMENT=Production`;
- `ASPNETCORE_URLS=http://0.0.0.0:8088`;
- `HttpsRedirection__Enabled=false` cho LAN HTTP;
- `Authentication__Cookie__SecurePolicy=SameAsRequest` cho cookie HttpOnly cùng origin;
- `QlhvRuntime__ProductionLocalConfigPath` trỏ đến file config ngoài Git.

Runtime cũ trước hardening chỉ có `GET /health`. Sau rollback, script ghi marker không chứa secret tại `D:\QLHV_APP_RUNTIME\run\legacy-runtime.marker`. Launcher hằng ngày tự nhận marker, truyền cấu hình allow-list qua environment của child process (không qua command line/log) và dùng legacy health. Deploy hardened thành công xóa marker; từ đó khởi động hằng ngày lại bắt buộc live + ready. Tham số `-AllowLegacyRollback` chỉ dành cho installer/updater nội bộ.

## Readiness và xử lý sự cố

- `GET /health/live`: process web đang sống.
- `GET /health/ready`: cấu hình, SQL/database, schema/auth, BAK profiles và file storage đã sẵn sàng.
- `GET /api/system/runtime-status`: diagnostics an toàn, không trả connection string/password/hash/secret.

Readiness chỉ đọc. Nó không tạo bảng, sửa database, chạy SQL patch hay sync. Nếu báo thiếu schema, áp dụng patch đã được phê duyệt theo quy trình riêng; installer/updater sẽ không tự chạy.

Khi launcher báo **“Thiếu hoặc sai cấu hình QLHV_APP”**, kiểm tra file Production local và quyền đọc của tài khoản vận hành. Không gửi nội dung file hoặc connection string vào log/ticket công khai.

## Dừng, cập nhật và gỡ

Dừng đúng QLHV PID:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\QLHV_APP\scripts\windows\qlhv-lan\Stop-QLHV-App.ps1
```

Cập nhật bằng PowerShell Administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\QLHV_APP\scripts\windows\qlhv-lan\Update-QLHV-App.ps1
```

Updater chuẩn hóa đúng hai cờ vận hành nói trên rồi lấy hash cấu hình, build/publish vào staging, dừng đúng PID, rename runtime cũ vào rollback, cài runtime mới và chờ ready. Các giá trị local còn lại không bị thay đổi. Nếu thất bại, nó phục hồi runtime cũ và kiểm tra legacy/current health. Vì updater chạy Administrator, process dùng để smoke/rollback validation luôn được dừng sau kiểm tra; hãy bấm shortcut để chạy lại bằng tài khoản vận hành bình thường. Updater không tự chạy SQL patch, refresh BAK hoặc full sync.

Nếu lỗi xảy ra đúng khoảng chuyển tiếp sau khi runtime cũ đã dừng nhưng trước khi move vào backup hoàn tất, script không bỏ mặc app ở trạng thái dừng không rõ nguyên nhân: nó kiểm tra lại binary cũ bằng legacy-compatible health, dừng process kiểm tra mang token Administrator, giữ marker cần thiết và hướng dẫn khởi động bằng shortcut.

Gỡ cài đặt (có xác nhận `UNINSTALL`) sẽ xóa runtime, config local, log, shortcut và đúng firewall rule QLHV; source/database không bị thay đổi:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\QLHV_APP\scripts\windows\qlhv-lan\Uninstall-QLHV-App.ps1
```

Các thao tác refresh BAK và full sync chỉ xảy ra khi Admin đăng nhập và chủ động thao tác trong ứng dụng; không script nào ở đây gọi các endpoint ghi đó.
