# RT-02A Insert, Update and Target-Only Safety Matrix

Status: **REVIEW CONTRACT; WRITER NOT PRODUCTION-REGISTERED**

| Candidate | Allowed disposition | Preconditions under source-profile lock | Mutation | Failure |
|---|---|---|---|---|
| Source-only new row | `WOULD_INSERT_SAFE_AFTER_APPROVAL` | Source hash matches staged plan; target key absent; no active or soft-deleted counterpart; no alias; no other profile owner; no duplicate identity; parent/reference validation passes | One active insert | Full cycle rollback |
| Stale imported value | `STALE_IMPORTED_VALUE` | Identity matches one active target; requested column list is exactly `HoTen`; old mapped hash, source hash and QLHV-owned hash match staged plan | Update `HoTen` and imported row hash only; affected rows must equal one | `TARGET_CHANGED_SINCE_SHADOW` or `SOURCE_CHANGED_SINCE_SHADOW`; full rollback |
| Source row removed / target-only | `MANUAL_REVIEW_REQUIRED` | Target remains active; disposition evidence hash belongs to cycle | Persist manual-review evidence; no learner mutation | Full cycle rollback if retention/evidence cannot be verified |

## Insert invariants

- A plain insert never reactivates a soft-deleted target.
- A target appearing after shadow returns `TARGET_CHANGED_SINCE_SHADOW`.
- An insert is never silently converted to update.
- Retry reads checkpoint/marker first, preventing a duplicate insert.

## Update allowlist

The only approved source-owned column in RT-02A is:

`HoTen`

The plan validator rejects an unknown field, multiple fields, `IsDeleted`,
manual notes, workflow state, photo state, internal IDs, audit ownership,
profile or ownership columns. The review-only SQL has a fixed parameterized
`UPDATE ... WHERE Id = @TargetId ... AND V2RowHash = @ExpectedMappedHash`.

Before update, the harness captures the QLHV-owned hash. After update, it
verifies the same hash and an affected row count of one.

## Target-only invariants

The retained row remains active. Evidence contains:

- cycle and operation IDs;
- diagnostic HMAC, never raw learner identity in logs;
- `MANUAL_REVIEW_REQUIRED`;
- disposition hash;
- `TargetRetainedActive=true`;
- `TargetMutated=false`.

The direct-realtime apply command set contains no:

- `DELETE FROM`;
- `TRUNCATE`;
- `MERGE`;
- `IsDeleted = ...`;
- deactivation;
- profile reassignment;
- ownership reassignment.

The checkpoint may advance past the retained candidate only after its evidence
and no-mutation state are part of the committed marker/disposition hash.

## Static P0 scan result

The focused RT-02A test suite scans all review-only apply commands for forbidden
delete, truncate, merge, soft-delete, profile/ownership reassignment, dynamic
execution and unbounded update patterns. The scan passed. SQL patch templates
are separately checked for exact identity guards, fixed tables, idempotence and
absence of business DML.
