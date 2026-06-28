# Task 5 B3W19 - HocVien Multi-Source Readiness Verification

Date: 2026-06-28

Scope: verify code/test readiness after the TEST-only HocVien database constraint patch. No SQL was run by this task, no sync was run, `/execute` was not called, and `EnableTargetWrites` was not enabled.

## TEST Database Patch Result

The `QLHV_APP_TEST` patch was reported as completed before this verification:

- `App_HocVien` total rows: `1970`.
- `SourceProfileCode = DATA_V2`: `1970`.
- `SourceMaDK = MaDK`: `1970`.
- `SourceSystem = V2`: `1970`.
- Missing source identity: `0`.
- Duplicate `SourceProfileCode + SourceMaDK`: `0`.
- `UQ_App_HocVien_MaDK`: dropped.
- `IX_App_HocVien_MaDK`: still exists.
- `UX_App_HocVien_SourceProfileCode_SourceMaDK_Active`: exists, unique, filtered.

## Build Result

Command:

```powershell
cd D:\QLHV_APP\server
dotnet build QLHV.sln -c Release
```

Result:

- Succeeded.
- Warnings: `0`.
- Errors: `0`.

## Focused Sync Test Result

Command:

```powershell
dotnet test QLHV.Tests\QLHV.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~QLHV.Tests.Sync"
```

Result:

- Passed: `37`.
- Skipped: `1`.
- Failed: `0`.
- Total: `38`.

The skipped test is the SQL integration opt-in safety test.

## Remaining MaDK-Only Write Identity Search

Search patterns:

- `ON tgt.MaDK = src.MaDK`
- `PRIMARY KEY (MaDK`
- `GetExistingKeysAsync`

Results:

### `ON tgt.MaDK = src.MaDK`

No runtime write SQL usage remains.

Remaining matches are historical docs and a negative regression test:

- `docs/task5-b3w14-b3w16-hocvien-multisource-write-identity.md`
- `docs/task5-b3w13-hocvien-sync-multisource-audit.md`
- `server/QLHV.Tests/Sync/HocVienTargetMergeSqlTests.cs`

### `PRIMARY KEY (MaDK`

No matches.

### `GetExistingKeysAsync`

No runtime code usage remains.

Remaining matches are historical docs:

- `docs/task5-b3w13-hocvien-sync-multisource-audit.md`

## Current Write Identity

Code write identity is:

```sql
ON tgt.SourceProfileCode = src.SourceProfileCode
AND tgt.SourceMaDK = src.SourceMaDK
```

The staging table primary key is:

```sql
PRIMARY KEY (SourceProfileCode, SourceMaDK)
```

`MaDK` remains a business/display field and is no longer the sole target write identity.

## Readiness Conclusion

HocVien multi-source write identity foundation is now aligned across:

- code write model;
- mapper source identity context;
- staging table;
- target MERGE identity;
- focused Sync tests;
- TEST database unique constraint strategy.

However, execute/sync is still not allowed yet. Before any execute test, the next step should be read-only pre-execute planning/dry-run review against the selected source profile. `EnableTargetWrites` must remain `false` until that plan is reviewed and explicitly approved for a local/test-only run.

