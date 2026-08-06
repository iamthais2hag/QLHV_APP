# AutoSync / Realtime state model

Captured: 2026-07-28 (Asia/Ho_Chi_Minh)

The operations contract has four independent groups. No group may be inferred from another.

## Realtime primary

`CSDL_OTO` and `CSDL_MOTO` write directly to `QLHV_APP`. The API reports service state, process state, overall worker health, worker instance, heartbeat, current profile/cycle, writer flag, global mutex, last failure code, and per-profile health/checkpoint/last-cycle time.

## Auto Sync fallback configuration

The Live → BAK → QLHV_APP path is explicitly fallback-only. Its contract reports `Enabled`, `RunOnStartup`, `PollingEnabled`, poll interval, `IsFallbackOnly`, `FallbackModeEnabled`, `ManualRunAllowed`, decision code and reason. Safe defaults are OFF for execution, startup and polling.

## Auto Sync runtime

A run is effective-active only when all conditions hold: status is `QUEUED` or `RUNNING`; durable active slot exists; completion is absent; `UpdatedAtUtc` is inside the heartbeat timeout; and source or step is present. Otherwise a nominally active row is `INACTIVE_STALE_RUN`. Terminal rows are `HISTORY`. Raw slots are reported separately from effective slots.

The worker refreshes `UpdatedAtUtc` every 15 seconds while executing. The default freshness limit is 120 seconds. Stale history remains durable; a legitimate fallback start may close it as failed only after acquiring the global writer mutex.

## UI refresh

Browser refresh is a read-only GET every 10 seconds. It is not a sync, run, slot, source step, or worker heartbeat. The UI reports its own interval and last successful refresh time separately.
