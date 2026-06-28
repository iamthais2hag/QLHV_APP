# Task 5 B3W22 - Sync source concept correction

## 1. Product identity

`QLHV_APP` is the business management application for **Trung tam dao tao lai xe co gioi duong bo Thanh Cong**.

Sync/import is an important feature of the product, but it is not the whole product. The application also owns learner
management, course management, teacher/vehicle workflows, documents, printing, reporting, and future administration
features.

Task 5 must therefore be treated as a controlled data migration/import capability inside QLHV_APP, not as a standalone
sync-only system.

## 2. Correct meaning of current test databases

The names `v1`, `v2`, and `v3` used during ChatGPT/Codex work are project/task phases. They are not formal software
versions or database versions unless the team later defines them that way.

Current local/test database meaning:

| Name used during work | Correct meaning |
| --- | --- |
| `CSDT_V1` | Moto old 2025/GPLX test database. |
| `CSDT_V2` | Moto new 2026 test database. |
| `QLHV_APP_TEST` | QLHV_APP application test database. |

These databases currently represent one Moto business data flow, not two independent learner source systems that should
automatically duplicate students inside QLHV_APP.

Important correction:

- `CSDT_V1` and `CSDT_V2` are not automatically two parallel learner lists to keep forever as duplicated business rows.
- They are technical/current test sources used to understand and safely import the Moto data transition.
- Any final business merge rule must decide how QLHV_APP presents a learner after Moto data is migrated or reconciled.

## 3. Correct meaning of the 7 connection profiles

The future architecture still keeps 7 fixed connection profiles:

1. `CSDT_MOTO`
2. `CSDT_OTO`
3. `CSDT_MOTO_GPLX`
4. `CSDT_OTO_GPLX`
5. `DATA_V1`
6. `DATA_V2`
7. `QLHV_APP`

These are configuration profiles and technical data-flow slots.

They are not a requirement that all 7 databases must exist before the app can run. Each profile can be:

- not configured;
- configured;
- test failed;
- test succeeded;
- active;
- inactive.

`QLHV_APP` is the app bootstrap database. In the first implementation stage, the app connection still comes from
protected server configuration such as appsettings, user-secrets, or environment variables. The connection-profile menu
can show or test its status, but should not be misunderstood as requiring the app to store its own bootstrap password in
the menu.

`DATA_V1` and `DATA_V2` are import/staging profiles. Their job is to let the app read controlled source snapshots before
updating QLHV_APP. They are not final business categories of learners.

## 4. Technical source identity vs business identity

`SourceProfileCode + SourceMaDK` is a technical sync identity.

It answers:

- Which technical source profile produced this imported row?
- Which source registration key did the row come from?
- Can the importer update the correct source-owned target row without touching another source's row?
- Can diagnostics and audit explain what happened?

It does not answer:

- Is this the same real person as another row?
- Should QLHV_APP show one canonical learner row or multiple source-specific rows?
- Which source wins when old Moto and new Moto data disagree?
- How should duplicate/overlapping learners be reviewed by staff?

Business identity is a separate design question. It may later use a reviewed combination of fields such as learner
profile, course, license category, identity number, registration lifecycle, and human review rules.

For safe sync code, `SourceProfileCode + SourceMaDK` prevents accidental overwrite. For business UI/data modeling, it
must not be treated as approval to duplicate students permanently.

## 5. What B3W14-B3W21 did correctly

B3W14-B3W21 moved the HocVien write path and read-only planning toward safer technical identity:

- write models carry source identity fields;
- staging/merge logic uses source-scoped identity instead of `MaDK` alone;
- TEST database constraint strategy supports source-scoped uniqueness;
- read-only readiness notes verify the TEST patch result;
- pre-execute plan reports insert/update/skip by source identity;
- preview plan items include readable `actionName`.

Those changes are still useful and correct as safety plumbing.

What must not be misunderstood:

- They do not approve execute/sync.
- They do not mean QLHV_APP must duplicate every learner once for `DATA_V1` and once for `DATA_V2`.
- They do not define the final Moto migration/merge business policy.
- They do not solve Oto-specific sync requirements.
- They do not replace the need for backup, dry-run, diagnostics, and human review before any write.

## 6. Rule for future Moto/Oto sync work

Future sync work should follow these rules:

1. Treat `SourceProfileCode + SourceMaDK` as a technical guardrail for safe import/upsert/audit.
2. Do not use `MaDK` alone as the sync write key.
3. Do not assume `DATA_V1` and `DATA_V2` are permanent duplicated business sources.
4. For Moto, define the intended old-to-new data flow before execute:
   - what comes from old 2025/GPLX data;
   - what comes from new 2026 data;
   - which data should become the visible learner record in QLHV_APP;
   - what conflicts need manual review.
5. For Oto, expect more complex requirements than Moto. Do not reuse Moto decisions blindly.
6. Every import must be scoped to the selected profile and must not accidentally delete or overwrite unrelated source
   data.
7. UI/reporting should distinguish technical source diagnostics from business learner presentation.

## 7. Execute/sync safety status

Execute/sync is still not approved.

Before any write path is used, the team must explicitly approve:

- source profile to import from;
- target environment;
- backup/snapshot status;
- dry-run result;
- source diagnostics;
- target diagnostics;
- pre-execute plan;
- expected insert/update/skip counts;
- conflict/mapping questions;
- operator confirmation;
- `EnableTargetWrites` and `Sync:DryRun` settings.

Until then:

- do not call `/execute`;
- do not enable `EnableTargetWrites`;
- do not schedule Hangfire sync;
- do not run SQL write scripts without review;
- do not run against production.

