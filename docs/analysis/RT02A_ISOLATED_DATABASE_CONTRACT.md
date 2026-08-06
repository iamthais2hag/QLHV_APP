# RT-02A Isolated Database Contract

Status: **PREPARED, NOT EXECUTED**

RT-02A prepares a future direct-realtime apply test:

`isolated OTO source -> isolated QLHV target`

`isolated MOTO source -> isolated QLHV target`

It does not select, create, restore, connect to, or alter a database. The exact
database names are deliberately unresolved until a separate RT-02B approval.

## Required approved environment record

RT-02B must provide all of these values as one immutable approval record:

| Field | Requirement |
|---|---|
| `IsolatedSourceOtoDatabase` | Exact approved OTO test database |
| `IsolatedSourceMotoDatabase` | Exact approved MOTO test database |
| `IsolatedTargetDatabase` | Exact approved QLHV test target |
| `SqlServerInstance` | Exact server identity, not a friendly alias |
| `EnvironmentId` | Unique TEST environment marker |
| `DatasetFingerprint` | SHA-256 or stronger fixture/copy fingerprint |
| `SourceCopyProvenance` | Sanitized clone approval or deterministic generator version |
| `CreatedAtUtc` / `ExpiresAtUtc` | Valid, non-expired approval window |
| `OwnerApprovalId` | Separate operator/owner approval for RT-02B |

The three database names, `database_id` values and database GUIDs must be
distinct. Blank, expired, partial or contradictory records fail closed.

## Runtime identity preflight

For each route, RT-02B must observe and compare:

- `DB_NAME()` against the exact approved name, case-sensitively;
- `database_id`;
- `SERVERPROPERTY('ServerName')`;
- `sys.database_recovery_status.database_guid`;
- ONLINE and READ_WRITE state;
- recovery model;
- the resolved connection route;
- `RT02_ISOLATED_ENVIRONMENT_ID`;
- `RT02_OWNER_APPROVAL_ID`;
- the approved production-identity comparison result.

The validator rejects a name or identity match with:

`ISOLATED_DATABASE_IDENTITY_REJECTED`

The following names are always refused:

- `CSDL_OTO`
- `CSDL_MOTO`
- `CSDL_OTO_BAK`
- `CSDL_MOTO_BAK`
- `QLHV_APP`
- `CSDL_OTO_V1`
- `CSDL_MOTO_V1`

Name checks are only the first gate. An alias, alternate route, database GUID,
database ID or server identity matching production is also rejected.

## Dataset and privacy contract

RT-02B may use only:

1. an approved sanitized isolated clone; or
2. a deterministic synthetic fixture.

No real name, CCCD, date of birth or address may appear in test artifacts or
logs. Diagnostic learner identities use purpose/version-bound keyed HMAC.
Reports contain counts and hashes only.

The isolated integration fixture must preserve:

| Relationship | Count |
|---|---:|
| OTO no-change | 150 |
| OTO source-only insert candidate | 1 |
| OTO HoTen-only update candidate | 1 |
| OTO target-only retained/manual-review | 1 |
| Existing OTO soft-deleted baseline | 3 |
| MOTO no-change | 5 |
| Duplicate active logical identity groups | 0 |

## Review-only SQL templates

- `20260727_rt02_enable_ct_snapshot_oto_test.sql`
- `20260727_rt02_enable_ct_snapshot_moto_test.sql`
- `20260727_rt02_disable_ct_snapshot_oto_test.sql`
- `20260727_rt02_disable_ct_snapshot_moto_test.sql`

Every template begins with `USE [EXACT_TEST_DB]; GO;` and contains unresolved
approval placeholders. It cannot pass its own guard until RT-02B replaces every
placeholder from the approved environment record.

The enable templates have an exact database/server/ID/GUID/marker guard, a
fixed `NguoiLX` and `NguoiLX_HoSo` allowlist, two-day Change Tracking retention,
and `ALLOW_SNAPSHOT_ISOLATION`. They explicitly do not enable RCSI. Rollback
templates revalidate the same identity before disabling only those allowlisted
tables, database Change Tracking and snapshot isolation.

No template was executed by RT-02A.

## RT-02B entry blocker

RT-02B is blocked until exact isolated names, identities, provenance,
fingerprint, expiration and owner approval are supplied and independently
verified. Example names in the task are not approval.
