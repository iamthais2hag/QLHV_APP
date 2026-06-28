# Task 5 B3W23 - Pre-execute concept wording audit

## Scope

This audit checks wording around HocVien pre-execute planning and source identity after the B3W22 concept correction.

Rules followed:

- docs/audit first;
- no runtime behavior change;
- no SQL;
- no sync;
- no `/execute`;
- no `EnableTargetWrites`;
- no production database.

## Files inspected

Docs:

- `docs/task5-b3w22-sync-source-concept-correction.md`
- `docs/task5-b3w21-pre-execute-plan-actual-result.md`
- `docs/task5-b3w20-hocvien-readonly-pre-execute-plan.md`
- `docs/sync-data-flow-architecture.md`
- `docs/qlhv-app-multisource-merge-rules.md`
- `docs/hoc-vien-multisource-identity.md`
- `docs/sync-v2-pre-execute-checklist.md`
- `docs/csdt-connection-profiles.md`

Code/API wording:

- `server/QLHV.Api/Controllers/DongBoV2Controller.cs`
- `server/QLHV.Application/Sync/HocVienSyncService.cs`
- `server/QLHV.Application/Sync/IHocVienSyncService.cs`
- `server/QLHV.Application/Sync/SyncOptions.cs`
- `server/QLHV.Application/Sync/Mapping/HocVienSourceIdentityContext.cs`
- `server/QLHV.Application/Sync/Dtos/HocVienSyncPlanDto.cs`
- `server/QLHV.Infrastructure/Sync/HocVienTargetMergeSql.cs`
- `server/QLHV.Infrastructure/Sync/QlhvHocVienTargetRepository.cs`

## Confusing wording found

1. `docs/sync-data-flow-architecture.md` had a diagram that implied:
   - `CSDT_MOTO -> DATA_V1`
   - `CSDT_OTO -> DATA_V2`

   That could be read as a finalized Moto/Oto to V1/V2 business mapping. B3W22 clarified that current `CSDT_V1` and
   `CSDT_V2` represent one Moto business transition flow, while future Oto work may be more complex.

2. `docs/qlhv-app-multisource-merge-rules.md` correctly protected source scopes, but some wording could be read as a
   business decision to preserve duplicated V1/V2 learner rows. B3W22 clarified that source scopes are technical
   import/audit boundaries.

3. `docs/task5-b3w20-hocvien-readonly-pre-execute-plan.md` and
   `docs/task5-b3w21-pre-execute-plan-actual-result.md` documented the current `DATA_V2` pre-execute result, but did not
   explicitly say this is a technical test-state conclusion only.

4. `docs/sync-v2-pre-execute-checklist.md` still referenced older B3V2 wording as WIP. Since B3W20/B3W21 have since
   merged a read-only plan, that wording needed clarification: the current plan is useful diagnostics, not execute
   approval.

5. Code comments around `CSDT_V2` remain mostly tied to the current V2 endpoint and source reader. The important runtime
   source identity comment already exists in `HocVienSourceIdentityContext`:

   ```text
   DATA_V1/DATA_V2 are technical import profiles, not Moto/Oto business groups.
   ```

   No code comment change was necessary for this task.

## Changes made

Updated `docs/sync-data-flow-architecture.md`:

- Added B3W22 concept correction.
- Reworded the diagram as profile slots and possible staging routes, not finalized Moto/Oto business mapping.
- Marked Oto staging design as TBD.
- Clarified that source-scoped identity is technical safety, not final learner presentation.

Updated `docs/qlhv-app-multisource-merge-rules.md`:

- Clarified that `DATA_V1` and `DATA_V2` are technical source scopes.
- Reworded preservation rules as import-layer safety, not permanent business duplication.
- Added Moto old/new reconciliation as a conflict-policy question.

Updated `docs/task5-b3w20-hocvien-readonly-pre-execute-plan.md`:

- Added concept correction near the top.
- Clarified `SourceProfileCode + SourceMaDK` is a technical import key, not final learner identity.
- Added a blocker requiring Moto old/new data-flow meaning review before execute.

Updated `docs/task5-b3w21-pre-execute-plan-actual-result.md`:

- Clarified the `DATA_V2` no-write-needed conclusion is technical/test-state only.

Updated `docs/csdt-connection-profiles.md`:

- Clarified that `DATA_V1` and `DATA_V2` do not automatically mean duplicated learners in QLHV_APP business screens.

Updated `docs/sync-v2-pre-execute-checklist.md`:

- Replaced stale B3V2 wording with current B3W20/B3W21 read-only-plan wording.
- Clarified that the current pre-execute plan is diagnostics only and not execute approval.

## Behavior change

No code behavior changed.

No DTO field, API route, service logic, repository SQL, write/upsert logic, guard, or configuration was changed.

## Current interpretation after audit

- `QLHV_APP` remains the business management app, not a sync-only app.
- `CSDT_V1` and `CSDT_V2` are current test names in one Moto business transition flow.
- The 7 connection profiles remain valid as technical configuration slots.
- `SourceProfileCode + SourceMaDK` remains the correct sync/upsert/audit identity.
- That technical identity must not be mistaken for a final business rule to duplicate learners.
- Execute/sync remains blocked until explicitly approved after read-only review.

## Verification

Docs-only changes. No build required.

