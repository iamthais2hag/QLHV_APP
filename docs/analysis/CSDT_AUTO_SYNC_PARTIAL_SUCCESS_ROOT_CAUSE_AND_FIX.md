# CSDT Auto Sync partial-success root cause and fix

Ngày kiểm tra: 2026-07-27  
Phạm vi: Existing QLHV_APP Auto Sync trên màn hình `Nhập dữ liệu CSDT → Auto Sync`.

## Kết quả cuối

- Root cause đã được xác định và sửa trong source/runtime.
- Release build, full regression, activation và read-only smoke test: đạt.
- Runtime mới đang phục vụ tại `D:\QLHV_APP_RUNTIME\app\QLHV.Api.exe`.
- Polling production được tắt bằng override theo tiến trình
  `QlhvAutoSync.RunOnServerStartup=false`; cấu hình file vẫn giữ `Enabled=true`.
- Production Auto Sync executed after fix: **YES — đúng một lượt**.
- Final RunId:
  `182ddbfa-b47f-47ec-b5f9-01830f74ad26`.
- Global, OTO và MOTO đều `SUCCEEDED`; toàn bộ 18 cổng hậu kiểm đạt.
- Kết luận:
  `EXISTING AUTO SYNC FULL-SUCCESS BASELINE VERIFIED`.

Sau khi operator đăng nhập, lượt mới đã xuất hiện và hoàn tất trên màn hình Admin
trước khi Codex bấm nút. Codex không gửi thêm request và không gọi lần thứ hai; số
run bền vững chỉ tăng từ 3 lên 4.

Không khởi động hoặc sửa RT-01/V2→V1. Không chạy SQL patch, DDL, Change
Tracking, snapshot, backup/restore thủ công, DELETE hoặc deactivation.

## Exact root cause

Ba RunId `6ad5a5d6`, `31fbe482`, `b284b897` có cùng một dấu vết cho cả OTO
và MOTO:

| Domain | Requested thực tế | Dữ liệu nguồn | Kết quả legacy | Có thay đổi dữ liệu |
|---|---:|---:|---|---:|
| `KHOA_HOC` | Có | OTO 4; MOTO 2 | `NO_OP` | Không |
| `GIAO_VIEN` | Không | 0 | `SKIPPED_SOURCE_NOT_READY` | Không |
| `KHOA_HOC_GIAO_VIEN` | Không | 0 | `SKIPPED_DEPENDENCY_NOT_READY` | Không |
| `HOC_VIEN` | Có, bắt buộc | OTO 152; MOTO 5 | `SUCCESS` hoặc `NO_OP` | Theo plan |

Read-only schema/count probe xác nhận:

- `dbo.GiaoVien` và `dbo.KhoaHoc_GiaoVien` đều tồn tại trong cả hai BAK.
- `CSDL_OTO_BAK`: giáo viên toàn DB = 0, giáo viên của CSDT 66029 = 0,
  quan hệ của CSDT = 0.
- `CSDL_MOTO_BAK`: giáo viên toàn DB = 0, giáo viên của CSDT 66030 = 0,
  quan hệ của CSDT = 0.
- `QLHV_APP`: active `App_GiaoVien` và `App_KhoaHoc_GiaoVien` của cả
  `CSDT_OTO`/`CSDT_MOTO` đều bằng 0.

Vì plan chỉ đưa domain có source model và không có blocker vào
`ExecutableDomains`, hai domain trên không phải requested operations. Repository
legacy vẫn trả `SKIPPED_*_NOT_READY`; sau đó `QlhvImportService` coi mọi optional
result khác `SUCCESS`/`NO_OP` là issue. Đây là:

`DOMAIN_RESULT_AGGREGATION_BUG`

Không phải `OPTIONAL_SCHEMA_MISSING`, `OPTIONAL_WRITE_FAILED`,
`MAPPING_VALIDATION_FAILED` hoặc runtime exception.

## Photo processing

Production Local hiện có:

- `PhotoProcessing.Enabled=false`
- `PhotoProcessing.AutoProcessAfterSync=false`

Các run cũ có photo queue OTO `152/0/152/0` và MOTO `5/0/5/0`
(`requested/queued/skipped/failed`). Tuy nhiên photo queue được gọi sau khi DB
commit và sau khi `overallStatus` đã được tính. Nó không nằm trong
`optionalIssues` đã gây partial.

Kết luận: `PHOTO_PROCESSING_DISABLED` là cảnh báo độc lập, không phải exact root
cause. Build mới báo module ảnh là `SKIPPED_DISABLED`, `requested=false`,
`contributesToPartial=false`.

## Dấu vết ba run cũ

| Run | Source | History | Source/Insert/Skip | Domain gây partial legacy |
|---|---|---|---:|---|
| `6ad5a5d6` | OTO | `PARTIAL_SUCCESS` | 156/4/152 | GV + quan hệ, đều 0 row và không requested |
| `6ad5a5d6` | MOTO | `PARTIAL_SUCCESS` | 7/0/7 | GV + quan hệ, đều 0 row và không requested |
| `31fbe482` | OTO | `PARTIAL_SUCCESS` | 156/0/156 | Như trên |
| `31fbe482` | MOTO | `PARTIAL_SUCCESS` | 7/0/7 | Như trên |
| `b284b897` | OTO | `PARTIAL_SUCCESS` | 156/0/156 | Như trên |
| `b284b897` | MOTO | `PARTIAL_SUCCESS` | 7/0/7 | Như trên |

## Semantics sau sửa

- Domain không nằm trong plan được ghi `SKIPPED_NOT_REQUESTED`.
- `SKIPPED_NOT_REQUESTED` và `SKIPPED_DISABLED` không đóng góp vào
  `PARTIAL_SUCCESS`.
- Optional domain đã requested mà `FAILED`/not-ready vẫn tạo
  `PARTIAL_SUCCESS`.
- Required `HOC_VIEN` failure vẫn tạo `FAILED`.
- `SUCCESS`/`NO_OP` của tất cả requested domains cho phép source result thành
  `SUCCEEDED`.
- Photo processing post-commit luôn được báo riêng và không đổi kết quả core DB.
- Warning vẫn được persist riêng trong plan/source result/history.

Mỗi domain result nay có:

- `requested`, `enabled`, `required`;
- `snapshotState`, `schemaState`;
- `attempted`, `committed`, `skipped`;
- `status`, `failureCode`, `requestReasonCode`, `reason`;
- counts và `contributesToPartial`;
- skipped breakdown theo reason.

Auto Sync source result persist cùng breakdown, nên reload/restart đọc từ durable
run JSON thay vì dựa vào frontend memory.

## Giải thích `Nguồn=156, Thêm=4, Bỏ qua=152`

Run OTO `6ad5a5d6`:

- Nguồn 156 = 4 khóa học + 152 học viên.
- Thêm 4 = 4 học viên mới.
- Bỏ qua 152 = 4 khóa học `NO_CHANGE` + 148 học viên `NO_CHANGE`.
- `ALREADY_EXISTS=0` như một reason riêng; dữ liệu tồn tại và cùng hash được phân
  loại chính xác là `NO_CHANGE`.
- `DISABLED_DOMAIN=0`, `NOT_REQUESTED=0`, `VALIDATION_REJECTED=0`, `OTHER=0`
  trong core count của run này vì hai optional domain có 0 source row.

Build mới persist `SkippedReasons` gồm:

`NoChange`, `NotRequested`, `Disabled`, `ValidationRejected`, `Other`, `Total`.

Photo skipped được hiển thị ở module ảnh riêng, không trộn vào core
`SkippedRows`.

## UI

Mỗi source OTO/MOTO có phần mở rộng “Chi tiết module”, hiển thị:

- Khóa học;
- Giáo viên;
- Phân công khóa học–giáo viên;
- Học viên / hồ sơ / giấy tờ;
- Ảnh thẻ.

`Hồ sơ` và `Giấy tờ` không phải independent transaction writers trong Existing
Auto Sync; chúng thuộc transaction `HOC_VIEN`, nên UI ghi rõ bằng một nhãn gộp
thay vì tạo kết quả giả.

UI hiển thị requested/enabled/required, snapshot/schema, attempted/committed,
status/reason code, counts, skipped reason và cờ ảnh hưởng partial. Module disabled
không bị tô như lỗi core.

## Files sửa trong AS-OPTIONAL

- `server/QLHV.Application/Sync/QlhvImportDomains.cs`
- `server/QLHV.Application/Sync/IQlhvImportWriteRepository.cs`
- `server/QLHV.Application/Sync/Dtos/QlhvImportDtos.cs`
- `server/QLHV.Application/Sync/Dtos/QlhvAutoSyncDtos.cs`
- `server/QLHV.Application/Sync/QlhvImportService.cs`
- `server/QLHV.Application/Sync/QlhvAutoSyncSourceRunner.cs`
- `server/QLHV.Infrastructure/Sync/QlhvHocVienTargetRepository.cs`
- `client/src/features/qlhv-import/types.ts`
- `client/src/features/qlhv-import/api.ts`
- `client/src/features/qlhv-import/AutoSyncPanel.tsx`
- `client/src/features/qlhv-import/QlhvImportPage.tsx`
- `client/src/styles/layout.css`
- `server/QLHV.Tests/Sync/QlhvImportServiceTests.cs`
- `server/QLHV.Tests/Sync/QlhvCourseTeacherFullSnapshotSyncSqlTests.cs`
- `server/QLHV.Tests/Sync/QlhvAutoSyncTests.cs`
- `server/QLHV.Tests/Sync/QlhvAutoSyncPhotoClientSourceTests.cs`
- báo cáo này.

## Tests/build

- Frontend `npm run build`: PASS, 73 modules.
- Backend Release build: PASS.
- Full .NET suite: 1022 passed, 0 failed, 2 opt-in tests skipped
  (1024 total).
- Focused optional-domain/UI/button acceptance: 10 passed, 0 failed.
- `Admin_auto_sync_button_request_returns_accepted_durable_run_id`: PASS bằng
  test host/fake durable run; không gọi production POST.
- Required failure, requested optional failure, non-requested optional skip,
  no-change, photo disabled, durable last-success/partial and source order đều
  nằm trong suite pass.
- Known warning không phát sinh từ task: `Magick.NET-Q16-AnyCPU 14.14.0`
  có NU1902 moderate advisory.

## Runtime activated

| Identity | Value |
|---|---|
| API build | `9dc3902795daaac74774b9ef4e3f1668c5e90ecba738aee5776753625f767b10` |
| Hosted Auto Sync worker build | `9dc3902795daaac74774b9ef4e3f1668c5e90ecba738aee5776753625f767b10` |
| Frontend build | `qlhv-ui-20260727012551` |
| Runtime instance | `9ac26a7e53aa497eb6e17d6747043bdb` |
| Executable | `D:\QLHV_APP_RUNTIME\app\QLHV.Api.exe` |
| Port | `8088` |
| Readiness | `isReady=true` |
| Resolved SourceOrder | `[OTO, MOTO]` |
| API/Worker config parity | `PASS` |
| Polling | disabled by process override `RunOnServerStartup=false` |
| Config file Enabled | `true` |
| Production Auto Sync executed after fix | `YES — exactly one run` |

Authenticated Admin UI served from the new build, reached “Hệ thống đã sẵn
sàng”, displayed the final successful run, and retained the same
`LastSuccessfulRunId`/time after a browser reload. Codex did not issue a second
production request after detecting the new run.

Previous runtime is recoverable at:

`D:\QLHV_APP_RUNTIME\run\as-optional-previous-app-20260727`

## Data integrity before the final run

| Source | Live NguoiLX | BAK NguoiLX | QLHV active | Duplicate active identity groups |
|---|---:|---:|---:|---:|
| OTO | 152 | 152 | 152 | 0 |
| MOTO | 5 | 5 | 5 | 0 |

QLHV_APP có 3 OTO soft-deleted rows đã tồn tại trước checkpoint. Tất cả sáu
full-sync operations của ba RunId cũ được trace đều có `SoftDeletedRows=0`.

## Config/worktree safety

- Không reset/clean/stash/revert/restore/stage/commit/merge/push.
- Staged files: 0.
- Protected config hashes không đổi:
  - API Development:
    `12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E`
  - Worker Development:
    `12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E`
- Production Local hash không đổi qua activation:
  `9847629CE2D576BB72C23F34AF8B50E8E3F65002DC805C3AF339DDCA8FB5F632`.

## Lượt production cuối

### Run identity

| Thuộc tính | Giá trị |
|---|---|
| RunId | `182ddbfa-b47f-47ec-b5f9-01830f74ad26` |
| Trigger / actor | `MANUAL` / `MANUAL_ADMIN` |
| SourceOrder | `[OTO, MOTO]` |
| Global status / stage | `SUCCEEDED` / `COMPLETED` |
| CreatedAtUtc | `2026-07-27T01:43:13.7333333Z` |
| StartedAtUtc | `2026-07-27T01:43:13.7533333Z` |
| CompletedAtUtc | `2026-07-27T01:43:21.4633333Z` |
| Error | `null` |
| ActiveSlot sau hoàn tất | `null` |

### Stage timeline

| Source/stage | OperationId | Bắt đầu UTC | Kết thúc UTC | Kết quả |
|---|---|---|---|---|
| OTO source | — | `01:43:13.7659081` | `01:43:17.7140041` | `SUCCEEDED` |
| OTO refresh BAK | `bb94ada6-c1df-4408-8ac2-d8816515a806` | `01:43:13.7833333` | `01:43:17.3600000` | `SUCCEEDED`; source 152 |
| OTO sync QLHV_APP | `6e0933e2-3b04-4e4e-a128-56a966d9e0a6` | `01:43:17.5533333` | `01:43:17.7033333` | history `SUCCEEDED`; core `NO_OP`; source 156, skipped/no-change 156 |
| MOTO source | — | `01:43:17.7247593` | `01:43:21.4588713` | `SUCCEEDED` |
| MOTO refresh BAK | `e8bdfeaa-7807-4383-a521-fde165d7f84a` | `01:43:17.7266667` | `01:43:21.1533333` | `SUCCEEDED`; source 5 |
| MOTO sync QLHV_APP | `d5387906-e0c0-47f8-936f-261b3388c871` | `01:43:21.3966667` | `01:43:21.4566667` | history `SUCCEEDED`; core `NO_OP`; source 7, skipped/no-change 7 |
| Global complete | — | — | `01:43:21.4633333` | `SUCCEEDED` |

OTO hoàn tất trước khi MOTO bắt đầu; mỗi source xuất hiện đúng một lần trong cùng
RunId.

### Domain results

| Source | Domain | Requested | Trạng thái | Counts chính | contributesToPartial |
|---|---|---:|---|---|---:|
| OTO | `KHOA_HOC` | true | `NO_OP` | source/no-change `4/4`; soft-delete 0 | false |
| OTO | `GIAO_VIEN` | false | `SKIPPED_NOT_REQUESTED` | source 0; soft-delete 0 | false |
| OTO | `KHOA_HOC_GIAO_VIEN` | false | `SKIPPED_NOT_REQUESTED` | source 0; soft-delete 0 | false |
| OTO | `HOC_VIEN` | true | `NO_OP` | source/no-change `152/152`; soft-delete 0 | false |
| MOTO | `KHOA_HOC` | true | `NO_OP` | source/no-change `2/2`; soft-delete 0 | false |
| MOTO | `GIAO_VIEN` | false | `SKIPPED_NOT_REQUESTED` | source 0; soft-delete 0 | false |
| MOTO | `KHOA_HOC_GIAO_VIEN` | false | `SKIPPED_NOT_REQUESTED` | source 0; soft-delete 0 | false |
| MOTO | `HOC_VIEN` | true | `NO_OP` | source/no-change `5/5`; soft-delete 0 | false |

Hai domain optional 0-row được persist đúng là `SKIPPED_NOT_REQUESTED`. Reason
code chẩn đoán vẫn được giữ riêng: `SKIPPED_SOURCE_NOT_READY` cho
`GIAO_VIEN` và `SKIPPED_DEPENDENCY_NOT_READY` cho quan hệ. Chúng là warning an
toàn, không phải failure và không đóng góp vào partial.

Photo processing của cả hai source là `SKIPPED_DISABLED`, `requested=false`,
`enabled=false`, `contributesToPartial=false`, với lý do
`PhotoProcessing.Enabled=false.` Đây là warning riêng, không hạ kết quả core.

### Counts và tính toàn vẹn trước/sau

| Source | Trước: Live/BAK/QLHV active | Sau: Live/BAK/QLHV active | Duplicate active trước/sau | Soft-deleted baseline trước/sau |
|---|---:|---:|---:|---:|
| OTO | `152/152/152` | `152/152/152` | `0/0` | `3/3` |
| MOTO | `5/5/5` | `5/5/5` | `0/0` | `0/0` |

Mọi mutation count của lượt mới đều bằng 0:
`InsertedRows=0`, `UpdatedRows=0`, `ReactivatedRows=0`,
`SoftDeletedRows=0`. Không có learner bị xóa/deactivate; OTO soft-deleted
baseline vẫn là 3.

### Durable state và 18 verification gates

- `LastSuccessfulRunId` =
  `182ddbfa-b47f-47ec-b5f9-01830f74ad26`.
- `LastSuccessfulSyncUtc` =
  `2026-07-27T01:43:21.4633333Z`.
- Reload UI vẫn hiển thị cùng RunId/time, global “Thành công”, actor
  `MANUAL_ADMIN`, OTO và MOTO “Đã nhập QLHV_APP”.
- Read-only hậu kiểm lúc `2026-07-27T01:52:22.7538312Z`: tổng run = 4,
  active run = 0, active slot = 0, active operation = 0; latest run vẫn là
  RunId trên.
- Polling vẫn `enabled=false`, `isPolling=false`,
  `lastPollStartedAtUtc=null`, `nextPollAtUtc=null`; reason:
  `QlhvAutoSync.RunOnServerStartup=false.`
- Runtime/readiness vẫn đúng executable/port/build/frontend; health ready,
  SourceOrder `[OTO, MOTO]`, API/Worker config parity `PASS`.
- Không có lượt Auto Sync thứ hai, không retry, không có conflict/duplicate,
  không có error code và không có delete/deactivation ngoài dự kiến.

Đối chiếu từng cổng operator: (1) global success PASS; (2) OTO success PASS;
(3) MOTO success PASS; (4) cùng RunId PASS; (5) history success PASS; (6)
LastSuccessfulRunId PASS; (7) LastSuccessfulSyncUtc PASS; (8) reload durable
state PASS; (9) OTO counts PASS; (10) MOTO counts PASS; (11) duplicate groups
PASS; (12) new SoftDeletedRows=0 PASS; (13) OTO soft-deleted baseline=3 PASS;
(14) no learner delete/deactivation PASS; (15) optional 0-row semantics PASS;
(16) photo warning riêng PASS; (17) polling off PASS; (18) no second run PASS.

## Final conclusion

`EXISTING AUTO SYNC FULL-SUCCESS BASELINE VERIFIED`
