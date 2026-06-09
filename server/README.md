# QLHV_APP - server (Backend)

.NET 8 backend solution following Clean Architecture.

## Solution: `QLHV.sln`

| Project | Type | Purpose |
| --- | --- | --- |
| `QLHV.Domain` | class library | Domain layer (no references) |
| `QLHV.Shared` | class library | Shared DTOs/utilities (no references) |
| `QLHV.Application` | class library | Application/use-case layer |
| `QLHV.Infrastructure` | class library | EF Core, Dapper, Hangfire, Polly wiring |
| `QLHV.Api` | ASP.NET Core Web API | HTTP host (startup project) |
| `QLHV.Worker` | Worker / Generic Host | Hangfire background jobs |

### Layering (project references)
- `QLHV.Domain` -> (none)
- `QLHV.Shared` -> (none)
- `QLHV.Application` -> Domain, Shared
- `QLHV.Infrastructure` -> Application, Domain, Shared
- `QLHV.Api` -> Application, Infrastructure, Shared
- `QLHV.Worker` -> Application, Infrastructure, Shared

## Build

```powershell
cd server
dotnet build QLHV.sln
```

## Run

```powershell
# API
dotnet run --project QLHV.Api

# Worker
dotnet run --project QLHV.Worker
```

## Configuration & secrets (IMPORTANT)

`appsettings.json` / `appsettings.Development.json` contain **placeholder
connection strings only** (e.g. `__DB_SERVER__`, `__DB_PASSWORD__`).

Real values MUST be provided via **user-secrets** or **environment
variables** and must never be committed:

```powershell
cd QLHV.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:QLHV_APP" "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
dotnet user-secrets set "ConnectionStrings:Hangfire" "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
```

For Task 5 Phase A, `CSDT_V1` and `CSDT_V2` remain placeholder keys only. Real source connections will later be configured from the Admin screen **Cau hinh ket noi du lieu**, restricted to `Admin` and `Giam doc trung tam`, encrypted at rest, masked in the UI, testable through a sanitized Test Connection action, and audited for create/update/test/enable/disable.

Do not commit real `CSDT_V1`, `CSDT_V2`, `QLHV_APP`, or Hangfire connection strings.
