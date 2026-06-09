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

## Planned Mapping: HocVien

Target table: `QLHV_APP.dbo.App_HocVien`.

Target columns confirmed from `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`:

| Target | Planned source | Confidence |
| --- | --- | --- |
| `MaDK` | `CSDT_V2.dbo.NguoiLX_HoSo.MaDK` / `NguoiLX.MaDK` | Inferred |
| `HoTen` | `NguoiLX.HoVaTen` | Inferred |
| `NgaySinh` | `NguoiLX.NgaySinh` | Inferred |
| `GioiTinh` | `NguoiLX.GioiTinh` | Inferred from scripts, confirm in real V2 |
| `SoCCCD` | `NguoiLX.SoCMT` | Uncertain: CCCD vs CMT naming must be confirmed |
| `DiaChiThuongTru` | likely `NguoiLX.NoiTT` or related address columns | Uncertain |
| `SoGPLXDaCo` | `NguoiLX_HoSo.SoGPLXDaCo` or `NguoiLX_GPLX.SoGPLX` | Uncertain |
| `HangGPLXDaCo` | `NguoiLX_HoSo.HangGPLXDaCo` or `NguoiLX_GPLX.HangGPLX` | Uncertain |
| `NguoiNhanHoSo` | likely `NguoiLX_HoSo.NguoiNhanHSo` | Uncertain |
| `TenKhoa` | `KhoaHoc.TenKH` | Inferred |
| `MaKhoa` | `KhoaHoc.MaKH` or `NguoiLX_HoSo.MaKhoaHoc` | Inferred |
| `HangGPLXHoc` | `KhoaHoc.HangGPLX` or `NguoiLX_HoSo.HangGPLX` | Uncertain |

## Schema Mapping Uncertainty

The repo contains QLHV_APP schema and V1/V2 transfer scripts, but not a complete authoritative CSDT_V2 schema. Phase B needs human confirmation or a read-only schema export for:

- Whether `SoCMT` is the canonical CCCD field for current V2 data.
- Whether permanent address maps to `NoiTT`, `NoiTT_MaDVHC`, `NoiTT_MaDVQL`, or another field.
- Whether existing license fields should prefer `NguoiLX_HoSo` or latest row from `NguoiLX_GPLX`.
- Whether `NguoiNhanHoSo` in QLHV_APP maps to V2 `NguoiNhanHSo` exactly.
- How to choose a single active course/license row if V2 has multiple related records per `MaDK`.

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
