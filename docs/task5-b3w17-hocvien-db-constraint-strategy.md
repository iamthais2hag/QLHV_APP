# Task 5 B3W17 - HocVien DB Constraint Strategy Review

Date: 2026-06-28

Scope: review only. No SQL was run, no database was modified, no sync was run, and `/execute` was not called.

## Summary

B3W14-B3W16 changed the HocVien write path to use:

```text
SourceProfileCode + SourceMaDK
```

The remaining database risk is the original global unique constraint:

```text
UQ_App_HocVien_MaDK UNIQUE (MaDK)
```

That constraint is incompatible with true multi-source imports if `DATA_V1` and `DATA_V2` can contain the same `MaDK`.

Recommendation for `QLHV_APP_TEST`: create/keep the source-scoped unique filtered index, then drop the MaDK-only unique constraint only after manual approval and backup.

## Files Reviewed

- `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`
- `database/patches/20260627_add_hocvien_source_profile.sql`
- `database/patches/20260627_backfill_hocvien_source_profile_data_v2.sql`
- `database/patches/20260627_rollback_hocvien_source_profile_data_v2.sql`
- `database/patches/20260627_prepare_hocvien_multisource_identity_unique_key.sql`
- `docs/hoc-vien-multisource-identity.md`
- `docs/task5-b3w14-b3w16-hocvien-multisource-write-identity.md`

## UQ_App_HocVien_MaDK Origin

`UQ_App_HocVien_MaDK` is created in the main schema file:

```text
database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql
```

Relevant table definition:

```sql
CREATE TABLE dbo.App_HocVien (
    HocVienId BIGINT IDENTITY(1,1) PRIMARY KEY,
    MaDK NVARCHAR(50) NOT NULL,
    ...
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_App_HocVien_MaDK UNIQUE (MaDK)
);
```

The same schema later creates a non-unique filtered lookup index:

```sql
CREATE NONCLUSTERED INDEX IX_App_HocVien_MaDK
ON dbo.App_HocVien(MaDK)
WHERE IsDeleted = 0;
```

The B3W6 schema patch intentionally did not drop `UQ_App_HocVien_MaDK`. It only added nullable source identity columns and source-scoped helper indexes:

- `IX_App_HocVien_SourceProfileCode_MaDK`
- `IX_App_HocVien_SourceProfileCode_SourceMaDK`

It also left a TODO to review/drop or replace `UQ_App_HocVien_MaDK` once the multi-source strategy was confirmed.

## Current Patch Draft Review

Patch under review:

```text
database/patches/20260627_prepare_hocvien_multisource_identity_unique_key.sql
```

Current behavior:

1. Uses explicit TEST database context:
   ```sql
   USE [QLHV_APP_TEST];
   GO
   ```
2. Sets required SQL Server SET options, including `QUOTED_IDENTIFIER ON`.
3. Starts a transaction with `XACT_ABORT ON`.
4. Verifies `dbo.App_HocVien` exists.
5. Verifies source identity columns exist:
   - `SourceProfileCode`
   - `SourceMaDK`
   - `SourceSystem`
   - `SourceVersion`
6. Blocks if active rows are missing `SourceProfileCode` or `SourceMaDK`.
7. Blocks if active rows already contain duplicate `SourceProfileCode + SourceMaDK`.
8. Creates source-scoped unique filtered index if missing:
   ```sql
   UX_App_HocVien_SourceProfileCode_SourceMaDK_Active
   ON dbo.App_HocVien(SourceProfileCode, SourceMaDK)
   WHERE IsDeleted = 0
     AND SourceProfileCode IS NOT NULL
     AND SourceMaDK IS NOT NULL
   ```
9. Detects `UQ_App_HocVien_MaDK`.
10. Fails closed unless this variable is manually changed:
    ```sql
    DECLARE @AllowDropMaDkUniqueConstraint bit = 0;
    ```
11. Drops `UQ_App_HocVien_MaDK` only when the variable is changed to `1`.

This is the right default posture. Running the script unchanged should fail safely if `UQ_App_HocVien_MaDK` still exists.

## Recommended DB Strategy for QLHV_APP_TEST

### Keep

Keep non-unique MaDK lookup support:

```text
IX_App_HocVien_MaDK
```

The UI and search can still use `MaDK`, but it must not enforce global uniqueness across sources.

### Add/ensure

Use a unique filtered source identity index:

```text
UX_App_HocVien_SourceProfileCode_SourceMaDK_Active
```

This is sufficient for sync identity because B3W14-B3W16 target MERGE now matches by:

```sql
ON tgt.SourceProfileCode = src.SourceProfileCode
AND tgt.SourceMaDK = src.SourceMaDK
```

It prevents duplicate active rows within one source, while allowing the same `MaDK` to exist once for `DATA_V1` and once for `DATA_V2`.

### Drop

Drop this constraint only after manual approval:

```text
UQ_App_HocVien_MaDK
```

Reason: it blocks valid multi-source data when `DATA_V1` and `DATA_V2` have overlapping `MaDK`.

### Do not change yet

Do not force `SourceProfileCode`/`SourceMaDK` to `NOT NULL` in this phase.

Reason: existing local/test data is backfilled, but future import/error recovery strategy still benefits from conservative nullable schema until full migration is proven.

## Exact Manual SQL Run Plan for TEST Only

Do not run this plan on production.

1. Confirm the API app is stopped or no sync/import operation is running.
2. Confirm `EnableTargetWrites=false`.
3. Take a backup/snapshot of `QLHV_APP_TEST`.
4. Run read-only checks manually in SQL Server against `QLHV_APP_TEST`:

```sql
SELECT COUNT(1) AS TotalRows
FROM dbo.App_HocVien;

SELECT SourceProfileCode, COUNT(1) AS Total
FROM dbo.App_HocVien
GROUP BY SourceProfileCode
ORDER BY SourceProfileCode;

SELECT COUNT(1) AS MissingSourceIdentity
FROM dbo.App_HocVien
WHERE IsDeleted = 0
  AND (
      SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N''
      OR SourceMaDK IS NULL OR LTRIM(RTRIM(SourceMaDK)) = N''
  );

SELECT SourceProfileCode, SourceMaDK, COUNT(1) AS Total
FROM dbo.App_HocVien
WHERE IsDeleted = 0
GROUP BY SourceProfileCode, SourceMaDK
HAVING COUNT(1) > 1;

SELECT name, type_desc
FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name = N'UQ_App_HocVien_MaDK';

SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name IN (
      N'IX_App_HocVien_MaDK',
      N'IX_App_HocVien_SourceProfileCode_SourceMaDK',
      N'UX_App_HocVien_SourceProfileCode_SourceMaDK_Active'
  );
```

5. If checks pass, run the patch unchanged first as a safety rehearsal:

```powershell
sqlcmd -S <TEST_SQL_SERVER> -d QLHV_APP_TEST -E -b -i database\patches\20260627_prepare_hocvien_multisource_identity_unique_key.sql
```

Expected result while `@AllowDropMaDkUniqueConstraint = 0`:

```text
THROW 51204
Manual review required: UQ_App_HocVien_MaDK still exists.
```

No change should remain because the transaction rolls back.

6. After the rehearsal confirms the guard works, edit only this local TEST copy of the script:

```sql
DECLARE @AllowDropMaDkUniqueConstraint bit = 1;
```

7. Run the edited script on `QLHV_APP_TEST` only.

8. Re-run read-only verification:

```sql
SELECT name, type_desc
FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name = N'UQ_App_HocVien_MaDK';

SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name = N'UX_App_HocVien_SourceProfileCode_SourceMaDK_Active';
```

Expected TEST result:

- `UQ_App_HocVien_MaDK` no longer exists.
- `UX_App_HocVien_SourceProfileCode_SourceMaDK_Active` exists and is unique.
- `IX_App_HocVien_MaDK` remains for lookup/search.

9. Only after this, rerun read-only target diagnostics and pre-execute planning. Do not call `/execute` yet.

## Rollback Consideration

Preferred rollback:

- restore the `QLHV_APP_TEST` backup/snapshot taken before running the patch.

Reason: once multi-source imports insert duplicate `MaDK` values across `DATA_V1` and `DATA_V2`, recreating `UQ_App_HocVien_MaDK` may fail.

If no duplicate `MaDK` exists after testing, a manual rollback can recreate the old constraint:

```sql
ALTER TABLE dbo.App_HocVien
ADD CONSTRAINT UQ_App_HocVien_MaDK UNIQUE (MaDK);
```

But this is only valid if:

```sql
SELECT MaDK, COUNT(1) AS Total
FROM dbo.App_HocVien
GROUP BY MaDK
HAVING COUNT(1) > 1;
```

returns no rows.

If duplicate `MaDK` values exist, do not recreate the old unique constraint. Restore from backup or manually resolve test data first.

The new unique filtered index can be dropped if needed:

```sql
DROP INDEX UX_App_HocVien_SourceProfileCode_SourceMaDK_Active
ON dbo.App_HocVien;
```

Only do this on TEST after confirming no sync/import is running.

## Recommendation Before Execute/Sync

Before any execute/sync attempt:

1. Apply the constraint patch on `QLHV_APP_TEST` only.
2. Confirm source identity columns are complete.
3. Confirm source-scoped unique index exists.
4. Confirm MaDK-only unique constraint is gone.
5. Run read-only diagnostics.
6. Run source-scoped pre-execute plan.
7. Keep `EnableTargetWrites=false` until the plan is reviewed.

