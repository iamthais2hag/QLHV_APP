# Dong bo V2 sang QLHV_APP - current design through Phase B3R

Task 5 has progressed beyond the original Phase A foundation. The current code includes the guarded
HocVien write path from Phase B3B and the no-database safety tests from Phase B3C. Target writes remain
disabled by default, Phase B3R is the mapping-readiness review before local dry-run, and Phase B4 Hangfire
scheduling has not been implemented.

## Scope

- Source: `CSDT_V2`, read-only.
- Target: `QLHV_APP`.
- First entity: `HocVien`.
- APIs: configuration check, dry-run, and guarded manual execute for HocVien.
- Dry-run behavior: resolve safe connection status, connect read-only to `CSDT_V2`, run `COUNT`, and return
  non-secret issues plus the confirmed field mapping. It does not write the target or sync log.
- Execute behavior: read source rows and write `App_HocVien`/`App_DongBoLog` only after all execution guards pass.

Current boundaries:

- No production execution is approved.
- No recurring Hangfire job is registered or scheduled; scheduling belongs to Phase B4 or later.
- Admin UI implementation.
- Writing encrypted connection settings to the database.
- Authorization for the execute endpoint has not been implemented because the application does not yet have
  an authentication/role pipeline. The endpoint must not be exposed for real environments until authorization exists.

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

- `IHocVienSyncService` exposes config-check, dry-run, and guarded manual execute operations.
- `HocVienSyncService` validates execution guards before reading pages or calling any writer.
- `IConnectionSettingsProvider` defines secure connection resolution without exposing secrets in API responses.
- `IV2HocVienSourceRepository` is read-only; `IQlhvHocVienTargetRepository` owns guarded target upsert.
- `HocVienSyncMapping` records the confirmed field mapping and remaining data questions.

Infrastructure layer:

- `ServerConnectionSettingsProvider` currently resolves `QLHV_APP` and `CSDT_V2` from protected server configuration.
- `V2HocVienSourceRepository` executes parameterized read-only `COUNT`/`SELECT` queries.
- `QlhvHocVienTargetRepository` uses staging, `SqlBulkCopy`, and transactional `MERGE`; it rejects calls when
  `EnableTargetWrites=false`.
- `SyncRunLogWriter` writes a sanitized run summary and independently rejects calls when
  `EnableTargetWrites=false`.
- `HocVienSyncJob` remains a dry-run-only Hangfire entry point.
- `SyncJobRegistration.ConfigureRecurringJobs()` intentionally schedules nothing.
- `SyncRetryPolicyFactory` wraps source reads and target batches with Polly retry.

API layer:

- `DongBoV2Controller` exposes `GET config-check`, `POST dry-run`, and guarded `POST execute` endpoints.
- Response DTOs never include connection strings or passwords.

## Dry-Run Response

The dry-run endpoint returns:

- `canRun`: whether non-secret configuration appears usable.
- `status`: `SanSang` or `ThieuCauHinh`.
- `issues`: safe text explaining missing/placeholder settings.
- `target`: safe status for `QLHV_APP`.
- `source`: safe status for `CSDT_V2`.
- `sourceRecordCount`: read-only count from `CSDT_V2` when the source configuration is usable.
- `plannedSummary`: no-write summary whose `TotalRead` reflects the source count.
- `mapping`: planned V2-to-QLHV_APP field mapping.
- `batchSize` and `timeoutSeconds`.

Dry-run does:

- Resolve both configured connections without returning their values.
- Open a read-only connection to `CSDT_V2` and run `SELECT COUNT` through `V2HocVienSourceRepository`.

Dry-run does not:

- Read source detail pages.
- Write `QLHV_APP`.
- Write `App_DongBoLog` or schedule Hangfire.

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
| `DiaChiThuongTru` | `DM_DVHC.TenDayDu` from `NguoiLX.NoiTT_MaDVQL + NguoiLX.NoiTT_MaDVHC = DM_DVHC.MaDV`; fallback `NguoiLX.NoiTT` | Confirmed |
| `SoGPLXDaCo` | `NguoiLX_HoSo.SoGPLXDaCo` | Confirmed |
| `HangGPLXDaCo` | `NguoiLX_HoSo.HangGPLXDaCo` | Confirmed |
| `NguoiNhanHoSo` | `NguoiLX_HoSo.NguoiNhanHSo` | Confirmed |
| `TenKhoa` | `KhoaHoc.TenKH` | Confirmed |
| `MaKhoa` | `NguoiLX_HoSo.MaKhoaHoc` / `KhoaHoc.MaKH` | Confirmed |
| `MaHangDT` | `NguoiLX_HoSo.HangDaoTao` | Confirmed source; local data coverage must still be checked |
| `HangGPLXHoc` | `NguoiLX_HoSo.HangDaoTao` -> `DM_HangDT.MaHangDT` -> `DM_HangDT.TenHangDT` | Confirmed related field |

## Remaining Data Questions

The source columns are now mapped, but Phase B still needs human confirmation from real test data for:

- `NguoiLX.GioiTinh` raw value semantics for display conversion.
- `NguoiLX.SoCMT` data quality: CCCD 12-digit vs legacy CMND 9-digit ratio.
- Whether `TrangThai` should filter cancelled/inactive rows.
- Whether the UI "Hạng GPLX/Hạng học" filter label should be renamed, now that synced `HangGPLXHoc` comes from
  `NguoiLX_HoSo.HangDaoTao` -> `DM_HangDT.TenHangDT`.

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

## Phase B3A: Historical target upsert foundation

Phase B3A originally prepared the target upsert without enabling SQL writes. The deferred write pieces described
below were implemented later by the guarded Phase B3B path.

Thành phần (Application):
- `HocVienTargetWriteModel`: mô hình giá trị ghi vào App_HocVien (đã áp quy tắc dữ liệu).
- `HocVienSyncMapper`: hàm thuần ánh xạ + kiểm tra (trim, bảo toàn gốc, cảnh báo CCCD ≠ 12 số).
- `HocVienSyncPlanner`: dựng kế hoạch dry-run (Insert/Update/Skip) từ dòng nguồn + tập khóa đích — hàm thuần, không ghi.
- DTO kế hoạch/cảnh báo: `HocVienSyncPlanDto`, `HocVienSyncPlanItemDto`, `HocVienDataWarningDto`.
- `SyncRunLogEntry` + `ISyncRunLogWriter`: contract prepared in B3A; the guarded writer is implemented in B3B.

Thành phần (Infrastructure):
- `QlhvHocVienTargetRepository`: was read-only during B3A; B3B now implements guarded `UpsertBatchAsync`.
- `HocVienTargetMergeSql`: cấu trúc SqlBulkCopy (staging `#Sync_HocVien_Staging`) + `MERGE` keyed on
  `MaDK`, UPDATE khi `V2RowHash` khác, INSERT khi chưa có, **không DELETE** (không xóa vật lý).
  Đây là SQL chuẩn bị, **không được thực thi** ở Phase B3A.
- `SyncRunLogWriter`: chứa `INSERT App_DongBoLog` tham số hóa (CHƯA thực thi).

Quy tắc dữ liệu áp dụng: xem [`hoc-vien-data-rules.md`](./hoc-vien-data-rules.md).

### Hành vi dry-run hiện tại (giữ an toàn)
- The current endpoint resolves configuration and runs only `COUNT` against `CSDT_V2`. It does not call
  `HocVienSyncPlanner`, read target keys, INSERT/UPDATE/DELETE, use `SqlBulkCopy`, or write `App_DongBoLog`.
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
- The request body includes `Confirm=true`.
- The request body includes `ConfirmationText="EXECUTE_DONG_BO_V2_HOC_VIEN"` when manual confirmation is required.
- `QLHV_APP` and `CSDT_V2` connections are usable and not placeholders.

Default repository defense also rejects writes when `EnableTargetWrites=false`, even if a caller bypasses the API controller.

### V2RowHash formula

`V2RowHash` is SHA-256 hex over length-prefixed, normalized fields from `HocVienTargetWriteModel`:

```text
MaDK
MaKhoa
TenKhoa
MaHangDT
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

`SyncRunLogWriter` also checks `SyncExecution:EnableTargetWrites` before resolving the target connection.
This defense prevents a direct writer call from creating `App_DongBoLog` rows while target writes are disabled.

### Authorization status

The current API does not have an authentication/role pipeline, so the execute endpoint has no role attribute yet.
The enable switch and confirmation phrase prevent accidental execution but are not authorization. Do not expose or
enable execute in production until server-side authentication and an approved role policy are implemented.

## Phase B3C: Safety tests

Phase B3C provides unit tests that require no SQL Server connection:

- execute rejects when target writes are disabled;
- execute rejects missing confirmation or a non-exact phrase;
- dry-run performs no target/log writes;
- mapping/data rules and `V2RowHash` stability are protected;
- `SyncRunLogWriter` rejects before resolving a connection when target writes are disabled.

SQL integration tests are opt-in through `QLHV_RUN_SQL_INTEGRATION_TESTS=true` and remain skipped by default.

## Phase B3R: Mapping readiness before local dry-run

B3R does not change sync behavior. It is a readiness pass to make sure code, docs, and schema assumptions agree
before the first local dry-run against `CSDT_V2_TEST`.

Confirmed from current code:

- `HocVienV2SqlBuilder` reads `NguoiLX`, `NguoiLX_HoSo`, `KhoaHoc`, `DM_HangDT`, and `DM_DVHC`.
- `MaHangDT` is selected from `NguoiLX_HoSo.HangDaoTao`.
- `HangGPLXHoc` is selected from `DM_HangDT.TenHangDT`.
- `DiaChiThuongTru` uses `DM_DVHC.TenDayDu`, falling back to `NguoiLX.NoiTT` in the mapper.
- `V2RowHash` includes both `MaHangDT` and `HangGPLXHoc`.
- `LastSyncFromV2At`, `LastSyncStatus`, `LastSyncMessage`, `UpdatedAt`, and `RowVersion` are excluded from the hash.

Readiness checklist and optional reference SQL are in
[`sync-v2-mapping-readiness.md`](./sync-v2-mapping-readiness.md). Those SQL snippets are documentation only; do
not run them against production.

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
