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

1. chạy mặc định ở mode `ProductionService` (connect-only, không start API);
2. lấy mutex cùng file lock cross-session dành riêng cho desktop launcher;
3. nếu port 8088 có listener, gọi `GET /api/system/runtime-status` và chỉ chấp nhận HTTP 200, `QLHV.Api`, version tương thích, contract 2.0 và frontend identity hợp lệ;
4. nếu port trống, chờ API service trong timeout hữu hạn rồi báo `SERVER_UNAVAILABLE`, không tự tạo process;
5. mở ngay `/qlhv-import` sau khi danh tính API được xác minh.

Mode `DevelopmentLocalHost` chỉ được bật rõ ràng bằng tham số PowerShell trong môi
trường phát triển. Chỉ mode này mới được start đúng một API khi port trống, sau đó
vẫn phải health/identity-check trước khi mở UI. Shortcut production khóa cứng
`-StartupMode ProductionService`, nên không thể vô tình dùng hành vi development.

Launcher không đọc `NeedSync`, không yêu cầu `runId`, không gọi POST
`session-start-sync` và không chờ Auto Sync. Vì vậy endpoint trạng thái operation
không hoạt động cũng không thể giữ cửa sổ ở vòng lặp thử lại. Mọi health retry đều
có timeout. Khi thất bại, hộp thoại cho phép **Thử lại**, **Xem chi tiết** hoặc
**Thoát**; launcher không mở một listener chưa xác minh.

Auto Sync startup và nút Auto Sync của Admin là các luồng backend độc lập. Lỗi Auto
Sync không làm server dừng, không chặn đăng nhập hoặc mở ứng dụng. Trang Đồng bộ dữ
liệu CSĐT hiển thị operation đang chạy, nguồn lỗi và thời điểm sync thành công gần
nhất.

Nếu port 8088 không trả đúng QLHV identity/contract, launcher báo
`PORT_IN_USE_BY_UNKNOWN_PROCESS` cùng port, PID (nếu đọc được), health failure và
đường dẫn log. Launcher không kill listener. Nếu API QLHV chưa ready, launcher không
restart/dừng API; nó báo lỗi để người vận hành kiểm tra. Thông báo chỉ ra file cần kiểm tra:

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

## Realtime CSDT Worker service

Near-realtime V2-to-V1 synchronization runs outside the web server in one
non-web Windows service:

```text
Service name: QLHV_APP_RealtimeWorker
Startup:      Automatic
Executable:   D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe
Config:       D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json
```

The Worker is pinned to the Production environment. Its publish output excludes
`appsettings.Development.json`; it loads the protected Production Local file
outside Git and never opens a web listener. The production API service owns the
only HTTP listener on port 8088; the Desktop launcher is a connect-only client.

The installer and updater publish the API and Worker into one staging tree. They
stop the exact service (after validating its fixed name, LocalSystem account and
exact executable path) before replacing `runtime\app`. The service stays stopped
during API readiness/smoke checks so deployment cannot start synchronization
early. It is configured with delayed recovery actions and started only after
activation succeeds. A failed activation restores the previous application tree
and the previous running/stopped service state. `Stop-QLHV-App.ps1` stops both the
exact Worker service and the verified API PID; it never kills arbitrary
`dotnet.exe` processes. Uninstall removes the exact service before deleting the
runtime directory.

Production Local normalization preserves connection strings and unrelated local
values, while adding the fixed realtime defaults:

- enabled with a 1-second poll interval and 5-minute reconciliation;
- `OTO_V2` to `OTO_V1`, center `66029`;
- `MOTO_V2` to `MOTO_V1`, center `66030`;
- live profiles only (`UseBackupProfiles=false`).

These scripts do not run SQL patches, create baselines, or invoke synchronization
POST endpoints. Apply the approved Change Tracking/state/schema patches and
baseline separately before starting the service. Service recovery is for
transient runtime failures; persistent schema/configuration errors must be fixed
from the documented deployment procedure rather than bypassed.

## Runtime launcher and local configuration hardening

The installed Desktop shortcut does not depend on the source checkout. Its target is:

```text
D:\QLHV_APP_RUNTIME\launcher\Start-QLHV-App.cmd
```

The installer stages both launcher files before activation. The updater stages and
replaces the runtime launcher together with the application, migrates the Public
Desktop shortcut to the runtime path, and restores the previous launcher if
activation or read-only smoke checks fail. The shortcut working directory is the
runtime launcher directory, not `D:\QLHV_APP`.

Before an existing `appsettings.Production.Local.json` is normalized, its prior
content is retained under the protected directory:

```text
D:\QLHV_APP_RUNTIME\config\backups
```

The backup keeps the protected config ACL. A failed deployment restores the prior
configuration while retaining a separate failed-version backup for diagnosis. The
normalizer preserves all unrelated local values and secrets. Missing operational
values default to full guarded synchronization (`DryRun=false`, target writes
enabled, startup Auto Sync enabled, refresh before sync, source order OTO then MOTO).
Photo processing defaults to disabled, including automatic post-sync processing.
It is forced back to disabled unless the configured model, SHA-256, accepted SPDX
license, license manifest, and manifest SHA-256 all validate. Production Local files,
their backups, models, and real photos remain outside Git.

The daily launcher only settles one runtime process and bounded liveness/readiness,
then opens `/qlhv-import`. An existing healthy runtime keeps the same PID; a missing
runtime is started once under the cross-session launcher locks. Desktop launch does
not read `NeedSync`, require a `runId`, call the session-start POST, or wait for Auto
Sync. Consequently a missing/failed operation-status endpoint cannot hold the
browser in a retry loop. If bounded readiness fails while the process is still live,
the launcher reports/logs the warning and still opens the application for login and
diagnosis. This non-blocking rule supersedes the earlier session-coordination daily
launch sequence.

Refresh BAK và full sync có thể được Admin chủ động chạy trong ứng dụng, hoặc được
orchestrator Auto Sync gọi theo guard hiện có. Launcher không gọi trực tiếp endpoint
`refresh-backup`, `import-execute` hay `session-start-sync`. Backend vẫn áp dụng
durable active slot, applock, snapshot token, transaction và duplicate guards cho
các luồng Auto Sync độc lập.
