# QLHV time authority, clock skew and business-date safety

## Kết luận

QLHV_APP dùng ba loại thời gian tách biệt:

- `AUDIT_TIME`: UTC có thẩm quyền từ SQL Server (`SYSUTCDATETIME()`) hoặc từ
  API/Worker server qua `TimeProvider.GetUtcNow()` khi ứng dụng phải tạo giá trị.
- `BUSINESS_DATE`: dữ liệu ngày do người dùng chọn rõ ràng hoặc do hệ thống nguồn
  sở hữu. Không suy ra từ đồng hồ Windows của máy người dùng.
- `DISPLAY_TIME`: frontend chỉ chuyển timestamp UTC sang
  `Asia/Ho_Chi_Minh`. Đồng hồ trình duyệt chỉ dùng để hiển thị/chẩn đoán.

Không có production clock, timezone, NTP, dữ liệu, checkpoint, service hoặc binary
nào được thay đổi trong audit này.

## Fresh read-only audit

Mốc audit: `2026-07-30T15:51:36Z`.

### Topology

Các thành phần production hiện cùng trên máy `CSDLTTTC`:

| Thành phần | Evidence |
|---|---|
| API | `D:\QLHV_APP_RUNTIME\app\QLHV.Api.exe`, PID `14100` |
| Frontend | static files do chính QLHV.Api host |
| SQL Server | default instance `MSSQLSERVER`, service account `NT SERVICE\MSSQLSERVER`, PID `7440` |
| Realtime Worker | service `QLHV_APP_RealtimeWorker`, executable `D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe`; đang `Stopped`, PID `0` |

### Clock/NTP

| Quan sát | Kết quả |
|---|---|
| Windows timezone | `SE Asia Standard Time` (UTC+07:00, không DST) |
| Windows Time | `Running`, account `NT AUTHORITY\LocalService` |
| NTP source | `time.windows.com,0x9` |
| Last successful sync | `2026-07-30 19:23:09 +07:00` |
| Time since last good sync | `12,499.579 s` tại thời điểm đọc |
| Phase offset | `+20.5244 ms` |
| Last sync error | `2`, stale time data |
| SQL UTC sample | `2026-07-30T15:51:36.1843116Z` |
| API-side bracket | `15:51:36.1319423Z` → `15:51:36.1908171Z` |
| Monotonic round trip | `56.913 ms` |
| Ước lượng API/SQL skew tại midpoint | khoảng `-23.9 ms` |

API/SQL skew nằm sâu trong ngưỡng an toàn. NTP có last-good-sync gần và phase
offset nhỏ, nhưng `Last Sync Error=2` tạo trạng thái `WARNING`; lỗi này không tự
biến đồng hồ trình duyệt thành nguồn có thẩm quyền và không chứng minh nguyên
nhân của `RT03_UNSUPPORTED_DRIFT`.

## Audit nguồn thời gian trong workspace

Kết quả quét runtime source (loại `bin/obj`, tests và tài liệu):

| Pattern | Số hit | Phân loại và quyết định |
|---|---:|---|
| `DateTime.Now` | 2 | `DISPLAY_TIME`; chỉ đặt tên file export học viên. Không ghi audit/business date |
| `DateTime.Today` | 0 | Không dùng |
| `DateTimeOffset.Now` | 0 | Logger đã chuyển sang UTC |
| `DateTime.UtcNow` | 67 | server-side audit/diagnostic/source processing; không nhận từ client. Các transaction assignment quan trọng đã chuyển sang một SQL UTC duy nhất |
| `DateTimeOffset.UtcNow` | 13 | server-side diagnostics/realtime plan; không phải client authority |
| frontend `new Date(...)` | 20 | `DISPLAY_TIME` hoặc cảnh báo client/server skew |
| frontend `Date.now()` | 0 | Không còn dùng để cho phép/chặn confirm hoặc tạo idempotency key |
| runtime C# SQL `SYSDATETIME()` | 9 | semantics local-time legacy trong full-snapshot/Auto Sync đang disabled; giữ nguyên contract, không tự đổi nghĩa |
| runtime C# SQL `SYSUTCDATETIME()` | 174 | UTC audit/state mặc định |
| `GETDATE()` | 0 trong runtime C# | Các hit còn lại nằm trong reference/analysis/legacy SQL, không được đổi hàng loạt |
| `CURRENT_TIMESTAMP` | 0 | Không dùng |

### Bảng phân loại

| Loại | Ví dụ | Authority |
|---|---|---|
| `AUDIT_TIME` | assignment history, import confirm, idempotency ledger, realtime marker/checkpoint/heartbeat | SQL UTC trong transaction; API `TimeProvider` khi phải tạo timestamp |
| `BUSINESS_DATE` | ngày khai giảng/bế giảng, ngày chứng từ, ngày hiệu lực theo quyết định | field nhập/chọn rõ ràng hoặc source-owned |
| `DISPLAY_TIME` | trang trạng thái, tên file export, timestamp trên UI | UTC được render cố định bằng `Asia/Ho_Chi_Minh` |
| `DURATION/MONOTONIC_TIME` | preview TTL, readiness cache, health query duration | `TimeProvider.GetTimestamp/GetElapsedTime`, framework timers |
| `SOURCE_SYSTEM_TIME` | `NgayTao/NgaySua`, thời gian trong CSDL_OTO/MOTO và legacy sync | giữ nguyên semantics của nguồn; không tự diễn giải lại thành QLHV audit UTC |
| `EXTERNAL_FILE_TIME` | ngày nghiệp vụ trong Excel/import | dữ liệu file được parse/validate; thời điểm confirm do SQL UTC tạo |

## Thay đổi an toàn thời gian

### Health check

`GET /api/system/time-health` và `runtime-status.time` trả dữ liệu không chứa
secret:

- `ServerUtcNow`;
- `DatabaseUtcNow`;
- `ClockSkewMilliseconds`;
- monotonic query duration;
- persisted last-observed UTC;
- server timezone và display timezone;
- Windows Time state, NTP source, last successful sync;
- `TimeHealth = HEALTHY | WARNING | BLOCKED`;
- thông báo remediation privacy-safe.

Health check chỉ đọc SQL và chạy `w32tm /query /status /verbose`; không gọi HTTP
time API, không đổi đồng hồ, timezone hoặc NTP.

### Clock-skew policy

| Trạng thái | Điều kiện chính | Hành vi |
|---|---|---|
| `HEALTHY` | API/SQL skew ≤ 2 s; wall/monotonic đồng thuận; NTP đủ evidence | Đọc/ghi bình thường |
| `WARNING` | skew > 2 s và ≤ 30 s; NTP unavailable/stale/error; Windows Time không đọc được | Hiển thị cảnh báo; write vẫn được phép khi timestamp transaction lấy từ SQL/API server UTC |
| `BLOCKED` | SQL UTC unavailable; skew > 30 s; persisted UTC ở tương lai > 30 s; wall clock rollback/jump > 30 s so với monotonic; NTP phase offset > 30 s | Production API mutations trả `503 TIME_AUTHORITY_BLOCKED`; read-only diagnostics vẫn hoạt động |

Không block chỉ vì giờ máy khách khác server.

### Rollback/jump detection

- API và SQL khác máy: midpoint API/SQL skew phát hiện sai lệch.
- API và SQL cùng máy và cùng bị chỉnh: persisted UTC gần nhất trong realtime,
  assignment ledger hoặc sync operation history phát hiện timestamp lịch sử nằm
  trong tương lai.
- Trong một health query: wall elapsed được đối chiếu với monotonic elapsed.
- Preview TTL và readiness cache đo duration bằng monotonic clock, nên lùi/tiến
  wall clock không kéo dài hoặc làm hết hạn sớm trong process.
- Realtime worker state, cycle history, apply marker và checkpoint publish dùng
  database UTC. Trước mỗi vòng OTO→MOTO, Worker gọi cùng time-health policy và
  dừng fail-closed với `RT03_TIME_AUTHORITY_BLOCKED` nếu write không an toàn.
  Assignment manual/group/Excel confirm dùng một
  `SYSUTCDATETIME()` duy nhất cho transaction, audit và idempotency ledger.

### Frontend

Trang quản trị hiển thị giờ API, SQL, trình duyệt, hai loại skew, trạng thái
Windows Time/NTP và câu bắt buộc:

`Giờ máy người dùng không được dùng làm thời điểm ghi nhận hệ thống.`

Client-skew chỉ là warning. Frontend không còn so `Date.now()` với preview expiry
để quyết định quyền confirm; server là bên duy nhất xác nhận token còn hiệu lực.
Idempotency key dùng CSPRNG của trình duyệt, không chứa timestamp local.

## Business date

Người dùng vẫn có thể chọn ngày nghiệp vụ lùi/tiến bằng field trong phần mềm.
Contract lưu tách biệt:

- `BusinessDate`: ngày người dùng chọn/source cung cấp;
- audit timestamp UTC: thời điểm SQL/API thực sự ghi;
- actor;
- reason/source khi contract yêu cầu.

Không cần và không được đổi Windows clock để mô phỏng business date.

## Internet outage

QLHV_APP tiếp tục vận hành khi mất Internet nếu API và SQL local còn khỏe.
Windows Time/NTP mất đồng bộ tạo `WARNING`; không có external time API hay API key.
Nếu persisted-time/API/SQL evidence chứng minh clock unsafe thì mutation mới bị
`BLOCKED`, còn diagnostics vẫn đọc được.

## Kiểm thử

Kết quả tại workspace:

- backend Release build: PASS, 0 errors; có 1 cảnh báo package vulnerability đã
  tồn tại (`Magick.NET-Q16-AnyCPU 14.14.0`, `NU1902`);
- frontend production build: PASS;
- Time/Auth/Runtime/Assignment/RT03 focused regression: `218/218 PASS`;
- AssignmentFocused: `88/88 PASS`;
- RT03 namespace: `45/45 PASS`;
- health policy có test HEALTHY, warning skew, blocked skew, persisted-future
  rollback, wall/monotonic jump và NTP unavailable;
- preview fake `TimeProvider` có test wall clock lùi/tiến một ngày và monotonic
  expiry;
- production write guard có test POST bị chặn và GET diagnostics vẫn đi qua;
- KhoaHoc business change tiếp tục `UNKNOWN_UNSAFE`.

Không test nào thay đổi đồng hồ thật.

## File ảnh hưởng chính

- `server/QLHV.Application/Runtime/TimeAuthorityModels.cs`
- `server/QLHV.Infrastructure/Runtime/TimeAuthorityService.cs`
- `server/QLHV.Infrastructure/Runtime/RuntimeReadinessService.cs`
- `server/QLHV.Api/Runtime/TimeAuthorityWriteGuardMiddleware.cs`
- `server/QLHV.Api/Controllers/SystemRuntimeController.cs`
- `server/QLHV.Application/Assignments/AssignmentPreviewStore.cs`
- `server/QLHV.Infrastructure/Assignments/SqlAssignmentRepository*.cs`
- `server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRuntimeStateStore.cs`
- `server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeCycleProcessor.cs`
- `client/src/features/runtime-status/*`
- `client/src/features/course-assignment/ui.tsx`
- hai màn confirm assignment/import và `client/src/styles/layout.css`
- focused tests tương ứng.

## Production deployment boundary

Chưa deploy API/Worker/UI, chưa restart service, chưa chạy migration và chưa đổi
production data/state. Khi được phê duyệt riêng, deployment phải thay đồng bộ API,
UI và Worker assemblies, kiểm tra hash, gọi health endpoint, xác nhận API/SQL skew,
sau đó mới thử mutation. Việc này không được gộp với quyết định ownership cho
`CT26 dbo.KhoaHoc INSERT`; realtime vẫn đang fail-closed vì blocker riêng đó.

## Trả lời trực tiếp

1. Trước sửa, QLHV_APP dùng hỗn hợp SQL UTC và API server UTC; browser chỉ render
   nhưng có hai nút confirm từng tin `Date.now()`. Hai client gates đó đã bị loại.
2. Đổi giờ máy người dùng chỉ làm giờ hiển thị/cảnh báo sai; không đổi audit,
   import confirm, assignment history, idempotency, preview authority hoặc
   realtime checkpoint.
3. Authority sau sửa là SQL/API server UTC; transaction mutation ưu tiên
   `SYSUTCDATETIME()`, duration dùng monotonic time.
4. Có. Mất Internet/NTP chỉ tạo warning nếu SQL/API/persisted-time vẫn an toàn.
5. Nếu chính server bị đổi giờ, API/SQL skew, wall-vs-monotonic và persisted-future
   checks phát hiện. Trên ngưỡng 30 giây, production API write bị chặn; hệ thống
   không tự sửa clock/checkpoint/history.
6. Có. BusinessDate vẫn nhập/chọn riêng, độc lập với audit UTC.
7. Không có thay đổi production nào đã được áp dụng.
