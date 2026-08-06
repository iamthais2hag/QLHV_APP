# RT-03 Rollback Plan

## Current observation plan

The current plan has zero candidates and performs zero mutation. Its rollback is simply to stop: no business data, metadata, feature, CT, Snapshot, checkpoint, or Auto Sync state is changed.

## Level 1 — before target transaction

Disable RT-03 feature/worker state, release no safety gate until its state is proved, and stop. There is no mutation, marker, or checkpoint. If CT/Snapshot setup had been applied, use only the exact profile disable script after verifying no realtime cycle is active.

## Level 2 — transaction not committed

Roll back the single SQL transaction. Mutations, manual-review evidence, and apply marker are atomic, so none may remain. Do not publish a checkpoint. Requery the marker and exact candidate hashes to prove the outcome before any retry.

## Level 3 — transaction committed

Only the locked before images in the sealed plan may be restored:

- Canary insert: delete the exact `HocVienId` only when source profile/identity/current row hash/creator all match and downstream reference count is zero.
- Source-owned update: restore only the exact previous `HoTen` and mapped hash when target ID, profile, identity, current mapped hash, and active status still match.
- Retain/manual-review: remove only the exact cycle/plan/candidate/HMAC evidence row; the learner was never mutated.

The rollback validator requires one action for every and only every plan candidate, matching profile, HMAC, locked before-image hash, current source-owned hash, current QLHV-owned hash, and action kind. Wildcards, pattern matching, dynamic SQL, full-row restoration, out-of-allowlist learners, QLHV-owned fields, delete/deactivate, and unsafe insert deletion are forbidden.

After exact rollback, disable all RT-03 feature flags, run the read-only postflight, verify duplicate and ownership fingerprints, and leave the checkpoint unpublished or explicitly reconciled to the proven marker state. CT/Snapshot rollback uses the matching exact OTO or MOTO disable script and never changes RCSI.

## Stop instead of rollback

Do not attempt a data rollback when downstream use cannot be proven zero, the current row hash differs, the before image/hash is unavailable, the commit outcome is ambiguous, the candidate is outside the allowlist, or a QLHV-owned field changed. Disable writers, preserve evidence, do not resume Auto Sync, and escalate as P0/P1.
