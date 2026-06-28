# Task 5 B3W25 - DATA_V2 HocVien write sync decision

## 1. Current facts

- `main` is merged through B3W24.
- Current `main` HEAD at decision time: `4d8ec3e3`.
- `QLHV_APP_TEST` already has `1970` HocVien rows attributed to `DATA_V2`.
- Current read-only pre-execute plan for DATA_V2 HocVien:

| Metric | Value |
| --- | --- |
| Source rows read | `1970` |
| Planned insert | `0` |
| Planned update | `0` |
| Planned skip | `1970` |
| Errors | `0` |
| Warnings | `1` |

Known warning:

- One row has a CCCD/identity length warning.
- This is a data quality warning for human review.
- The sync must not auto-pad or auto-correct the value.

## 2. Decision

Do not run DATA_V2 HocVien write sync now.

Do not call:

```http
POST /api/dong-bo-v2/hoc-vien/execute
```

Do not enable:

```text
SyncExecution:EnableTargetWrites=true
```

## 3. Reason

The current DATA_V2 plan has no planned writes:

- `plannedInsert = 0`
- `plannedUpdate = 0`
- `plannedSkip = 1970`

Running execute would add operational risk without a data benefit.

The single CCCD length warning is not a sync blocker for the current no-write decision. It remains a data-quality item
for manual review and must not be silently modified by sync logic.

## 4. What remains useful

Keep these read-only endpoints and checks available:

- `config-check`
- `source-diagnostics`
- `target-diagnostics`
- `source-attribution-diagnostics`
- `pre-execute-plan`

Use them before any future import when:

- the DATA_V2 source changes;
- mapping changes;
- target data changes;
- source identity rules change;
- a new TEST-only write scenario is proposed.

The pre-execute plan remains valuable because it can show whether a future source snapshot would insert, update, or skip
rows before any write guard is opened.

## 5. Next work

Recommended next phase:

### B3W26 - Design Moto old/new CSDT_V1 <-> CSDT_V2 sync scope

Clarify:

- what `CSDT_V1` contributes to the Moto old 2025/GPLX flow;
- what `CSDT_V2` contributes to the Moto new 2026 flow;
- whether any data must move between old/new Moto sources;
- which fields are authoritative;
- what differences are warnings versus blockers;
- how QLHV_APP should present the final learner record after reconciliation.

### Later - Separate Oto analysis

Oto must be analyzed separately.

Do not copy Moto assumptions into Oto without checking Oto source tables, GPLX pairing, field rules, and business
workflow.

## 6. Safety

No execute/sync until the owner approves a specific TEST-only scenario.

Any future write test must explicitly define:

- source profile;
- target DB;
- backup/snapshot status;
- expected insert/update/skip counts;
- accepted warnings;
- rollback plan;
- `Sync:DryRun` state;
- `EnableTargetWrites` state;
- manual confirmation text;
- person responsible for reviewing the result.

Still prohibited by default:

- production database use;
- SQL write scripts;
- `/execute`;
- Hangfire scheduling;
- enabling target writes;
- connection strings or secrets in repo/docs/logs.

