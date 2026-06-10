# Dong bo V2 sang QLHV_APP - Phase A design

Phase A builds the safe foundation only. It does not run sync, does not schedule Hangfire jobs, does not execute SQL scripts, and does not write to SQL Server.

## Scope

- Source: `CSDT_V2`, read-only.
- Target: `QLHV_APP`.
- First entity: `HocVien`.
- API: `POST /api/dong-bo-v2/hoc-vien/dry-run`.
- Runtime behavior: return a safe dry-run plan, connection status, non-secret issues, and planned field mapping.

Out of scope for Phase A:

- Opening live SQL connections for sync execution.
- `SqlBulkCopy`, merge/upsert, transactions, rollback execution.
- Scheduling recurring Hangfire jobs.
- Admin UI implementation.
- Writing encrypted connection settings to the database.

## Secure Connection Settings

`QLHV_APP` is the bootstrap database. Its connection string must come from one of these protected server-side sources:

- Environment variables, for example `ConnectionStrings__QLHV_APP`.
- ASP.NET Core user-secrets during local development.
- Protected server configuration in deployment.

`QLHV_APP` must not be hardcoded in source code or committed with real credentials.

`CSDT_V1` and `CSDT_V2` are source-system connection settings. They are represented by keys in configuration now, but their real values will later be managed from the Admin screen named **Cau hinh ket noi du lieu**. The allowed roles for that screen are:

- `Admin`
- `Giam doc trung tam`

Planned Admin behavior:

- Create/update source connection settings for `CSDT_V1` and `CSDT_V2`.
- Store passwords or full connection strings encrypted at rest.
- Never return full connection strings to the frontend.
- Display passwords as masked text only, for example `********`.
- Provide a **Test Connection** action.
- Allow enable/disable for each source connection.
- Audit log every create, update, test, enable, and disable action.

Audit log records must not include passwords, tokens, or full connection strings. Store only safe metadata such as source key, action, actor, timestamp, success/failure, and sanitized error code/message.

## Backend Structure

Application layer:

- `IHocVienSyncService` exposes `DryRunHocVienAsync`.
- `HocVienSyncService` builds the dry-run result only.
- `IConnectionSettingsProvider` defines secure connection resolution without exposing secrets in API responses.
- `IV2HocVienSourceRepository` and `IQlhvHocVienTargetRepository` define Phase B read/write contracts.
- `HocVienSyncMapping` records the planned field mapping and uncertainty.

Infrastructure layer:

- `ServerConnectionSettingsProvider` resolves the bootstrap `QLHV_APP` connection from server configuration and reserves `CSDT_V1`/`CSDT_V2` keys for the later Admin-managed storage.
- `V2HocVienSourceRepository` and `QlhvHocVienTargetRepository` are Phase A stubs. They throw if called so accidental SQL access does not happen.
- `HocVienSyncJob` is the Hangfire job entry point, but it only calls dry-run in Phase A.
- `SyncJobRegistration.ConfigureRecurringJobs()` intentionally schedules nothing.
- `SyncRetryPolicyFactory` defines retry structure for Phase B. Phase A does not wrap or execute sync work with it.

API layer:

- `DongBoV2Controller` exposes `POST /api/dong-bo-v2/hoc-vien/dry-run`.
- Response DTOs never include connection strings or passwords.

## Dry-Run Response

The dry-run endpoint returns:

- `canRun`: whether non-secret configuration appears usable.
- `status`: `SanSang` or `ThieuCauHinh`.
- `issues`: safe text explaining missing/placeholder settings.
- `target`: safe status for `QLHV_APP`.
- `source`: safe status for `CSDT_V2`.
- `plannedSummary`: zero-count planned sync summary.
- `mapping`: planned V2-to-QLHV_APP field mapping.
- `batchSize` and `timeoutSeconds`.

Dry-run does not:

- Open SQL connections.
- Test SQL credentials.
- Read V2 data.
- Write `QLHV_APP`.
- Write Hangfire or sync logs.

## Confirmed Mapping: HocVien

> **Cập nhật Phase B3H:** Ánh xạ hiển thị bởi dry-run Học viên dùng cùng nguồn đã chốt trong
> [`hoc-vien-v2-mapping.md`](./hoc-vien-v2-mapping.md) và đã đối chiếu với
> `database/reference/V2_schema_full.sql`.

Target table: `QLHV_APP.dbo.App_HocVien`.

Target columns confirmed from `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`:

| Target | Source | Confidence |
| --- | --- | --- |
| `MaDK` | `NguoiLX.MaDK` / `NguoiLX_HoSo.MaDK` | Confirmed |
| `HoTen` | `NguoiLX.HoVaTen` | Confirmed |
| `NgaySinh` | `NguoiLX.NgaySinh` | Confirmed |
| `GioiTinh` | `NguoiLX.GioiTinh` | Confirmed source; raw value preserved |
| `SoCCCD` | `NguoiLX.SoCMT` | Confirmed source; trim/preserve only |
| `DiaChiThuongTru` | `NguoiLX.NoiTT` | Confirmed |
| `SoGPLXDaCo` | `NguoiLX_HoSo.SoGPLXDaCo` | Confirmed |
| `HangGPLXDaCo` | `NguoiLX_HoSo.HangGPLXDaCo` | Confirmed |
| `NguoiNhanHoSo` | `NguoiLX_HoSo.NguoiNhanHSo` | Confirmed |
| `TenKhoa` | `KhoaHoc.TenKH` | Confirmed |
| `MaKhoa` | `NguoiLX_HoSo.MaKhoaHoc` / `KhoaHoc.MaKH` | Confirmed |
| `HangGPLXHoc` | `NguoiLX_HoSo.HangGPLX` | Confirmed related field |

## Remaining Data Questions

The source columns are now mapped, but Phase B still needs human confirmation from real test data for:

- `NguoiLX.GioiTinh` raw value semantics for display conversion.
- `NguoiLX.SoCMT` data quality: CCCD 12-digit vs legacy CMND 9-digit ratio.
- Whether `TrangThai` should filter cancelled/inactive rows.
- Whether the UI "Hạng GPLX" filter should apply to `NguoiLX_HoSo.HangGPLX` or `HangGPLXDaCo`.

## Phase B Preconditions

Before enabling real sync:

- Confirm V2 schema mapping.
- Implement encrypted storage for Admin-managed `CSDT_V1`/`CSDT_V2` settings.
- Implement authorization for `Admin` and `Giam doc trung tam`.
- Implement sanitized audit logging for connection setting actions.
- Implement real Test Connection with safe timeout and sanitized errors.
- Implement read-only Dapper queries against V2.
- Implement transactional target upsert/merge with rollback on error.
- Add explicit enable switch before scheduling any Hangfire recurring job.

## Phase B3A: Target upsert foundation (CHƯA thực thi ghi)

Phase B3A chuẩn bị nền tảng upsert vào `QLHV_APP.dbo.App_HocVien` nhưng **không ghi SQL Server**.

Thành phần (Application):
- `HocVienTargetWriteModel`: mô hình giá trị ghi vào App_HocVien (đã áp quy tắc dữ liệu).
- `HocVienSyncMapper`: hàm thuần ánh xạ + kiểm tra (trim, bảo toàn gốc, cảnh báo CCCD ≠ 12 số).
- `HocVienSyncPlanner`: dựng kế hoạch dry-run (Insert/Update/Skip) từ dòng nguồn + tập khóa đích — hàm thuần, không ghi.
- DTO kế hoạch/cảnh báo: `HocVienSyncPlanDto`, `HocVienSyncPlanItemDto`, `HocVienDataWarningDto`.
- `SyncRunLogEntry` + `ISyncRunLogWriter`: cấu trúc ghi `App_DongBoLog` — CHƯA thực thi ghi.

Thành phần (Infrastructure):
- `QlhvHocVienTargetRepository`: CHỈ ĐỌC (`CountAsync`, `GetExistingKeysAsync`) dùng để phân loại
  Insert/Update; `UpsertBatchAsync` ném lỗi (chưa hiện thực ghi).
- `HocVienTargetMergeSql`: cấu trúc SqlBulkCopy (staging `#Sync_HocVien_Staging`) + `MERGE` keyed on
  `MaDK`, UPDATE khi `V2RowHash` khác, INSERT khi chưa có, **không DELETE** (không xóa vật lý).
  Đây là SQL chuẩn bị, **không được thực thi** ở Phase B3A.
- `SyncRunLogWriter`: chứa `INSERT App_DongBoLog` tham số hóa (CHƯA thực thi).

Quy tắc dữ liệu áp dụng: xem [`hoc-vien-data-rules.md`](./hoc-vien-data-rules.md).

### Hành vi dry-run (giữ an toàn)
- Dry-run chỉ tính kế hoạch (đọc nguồn + đối chiếu khóa đích bằng SELECT). Không INSERT/UPDATE/DELETE,
  không SqlBulkCopy, không ghi `App_DongBoLog`.
- Khi kết nối còn placeholder: trả về trạng thái thiếu cấu hình an toàn, không lộ chuỗi kết nối.

### Cần xác nhận trước khi ghi thật (Phase B3B)
- Quy ước `GioiTinh char(1)` và thực trạng `SoCMT` (CCCD 12 số) — ảnh hưởng cảnh báo, không chặn.
- Công thức `V2RowHash` để phát hiện thay đổi (chọn cột tham gia hash).
- Có áp `IsDeleted`/đánh dấu bản ghi nguồn biến mất hay không (mặc định không xóa vật lý).
- Bật công tắc cho phép ghi (enable switch) trước khi thực thi và trước khi lập lịch Hangfire.

## Phase B3B: Guarded target write path

Phase B3B implements the write code path for `CSDT_V2` HocVien -> `QLHV_APP.dbo.App_HocVien`, but keeps it locked by default. Development/build must not execute the endpoint or run sync jobs.

### Execution guards

Server-side options:

- `SyncExecution:EnableTargetWrites` default `false`.
- `SyncExecution:RequireManualConfirmation` default `true`.
- `SyncExecution:AllowHangfireSchedule` default `false`.
- `SyncExecution:ConfirmationPhrase` default `EXECUTE_DONG_BO_V2_HOC_VIEN`.

Manual endpoint:

```text
POST /api/dong-bo-v2/hoc-vien/execute
```

The endpoint rejects unless:

- `EnableTargetWrites=true` in protected server configuration.
- The request body includes `ConfirmTargetWrites=true`.
- The request body includes `ConfirmationText="EXECUTE_DONG_BO_V2_HOC_VIEN"` when manual confirmation is required.
- `QLHV_APP` and `CSDT_V2` connections are usable and not placeholders.

Default repository defense also rejects writes when `EnableTargetWrites=false`, even if a caller bypasses the API controller.

### V2RowHash formula

`V2RowHash` is SHA-256 hex over length-prefixed, normalized fields from `HocVienTargetWriteModel`:

```text
MaDK
MaKhoa
TenKhoa
HangGPLXHoc
HoTen
NgaySinh formatted yyyy-MM-dd
GioiTinh
SoCCCD
DiaChiThuongTru
SoGPLXDaCo
HangGPLXDaCo
NguoiNhanHoSo
SourceOfTruth
```

Normalization is the mapper output defined in `HocVienSyncMapper`: trim strings, empty to null, preserve source values, do not convert CMND to CCCD, do not interpret `GioiTinh`. Volatile fields are excluded: `LastSyncFromV2At`, `LastSyncStatus`, `LastSyncMessage`, `UpdatedAt`, `RowVersion`.

### Upsert transaction

`QlhvHocVienTargetRepository.UpsertBatchAsync` performs one transaction per batch:

1. Resolve `QLHV_APP` connection internally. The connection string is never returned or logged.
2. Begin SQL transaction.
3. Create temp table `#Sync_HocVien_Staging`.
4. Map and hash source rows with `HocVienSyncMapper`.
5. Bulk load mapped rows into staging with `SqlBulkCopy`.
6. `MERGE` staging into `dbo.App_HocVien` keyed by `MaDK`.
7. Insert rows missing in target.
8. Update matched rows only when `ISNULL(tgt.V2RowHash, '') <> ISNULL(src.V2RowHash, '')`.
9. Skip matched rows with identical hash.
10. Do not include a delete clause. No physical delete is performed.
11. Commit on success; rollback on any exception.

The MERGE output is counted as safe summary data only: inserted, updated, skipped. It does not expose raw CCCD/GPLX values.

### App_DongBoLog

`SyncRunLogWriter.WriteAsync` writes one sanitized run summary row to `dbo.App_DongBoLog` after guarded manual execution:

- `JobName`
- `EntityType`
- `SourceSystem`
- `StartedAt`
- `EndedAt`
- `DurationMs`
- `Status`
- `TotalRead`
- `TotalInserted`
- `TotalUpdated`
- `TotalSkipped`
- `TotalError`
- `RetryCount`
- `ErrorMessage`
- `DetailJson`
- `CreatedBy`

`DetailJson` contains counts/status/error codes only. Do not put CCCD, GPLX raw values, passwords, tokens, usernames with passwords, or full connection strings into log fields.

### Hangfire

The job class remains ready, but Phase B3B does not schedule recurring jobs. `AllowHangfireSchedule=false` is the default, and scheduling belongs to Phase B4 or later after a separate review.

## Phase B3D: Local dry-run preparation

Phase B3D prepares local dry-run against `QLHV_APP_TEST` and `CSDT_V2_TEST`. It must not call the execute endpoint, enable target writes, schedule Hangfire, or write `App_HocVien` / `App_DongBoLog`.

Safe configuration endpoint:

```text
GET /api/dong-bo-v2/hoc-vien/config-check
```

Response fields only:

- `qlhvAppConfigured`
- `csdtV2Configured`
- `enableTargetWrites`
- `requireManualConfirmation`
- `allowHangfireSchedule`

The configured flags mean the backend sees non-placeholder local/test configuration. The endpoint does not open SQL connections, read source rows, write target rows, write sync logs, or return server/database/user/password/connection string values.

Manual dry-run endpoint remains:

```text
POST /api/dong-bo-v2/hoc-vien/dry-run
```

Dry-run is still no-write. Run it only after config-check confirms both local/test connections are configured and `enableTargetWrites=false`.
