# Local dev hosts guide

Guide nay cau hinh ten mien local de chay QLHV_APP de nho hon `localhost`.

- Frontend: `http://qlhv.local:5173`
- Backend API: `http://api.qlhv.local:5000`

Khong dung production database. Khong commit username, password, token, secret, hoac connection string that.
Khong goi endpoint execute trong phase nay.

## 1. Cap nhat Windows hosts file

Mo Notepad bang quyen Administrator:

1. Bam Start.
2. Go `Notepad`.
3. Chon **Run as administrator**.
4. Mo file:

```text
C:\Windows\System32\drivers\etc\hosts
```

Them hai dong:

```text
127.0.0.1 qlhv.local
127.0.0.1 api.qlhv.local
```

Luu file.

Flush DNS cache:

```powershell
ipconfig /flushdns
```

Kiem tra nhanh:

```powershell
ping qlhv.local
ping api.qlhv.local
```

Ca hai ten phai resolve ve `127.0.0.1`.

## 2. Chay backend API bang api.qlhv.local:5000

Backend co launch profile `api.qlhv.local`.

```powershell
cd server\QLHV.Api
dotnet run --launch-profile "api.qlhv.local"
```

API se lang nghe tren:

```text
http://api.qlhv.local:5000
```

Profile nay la HTTP local dev. Khong bat buoc HTTPS khi chua co local certificate phu hop.

## 3. Chay frontend bang qlhv.local:5173

```powershell
cd client
npm install
npm run dev:local
```

Hoac:

```powershell
npm run dev -- --host qlhv.local
```

Mo:

```text
http://qlhv.local:5173
```

## 4. API base URL cho frontend

Mac dinh Vite proxy `/api` toi:

```text
http://api.qlhv.local:5000
```

Neu muon frontend goi API truc tiep thay vi proxy, tao file local khong commit:

```text
client/.env.local
```

Noi dung:

```text
VITE_API_BASE_URL=http://api.qlhv.local:5000/api
```

Khong dua production URL vao file nay. Khong commit `.env.local`.

## 5. Kiem tra cau hinh dong bo an toan

Chi goi config-check sau khi da cau hinh user-secrets/env vars cho local/test DB.
Endpoint nay khong tra connection string.

```powershell
Invoke-RestMethod -Method Get -Uri "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/config-check"
```

curl:

```powershell
curl.exe -X GET "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/config-check"
```

Ket qua an toan mong doi truoc dry-run:

```json
{
  "qlhvAppConfigured": true,
  "csdtV2Configured": true,
  "enableTargetWrites": false,
  "requireManualConfirmation": true,
  "allowHangfireSchedule": false
}
```

Bat buoc xac nhan `enableTargetWrites=false`.

## 6. Goi dry-run HocVien

Chi goi dry-run khi `QLHV_APP_TEST` va `CSDT_V2_TEST` da duoc cau hinh, va config-check van bao
`enableTargetWrites=false`.

```powershell
Invoke-RestMethod -Method Post -Uri "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/dry-run"
```

curl:

```powershell
curl.exe -X POST "http://api.qlhv.local:5000/api/dong-bo-v2/hoc-vien/dry-run"
```

Dry-run khong duoc ghi `App_HocVien`, khong ghi `App_DongBoLog`, khong schedule Hangfire.

## 7. Nhac lai gioi han an toan

- Khong dung production database.
- Khong chay SQL script tu dong.
- Khong goi `POST /api/dong-bo-v2/hoc-vien/execute`.
- Khong bat `SyncExecution:EnableTargetWrites`.
- Khong commit connection string that hoac file `.env.local`.
