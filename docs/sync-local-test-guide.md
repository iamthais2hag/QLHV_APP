# Sync V2 local test guide

Guide nay danh cho Task 5 Phase B3C: test an toan va chuan bi chay thu local cho luong dong bo
`CSDT_V2` HocVien -> `QLHV_APP.dbo.App_HocVien`.

Khong dung production database. Khong commit connection string that, username, password, token, hoac secret.

## Required local/test databases

Dung database test rieng, toi thieu:

- `QLHV_APP_TEST`: target test, cung schema voi QLHV_APP.
- `CSDT_V2_TEST`: source test/read-only, co du bang `NguoiLX`, `NguoiLX_HoSo`, `KhoaHoc`.

Tuy chon:

- `HANGFIRE_TEST`: chi can khi Phase B4 bat dau test scheduling. Phase B3C/B3B khong schedule recurring job.

Khong tro cac bien cau hinh local vao production SQL Server.

## Configure local secrets or environment variables

Dung ASP.NET Core user-secrets hoac environment variables tren may local. Khong dien gia tri that vao
`appsettings*.json`.

User-secrets, chay trong thu muc `server/QLHV.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:QLHV_APP" "<local QLHV_APP_TEST connection string>"
dotnet user-secrets set "ConnectionStrings:CSDT_V2" "<local CSDT_V2_TEST connection string>"
dotnet user-secrets set "SyncExecution:EnableTargetWrites" "false"
dotnet user-secrets set "SyncExecution:RequireManualConfirmation" "true"
dotnet user-secrets set "SyncExecution:AllowHangfireSchedule" "false"
dotnet user-secrets set "SyncExecution:ConfirmationPhrase" "EXECUTE_DONG_BO_V2_HOC_VIEN"
```

Environment variable names tuong ung:

```text
ConnectionStrings__QLHV_APP
ConnectionStrings__CSDT_V2
SyncExecution__EnableTargetWrites=false
SyncExecution__RequireManualConfirmation=true
SyncExecution__AllowHangfireSchedule=false
SyncExecution__ConfirmationPhrase=EXECUTE_DONG_BO_V2_HOC_VIEN
```

## Keep writes disabled by default

Mac dinh phai giu:

```text
SyncExecution:EnableTargetWrites=false
SyncExecution:RequireManualConfirmation=true
SyncExecution:AllowHangfireSchedule=false
```

Khi `EnableTargetWrites=false`, endpoint execute phai reject va khong goi repository ghi target.

## Run dry-run first

Sau khi cau hinh local/test database, chay API local va goi dry-run truoc:

```text
POST /api/dong-bo-v2/hoc-vien/dry-run
```

Dry-run chi duoc doc/can ke hoach. Dry-run khong:

- ghi `dbo.App_HocVien`
- ghi `dbo.App_DongBoLog`
- goi `SqlBulkCopy`
- goi `MERGE`
- schedule Hangfire

Kiem tra response chi co summary an toan, khong co password, username kem password, token, hoac full connection string.

## Manual execute body for later local write test

Khong goi endpoint execute trong Phase B3C. Khi duoc phep test local write o phase sau, body xac nhan bat buoc la:

```json
{
  "confirmTargetWrites": true,
  "confirmationText": "EXECUTE_DONG_BO_V2_HOC_VIEN",
  "maxRows": 10
}
```

Endpoint execute van phai reject neu `SyncExecution:EnableTargetWrites=false`.

## SQL integration tests

Normal build/test khong can SQL Server:

```powershell
cd server
dotnet build QLHV.sln
dotnet test QLHV.sln
```

Bat ky integration test nao can SQL Server phai skip mac dinh. Chi opt-in bang bien moi truong:

```text
QLHV_RUN_SQL_INTEGRATION_TESTS=true
```

Neu bien nay khong bang `true`, SQL integration tests phai bi skip va khong yeu cau DB config.

## Rollback and failure checklist

Truoc lan test write local dau tien:

- Xac nhan database la `QLHV_APP_TEST` va `CSDT_V2_TEST`, khong phai production.
- Backup/snapshot database test neu can so sanh.
- Bat dau voi `maxRows` nho, vi du 10.
- Chay dry-run va luu summary an toan.
- Chi bat `SyncExecution:EnableTargetWrites=true` trong protected local config, khong commit.
- Sau execute local, kiem tra `App_DongBoLog` chi co count/status/error code an toan.
- Kiem tra khong co delete vat ly tren `App_HocVien`.
- Neu loi, xac nhan transaction rollback: khong co batch partial insert/update.
- Dat lai `SyncExecution:EnableTargetWrites=false` sau khi test xong.

Tuyet doi khong dung production database cho dry-run, execute, build verification, hoac integration test.
