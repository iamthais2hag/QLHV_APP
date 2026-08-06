# RT-04 CT and Checkpoint Operations

## Current read-only state

| Profile | Checkpoint | CT current | CT min-valid | Tracked tables | Snapshot | RCSI |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| OTO | 0 | 1 | 0 | 5 | ON | OFF |
| MOTO | 0 | 0 | 0 | 5 | ON | OFF |

Both checkpoint namespaces remain distinct and currently satisfy
`checkpoint >= min-valid`. OTO nevertheless cannot advance because CT version 1
contains one delete in `NguoiLX` and one related delete in `NguoiLX_HoSo`; the
resulting retained target is an ownership conflict requiring manual review.

No checkpoint, marker, CT option, retention setting or learner row was changed in
RT-04. Retention remains 7 days. Expected maximum operational downtime was not
provided, so retention adequacy was not asserted and no retention change was made.

Manual recovery contract:

1. Keep realtime and Auto Sync writers OFF.
2. Preserve the version-1 CT evidence and current target record.
3. Obtain an operator-approved disposition for the source deletion and retained
   target ownership; never auto-delete/deactivate or invent a source row.
4. Do not advance checkpoint 0 and do not full-resync production without a separately
   sealed, reviewed recovery plan.
5. After the approved data state is stable, rerun exact identities, fingerprints,
   three stable classifications, CT/min-valid, checkpoints and mutex checks from
   scratch.
6. If checkpoint is below min-valid, stop fail-closed and use a manually approved
   recovery/backfill plan; do not silently reset it to current CT version.

Evidence:
`D:\QLHV_RT04_EVIDENCE\RT04_PRODUCTION_OPERATIONS_20260727_225621\06_oto_ct_change_shape_read_only.log`
