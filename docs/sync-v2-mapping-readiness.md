# Task 5 B3R - V2 HocVien mapping readiness

Tai lieu nay la checklist truoc local dry-run cho dong bo HocVien tu `CSDT_V2_TEST` sang `QLHV_APP_TEST`.
Khong chay cac cau SQL trong task B3R. Moi cau SQL ben duoi chi la tham khao cho nguoi van hanh khi da xac
nhan dung moi truong local/test.

## Current code mapping

Current read path:

- `HocVienV2SqlBuilder` reads from `dbo.NguoiLX`, `dbo.NguoiLX_HoSo`, `dbo.KhoaHoc`, `dbo.DM_HangDT`, and `dbo.DM_DVHC`.
- `MaDK` comes from `NguoiLX.MaDK` / `NguoiLX_HoSo.MaDK`.
- `HoTen` comes from `NguoiLX.HoVaTen`.
- `NgaySinh` uses `TRY_CONVERT(date, nlx.NgaySinh, 112)`.
- `GioiTinh` comes from `NguoiLX.GioiTinh` and is preserved as raw value during sync.
- `SoCCCD` comes from `NguoiLX.SoCMT`, trim/preserve only.
- `DiaChiThuongTru` prefers `DM_DVHC.TenDayDu` joined by `NoiTT_MaDVQL + NoiTT_MaDVHC = DM_DVHC.MaDV`, with mapper fallback to `NguoiLX.NoiTT`.
- `SoGPLXDaCo` comes from `NguoiLX_HoSo.SoGPLXDaCo`.
- `HangGPLXDaCo` comes from `NguoiLX_HoSo.HangGPLXDaCo`.
- `NguoiNhanHoSo` comes from `NguoiLX_HoSo.NguoiNhanHSo`.
- `MaKhoa` comes from `NguoiLX_HoSo.MaKhoaHoc`.
- `TenKhoa` comes from `KhoaHoc.TenKH`.
- `MaHangDT` comes from `NguoiLX_HoSo.HangDaoTao`.
- `HangGPLXHoc` comes from `DM_HangDT.TenHangDT`.

Current write path:

- `QlhvHocVienTargetRepository` stages and merges `MaHangDT` and `HangGPLXHoc`.
- `V2RowHash` includes `MaHangDT` and `HangGPLXHoc`.
- No physical delete exists in the merge statement.
- Writes remain guarded by `SyncExecution:EnableTargetWrites`.

## Schema readiness notes

- `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql` contains `dbo.App_HocVien.MaHangDT NVARCHAR(20) NULL`.
- The same schema file contains `IX_App_HocVien_MaHangDT`.
- `database/reference/V2_schema_full.sql` contains:
  - `NguoiLX.MaDK`, `HoVaTen`, `NgaySinh`, `GioiTinh`, `SoCMT`, `NoiTT`, `NoiTT_MaDVQL`, `NoiTT_MaDVHC`, `TrangThai`;
  - `NguoiLX_HoSo.MaDK`, `MaKhoaHoc`, `HangDaoTao`, `SoGPLXDaCo`, `HangGPLXDaCo`, `NguoiNhanHSo`, `TrangThai`;
  - `KhoaHoc.MaKH`, `TenKH`, `TrangThai`;
  - `DM_HangDT.MaHangDT`, `TenHangDT`, `TrangThai`;
  - `DM_DVHC.MaDV`, `TenDayDu`, `TrangThai`.
- The requested file `database/patches/20260610_add_mahangdt_app_hocvien.sql` is not present in the current repository state. Before any local execute test, confirm whether the target test database was created from the latest schema or has an equivalent already-applied patch.

## Questions to confirm before dry-run

Confirm from `CSDT_V2_TEST` only:

- `GioiTinh`: actual values and display mapping, for example `M/F`, `1/0`, or another convention.
- `SoCMT`: ratio of valid 12-digit CCCD values versus legacy 9-digit CMND or other non-12-digit values.
- `TrangThai`: meaning of `NguoiLX.TrangThai`, `NguoiLX_HoSo.TrangThai`, and `KhoaHoc.TrangThai`; current code does not filter by status.
- `NgaySinh`: count and examples of invalid `yyyyMMdd` values where `TRY_CONVERT(date, ..., 112)` returns null.
- `DM_DVHC`: coverage of the address join by `NoiTT_MaDVQL + NoiTT_MaDVHC`.
- `DM_HangDT`: coverage of the training-rank join by `NguoiLX_HoSo.HangDaoTao`.
- Duplicate effective rows: confirm whether the test data can return more than one row for the same `MaDK` after current joins. Schema review shows `NguoiLX_HoSo` has primary key `MaDK`, but test data should still be checked.

## Reference SQL for later local/test review only

Run these only against `CSDT_V2_TEST` or another approved disposable test database. Never run against production.

```sql
-- 1. GioiTinh raw values
SELECT nlx.GioiTinh, COUNT(1) AS Total
FROM dbo.NguoiLX AS nlx
GROUP BY nlx.GioiTinh
ORDER BY Total DESC;

-- 2. SoCMT / CCCD / CMND shape
SELECT
    CASE
        WHEN LTRIM(RTRIM(ISNULL(nlx.SoCMT, ''))) = '' THEN 'blank'
        WHEN LEN(LTRIM(RTRIM(nlx.SoCMT))) = 12
             AND LTRIM(RTRIM(nlx.SoCMT)) NOT LIKE '%[^0-9]%' THEN '12-digit'
        WHEN LEN(LTRIM(RTRIM(nlx.SoCMT))) = 9
             AND LTRIM(RTRIM(nlx.SoCMT)) NOT LIKE '%[^0-9]%' THEN '9-digit'
        ELSE 'other'
    END AS IdentityShape,
    COUNT(1) AS Total
FROM dbo.NguoiLX AS nlx
GROUP BY
    CASE
        WHEN LTRIM(RTRIM(ISNULL(nlx.SoCMT, ''))) = '' THEN 'blank'
        WHEN LEN(LTRIM(RTRIM(nlx.SoCMT))) = 12
             AND LTRIM(RTRIM(nlx.SoCMT)) NOT LIKE '%[^0-9]%' THEN '12-digit'
        WHEN LEN(LTRIM(RTRIM(nlx.SoCMT))) = 9
             AND LTRIM(RTRIM(nlx.SoCMT)) NOT LIKE '%[^0-9]%' THEN '9-digit'
        ELSE 'other'
    END;

-- 3. TrangThai values
SELECT 'NguoiLX' AS TableName, CAST(TrangThai AS varchar(10)) AS TrangThai, COUNT(1) AS Total
FROM dbo.NguoiLX
GROUP BY TrangThai
UNION ALL
SELECT 'NguoiLX_HoSo', CAST(TrangThai AS varchar(10)), COUNT(1)
FROM dbo.NguoiLX_HoSo
GROUP BY TrangThai
UNION ALL
SELECT 'KhoaHoc', CAST(TrangThai AS varchar(10)), COUNT(1)
FROM dbo.KhoaHoc
GROUP BY TrangThai;

-- 4. Invalid NgaySinh for yyyyMMdd conversion
SELECT COUNT(1) AS InvalidNgaySinh
FROM dbo.NguoiLX AS nlx
WHERE TRY_CONVERT(date, nlx.NgaySinh, 112) IS NULL;

-- 5. DM_DVHC join coverage
SELECT
    CASE WHEN dvhc.MaDV IS NULL THEN 'missing-dvhc' ELSE 'matched-dvhc' END AS JoinStatus,
    COUNT(1) AS Total
FROM dbo.NguoiLX AS nlx
LEFT JOIN dbo.DM_DVHC AS dvhc
    ON dvhc.MaDV = LTRIM(RTRIM(nlx.NoiTT_MaDVQL)) + LTRIM(RTRIM(nlx.NoiTT_MaDVHC))
GROUP BY CASE WHEN dvhc.MaDV IS NULL THEN 'missing-dvhc' ELSE 'matched-dvhc' END;

-- 6. DM_HangDT join coverage
SELECT
    CASE WHEN hdt.MaHangDT IS NULL THEN 'missing-hangdt' ELSE 'matched-hangdt' END AS JoinStatus,
    COUNT(1) AS Total
FROM dbo.NguoiLX_HoSo AS hs
LEFT JOIN dbo.DM_HangDT AS hdt
    ON hdt.MaHangDT = hs.HangDaoTao
GROUP BY CASE WHEN hdt.MaHangDT IS NULL THEN 'missing-hangdt' ELSE 'matched-hangdt' END;

-- 7. Duplicate MaDK after the current joins
SELECT nlx.MaDK, COUNT(1) AS Total
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
LEFT JOIN dbo.KhoaHoc AS kh ON kh.MaKH = hs.MaKhoaHoc
LEFT JOIN dbo.DM_HangDT AS hdt ON hdt.MaHangDT = hs.HangDaoTao
LEFT JOIN dbo.DM_DVHC AS dvhc
    ON dvhc.MaDV = LTRIM(RTRIM(nlx.NoiTT_MaDVQL)) + LTRIM(RTRIM(nlx.NoiTT_MaDVHC))
GROUP BY nlx.MaDK
HAVING COUNT(1) > 1
ORDER BY Total DESC, nlx.MaDK;
```
