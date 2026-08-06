# RT-04 Production Operations Runbook

## Normal state

The supported production writer is Windows service `QLHV_APP_RealtimeWorker` at
`D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe`. It runs under virtual account
`NT SERVICE\QLHV_APP_RealtimeWorker` with delayed automatic start. A standalone
worker must never run at the same time.

Normal health requires:

- service `Running`, one PID matching SCM and the approved runtime hashes;
- worker `HEALTHY`, fresh heartbeat and no error;
- OTO then MOTO profiles `HEALTHY_NO_CHANGE`;
- checkpoint equal to current CT and not below min-valid version;
- mutex held by the realtime worker;
- Auto Sync OFF with active run/slot/operation 0/0/0;
- duplicate, conflict, manual review, transaction and blocker counters 0;
- CT/snapshot isolation ON for both sources and RCSI OFF;
- learner counts OTO 156/156 and MOTO 5/5 until an approved real business change.

`CycleActive=1` during a healthy bounded cycle is informational, not by itself an
alarm. It becomes an incident if heartbeat stops, the cycle does not complete, an
error is recorded, or a transaction/blocker persists.

## Monitoring

Run the privacy-safe monitor:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File D:\QLHV_APP\ops\rt04\Get-Rt04ProductionHealth.ps1
```

It reports service/PID/binary hashes, profile state, heartbeat, cycle state,
checkpoint/current/min-valid CT and lag, mutex, Auto Sync, duplicates/manual review,
transactions/blockers, CPU/memory and Windows Application log policy. It emits no
learner PII or credentials. The Application log is circular with a 20 MB limit.
Retry telemetry is not persisted inside a cycle; a failure is recorded as the
privacy-safe `LastErrorCode`, the host exits fail-closed, and SCM retries on the
verified 5/15/60-second recovery schedule.

## Controlled stop and start

Use an elevated, non-interactive PowerShell process:

```powershell
. D:\QLHV_APP\scripts\windows\qlhv-lan\RealtimeWorkerService.ps1
Stop-QlhvRealtimeWorkerService -RuntimeRoot D:\QLHV_APP_RUNTIME
Start-QlhvRealtimeWorkerService -RuntimeRoot D:\QLHV_APP_RUNTIME
```

After stop, require SCM `Stopped/PID 0`, durable `STOPPED`, `CycleActive=0`, no
transaction and mutex free. Before start, require no standalone worker, exact
service account/path/environment, current checkpoints, Auto Sync 0/0/0 and no
blocker. After start, rerun the monitor and require one worker plus mutex ownership.

## Deployment or upgrade

1. Take a fresh read-only production health/classification gate.
2. Stop SCM gracefully and prove quiescence.
3. Publish to a separate staging directory and test it.
4. Copy the complete worker bundle only while the service is stopped.
5. Verify approved bundle hashes and the protected config hash.
6. Preserve the explicit read-only service ACE on
   `D:\QLHV_APP_RUNTIME\config\appsettings.Production.Local.json`.
7. Start through the reviewed non-interactive elevation launcher.
8. Require fresh health, mutex, checkpoint and regression evidence.

Do not broaden source SQL rights to `VIEW DATABASE PERFORMANCE STATE`; snapshot
metadata uses `sys.partitions`. Do not grant target learner delete to the service
account.

## Incident and fail-closed rules

Stop new cycles and preserve evidence on any identity/fingerprint drift, invalid CT
window, unexpected candidate/conflict/manual review, duplicate, Auto Sync overlap,
lost mutex, stale heartbeat, persistent transaction/blocker, CT/snapshot/RCSI drift,
unexpected delete/deactivate, or SCM/worker state mismatch.

Do not auto-full-resync, run Existing Auto Sync to balance counts, manually advance
a checkpoint, restore a deleted source learner, replay the RT-03 canary, process the
three historical photo drifts, or run V2-to-V1. Diagnose read-only first. A safety
block must stop the hosted application; an unexpected crash is handled by SCM
recovery. Reboot requires separate approval.

## Evidence and escalation

Current evidence root:
`D:\QLHV_RT04_EVIDENCE\RT04_CANARY_TARGET_ROLLBACK_EXEC_20260727_230638001`.
Retain the encrypted rollback image and all JSON/TRX/log evidence. Escalate rather
than mutating data if the exact identity, downstream-use safety or checkpoint
contract cannot be proved.
