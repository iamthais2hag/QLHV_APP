# RT-02B0 Isolated Environment Proposal

## Decision

`RT02B ISOLATED ENVIRONMENT PROPOSED — AWAITING OPERATOR APPROVAL`

This document selects a planned isolated SQL Server route for review. It does
not authorize installation, database creation, restore, patch execution,
Change Tracking, snapshot isolation, fixture loading or direct-realtime writes.

Observation time: `2026-07-27T10:40:46+07:00`

Repository baseline:

- branch: `codex/csdt-realtime-v2-to-v1-oto-moto`
- HEAD: `383387e8456d1a61640eee190519ff3f28619218`
- staged files: 0

## Read-only inventory result

### Existing local SQL capability

| Capability | Observation | Disposition |
|---|---|---|
| Default SQL Server | Running `MSSQLSERVER`; observed server identity `CSDLTTTC`; SQL Server 2022 Enterprise 16.0.4255.1 | **REJECTED** |
| LocalDB | `SqlLocalDB.exe` not installed/found | Not currently available |
| Docker/container runtime | Docker command/runtime not installed/found | Not currently available |
| Other named SQL instance | None registered | Not currently available |
| SQL client aliases | None registered in inspected machine/user alias locations | Pass |

The existing default instance is not eligible. Production configuration uses
the `localhost` route and names databases on this instance, including
`QLHV_APP`, `CSDL_OTO`, `CSDL_MOTO`, their BAK databases and V1 databases.
Read-only catalog inventory confirms those databases are online on
`CSDLTTTC\MSSQLSERVER`.

The proposed names do not currently exist on that instance:

`PROPOSED_NAME_COUNT = 0`

No production/application configuration reference to any proposed name was
found. No current user database reported a non-null `source_database_id`;
therefore none of the observed databases, and none of the absent proposed
names, is an active SQL database snapshot.

The production database name/database-ID/GUID catalog was captured as a
comparison denylist. Its canonical evidence digest is:

`SHA256:6D79AEF1AA76BF672B1D8042A66CD51639AF3A6517069BBE555E62A26C591FE8`

No connection string, username or password is included in this proposal.

## Selected planned environment

The selected proposal is a new SQL Server 2022 Developer named instance,
separate from the production default instance.

| Field | Proposed value |
|---|---|
| Host | `CSDLTTTC` |
| Instance name | `QLHVRT02` |
| Expected server identity | `CSDLTTTC\QLHVRT02` |
| Edition | SQL Server 2022 Developer, 64-bit |
| Service start | Manual |
| Network exposure | Shared Memory/local access only during RT-02B |
| TCP | Disabled |
| Named Pipes | Disabled |
| SQL Browser | Remains disabled |
| Proposed isolated data root | `D:\QLHV_RT02_SQLDATA` |
| Environment ID | `RT02B0-CSDLTTTC-QLHVRT02-20260727-01` |
| Owner approval ID | `RT02B-OPERATOR-APPROVAL-20260727-01` |
| Dataset mode | `SYNTHETIC` |
| Approval expiration | `2026-07-31T16:59:59Z` |

The expected server identity is a proposal, not an observed identity: the
named instance has not been installed. Operator approval of this document does
not itself authorize provisioning. After separately approved provisioning,
RT-02B must observe `SERVERPROPERTY('ServerName')` and fail closed unless it
exactly equals the proposed server identity.

Sharing the physical host is acceptable only if all route isolation conditions
above are enforced. If local-only protocols cannot be enforced, this candidate
is rejected and a separately managed Developer VM/container must be proposed.

## Exact planned database names

| Role | Exact proposed name |
|---|---|
| Isolated OTO source | `QLHV_RT02_OTO_TEST` |
| Isolated MOTO source | `QLHV_RT02_MOTO_TEST` |
| Isolated target | `QLHV_RT02_TARGET_TEST` |

These databases have not been created. Consequently they have no approved
`database_id` or database GUID yet. After provisioning and creation are
separately approved, RT-02B must record three unique IDs/GUIDs and compare them
against the captured production denylist before any patch or fixture operation.

The post-provision gate must also prove:

- exact `DB_NAME()` for every route;
- three distinct database names, IDs and GUIDs;
- no production name, ID, GUID, route or alias match;
- ONLINE and READ_WRITE state;
- approved recovery model;
- no `source_database_id`;
- no synonym or linked route to a production database;
- no production application configuration reference;
- no production API/Worker process can resolve the named test instance;
- the approval window has not expired.

Any failure returns:

`ISOLATED_DATABASE_IDENTITY_REJECTED`

## Planned TEST markers

Each future isolated database must contain matching database-level extended
properties:

| Marker | Exact proposed value |
|---|---|
| `RT02_ISOLATED_ENVIRONMENT_ID` | `RT02B0-CSDLTTTC-QLHVRT02-20260727-01` |
| `RT02_OWNER_APPROVAL_ID` | `RT02B-OPERATOR-APPROVAL-20260727-01` |
| `RT02_DATASET_MODE` | `SYNTHETIC` |
| `RT02_PRODUCTION_ROUTE_ALLOWED` | `FALSE` |
| `RT02_EXPIRES_AT_UTC` | `2026-07-31T16:59:59Z` |

Creation of these markers is not authorized by RT-02B0.

## Synthetic dataset and fingerprint strategy

No production clone or production PII is proposed. The deterministic synthetic
generator must create:

- 150 no-change OTO rows;
- 1 OTO source-only insert candidate;
- 1 OTO `HoTen`-only update candidate;
- 1 OTO target-only active/manual-review candidate;
- 3 existing OTO soft-deleted baseline rows;
- 5 no-change MOTO rows;
- 0 duplicate active logical identities.

The dataset fingerprint is computed only after the generator and schemas are
approved. It must be SHA-256 over one canonical UTF-8 manifest containing:

1. environment ID and dataset mode;
2. generator version and repository commit;
3. source/target schema fingerprints;
4. the seven required fixture counts;
5. ordered purpose/version-bound HMAC identities;
6. ordered source/mapped/QLHV-owned row hashes;
7. hashes of every materialized SQL artifact;
8. explicit statement `PII_ROWS=0`.

Raw learner names, CCCD, dates of birth, addresses and source identifiers must
not enter the manifest or test logs.

## SQL templates to materialize after separate approval

The repository templates remain unchanged in RT-02B0.

| Template | Future exact database |
|---|---|
| `20260727_rt02_enable_ct_snapshot_oto_test.sql` | `QLHV_RT02_OTO_TEST` |
| `20260727_rt02_disable_ct_snapshot_oto_test.sql` | `QLHV_RT02_OTO_TEST` |
| `20260727_rt02_enable_ct_snapshot_moto_test.sql` | `QLHV_RT02_MOTO_TEST` |
| `20260727_rt02_disable_ct_snapshot_moto_test.sql` | `QLHV_RT02_MOTO_TEST` |

Materialization must occur in an operator-controlled copy after database
identity observation. The repository `EXACT_TEST_DB` templates must not be
edited in place. RCSI remains unauthorized.

## Proposed lifetime and cleanup

The environment approval expires at `2026-07-31T16:59:59Z`. If provisioning
has not completed before then, the proposal expires without extension.

The future cleanup procedure requires its own destructive approval:

1. stop all isolated test clients and prove zero active apply cycle;
2. revalidate exact server/database identities;
3. archive counts, hashes, marker and checkpoint evidence without PII;
4. execute the matching MOTO rollback template and verify metadata;
5. execute the matching OTO rollback template and verify metadata;
6. revoke all test routes and stop the named instance;
7. approve and remove the three test databases;
8. approve and uninstall/remove the `QLHVRT02` named instance and isolated data
   root;
9. verify production databases, API/Worker config, Existing Auto Sync, RT-01
   and V2-to-V1 are unchanged.

RT-02B0 performs none of these steps.

## Separate permissions still required

The operator must approve each scope explicitly:

1. install/provision SQL Server 2022 Developer named instance `QLHVRT02`;
2. configure manual startup, local-only protocol policy and isolated data root;
3. create the three exact test databases;
4. create the exact TEST/approval/expiration markers;
5. create isolated source and target schemas;
6. materialize and execute the four reviewed CT/snapshot templates;
7. generate/load deterministic synthetic fixture data;
8. allow the explicit test-only composition root to connect and write;
9. run behavioral/load/fault scenarios;
10. execute rollback templates;
11. drop databases, remove files and uninstall the named instance.

Approval for one item does not authorize another. No production connection,
production polling, Existing Auto Sync, RT-01 worker, V2-to-V1 worker, database
clone/restore or production checkpoint publication is included.

## RT-02B entry state

The environment is **PROPOSED, NOT PROVISIONED, NOT READY TO EXECUTE**.

RT-02B may begin only after:

- the selected instance and all required actions receive explicit approval;
- the named instance is separately provisioned;
- the post-provision identity gate passes;
- exact database IDs/GUIDs and dataset fingerprint are recorded;
- rendered templates receive peer review;
- a separate RT-02B execution window is approved.
