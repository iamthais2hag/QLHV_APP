# CSDT connection profiles

This document describes the fixed business connection profiles planned for QLHV_APP.
These profiles are configuration slots, not proof that all databases already exist.

## Fixed profiles

QLHV_APP will manage these fixed profiles:

| Profile | Role | Expected direction |
| --- | --- | --- |
| `CSDT_MOTO` | Motorcycle training operational data | Paired with `CSDT_MOTO_GPLX` |
| `CSDT_OTO` | Car training operational data | Paired with `CSDT_OTO_GPLX` |
| `CSDT_MOTO_GPLX` | Motorcycle GPLX-related data | Paired with `CSDT_MOTO` |
| `CSDT_OTO_GPLX` | Car GPLX-related data | Paired with `CSDT_OTO` |
| `DATA_V1` | Restored/import staging source for V1 data | Imported into `QLHV_APP` |
| `DATA_V2` | Restored/import staging source for V2 data | Imported into `QLHV_APP` |
| `QLHV_APP` | Application database and final merged target | Receives imports from `DATA_V1` and `DATA_V2` |

Important:

- These are fixed profile names for business configuration.
- The application must not require all 7 databases to exist before it can start.
- A missing profile should block only the feature that needs that profile.
- Profile status must be shown safely without exposing server, database, user, password, token, or full connection string.

## Admin menu

Connection profiles must be managed from a QLHV_APP menu, not only from `appsettings`, user-secrets, or environment
variables.

Proposed menu names:

- `He thong / Cau hinh ket noi CSDT`
- `Quan tri / Ket noi CSDL`

The menu manages the 7 fixed profiles:

1. `CSDT_MOTO`
2. `CSDT_OTO`
3. `CSDT_MOTO_GPLX`
4. `CSDT_OTO_GPLX`
5. `DATA_V1`
6. `DATA_V2`
7. `QLHV_APP`

The menu is a configuration and status screen. It must not imply that all 7 databases already exist.

## Bootstrap and storage boundary

`QLHV_APP` is special:

- `QLHV_APP` is the primary application database and bootstrap database.
- In the first stage, the `QLHV_APP` connection still comes from protected server configuration:
  `appsettings`, user-secrets, environment variables, or deployment secret store.
- The menu may display and test `QLHV_APP` status, but it should not require editing the `QLHV_APP` password from
  the menu in the first stage.
- If `QLHV_APP` is unavailable, the app may not be able to load the menu because the menu configuration itself is
  stored in `QLHV_APP`.

The following profiles will be stored inside `QLHV_APP` when the Admin-managed connection settings feature is built:

- `CSDT_MOTO`
- `CSDT_OTO`
- `CSDT_MOTO_GPLX`
- `CSDT_OTO_GPLX`
- `DATA_V1`
- `DATA_V2`

These six profiles are operational/source/staging profiles. They can be edited, saved, tested, and enabled/disabled
from the menu after the storage and encryption design is implemented.

## Profile fields

Each profile row should expose these safe configuration fields:

| Field | Meaning | Notes |
| --- | --- | --- |
| `ProfileCode` | Fixed code such as `CSDT_MOTO` or `DATA_V2` | Primary business key; not freely renamed. |
| `DisplayName` | User-facing name | Editable label is allowed. |
| `Group` | `MOTO`, `OTO`, `GPLX`, `DATA`, or `APP` | Used for filtering/grouping in UI. |
| `Server` | SQL Server host/instance | Treat as sensitive-adjacent; avoid leaking in error details. |
| `Database` | Database name | Treat as sensitive-adjacent; avoid leaking in error details. |
| `AuthMode` | `Windows` or `SQL Login` | Determines whether username/password fields are required. |
| `UserName` | SQL login username when applicable | Do not include in unsafe logs/errors. |
| `Password` | Encrypted at rest, masked in UI | Never return plaintext. |
| `Active` | Feature can use this profile when ready | Inactive blocks related features only. |
| `LastTestedAt` | Last test timestamp | Null if never tested. |
| `LastTestStatus` | Last safe test result | For example `NotTested`, `Success`, `Failed`. |
| `LastTestMessage` | Sanitized test result message | No full connection string, password, raw server error dump. |

The UI should always render the fixed 7 rows, even when some profiles are not configured.

## Planned storage table

Planned schema patch:

```text
database/patches/20260627_add_csdt_connection_profiles.sql
```

Main table:

```text
dbo.App_CsdtConnectionProfile
```

Designed columns:

| Column | Type | Purpose |
| --- | --- | --- |
| `Id` | `uniqueidentifier` | Primary key. |
| `ProfileCode` | `nvarchar(50)` | Fixed unique code, for example `CSDT_MOTO`. |
| `DisplayName` | `nvarchar(200)` | User-facing name. |
| `ProfileGroup` | `nvarchar(50)` | `MOTO`, `OTO`, `GPLX`, `DATA`, or `APP`. |
| `ServerName` | `nvarchar(255)` | SQL Server host/instance. Sensitive-adjacent. |
| `DatabaseName` | `nvarchar(255)` | Database name. Sensitive-adjacent. |
| `AuthMode` | `nvarchar(30)` | `Windows` or `SqlLogin`. |
| `UserName` | `nvarchar(255)` | Required only for SQL Login. Sensitive-adjacent. |
| `PasswordCipherText` | `varbinary(max)` | Encrypted password bytes. Never plaintext. |
| `PasswordUpdatedAt` | `datetime2` | Last password update time. |
| `IsPasswordConfigured` | `bit` | Safe flag for UI; does not expose the password. |
| `IsActive` | `bit` | Enables use by related features. |
| `LastTestedAt` | `datetime2` | Last test time. |
| `LastTestStatus` | `nvarchar(50)` | `NotConfigured`, `Success`, `Failed`, or `Unknown`. |
| `LastTestMessage` | `nvarchar(1000)` | Sanitized message only. |
| `CreatedAt` | `datetime2` | UTC creation time. |
| `UpdatedAt` | `datetime2` | UTC update time. |
| `RowVersion` | `rowversion` | Optimistic concurrency token. |

Constraints in the patch:

- `ProfileCode` is unique.
- `AuthMode` is limited to `Windows` or `SqlLogin`.
- `ProfileGroup` is limited to `MOTO`, `OTO`, `GPLX`, `DATA`, or `APP`.
- `LastTestStatus` is null or one of `NotConfigured`, `Success`, `Failed`, `Unknown`.
- `SqlLogin` requires a non-empty `UserName`.
- `IsPasswordConfigured` must match whether `PasswordCipherText` is present.

Indexes:

- `IX_App_CsdtConnectionProfile_ProfileGroup` on `ProfileGroup`, `IsActive`, `ProfileCode`.

## Fixed seed rows

The schema patch seeds the fixed 7 rows with `IF NOT EXISTS` behavior:

| ProfileCode | DisplayName | ProfileGroup |
| --- | --- | --- |
| `CSDT_MOTO` | `CSDT Moto` | `MOTO` |
| `CSDT_OTO` | `CSDT Oto` | `OTO` |
| `CSDT_MOTO_GPLX` | `CSDT Moto GPLX` | `GPLX` |
| `CSDT_OTO_GPLX` | `CSDT Oto GPLX` | `GPLX` |
| `DATA_V1` | `Data V1` | `DATA` |
| `DATA_V2` | `Data V2` | `DATA` |
| `QLHV_APP` | `QLHV App` | `APP` |

Seed rows are intentionally empty/unconfigured:

- no real server;
- no real database;
- no username;
- no password;
- no connection string.

`QLHV_APP` is seeded so the menu can display/test bootstrap status later.
In the first stage, the actual `QLHV_APP` connection still comes from protected server configuration, not from this
table.

## Audit table

The B3W1 patch also designs a lightweight audit table:

```text
dbo.App_CsdtConnectionProfileAudit
```

It is intended for metadata-only audit events:

- `Create`
- `Update`
- `Test`
- `Enable`
- `Disable`
- `PasswordChange`
- `Seed`

Audit records must not store plaintext passwords, password ciphertext, full connection strings, tokens, or raw provider
error dumps. Store only safe metadata such as profile code, action, actor, timestamp, result status, and sanitized
message.

## Profile status model

Each profile can independently be in these states:

| State | Meaning |
| --- | --- |
| `NotConfigured` | No usable connection settings have been provided. |
| `Configured` | Settings exist, but no successful test result is known yet. |
| `TestFailed` | Last test failed. Store only sanitized error code/message. |
| `TestSucceeded` | Last test succeeded. |
| `Active` | Profile is enabled for features that use it. |
| `Inactive` | Profile is intentionally disabled even if settings exist. |

The state model can be represented as separate fields, for example:

- `isConfigured`
- `isEnabled`
- `lastTestStatus`
- `lastTestedAt`
- `safeMessage`

`Active` means `isConfigured = true`, `isEnabled = true`, and the feature accepts the latest test status.
The exact production rule can be stricter later, for example requiring a recent successful test.

## Feature-level gating

Do not gate the whole application on all profiles.
Gate only the feature that needs a profile.

Examples:

| Feature | Required profile(s) | If missing/not ready |
| --- | --- | --- |
| View normal QLHV_APP screens | `QLHV_APP` | Block only database-backed app screens. |
| Test motorcycle operational connection | `CSDT_MOTO` | Disable only this test action. |
| Test motorcycle GPLX connection | `CSDT_MOTO_GPLX` | Disable only this test action. |
| Restore MOTO to staging | `CSDT_MOTO`, target staging profile | Block only this restore operation. |
| Import V1 to QLHV_APP | `DATA_V1`, `QLHV_APP` | Block only V1 import. |
| Import V2 to QLHV_APP | `DATA_V2`, `QLHV_APP` | Block only V2 import. |
| Compare V1 and V2 imports | `DATA_V1`, `DATA_V2`, `QLHV_APP` | Block only comparison/merge review. |

## Security rules

- Do not commit real connection strings.
- Do not expose full connection strings in API responses, logs, docs, or frontend.
- Store `QLHV_APP` bootstrap secrets only in protected server configuration, user-secrets, environment variables, or
  deployment secret store.
- Store non-bootstrap CSDT/DATA profile passwords encrypted at rest in `QLHV_APP` when Admin-managed storage is
  implemented.
- Do not return password plaintext to the frontend.
- The UI must show passwords as masked text only.
- When a new password is submitted, the backend must encrypt it before saving.
- Test connection must not log the full connection string.
- API responses and logs must not include server, database, username, password, or detailed provider errors when that
  could leak sensitive infrastructure details.
- Audit create/update/test/enable/disable actions without storing raw secrets.

## Initial menu functions

The first version of the menu should support:

- list the 7 fixed profiles;
- edit configuration for profiles stored in `QLHV_APP`;
- save configuration;
- test connection;
- enable/disable `Active`;
- show last test timestamp, status, and sanitized message.

Adding arbitrary new profiles is not the main business requirement now.
The UI may leave room for future extension, but the current design should prioritize the fixed 7 profiles.

If a profile is not configured, inactive, or has a failed test, only the related function should be blocked.
The application must not fail globally just because one of the 7 profiles is not ready.

## Relationship to current Task 5 work

The current V2 sync work was built around a single source key named `CSDT_V2`.
That path is useful as a foundation, but it is not the final multi-source architecture.

Before any production execute design, the team must decide how `CSDT_V2` maps into the new fixed profile model.
Likely options:

- Rename/replace `CSDT_V2` with `DATA_V2`.
- Keep `CSDT_V2` only as a legacy alias during migration.
- Add source-profile selection so the same sync code can run against `DATA_V1` or `DATA_V2`.

Until that decision is made, single-source execute must remain blocked.
