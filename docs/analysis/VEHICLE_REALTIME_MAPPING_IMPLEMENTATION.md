# Vehicle realtime mapping implementation

## Trạng thái

Phần ingestion xe tập lái đã được triển khai ở mức source code, mapping,
Change Tracking reader, sealed planner, target writer, migration và test cô lập.
Không migration nào trong tài liệu này đã được chạy. Không có backfill, checkpoint
xe hoặc hosted worker xe nào được bật.

Trạng thái chính xác: **IMPLEMENTED — NOT MIGRATED / NOT ACTIVATED**.

## Bằng chứng source mới ngày 2026-07-30

Kiểm tra production chỉ đọc đã xác minh:

| Profile | Database identity | Object | Key | Schema | CT của `XeTap` | Dòng quan sát |
|---|---|---|---|---|---:|---:|
| `CSDT_OTO` | `CSDL_OTO`, id `9`, GUID `9A8B9BC1-18F3-4823-8123-3DC197A9D540` | `dbo.XeTap`, `USER_TABLE` | PK `BienSoXe varchar(10) NOT NULL` | 34 cột, fingerprint `1dddde7ce4bbc97a7f7b18d1cc7efc7f761af1b7195be2bbaacb2562266232e8` | OFF | 29 |
| `CSDT_MOTO` | `CSDL_MOTO`, id `8`, GUID `308BDDA8-80F3-4ACB-9836-578D80A9E98E` | `dbo.XeTap`, `USER_TABLE` | PK `BienSoXe varchar(10) NOT NULL` | 34 cột, cùng fingerprint OTO | OFF | 0 |

Hai source đều dùng collation `SQL_Latin1_General_CP1_CI_AS`, Snapshot
Isolation ON, RCSI OFF. Không có view xe thay thế và không có cột `MaXe` hoặc
`SourceMaXe`. Vì vậy:

- business/source identity đã chứng minh là `BienSoXe`;
- identity trong QLHV là `(SourceProfileCode, exact trimmed BienSoXe)`;
- nếu API/UI cần nhãn “mã xe”, biển số chỉ được dùng làm display alias; không tạo
  một identity nguồn giả;
- count `29` và `0` chỉ là quan sát, không phải deployment/checkpoint guard.

Tại cùng thời điểm, `QLHV_APP` có `App_XeTap=0`; chưa có
`SourceProfileCode`, checkpoint, event hoặc manual-review table của xe. CT version
toàn database vẫn là OTO `25`, MOTO `0`; `dbo.XeTap` ở cả hai source vẫn chưa
được tracking. `@@TRANCOUNT=0` sau kiểm tra.

Target metadata thực tế đã được đọc trực tiếp trước khi viết migration:
`App_XeTap.XeTapId` là `bigint IDENTITY NOT NULL`, `BienSoXe` là
`nvarchar(20) NOT NULL` (`max_length=40` byte), và `RowVersion` là
`timestamp/rowversion(8) NOT NULL`. Migration có exact precondition cho ba contract
này và pin database GUID production; không suy ra type từ proposal.

## Public contract

Các type công khai nằm trong
`QLHV.Application.Sync.VehicleRealtime`:

- `VehicleRealtimeRouteCatalog`: exact live profile/database/GUID/MaCSDT;
- `VehicleSourceIdentity`: `(SourceProfileCode, SourceBienSoXe)`;
- `VehicleSourceRow`: read model đủ 34 cột nguồn;
- `VehicleSourceWriteModel`: chỉ chứa field source-owned;
- `VehicleMappingResult` và `VehicleRealtimePlanner`;
- `VehicleRealtimeCheckpoint`, `VehicleSourceBatch`,
  `VehicleRealtimeSealedPlan`, `VehicleRealtimeCycleResult`;
- `IVehicleRealtimeSourceFeed` và `IVehicleRealtimeTargetStore`;
- `VehicleRealtimeCycleProcessor`.

Profile được allow-list chính xác:

| Profile | Database | GUID | `MaCSDT` |
|---|---|---|---|
| `CSDT_OTO` | `CSDL_OTO` | `9A8B9BC1-18F3-4823-8123-3DC197A9D540` | `66029` |
| `CSDT_MOTO` | `CSDL_MOTO` | `308BDDA8-80F3-4ACB-9836-578D80A9E98E` | `66030` |

Target runtime cũng pin `QLHV_APP` GUID
`9C44B304-8A84-4D0D-9A82-19C7233FF6BB`.

## Mapping và ownership

| Source `dbo.XeTap` | Target source-owned | Quy tắc |
|---|---|---|
| `BienSoXe` | `BienSoXe`, `SourceBienSoXe`, `NormalizedBienSoXe` | exact trimmed PK được giữ; uppercase/bỏ khoảng trắng, `.`, `-` chỉ cho search/collision |
| profile cố định | `SourceProfileCode` | unique filtered `(SourceProfileCode,SourceBienSoXe)` |
| `MaCSDT`, `MaSoGTVT` | cùng tên | phải khớp route/profile |
| `SoDK` | `SoDK`, `NormalizedSoDK` | source-owned, collision guard |
| `SoHuu` | `SoHuu`, `XeCuaCoSoDaoTao`, `XeHopDong` | hai cờ sau derive nhất quán, không nhận input độc lập |
| nhãn/loại/mác/hãng/màu/năm | cột target tương ứng | trim NFC, không truncate |
| `SoDongCo`, `SoKhung` | giá trị và normalized key | collision guard, không âm thầm merge |
| giấy phép xe tập lái | các cột GPLX target | ngày hết hạn map từ `NgayHHGPXTL` |
| `HeThongPP` | `HeThongPhanhPhu` | source-owned |
| kiểm định | ngày cấp/hết hạn target | source không có số GCN; `SoGCNKiemDinh` được giữ nguyên |
| `BaoHiem`, `TuyenDuong`, `ChatLuong` | cột target tương ứng | nullable được giữ |
| `GhiChu` | `GhiChuV2` | không ghi `GhiChuNoiBo` |
| `TrangThai` | `SourceTrangThai`, `SourceLifecycle` | tách khỏi `TrangThai` nội bộ |
| source audit/XML | các cột `Source*` | không dùng làm audit tạo/sửa của QLHV |
| `DuongDanAnh` | `SourceImagePathHash` | chỉ hash path để phát hiện thay đổi; không copy absolute path vào `AnhRelativePath` |
| canonical mapped row | `SourceRowHash char(64)` | SHA-256 length-prefixed, gồm profile và exact source key |

Mapper fail-closed nếu partition sai hoặc source vượt độ dài target. Không field
nào bị truncate im lặng.

Realtime update không có các field QLHV-owned sau trong `SET`:

- `SoGCNKiemDinh`, `AnhRelativePath`;
- `GVQuanLyMa`, `GVQuanLyTen`;
- `GhiChuNoiBo`, `TrangThai`, `CanhBaoDuLieu`;
- legacy Auto Sync metadata `V2RowHash`, `LastSync*`;
- `IsDeleted`, delete audit, QLHV create/update audit và `RowVersion`;
- mọi bảng nhóm/phân công/người nhận hồ sơ.

Insert chỉ điền source-owned columns; default target tạo `CreatedAt`. Writer bị
`DENY DELETE` trên `App_XeTap`.

## Identity, collision và lifecycle

Secondary collision keys:

- normalized plate;
- normalized `SoDK`;
- normalized `SoKhung`;
- normalized `SoDongCo`.

Kết quả:

| Tình huống | Action |
|---|---|
| source identity mới, không collision | `INSERT_SOURCE_ROW` |
| exact identity, source hash/lifecycle đổi | `UPDATE_SOURCE_OWNED_FIELDS` |
| exact hash + lifecycle | `NO_CHANGE` |
| source `TrangThai=0`, chưa được tham chiếu | `MARK_SOURCE_INACTIVE` |
| source delete/missing, chưa được tham chiếu | `MARK_SOURCE_MISSING` |
| source inactive/missing nhưng đang được dùng | `MANUAL_REVIEW`, không mutate xe |
| cùng normalized plate giữa OTO/MOTO | `CROSS_PROFILE_PLATE_COLLISION`, không merge |
| collision đăng ký/khung/máy | `MANUAL_REVIEW`, không insert/update |
| target soft-deleted/manual hold | `MANUAL_REVIEW` |
| target identity trùng nhiều dòng | fail-closed/manual review |

Không action nào hard-delete. `SOURCE_MISSING` chỉ là lifecycle nguồn. Nếu xe có
current assignment, group default hoặc source course-vehicle relation, writer chỉ
ghi control-plane review event và giữ nguyên target.

## Realtime/continuous-entry protocol

Reader dùng Change Tracking riêng của `dbo.XeTap`, không dùng Auto Sync:

1. Resolve exact live profile và xác minh database name/GUID.
2. Mở source transaction `SNAPSHOT`.
3. Xác minh exact 34-column schema, PK, CT và minimum-valid version.
4. Seal `CHANGE_TRACKING_CURRENT_VERSION()`.
5. Đọc **toàn bộ key của đúng một CT commit version**. Không dùng `TOP` làm mất
   các row cùng version.
6. Map và lập plan theo source identity; không dùng count/row order.
7. Revalidate chỉ các `BienSoXe` trong plan. Commit học viên/khóa khác không làm
   plan xe thất bại; thay đổi mới trên chính key xe làm cycle retry mà không đổi
   checkpoint.
8. Target transaction `SERIALIZABLE` lấy global lock
   `QLHV:RT03:VEHICLE:GLOBAL`, CAS checkpoint `RowVersion`, khóa/re-read target,
   assignment và collision evidence.
9. Apply source-owned allow-list, assert affected row, ghi event/manual review,
   rồi advance checkpoint nguyên tử.

Một event được định danh idempotent bởi
`(SourceProfileCode,SourceCtVersion,SourceBienSoXe)`. Checkpoint xe độc lập với
checkpoint học viên. Reader có thể advance qua sealed global CT version khi
`XeTap` không có change; commit sau đó vẫn được đọc vì có version lớn hơn.

`AddVehicleRealtimeIngestionCore()` là composition hook riêng cho standalone
worker. Task này cố ý chưa gọi hook và chưa thêm hosted service: tránh khởi động
writer trước migration/baseline/operator activation. Khi tích hợp sau migration,
hook phải chạy trong worker đang giữ mutual-exclusion lock hiện hành với Auto
Sync; không host ở API.

## Migration artifacts chưa chạy

| File | Nội dung |
|---|---|
| `20260730_add_vehicle_realtime_mapping.sql` | source identity/hash/lifecycle trên `App_XeTap`; unique/index/check; checkpoint/event/manual-review; grants/deny |
| `20260730_enable_oto_vehicle_change_tracking.sql` | exact OTO GUID/table/PK guard, enable CT cho duy nhất `dbo.XeTap` |
| `20260730_enable_moto_vehicle_change_tracking.sql` | exact MOTO GUID/table/PK guard, enable CT cho duy nhất `dbo.XeTap` |
| `20260730_rollback_vehicle_realtime_mapping.sql` | chỉ drop khi chưa có source row/checkpoint/event/review; nếu có dữ liệu thì bắt buộc roll forward |

Target migration không insert/backfill `App_XeTap` và không tạo checkpoint row.
Source scripts không thay RCSI/Snapshot và không dùng `_BAK`/V1.

Migration thêm:

- `SourceProfileCode`, `SourceBienSoXe`, normalized plate/secondary keys;
- `MaCSDT`, `MaSoGTVT`, `SourceRowHash`, `SourceTrangThai`,
  `SourceLifecycle`;
- `SourceCtVersion`, last-seen/missing/review metadata;
- source audit, image-path hash và XML provenance;
- `App_XeTap_RealtimeCheckpoint`;
- `App_XeTap_RealtimeEvent`;
- `App_XeTap_RealtimeManualReview`.

Mọi FK manual-review/event đến `App_XeTap` là `ON DELETE NO ACTION`.

## Activation plan bắt buộc cho task sau

Không chạy các bước này trong implementation hiện tại:

1. Rehearse target migration và rollback trên restore/copy.
2. Apply target schema với exact target identity preflight.
3. Enable `dbo.XeTap` CT OTO rồi MOTO; xác minh minimum-valid/current version.
4. Lấy snapshot baseline theo từng profile, map toàn bộ source bằng cùng mapper,
   classify collision/manual review; không dùng count cố định.
5. Apply baseline OTO nguyên tử; sau khi coverage PASS mới tạo sealed checkpoint
   tại exact captured CT version. Lặp lại MOTO.
6. Re-read checkpoint/mapping/source schema fingerprints byte-identical.
7. Xác minh Auto Sync OFF, worker singleton/mutex, active run/slot/operation `0`.
8. Chỉ sau mọi guard mới compose hosted cycle trong standalone worker.

Nếu source CT retention hết hạn, schema/GUID/fingerprint đổi, checkpoint chưa có,
hoặc target/assignment đổi trong plan, code dừng fail-closed.

## Test/build

Focused command:

`dotnet test server/QLHV.Tests/QLHV.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~VehicleRealtime`

Kết quả: **20 PASS, 0 FAIL, 0 SKIP**.

Coverage gồm:

- exact source table/key, không tạo `MaXe`;
- mapping OTO/MOTO và profile-scoped identity;
- source hash/image-path safety;
- partition/length fail-closed;
- plate, registration, chassis và engine collision;
- OTO/MOTO collision;
- inactive/missing có và không có assignment;
- no-change;
- continuous unrelated source commits và same-key revalidation failure;
- writer ownership allow-list/RowVersion;
- migration exact database/table, no backfill/checkpoint;
- one-complete-CT-version reader.

Application và Infrastructure build thành công. Build chỉ còn warning package
`Magick.NET-Q16-AnyCPU 14.14.0` đã tồn tại trước phần vehicle.

### Rehearsal migration/rollback cô lập

Rehearsal đã chạy trên database dùng một lần, không phải `QLHV_APP`:

`& 'D:\QLHV_APP\ops\vehicle-realtime\Invoke-VehicleMigrationRehearsal.ps1' -ServerInstance 'CSDLTTTC'`

Kết quả cuối:

- database rehearsal:
  `QLHV_VEHICLE_REHEARSAL_20260730151425_25756`
  (`BED60263-8F3A-491A-AE67-C477B5B79453`);
- empty migration: `PASS`;
- empty rollback: `PASS`;
- populated rollback: `BLOCKED_AS_REQUIRED`;
- synthetic populated row/schema retained after blocked rollback: `PASS`;
- disposable database cleanup: `PASS`;
- số database còn lại với prefix `QLHV_VEHICLE_REHEARSAL_`: `0`.

Script kiểm tra bắt buộc target name có prefix rehearsal, xác minh target tồn tại
trước khi drop, dùng cùng một SQL connection qua mọi batch `GO`, và luôn cleanup
trong `finally`. Không migration nguồn/target production nào được chạy.

## Production safety sau implementation

Fresh verification chỉ đọc lúc `2026-07-30T15:16:08Z` xác nhận:

- `CSDL_OTO.dbo.XeTap` và `CSDL_MOTO.dbo.XeTap` CT vẫn OFF;
- target mapping columns `0/23`, control tables `0/3`;
- Auto Sync active run/slot/operation `0/0/0`;
- không checkpoint/config/CT/Snapshot/RCSI nào bị task thay đổi;
- realtime service hiện `Stopped`, persisted worker state
  `BLOCKED / RT03_UNSUPPORTED_DRIFT`; task không restart hoặc sửa production;
- hai protected development config vẫn SHA-256
  `12FF324C8A3F844360DF8A68AEAE1556C9F12A49249220811541586086A21E5E`.

Code vehicle và isolated rehearsal đã hoàn tất, nhưng production vehicle ingestion chưa và
không thể chạy trước khi realtime owner xử lý/approve drift, sau đó migration + sealed
baseline/operator activation được phê duyệt riêng.
