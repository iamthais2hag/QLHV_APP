# RT-04 Auto Sync Fallback Runbook

## Current boundary

- API Auto Sync polling: OFF;
- history rows/latest run: 11/11;
- active run/slot/operation: 0/0/0;
- RT-03 worker: fail-closed and mutex released;
- Auto Sync was not started during RT-04.

## Emergency fallback sequence

Auto Sync may be restored only after explicit operator approval:

1. Stop the exact standalone worker or SCM service, whichever owns production.
2. Verify the process/service is OFF and durable `CycleActive=0`.
3. Verify no active transaction, blocker or in-flight marker/checkpoint publish.
4. Acquire and release `QLHV:CSDT_AUTO_SYNC` from a verification session to prove
   the realtime lifetime lock is gone.
5. Disable both RT-03 profiles and controlled-cutover feature state using the exact
   reviewed rollback patch; do not mutate `App_HocVien`.
6. Recheck checkpoints, markers, CT/Snapshot/RCSI and production counts.
7. Restart the API through the reviewed launcher without pause overrides only after
   steps 1–6 pass.
8. Prove exactly one Auto Sync writer, active slots settle correctly, and no realtime
   worker can acquire the shared lock.

Never run Auto Sync merely because the RT-03 hosted worker is `BLOCKED`. The present
OTO ownership conflict must first receive a manual disposition; fallback does not
authorize a delete, deactivate, full sync or synthetic mutation.

Rollback/recovery commands must continue to use the exact repository scripts and
exact PID/service identity checks. No wildcard process stop or learner DML is
permitted.
