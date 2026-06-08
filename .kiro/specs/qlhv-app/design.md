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
