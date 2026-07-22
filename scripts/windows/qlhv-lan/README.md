# QLHV Thành Công — chạy một nút trong mạng LAN

Bộ script này build và cài bản Production gồm frontend và API chạy chung trong **một process ASP.NET Core**. Khi vận hành không khởi động Vite, npm hoặc một frontend riêng.

## Cài đặt lần đầu trên máy chủ

Máy chủ cần có source tại `D:\QLHV_APP`, .NET 8 SDK và Node.js/npm để build. Mở **PowerShell as Administrator**, sau đó chạy:

```powershell
Set-Location D:\QLHV_APP
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\qlhv-lan\Install-QLHV-App.ps1
```

Installer thực hiện các việc sau:

- build frontend Production với API tương đối `/api`;
- publish `QLHV.Api` Release và đặt frontend vào `wwwroot`;
- cài bản chạy tại `D:\QLHV_APP_RUNTIME\app`;
- tạo `D:\QLHV_APP_RUNTIME\logs` và `D:\QLHV_APP_RUNTIME\run`;
- tạo đúng một firewall inbound TCP 8088, chỉ áp dụng cho network profile **Private**;
- tạo shortcut dùng chung trên Desktop tên **QLHV Thành Công**.

`appsettings.Development.json`, `.git`, source code và thư mục ảnh `IM_GPLX` không được đưa vào package. Script không chạy SQL patch, backup/restore BAK, refresh hoặc full sync.

### Cấu hình riêng của máy chủ

Không đặt connection string, mật khẩu hoặc secret trong Git hay `appsettings.Development.json`. Cấp cấu hình Production bằng environment variable của máy, ví dụ tên biến ASP.NET Core dùng dấu `__` cho cấp cấu hình (`ConnectionStrings__...`). Không ghi giá trị secret vào tài liệu hoặc script. Sau khi đổi biến môi trường cấp máy, mở phiên đăng nhập/PowerShell mới trước khi chạy app.

Nếu connection string dùng Windows Authentication, hãy luôn mở shortcut bằng cùng tài khoản Windows đã được SQL Server cấp quyền. Không chạy runtime bằng một tài khoản dịch vụ khác nếu chưa cấp quyền database và cấu hình nơi lưu Data Protection keys phù hợp cho tài khoản đó.

Runtime luôn đặt:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://0.0.0.0:8088`
- `HttpsRedirection__Enabled=false` cho HTTP nội bộ LAN.
- `Authentication__Cookie__SecurePolicy=SameAsRequest` để cookie đăng nhập HttpOnly hoạt động trên HTTP LAN cùng origin.

Ảnh học viên được giữ ngoài package. Nếu máy chưa đặt `FileStorage__Root`, launcher mặc định dùng `D:\QLHV_APP` (thư mục ảnh là `D:\QLHV_APP\IM_GPLX`). Có thể đặt `FileStorage__Root` bằng environment variable cấp máy để dùng vị trí ngoài Git khác; launcher tôn trọng giá trị đã cấu hình.

## Sử dụng hằng ngày

### Máy chủ

Bấm shortcut Desktop **QLHV Thành Công**. Launcher:

1. kiểm tra port 8088 và PID hiện có;
2. chỉ khởi động `D:\QLHV_APP_RUNTIME\app\QLHV.Api` nếu chưa chạy;
3. đợi `GET /health` thành công;
4. mở [http://localhost:8088](http://localhost:8088).

Nếu khởi động lỗi, launcher hiển thị thông báo và ghi chi tiết trong `D:\QLHV_APP_RUNTIME\logs`.

### Máy trạm

Mở trình duyệt và truy cập:

```text
http://192.168.100.101:8088
```

Đăng nhập bằng tài khoản QLHV được cấp. Máy trạm không cần clone Git, cài Node.js/.NET SDK, chạy PowerShell hoặc sửa cấu hình.

Máy chủ phải có địa chỉ LAN `192.168.100.101` và Windows network profile phải là **Private**. Nếu IP máy chủ thay đổi, dùng địa chỉ mới tương ứng; firewall vẫn giới hạn ở profile Private.

## Dừng, cập nhật và gỡ cài đặt

Dừng đúng QLHV runtime (không dừng các process dotnet/node khác):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\QLHV_APP\scripts\windows\qlhv-lan\Stop-QLHV-App.ps1
```

Cập nhật từ source hiện tại, chạy bằng PowerShell as Administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\QLHV_APP\scripts\windows\qlhv-lan\Update-QLHV-App.ps1
```

Update build vào thư mục tạm trước khi dừng bản cũ. Bản cũ được giữ ở `D:\QLHV_APP_RUNTIME\run\rollback-app`; nếu bản mới không qua `/health`, script tự khôi phục và khởi động lại bản cũ. Update không tự chạy SQL patch, refresh BAK hoặc full sync.

Gỡ runtime, logs, shortcut và đúng firewall rule của QLHV (không xóa source hoặc database), chạy bằng PowerShell as Administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\QLHV_APP\scripts\windows\qlhv-lan\Uninstall-QLHV-App.ps1
```

## Vị trí vận hành

| Nội dung | Vị trí |
|---|---|
| Bản Production | `D:\QLHV_APP_RUNTIME\app` |
| Log stdout/stderr và lỗi launcher | `D:\QLHV_APP_RUNTIME\logs` |
| PID, staging và bản rollback | `D:\QLHV_APP_RUNTIME\run` |
| URL máy chủ | `http://localhost:8088` |
| URL máy trạm | `http://192.168.100.101:8088` |

Các thao tác refresh BAK và full sync chỉ xảy ra khi Admin đăng nhập rồi chủ động bấm trong ứng dụng; không script nào trong thư mục này tự gọi các thao tác đó.
