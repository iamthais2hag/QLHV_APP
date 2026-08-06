# V1 deletion exclusion design

Audit date: `2026-07-26`  
Status: design only; not implemented.

## 1. Required outcome

The SQL audit rejects every existing-column candidate as a universal source-deletion predicate. The required model is:

```text
physical V1 row/history remains unchanged
        +
stream-scoped source membership says active/inactive
        +
only NEW_BUSINESS selectors and final mutations apply the active-membership predicate
```

This design resolves the H12 semantics. It does not enable Change Tracking or snapshot isolation and does not replace the separate H14/H15 approvals.

The audited enforcement manifest contains `75` paths: `63` transactional new-business branches and `12` unit/course master-control hazards. Every path is currently unprotected by source membership.

## 2. Logical registry contract

One protected current-state registry row exists for every source key ever admitted to the stream.

| Field | Required meaning and rule |
| --- | --- |
| `TargetProfile/TargetDatabase` | Immutable target identity. It separates OTO and MOTO registries even when their mapping fingerprints are equal. |
| `SourceProfile` | Immutable route identity, for example the OTO or MOTO V2→V1 profile. Never infer it from the target database alone. |
| `Stream/MaCSDT` | Immutable ownership partition captured while the source row exists. It is mandatory because a delete tombstone may contain only the PK. |
| `TableName` | Allowlisted canonical mapped-table name; no arbitrary identifier input. |
| `KeySchemaVersion` | Version of the versioned, type-tagged, length-prefixed key encoding. |
| `MembershipId` | Stable internal surrogate for one logical route membership. It is not derived from, and does not rotate with, the diagnostic HMAC. |
| `CanonicalBusinessKey` | Exact protected key tuple. Preserve type, bytes, case, collation-significant spaces and component order; do not trim, case-fold, or regenerate. Operational use only. |
| `TargetEqualityKey` | Protected non-rotating ownership key, independent of source stream and normalized to exactly the target column types, collations and trailing-space equality semantics. It must collapse every pair of values SQL Server would treat as the same target key. It is not a public diagnostic. |
| `CanonicalBusinessKeyHash` | Context-bound keyed `HMAC-SHA-256` over target/source profiles, stream, table, key schema and canonical key bytes, with `HashKeyVersion`. This is the only key identity allowed on diagnostics/API/UI/log surfaces. |
| `IsActive` | Current applied convenience projection: `1` only for `ACTIVE/APPLIED`; it is not sufficient by itself for crash recovery. |
| `ClaimsTargetKey` | `1` for `INSERT_PENDING`, `REACTIVATE_PENDING`, and `ACTIVE`; used to serialize ownership before target insert/reactivation, not only after activation. |
| `OwnershipReserved` | `1` from the first accepted claim through active and inactive retention. A source delete does not release ownership; only a separately approved transfer/purge proof may set it to zero. |
| `MembershipStatus` | At least `INSERT_PENDING`, `ACTIVE`, `DELETE_PENDING`, `INACTIVE`, `REACTIVATE_PENDING`, and `CONFLICT`; equivalently store separate desired/applied states. |
| `TargetAction` | Allowlisted applied outcome, including `UPSERTED`, `HARD_DELETED`, `PRESERVED_EXCLUDED`, or `NONE`. It is distinct from the reason. |
| `LastObservedSourceVersion` / `AppliedSourceVersion` | Monotonic source evidence and the version whose target action has committed. |
| `DeletedAtSourceVersion` | Current-state field: non-null only while `INACTIVE`; it is set by the applied deletion and deterministically cleared by a later applied reactivation. The append-only journal retains every prior delete version. |
| `ReactivatedAtSourceVersion` | Source version/watermark at which the latest reactivation was applied. |
| `FirstSeenCycle` | First accepted cycle for the key; immutable. |
| `LastSeenCycle` | Latest cycle that proved presence or processed deletion/reactivation. Monotonic under the profile checkpoint. |
| `LastAppliedCycle` | Latest cycle whose membership transition and target action committed. |
| `Reason` | Allowlisted cause such as `SOURCE_DELETE`, `FULL_RECONCILE_ABSENT`, `REACTIVATED_AT_SOURCE`, or `BLOCK_DELETE_CONFLICT`. No free-text PII. |
| `MappingFingerprint` | Fingerprint of the table/column mapping used by the deciding cycle. A mismatch blocks mutation until remapped. |
| `RouteFingerprint/OwnershipEpoch` | Pins the routing/ownership rules used to claim the target key. A mismatch or epoch transfer requires explicit reconciliation. |
| `CreatedAtUtc` / `UpdatedAtUtc` / `DeactivatedAtUtc` / `ReactivatedAtUtc` | Operational timestamps; source ordering still uses source version, not wall clock. |
| `RowVersion` | Target concurrency token. |

An append-only transition journal is mandatory. It records the before/after state, deciding source version, cycle, reason, target action and fingerprints so reactivation cannot erase the deletion timeline.

### Canonical keys by table

| Table | Canonical operational key | Stream source |
| --- | --- | --- |
| `DM_DonViGTVT` | `MaDV` | routed `MaCSDT/MaDV`; never target-global |
| `KhoaHoc` | `MaKH` | source row `MaCSDT` |
| `BaoCaoI` | `MaBCI` | source row `MaCSDT` |
| `NguoiLX` | `MaDK` | durable source membership captured from intake/parent scope |
| `NguoiLX_HoSo` | `MaDK` | source row `MaCSDT` |
| `NguoiLXHS_GiayTo` | ordered tuple `(MaGT, MaDK)` | parent dossier membership |

The operational key is required for exact matching and collision verification. It must be accessible only to signed/approved database modules and the sync principal, protected at rest/backups, and excluded from generic telemetry. Public diagnostics receive the context-bound keyed hash only. `CanonicalBusinessKeyHash` is versioned diagnostics, never route identity or a unique key. The trusted path verifies the exact protected key and the target-equality key and fails closed on a token collision or target-collation alias.

## 3. Uniqueness and indexing

Required logical uniqueness:

```text
(TargetProfile, SourceProfile, Stream/MaCSDT, TableName,
 KeySchemaVersion, CanonicalBusinessKey)
```

All six audited key shapes fit within ordinary SQL Server index limits; the protected exact canonical key, or an equivalent stable internal `MembershipId` mapping with a unique exact-key constraint, must therefore define route identity. HMAC rotation cannot create a second membership.

In addition, a filtered owner rule must allow at most one stream to reserve a target key, including while it is inactive:

```text
UNIQUE (TargetProfile, TableName, KeySchemaVersion, TargetEqualityKey)
WHERE OwnershipReserved = 1
```

`ClaimsTargetKey` still identifies insert/reactivation/active transitions, but it cannot release the uniqueness reservation. `TargetEqualityKey` is valid only after per-table tests prove equivalence with the actual target PK's data type, collation, case/accent and trailing-space behavior. If that equivalence cannot be proved, use a typed ownership-claim relation with the actual target key types/collations and its own unique constraint. Every first claim also takes the approved key-range/application lock and scans both active and inactive reservations plus target equality in the target transaction. A mismatch, alias or different-stream reservation fails closed as `STREAM_OWNERSHIP_CONFLICT`.

The preferred NEW_BUSINESS lookup joins an indexed protected exact canonical key through a signed/approved active relation. A covering path begins with:

```text
TargetProfile, SourceProfile, Stream/MaCSDT, TableName,
IsActive, KeySchemaVersion, CanonicalBusinessKey
```

Include `AppliedSourceVersion`, `MembershipStatus`, `MappingFingerprint` and the target-equality key needed by the predicate. The diagnostic HMAC must not double as the SQL lookup key or unique identity. Index design and plans must be tested at production cardinality; no scalar UDF may be called once per candidate row.

## 4. State transitions

| Current applied state | Accepted source evidence | Desired/intermediate state | Committed state and target action |
| --- | --- | --- | --- |
| absent | source row present | `INSERT_PENDING`; claim target key first | `ACTIVE/APPLIED`; create membership and insert/upsert mapped row |
| `ACTIVE/APPLIED` | source row present | active | update `LastSeenCycle`; sync V2-owned columns; remain `ACTIVE/APPLIED` |
| `ACTIVE/APPLIED` | accepted delete or bounded full-reconcile absence | `DELETE_PENDING` | `INACTIVE/APPLIED`; hard-delete only after complete history proof, otherwise `PRESERVED_EXCLUDED` |
| `INACTIVE/APPLIED` | same/older delete replay | inactive | idempotent no-op except safe retry metadata |
| `INACTIVE/APPLIED` | exact key present at a newer accepted source version | `REACTIVATE_PENDING` | `ACTIVE/APPLIED`; upsert V2-owned columns and preserve V1 history/conditional merge |
| any | mapping/profile/key mismatch or ambiguous ownership | `CONFLICT` | fail closed; no target mutation or checkpoint advance |

Version ordering is mandatory. A late tombstone cannot deactivate a key that has already reappeared at a newer accepted source version. Registry state and the target hard-delete/retained-shell/upsert action commit in the same target-database transaction. If a separately hosted registry is ever proposed, it requires an owner-approved `PREPARED/APPLIED` commit protocol and the global checkpoint cannot advance before convergence.

### Bootstrap gate

Before any delete/deactivation path is enabled:

1. backfill active membership for every mapped key at one complete accepted baseline watermark;
2. persist a table/stream coverage-complete marker tied to that watermark and mapping fingerprint;
3. have the owner classify every target-only row inside mapped scope;
4. fail closed when an enabled mapped-scope key has no membership record;
5. resolve a tombstone to exactly one active owner, otherwise record `UNOWNED_DELETE_KEY`/conflict and block checkpoint advance.

A full reconcile compares one source snapshot only with the same stream's active registry membership. It never anti-joins a source stream against all target rows.

### Hard-delete proof gate

Automated hard delete for A–E is disabled until the owner approves a versioned `HistoryProbeManifest` for each table. Each manifest must name:

- every hard FK and soft/logical relation;
- the exact in-transaction probe/query identifier;
- the exact null/default/sentinel semantics for result, decision, exam, print/XML and GPLX signals;
- child ordering and the history-free target action;
- the schema/mapping fingerprint under which the proof is valid.

An unknown/new relation, unrecognized sentinel or probe-version mismatch always selects `PRESERVED_EXCLUDED`; it never falls through to hard delete. Case F, `DM_DonViGTVT`, is never hard-deleted automatically. T01 may enable its hard-delete assertion only after these manifests and sentinels are approved.

## 5. Applying the exclusion

### Options

| Option | Coverage | Omission risk | Performance/indexing | Legacy impact | Testability | History safety |
| --- | --- | --- | --- | --- | --- | --- |
| A. Add an explicit membership predicate to every NEW_BUSINESS query | Positive `EXISTS ACTIVE/APPLIED` covers complex branches precisely. A `NOT EXISTS inactive` form is valid only with a mandatory membership-existence and bootstrap-coverage gate. | High unless inventory and deployment lint stay complete | Good when the registry join is sargable and indexed | Many procedures change | Direct branch tests are clear | Good if added only to new branches |
| B. Canonical active views | Central per-table rule | Existing base-table references bypass until migrated/revoked | Usually optimizer-friendly; plans must be measured | Requires replacing base-table references and permission changes | Strong contract tests around each view | Excellent because separate history views/base tables remain available |
| C. Inline TVF/central predicate | Parameterized central semantics | Callers can omit it; per-row misuse can still harm plans | Acceptable only as an inline, set-based relation with verified plans | Moderate | Central unit tests plus caller tests | Good when history callers do not invoke it |
| D. Domain combination | Positive active views for simple candidate sets; direct indexed `EXISTS ACTIVE/APPLIED` for complex `MIXED` branches; shared registry semantics | Lowest when base access and static gates are enforced | Best fit per query; no scalar UDF | Controlled migration by domain | Highest: view, branch, permission and static-analysis tests | Best separation of new business from history |

### Recommendation: D

1. Define one canonical active relation per core domain backed by the registry.
2. Move simple candidate lists to those active relations.
3. Add a direct, indexed positive `EXISTS ACTIVE/APPLIED` membership semi-join inside each complex `MIXED` new-business branch. A pure “no inactive row” anti-join is forbidden because missing membership must fail closed.
4. Recheck membership with the target row/registry lock inside every final mapped intake/document, BCI/BCII approval, training/graduation, exam/result/decision, and GPLX issue/print/receive/return mutation; a filtered list is not a concurrency guard.
5. Require active ancestry: an inactive `KhoaHoc`, `BaoCaoI`, or `NguoiLX` makes the dependent dossier ineligible for new business even if the child membership itself is active.
6. Keep history branches on the original tables/history views without exclusion.
7. Deny direct base-table read/write to business execution roles where operationally feasible; grant only approved procedures/views. Signed modules or equivalent ownership chaining may expose the required projection.
8. Add a deployment gate that fails when a new or changed SQL module references a core table in a NEW_BUSINESS branch without the registry relation.
9. Re-run text/dependency/dynamic-SQL scans on every schema release. The current database contains no executable scoped dynamic SQL, but the gate must not assume that remains true.

The legacy V1 caller source is not present in this repository. Permission enforcement and database-side branch coverage are therefore part of the approval, not optional hardening.

Active membership is only a veto. It never makes a row eligible, overrides the existing workflow/date/scope predicates, or permits reuse of a completed lifecycle.

For `DM_DonViGTVT`, apply the positive membership requirement only inside the exact enabled source-profile/stream route that claims the unit. Target-native or other-stream national units outside that route are not globally filtered. Within an enabled mapped route, a missing registry row fails closed.

The 12 enumerated unit/course master-data paths may never create, clear, transfer or reactivate registry membership. A physical insert/update of a key reserved as inactive remains excluded; only a newer accepted source reappearance under the same owner may execute the reactivation transition. The same rule applies to any authorized identity/history correction through a parameter-driven `MIXED` writer.

## 6. Same-cycle and idempotency requirements

- Membership presence/absence is computed from one accepted source snapshot/watermark shared by all enabled mapped tables.
- A CT tombstone is scoped through durable membership captured before deletion; the target national table is never anti-joined globally.
- Registry mutation and target delete/retain outcome use one target transaction, one lock order, one cycle journal and one commit marker.
- Every final new-business mutation takes the approved lock and rechecks active membership at the point of write, closing selector/delete TOCTOU races.
- `BLOCK_DELETE_CONFLICT`, unknown consumers, stale mapping fingerprints, or missing membership scope leave the cycle incomplete and do not publish the global checkpoint.
- Retry uses the same cycle/version and is idempotent.
- Reactivation and a prior delete are ordered by accepted source version, not wall-clock arrival time.

## 7. Ownership and retention

- The registry is owned by the sync control plane, not V1 business users.
- OTO and MOTO have separate `SourceProfile`, stream membership, versions and checkpoints even though they reuse the mapping.
- Current membership rows are retained while the target row or any V1 history exists.
- Inactive membership continues reserving its target-equality key; a delete never makes that key available to another stream.
- Inactive records must outlive source change-retention, retry/replay windows and the V1 legal/audit retention of related history. Default is no purge until an owner-approved retention proof exists.
- Purge requires proof that no target business/history row, cycle journal entry, replay obligation or other stream ownership remains.
- Before production the owner must record legal duration, key/HMAC rotation and custody, backup retention and purge authority. Until then the safe default is no purge.
- Raw canonical keys are forbidden in logs, diagnostics, APIs and UI; expose table, reason, cycle/version, counts and keyed hash only.

## 8. Reactivation contract

An exact key that reappears at a newer accepted V2 version is reactivated only under the same target/source profile and stream owner:

1. lock the registry row, retained V1 shell and required parents in the canonical dependency order;
2. reject a different-stream/profile claim as `STREAM_OWNERSHIP_CONFLICT`; never transfer ownership automatically;
3. reactivate parents before children and upsert all V2-owned fields;
4. preserve all V1-owned BCII/exam/result/decision/GPLX data;
5. apply the existing special merge rules for `TT_XuLy`, `GhiChu`, `GiayCNSK`, and `GiaiTrinh`;
6. keep existing business eligibility predicates—active membership does not reopen a completed lifecycle;
7. match the retained shell by exact key and owner, creating no duplicate;
8. commit the target upsert and `ACTIVE/APPLIED` transition atomically;
9. treat an already active, up-to-date replay as a no-op;
10. preserve the prior deletion in the append-only journal and record `ReactivatedAtSourceVersion`.

## 9. Acceptance test matrix

| ID | Scenario | Required assertions |
| --- | --- | --- |
| T01 | V2 deletes a learner/dossier/documents with no V1 history | Explicit reverse-dependency probes allow child-first hard delete; registry becomes `INACTIVE/HARD_DELETED`; replay is a no-op and no new-business result contains the key |
| T02 | V2 deletes a dossier having `MaBC2` | Dossier/BCII history remains readable; every new BCII candidate branch and direct final BCII mutation rejects it |
| T03 | Dossier has `MaKySH`/`SoBD` | Old exam and candidate number remain readable; candidate list and direct final registration/assignment reject it; `TT_XuLy` is unchanged |
| T04 | Dossier has exam results or a decision signal, including a soft/non-FK signal | All result/remark/decision values and counts remain; history returns them; result/approval entry points reject new work for the inactive membership |
| T05 | `NguoiLX_GPLX` exists | GPLX and required shells remain; historical GPLX remains printable; new issue/print/receive/return workflow and final DML reject the inactive key |
| T06 | V2 removes A and adds B in one complete cycle | V1 active training/new-business membership contains only B; A history remains if required; replay is idempotent; checkpoint advances only after both registry and target actions commit |
| T07 | Deleted BCI is referenced by BCII | Historical BCII still joins to BCI; BCI cannot be selected for a new BCII and direct final BCII mutation rejects it |
| T08 | Deleted course has history | No cascade loss; course and descendants remain joinable; active-ancestor enforcement removes course and descendants from every new intake/BCI/BCII path |
| T09 | Exact inactive key reappears in V2 | Same target shell is reactivated; V2-owned columns resync; V1-owned history and all four special merges remain intact; no duplicate; duplicate reactivation is a no-op |
| T10 | One of the 63 transactional NEW_BUSINESS branches, including a final DML block, omits exclusion—or a master path releases one of the 12 reserved hazards | Static/deployment gate and integration test fail; an unknown or unsplit mixed branch also fails release |
| T11 | HISTORY_READ for inactive A–F row | Every enumerated historical detail/report/print/XML/decision/GPLX path remains unchanged except an authorized non-PII inactivity label; none is moved to an active-only relation |
| T12 | OTO/MOTO reuse | Same rule set/fingerprint passes while target/source profiles, memberships, versions, cycle journals and checkpoints remain isolated; an OTO delete cannot change MOTO state and vice versa |

Additional gates cover registry cardinality; target-collation alias/hash collision; mixed-branch plans; delete-versus-business-insert linearizability; retry after target commit; incomplete bootstrap; CT-retention/reconcile recovery; key rotation; raw-key/PII leakage; backup/purge controls; and permission attempts to bypass active views/procedures or read protected keys.
