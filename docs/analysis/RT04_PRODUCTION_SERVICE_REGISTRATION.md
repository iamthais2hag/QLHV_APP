# RT-04 Production Service Registration

## Registered service

| Property | Verified value |
| --- | --- |
| SCM name | `QLHV_APP_RealtimeWorker` |
| Display name | `QLHV_APP Realtime CSDT Worker` |
| Binary | `D:\QLHV_APP_RUNTIME\app\worker\QLHV.Worker.exe` |
| Working directory contract | `AppContext.BaseDirectory` |
| Start mode | Automatic, delayed start |
| Account | `NT SERVICE\QLHV_APP_RealtimeWorker` |
| Service SID | Unrestricted service SID |
| Recovery | Restart after 5s, 15s and 60s; reset after 86400s |
| Event log | Windows Application, source `QLHV_APP_RealtimeWorker` |
| Log retention | 20 MB circular Application log |

The SCM environment is a durable `REG_MULTI_SZ` contract. It explicitly disables
Existing Auto Sync and the older CSDT realtime path, enables RT-03 realtime/shadow/
writes/controlled-cutover for OTO and MOTO, disables canary/deletes, sets production
environment and a two-second poll interval, and contains no password or connection
secret.

The virtual account has read/execute on the worker bundle and one explicit
read-only ACE on protected production-local configuration. The configuration file
content hash stayed
`9847629CE2D576BB72C23F34AF8B50E8E3F65002DC805C3AF339DDCA8FB5F632` during
ACL repair. No service-account write, delete, ownership or ACL-change right was
granted.

SQL permissions are bounded: target read, exact RT-03 state/marker/checkpoint writes
and learner insert/update; learner delete is explicitly denied. Source access is
read plus object-level `VIEW CHANGE TRACKING` on the five approved tables. The
worker snapshot metadata query uses `sys.partitions`, so no
`VIEW DATABASE PERFORMANCE STATE` grant was added.

## Elevation safety and failed-attempt accounting

The original visible elevation was canceled at an interactive `Join-Path` prompt;
it created no service and caused no worker, checkpoint, Auto Sync or business-data
change. All later launchers resolve the exact repository root `D:\QLHV_APP`, verify
the service script is a leaf, AST-check every `Join-Path` for explicit non-empty
`-Path` and `-ChildPath`, log the sanitized semantic command, pass all arguments,
and use `-NoProfile -NonInteractive`.

A later installer attempt registered the service in stopped state but failed its
initial SQL permission patch; the service had never started. The SQL syntax was
corrected and permissions were proved by `EXECUTE AS LOGIN`. The first start then
failed before database access because the protected config file did not inherit its
directory ACE; an exact read-only file ACE fixed that without changing content.
The next start exposed the read-only DMV permission requirement; code was changed
instead of broadening database permissions. Each failure left the service stopped
or fail-closed and production counts/checkpoints/Auto Sync unchanged.

Final evidence includes `06_elevation_preflight.json`, the sanitized elevation
command logs, `08_service_start_elevated.json`, the three final health samples and
the lifecycle proof in the RT-04 evidence root.
