# H12 owner approval checklist

Audit date: `2026-07-26`  
Decision: safe treatment of V2-deleted rows retained in `CSDL_OTO_V1`  
Status: proposed for owner approval; no implementation authorized by this document.

## 1. Evidence recorded by the read-only audit

The following are audit facts, not owner approvals:

- [x] All `492` SQL module definitions in `CSDL_OTO_V1` were read.
- [x] The relevant SQL universe contains `182` modules: `171` stored procedures and `11` scalar functions.
- [x] Dependency metadata, module text, comment-stripped dynamic-SQL search, object definitions, procedure/function call graph and readable SQL Agent steps were combined.
- [x] No encrypted definition, executable scoped dynamic SQL, relevant SQL Agent step, relevant view, TVF, trigger or synonym remained hidden from the database inventory.
- [x] Fifteen production application query-bearing/routing components in this repository were reviewed; this is the repository evidence boundary, not a claim that unavailable legacy caller source was inspected.
- [x] Five exact application branches address `CSDL_OTO_V1`: three `RUNTIME_ONLY` forward branches and two `ADMIN_MAINTENANCE` reverse branches.
- [x] No production repository call to the enumerated legacy business procedures was found; the legacy V1 business caller source is outside this repo.
- [x] Sixty-three exact transactional `NEW_BUSINESS` SQL branches were identified after selector and final-DML recount.
- [x] Twelve additional unit/course `ADMIN_MAINTENANCE` resurrection hazards were enumerated outside that denominator.
- [x] The matrix contains `59` exhaustive `HISTORY_READ` classification units under its declared rule: `56` dedicated HR modules plus `3` HR branches split from mixed modules.
- [x] Five exact `HISTORY_READ` blocks demonstrate history loss under status/stage reuse. They are the minimum counterexample subset of those 59 units.
- [x] No `UNKNOWN` remains in the SQL consumer classification units or in the five exact application branches.
- [x] Existing-predicate source-deletion coverage is `0/63 = 0%`.
- [x] All `63/63` transactional new-business branches lack a guaranteed V2-deletion predicate.
- [x] Combined H12 guard coverage is `0/75 = 0%`: `63` transactional branches plus `12` master/control-plane hazards.
- [x] The formal predicate result is `NO_UNIVERSAL_EXISTING_PREDICATE`.

Supporting artifacts:

- `V1_NEW_BUSINESS_CONSUMER_MATRIX.md`
- `V1_DEACTIVATION_PREDICATE_PROOF.md`
- `V1_HISTORY_PRESERVATION_MATRIX.md`
- `V1_DELETION_EXCLUSION_DESIGN.md`

## 2. H12 decision

Owner must select exactly one:

- [ ] **Approve `H12 EXCLUSION REGISTRY REQUIRED`.**
- [ ] Reject the proposal and return H12 for additional evidence, naming the missing consumer or disputed fact.

`H12 EXISTING-COLUMN DEACTIVATION PROVEN` is not an available approval choice under this evidence: a single unprotected new-business branch would reject it, and the audit found `63` transactional branches plus `12` additional unprotected control-plane hazards.

Approval of H12 accepts a design direction only. It does not authorize schema, procedure, code, configuration, Change Tracking, snapshot-isolation or data changes.

## 3. Membership and identity contract

Owner approval is required for every item:

- [ ] Registry is hosted in the target V1 transactional boundary, or an explicitly approved `PREPARED/APPLIED` protocol prevents checkpoint advance until target action and membership converge.
- [ ] `TargetProfile/TargetDatabase`, `SourceProfile`, `Stream/MaCSDT`, allowlisted `TableName`, `KeySchemaVersion`, stable `MembershipId`, protected exact canonical key, target-equality key and diagnostic hash are part of the identity contract.
- [ ] Canonical keys use a versioned, type-tagged, length-prefixed binary encoding that preserves exact type, bytes, case and spaces.
- [ ] The operational exact key is available only to the sync principal and signed/approved modules; business roles cannot select it.
- [ ] Diagnostics, APIs, UI, telemetry and ordinary errors expose only a keyed `HMAC-SHA-256` diagnostic hash with `HashKeyVersion`, never a raw key or plain low-entropy hash.
- [ ] Logical route uniqueness uses the protected exact canonical key or stable internal mapping, never the rotatable diagnostic HMAC.
- [ ] `OwnershipReserved=1` from first claim through inactive retention, and a filtered owner uniqueness rule prevents another stream from claiming the same target-profile/table/target-equal key in any pending, active or inactive state.
- [ ] Target-equality normalization is proven against the actual PK types, collations and trailing-space semantics, or a typed ownership-claim relation plus locked target-equality probe is used.
- [ ] A token collision or target-collation alias fails closed; it never overwrites an owner.
- [ ] `TableName`, `Reason`, action and state values are allowlisted; no identifier or SQL fragment is built from untrusted registry text.

## 4. State, bootstrap and transaction contract

- [ ] State distinguishes desired from applied membership, including at least `INSERT_PENDING`, `ACTIVE`, `DELETE_PENDING`, `INACTIVE`, `REACTIVATE_PENDING` and `CONFLICT`.
- [ ] `ClaimsTargetKey=1` while membership is insert-pending, reactivate-pending or active.
- [ ] A source delete never releases ownership; transfer or release requires a separate owner-approved proof/purge operation.
- [ ] The registry records source observed/applied versions, delete/reactivation versions, first/last seen and applied cycles, target action, mapping/route fingerprint, timestamps and row version.
- [ ] An append-only transition journal preserves delete/reactivation history even when current state changes.
- [ ] Initial baseline backfills complete active membership at one accepted watermark before the feature can be enabled.
- [ ] A bootstrap completeness/coverage marker is verified for every enabled mapped table and stream.
- [ ] Missing membership inside an enabled mapped scope fails closed; it is never interpreted as active.
- [ ] Target-only rows inside mapped scope receive an explicit owner classification before enablement.
- [ ] A CT tombstone is accepted only when its durable key resolves exactly one active stream owner.
- [ ] Full reconciliation compares the source snapshot with that stream's active membership, never with the whole national target table.
- [ ] Registry transition and target hard-delete/retained-shell outcome commit in the same target transaction.
- [ ] Business final DML locks and rechecks membership in that transaction so a selector/delete race is linearizable.
- [ ] A conflict, stale fingerprint, unowned delete key, incomplete bootstrap or unapplied target action blocks checkpoint advance.
- [ ] Duplicate/older delete and duplicate/up-to-date reactivation are idempotent no-ops.

## 5. Eligibility and history separation

- [ ] Active membership is a veto only: `NEW_BUSINESS = existing eligibility AND active membership`.
- [ ] Active membership never grants eligibility, resets a lifecycle or overrides `TrangThai`, `TT_XuLy`, date, scope or downstream rules.
- [ ] Active parent ancestry is required where applicable: inactive course, BCI or learner membership prevents new child business even if a child membership row is active.
- [ ] Simple selectors use approved typed active relations/views.
- [ ] Complex or `MIXED` new-business branches use a direct, indexed positive `EXISTS ACTIVE/APPLIED` membership semi-join.
- [ ] A pure “no inactive row” anti-join is forbidden; if an anti-join form is retained, it also requires positive membership existence and a verified bootstrap coverage marker.
- [ ] Every final mapped intake/document, BCI/BCII approval, training/graduation, exam/result/decision, and GPLX issue/print/receive/return mutation rechecks membership under transaction; list filtering alone is insufficient.
- [ ] History/detail/report/print/XML/decision/GPLX-history branches continue to read physical/history relations without active-membership exclusion.
- [ ] `MIXED` procedures isolate the new-business guard to the correct branch.
- [ ] Business roles cannot bypass approved modules with direct base-table new-business reads/writes.
- [ ] Static inventory and integration gates fail a release if any new/changed core-table branch is unknown or lacks the appropriate history/new-business treatment.

## 6. A–F owner decisions

Before enabling any A–E hard delete:

- [ ] Owner approves a versioned `HistoryProbeManifest` naming every hard/soft relation, exact in-transaction probe identifier, null/default/sentinel semantics, child order and schema/mapping fingerprint.
- [ ] An unknown/new relation, unrecognized sentinel or probe-version mismatch defaults to `PRESERVED_EXCLUDED`.
- [ ] Automated A–E hard delete remains disabled until those manifests are approved; T01's hard-delete assertion is gated by the same approval.

### A. `NguoiLXHS_GiayTo`

- [ ] Hard delete is allowed only after an in-transaction proof that no retained dossier/history/report/form/XML/audit flow needs the document.
- [ ] Otherwise the original row and historical payload remain unchanged and inactive membership excludes only new processing.

### B. `NguoiLX_HoSo`

- [ ] Hard-delete proof checks GPLX, documents, `MaBC2`, `MaKySH`, `SoBD`, all result/remark columns, all decision fields and every hard or soft relation.
- [ ] Any history signal retains the complete dossier shell and V1-owned state.
- [ ] Every new BCII/exam/result/decision/GPLX selector and final writer excludes inactive membership.

### C. `NguoiLX`

- [ ] Hard delete is allowed only when no dossier shell, GPLX or other retained relationship exists.
- [ ] Cascades are never used to discover scope.
- [ ] A learner shell remains when required for identity snapshots or historical joins.

### D. `BaoCaoI`

- [ ] Proof includes the hard dossier relation and soft `BaoCaoII.MaBCI` relation.
- [ ] A history-bearing BCI shell remains joinable but cannot be selected or mutated for a new BCII.

### E. `KhoaHoc`

- [ ] Proof enumerates all descendants explicitly; the course cascade is never the scope algorithm.
- [ ] A history-bearing course and descendants remain joinable.
- [ ] Inactive course ancestry excludes every descendant from new intake/BCI/BCII/exam/result work.

### F. `DM_DonViGTVT`

- [ ] Automated sync never hard-deletes a national unit row.
- [ ] Only the exact source-profile/stream/routed membership is made inactive.
- [ ] No source-to-target global anti-join is permitted.
- [ ] Any exceptional manual retirement is a separately approved owner operation outside the sync algorithm.

## 7. Reactivation contract

- [ ] Reappearance of the same exact key under the same owner stream locks the registry and existing V1 shell.
- [ ] Parent membership is reactivated before child membership.
- [ ] V2-owned columns resync using the approved mapping.
- [ ] V1-owned BCII, exam, result, decision and GPLX history remains byte-for-byte unchanged.
- [ ] Special merge rules remain in force for `TT_XuLy`, `GhiChu`, `GiayCNSK` and `GiaiTrinh`.
- [ ] Reactivation creates no duplicate and does not reset a completed lifecycle.
- [ ] A duplicate current-version reactivation is an idempotent no-op.
- [ ] A same-key claim from another source profile/stream becomes `STREAM_OWNERSHIP_CONFLICT`; ownership is never transferred automatically.
- [ ] On reactivation, current `DeletedAtSourceVersion` is deterministically cleared, `ReactivatedAtSourceVersion` is set, and the append-only journal retains the prior delete event.

## 8. Security, indexing and retention

- [ ] Exact protected-key lookup is sargable through an approved indexed relation; the diagnostic HMAC is not used as route identity, ownership uniqueness or SQL lookup key.
- [ ] The lookup/index design is benchmarked at production cardinality and plans are reviewed for every domain.
- [ ] No scalar per-row UDF is introduced without a separate measured performance proof.
- [ ] Target data, registry, logs, backups and transport follow approved encryption and least-privilege controls.
- [ ] Business-role attempts to read raw keys or mutate registry state are tested and denied.
- [ ] Owner records the legal/audit retention duration: `________________`.
- [ ] Owner records inactive-membership retention: `________________`.
- [ ] Owner records HMAC/key-rotation policy and custodian: `________________`.
- [ ] Owner records backup retention and purge authority: `________________`.
- [ ] Until those periods and a purge proof are approved, inactive membership and its transition journal are not purged.

## 9. Acceptance gates

All design tests in `V1_DELETION_EXCLUSION_DESIGN.md` must pass for OTO and independently for MOTO. Owner additionally requires:

- [ ] delete versus final-business-insert concurrency is linearizable;
- [ ] bootstrap-incomplete and unowned-tombstone paths fail closed;
- [ ] CT-retention expiry recovers only through bounded stream-scoped reconciliation;
- [ ] target-collation alias and deliberate token-collision tests fail closed;
- [ ] base-table permission bypass and raw-key disclosure tests fail;
- [ ] logs, errors, diagnostics and backup extracts contain no prohibited raw operational key or PII;
- [ ] index plans, latency, blocking and full-cycle load remain within approved limits;
- [ ] removing the guard from any one of the `63` transactional branches fails the release suite;
- [ ] none of the `12` master/control-plane paths can release inactive ownership or make a retained shell eligible for new business;
- [ ] the release manifest accounts for all `75/75` guard-required paths;
- [ ] every enumerated history reader remains available for inactive retained shells.

## 10. Dependencies and sign-off

H12 approval does not approve:

- Change Tracking enablement or retention;
- snapshot isolation;
- schema or stored-procedure deployment;
- H14/H15 or any separate mapping-owner decision;
- a production rollout or data backfill.

Owner decision:

```text
Decision: __________________________________________
Owner:    __________________________________________
Date:     __________________________________________
Evidence exceptions / conditions:
____________________________________________________
____________________________________________________
```
