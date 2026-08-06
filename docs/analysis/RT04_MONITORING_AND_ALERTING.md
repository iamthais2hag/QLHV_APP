# RT-04 Monitoring and Alerting

## Status

Monitoring design is documented, but production handoff is blocked. Process presence
alone is explicitly not a health signal: PID 23832 remained present after the RT-03
hosted worker had stopped fail-closed and released its mutex.

Minimum PII-safe monitoring set for the resumed RT-04 window:

| Signal | Source | Stop/alert condition |
| --- | --- | --- |
| SCM service/process | SCM + exact executable path | missing, stopped, duplicate instance |
| worker health/heartbeat | `App_QlhvDirectRealtimeWorkerState` | non-`HEALTHY`, `CycleActive` stuck, stale heartbeat |
| profile health | `App_QlhvDirectRealtimeProfileState` | non-`HEALTHY_NO_CHANGE`/approved apply result |
| last cycle and mutations | cycle history | unexpected insert/update, any delete/deactivate or duplicate |
| checkpoint/source CT | checkpoint + `CHANGE_TRACKING_CURRENT_VERSION()` | checkpoint regression or unapproved lag |
| CT validity | `CHANGE_TRACKING_MIN_VALID_VERSION()` | checkpoint below min-valid version |
| conflicts | privacy-safe classifier | duplicate, conflict, target-only or manual review > 0 |
| mutex | application lock probe | service healthy but competing lock can be acquired |
| Auto Sync | runtime status + run tables | polling enabled or active run/slot/operation > 0 |
| database options | catalog views | CT/Snapshot not ON, RCSI ON, tracked-table count not 5 |
| blocking | DMVs | active user transaction or blocker beyond the approved threshold |
| process resources | exact worker PID | sustained CPU/memory growth or unexpected restart |

Immediate alert observed in this gate:

- durable health `BLOCKED`;
- last error `RT03_UNSUPPORTED_DRIFT`;
- OTO CT current/checkpoint 1/0;
- mutex absent;
- OTO conflict/target-only/manual-review 1/1/1.

Current durable schema does not yet record cycle start/duration or retry count as
first-class fields. Those gaps must be implemented and tested before declaring the
full RT-04 monitoring requirement complete. Logs and alerts must contain only
profile, cycle/correlation ID, status, counts, versions, duration and error code—no
CCCD, learner name, address, raw source key, connection string or secret.

The hosted worker currently catches a fail-closed cycle exception, records
`BLOCKED`, and returns while the generic host process stays alive. A future SCM
service could therefore remain `Running` without a writer or mutex, and SCM crash
recovery would not fire. RT-04 must make fatal worker/lock failures terminate the
host non-zero or provide an equivalent service-health failure mechanism, and its
service start probe must verify durable health rather than SCM state alone.

Evidence:
`D:\QLHV_RT04_EVIDENCE\RT04_PRODUCTION_OPERATIONS_20260727_225621`
