# Task 5 B3W26-B3W28 - Moto old/new sync implementation plan

## Scope and safety

This is an implementation plan only.

Do not run SQL, sync, or `/execute` until the owner approves an exact TEST-only scenario.

No production database is approved.

## Phase 1 - Read-only schema/table comparison

### Goal

Compare `CSDT_V1` and `CSDT_V2` schemas for the candidate Moto old/new tables before proposing any data movement.

Candidate tables:

- `KhoaHoc`
- `BaoCaoI`
- `NguoiLX`
- `NguoiLX_HoSo`
- `NguoiLX_GPLX`
- `NguoiLXHS_GiayTo`
- `KhoaHoc_GiaoVien`
- `LichHoc`
- `KhoaHoc_XeTap` if needed

### Files likely touched

Docs:

- `docs/task5-b3w26-b3w28-moto-old-new-sync-design.md`
- new schema comparison report, for example:
  `docs/task5-b3w29-moto-v1-v2-schema-comparison.md`

Possible backend code later:

- new read-only diagnostics DTOs under `server/QLHV.Application/Sync`
- new read-only repository under `server/QLHV.Infrastructure/Sync`
- controller route under `DongBoV2Controller` or a future Moto sync controller

Do not add write code in this phase.

### Verification

- Build only if backend code is added.
- No SQL execution through Codex.
- If owner runs read-only SQL manually, capture only aggregate results.

### Stop conditions

- Requested source/target DB is not TEST/local.
- Any schema mismatch affects key fields.
- Any table is missing in either source and the missing table is required.
- Any diagnostic would expose secrets or sensitive raw values in an unsafe way.

## Phase 2 - Read-only row count, duplicate, and key diagnostics

### Goal

Produce safe aggregate diagnostics before mapping:

- row counts per table in V1 and V2;
- missing parent rows;
- duplicate keys;
- same CCCD with different `MaDK`;
- same `MaDK` with different personal fields;
- course counts by `MaKH`;
- optional teacher/schedule/vehicle counts.

### Files likely touched

Docs:

- diagnostics checklist/report for Moto V1/V2.

Possible backend code:

- read-only diagnostics endpoint such as:
  ```http
  GET /api/dong-bo-v2/moto/v1-v2-diagnostics
  ```
- DTOs for table counts, duplicate counts, conflict counts.
- repository methods that only read `CSDT_V1` and `CSDT_V2`.

### Verification

- `dotnet build QLHV.sln -c Release` if code is added.
- focused tests for SQL builder/diagnostics if code is added.
- no SQL integration test by default unless opt-in exists.

### Stop conditions

- CCCD conflict count is non-zero and not reviewed.
- Duplicate key count is non-zero for required keys.
- Parent/child mismatch exists in high-risk tables.
- Row counts are unexpectedly large or inconsistent.
- V2 -> V1 write is requested without a concrete business use case.

## Phase 3 - Mapping proposal for lowest-risk tables

### Goal

Propose mappings only for fields with known equivalent meaning.

Recommended low-to-medium risk first pass:

1. `KhoaHoc`
2. `BaoCaoI`
3. optionally `KhoaHoc_GiaoVien`
4. optionally `LichHoc`

High-risk learner tables should remain read-only until conflict rules are approved:

- `NguoiLX`
- `NguoiLX_HoSo`
- `NguoiLX_GPLX`
- `NguoiLXHS_GiayTo`

### Files likely touched

Docs:

- table-by-table mapping document.
- human confirmation checklist.

Possible backend code:

- pure mapper classes for read-only planning;
- mapper tests;
- no target writes yet.

### Verification

- Unit tests for mapper behavior if code is added.
- Sample dry-run output reviewed by owner.

### Stop conditions

- Mapping requires destructive overwrite.
- Field meaning is not confirmed.
- Status fields such as `TrangThai`, `TT_XuLy`, `TT_Xuly` are not understood.
- File/path fields require physical file copy rules.
- CCCD/GPLX values would be changed automatically.

## Phase 4 - Dry-run plan endpoint

### Goal

Create a read-only plan endpoint that reports planned actions without writing either DB.

Potential route:

```http
GET /api/dong-bo-v2/moto/v1-v2-pre-execute-plan
```

The route name can change, but the endpoint must be explicit about:

- source: `CSDT_V1`;
- target: `CSDT_V2`;
- direction: V1 -> V2;
- table set;
- filters;
- dry-run status.

### Plan output

Return aggregate table-level plan first:

- table name;
- source rows;
- target rows;
- would insert;
- would update;
- would skip;
- conflict count;
- warning count;
- blocker count.

Only after aggregate plan is accepted should sanitized sample details be considered.

### Files likely touched

Backend:

- controller route;
- service interface;
- service implementation;
- source/target read repositories;
- SQL builder for read-only plan;
- DTOs;
- tests.

Docs:

- dry-run guide;
- plan response examples;
- stop-condition checklist.

### Verification

- `dotnet build QLHV.sln -c Release`
- focused Sync tests
- no SQL integration tests unless explicit opt-in
- manual owner-run read-only call only after TEST config is confirmed

### Stop conditions

- Endpoint tries to write.
- Endpoint requires `EnableTargetWrites=true`.
- Endpoint hides blocker conflicts.
- Endpoint cannot identify source/target direction clearly.
- Planned updates are high and not explainable.

## Phase 5 - TEST-only write with manual approval

### Goal

Only after phases 1-4 are accepted, implement or use a guarded TEST-only write path for an exact scenario.

The initial write scenario should be as narrow as possible:

- one direction, likely V1 -> V2 first;
- one table set;
- one date/course filter if possible;
- insert-only if business accepts;
- no destructive update unless explicitly approved.

### Required guards

- TEST/local only.
- Backup/snapshot before run.
- `DryRun=false` only by explicit approval.
- Manual confirmation text.
- `EnableTargetWrites=true` only for the approved TEST run.
- transaction and rollback plan.
- audit summary.
- no Hangfire schedule.
- no production.

### Files likely touched

Backend:

- guarded execute request/response DTO;
- service guard checks;
- transaction-based repository methods;
- audit writer;
- tests for guards.

Docs:

- TEST runbook;
- rollback guide;
- acceptance checklist.

### Verification

- build;
- focused tests;
- owner manually reviews pre-execute plan;
- owner confirms backup/snapshot;
- execute remains blocked until the final explicit prompt.

### Stop conditions

- No backup/snapshot.
- Production-like connection detected.
- Any blocker remains unresolved.
- Expected counts are not documented.
- Operator cannot explain why write is needed.

## Suggested next Codex task

Recommended next task:

```text
Task B3W29: read-only schema comparison design for Moto CSDT_V1 vs CSDT_V2.
```

Scope:

- docs + optional read-only SQL reference snippets only;
- compare the candidate tables listed in B3W26-B3W28;
- no SQL execution;
- no write code;
- no sync;
- no `/execute`.

Suggested deliverables:

- `docs/task5-b3w29-moto-v1-v2-schema-comparison.md`
- table/column/key comparison matrix
- missing/extra field list
- fields safe to map
- fields requiring owner confirmation
- next diagnostic plan

## Final stop statement

Do not run any SQL, write, sync, or execute until the owner approves an exact TEST-only scenario.

Current DATA_V2 HocVien state does not need write sync:

- read `1970`;
- insert `0`;
- update `0`;
- skip `1970`;
- errors `0`;
- warnings `1`.

The next useful work is Moto old/new read-only analysis, not DATA_V2 HocVien execute.

