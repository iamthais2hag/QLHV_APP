# RT-02B Operator Execution Runbook

Status: **NOT AUTHORIZED — DO NOT EXECUTE**

This runbook is prepared by RT-02A. It does not authorize RT-02B, SQL execution,
database creation, restore, Change Tracking, snapshot isolation or business
writes.

## 1. Separate approval package

Before any connection, obtain one signed/recorded approval containing:

- exact isolated OTO source database;
- exact isolated MOTO source database;
- exact isolated QLHV target database;
- exact SQL Server identity;
- environment ID and owner approval ID;
- dataset SHA-256 and sanitized/synthetic provenance;
- creation/expiration times;
- database IDs and GUIDs;
- proof that no identity/alias/route matches production;
- permission to execute the named enable and rollback templates;
- planned test window and operator.

Example database names in task text are not valid approval.

## 2. Independent identity preflight

Before replacing template placeholders, capture `DB_NAME()`, `DB_ID()`,
server identity, database GUID, read/write state, recovery model, resolved
connection route and TEST/approval extended properties for all three databases.
Compare against the approval package and against a current production identity
catalog.

Any mismatch concludes:

`BLOCKED — ISOLATED APPLY SAFETY CONTRACT NOT PROVEN`

Do not edit or execute a template after a mismatch.

## 3. Materialize reviewed templates

Create an operator-controlled copy outside the repository. Replace:

- `EXACT_TEST_DB`;
- `__RT02_SQL_SERVER_INSTANCE__`;
- `__RT02_ENVIRONMENT_ID__`;
- `__RT02_OWNER_APPROVAL_ID__`;
- `__RT02_DATABASE_ID__`;
- `__RT02_DATABASE_GUID__`.

Peer-review the rendered diff. No arbitrary table or column may be added.

Approved enable templates:

- `database/patches/20260727_rt02_enable_ct_snapshot_oto_test.sql`
- `database/patches/20260727_rt02_enable_ct_snapshot_moto_test.sql`

Approved rollback companions:

- `database/patches/20260727_rt02_disable_ct_snapshot_oto_test.sql`
- `database/patches/20260727_rt02_disable_ct_snapshot_moto_test.sql`

RCSI is not authorized. Production connections are prohibited.

## 4. Execution order after approval

1. Record zero active isolated apply cycles.
2. Re-run identity preflight immediately before execution.
3. Enable OTO test CT/snapshot with its rendered template.
4. Verify OTO metadata and two-table allowlist.
5. Enable MOTO test CT/snapshot with its rendered template.
6. Verify MOTO metadata and two-table allowlist.
7. Load or verify the approved deterministic fixture.
8. Run read-only shadow plan and compare expected counts/hashes.
9. Run focused insert safety cases.
10. Run focused update safety cases.
11. Run target-only retention cases.
12. Run atomic rollback, concurrency and crash recovery cases.
13. Run 100/100/1,000 load cases and record non-production metrics.
14. Verify final row counts, duplicate count, retained rows, QLHV-owned hashes,
    marker and isolated checkpoint.

Do not run Existing Auto Sync, RT-01 production polling or V2-to-V1 in the same
window. Do not connect the production API/Worker to isolated databases.

## 5. Pass gates

All must pass:

- exact identity preflight and non-production proof;
- expected fixture counts and zero duplicate active identities;
- exactly one insert and one HoTen-only update in the minimal cycle;
- target-only remains active with manual-review evidence;
- no delete/deactivation/profile/ownership change;
- one transaction/one commit per successful cycle;
- complete rollback on every injected pre-commit failure;
- durable-marker recovery after post-commit crash;
- separate environment/profile/mapping checkpoint;
- replay idempotency;
- no raw PII in artifacts;
- Existing Auto Sync, RT-01 and V2-to-V1 production states unchanged.

Any failure stops the run. Do not automatically retry; retain the exact cycle,
marker and error evidence for review.

## 6. Rollback order

After tests or on operator decision:

1. prove no active isolated cycle;
2. re-run exact identity preflight;
3. execute only the matching MOTO rollback companion;
4. verify MOTO CT/snapshot state;
5. execute only the matching OTO rollback companion;
6. verify OTO CT/snapshot state;
7. archive counts/hashes and approval evidence;
8. expire/revoke isolated application routes.

Never execute rollback against a route failing identity validation.

## 7. Production prohibition

RT-02B must stop immediately if any resolved name, server, database ID, GUID,
route or marker matches production. It must not cut over, register a production
writer, enable polling, modify Existing Auto Sync, publish a production apply
checkpoint, or change the V2-to-V1 foundation.
