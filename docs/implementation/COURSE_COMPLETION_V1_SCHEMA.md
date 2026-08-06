# Course Completion V1 — Schema

## Target database

All new objects belong only to the SQLCMD-selected QLHV database. Production deployment must pass `CourseCompletionTargetDatabase=QLHV_APP`; the scripts have no implicit database default.

## `dbo.App_CourseCompletion`

Immutable marker, one row per exact `(SourceProfileCode, SourceCourseKey)` and one row per `KhoaHocId`. It stores contract/status, user-selected business date, canonical snapshot hash/count, actor/reason, operation identity, SQL UTC completion time, and rowversion. Status is constrained to `COMPLETED`, contract to `1.0`, profile to OTO/MOTO, and hash to uppercase SHA-256. The `App_KhoaHoc` foreign key is `NO ACTION`.

## `dbo.App_CourseCompletionLearnerSnapshot`

Immutable protected learner snapshot. One row per `(CourseCompletionId, ProtectedLearnerIdentity)`. It stores no learner PII and no raw registration code: only protected identity, course/profile identity, TT_XuLy, learner/result/downstream classifications, canonical row hash, SQL UTC snapshot time, and rowversion. The marker foreign key is `NO ACTION`.

## `dbo.App_CourseCompletionOperation`

Durable idempotency ledger. It stores operation id, course/profile identity, actor, binary SHA-256 of the idempotency key, request and preview fingerprints, result, marker reference, SQL UTC created/completed times, error code, and rowversion. The raw idempotency key is never stored. Uniqueness is `(profile, course key, actor, idempotency hash)`; result is restricted to `COMPLETED` or `NO_CHANGE`.

## Audit

V1 reuses `dbo.App_AuditLog`. Confirm writes before/after evidence in the same transaction as the marker, snapshots, and ledger. `CreatedAt` is assigned from the same SQL `SYSUTCDATETIME()` value. No client timestamp is accepted.

## Migration properties

- Additive and transactional.
- First apply creates all three tables; partial pre-existing schema fails with `COURSE_COMPLETION_V1_PARTIAL_SCHEMA_DRIFT`.
- Second apply verifies exact columns, required constraints, no-cascade foreign keys, and least-privilege role permissions.
- Rollback refuses to run if any V1 table contains data.
- Empty rollback removes only the three V1 tables and the dedicated role.
- Realtime, assignment, source, review, report, and XML objects are not referenced.

## Permission matrix

| Database/object | Permission | Purpose |
|---|---:|---|
| QLHV / three V1 tables | SELECT, INSERT | Read immutable history and append marker/snapshot/ledger |
| QLHV / `App_KhoaHoc` | SELECT | Resolve exact QLHV course identity |
| QLHV / `App_AuditLog` | INSERT | Append atomic audit |
| V2 / `KhoaHoc`, `NguoiLX_HoSo`, `BaoCaoI`, `KhoaHoc_GiaoVien`, `KhoaHoc_XeTap` | SELECT only | Preview/revalidation and warning diagnostics |
| V2 / `sys.database_recovery_status` | SELECT via public metadata visibility | Validate database identity |
| V1 / `KhoaHoc`, `NguoiLX_HoSo`, `NguoiLX_GPLX` | SELECT only | Downstream classification |
| Source V2/V1 | INSERT, UPDATE, DELETE, EXECUTE | Not granted by this feature |
| QLHV V1 tables | UPDATE, DELETE | Not granted by the dedicated role |

The forward migration creates `qlhv_course_completion_api` but intentionally does not guess or bind a production principal. Principal resolution and exact effective-permission verification are mandatory deployment gates.
