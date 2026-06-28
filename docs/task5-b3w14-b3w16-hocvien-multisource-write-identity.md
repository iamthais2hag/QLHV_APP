# Task 5 B3W14-B3W16 - HocVien Multi-Source Write Identity

Date: 2026-06-27

Scope: prepare the HocVien sync write path for multi-source identity. No SQL was run, no sync was run, no execute endpoint was called, and `EnableTargetWrites` was not enabled.

## Summary

B3W14-B3W16 changes the guarded HocVien write foundation from MaDK-only identity toward source-scoped identity.

The target write identity is now:

```text
SourceProfileCode + SourceMaDK
```

`MaDK` remains a business/display field, but it is no longer the only key used by the target MERGE statement.

## B3W14 - Code Identity Plumbing

Updated write model fields:

- `SourceProfileCode`
- `SourceMaDK`
- `SourceSystem`
- `SourceVersion`

Current V2 path maps source identity as:

| Field | Value |
| --- | --- |
| `SourceProfileCode` | `DATA_V2` |
| `SourceMaDK` | source `MaDK` |
| `SourceSystem` | `V2` |
| `SourceVersion` | `NULL` |

The mapper also supports a `DATA_V1` context:

| Field | Value |
| --- | --- |
| `SourceProfileCode` | `DATA_V1` |
| `SourceMaDK` | source `MaDK` |
| `SourceSystem` | `V1` |
| `SourceVersion` | `NULL` |

The current `/api/dong-bo-v2/hoc-vien/execute` endpoint still reads the V2 source path and uses `DATA_V2` context. This task does not introduce a DATA_V1 execute endpoint and does not approve execute.

## Changed Write Path

Current write path remains:

```text
POST /api/dong-bo-v2/hoc-vien/execute
  -> HocVienSyncService.ExecuteHocVienAsync
  -> V2HocVienSourceRepository.ReadPageAsync
  -> HocVienSyncMapper.MapAndValidate(..., DATA_V2)
  -> QlhvHocVienTargetRepository.UpsertBatchAsync
  -> SqlBulkCopy #Sync_HocVien_Staging
  -> HocVienTargetMergeSql MERGE dbo.App_HocVien
```

The staging table now includes:

- `SourceProfileCode`
- `SourceMaDK`
- `SourceSystem`
- `SourceVersion`
- existing HocVien mapped fields

The repository now validates that source identity fields are present before resolving the target connection or writing.

## MERGE Identity Before/After

Before:

```sql
ON tgt.MaDK = src.MaDK
```

After:

```sql
ON tgt.SourceProfileCode = src.SourceProfileCode
AND tgt.SourceMaDK = src.SourceMaDK
```

This prevents DATA_V1 from updating DATA_V2 rows, and prevents DATA_V2 from updating DATA_V1 rows, when both sources contain the same `MaDK`.

## Planning/Lookup Change

The target repository lookup contract was changed from MaDK-only keys to source-scoped keys:

```text
GetExistingSourceKeysAsync(sourceProfileCode, sourceMaDks)
```

`HocVienSyncPlanner` now classifies insert/update by `SourceProfileCode + SourceMaDK`, not by raw `MaDK`.

## B3W15 - Test Coverage

Focused tests were added/updated for:

- mapper defaulting V2 rows to `DATA_V2/V2`;
- mapper accepting `DATA_V1/V1` context;
- planner treating the same `MaDK` under different source profiles as different target identities;
- MERGE SQL no longer containing `ON tgt.MaDK = src.MaDK`;
- staging table primary key using `SourceProfileCode + SourceMaDK`;
- MERGE inserting source identity columns;
- repository rejecting write rows that lack required source identity before resolving a connection.

## B3W16 - DB Patch Draft

Patch draft created:

```text
database/patches/20260627_prepare_hocvien_multisource_identity_unique_key.sql
```

This patch was not run.

The draft is conservative:

- checks `dbo.App_HocVien` exists;
- checks source identity columns exist;
- blocks if active rows still have missing `SourceProfileCode` or `SourceMaDK`;
- blocks if duplicate active `SourceProfileCode + SourceMaDK` rows exist;
- creates a filtered unique index for active `SourceProfileCode + SourceMaDK` rows if approved/run later;
- intentionally stops before dropping `UQ_App_HocVien_MaDK` unless a human changes `@AllowDropMaDkUniqueConstraint` to `1`.

## Remaining DB Constraint Risk

The main schema still defines:

```text
UQ_App_HocVien_MaDK
```

That constraint can still block valid multi-source imports if DATA_V1 and DATA_V2 contain the same `MaDK`.

The runtime MERGE has been changed to source identity, but execute/sync should still remain blocked until the database constraint strategy is manually reviewed and applied on test first.

## Why Execute/Sync Is Still Not Allowed

Execute remains unsafe for real multi-source operations until:

1. the DB unique constraint strategy is reviewed;
2. the patch draft is manually approved and tested on `QLHV_APP_TEST`;
3. read-only diagnostics confirm all existing rows have valid source identity;
4. pre-execute plan is updated/verified per source profile;
5. authorization/role policy for execute is confirmed before any real environment;
6. `Sync:DryRun`, `EnableTargetWrites`, and manual confirmation policy are intentionally reviewed for local test only.

Do not call `/execute` as part of B3W14-B3W16.

## Manual SQL Review Required

Before any DB patch is run, a human must review:

- whether `UQ_App_HocVien_MaDK` can be dropped safely;
- whether any other foreign keys or app screens assume global unique `MaDK`;
- whether a filtered unique index on `(SourceProfileCode, SourceMaDK)` is the final target constraint;
- whether `MaDK` remains a display/business field or should eventually become source-qualified in dependent tables.

The patch draft should be treated as a review artifact, not an approved production migration.

## Suggested Verification Commands

```powershell
cd D:\QLHV_APP\server
dotnet build QLHV.sln -c Release
dotnet test QLHV.Tests\QLHV.Tests.csproj -c Release --filter "FullyQualifiedName~QLHV.Tests.Sync"
```

Do not run SQL integration tests unless the explicit opt-in for local/test databases is intentionally set.

