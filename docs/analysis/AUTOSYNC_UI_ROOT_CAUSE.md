# AutoSync UI root cause

## Confirmed production cause

At API startup, the effective API-local production configuration had `QlhvAutoSync.Enabled=true` and `RunOnServerStartup=true`. The standalone realtime Windows worker had the opposite Auto Sync overrides and already owned the global `QLHV:CSDT_AUTO_SYNC` mutex.

The API created durable run `FB2660EE-0518-43EB-B069-76DF2EF7495E` as `QUEUED` with `ActiveSlot=1`. The API-hosted Auto Sync worker could not acquire the mutex and returned without completing or heartbeating the row. Repository status logic treated every `ActiveSlot=1` row as active forever. The 60-second startup polling loop then repeatedly returned `ACTIVE_OPERATION`.

The frontend compounded this by treating `QUEUED`/`RUNNING` and any Active RunId as execution, displaying server polling beside execution, and polling the status GET every 2.5/10 seconds. Login also sent a best-effort ensure-fresh POST. Thus UI refresh, backend polling, stale history and real execution were conflated.

## Corrections

- Durable `UpdatedAtUtc` and `ActiveSlot` are now projected.
- A single classifier determines effective active/stale/history state.
- Active RunId is null for stale history.
- Login no longer starts fallback work.
- Startup polling requires explicit `PollingEnabled` and `FallbackModeEnabled`.
- API/worker identity labels no longer claim parity with the separate Windows realtime worker.
- Update tooling now preserves safe Auto Sync fallback defaults instead of re-enabling it.
