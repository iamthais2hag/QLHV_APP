# RT-02A Crash, Concurrency and Load/Fault Test Plan

Status: **IN-MEMORY ISOLATED HARNESS — NOT A PRODUCTION LATENCY CLAIM**

## Behavioral coverage

The suite implements the 50 numbered RT-02A requirements:

- environment identity: 1–6;
- insert: 7–14;
- update: 15–23;
- target-only retention: 24–29;
- atomicity/recovery: 30–37;
- privacy/security: 38–42;
- regression boundaries: 43–50.

The environment cases exercise production names, alias-to-production,
production identity matches, absent TEST marker, wrong server, and exact
isolated acceptance.

## Concurrency cases

| Injection | Expected result |
|---|---|
| Target appears after shadow insert plan | `TARGET_CHANGED_SINCE_SHADOW`, rollback, no insert-to-update conversion |
| Target mapped hash changes before update | `TARGET_CHANGED_SINCE_SHADOW`, rollback |
| Source row hash changes after staging | `SOURCE_CHANGED_SINCE_SHADOW`, rollback |
| Update affects zero/multiple rows | rollback |
| Mapping/schema fingerprint changes | `PLAN_FINGERPRINT_CONFLICT`, no checkpoint |
| Existing checkpoint has different content | `CHECKPOINT_CONFLICT`, no target transaction |

## Crash/fault cases

| Injection | Assertion |
|---|---|
| Update fails after an insert | earlier insert is rolled back |
| Final verification fails | all operations and marker are rolled back |
| Target timeout at commit | rollback; a separately invoked retry may succeed |
| Synthetic deadlock at commit | rollback; a separately invoked retry may succeed |
| Crash after commit/before checkpoint | durable marker survives; retry publishes one checkpoint without second target transaction |
| Duplicate event/cycle replay | no duplicate learner or checkpoint |

The harness does not hide retry policy inside a repository. Timeout/deadlock
tests explicitly invoke the next attempt and count it, preserving an auditable
cycle boundary.

## Load scenarios

The fake harness executes:

1. one insert + one update + one retained target-only;
2. 100 inserts;
3. 100 updates;
4. 1,000 mixed operations (500 insert, 499 update, 1 retained);
5. duplicate replay;
6. target timeout;
7. deadlock;
8. crash after commit;
9. checkpoint conflict.

Each measured success scenario captures:

- cycle duration;
- transaction duration;
- fake rows/second;
- query count;
- explicit retry count;
- managed-memory delta;
- rollback success;
- inserted/updated/retained counts.

These numbers only validate harness behavior and measurement plumbing. They
must not be used to claim a production latency of one to three seconds.

## RT-02B extensions

After separate approval, RT-02B must repeat these tests on approved isolated
databases with:

- real target transaction timing;
- SQL command and round-trip counts;
- lock wait/deadlock evidence;
- transaction rollback verification queries;
- durable marker/checkpoint recovery after controlled process termination;
- data integrity counts and QLHV-owned hash comparisons.

Any production route, PII exposure, partial commit, delete/deactivation,
checkpoint-before-commit or non-idempotent retry is P0 and stops RT-02B.
