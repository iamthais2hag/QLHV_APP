# Course Completion V1 — API

Base path: `/api/khoa-hoc/{courseId}/hoan-thanh`.

## GET `/api/khoa-hoc/{courseId}/hoan-thanh`

Policy: `Courses.ViewCompletionStatus`.

Returns `NOT_COMPLETED`, `COMPLETED`, or `CORRECTION_REQUIRED`. An existing marker includes business date, SQL UTC completion timestamp, actor, learner count, contract version, and snapshot hash. Correction diagnostics expose counts only, not learner identity or PII.

## POST `/api/khoa-hoc/{courseId}/hoan-thanh/preview`

Policy: `Courses.PreviewCompletion` (Admin only).

Optional body:

```json
{ "sourceProfileCode": "CSDT_OTO" }
```

The profile is only a constraint; the authoritative profile/course key is resolved from exact `App_KhoaHoc`. The response includes opaque preview token, expiry UTC, status, `canConfirm`, course/profile identity, snapshot hash, learner totals, blockers, and warnings. No mutation occurs.

Minimum reason codes: `READY`, `COURSE_NOT_FOUND`, `EMPTY_COURSE`, `STUDENT_STATUS_INVALID`, `STUDENT_RESULT_INCOMPLETE`, `DUPLICATE_IDENTITY`, `AMBIGUOUS_IDENTITY`, `CONFLICT`, `TIME_AUTHORITY_BLOCKED`, `BLOCKED`.

## POST `/api/khoa-hoc/{courseId}/hoan-thanh/confirm`

Policy: `Courses.Complete` (Admin only).

```json
{
  "previewToken": "opaque-server-token",
  "idempotencyKey": "client-generated-unique-value",
  "completionBusinessDate": "2026-08-01",
  "reason": "Đã kiểm tra và chốt kết quả đào tạo"
}
```

No learner result, lifecycle status, SQL field, or timestamp is accepted from the client. Reason is trimmed, required, and limited to 500 characters. The business date is business input, not an audit timestamp.

Responses:

- `COMPLETED`: a new immutable marker was committed and verified.
- durable replay: same operation id/result for the same idempotency key and request.
- `NO_CHANGE`: marker already exists with the same current snapshot; marker is untouched.
- HTTP 409 `CONFLICT`: token, identity, idempotency request, or source snapshot changed; zero mutation.
- HTTP 409 `CORRECTION_REQUIRED`: existing marker differs; zero mutation and correction workflow required.
- HTTP 503 `TIME_AUTHORITY_BLOCKED`: QLHV SQL clock/write authority unavailable.

Domain failures use RFC-style problem details with `code` and `traceId`. There is no reopen route.
