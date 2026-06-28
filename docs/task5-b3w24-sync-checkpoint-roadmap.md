# Task 5 B3W24 - Sync checkpoint and roadmap

## 1. Current main status

- Branch `main` is merged through B3W23.
- Current `main` HEAD: `6194ac01`.
- B3W23 completed the wording audit for pre-execute/source concept alignment.
- This document is a checkpoint only. It does not approve sync, execute, SQL scripts, or production use.

## 2. What is complete

Completed Task 5 sync foundation and readiness work includes:

- Source identity columns for `App_HocVien`:
  - `SourceProfileCode`
  - `SourceMaDK`
  - `SourceSystem`
  - `SourceVersion`
- Controlled TEST backfill:
  - existing 1970 `App_HocVien` rows in `QLHV_APP_TEST` were attributed to `DATA_V2`;
  - `SourceProfileCode = DATA_V2`;
  - `SourceMaDK = MaDK`;
  - `SourceSystem = V2`.
- TEST DB constraint patch:
  - old `UQ_App_HocVien_MaDK` was dropped on TEST;
  - normal `IX_App_HocVien_MaDK` remains;
  - source-scoped unique filtered index exists for `SourceProfileCode + SourceMaDK`.
- HocVien write path now uses source identity:
  - target staging/merge uses `SourceProfileCode + SourceMaDK`;
  - `MaDK` remains a business/display field, not the sole sync identity.
- Read-only pre-execute plan exists:
  - endpoint: `GET /api/dong-bo-v2/hoc-vien/pre-execute-plan`;
  - does not write `App_HocVien`;
  - does not write `App_DongBoLog`;
  - does not call `/execute`.
- Concept correction is documented:
  - QLHV_APP is the business management app, not a sync-only app;
  - `SourceProfileCode` is technical sync identity, not business duplication.

## 3. Actual current DATA_V2 plan

Current read-only DATA_V2 plan result:

| Metric | Value |
| --- | --- |
| Source rows read | `1970` |
| Planned insert | `0` |
| Planned update | `0` |
| Planned skip | `1970` |
| Errors | `0` |
| Warnings | `1` |

Known warning:

- One learner has a CCCD/identity length warning:
  - `MaDK 66016-20260513-115000413`
  - `SoCCCD` length warning
  - must be reviewed manually;
  - must not be auto-padded or auto-corrected by sync.

Conclusion for current DATA_V2 test state:

- QLHV_APP_TEST is already aligned with current DATA_V2 by the current technical source identity.
- No DATA_V2 write is needed based on this plan.
- This does not approve execute.

## 4. Correct concept

Current database meanings:

| Name | Meaning |
| --- | --- |
| `CSDT_V1` | Moto old 2025/GPLX test database. |
| `CSDT_V2` | Moto new 2026 test database. |
| `QLHV_APP_TEST` | QLHV_APP application test database. |

These represent the current Moto old/new business data flow under review.
They are not two permanent parallel learner sources that should automatically duplicate students in QLHV_APP.

The future architecture still keeps 7 profiles:

1. `CSDT_MOTO`
2. `CSDT_OTO`
3. `CSDT_MOTO_GPLX`
4. `CSDT_OTO_GPLX`
5. `DATA_V1`
6. `DATA_V2`
7. `QLHV_APP`

Profile meaning:

- The 7 profiles are configuration slots for future operations.
- They do not require all 7 databases to exist immediately.
- A missing profile should block only the feature that needs it.
- `DATA_V1` and `DATA_V2` are technical staging/import profiles.
- `SourceProfileCode + SourceMaDK` is a technical sync/upsert/audit identity.
- It is not final learner/person business identity.
- It must not be misunderstood as approval to duplicate learners in the business UI.

## 5. Next recommended work

### B3W25 - Decide whether any TEST write sync is still needed for current Moto DATA_V2 state

Recommended decision point:

- Current DATA_V2 pre-execute plan says `insert = 0`, `update = 0`, `skip = 1970`.
- Owner should decide whether there is any value in running a TEST execute when no write is expected.
- If no write is needed, keep execute blocked and move to old/new Moto scope design.
- If a TEST execute is still desired, define the exact reason, expected no-op result, backup status, and approval steps.

### B3W26 - Design CSDT_V1 <-> CSDT_V2 Moto old/new sync scope

Questions to settle:

- What is the authoritative Moto learner flow between old 2025/GPLX and new 2026 data?
- Which direction, if any, needs sync/import?
- Which fields should be compared?
- Which source wins when fields differ?
- Which differences are warnings versus blockers?
- How should QLHV_APP present the final learner record after reconciliation?

### B3W27+ - Oto sync analysis separately

Oto should be analyzed separately because it may have more complex source rules than Moto.

Do not copy Moto assumptions into Oto without review.

Recommended Oto steps:

- identify Oto operational databases;
- identify GPLX pairing;
- document Oto learner/course/license mappings;
- run read-only source diagnostics;
- design Oto-specific conflict and presentation rules.

## 6. Explicit stop

Do not run execute or sync until the owner approves an exact TEST-only scenario.

Before any future write test, require:

- exact source profile;
- exact target DB;
- backup/snapshot confirmation;
- config-check result;
- source diagnostics;
- target diagnostics;
- source attribution if relevant;
- pre-execute plan output;
- expected insert/update/skip counts;
- accepted warnings;
- `EnableTargetWrites` approval;
- `Sync:DryRun` state review;
- manual confirmation text;
- clear rollback plan.

Still prohibited by default:

- production database use;
- `/execute`;
- SQL write scripts;
- Hangfire scheduling;
- enabling `EnableTargetWrites`;
- hidden connection strings or real secrets in repo/docs/logs.

