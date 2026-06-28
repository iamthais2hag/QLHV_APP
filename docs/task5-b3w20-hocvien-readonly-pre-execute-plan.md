# Task 5 B3W20 - HocVien Read-Only Pre-Execute Plan

Date: 2026-06-28

Scope: prepare and verify a read-only pre-execute plan after the multi-source readiness work. This task does not run sync, does not call `/execute`, does not write the database, and does not enable `EnableTargetWrites`.

B3W22 concept correction: this plan uses `DATA_V2` as the current technical source identity for the Moto new 2026 test
data. It must not be read as a business decision to permanently duplicate learners by `DATA_V1`/`DATA_V2` in QLHV_APP.
The plan protects safe upsert/audit boundaries only.

## Endpoint / Command To Use

Read-only endpoint:

```http
GET /api/dong-bo-v2/hoc-vien/pre-execute-plan
```

Example local command, only after local TEST connections are configured:

```powershell
Invoke-RestMethod "http://localhost:5000/api/dong-bo-v2/hoc-vien/pre-execute-plan" |
  ConvertTo-Json -Depth 10
```

Related read-only checks to run before reviewing any execute:

```http
GET /api/dong-bo-v2/hoc-vien/config-check
GET /api/dong-bo-v2/hoc-vien/source-diagnostics
GET /api/dong-bo-v2/hoc-vien/target-diagnostics
GET /api/dong-bo-v2/hoc-vien/source-attribution-diagnostics
GET /api/dong-bo-v2/hoc-vien/pre-execute-plan
```

Do not call:

```http
POST /api/dong-bo-v2/hoc-vien/execute
```

## Plan Logic

The pre-execute plan is read-only:

1. Validates safe config names and connection readiness.
2. Reads CSDT_V2 source rows by page.
3. Maps source rows using the current `DATA_V2` source identity context.
4. Reads target `V2RowHash` values from `QLHV_APP.dbo.App_HocVien` by:
   ```text
   SourceProfileCode + SourceMaDK
   ```
5. Builds insert/update/skip counts without writing `App_HocVien` or `App_DongBoLog`.

The endpoint does not resolve or require target write guards. It is a planning/diagnostic endpoint only.

## Source Identity Rule

Current V2 plan identity:

| Field | Value |
| --- | --- |
| `SourceProfileCode` | `DATA_V2` |
| `SourceMaDK` | source `MaDK` |
| `SourceSystem` | `V2` |
| `SourceVersion` | `NULL` |

Target matching uses:

```text
SourceProfileCode + SourceMaDK
```

`MaDK` remains a business/display field and must not be used alone as sync identity.
`SourceProfileCode + SourceMaDK` is a technical import key, not a final business learner identity.

## Insert / Update / Skip Meaning

`Insert`:

- source row has a valid `MaDK`;
- no target row exists for `DATA_V2 + SourceMaDK`.

`Update`:

- target row exists for `DATA_V2 + SourceMaDK`;
- target `V2RowHash` is missing/empty or differs from the newly mapped source hash.

`Skip`:

- source row is skipped by data rules, for example missing valid `MaDK`; or
- target row exists for `DATA_V2 + SourceMaDK` and target `V2RowHash` equals the newly mapped source hash.

Warnings:

- data quality warnings from the mapper are returned in the plan;
- no raw CCCD/GPLX details are required for the plan response.

## Current Plan Readiness

After B3W19:

- TEST DB patch succeeded.
- Existing 1970 target rows are backfilled with `SourceProfileCode = DATA_V2`.
- `UQ_App_HocVien_MaDK` has been dropped on TEST.
- Source-scoped unique filtered index exists on TEST.
- Code write identity is `SourceProfileCode + SourceMaDK`.
- Pre-execute plan now reads target hashes by `SourceProfileCode + SourceMaDK`.

Expected first local read-only plan after no source changes:

- `SourceRowsRead` should align with CSDT_V2 source row count.
- `PlannedInsert` should be `0` if target already has all DATA_V2 rows.
- `PlannedUpdate` should be `0` if hashes match.
- `PlannedSkip` should equal readable unchanged rows, except rows skipped by source data rules.

If `PlannedUpdate` is unexpectedly high, stop and review mapping/hash differences before any execute.

## Remaining Blockers Before Execute

Execute/sync is still blocked until:

1. `config-check` confirms TEST/local only.
2. `source-diagnostics` is clean or warnings are accepted.
3. `target-diagnostics` confirms required source identity columns and indexes are present.
4. `source-attribution-diagnostics` remains consistent with DATA_V2 ownership for existing rows.
5. `pre-execute-plan` counts are reviewed and accepted.
6. `EnableTargetWrites` remains `false` until a human explicitly approves a local/test execute.
7. Authorization/role policy for execute is reviewed before any non-local environment.
8. A fresh backup/snapshot exists before any write test.
9. The Moto old/new data-flow meaning is reviewed so `DATA_V1`/`DATA_V2` technical source identity is not mistaken for
   intentional business duplication.

## Exact Next Manual Read-Only Checks

Run only against local/TEST configuration:

1. Confirm branch/build is current.
2. Start API locally using TEST/local user-secrets.
3. Run:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/dong-bo-v2/hoc-vien/config-check" |
     ConvertTo-Json -Depth 10
   ```
4. Verify:
   - `EnableTargetWrites = false`;
   - `DryRun = true` unless a later approved test changes it;
   - no production warning/placeholder issue.
5. Run:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/dong-bo-v2/hoc-vien/source-diagnostics" |
     ConvertTo-Json -Depth 10
   ```
6. Run:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/dong-bo-v2/hoc-vien/target-diagnostics" |
     ConvertTo-Json -Depth 10
   ```
7. Run:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/dong-bo-v2/hoc-vien/source-attribution-diagnostics" |
     ConvertTo-Json -Depth 10
   ```
8. Run:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/dong-bo-v2/hoc-vien/pre-execute-plan" |
     ConvertTo-Json -Depth 10
   ```
9. Save the output for review.
10. Do not call `/execute`.

