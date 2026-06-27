# Task 5 B3W13 - HocVien Sync Multi-Source Identity Audit

Date: 2026-06-27

Scope: read-only code audit for HocVien sync/write readiness after the project moved to multi-source identity. No SQL, no sync, no execute endpoint, no database writes, and no code behavior changes were performed.

## Summary

The current HocVien write path is still a guarded single-source V2 path. It can only write when several safety gates are deliberately opened, but if opened it still upserts `dbo.App_HocVien` by `MaDK` alone.

That is not multi-source ready. After DATA_V1 and DATA_V2 are both imported into QLHV_APP, `MaDK` alone can collide across sources. The write identity must become `SourceProfileCode + MaDK` or `SourceProfileCode + SourceMaDK` before any multi-source execute/import is allowed.

Current code does not set these target identity fields when writing:

- `SourceProfileCode`
- `SourceMaDK`
- `SourceSystem`
- `SourceVersion`

So the correct B3W14 direction is implementation work to make the execute/upsert/planning path source-scoped before the next execute test.

## Files Inspected

- `server/QLHV.Api/Controllers/DongBoV2Controller.cs`
- `server/QLHV.Api/Program.cs`
- `server/QLHV.Api/appsettings.json`
- `server/QLHV.Api/appsettings.Development.json`
- `server/QLHV.Worker/appsettings.json`
- `server/QLHV.Worker/appsettings.Development.json`
- `server/QLHV.Application/Sync/HocVienSyncService.cs`
- `server/QLHV.Application/Sync/HocVienSyncPlanner.cs`
- `server/QLHV.Application/Sync/IQlhvHocVienTargetRepository.cs`
- `server/QLHV.Application/Sync/Mapping/HocVienSyncMapper.cs`
- `server/QLHV.Application/Sync/Mapping/HocVienTargetWriteModel.cs`
- `server/QLHV.Application/Sync/Mapping/V2RowHashCalculator.cs`
- `server/QLHV.Application/Sync/Configuration/SyncExecutionOptions.cs`
- `server/QLHV.Application/Sync/SyncOptions.cs`
- `server/QLHV.Application/Sync/HocVienSourceAttributionDiagnosticsService.cs`
- `server/QLHV.Application/Sync/Dtos/HocVienSourceAttributionDiagnosticsDto.cs`
- `server/QLHV.Infrastructure/Sync/QlhvHocVienTargetRepository.cs`
- `server/QLHV.Infrastructure/Sync/HocVienTargetMergeSql.cs`
- `server/QLHV.Infrastructure/Sync/V2HocVienSourceRepository.cs`
- `server/QLHV.Infrastructure/Sync/HocVienV2SqlBuilder.cs`
- `server/QLHV.Infrastructure/Sync/SyncRunLogWriter.cs`
- `server/QLHV.Infrastructure/Sync/HocVienSyncJob.cs`
- `server/QLHV.Infrastructure/Sync/SyncJobRegistration.cs`
- `server/QLHV.Infrastructure/Sync/HocVienSourceAttributionDiagnosticsRepository.cs`
- `server/QLHV.Tests/Sync`

## Current Write Path

Current execute/write flow:

1. `POST /api/dong-bo-v2/hoc-vien/execute`
2. `DongBoV2Controller.ExecuteHocVien`
3. `HocVienSyncService.ExecuteHocVienAsync`
4. `V2HocVienSourceRepository.ReadPageAsync`
5. `HocVienSyncMapper.Map`
6. `QlhvHocVienTargetRepository.UpsertBatchAsync`
7. `SqlBulkCopy` into temp table `#Sync_HocVien_Staging`
8. `HocVienTargetMergeSql.MergeStatement`
9. `MERGE dbo.App_HocVien`
10. `SyncRunLogWriter.WriteAsync` writes one summary row to `dbo.App_DongBoLog`

The only current HocVien target writer found is `QlhvHocVienTargetRepository.UpsertBatchAsync`. `SyncRunLogWriter` writes to `App_DongBoLog`, not `App_HocVien`.

Hangfire is not currently scheduling an execute path. `HocVienSyncJob` calls dry-run only, and `SyncJobRegistration.ConfigureRecurringJobs()` remains a no-op placeholder.

## MaDK-Only Identity Usages

These are the important MaDK-only usages for sync/write readiness:

| Location | Current behavior | Multi-source risk |
| --- | --- | --- |
| `HocVienTargetMergeSql.CreateStagingTable` | Staging table uses `MaDK NVARCHAR(50) NOT NULL PRIMARY KEY`. | Cannot stage the same `MaDK` for multiple sources. |
| `HocVienTargetMergeSql.MergeStatement` | `MERGE` uses `ON tgt.MaDK = src.MaDK`. | DATA_V1 can update DATA_V2 rows, or DATA_V2 can update DATA_V1 rows, if `MaDK` overlaps. |
| `QlhvHocVienTargetRepository.GetExistingKeysAsync` | Reads existing target keys by `MaDK` only. | Dry-run/pre-plan can classify rows incorrectly when another source already has the same `MaDK`. |
| `HocVienSyncPlanner` | Uses `existingTargetKeys.Contains(result.Model.MaDK)`. | Planned insert/update/skip is not source-scoped. |
| `HocVienTargetWriteModel` | Has `MaDK` but no `SourceProfileCode`, `SourceMaDK`, `SourceSystem`, or `SourceVersion`. | Mapper/repository cannot write source identity. |
| `HocVienSyncMapper` | Maps V2 fields to target model, but no source identity fields. | Inserts after backfill can create target rows without source attribution. |
| `V2RowHashCalculator` | Hash is based on V2 mapped values and `MaDK`, but no source profile context. | Change detection is not explicitly source-scoped. |

Read-only diagnostics also compare by `MaDK` in places, especially source attribution diagnostics. That is acceptable only for diagnostics/reporting. It must not become the write identity.

Source-side joins such as `NguoiLX.MaDK` to `NguoiLX_HoSo.MaDK` are source-internal and not the target identity risk by themselves.

## Existing Safety Guards

Current guard defaults are conservative:

- `Sync:DryRun = true`
- `SyncExecution:EnableTargetWrites = false`
- `SyncExecution:RequireManualConfirmation = true`
- `SyncExecution:AllowHangfireSchedule = false`
- `SyncExecution:ConfirmationPhrase = EXECUTE_DONG_BO_V2_HOC_VIEN`

Guard locations:

- `HocVienSyncService.ValidateExecutionGuard`
  - blocks execute when `Sync:DryRun = true`
  - blocks execute when `EnableTargetWrites = false`
  - requires `ConfirmTargetWrites = true`
  - requires exact confirmation phrase
- `QlhvHocVienTargetRepository.UpsertBatchAsync`
  - blocks writes when `Sync:DryRun = true`
  - blocks writes when `EnableTargetWrites = false`
- `SyncRunLogWriter.WriteAsync`
  - blocks log writes when `Sync:DryRun = true`
  - blocks log writes when `EnableTargetWrites = false`
- `SyncJobRegistration.ConfigureRecurringJobs`
  - currently schedules no recurring job

Authorization note: this audit did not find `[Authorize]` on the inspected API controller route. `Program.cs` calls `UseAuthorization()`, but the execute endpoint should not be considered production-ready based on authorization until a real auth/role policy is confirmed and enforced.

## Current Source Identity Write Readiness

The target database has source identity columns after B3W7/B3W10/B3W11:

- `SourceProfileCode`
- `SourceMaDK`
- `SourceSystem`
- `SourceVersion`

But the sync write code does not use them yet:

- `HocVienTargetWriteModel` does not contain these fields.
- `HocVienSyncMapper` does not set these fields.
- `QlhvHocVienTargetRepository.BuildStagingTable` does not include these columns.
- `HocVienTargetMergeSql.CreateStagingTable` does not include these columns.
- `HocVienTargetMergeSql.MergeStatement` does not insert/update these columns.
- `HocVienTargetMergeSql.MergeStatement` matches rows by `MaDK` only.
- Target diagnostics required-column list does not currently require source identity columns for execute readiness.

Also, `HocVienSyncService` still reads the source through `SourceSystem.V2` and emits summary/log source system as `"V2"`. That reflects the old single-source design, not an explicit DATA_V1/DATA_V2 connection profile execution model.

## Risks If Running Sync Now

If someone intentionally changes `Sync:DryRun=false`, `EnableTargetWrites=true`, and sends the exact confirmation phrase, execute can write.

In that state, the current code has these risks:

1. `MERGE ON tgt.MaDK = src.MaDK` can update a row from the wrong source when DATA_V1 and DATA_V2 share a `MaDK`.
2. New inserted rows would not receive `SourceProfileCode`, `SourceMaDK`, `SourceSystem`, or `SourceVersion`.
3. Planning/dry-run existing-key checks are also MaDK-only, so pre-run counts can be misleading in multi-source scenarios.
4. Current execute endpoint does not require an explicit source profile such as `DATA_V1` or `DATA_V2`.
5. Current source connection path still uses `SourceSystem.V2`, not the CSDT connection profile table as the source selector.
6. Existing backfilled DATA_V2 rows can be overwritten by a later DATA_V1 import if the same `MaDK` appears.
7. No physical delete exists in the MERGE, which is good, but source collision updates are still possible.

Conclusion: execute/import for HocVien should remain blocked for multi-source use until B3W14 fixes the write identity.

## Recommended B3W14 Implementation Plan

Recommended goal: make HocVien execute/upsert source-scoped and block any write path that does not provide a valid source profile.

Suggested steps:

1. Add an explicit source profile execution contract.
   - Accept or resolve `SourceProfileCode` for import, initially only `DATA_V1` or `DATA_V2`.
   - Reject execute if source profile is missing, unsupported, inactive, or not connection-tested.
   - Keep DATA_V1/DATA_V2 as technical staging/import profiles, not Moto/Oto.

2. Extend sync write model.
   - Add `SourceProfileCode`.
   - Add `SourceMaDK`.
   - Add `SourceSystem`.
   - Add `SourceVersion`.
   - For DATA_V2 imports, use `SourceProfileCode = DATA_V2`, `SourceMaDK = MaDK`, `SourceSystem = V2`, and leave `SourceVersion` null until a convention is approved.

3. Update mapper to receive source context.
   - Do not hardcode source profile inside a low-level mapper without context.
   - Keep source identity stable and explicit.

4. Update staging and bulk copy.
   - Include `SourceProfileCode`, `SourceMaDK`, `SourceSystem`, `SourceVersion` in `#Sync_HocVien_Staging`.
   - Use a staging primary key such as `(SourceProfileCode, MaDK)` or `(SourceProfileCode, SourceMaDK)`.

5. Update MERGE identity.
   - Replace `ON tgt.MaDK = src.MaDK` with source-scoped matching, for example:
     `ON tgt.SourceProfileCode = src.SourceProfileCode AND tgt.MaDK = src.MaDK`
   - Do not update rows belonging to a different `SourceProfileCode`.
   - Insert source identity fields for new rows.
   - Do not physically delete target-only rows.

6. Update target lookup and planning.
   - Replace `GetExistingKeysAsync(maDks)` with source-scoped target lookup.
   - Plan insert/update/skip by `(SourceProfileCode, MaDK)`.
   - Update pre-execute plan to report per source profile.

7. Update V2RowHash decision.
   - Review whether `SourceProfileCode`/`SourceMaDK` should be part of the hash.
   - Even if identity fields are not included in the row hash, all comparisons must be source-scoped.

8. Update diagnostics.
   - Target diagnostics should check that source identity columns exist.
   - Execute readiness should fail if existing target data has missing source identity for the selected operation.

9. Add focused tests.
   - Execute rejects missing/invalid source profile.
   - Merge SQL uses source-scoped identity, not `MaDK` alone.
   - Upsert inserts source identity fields.
   - Upsert does not update a row from another `SourceProfileCode` with the same `MaDK`.
   - Planner classifies insert/update/skip by source profile.
   - Existing guards still block when `Sync:DryRun=true`, `EnableTargetWrites=false`, confirmation is missing, or confirmation text is wrong.
   - Sync log still does not write when target writes are disabled.

10. Only after code and tests pass, rerun read-only diagnostics and pre-execute plan for DATA_V2 before any controlled execute test.

## Suggested Tests/Build Command

B3W13 only created this markdown report, so build/test is not required for this task.

For B3W14 implementation, suggested commands:

```powershell
cd D:\QLHV_APP\server
dotnet build QLHV.sln -c Release
dotnet test QLHV.sln -c Release
```

Do not run SQL integration tests unless the existing explicit opt-in environment variable is intentionally set for a local/test database.
