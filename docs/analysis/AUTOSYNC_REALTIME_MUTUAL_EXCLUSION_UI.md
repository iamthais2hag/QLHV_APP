# Realtime / Auto Sync mutual exclusion and UI

The direct realtime worker and fallback Auto Sync use the same session-owned SQL application lock: `QLHV:CSDT_AUTO_SYNC`.

Auto Sync evaluates the read-only realtime snapshot before queue creation. When realtime is enabled and writable with a live heartbeat, active cycle, or held mutex, backend rejection is:

`AUTOSYNC_BLOCKED_BY_REALTIME_PRIMARY_WRITER`

If the first snapshot is clear, Auto Sync must still acquire the global mutex before stale reconciliation or durable queue creation. This closes the state-check/queue race. The direct realtime startup guard ignores stale Auto Sync rows but continues to reject fresh runs and real source operations.

The operations UI has two independent sections:

1. `A. Realtime trực tiếp — đường chính`: Windows service/process, health, writer/mutex, heartbeat and OTO/MOTO checkpoints.
2. `B. Auto Sync dự phòng`: explicit configuration, effective versus raw runtime counts, source/step and durable history classification.

`Làm mới trạng thái` is GET-only. `Chạy Auto Sync dự phòng` is disabled with the backend decision/reason while realtime owns the write path.
