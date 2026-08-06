# Course Completion V1 — Implementation

## Outcome

Course Completion V1 implements a QLHV-owned, immutable business marker with the meaning: “Đã chốt kết quả đào tạo của khóa tại thời điểm xác nhận.” It does not change course status, learner results, V1/V2 lifecycle data, reports, XML, exams, certificates, licences, realtime checkpoints, reviews, assignments, groups, teachers, vehicles, or manual overrides.

The user flow is `Khóa học → Chi tiết khóa học → Hoàn thành khóa học`. V1 has preview, confirm, and status/correction detection. It deliberately has no reopen or auto-correction operation.

## Components

- `CourseCompletionCanonicalSnapshotBuilder`: shared deterministic SHA-256 snapshot and learner classifier.
- `CourseCompletionPreviewStore`: opaque random, actor/course-bound, monotonic-expiry preview token.
- `CourseCompletionService`: input normalization, SQL clock write gate, preview/confirm/status orchestration.
- `SqlCourseCompletionRepository`: fixed OTO/MOTO routing, source SELECT-only reads, stable V2/V1 sampling, application lock, idempotency, and one QLHV transaction.
- `CourseCompletionController`: three policy-protected API routes.
- `CourseCompletionPanel`: seventh course-detail section, without bypass or reopen controls.

## Eligibility

- `09`: `PASSED`, READY only when the required training result is complete.
- `10`: `FAILED`, READY only when the required training result is complete.
- `11`–`19`: `DOWNSTREAM`, READY and strictly read-only.
- `01`–`08`, `90`, NULL, unknown, incomplete result, empty scope, duplicate identity, ambiguous identity: blocked.
- Missing Report I, teacher, vehicle, program, and incomplete course dates: warnings only.

The legacy result rule always requires learner/course identity, conclusion, start/end time, and a valid time range. For `A*`/`B1m`, optional result fields may remain NULL. Other classes require all approved result/score/time/distance fields to parse as numbers.

## Source safety

Only allowlisted routes are accepted:

- `CSDT_OTO`: `CSDL_OTO` and `CSDL_OTO_V1`, MaCSDT `66029`, production V2 database GUID from the sealed realtime route catalog.
- `CSDT_MOTO`: `CSDL_MOTO` and `CSDL_MOTO_V1`, MaCSDT `66030`, production V2 database GUID from the sealed realtime route catalog.

Every source connection validates its `Initial Catalog`; V2 validates database name and production GUID. A profile-specific connection is preferred. When production exposes only the existing `CSDT_V1`/`CSDT_V2` authority, the resolver accepts it only if its original catalog belongs to the fixed V1/V2 database-family allowlist, then selects the exact allowlisted OTO/MOTO catalog. No new secret is required. All source connection strings set `ApplicationIntent=ReadOnly`. The source SQL constants contain SELECT statements only. A stable scope is accepted only when two V2 samples and two V1 samples have identical deterministic raw fingerprints. A V1-only learner, duplicate V1 identity, wrong learner course key, or missing identity fails closed.

## Canonical snapshot

The snapshot includes source profile, course key, MaCSDT, MaSoGTVT, training class/form, and the full exact learner scope. Each learner row includes protected identity plus all approved status, result, timing, score, practice, distance, and downstream signals. Values are normalized without locale dependence; rows are sorted by protected identity and row hash; hashes use SHA-256. Learner count and Change Tracking version are not snapshot identity.

Only the protected learner identity and classification contract are persisted. Names, address, phone, photo, birth date, identity papers, and raw idempotency keys are not stored.

## Confirm transaction

Confirm requires an unexpired actor/course-bound preview, no blocker, a valid business date and reason, and SQL database clock write authorization. The repository opens one Serializable QLHV transaction, enables `XACT_ABORT`, and obtains an exclusive transaction-owned lock named `CourseCompletion:{profile}:{sourceCourseKey}`.

Under that lock it checks durable idempotency, resolves the exact QLHV course identity again, re-reads and rebuilds the source snapshot, and rejects any drift. It then either:

- returns durable replay for the same key/request;
- writes a `NO_CHANGE` operation/audit when an immutable marker already has the same snapshot;
- returns `CORRECTION_REQUIRED` without mutation when the marker differs; or
- inserts marker, all learner snapshots with one set-based `OPENJSON` statement, operation ledger, and audit, verifies exact count/hashes, then commits once.

Any exception before commit rolls back the entire QLHV transaction.

## Status and correction

GET returns `NOT_COMPLETED` without source mutation when no marker exists. For an existing marker it rebuilds the current snapshot. An exact match returns `COMPLETED`; any drift returns `CORRECTION_REQUIRED` with redacted added/missing/changed counts. No marker or snapshot is updated automatically.

## Authorization

- Admin: view, preview, complete.
- Employee: view only.
- Viewer: view only.
- All policies require authentication and `MustChangePassword=false`.
- Completion permissions are independent of course editing and assignment permissions.

## Non-interference

Implementation does not reference a source mutation procedure, Realtime run-once/integrity scan, Báo cáo I/XML workflow, checkpoint mutation, or reopen route. Production was audited read-only after validation and remained Worker Running, Master OFF/REALTIME_OFF, checkpoint OTO/MOTO `124/0`, two retained V9 reviews, writers `0/0`, and no Course Completion tables.
