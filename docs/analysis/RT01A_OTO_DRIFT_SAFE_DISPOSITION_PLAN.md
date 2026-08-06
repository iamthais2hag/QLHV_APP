# RT-01A OTO safe disposition plan

## Disposition summary

| Candidate | Classification | Safe disposition | Apply now |
|---|---|---|---|
| `WOULD_INSERT` | `SOURCE_ONLY_NEW_ROW` | `WOULD_INSERT_SAFE_AFTER_APPROVAL` | No |
| `WOULD_UPDATE` | `STALE_IMPORTED_VALUE` in source-owned `HoTen` | `WOULD_UPDATE_SOURCE_OWNED_FIELDS_AFTER_APPROVAL` | No |
| `TARGET_ONLY_ACTIVE` | `SOURCE_ROW_REMOVED`, Existing Auto Sync-owned | `MANUAL_REVIEW_REQUIRED` and retain active | No |

The candidates are fully identified and their ownership is resolved. The
target-only row is deliberately not interpreted as permission to delete,
soft-delete, or deactivate.

## Candidate gates

### Source-only new row

Before any future apply:

1. obtain explicit business-write approval
2. re-run a stable read-only comparison
3. require the same source-only classification
4. require no active, soft-deleted, alias, cross-profile, or rekey counterpart
5. use the existing canonical import mapper and transaction guards

Proposed disposition:
`WOULD_INSERT_SAFE_AFTER_APPROVAL`.

### Source-owned mapped-field update

Before any future apply:

1. obtain explicit business-write approval
2. re-run the comparison and field HMAC proof
3. require only the source-owned `HoTen` field to differ
4. preserve every QLHV-owned field and all unrelated audit/workflow state
5. reject the update if it becomes normalized-equal or mapping parity fails

Proposed disposition:
`WOULD_UPDATE_SOURCE_OWNED_FIELDS_AFTER_APPROVAL`.

### Target-only active row

The row was imported by Existing Auto Sync but no longer exists in either
current OTO learner source table. There is no alias, rekey, profile mismatch,
soft-deleted counterpart, or source-scope representation.

Required handling:

1. retain the row active
2. do not include it in any automatic absence/delete plan
3. obtain operator/domain confirmation about why the source representation was
   removed
4. require a separately reviewed ownership/lifecycle policy before any future
   state change

Proposed disposition: `MANUAL_REVIEW_REQUIRED`.

## RT-01/RT-02 boundary

This task did not start RT-02. A future shadow-only phase may be considered
under separate approval, but no production apply/cutover may treat the
target-only row as source-deleted. Business apply remains gated by the
candidate-specific approvals above.

No mapping fix or RT-01 identity matcher fix is required for the observed
production drift. The safe diagnostics contract may continue to report:

- matched identities: 151
- no-change: 150
- would-insert: 1
- would-update: 1
- target-only active: 1
- conflict: 0
- manual review: 1
- mapping contract: `PASS`
- consistency: `BEST_EFFORT_READ_ONLY_STABLE_SAMPLE`

Only counts, reason codes, booleans, field categories, and
`RT01A-HMAC-SHA256-v1` values are emitted.

## Explicit non-actions

- `BusinessDataWrites = 0`
- apply checkpoint not published
- no insert or update
- no delete, soft-delete, or deactivation
- no Auto Sync run
- no polling enablement
- no SQL write, patch, or migration
- no backup/restore
- no CT or Snapshot enablement
- no production RT-01 worker
- no RT-02
- no V2-to-V1 changes
- no stage, commit, merge, or push
