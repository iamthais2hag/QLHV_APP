# RT-02A Apply Transaction and Checkpoint Design

Status: **ISOLATED FAKE HARNESS ONLY — NO DATABASE EXECUTION**

## Separation

The new namespace is:

`QLHV.Application.Sync.QlhvDirectRealtime`

It does not reference the V2-to-V1 realtime namespace or repository set. It
also does not reference Existing Auto Sync. Neither API nor Worker production
composition registers the transaction factory or apply cycle.

Existing flows remain independent:

- Existing Auto Sync: `live -> BAK -> QLHV_APP`
- proposed direct realtime: `live -> QLHV_APP`
- V2-to-V1: out of RT-02A scope

## Immutable apply plan

`QlhvDirectRealtimeApplyPlan` binds:

- cycle and isolation environment identity;
- source profile;
- mapping fingerprint;
- source and target schema fingerprints;
- source watermark;
- identity-normalization version;
- stage hash;
- comparison hash;
- disposition hash;
- ordered immutable operations.

The apply cycle has no source reader. It cannot silently reread source data or
rebuild a validated plan during apply.

## Feature gates

All five options default to `false`:

- `EnableQlhvDirectRealtime`
- `EnableQlhvDirectRealtimeShadow`
- `EnableQlhvDirectRealtimeWrites`
- `EnableQlhvDirectRealtimeDeletes`
- `EnableQlhvDirectRealtimeIsolatedApply`

The isolated test composition root explicitly enables master, writes and
isolated apply while keeping delete disabled. A cycle fails closed if any
required flag is off or delete is on.

## Caller-owned target transaction

One cycle obtains one `IQlhvDirectRealtimeTargetTransaction`. The coordinator,
not a row repository, owns commit/rollback:

1. validate all three isolated identities;
2. open one target transaction;
3. revalidate the isolated target;
4. acquire `environment + source profile` lock;
5. verify immutable plan fingerprints and target comparison state;
6. apply allowed insert operations;
7. update only approved source-owned `HoTen`;
8. retain target-only rows and persist manual-review evidence without mutation;
9. verify affected counts and the aggregate QLHV-owned hash;
10. write a durable apply marker inside the transaction;
11. commit once;
12. publish the separate apply checkpoint after commit.

Any failure before commit invokes one rollback. Insert and update are not
committed independently. The test adapter keeps a transaction-local snapshot,
so a later update or verification failure removes an earlier insert.

## Checkpoint identity and publication

The logical key is:

`SourceProfile + DIRECT_REALTIME_APPLY + MappingFingerprint + EnvironmentId`

It is separate from:

- Existing Auto Sync run/history;
- RT-01 shadow observations/checkpoint;
- V2-to-V1 checkpoints.

Publication requires a committed marker whose cycle, plan and disposition
hashes match. Conflicting content returns `CHECKPOINT_CONFLICT`; no second
checkpoint is created.

## Crash recovery

| Crash point | Recovery contract |
|---|---|
| Before target transaction | No mutation and no checkpoint |
| Inside target transaction | Full rollback |
| After target commit, before checkpoint | Read durable marker; publish idempotently |
| After checkpoint, before completion response | Re-read matching checkpoint; return idempotent success |
| Mapping/schema/plan drift | Conflict; no replay |
| Same cycle retry | No duplicate insert/update/checkpoint |

`TARGET_CHANGED_SINCE_SHADOW` never converts an insert into an update. It
rolls back the whole cycle.

## Coexistence/cutover lock contract

RT-02A uses an isolated lock name and isolated in-memory state. It shares no
Existing Auto Sync `ActiveSlot`, global lock, plan or checkpoint. A future
cutover design must add a production coordination lock covering the same source
profile before either Existing Auto Sync target writes or direct-realtime
target writes are enabled. That lock is designed here but not production-wired.
