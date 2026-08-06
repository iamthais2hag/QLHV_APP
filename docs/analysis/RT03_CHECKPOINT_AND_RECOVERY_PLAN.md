# RT-03 Checkpoint and Recovery Plan

## Current state

Production has no RT-03 marker, checkpoint, or manual-review table, so neither OTO nor MOTO has a production direct-realtime checkpoint. Change Tracking is also disabled, making the current CT version and minimum-valid version null. No value is borrowed from RT-02, Existing Auto Sync, or V2-to-V1.

The sealed observation plan records both initial CT versions as null and publishes no checkpoint.

## Namespace and content

The future checkpoint key is the exact tuple:

`(SourceProfileCode, Mode=DIRECT_REALTIME_APPLY, MappingFingerprint, EnvironmentId=PRODUCTION)`

OTO and MOTO therefore have separate namespaces. Each value stores the exact source database GUID, source CT version, cycle ID, plan hash, marker hash, publication time, and optimistic row version. A committed marker stores the profile, plan/marker/disposition hashes, source GUID and CT version, exact insert/update/retain counts, preserved QLHV-owned hash, and commit time.

## Initialization

For a future mutation plan, CT is enabled only by the exact profile script. After validating retention, table allowlist, snapshot state, and `CHANGE_TRACKING_CURRENT_VERSION()`, the plan seals that version. Initialization must not infer changes before the sealed version and must not create a checkpoint until a target transaction has committed and the marker/post-commit integrity proof has passed.

MOTO initialization is prohibited until the OTO canary result is `PASSED` and verified.

## Commit sequence

1. Acquire the profile/global exclusion lock and validate production identity.
2. Revalidate immutable plan, source CT window/stage, target comparison, schema/mapping, duplicate and QLHV-owned invariants.
3. Open one target connection and one target transaction.
4. Recheck each exact learner under lock; execute only allowlisted insert/update/manual-review action.
5. Insert the apply marker in the same transaction after all dispositions.
6. Commit once.
7. On a separate post-commit step, verify the marker and all integrity invariants.
8. Publish the checkpoint idempotently only if its namespace is empty or contains exactly the same cycle/plan/marker/version.

## Crash recovery

- Before transaction: no mutation, marker, or checkpoint; safe to retry only after a full preflight.
- Inside transaction before commit: connection/transaction rollback; absence of marker proves no committed cycle.
- After commit before checkpoint: find the exact committed marker by cycle/plan/marker hash, re-run integrity proof, then idempotently publish the matching checkpoint without a second business transaction.
- Retry after published checkpoint: exact matching content is a no-op. Any different cycle, plan, marker, source version, or optimistic version is `RT03_CHECKPOINT_CONFLICT` and stops.
- Ambiguous commit: do not guess and do not advance the checkpoint. Inspect marker and exact target hashes; either complete the verified checkpoint path or invoke the sealed exact rollback and escalate.

The RT-02 isolated harness already verified crash-before/inside/after-commit and replay semantics; Task 1 reused that contract without rerunning RT-02 production/isolation execution. RT-03 adds production identity, immutable-plan, Auto Sync exclusion, and exact rollback gates around it.
