# Sync data flow architecture

This document describes the planned multi-source data flow for QLHV_APP.
It is architecture documentation only. It does not create databases and does not approve execute.

## Planned profiles

The architecture uses these fixed connection profiles:

1. `CSDT_MOTO`
2. `CSDT_OTO`
3. `CSDT_MOTO_GPLX`
4. `CSDT_OTO_GPLX`
5. `DATA_V1`
6. `DATA_V2`
7. `QLHV_APP`

These profiles are configuration slots.
They may be unconfigured, configured, test failed, test succeeded, active, or inactive.
The application must not require all 7 profiles to exist before running.

Profile configuration and status should be managed through a QLHV_APP Admin menu, for example
`He thong / Cau hinh ket noi CSDT` or `Quan tri / Ket noi CSDL`.
The bootstrap `QLHV_APP` connection can still come from protected server configuration in the first stage, while the
other CSDT/DATA profiles are stored in `QLHV_APP` with encrypted passwords and masked UI display.

## Business data flow

Important concept correction from B3W22:

- Current `CSDT_V1` and `CSDT_V2` test databases are part of one Moto business transition flow, not two permanent
  parallel learner sources to duplicate in `QLHV_APP`.
- `DATA_V1` and `DATA_V2` are technical staging/import profiles.
- The diagram below shows connection-profile slots and possible staging routes. It is not final approval that Moto and
  Oto must map one-to-one to V1/V2.

```mermaid
flowchart LR
    CSDT_MOTO["CSDT_MOTO"]
    CSDT_MOTO_GPLX["CSDT_MOTO_GPLX"]
    CSDT_OTO["CSDT_OTO"]
    CSDT_OTO_GPLX["CSDT_OTO_GPLX"]
    DATA_V1["DATA_V1"]
    DATA_V2["DATA_V2"]
    QLHV_APP["QLHV_APP"]

    CSDT_MOTO <--> CSDT_MOTO_GPLX
    CSDT_OTO <--> CSDT_OTO_GPLX

    CSDT_MOTO -->|"current Moto old/new staging under review"| DATA_V1
    CSDT_MOTO -->|"current Moto old/new staging under review"| DATA_V2
    CSDT_OTO -.->|"future Oto staging design TBD"| DATA_V1
    CSDT_OTO -.->|"future Oto staging design TBD"| DATA_V2

    DATA_V1 -->|"import V1 scope"| QLHV_APP
    DATA_V2 -->|"import V2 scope"| QLHV_APP
```

The exact mapping between Moto/Oto operational systems and `DATA_V1`/`DATA_V2` staging profiles still needs business
confirmation.
The key architectural rule is that operational sources are first staged/restored into import profiles, then imported
into `QLHV_APP`.
Source-scoped import identity is a technical safety rule. It does not decide whether the business UI should display one
canonical learner or multiple source-specific records.

## Readiness by operation

Each operation checks only the profiles it needs.

| Operation | Required profiles | Writes? | Notes |
| --- | --- | --- | --- |
| Connection status screen | Any profile | No | Can show missing/unconfigured profiles safely. |
| Test one profile | Selected profile only | No app data write | May update audit/status later; no secrets returned. |
| Restore/prepare DATA_V1 source | Source profile plus `DATA_V1` | Outside current sync execute | Technical staging operation; not a business version label. |
| Restore/prepare DATA_V2 source | Source profile plus `DATA_V2` | Outside current sync execute | Technical staging operation; not a business version label. |
| Dry-run DATA_V1 import | `DATA_V1`, `QLHV_APP` | No | Reads source and target counts/mapping only. |
| Dry-run DATA_V2 import | `DATA_V2`, `QLHV_APP` | No | Reads source and target counts/mapping only. |
| Execute DATA_V1 import | `DATA_V1`, `QLHV_APP` | Yes | Requires guards, authorization, backup/snapshot. |
| Execute DATA_V2 import | `DATA_V2`, `QLHV_APP` | Yes | Requires guards, authorization, backup/snapshot. |

The menu should show all 7 fixed profiles at all times, but a failed or missing profile must block only operations
that need that profile.

## Import boundaries

Imports into QLHV_APP must be source-scoped:

- Importing `DATA_V1` must not delete, overwrite, or hide valid `DATA_V2` records unless a reviewed merge rule says so.
- Importing `DATA_V2` must not delete, overwrite, or hide valid `DATA_V1` records unless a reviewed merge rule says so.
- Target rows should preserve source identity, such as `SourceSystem`, `SourceProfile`, or another stable source marker.
- Change detection should include the source scope so two sources cannot accidentally compete for the same target row.

The planned connection profile storage is `dbo.App_CsdtConnectionProfile`.
Future import jobs should resolve their source through `SourceProfile` rather than through a hardcoded single-source
key. For example, a V2 import should be tied to `DATA_V2`, and a V1 import should be tied to `DATA_V1`.
This source profile must flow into diagnostics, pre-execute plan, execution summary, audit records, and target merge
rules.

## Current Task 5 relationship

Current Task 5 work has a single-source V2 path:

- config-check
- dry-run
- source diagnostics
- target diagnostics
- guarded execute path
- WIP pre-execute plan on a separate branch

That work remains useful as a technical foundation, but it is not enough for the final multi-source import model.

Before any execute test beyond the current local single-source experiment, the team must decide:

- whether `CSDT_V2` becomes `DATA_V2`;
- whether V1/V2 imports share one pipeline with a profile selector;
- how target uniqueness works when V1 and V2 contain similar or overlapping learners;
- which fields are source-specific and which fields are globally merged.
- how the final business learner view avoids confusing technical source identity with learner duplication.
- how Oto differs from the current Moto test flow.

## No-go rules

- Do not run execute until multi-source strategy is approved.
- Do not assume all 7 databases exist.
- Do not require all 7 profiles for normal app startup.
- Do not expose secrets in status or diagnostics.
- Do not schedule Hangfire for multi-source import until profile gating and merge rules are approved.
