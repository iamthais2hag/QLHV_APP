# Task 5 B3W18A - HocVien DB Patch Preflight/Postcheck Runbook

Date: 2026-06-28

Scope: TEST-only SQL runbook for reviewing and applying the HocVien multi-source constraint patch. This document does not approve production use.

Do not run these scripts on production. Do not run sync. Do not call `/execute`. Do not enable `EnableTargetWrites`.

## Patch File To Review

Patch draft:

```text
database/patches/20260627_prepare_hocvien_multisource_identity_unique_key.sql
```

The patch is expected to:

- use `QLHV_APP_TEST`;
- require source identity columns to exist;
- require active rows to have `SourceProfileCode` and `SourceMaDK`;
- block duplicate active `SourceProfileCode + SourceMaDK`;
- create `UX_App_HocVien_SourceProfileCode_SourceMaDK_Active` if missing;
- fail closed while `@AllowDropMaDkUniqueConstraint = 0`;
- drop `UQ_App_HocVien_MaDK` only if a human changes `@AllowDropMaDkUniqueConstraint = 1` after review.

## Expected Current State Before Patch

Expected state for the current TEST database after prior B3W backfill:

- `dbo.App_HocVien` exists.
- Total rows: `1970`.
- `SourceProfileCode` distribution: `DATA_V2 = 1970`.
- Missing active `SourceProfileCode` / `SourceMaDK`: `0`.
- Duplicate active `SourceProfileCode + SourceMaDK`: `0`.
- `UQ_App_HocVien_MaDK` exists before the patch.
- `UX_App_HocVien_SourceProfileCode_SourceMaDK_Active` may not exist yet before the patch.
- `IX_App_HocVien_MaDK` should remain as a non-unique lookup index.

Stop if the actual state differs unless the difference is reviewed and accepted.

## Precheck SQL - QLHV_APP_TEST Only

Run this as read-only preflight before touching the patch.

```sql
USE [QLHV_APP_TEST];
GO

SELECT
    DB_NAME() AS CurrentDatabase,
    OBJECT_ID(N'dbo.App_HocVien', N'U') AS AppHocVienObjectId;
GO

SELECT COUNT(1) AS TotalRows
FROM dbo.App_HocVien;
GO

SELECT
    COALESCE(NULLIF(LTRIM(RTRIM(SourceProfileCode)), N''), N'<NULL_OR_EMPTY>') AS SourceProfileCode,
    COUNT(1) AS Total
FROM dbo.App_HocVien
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(SourceProfileCode)), N''), N'<NULL_OR_EMPTY>')
ORDER BY SourceProfileCode;
GO

SELECT COUNT(1) AS MissingActiveSourceIdentity
FROM dbo.App_HocVien
WHERE IsDeleted = 0
  AND (
      SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N''
      OR SourceMaDK IS NULL OR LTRIM(RTRIM(SourceMaDK)) = N''
  );
GO

SELECT
    SourceProfileCode,
    SourceMaDK,
    COUNT(1) AS Total
FROM dbo.App_HocVien
WHERE IsDeleted = 0
GROUP BY SourceProfileCode, SourceMaDK
HAVING COUNT(1) > 1
ORDER BY Total DESC, SourceProfileCode, SourceMaDK;
GO

SELECT
    name,
    type_desc
FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name = N'UQ_App_HocVien_MaDK';
GO

SELECT
    name,
    is_unique,
    has_filter,
    filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name IN (
      N'IX_App_HocVien_MaDK',
      N'IX_App_HocVien_SourceProfileCode_MaDK',
      N'IX_App_HocVien_SourceProfileCode_SourceMaDK',
      N'UX_App_HocVien_SourceProfileCode_SourceMaDK_Active'
  )
ORDER BY name;
GO

SELECT
    MaDK,
    COUNT(1) AS Total
FROM dbo.App_HocVien
WHERE IsDeleted = 0
GROUP BY MaDK
HAVING COUNT(1) > 1
ORDER BY Total DESC, MaDK;
GO
```

## Failure/Stop Conditions Before Patch

Stop before running the patch if any condition is true:

- Current database is not `QLHV_APP_TEST`.
- `dbo.App_HocVien` is missing.
- Total row count is not the expected TEST count, unless reviewed.
- Any active row has missing `SourceProfileCode` or `SourceMaDK`.
- Duplicate active `SourceProfileCode + SourceMaDK` rows exist.
- `UQ_App_HocVien_MaDK` is missing before the first rehearsal, unless someone already applied the patch and documented it.
- The backup/snapshot has not been taken.
- Any app/sync process may write to `App_HocVien` during the patch window.

## Patch Rehearsal - Guard Must Fail Closed

First run the patch unchanged with:

```sql
DECLARE @AllowDropMaDkUniqueConstraint bit = 0;
```

Expected result:

```text
THROW 51204
Manual review required: UQ_App_HocVien_MaDK still exists.
```

This confirms the script will not drop `UQ_App_HocVien_MaDK` accidentally.

Because the script runs inside a transaction and throws before commit, no change should remain.

## Manual TEST-Only Patch Run

Only after review, backup, and successful fail-closed rehearsal:

1. Make a TEST-only local copy or temporary edit.
2. Change:

```sql
DECLARE @AllowDropMaDkUniqueConstraint bit = 1;
```

3. Run only against `QLHV_APP_TEST`.
4. Do not run production.
5. Do not run sync.
6. Do not call `/execute`.
7. Do not enable `EnableTargetWrites`.

## Postcheck SQL - QLHV_APP_TEST Only

Run after the approved TEST-only patch.

```sql
USE [QLHV_APP_TEST];
GO

SELECT COUNT(1) AS TotalRows
FROM dbo.App_HocVien;
GO

SELECT
    COALESCE(NULLIF(LTRIM(RTRIM(SourceProfileCode)), N''), N'<NULL_OR_EMPTY>') AS SourceProfileCode,
    COUNT(1) AS Total
FROM dbo.App_HocVien
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(SourceProfileCode)), N''), N'<NULL_OR_EMPTY>')
ORDER BY SourceProfileCode;
GO

SELECT COUNT(1) AS MissingActiveSourceIdentity
FROM dbo.App_HocVien
WHERE IsDeleted = 0
  AND (
      SourceProfileCode IS NULL OR LTRIM(RTRIM(SourceProfileCode)) = N''
      OR SourceMaDK IS NULL OR LTRIM(RTRIM(SourceMaDK)) = N''
  );
GO

SELECT
    SourceProfileCode,
    SourceMaDK,
    COUNT(1) AS Total
FROM dbo.App_HocVien
WHERE IsDeleted = 0
GROUP BY SourceProfileCode, SourceMaDK
HAVING COUNT(1) > 1
ORDER BY Total DESC, SourceProfileCode, SourceMaDK;
GO

SELECT
    name,
    type_desc
FROM sys.key_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name = N'UQ_App_HocVien_MaDK';
GO

SELECT
    name,
    is_unique,
    has_filter,
    filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.App_HocVien')
  AND name IN (
      N'IX_App_HocVien_MaDK',
      N'IX_App_HocVien_SourceProfileCode_MaDK',
      N'IX_App_HocVien_SourceProfileCode_SourceMaDK',
      N'UX_App_HocVien_SourceProfileCode_SourceMaDK_Active'
  )
ORDER BY name;
GO
```

Expected postcheck:

- Total rows unchanged.
- `DATA_V2 = 1970` if the current TEST state is unchanged.
- Missing active source identity: `0`.
- Duplicate active `SourceProfileCode + SourceMaDK`: no rows.
- `UQ_App_HocVien_MaDK`: no rows returned.
- `UX_App_HocVien_SourceProfileCode_SourceMaDK_Active`: exists, unique, filtered.
- `IX_App_HocVien_MaDK`: still exists for lookup/search.

## Failure/Stop Conditions After Patch

Stop all next steps and do not run sync if any condition is true:

- Total row count changes unexpectedly.
- Any active row is missing `SourceProfileCode` or `SourceMaDK`.
- Duplicate active `SourceProfileCode + SourceMaDK` exists.
- `UQ_App_HocVien_MaDK` still exists after approved patch run.
- `UX_App_HocVien_SourceProfileCode_SourceMaDK_Active` does not exist.
- Any unexpected error occurs during patch execution.

## Rollback Consideration

Preferred rollback is restoring the TEST backup/snapshot taken before the patch.

Manual rollback is only possible if no duplicate `MaDK` values exist:

```sql
USE [QLHV_APP_TEST];
GO

SELECT
    MaDK,
    COUNT(1) AS Total
FROM dbo.App_HocVien
WHERE IsDeleted = 0
GROUP BY MaDK
HAVING COUNT(1) > 1;
GO
```

If this returns no rows, the old constraint can be recreated manually on TEST:

```sql
USE [QLHV_APP_TEST];
GO

ALTER TABLE dbo.App_HocVien
ADD CONSTRAINT UQ_App_HocVien_MaDK UNIQUE (MaDK);
GO
```

If duplicate `MaDK` values exist, do not recreate `UQ_App_HocVien_MaDK`. Restore backup/snapshot or manually resolve TEST data first.

The new source-scoped unique index can be removed on TEST only if needed:

```sql
USE [QLHV_APP_TEST];
GO

DROP INDEX UX_App_HocVien_SourceProfileCode_SourceMaDK_Active
ON dbo.App_HocVien;
GO
```

Do not perform rollback while the app or any sync process is writing.

