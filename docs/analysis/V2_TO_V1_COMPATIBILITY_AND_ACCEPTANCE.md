# Khảo sát khả năng tiếp nhận dữ liệu V2 → V1

**Kết luận audit:** `COMPATIBILITY AUDIT COMPLETE`

**Ngày audit:** 2026-07-25  
**Chiều dữ liệu duy nhất:** V2 → V1  
**Phạm vi chính:** `CSDL_OTO_BAK`, `CSDL_MOTO_BAK`, `CSDL_OTO_V1_BAK`, `CSDL_MOTO_V1_BAK`  
**Đối chiếu drift:** `CSDL_OTO`, `CSDL_MOTO`, `CSDL_OTO_V1`, `CSDL_MOTO_V1`

Kết luận trên chỉ xác nhận audit có đủ quyền truy cập schema/dữ liệu để hoàn thành. Nó **không** có nghĩa luồng sync đã sẵn sàng chạy. Không có lệnh SQL ghi, sync, BAK preflight, reverse sync, stage, commit, merge hoặc push nào được thực hiện.

## 1. Executive summary

| Nhóm kết luận | Số domain |
| --- | ---: |
| `READY` | 0 |
| `READY_WITH_VALIDATION` | 4 |
| `READY_WITH_TRANSFORM` | 1 |
| `OPTIONAL` | 3 |
| `DISABLED` | 2 |
| `BLOCKED` | 2 |

Các kết quả chính:

- Metadata của V2 OTO và MOTO giống nhau trên phạm vi 24 bảng khảo sát; metadata của V1 OTO và MOTO cũng giống nhau. Không có drift giữa từng BAK và live tương ứng.
- Có 13 sai khác cột giữa schema V2 và V1 trong 12 domain forward/candidate. Hai sai khác độ dài của `DM_DonViGTVT` không làm mất dữ liệu hiện tại, nhưng schema gate hiện hành vẫn chặn do yêu cầu kiểu SQL bằng tuyệt đối.
- `GiaoVien` bị chặn bởi năm cột V2 được policy cho đọc/ghi nhưng V1 không có, cộng thêm các khác biệt kiểu, độ dài và nullability. Cả OTO/MOTO đang có 0 row nên chưa có bằng chứng mapping giá trị.
- `NguoiLX_HoSo` vừa schema/FK theo projection, nhưng bị chặn về an toàn vì default non-null của V1 làm sai tín hiệu lifecycle. Đây là P1 đã biết và không được sửa trong lượt audit này.
- Nguồn có 108 hồ sơ OTO và 5 hồ sơ MOTO. Planner hiện chỉ chấp nhận trạng thái đào tạo cho 68 OTO và 0 MOTO; 40 OTO và 5 MOTO sẽ bị loại bởi state gate.
- Cả hai nguồn `BaoCaoI` đều có 0 row và kết quả mô phỏng điều kiện SQL tạo BCII đều là 0 row. Vì vậy dữ liệu hiện tại chưa thể tạo BCII, ngay cả khi các domain tương thích khác được xử lý.
- `NguoiLX_GPLX` phải giữ `DISABLED`/preserve vì provenance chưa được chứng minh.
- Không có dữ liệu PII nào được xuất. Mọi kết quả dữ liệu là count, độ dài, enum hoặc trạng thái tổng hợp.

Top incompatibilities:

1. False lifecycle của `NguoiLX_HoSo` do `LanSH`, các cột kết quả và `KetQuaSH`/`CHON_IN_GPLX` có default hoặc giá trị sentinel non-null.
2. Policy `GiaoVien` đang cấp quyền forward cho các cột V1 không có và chưa quy định transformation cho các mã `nvarchar` → `varchar`.
3. `DM_DonViGTVT` tương thích theo dữ liệu hiện tại nhưng writer chặn `nvarchar(1000)` → `nvarchar(100)` trước khi áp dụng kiểm tra fit-by-value.
4. `KhoaHoc_XeTap.MaGV` nullable ở V2 nhưng NOT NULL và có FK ở V1; không có row thực tế để chứng minh mapping.
5. Nguồn không có `BaoCaoI`, do đó minimum data set cho BCII chưa tồn tại.

## 2. Forward domain inventory

### Forward catalog hiện hành

| Thứ tự | Domain | Nhóm | Bắt buộc theo catalog | Vai trò |
| ---: | --- | --- | --- | --- |
| 1 | `DM_DonViGTVT` | Reference | Có | Đơn vị/CSDT gốc của toàn bộ quan hệ |
| 2 | `GiaoVien` | Course | Không, optional | Nguồn lực đào tạo |
| 3 | `KhoaHoc` | Course | Có | Khóa đào tạo |
| 4 | `KhoaHoc_GiaoVien` | Course | Không, optional | Phân công giáo viên |
| 5 | `BaoCaoI` | Course | Có | Header BCI |
| 6 | `NguoiLX` | Learner | Có | Người học |
| 7 | `NguoiLX_HoSo` | Learner | Có | Hồ sơ đào tạo và liên kết BCI/khóa |
| 8 | `NguoiLX_GPLX` | Learner | Có trong catalog, ghi tự động tắt | Dữ liệu GPLX có provenance chưa rõ |
| 9 | `NguoiLXHS_GiayTo` | Learner | Có | Giấy tờ của hồ sơ |

### Candidate chưa có trong catalog

| Domain | Hiện trạng | Nhu cầu BCII core | Kết luận |
| --- | --- | --- | --- |
| `XeTap` | Schema hai phía giống nhau, 0 row | Không | `OPTIONAL`; chưa cấp quyền tự động |
| `KhoaHoc_XeTap` | `MaGV` khác nullability, V1 có thêm FK, 0 row | Không | `DISABLED` cho đến khi có mapping |
| `DM_LuuLuongDaoTao` | Schema giống nhau, 0 row | Không | `OPTIONAL`; chưa cấp quyền tự động |

### Lookup phụ thuộc

| Lookup | Hướng xử lý | Bằng chứng |
| --- | --- | --- |
| `DM_DVHC` | Validate-only, V1 đã có | 15.755/15.755 key, thiếu 0 |
| `DM_QuocTich` | Validate-only | 249/249 key, thiếu 0 |
| `DM_HangGPLX` | Validate-only | 26/26 key, thiếu 0 |
| `DM_HangDT` | Validate-only theo row đang dùng | V2 57, V1 54, thiếu 3 key tổng thể; khóa/hồ sơ hiện tại dùng key được phủ đầy đủ |
| `DM_LoaiHSo` | Validate-only | 3/3 key, thiếu 0 |
| `DM_HTCap` | Validate-only; đây là tên bảng thực tế của “DM_HTCapGPLX” trong yêu cầu | 9/9 key, thiếu 0 |
| `DM_GiayTo` | Validate key và nghĩa | 25/25 key; cùng key khác tên = 0 |
| `DM_TrangThai` | Validate-only | 19/19 key, thiếu 0 |

### Domain V1-owned, tuyệt đối không forward-sync

`BaoCaoII`, `KySH`, `DM_LyDoTCBC2`, `DM_DiemSatHach`, `DM_KetQuaSatHach`.

`DM_KetQuaSatHach` không tồn tại ở các schema khảo sát. `DM_LyDoTCBC2` có 14 row ở V2 và 7 row ở V1 nhưng đây không phải lý do cho phép đồng bộ; V1 vẫn là nguồn thẩm quyền. `DM_DiemSatHach` có 36 row mỗi phía và cũng chỉ được dùng làm bằng chứng preserve.

## 3. Schema difference summary

Hai profile OTO/MOTO dùng chung 13 sai khác sau:

| # | Domain.Column | V2 | V1 | Tác động |
| ---: | --- | --- | --- | --- |
| 1 | `DM_DonViGTVT.CoQuanQL` | `nvarchar(1000)` | `nvarchar(100)` | Giá trị hiện tại vừa; cần mapping authority và kiểm tra độ dài, không truncate. Writer hiện chặn do kiểu không bằng tuyệt đối. |
| 2 | `DM_DonViGTVT.TenDV` | `nvarchar(1000)` | `nvarchar(100)` | Max thực tế 52, vượt 100 = 0; tương thích theo giá trị nhưng writer hiện chặn metadata. |
| 3 | `GiaoVien.CacHangDaCo` | `nvarchar(500)` | Không có | Policy đang forward-read/write; làm block domain. |
| 4 | `GiaoVien.CacMonHoc` | `nvarchar(500)` | Không có | Policy đang forward-read/write; làm block domain. |
| 5 | `GiaoVien.GhiChu` | `nvarchar(500)` | `nvarchar(255)` | Cần length validation; hiện không có row để chứng minh. |
| 6 | `GiaoVien.HangGPLX` | `nvarchar(50) NULL` | `varchar(3) NOT NULL` | Cần code mapping, ASCII/lossless, non-null và lookup; hiện không có row. |
| 7 | `GiaoVien.HinhThuc_TuyenDung` | `nvarchar(50)` | `varchar(2)` | Không được đoán enum; cần mapping được duyệt. |
| 8 | `GiaoVien.LoaiGiaoVien` | `nvarchar(50)` | Không có | Policy đang forward-read/write; làm block domain. |
| 9 | `GiaoVien.LoaiHinh_DaoTao` | `nvarchar(500)` | `varchar(2)` | Không được đoán enum; cần mapping được duyệt. |
| 10 | `GiaoVien.NgayHHGPLX` | `datetime` | Không có | Policy đang forward-read/write; làm block domain. |
| 11 | `GiaoVien.NoiCapGCN` | `nvarchar(500)` | Không có | Policy đang forward-read/write; làm block domain. |
| 12 | `KhoaHoc_XeTap.MaGV` | `varchar(8) NULL` | `varchar(8) NOT NULL` | V1 còn có FK tới `GiaoVien`; 0 row nên không có bằng chứng giá trị. |
| 13 | `NguoiLX_HoSo.QDThucHanhHinh` | `float` | Không có | Source-only, toàn bộ giá trị hiện tại NULL; policy đã phân loại non-writable nên skip đúng. |

Các thuộc tính còn lại đã được đối chiếu theo tên, kiểu, length, precision/scale, nullability, default, identity, computed và collation. Không phát hiện khác biệt metadata OTO/MOTO hoặc BAK/live trong phạm vi. PK/UQ giữa V2/V1 trùng nhau, gồm các khóa đáng chú ý:

- `BaoCaoI`: PK `MaBCI`, UQ `SoBaoCao`.
- `NguoiLX_HoSo`: PK/UQ theo `MaDK`.
- `NguoiLXHS_GiayTo`: PK `(MaGT, MaDK)`.
- `KhoaHoc`, `GiaoVien`: PK đơn và UQ quan hệ như schema hiện hành.
- `DM_LuuLuongDaoTao`: PK `(MaCSDT, HangGPLX)`.

Không phát hiện CHECK constraint hoặc trigger trên 17 bảng forward/lookup/V1-owned đã kiểm tra. Không có FK disabled; hai FK địa bàn composite của `NguoiLX` đang ở trạng thái untrusted. V1 bổ sung FK `GiaoVien.HangGPLX → DM_HangGPLX` và `KhoaHoc_XeTap.MaGV → GiaoVien`. Các cascade delete liên quan `KhoaHoc → BaoCaoI/NguoiLX_HoSo` và hồ sơ → giấy tờ càng củng cố quy tắc không DELETE; audit không thực hiện delete.

## 4. Value-profile summary

| Domain/profile | Total | Null/blank hoặc format rủi ro | Max/overflow | Enum/trạng thái | Khóa/FK/collision |
| --- | ---: | --- | --- | --- | --- |
| `DM_DonViGTVT` OTO route | 1 | Required null = 0 | `TenDV` 52, `CoQuanQL` 47, vượt 100 = 0 | `TrangThai` được policy copy | Target collision = 0 |
| `DM_DonViGTVT` MOTO route | 1 | Required null = 0 | Như OTO | Như OTO | Target collision = 0 |
| `KhoaHoc` OTO | 3 | Required null = 0 | `MaKH` max 12, `TenKH` max 6, reject length = 0 | Hang GPLX `B1:1, B11:1, C1:1`; HangDT `B:1, B.01:1, C1:1`; state `1:3` | Duplicate/trailing/collision = 0; FK lookup được phủ |
| `KhoaHoc` MOTO | 2 | Required null = 0 | `MaKH` max 12, `TenKH` max 5, reject length = 0 | Hang GPLX `A1m:1, A2:1`; HangDT `A1m:1, Am:1`; state `1:2` | Duplicate/trailing/collision = 0; FK lookup được phủ |
| `BaoCaoI` OTO/MOTO | 0/0 | Không có mẫu | Không có mẫu | Không có mẫu | Target cũng 0/0 |
| `NguoiLX` OTO | 108 | Required null = 0; `NoiTT` và `NoiCT` blank 108 dù NOT NULL | Họ đệm 16, tên 6, họ tên 21, `HoVaTenIn` 21; reject length = 0 | Giới tính `F:43, M:65`; state `1:108` | Quốc tịch/địa bàn thiếu = 0; collision/duplicate định danh = 0 |
| `NguoiLX` MOTO | 5 | Required null = 0; `NoiTT` và `NoiCT` blank 5 | Họ đệm 11, tên 5, họ tên 16, `HoVaTenIn` 16; reject length = 0 | Giới tính `M:5`; state `1:5` | Quốc tịch/địa bàn thiếu = 0; collision/duplicate định danh = 0 |
| `NguoiLX.NgaySinh` | 108/5 | Chuỗi ngày 8 ký tự; sai format = 0/0 | Không có out-of-range phát hiện | Không áp dụng | Không áp dụng |
| `NguoiLX_HoSo` OTO | 108 | Required null = 0; `SoGiayCNTN` null toàn bộ; `QDThucHanhHinh` null 108 | Reject schema length = 0 | `TT_XuLy 01:40, 03:68`; Hang `B1:40, B11:20, C1:48`; loại HS `2:108`; HT cấp `CM_VN:108` | Source parent thiếu = 0; target parent hiện thiếu do target business rỗng |
| `NguoiLX_HoSo` MOTO | 5 | Required null = 0; `SoGiayCNTN` null toàn bộ; `QDThucHanhHinh` null 5 | Reject schema length = 0 | `TT_XuLy 01:5`; Hang `A1m:1, A2:4`; loại HS `1:5`; HT cấp `CM_VN:5` | Source parent thiếu = 0; target parent hiện thiếu do target business rỗng |
| `NguoiLXHS_GiayTo` OTO/MOTO | 324/15 | Required null = 0 | `TenGT` max 82; reject length = 0 | `MaGT` lookup phủ đủ | Orphan = 0/0; collision = 0/0 |
| `GiaoVien`, `KhoaHoc_GiaoVien`, `XeTap`, `KhoaHoc_XeTap`, `DM_LuuLuongDaoTao`, `NguoiLX_GPLX` | 0/0 mỗi domain | Không có mẫu thực tế | Không có bằng chứng max/Unicode/conversion | Không được suy đoán enum | Không có collision thực tế |

Đối với chuyển `nvarchar` → `varchar`, chỉ `GiaoVien` có rủi ro thực tế trong schema khảo sát; vì nguồn đang rỗng, count Unicode/unsafe conversion bằng 0 chỉ là kết quả vacuous, không phải bằng chứng mapping an toàn. Không có phép truncate nào được chấp nhận.

Lifecycle profile của hồ sơ:

- Strong signal thực sự (`MaBC2`, `MaKySH`, SBD/quyết định hoặc state 11–19) = 0 ở cả OTO/MOTO.
- Tuy vậy, `LanSH`, `KetQua_LyThuyet`, `KetQua_Hinh`, `KetQua_Duong`, `KetQuaSHM`, `KetQuaSH`, `CHON_IN_GPLX` đều non-null trên 108/108 OTO và 5/5 MOTO.
- `CHON_IN_GPLX` có mã `1` trên toàn bộ nguồn; target default là `0`. Đây vẫn là cờ/sentinel, không phải bằng chứng lifecycle.
- Do đó `IS NOT NULL` trên các cột này không thể dùng để phát hiện lifecycle/conflict.

## 5. Required-target-column matrix

`V2 có?` được xác định từ schema; `Null hiện tại` chỉ có ý nghĩa khi có row. Hai trường hợp source nullable nhưng source rỗng không được xem là bằng chứng an toàn.

| Domain | Cột V1 NOT NULL không default/identity/computed | V2 có? | Null hiện tại | Nguồn/mapping | Kết luận cột required |
| --- | --- | ---: | --- | --- | --- |
| `DM_DonViGTVT` | `MaDV, MaDVQL, LoaiDV, TenDV` | Đủ | 0/0 | Copy + length validation | `INSERT_READY` theo required columns; writer gate vẫn block kiểu độ dài |
| `GiaoVien` | `MaGV, MaSoGTVT, MaCSDT, HoTenDem, TenGV, NgaySinh, SoCMT, HangGPLX, NgayCapGPLX` | Đủ | `HangGPLX` source nullable; 0 row | Cần non-null + code mapping | `NOT_INSERT_READY` |
| `KhoaHoc` | `MaKH, MaCSDT, MaSoGTVT, TenKH, HangGPLX` | Đủ | 0/0 | Copy + FK validation | `INSERT_READY` |
| `KhoaHoc_GiaoVien` | `MaKH, MaGV, TenGV, LoaiGV, GhiChu` | Đủ | 0 row | Copy + FK/identity guard | `INSERT_READY` theo schema, optional và chưa có mẫu |
| `BaoCaoI` | `MaBCI, MaCSDT, MaKH` | Đủ | 0 row | Copy + UQ/FK validation | `INSERT_READY` theo schema, chưa có mẫu |
| `NguoiLX` | `MaDK, DonViNhanHSo, HoDemNLX, TenNLX, HoVaTen, MaQuocTich, NgaySinh, NoiTT, NoiTT_MaDVHC, NoiTT_MaDVQL, NoiCT, NoiCT_MaDVHC, NoiCT_MaDVQL, SoCMT, GioiTinh, HoVaTenIn` | Đủ | Null = 0; hai trường địa chỉ blank | Copy + business completeness warning | `INSERT_READY` theo schema/FK |
| `NguoiLX_HoSo` | `MaDK, SoHoSo, MaCSDT, MaSoGTVT, MaDVNhanHSo, NgayNhanHSo, MaLoaiHs, TT_XuLy, HangGPLX` | Đủ | 0/0 | Copy + state/FK/lifecycle gate | `INSERT_READY` theo cột; domain vẫn `BLOCKED` vì lifecycle P1 |
| `NguoiLX_GPLX` | `MaDK, SoGPLX, HangGPLX, NoiCapGPLX, NgayCapGPLX, CoQuanQLGPLX, NgayTTGPLX, MaHTCap, NoiHocGPLX, NamHocGPLX, HoTenDem, TenNLX, HoVaTen, NgaySinh, MaQuocTich, SoCMT, GioiTinh` | Đủ | 0 row | Schema có nguồn nhưng provenance không rõ | Required columns đủ; policy `DISABLED`, không được insert |
| `NguoiLXHS_GiayTo` | `MaGT, MaDK, SoHoSo, TenGT` | Đủ | 0/0 | Copy + parent/lookup validation | `INSERT_READY` sau khi parent được nhận |
| `XeTap` | `BienSoXe, MaSoGTVT, MaCSDT` | Đủ | 0 row | Chưa có policy | `INSERT_READY` theo schema, domain `OPTIONAL` |
| `KhoaHoc_XeTap` | `MaKH, BienSoXe, MaGV, GhiChu` | Đủ | `MaGV` source nullable; 0 row | Cần non-null và parent `GiaoVien` | `NOT_INSERT_READY` |
| `DM_LuuLuongDaoTao` | `MaCSDT, HangGPLX` | Đủ | 0 row | Chưa có policy | `INSERT_READY` theo schema, domain `OPTIONAL` |

Không có cột target-only bắt buộc nào thiếu source trong 12 domain. Các cột audit/default do target tự tạo không được xem là dữ liệu V2.

## 6. Column acceptance matrix

Ma trận đầy đủ 392 entry nằm tại [V2_TO_V1_ACCEPTANCE_MATRIX.json](./V2_TO_V1_ACCEPTANCE_MATRIX.json). Mỗi entry có đủ `domain`, cột nguồn/đích, hai kiểu, ownership, disposition insert/update, transformation, validation, failure action và evidence. Không entry `UNKNOWN` nào được cấp quyền copy tự động.

Tổng hợp:

| Domain | Số cột | INSERT dispositions |
| --- | ---: | --- |
| `DM_DonViGTVT` | 25 | `COPY_EXACT:18`, `COPY_WITH_VALIDATION:1`, `TRANSFORM_REQUIRED:1`, `INSERT_ONLY:1`, `TARGET_DEFAULT:4` |
| `GiaoVien` | 42 | `COPY_EXACT:28`, `COPY_WITH_VALIDATION:1`, `TRANSFORM_REQUIRED:3`, `BLOCK_DOMAIN:5`, `INSERT_ONLY:1`, `TARGET_DEFAULT:4` |
| `KhoaHoc` | 29 | `COPY_EXACT:24`, `INSERT_ONLY:1`, `TARGET_DEFAULT:4` |
| `KhoaHoc_GiaoVien` | 20 | `COPY_EXACT:15`, `INSERT_ONLY:1`, `TARGET_DEFAULT:4` |
| `BaoCaoI` | 25 | `COPY_EXACT:20`, `INSERT_ONLY:1`, `TARGET_DEFAULT:4` |
| `NguoiLX` | 25 | `COPY_EXACT:20`, `INSERT_ONLY:1`, `TARGET_DEFAULT:4` |
| `NguoiLX_HoSo` | 109 | `COPY_EXACT:56`, `COPY_WITH_VALIDATION:7`, `V1_OWNED:36`, `SKIP_V2_ONLY:4`, `INSERT_ONLY:1`, `TARGET_DEFAULT:5` |
| `NguoiLX_GPLX` | 53 | `DISABLED:53` |
| `NguoiLXHS_GiayTo` | 5 | `COPY_EXACT:3`, `INSERT_ONLY:2` |
| `XeTap` | 34 | `UNKNOWN:34` |
| `KhoaHoc_XeTap` | 17 | `UNKNOWN:17` |
| `DM_LuuLuongDaoTao` | 8 | `UNKNOWN:8` |

Các rule quyết định:

- Key: `INSERT_ONLY` khi insert mới, `IMMUTABLE` khi update; collision hoặc alias casing/trailing-space chặn domain/stream theo tính bắt buộc.
- V2 exact: `COPY_EXACT`; vẫn phải qua FK/UQ/partition validation.
- Narrower/nullability/collation: `COPY_WITH_VALIDATION`; lỗi không được truncate.
- Mapping nghiệp vụ/mã: `TRANSFORM_REQUIRED`; không đoán enum.
- Shared lifecycle: insert có validation, update `PRESERVE_V1`.
- V1-owned: `V1_OWNED` và `PRESERVE_TARGET`.
- Audit target-generated: `TARGET_DEFAULT`/`IMMUTABLE`.
- Candidate chưa có policy: `UNKNOWN` + `SKIP_DOMAIN`, tuyệt đối không auto-copy.
- Lỗi row riêng của state/parent giấy tờ: `REJECT_ROW`; lỗi schema/FK bắt buộc: `BLOCK_STREAM`; optional schema: `SKIP_DOMAIN`.

## 7. Domain verdict

| Domain | Verdict | Lý do |
| --- | --- | --- |
| `DM_DonViGTVT` | `READY_WITH_TRANSFORM` | Hai route row fit độ dài; `CoQuanQL` có mapping theo route. Cần thay exact-type gate bằng lossless-value validation trong task sau. |
| `KhoaHoc` | `READY_WITH_VALIDATION` | 3/2 row fit, lookup đang dùng đủ; phải đi sau đơn vị. |
| `BaoCaoI` | `READY_WITH_VALIDATION` | Schema/required tương thích nhưng nguồn 0/0, chưa có proof insert thực tế. |
| `NguoiLX` | `READY_WITH_VALIDATION` | 108/5 row fit; lookup đủ; địa chỉ blank là warning nghiệp vụ. |
| `NguoiLXHS_GiayTo` | `READY_WITH_VALIDATION` | Schema/key/lookup/orphan đều đạt; phụ thuộc parent hồ sơ được nhận. |
| `GiaoVien` | `BLOCKED` | Policy cho phép cột target không có và thiếu mapping kiểu/mã; 0 row không chứng minh được. |
| `NguoiLX_HoSo` | `BLOCKED` | Lifecycle P1 do default/sentinel; chỉ 68/0 row qua training-state gate. |
| `NguoiLX_GPLX` | `DISABLED` | Provenance chưa rõ; policy tắt automatic writes là đúng. |
| `KhoaHoc_GiaoVien` | `OPTIONAL` | Schema tương thích, 0 row, không cần cho SQL path BCII tối thiểu. |
| `XeTap` | `OPTIONAL` | Ngoài catalog, 0 row, không cần cho BCII core. |
| `KhoaHoc_XeTap` | `DISABLED` | Ngoài catalog; nullable → required và V1 có FK bổ sung. |
| `DM_LuuLuongDaoTao` | `OPTIONAL` | Ngoài catalog, schema tương thích, 0 row, không cần cho BCII core. |

Không domain nào được gắn `READY` vì tất cả domain có dữ liệu hoặc cấu trúc chấp nhận được vẫn cần ít nhất validation, transformation, provenance hoặc parent ordering.

## 8. Default/sentinel matrix của V1

`Có phải dữ liệu thật?` ở đây chỉ nói giá trị mặc định có chứng minh một sự kiện nghiệp vụ đã xảy ra hay không.

| Table | Column | Default | Ý nghĩa chắc chắn | Dữ liệu thật? | Dùng phát hiện lifecycle/conflict? |
| --- | --- | --- | --- | --- | --- |
| `BaoCaoI` | `TrangThai` | `0` | Trạng thái khởi tạo | Chưa thể kết luận ngoài ngữ cảnh | Không chỉ từ default |
| `BaoCaoI` | `NgayTao` | `getdate()` | Audit time target | Không phải dữ liệu nguồn | Không |
| `BaoCaoI` | `NgaySua` | `getdate()` | Audit time target | Không phải lifecycle | Không |
| `DM_DonViGTVT` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `DM_DonViGTVT` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `DM_DonViGTVT` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `DM_LuuLuongDaoTao` | `NguoiTao` | `ADMIN` | Audit principal mặc định | Không phải actor đã xác thực | Không |
| `DM_LuuLuongDaoTao` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `DM_LuuLuongDaoTao` | `NguoiSua` | `ADMIN` | Audit principal mặc định | Không phải actor đã xác thực | Không |
| `DM_LuuLuongDaoTao` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `GiaoVien` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `GiaoVien` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `GiaoVien` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `KhoaHoc` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc_GiaoVien` | `SoHV` | `0` | Giá trị khởi tạo | Sentinel/chưa chắc | Không |
| `KhoaHoc_GiaoVien` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `KhoaHoc_GiaoVien` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc_GiaoVien` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc_GiaoVien` | `IsKhoaHocGiaoVien` | `1` | Cờ loại quan hệ mặc định | Initial state | Không |
| `KhoaHoc_XeTap` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `KhoaHoc_XeTap` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc_XeTap` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `KhoaHoc_XeTap` | `IsKhoaHocXeTap` | `0` | Cờ loại quan hệ mặc định | Initial state | Không |
| `NguoiLX` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `NguoiLX` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `NguoiLX` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `NguoiLX_GPLX` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `NguoiLX_GPLX` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `NguoiLX_GPLX` | `TrangThai` | `0` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `NguoiLX_HoSo` | `GiayCNSK` | `0` | Cờ khởi tạo; source shared khi có | Không tự chứng minh giấy tờ | Không |
| `NguoiLX_HoSo` | `BC1_TuoiTS` | `1` | Cờ kiểm tra BCI khởi tạo | Chưa thể kết luận | Không |
| `NguoiLX_HoSo` | `BC1_ThamNien` | `1` | Cờ kiểm tra BCI khởi tạo | Chưa thể kết luận | Không |
| `NguoiLX_HoSo` | `LanSH` | `1` | Sentinel lần sát hạch khởi tạo | Không chứng minh đã sát hạch | **Không** |
| `NguoiLX_HoSo` | `KetQua_LyThuyet` | `0` | Sentinel chưa/có kết quả âm | Không chứng minh lifecycle | **Không** |
| `NguoiLX_HoSo` | `KetQua_Hinh` | `0` | Sentinel chưa/có kết quả âm | Không chứng minh lifecycle | **Không** |
| `NguoiLX_HoSo` | `KetQua_Duong` | `0` | Sentinel chưa/có kết quả âm | Không chứng minh lifecycle | **Không** |
| `NguoiLX_HoSo` | `KetQuaSH` | `1` | Sentinel/default legacy | Không có strong signal đi kèm | **Không** |
| `NguoiLX_HoSo` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `NguoiLX_HoSo` | `NgaySua` | `getdate()` | Audit time target | Không | Không |
| `NguoiLX_HoSo` | `TrangThai` | `0` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `NguoiLX_HoSo` | `Transfer_flag` | `0` | Cờ vận hành khởi tạo | Không chứng minh lifecycle | Không |
| `NguoiLX_HoSo` | `CHON_IN_GPLX` | `0` | Cờ chọn in khởi tạo | Không chứng minh đã in | **Không** |
| `NguoiLX_HoSo` | `KetQuaSHM` | `0` | Sentinel kết quả mô phỏng | Không chứng minh lifecycle | **Không** |
| `NguoiLXHS_GiayTo` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `XeTap` | `SoHuu` | `0` | Cờ khởi tạo | Chưa thể kết luận | Không |
| `XeTap` | `GiayPhepXTL` | `1` | Cờ khởi tạo | Chưa thể kết luận | Không |
| `XeTap` | `HeThongPP` | `1` | Cờ khởi tạo | Chưa thể kết luận | Không |
| `XeTap` | `BaoHiem` | `1` | Cờ khởi tạo | Chưa thể kết luận | Không |
| `XeTap` | `TrangThai` | `1` | Trạng thái khởi tạo | Chưa thể kết luận | Không chỉ từ default |
| `XeTap` | `NgayTao` | `getdate()` | Audit time target | Không | Không |
| `XeTap` | `NgaySua` | `getdate()` | Audit time target | Không | Không |

Kết luận bắt buộc cho task sửa P1 sau: lifecycle phải dựa trên strong business evidence và state thực, không dựa vào `IS NOT NULL` của cột có default/sentinel non-null.

## 9. FK/lookup compatibility

| Child | Parent/lookup V1 | Hiện tại | Sau thứ tự tối thiểu | Kết luận |
| --- | --- | --- | --- | --- |
| `KhoaHoc` | `DM_DonViGTVT`, `DM_HangGPLX`, `DM_HangDT` | Thiếu parent đơn vị 3/2; lookup khác thiếu 0 | Thiếu 0 sau unit route | Accept với ordered FK gate |
| `BaoCaoI` | `DM_DonViGTVT`, `KhoaHoc` | 0 row | Không có mẫu | Schema ready, value proof thiếu |
| `NguoiLX` | `DM_DonViGTVT`, `DM_DVHC` composite, `DM_QuocTich` | Thiếu đơn vị 108/5; lookup thiếu 0 | Thiếu 0 sau unit route | Accept; cảnh báo FK địa bàn untrusted |
| `NguoiLX_HoSo` | `NguoiLX`, `KhoaHoc`, optional `BaoCaoI`, đơn vị, Hang/HTCap/LoaiHS/TrangThai | Target business parent đang rỗng; source parent/lookup thiếu 0 | FK thiếu 0 cho row được nhận sau unit/course/learner; `MaBC1` hiện blank 108/5 | FK không phải blocker sau ordering; lifecycle/state vẫn block |
| `NguoiLXHS_GiayTo` | `NguoiLX_HoSo`, `DM_GiayTo` | Lookup thiếu 0, source orphan 0 | Phụ thuộc parent được planner nhận | Reject giấy tờ nếu parent bị reject |
| `GiaoVien` | Đơn vị, địa bàn, V1 thêm Hang GPLX | 0 row | Chưa chứng minh | Block bởi schema/mapping trước FK |
| `KhoaHoc_XeTap` | `KhoaHoc`, `XeTap`, V1 thêm `GiaoVien` | 0 row | Chưa chứng minh | Disabled |
| `XeTap` | Đơn vị | 0 row | Optional | Không thuộc minimum set |
| `DM_LuuLuongDaoTao` | Đơn vị, Hang GPLX | 0 row | Optional | Không thuộc minimum set |

`DM_HangDT` có ba key V2 chưa tồn tại ở V1, nhưng không key nào được các row `KhoaHoc`/`NguoiLX_HoSo` hiện tại tham chiếu. Không được đồng bộ toàn bộ lookup chỉ vì có chênh lệch count; gate đúng là validate những key thực sự dùng.

## 10. Insert/update simulation counts

Mô phỏng chỉ dùng SELECT/CASE và metadata; không INSERT. Tất cả bảng business V1 mục tiêu đang có 0 row, nên `WOULD_UPDATE` và collision hiện tại đều bằng 0.

| Domain | Total OTO/MOTO | Projection `WOULD_INSERT` | `WOULD_REJECT` | FK thiếu sau ordered parents | Gate/domain result |
| --- | ---: | ---: | ---: | ---: | --- |
| `DM_DonViGTVT` | 1/1 route | 1/1 theo giá trị | 0/0 | 0/0 | `WOULD_BLOCK` ở writer hiện tại do exact type; compatibility cần transform/validation |
| `GiaoVien` | 0/0 | 0/0 | 0/0 | 0/0 | Domain schema `WOULD_BLOCK` dù không có row |
| `KhoaHoc` | 3/2 | 3/2 | 0/0 | 0/0 | `WOULD_INSERT` sau unit |
| `KhoaHoc_GiaoVien` | 0/0 | 0/0 | 0/0 | 0/0 | Optional skip |
| `BaoCaoI` | 0/0 | 0/0 | 0/0 | 0/0 | Không có header để mô phỏng |
| `NguoiLX` | 108/5 | 108/5 | 0/0 schema | 0/0 | `WOULD_INSERT` sau unit; blank address warning |
| `NguoiLX_HoSo` | 108/5 | 68/0 theo planner state | 40/5 state | 0/0 cho row accepted | Audit `WOULD_BLOCK` safe rollout vì lifecycle P1 |
| `NguoiLX_GPLX` | 0/0 | 0/0 | 0/0 | 0/0 | `WOULD_PRESERVE`; automatic writes disabled |
| `NguoiLXHS_GiayTo` | 324/15 | 204/0 sau parent accepted | 120/15 do parent dossier bị reject | 0/0 cho parent accepted | Accept có điều kiện |
| `XeTap` | 0/0 | 0/0 | 0/0 | 0/0 | Optional skip |
| `KhoaHoc_XeTap` | 0/0 | 0/0 | 0/0 | 0/0 | Disabled |
| `DM_LuuLuongDaoTao` | 0/0 | 0/0 | 0/0 | 0/0 | Optional skip |

Điều kiện display/SQL procedure cho BCII cho kết quả 0 row ở cả OTO và MOTO. OTO có 68 hồ sơ state `03`, nhưng khóa hiện tại thuộc nhánh không phải A và cần state `09` theo logic SQL đã khảo sát. Đây là bằng chứng dữ liệu hiện tại chưa đủ cho BCII, không phải lý do nới state mapping.

## 11. OTO/MOTO comparison

| Tiêu chí | OTO | MOTO | Kết luận |
| --- | --- | --- | --- |
| Schema V2 | Giống MOTO trên 24 bảng | Giống OTO | Dùng chung metadata mapping |
| Schema V1 | Giống MOTO trên 24 bảng | Giống OTO | Dùng chung target validation |
| BAK/live drift | 0 | 0 | Vẫn cần drift gate mỗi lần chạy tương lai |
| Route unit | 1 row cho profile `66029` | 1 row cho profile `66030` | Dùng chung transform, tham số profile khác |
| Khóa học | 3 | 2 | Mapping chung, enum value khác |
| Người học/hồ sơ | 108/108 | 5/5 | Mapping chung, volume/profile khác |
| Training state accepted | 68 | 0 | Rule chung nhưng kết quả profile-specific |
| Giấy tờ | 324 | 15 | Mapping chung, phụ thuộc accepted parent |
| BCI/source eligibility | 0/0 | 0/0 | Không profile nào có minimum data set hoàn chỉnh |

Không có domain chỉ tồn tại về schema ở OTO hoặc chỉ ở MOTO. Mapping có thể dùng chung ở mức schema, nhưng validation/state/FK phải chạy riêng cho từng profile và không được dùng kết quả OTO thay cho MOTO.

## 12. Minimum sync set cho Báo cáo II

Thứ tự FK thực tế:

1. Validate lookup V1 có sẵn: `DM_DVHC`, `DM_QuocTich`, `DM_HangGPLX`, `DM_HangDT`, `DM_LoaiHSo`, `DM_HTCap`, `DM_GiayTo`, `DM_TrangThai`.
2. `DM_DonViGTVT`.
3. `KhoaHoc`.
4. `BaoCaoI`.
5. `NguoiLX`.
6. `NguoiLX_HoSo`.
7. `NguoiLXHS_GiayTo` nếu cần giấy tờ ngoài SQL path tối thiểu.
8. Nguồn lực optional theo FK nội bộ: `GiaoVien` → `KhoaHoc_GiaoVien`; `XeTap`/`GiaoVien` → `KhoaHoc_XeTap`; `DM_LuuLuongDaoTao`.

Minimum set để `usp_BaoCaoI_Search`, `usp_NguoiLX_Select_By_MaKH2` và quá trình lập BCII có dữ liệu:

- Lookup validate-only nêu trên.
- `DM_DonViGTVT`: các cột key/đơn vị cần cho FK và hiển thị.
- `KhoaHoc`: key, đơn vị, hạng, thời gian/trạng thái đào tạo được policy cho phép.
- `BaoCaoI`: header key, `MaCSDT`, `MaKH`, số/ngày báo cáo và các trường BCI V2-owned.
- `NguoiLX`: key và dữ liệu người học V2-owned cần join/hiển thị.
- `NguoiLX_HoSo`: key, liên kết khóa/BCI/đơn vị, hạng, loại hồ sơ, hình thức cấp và training state được phép.

Trong snapshot hiện tại, minimum set **không tồn tại** vì `BaoCaoI` có 0 row và SQL eligibility có 0 row. Không được suy ra “sync sẵn sàng” từ việc các bảng khác fit schema.

## 13. Optional/disabled domains

### Optional data set

- `NguoiLXHS_GiayTo`: không bắt buộc cho core selection nhưng cần cho hiển thị/in/XML/kiểm tra giấy tờ; chỉ nhận sau parent.
- `GiaoVien`, `KhoaHoc_GiaoVien`: nguồn lực đào tạo; hiện bị block/optional và không cần cho core BCII SQL.
- `XeTap`, `DM_LuuLuongDaoTao`: phục vụ nguồn lực/pháp lý/hiển thị ngoài SQL core; ngoài catalog.

### Disabled/preserve

- `NguoiLX_GPLX`: `DISABLED`, V1 preserve, không forward-write cho đến khi provenance được chứng minh.
- `KhoaHoc_XeTap`: `DISABLED` vì chưa có policy, V1 yêu cầu `MaGV` non-null và FK.
- Các cột V1-owned trong `NguoiLX_HoSo`: preserve tuyệt đối.
- Các domain V1-only: `BaoCaoII`, `KySH`, `DM_LyDoTCBC2`, `DM_DiemSatHach`, `DM_KetQuaSatHach`.

## 14. Ownership-policy gap analysis

| Domain/cột | Compatibility verdict | Policy/code hiện tại | Khớp? | Rủi ro | Thay đổi cần làm sau |
| --- | --- | --- | ---: | --- | --- |
| `DM_DonViGTVT.TenDV` | Copy với length validation | Policy V2 direct; writer đòi exact SQL type | Không | Chặn 1/1 row dù max 52 | Cho phép narrower target khi mọi value fit; không truncate |
| `DM_DonViGTVT.CoQuanQL` | Transform theo route + length validation | Có business mapping nhưng exact-type gate vẫn chặn | Không đầy đủ | Mapping không bao giờ đến bước ghi an toàn | Validation theo projected value và chứng minh mapping nghiệp vụ |
| Năm cột V2-only của `GiaoVien` | Target không có, block | Policy V2 read/write | Không | Optional domain luôn schema-fail; nếu mandatory hóa sẽ block stream | Quyết định skip/target schema/mapping trước khi bật |
| `GiaoVien.GhiChu` | Copy có validation | Policy direct, writer exact type | Không | Schema block dù row có thể fit | Length-by-value gate |
| `GiaoVien.HangGPLX` | Transform + non-null + lookup | Có special validation nhưng mapping chưa được chứng minh | Một phần | Mã dài/Unicode/null/lookup sai | Mapping enum được duyệt và test bằng dữ liệu |
| `GiaoVien.HinhThuc_TuyenDung`, `LoaiHinh_DaoTao` | Transform required | Policy direct | Không | Lossy Unicode/enum guessing | Bảng mapping rõ ràng hoặc giữ disabled |
| `NguoiLX_HoSo` shared/lifecycle | Preserve khi lifecycle thật | Planner coi non-null default là lifecycle | Không, P1 đã biết | False preserve/conflict ngay sau insert | Sửa detector theo default/sentinel matrix trong task sau |
| `NguoiLX_HoSo` V1-owned source data | Không write, không lộ định danh | Preserve đúng nhưng conflict/tombstone còn lộ raw `MaDK` theo P1 trước | Không đầy đủ | PII/key exposure | Mask/hash key trong task P1 sau |
| Critical ownership tests | Phải chứng minh behavior | Một số test chỉ kiểm chuỗi source theo P1 trước | Không | Regression vẫn lọt | Bổ sung behavior/integration tests sau |
| `NguoiLX_HoSo.QDThucHanhHinh` | Source-only skip | Classified non-writable | Có | Không | Giữ nguyên |
| `NguoiLX_GPLX` | Disabled/preserve | Automatic writes disabled | Có | Không cho đến khi provenance rõ | Giữ disabled |
| `XeTap`, `KhoaHoc_XeTap`, `DM_LuuLuongDaoTao` | Không auto-copy | Không catalog/policy | Có về fail-closed | Thiếu chức năng optional | Chỉ thêm sau khi có business need + mapping |
| Lookup phụ thuộc | Validate-only | Không có forward domain riêng | Có cho dữ liệu hiện tại | Ba `DM_HangDT` key tương lai chưa được phủ | Drift/coverage gate theo key được dùng |

Domain nên mandatory trong luồng core sau khi các blocker được sửa: `DM_DonViGTVT`, `KhoaHoc`, `BaoCaoI`, `NguoiLX`, `NguoiLX_HoSo`. `NguoiLXHS_GiayTo` chỉ mandatory nếu yêu cầu nghiệp vụ đòi giấy tờ đi cùng; catalog hiện xem mandatory nhưng SQL core không cần. `NguoiLX_GPLX` không được mandatory-write khi provenance chưa rõ.

Ba P1 hiện hữu chỉ được ghi nhận làm input cho task sau; không P1 nào được sửa trong audit này.

## 15. Các điều chưa thể kết luận

- Tính đúng nghiệp vụ của authority mapping cho `66029`/`66030` chưa được kiểm chứng bằng nguồn chuẩn ngoài code; audit chỉ chứng minh giá trị projected fit target.
- Mapping enum của `GiaoVien` chưa thể kết luận vì không có row và không có bảng mapping được phê duyệt.
- `BaoCaoI` chưa có row thật nên không thể chứng minh UQ `SoBaoCao`, FK và header insertion bằng dữ liệu thực.
- Provenance của `NguoiLX_GPLX` chưa rõ; schema giống nhau không đủ cho phép sync.
- `KhoaHoc_XeTap.MaGV` và các resource domain rỗng chưa có bằng chứng null/Unicode/enum/FK thực tế.
- Ý nghĩa nghiệp vụ của địa chỉ blank-but-NOT-NULL ở `NguoiLX` cần owner nghiệp vụ xác nhận; schema vẫn nhận được.
- Ba key `DM_HangDT` thiếu ở V1 chưa ảnh hưởng snapshot hiện tại nhưng có thể block dữ liệu tương lai.
- Các default trạng thái/cờ ngoài lifecycle chỉ được phân loại “initial/unconfirmed”; không được nâng thành business truth khi chưa có tài liệu chuẩn.
- Audit không kết luận sync có thể chạy production, không thực hiện BAK preflight và không tạo MASTER TASK.

## 16. Query/script read-only đã chạy

Tất cả batch SQL được thực thi theo mẫu bắt buộc:

```sql
USE [ExactDatabaseName];
GO
-- Chỉ SELECT metadata hoặc aggregate; không có DML/DDL.
```

Các nhóm truy vấn:

| ID | Database | Nội dung | Kết quả |
| --- | --- | --- | --- |
| Q01 | Cả 8 DB, mỗi DB một batch `USE` riêng | `sys.databases`: state, read/write, compatibility | 8/8 ONLINE, READ_WRITE, compatibility 110 |
| Q02 | 4 BAK + 4 live | Row count các bảng forward/lookup/V1-owned | Thu được count nêu trong báo cáo |
| Q03 | 4 BAK + 4 live | `sys.tables`, `sys.columns`, `sys.types`, default, collation, identity, computed | 0 drift BAK/live; OTO/MOTO schema giống nhau |
| Q04 | V2 BAK ↔ V1 BAK | Full outer column comparison | 13 sai khác |
| Q05 | V2/V1 BAK | PK/UQ, FK, trust/disabled, cascade, CHECK, trigger | PK/UQ tương ứng; 0 CHECK/trigger; V1 thêm 2 FK đáng chú ý |
| Q06 | V1 BAK | Required NOT NULL/no-default và 53 default | Hai source-nullable mismatch; không target-only required |
| Q07 | V2 BAK | Aggregate string length/null/blank/Unicode/trailing/unsafe conversion | Các count/max nêu tại mục 4 |
| Q08 | V2 BAK | Numeric/date aggregate và enum count | `QDThucHanhHinh` null toàn bộ; date format hợp lệ; enum đã tổng hợp |
| Q09 | V2 BAK ↔ V1 BAK | Duplicate, orphan, lookup coverage, collision | Không collision/orphan hiện tại; `DM_HangDT` thiếu 3 key tổng thể nhưng row đang dùng đủ |
| Q10 | V2 BAK ↔ V1 BAK | SELECT/CASE mô phỏng projection, required, FK, state, parent giấy tờ | Count `WOULD_*` tại mục 10 |
| Q11 | V2 BAK | Logic eligibility theo SQL requirements | 0 row đủ điều kiện BCII ở OTO và MOTO |

Ví dụ batch metadata thực tế:

```sql
USE [CSDL_OTO_BAK];
GO
SET NOCOUNT ON;
SELECT t.name, c.name, ty.name, c.max_length, c.precision, c.scale,
       c.is_nullable, c.is_identity, c.is_computed, c.collation_name
FROM sys.tables AS t
JOIN sys.columns AS c ON c.object_id = t.object_id
JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id;
```

Ví dụ batch aggregate không PII:

```sql
USE [CSDL_MOTO_BAK];
GO
SET NOCOUNT ON;
SELECT COUNT_BIG(*) AS TotalRows,
       SUM(CASE WHEN TT_XuLy IN ('03','04','09') THEN 1 ELSE 0 END) AS AcceptedStateRows,
       SUM(CASE WHEN TT_XuLy NOT IN ('03','04','09') OR TT_XuLy IS NULL THEN 1 ELSE 0 END) AS RejectedStateRows
FROM dbo.NguoiLX_HoSo;
```

Ba lần thử SELECT-only ban đầu báo lỗi cú pháp/qualification (`RowCount` alias, cột `KhoaHoc` ambiguous, và `QUOTED_IDENTIFIER` khi dùng XML aggregation); đều không thực thi ghi và đã được chạy lại bằng query read-only đã sửa. Không có DML, DDL, stored procedure ghi hoặc transaction mutation.

## 17. Xác nhận

- Đúng branch lúc bắt đầu: `codex/csdt-realtime-v2-to-v1-oto-moto`.
- Snapshot ban đầu: staged = 0; 19 tracked modified và 53 untracked đã có sẵn.
- Không sửa production code hoặc ownership policy.
- Không sửa hai `appsettings.Development.json`.
- Chỉ tạo hai artifact audit untracked:
  - `docs/analysis/V2_TO_V1_COMPATIBILITY_AND_ACCEPTANCE.md`
  - `docs/analysis/V2_TO_V1_ACCEPTANCE_MATRIX.json`
- Không chạy SQL ghi.
- Không chạy sync.
- Không chạy reverse V1 → V2.
- Không DELETE/truncate/tái sinh mã/update PK/identity.
- Không bắt đầu BAK preflight.
- Không stage/commit/merge/push.
- Không xuất họ tên, CCCD, địa chỉ, `MaDK` hoặc giá trị PII khác.
- Không sửa ba P1 ownership/lifecycle/test hiện tại.
- Không tạo MASTER TASK.

**Kết luận cuối:** `COMPATIBILITY AUDIT COMPLETE`
