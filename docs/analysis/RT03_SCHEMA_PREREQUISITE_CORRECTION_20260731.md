# RT03 schema prerequisite correction and rehearsal — 2026-07-31

## Scope and production boundary

This correction task did not continue the production deployment. It did not run
production DDL/DML, copy runtime binaries, start/restart the worker, change
checkpoint/worker state, enable Auto Sync, change Change Tracking, run RT-02, apply
the assignment migration, or mutate Báo cáo I/XML Báo cáo I.

The failed deployment-attempt backup remains intact at:

`D:\QLHV_APP_RUNTIME\rollback\rt03-khoahoc-20260731-071102`

## Exact root cause

The failed target was:

- SQL Server/database/schema/table: `CSDLTTTC / QLHV_APP / dbo / App_KhoaHoc`;
- DDL: `CREATE NONCLUSTERED INDEX IX_App_KhoaHoc_SourceProfile_MaKhoa`;
- index type: nonunique, nonfiltered, nonclustered;
- keys: `(SourceProfileCode, MaKhoa)`;
- includes:
  `(SourceMaKhoaHoc, SourceHash, IsDeleted, TrangThaiNguon)`;
- computed columns on `dbo.App_KhoaHoc`: none;
- pre-existing dependent filtered index:
  `UX_App_KhoaHoc_SourceIdentity`, unique nonclustered, keys
  `(SourceProfileCode, SourceMaKhoaHoc)`, filter
  `SourceProfileCode IS NOT NULL AND SourceMaKhoaHoc IS NOT NULL`.

The original operator path was:

```powershell
sqlcmd -S CSDLTTTC -d QLHV_APP -E -b `
  -i .\20260730_rt03_support_khoahoc_business_identity.sql
```

The exact same integrated-authentication `sqlcmd` path, reconstructed read-only
after the failed transaction, produced these raw session values:

```text
ANSI_NULLS=1
ANSI_PADDING=1
ANSI_WARNINGS=1
ARITHABORT=0
CONCAT_NULL_YIELDS_NULL=1
QUOTED_IDENTIFIER=0
NUMERIC_ROUNDABORT=0
```

The failed session was not instrumented to print `SESSIONPROPERTY` before the
error. Therefore the values above are explicitly recorded as a reconstruction
using the same executable, authentication model and invocation path, not as output
captured from the already-ended session. The server returned:

```text
Msg 1934, Level 16, State 1
CREATE INDEX failed because the following SET options have incorrect settings:
'QUOTED_IDENTIFIER'. Verify that SET options are correct for use with indexed
views and/or indexes on computed columns and/or filtered indexes and/or query
notifications and/or XML data type methods and/or spatial index operations.
```

SQL Server evidence was version `16.0.4255.1` (major 16), compatibility level 160
and collation `SQL_Latin1_General_CP1_CI_AS`. The exact cause was the same-batch
`QUOTED_IDENTIFIER=0` session used for DDL while the table already had a filtered
index. This is a specific, reproducible SQL session-contract defect; no general
operator drift approval is used.

## Corrected SQL contract

Both prerequisite and rollback now set, in the same batch as their DDL:

```sql
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
```

Before opening the DDL transaction, each script asserts all seven required
`SESSIONPROPERTY` values with a distinct error code. The prerequisite codes are
527620–527626; rollback uses 527630–527636. Database name, ID and GUID are pinned
for production. Rehearsal mode is restricted to disposable database names with
the `QLHV_RT03_KHOAHOC_REHEARSAL_` prefix.

The scripts classify only two acceptable schema states:

- exact pre-migration baseline;
- exact migrated V2 state.

Any other definition returns error 527627/527637 with
`BLOCKED - RT03 SCHEMA DRIFT DETECTED`. There is no `IF NOT EXISTS` success masking.
The prerequisite asserts every post-DDL object before commit and supports a
rehearsal-only forced failure after index creation. The rollback reverses
dependencies in order, asserts the exact baseline, and fails closed on unsafe
cross-profile `MaKhoa` data (527611) or multi-row cycle history (527612).

## Execution-path rehearsal

The rehearsal used the same `sqlcmd -E -b -i` path and integrated sysadmin
permission model planned for production. It created a disposable database on
`CSDLTTTC` with SQL Server major 16, compatibility 160, required collation and the
exact relevant baseline.

Report:

`D:\QLHV_APP\.runlogs\rt03-khoahoc-schema-rehearsal-v2.json`

Result: `25/25 PASS`, `0 FAIL`, including:

- raw sqlcmd option evidence;
- negative `QUOTED_IDENTIFIER` preflight: 527625 and no partial DDL;
- wrong-definition schema drift: 527627 and no partial DDL;
- clean-baseline prerequisite and exact migrated index/constraint definitions;
- deterministic prerequisite second run;
- forced failure after index creation: 527629 and full rollback;
- unsafe course/history rollback blocks without partial DDL;
- safe rollback to exact baseline and deterministic second rollback;
- prerequisite reapply after rollback;
- final rollback to exact baseline;
- disposable database cleanup.

The corrected execution printed
`ANSI_NULLS/ANSI_PADDING/ANSI_WARNINGS/ARITHABORT/CONCAT_NULL_YIELDS_NULL/`
`QUOTED_IDENTIFIER/NUMERIC_ROUNDABORT = 1/1/1/1/1/1/0`.
The disposable database
`QLHV_RT03_KHOAHOC_REHEARSAL_20260731_080539_27868` was removed. The report records
`productionDatabaseTouched=false`, `productionServiceTouched=false` and
`cleanupPassed=true`.

## Tests and build

- focused schema/course/classifier tests: `46/46 PASS`;
- full RT03 focused suite: `75/75 PASS`;
- backend Release build: PASS, 0 errors;
- `git diff --check`: PASS apart from existing line-ending notices.

Runtime implementation code was not changed by this correction, so broad
regression was not repeated.

## V2 artifact

The superseded 20260730 package must not be used. The corrected package is:

`D:\QLHV_APP\handoff\RT03_KHOAHOC_BUSINESS_EVENT_20260731_V2`

The finalized artifact has 14 files totaling 6,186,091 bytes. `MANIFEST.sha256`
contains 13 stable non-self-referential entries and validates every other file.
Deployment hashes are:

- prerequisite:
  `39ADAF095A99677A96E87435EBCC2BA0E720A24AD11D8DC4AFBAAF44A9304CCD`;
- rollback:
  `B39ED7B3EBF7BAA4E809EBF757254B5E0863BB326CFE1B6D196C32A5B88C6F5E`;
- operator runbook:
  `ABCBBE62F53E0683A76059A56FD724B0057F9C3085EE530632769401E41064CA`;
- `QLHV.Application.dll`:
  `13A027A5331F80C6493C93BC0C4767635A6A4D65722E07A69B271B2DA164841D`;
- `QLHV.Infrastructure.dll`:
  `4F923BE76821EBFE573D168C11613BBEFF2B8406E4D8E08B338304D850396D0C`;
- `QLHV.Worker.dll`:
  `8158D983B6D6FAFFF964FF32FD4312D5534A7FB13D02105433C78028B504E8F1`.

The manifest file itself hashes to
`94C3E342ECD91B34C5AB4B9BAFD0FC92D48ABDB0E8560C8D2814B7E75C2E2ADC`.
A high-confidence text scan found zero password/connection-string/email/raw
learner-identity matches. No appsettings, production database, runtime backup or
assignment handoff pack is present.

The V2 operator runbook requires a fresh preflight and a new backup for a future
separately approved deployment attempt. It does not permit reuse of the earlier
backup as the only rollback source.

## Fresh production read-only baseline

The final correction audit observed:

- service Stopped, PID 0, exact executable path;
- deployed binary hashes unchanged from the pre-attempt baseline;
- exact database ID/GUID, SQL version, compatibility and collation;
- OTO checkpoint 25 and MOTO checkpoint 0;
- worker `BLOCKED / RT03_UNSUPPORTED_DRIFT`, cycle inactive;
- Auto Sync environment flags OFF and active run/operation counts 0/0;
- exact baseline global unique constraint, exact filtered source-identity index,
  V2 lookup index absent, old trusted cycle-history check exact;
- no global `MaKhoa` or source-identity duplicates;
- first OTO course event after checkpoint 25 remains CT26 INSERT with NULL mask;
- assignment production objects absent.

Production remains unchanged and requires a new explicit approval against the V2
manifest.

`READY FOR OPERATOR RE-APPROVAL — RT03 SCHEMA PREREQUISITE CORRECTED AND REHEARSED`
