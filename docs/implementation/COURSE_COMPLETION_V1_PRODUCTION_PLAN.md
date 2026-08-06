# Course Completion V1 — Production Plan

Production deployment is not authorized by the implementation task. Use only the sealed artifact runbook after explicit operator approval.

## Ordered phases

1. Verify artifact manifest and supersession/production authority.
2. Capture fresh read-only baseline: API, Worker/Master, writers, checkpoints/markers, V9 reviews, source database identities/schema, and absence of partial V1 schema.
3. Create fresh QLHV database and API runtime backups; hash both.
4. Stop only the API process using the already-approved hosting procedure. Do not stop Worker; Master must remain OFF/REALTIME_OFF.
5. Apply the exact additive migration with the explicit `QLHV_APP` SQLCMD variable; execute exact verifier.
6. Resolve the real API SQL principal. Bind only the permissions in the sealed matrix and verify effective source access is SELECT-only and QLHV access is append/read-only for the approved objects.
7. Deploy only manifest-listed API/client payload; start one API process/listener.
8. Verify HTTP 200/contract 2.0, status and preview with read-only test data, and that no completion marker exists before an explicitly authorized business confirm.
9. Run final non-interference audit; keep Master OFF and Worker REALTIME_OFF.

## Stop conditions

Stop before mutation if a hash, target database, route GUID/MaCSDT, schema, principal, permission, runtime path, checkpoint/marker, V9 review, Master/Worker state, writer count, or backup differs from the runbook contract. Partial Course Completion schema is not repaired in place.

Stop after migration and roll back only if all three V1 tables are empty. If any completion data exists, rollback must fail closed and the schema must remain for investigation.

## Rollback boundary

- Before first completion: stop API, restore prior runtime, run the sealed rollback (which verifies empty tables), verify role/tables removed, restart prior API, and audit production.
- After any completion: do not run schema rollback, delete marker, or reopen. Preserve evidence and use a separate approved correction/recovery procedure.

## Explicit exclusions

Do not enable Master Realtime, call run-once/integrity scan, stop/start Worker, change checkpoint/reviews, run full convergence, mutate V2/V1, change V7/V8/V9, alter W32Time/NTP, touch Báo cáo I/XML, or stage/commit/push.
