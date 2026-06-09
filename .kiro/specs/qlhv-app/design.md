# Design - QLHV_APP

## Backend

- ASP.NET Core
- EF Core Database-First cho CRUD
- Dapper cho lookup/report/query phức tạp
- SqlBulkCopy cho đồng bộ/import lớn
- Hangfire cho background job
- Polly cho retry
- IMemoryCache cho lookup

## Frontend

- React + TypeScript
- Font Be Vietnam Pro
- Sidebar trái, header trên
- Giao diện tiếng Việt
- Các bộ lọc dùng Autocomplete/Combobox debounce 300ms

## Database

Dùng file:

```text
database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql
```

## Dong bo V2 - Task 5 Phase A

- Phase A chi tao nen tang: dry-run endpoint, service structure, repository interfaces, Hangfire job structure, Polly retry structure.
- Khong lap lich Hangfire recurring job trong Phase A.
- Khong chay sync that, khong mo ket noi SQL de doc/ghi, khong ghi SQL Server.
- `QLHV_APP` bootstrap connection lay tu environment variables, user-secrets, hoac protected server config.
- `CSDT_V1` va `CSDT_V2` se duoc cau hinh ve sau trong Admin screen: **Cau hinh ket noi du lieu**.
- Vai tro duoc phep cau hinh ket noi nguon: `Admin`, `Giam doc trung tam`.
- Connection source phai ma hoa khi luu tru, password hien thi dang masked, co Test Connection, va audit log cho create/update/test/enable/disable.
- Khong tra ve hoac ghi log password, token, username kem password, hoac full connection string.
