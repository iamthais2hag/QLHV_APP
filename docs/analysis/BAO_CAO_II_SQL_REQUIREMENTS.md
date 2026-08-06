# Khảo sát SQL V1 cho chức năng Báo cáo II — đăng ký sát hạch

Ngày khảo sát: 2026-07-25  
Phạm vi: chỉ đọc; ưu tiên `CSDL_OTO_V1_BAK`, `CSDL_MOTO_V1_BAK`; đối chiếu live và nguồn V2_BAK.  
Mục đích: xác định điều kiện dữ liệu để V1 lập Báo cáo II, ranh giới sở hữu dữ liệu và whitelist đồng bộ V2 → V1.

## 1. Kết luận điều hành

1. V1 lập Báo cáo II bằng một **header** ở `dbo.BaoCaoII` và một **detail logic** nằm ngay trên các row `dbo.NguoiLX_HoSo`:
   - header: `BaoCaoII.MaBCII`;
   - detail logic: `NguoiLX_HoSo.MaBC2 = BaoCaoII.MaBCII`;
   - trạng thái detail: `NguoiLX_HoSo.TT_XuLy`.
   Không có bảng detail Báo cáo II riêng và cũng không có FK từ `NguoiLX_HoSo.MaBC2` tới `BaoCaoII`.
2. Luồng chính được chứng minh bởi SQL:

   ```text
   BaoCaoI.MaKH
       -> KhoaHoc.MaKH
       -> NguoiLX_HoSo.MaKhoaHoc
       -> NguoiLX_HoSo.MaDK = NguoiLX.MaDK
       -> tạo BaoCaoII
       -> gán NguoiLX_HoSo.MaBC2 và đổi TT_XuLy
   ```

3. Các object quyết định luồng lập:
   - `dbo.usp_BaoCaoI_Search`;
   - `dbo.usp_BaoCaoII_SearchByBC1`;
   - `dbo.usp_NguoiLX_Select_By_MaKH2`;
   - `dbo.usp_BaoCaoII_Insert`;
   - `dbo.usp_NguoiLX_TongHop_By_MaKH2`.
4. SQL chính **không kiểm tra** giấy phép đơn vị, cơ quan quản lý, trạng thái khóa, ngày khai giảng/bế giảng, ảnh, giấy tờ, GPLX cũ, số giấy chứng nhận tốt nghiệp, giáo viên, xe tập hoặc lưu lượng trước khi lập. Các trường này có thể được đọc để hiển thị/in/XML, nhưng không được phép gọi là điều kiện eligibility nếu chỉ dựa trên SQL đã khảo sát.
5. Điều kiện học viên của luồng chính:
   - nhánh “A”: `TT_XuLy IN ('03','04') AND TrangThai = 1`;
   - nhánh còn lại: `TT_XuLy = '09'`.
6. Có các khoảng hở quan trọng:
   - danh sách hiển thị nhánh A kiểm tra `TrangThai = 1`, nhưng procedure tổng hợp không kiểm tra `TrangThai`;
   - procedure tổng hợp không loại row đã có `MaBC2`;
   - `BaoCaoII.MaBCI` không có FK và không unique; một BCI có thể có nhiều BCII nếu application không gọi kiểm tra trùng;
   - hủy tổng hợp qua `usp_NguoiLX_TongHop_By_MaKH2` khôi phục `TT_XuLy` nhưng không xóa `MaBC2`;
   - tìm học viên thêm tay có điều kiện `... AND HS.HangGPLX LIKE '%A%' OR HS.HangGPLX = 'B1m'`; do precedence, row `B1m` có thể bỏ qua toàn bộ điều kiện đứng trước;
   - tham số `@maBC2` của procedure tìm học viên thêm tay không được dùng; các điều kiện loại học viên đã có BCII đang bị comment.
7. `CSDL_OTO_V1_BAK` và `CSDL_MOTO_V1_BAK` giống nhau hoàn toàn trong phạm vi khảo sát:
   - 0 khác biệt metadata cột trên 20 bảng lõi;
   - 0 khác biệt definition của module BCII;
   - 0 khác biệt object liên quan;
   - 0 khác biệt metadata cột liên quan giữa OTO_BAK và MOTO_BAK.
   Hiện tại không cần hai bộ mapping logic; nên dùng một mapping versioned với hai profile/stream tách biệt và kiểm tra schema drift.
8. Schema liên quan của mỗi V1_BAK trùng với V1 live tương ứng: 0 khác biệt metadata cột và 0 khác biệt module BCII. Vì vậy BAK đủ tin cậy để khảo sát schema/logic.
9. Cả V1_BAK và V1 live hiện không có dữ liệu ở các bảng nghiệp vụ được đếm (`BaoCaoI`, `BaoCaoII`, `KhoaHoc`, `NguoiLX`, `NguoiLX_HoSo`, `NguoiLX_GPLX`, `NguoiLXHS_GiayTo`, `KySH` đều 0). Vì vậy không thể xác nhận bằng dữ liệu thật các ngưỡng eligibility hay tỉ lệ loại tại V1.
10. V2_BAK hiện có schema `BaoCaoII`, `KySH` và các cột sát hạch trong `NguoiLX_HoSo`, nhưng dữ liệu các bảng này bằng 0. Sự tồn tại vật lý của schema cũ **không biến chúng thành nguồn V2-owned**. Không được đồng bộ các domain này V2 → V1.
11. Luồng realtime hiện tại cần được xem là **chưa an toàn cho BCII**: catalog không đồng bộ bảng `BaoCaoII`, nhưng writer cập nhật mọi cột non-computed chung của `NguoiLX_HoSo` và `NguoiLX_GPLX`. Điều này có thể ghi đè `MaBC2`, `TT_XuLy`, kỳ/kết quả sát hạch và GPLX do V1 phát sinh. Kết luận này chỉ là audit read-only; không có code nào được sửa trong lượt này.

## 2. Sơ đồ luồng SQL

```text
dbo.BaoCaoI
  MaBCI (PK), MaKH, MaCSDT
       |
       | usp_BaoCaoI_Search(@TrangThai = 2)
       | MaBCI NOT LIKE 'A1A2%' AND MaKH NOT LIKE '%DB%'
       v
dbo.KhoaHoc
  MaKH (PK), MaCSDT, HangGPLX, HangDT, NgayKG, NgayBG
       |
       | NguoiLX_HoSo.MaKhoaHoc = KhoaHoc.MaKH
       v
dbo.NguoiLX_HoSo ---------------- dbo.NguoiLX
  MaDK (PK/FK)                       MaDK (PK)
  TT_XuLy, TrangThai                 thông tin người lái
       |
       | usp_NguoiLX_Select_By_MaKH2
       | nhánh A: TT_XuLy 03/04 + TrangThai=1
       | nhánh khác: TT_XuLy=09
       v
dbo.BaoCaoII
  MaBCII (PK), MaBCI, MaCSDT, TrangThai, TT_Xuly
       |
       | usp_NguoiLX_TongHop_By_MaKH2
       v
dbo.NguoiLX_HoSo
  MaBC2 = BaoCaoII.MaBCII
  TT_XuLy = '11'
  TT_XuLy_Old = '03'/'09'
```

Lưu ý về tính nguyên tử: `usp_BaoCaoII_Insert` và `usp_NguoiLX_TongHop_By_MaKH2` tự mở transaction riêng. Không có transaction SQL duy nhất bao trùm cả tạo header và gán detail.

## 3. Inventory object liên quan

### 3.1. Bảng lõi

| Nhóm | Object | Vai trò |
| --- | --- | --- |
| Báo cáo | `dbo.BaoCaoI` | BCI nguồn để chọn lập BCII |
| Báo cáo | `dbo.BaoCaoII` | Header BCII |
| Khóa | `dbo.KhoaHoc` | Khóa đào tạo |
| Học viên | `dbo.NguoiLX` | Thông tin người lái |
| Hồ sơ/detail | `dbo.NguoiLX_HoSo` | Detail logic BCII và toàn bộ trạng thái đào tạo/sát hạch |
| GPLX | `dbo.NguoiLX_GPLX` | GPLX đã cấp/phát sinh; không phải điều kiện của luồng lập BCII |
| Giấy tờ | `dbo.NguoiLXHS_GiayTo`, `dbo.DM_GiayTo` | Giấy tờ hồ sơ; không được lọc trong luồng chính |
| Đơn vị | `dbo.DM_DonViGTVT` | FK đơn vị/CSDT |
| Trạng thái | `dbo.DM_TrangThai` | Lookup cho `NguoiLX_HoSo.TT_XuLy` |
| Sát hạch | `dbo.KySH`, `dbo.DM_NoiDungSH`, `dbo.DM_DiemSatHach` | Kỳ, nội dung và điểm sát hạch |
| Lý do BCII | `dbo.DM_LyDoTCBC2` | Lý do không đạt BCII |
| Nguồn lực | `dbo.GiaoVien`, `dbo.KhoaHoc_GiaoVien`, `dbo.XeTap`, `dbo.KhoaHoc_XeTap`, `dbo.DM_LuuLuongDaoTao` | Dữ liệu đào tạo, không được core BCII procedure dùng làm eligibility |

### 3.2. Stored procedure quyết định nghiệp vụ

| Object | Thao tác | Đọc | Ghi | Điều kiện/ghi chú quyết định |
| --- | --- | --- | --- | --- |
| `usp_BaoCaoI_Search` | Danh sách BCI/khóa để tổng hợp | `BaoCaoI` | — | Khi `@TrangThai=2`: `MaBCI NOT LIKE 'A1A2%'`, `MaKH NOT LIKE '%DB%'`; không lọc `BaoCaoI.TrangThai` |
| `usp_BaoCaoII_SearchByBC1` | Kiểm tra BCII theo BCI | `BaoCaoII` | — | `MaBCI=@MaBC1`; đây là lookup, không phải constraint |
| `usp_NguoiLX_Select_By_MaKH2` | Danh sách học viên đủ điều kiện hiển thị | `KhoaHoc`, `NguoiLX`, `NguoiLX_HoSo` | — | Nhánh A: trạng thái `03/04` và `TrangThai=1`; nhánh khác: trạng thái `09` |
| `usp_BaoCaoII_Insert` | Tạo header | — | `BaoCaoII` | Không tự kiểm tra BCI tồn tại/trùng; transaction + `@@ERROR`, không có message riêng |
| `usp_NguoiLX_TongHop_By_MaKH2` | Gán toàn bộ học viên của khóa | `KhoaHoc`, `NguoiLX_HoSo` | `NguoiLX_HoSo` | Gán `MaBC2`, đổi `TT_XuLy='11'`; không loại `MaBC2` cũ; nhánh A không kiểm tra `TrangThai=1` |
| `usp_NguoiLX_HoSo_ThemHS_BC2` | Tìm học viên để thêm tay | `KhoaHoc`, `NguoiLX`, `NguoiLX_HoSo` | — | `TT_XuLy IN ('03','09','14','17','18')`, `NgayBG<=hôm nay`; có lỗi precedence `B1m`, không loại BCII cũ |
| `usp_NguoiLX_Update_ThemHSBC2` | Thêm tay | — | `NguoiLX_HoSo` | Gán `MaBC2`, `TT_XuLy='11'`, lưu `TT_XuLy_Old`; điều kiện chỉ `MaDK` |
| `usp_NguoiLXHoSo_Update_DanhSachHocSinhBC2` | Thêm/bỏ detail | — | `NguoiLX_HoSo` | `101` khôi phục trạng thái và xóa `MaBC2`; các nhánh update chỉ theo `MaDK` |
| `usp_BaoCaoII_Update` | Sửa header | — | `BaoCaoII` | Update mọi field header, kể cả `NguoiTao`, `NgayTao` |
| `usp_BaoCaoII_Delete` | Xóa header | — | `BaoCaoII` | Chỉ xóa header; không dọn `NguoiLX_HoSo.MaBC2` |
| `usp_BaoCaoII_Update_PheDuyetKQDT` | Cờ duyệt/gửi gần nhất tìm thấy | — | `BaoCaoII` | Đặt `TrangThai=1`, `NgaySua=GETDATE()` |
| `usp_BaoCaoII_Search` | Danh sách BCII | `BaoCaoII` | — | Lọc mã, số báo cáo, CSDT, khoảng ngày |
| `usp_BaoCaoII_Search_KQ` | Trạng thái BCII/kết quả SH | `BaoCaoII` | — | `@LoaiKQ='BC'` dùng `TrangThai`; `'SH'` dùng `TT_XuLy` |
| `usp_BaoCaoII_Select`, `usp_BaoCaoII_SelectAll` | Xem header | `BaoCaoII` | — | Không thêm eligibility |
| `usp_BaoCao2_DSHS` | Danh sách in | `NguoiLX_HoSo`, `NguoiLX`, `KhoaHoc`, lookup hạng/địa bàn | — | `MaBC2=@MaBC2`, `TT_XuLy IN ('11','12','13','14','16','17','18','19')` |
| `usp_BaoCao2_ViewRPT` | In báo cáo | `NguoiLX_HoSo`, `NguoiLX` | — | Cùng tập trạng thái `11..19` được liệt kê |
| `usp_NguoiLX_Select_By_MaBC2` | XML/danh sách mới lập | `NguoiLX_HoSo`, `NguoiLX`, lookup hạng | — | `MaBC2=@MaBC2 AND TT_XuLy='11'` |
| `usp_NguoiLX_Select_By_MaBC2_2XML` | XML đầy đủ | `NguoiLX_HoSo`, `NguoiLX`, lookup hạng | — | Chỉ lọc `MaBC2`; không lọc trạng thái |
| `usp_NguoiLX_HoSo_UpdateKQBC2` | Nhận kết quả BCII | `NguoiLX_HoSo` | `NguoiLX_HoSo` | Đạt → `13`, không đạt → `14`; ghi kỳ, SBD, quyết định |
| `usp_NguoiLX_HoSo_UpdateKQSH` | Nhận kết quả sát hạch | `BaoCaoII`, `NguoiLX_HoSo` | `NguoiLX_HoSo` | Chỉ update nếu `BaoCaoII.TrangThai=1`; trạng thái `16/17/18` |
| `usp_CSDT_PheDuyetKQDT_TiepNhan` | Tiếp nhận phê duyệt KQĐT | `NguoiLX_HoSo`, `NguoiLX` | Hai bảng trên | TRY/CATCH/THROW; `KetQuaPDSo=1` → `13`, ngược lại `14`; update chỉ theo `MaDK` |

### 3.3. Function

| Object | Vai trò |
| --- | --- |
| `dbo.usf_BaBT_GetNoidungSathach` | Sinh nội dung sát hạch từ hạng, kết quả cũ, lần SH, ngày QĐ/TN/BG |
| `dbo.usf_BaBT_GetMaNoidungSathach` | Sinh mã nội dung sát hạch |
| `dbo.usf_ToanPK_BoDauPhaiCuoi` | Ghép số CCN/CNTN để in |
| `dbo.usf_GetTrangThaiHocVien` | Diễn giải mã `TT_XuLy` |
| `dbo.CreateNewMaKhoaHoc` | Khớp inventory theo tên khóa học; không tham gia điều kiện BCII đã tìm thấy |

Ba scalar function trong thống kê inventory là các object khớp trực tiếp bộ từ khóa rộng. Hai function format/diễn giải còn lại được bổ sung khi lần dependency của các procedure BCII.

### 3.4. View, trigger, synonym và SQL Agent job

Không tìm thấy view, trigger, synonym hoặc SQL Agent job có tên/nội dung liên quan trực tiếp đến `BaoCaoII`, `BaoCao2`, `MaBC2`, sát hạch hoặc khóa học trong phạm vi đã truy vấn. Không có trigger trên các bảng lõi được liệt kê.

### 3.5. Số lượng inventory theo loại

Hai V1_BAK đều có cùng kết quả:

| Loại | Số object liên quan |
| --- | ---: |
| Default constraint | 42 |
| Foreign key constraint | 41 |
| Primary key constraint | 11 |
| Unique constraint | 4 |
| Scalar function | 3 |
| Stored procedure | 119 |
| User table | 12 |

119 procedure là tập tìm theo tên **hoặc definition** rộng. Bảng 3.2 chỉ giữ các procedure trực tiếp quyết định/tác động BCII.

### 3.6. Transaction, error và dependency

- `usp_BaoCaoII_Insert`, `Update`, `Delete`, `usp_NguoiLX_TongHop_By_MaKH2`,
  các procedure thêm/bỏ học viên và cập nhật kết quả dùng `BEGIN TRANSACTION`,
  kiểm tra `@@ERROR`, rồi `COMMIT`/`ROLLBACK`. Chúng không trả error code/message
  nghiệp vụ riêng.
- `usp_CSDT_PheDuyetKQDT_TiepNhan` dùng `TRY/CATCH`, rollback toàn transaction,
  `RAISERROR` khi không tìm thấy hồ sơ/người lái và `THROW` lỗi lên caller.
- Các procedure đọc không mở transaction ghi; `usp_BaoCaoII_SearchByBC1` đặt
  isolation level `READ COMMITTED`.
- Dependency trực tiếp đáng chú ý:
  - `usp_BaoCao2_DSHS` → `usf_ToanPK_BoDauPhaiCuoi`;
  - `usp_BaoCao2_ViewRPT` → `usf_ToanPK_BoDauPhaiCuoi`,
    `usf_BaBT_GetNoidungSathach`;
  - `usp_NguoiLX_KetQuaDaoTao_CSDT_GetHocVien` →
    `usf_GetTrangThaiHocVien`.

## 4. Schema và quan hệ

### 4.1. `dbo.BaoCaoII`

| Cột | Kiểu | Null | Default | Khóa |
| --- | --- | --- | --- | --- |
| `MaBCII` | `varchar(13)` | NOT NULL | — | PK clustered |
| `MaBCI` | `varchar(18)` | NULL | — | Không FK, không unique |
| `MaCSDT` | `varchar(6)` | NOT NULL | — | FK → `DM_DonViGTVT.MaDV` |
| `SoBaoCao` | `nvarchar(20)` | NULL | — | — |
| `NgayBaoCao` | `datetime` | NULL | — | — |
| `TongSoThiSinh` | `int` | NULL | — | — |
| `GhiChu` | `nvarchar(255)` | NULL | — | — |
| `TrangThai` | `bit` | NOT NULL | `0` | — |
| `NguoiTao` | `nvarchar(30)` | NULL | — | — |
| `NguoiSua` | `nvarchar(30)` | NULL | — | — |
| `NgayTao` | `datetime` | NOT NULL | `GETDATE()` | — |
| `NgaySua` | `datetime` | NOT NULL | `GETDATE()` | — |
| `TT_Xuly` | `int` | NULL | — | — |

Không identity, không computed column, không check constraint, không trigger, không unique index ngoài PK.

### 4.2. `dbo.BaoCaoI`

`MaBCI varchar(18) NOT NULL PK`; `MaCSDT varchar(6) NOT NULL FK`; `MaKH varchar(13) NOT NULL FK`; `SoBaoCao nvarchar(20) NULL UNIQUE`; `NgayBaoCao datetime NULL`; `SoGP nvarchar(20) NULL`; `NgayCapGP datetime NULL`; `LuuLuongGP int NULL`; `SoHocSinh int NULL`; `NgayKG datetime NULL`; `NgayBG datetime NULL`; `NgayTiepNhan datetime NULL`; `NguoiTiepNhan nvarchar(50) NULL`; `ThoiGianTiepNhan bit NULL`; `ThoiGianDaoTao bit NULL`; `LuuLuong bit NULL`; `BoTriHocVienXeTap bit NULL`; `GhiChu nvarchar(255) NULL`; `TrangThai bit NOT NULL DEFAULT 0`; `NguoiTao nvarchar(30) NULL`; `NguoiSua nvarchar(30) NULL`; `NgayTao datetime NOT NULL DEFAULT GETDATE()`; `NgaySua datetime NOT NULL DEFAULT GETDATE()`; `SoHSCanhBao int NULL`; `TT_Xuly int NULL`.

FK:

- `MaCSDT` → `DM_DonViGTVT.MaDV`;
- `MaKH` → `KhoaHoc.MaKH`, delete cascade.

### 4.3. Header/detail và khóa liên kết

| Quan hệ | Cột |
| --- | --- |
| BCII → BCI | `BaoCaoII.MaBCI = BaoCaoI.MaBCI` — logic only, không FK |
| BCI → khóa | `BaoCaoI.MaKH = KhoaHoc.MaKH` — có FK |
| Hồ sơ → khóa | `NguoiLX_HoSo.MaKhoaHoc = KhoaHoc.MaKH` — có FK, delete cascade |
| Hồ sơ → người lái | `NguoiLX_HoSo.MaDK = NguoiLX.MaDK` — có FK, delete cascade |
| Detail logic → BCII | `NguoiLX_HoSo.MaBC2 = BaoCaoII.MaBCII` — không FK |
| Hồ sơ → BCI | `NguoiLX_HoSo.MaBC1 = BaoCaoI.MaBCI` — có FK |
| Hồ sơ → trạng thái | `NguoiLX_HoSo.TT_XuLy = DM_TrangThai.MaTT` — có FK |
| Hồ sơ → kỳ SH | `NguoiLX_HoSo.MaKySH = KySH.MaKySH` — không thấy FK trong database thực tế |

### 4.4. Index/constraint chính

| Bảng | Index/constraint |
| --- | --- |
| `BaoCaoI` | PK clustered `(MaBCI)`; UQ nonclustered `(SoBaoCao)` |
| `BaoCaoII` | PK clustered `(MaBCII)` |
| `KhoaHoc` | PK clustered `(MaKH)`; UQ `(MaKH,MaCSDT,MaSoGTVT)` |
| `NguoiLX` | PK clustered `(MaDK)` |
| `NguoiLX_HoSo` | PK clustered `(MaDK)`; UQ `(MaDK)`; `IDs bigint IDENTITY` không phải PK |
| `NguoiLX_GPLX` | PK clustered `(MaDK)`; UQ `(MaDK,SoHoSo)` |
| `NguoiLXHS_GiayTo` | PK clustered `(MaGT,MaDK)` |
| `DM_LuuLuongDaoTao` | PK clustered `(MaCSDT,HangGPLX)` |
| `KhoaHoc_GiaoVien` | PK clustered identity `(MaLichLV)` |
| `KhoaHoc_XeTap` | PK clustered identity `(MaLichSD)` |
| `KySH` | PK clustered `(MaKySH)` |

Không có check constraint trên tập bảng lõi đã kiểm tra.

## 5. Ma trận điều kiện

Quy ước “Bắt buộc?”:

- **Có — schema/SQL**: được constraint hoặc WHERE/JOIN thực sự cưỡng chế.
- **Có — application guard**: có object kiểm tra nhưng không có constraint.
- **Không thấy**: không được khẳng định là điều kiện nghiệp vụ.
- **Chỉ dùng output**: được đọc để in/XML, không lọc eligibility.

| STT | Điều kiện | SQL object | Bảng/cột | Giá trị yêu cầu | Bắt buộc? | Nguồn | Sync action |
| --: | --- | --- | --- | --- | --- | --- | --- |
| 1 | `MaCSDT` tồn tại | FK nhiều bảng | `DM_DonViGTVT.MaDV` | Khớp chính xác | Có — schema | V2 | Upsert đơn vị trước |
| 2 | Mã đơn vị quản lý | — | `DM_DonViGTVT.MaDVQL` | Không thấy filter | Không thấy | V2 | Copy nếu có, không gate |
| 3 | Giấy phép đào tạo | output/schema | `DM_DonViGTVT.SoGP`, `NgayGP`, `NgayHHGP`; BCI có `SoGP`, `NgayCapGP` | Không thấy filter | Không thấy | V2 | Copy có guard độ dài |
| 4 | Cơ quan quản lý | output/master data | `DM_DonViGTVT.CoQuanQL` | Không thấy filter | Không thấy | V2 | Copy có guard `nvarchar(100)` target |
| 5 | Trạng thái đơn vị | — | `DM_DonViGTVT.TrangThai` | Không thấy filter | Không thấy | V2 | Không dùng làm gate nếu chưa có quyết định nghiệp vụ |
| 6 | Mã khóa | FK | `KhoaHoc.MaKH`, `BaoCaoI.MaKH`, `NguoiLX_HoSo.MaKhoaHoc` | Tồn tại/khớp | Có — schema | V2 | Upsert khóa trước BCI/hồ sơ |
| 7 | CSDT của khóa | FK | `KhoaHoc.MaCSDT` | Tồn tại trong `DM_DonViGTVT` | Có — schema | V2 | Copy |
| 8 | Hạng đào tạo | JOIN/schema | `KhoaHoc.HangGPLX`, `HangDT`; `NguoiLX_HoSo.HangGPLX`, `HangDaoTao` | Hạng phải join được khi in/XML | Có cho JOIN/output; không có filter completeness | V2 | Copy + validate lookup |
| 9 | Ngày khai giảng | — | `KhoaHoc.NgayKG` | Không thấy filter ở luồng chính | Không thấy | V2 | Copy, không gate |
| 10 | Ngày bế giảng | `usp_NguoiLX_HoSo_ThemHS_BC2` | `KhoaHoc.NgayBG` | `<=` ngày hiện tại chỉ khi tìm thêm tay | Có cho nhánh thêm tay | V2 | Copy |
| 11 | Trạng thái khóa | — | `KhoaHoc.TrangThai` | Không thấy filter | Không thấy | V2 | Copy, không gate |
| 12 | BCI tồn tại | `usp_BaoCaoI_Search` | `BaoCaoI.MaBCI`, `MaKH` | Có row BCI | Có — luồng màn hình | V2 | Insert/update BCI trước readiness |
| 13 | Trạng thái BCI | `usp_BaoCaoI_Search` | `BaoCaoI.TrangThai` | Điều kiện bị comment | Không thấy | V2 | Không suy diễn |
| 14 | Loại BCI/khóa được chọn | `usp_BaoCaoI_Search` | `MaBCI`, `MaKH` | `MaBCI NOT LIKE 'A1A2%'`; `MaKH NOT LIKE '%DB%'` | Có — WHERE khi `@TrangThai=2` | V2 | Validate |
| 15 | Khóa chưa có BCII | `usp_BaoCaoII_SearchByBC1` | `BaoCaoII.MaBCI` | Không có row cùng BCI | Có — application guard, không constraint | V1 | Check target ngay trước insert |
| 16 | Khóa bị khóa/hủy | — | `KhoaHoc.TrangThai`, `TT_Xuly` | Không thấy | Không thấy | V2/V1 | Cần quyết định riêng |
| 17 | `NguoiLX.MaDK` | PK/FK/JOIN | `NguoiLX.MaDK` | Tồn tại/khớp hồ sơ | Có — schema/SQL | V2 | Insert người lái trước hồ sơ |
| 18 | `NguoiLX_HoSo.MaDK` | PK | `NguoiLX_HoSo.MaDK` | Tồn tại, duy nhất | Có — schema | Shared | Insert-if-missing; update whitelist |
| 19 | Hồ sơ thuộc khóa | WHERE/FK | `NguoiLX_HoSo.MaKhoaHoc` | `=@MaKH` | Có — SQL | V2 | Copy |
| 20 | Hồ sơ thuộc CSDT | FK, không so với khóa trong proc | `NguoiLX_HoSo.MaCSDT` | FK hợp lệ | Có — schema; không có cross-check | V2 | Copy + cross-check đề xuất |
| 21 | Hạng hồ sơ | output/JOIN | `HangGPLX`, `HangDaoTao` | Có dữ liệu để join hạng khi in/XML | Có cho output | V2 | Copy |
| 22 | Trạng thái học viên nhánh A | `usp_NguoiLX_Select_By_MaKH2` | `TT_XuLy`, `TrangThai` | `TT_XuLy IN ('03','04') AND TrangThai=1` | Có — WHERE | Shared | Chỉ V2 được đặt trạng thái đào tạo trước BCII |
| 23 | Trạng thái học viên nhánh khác | cùng object | `TT_XuLy` | `TT_XuLy='09'` | Có — WHERE | Shared | Như trên |
| 24 | Ngày sinh | SELECT/output | `NguoiLX.NgaySinh` | Không thấy filter; schema NOT NULL | Có — schema, không eligibility | V2 | Copy + validate format 4/6/8 nếu cần |
| 25 | Số định danh | SELECT/output | `NguoiLX.SoCMT` | Không thấy filter; schema NOT NULL | Có — schema, không eligibility | V2 | Copy |
| 26 | Ảnh | —/XML | `NguoiLX_HoSo.DuongDanAnh` | Không thấy filter | Chỉ dùng dữ liệu khác; không eligibility | V2 | Copy nếu có |
| 27 | GPLX cũ khi nâng hạng | SELECT/output/function | Các cột `*GPLXDaCo` | Không thấy filter bắt buộc | Chỉ dùng output/tính nội dung SH | V2 | Copy vào phần training; không update `NguoiLX_GPLX` V1 |
| 28 | Giấy tờ | Không được core BCII proc join | `NguoiLXHS_GiayTo` | Không thấy | Không thấy | V2 | Insert/update metadata nếu cần nghiệp vụ khác |
| 29 | `SoGiayCNTN`/`SoCCN` | report/XML/function | `NguoiLX_HoSo` | Không thấy filter; được ghép để in | Chỉ dùng output | V2 | Copy; cảnh báo thiếu |
| 30 | Ngày hoàn thành/tốt nghiệp | function/output | `NgayRaQDTN`, `NgayCapCCN`, `KhoaHoc.NgayBG` | Không thấy filter luồng chính | Chỉ dùng tính/output | V2 | Copy |
| 31 | Đã có BCII khác | các điều kiện trong tìm thêm tay bị comment; tổng hợp không kiểm tra | `NguoiLX_HoSo.MaBC2` | Không được loại | Không được cưỡng chế | V1 | Bắt buộc Worker preserve và thêm guard |
| 32 | Đã/chờ/vắng/rớt sát hạch | `TT_XuLy` | `11,12,13,14,16,17,18,19` | Luồng chính không chọn; thêm tay lại cho phép `14/17/18` | Tùy thao tác | V1 | Tuyệt đối không overwrite |
| 33 | Giáo viên | — | `GiaoVien`, `KhoaHoc_GiaoVien` | Không được core BCII proc đọc | Không thấy | V2 | Optional sync, không readiness gate |
| 34 | Xe tập | — | `XeTap`, `KhoaHoc_XeTap` | Không được core BCII proc đọc | Không thấy | V2 | Optional sync, không readiness gate |
| 35 | Lưu lượng | — | `DM_LuuLuongDaoTao` | Không được core BCII proc đọc | Không thấy | V2 | Optional; BAK hiện 0 row |
| 36 | Số giờ/km đào tạo | output | `SoNamLX`, `SoKmLXAnToan`, các field KQĐT/thời gian | Không filter | Chỉ dùng output | V2 | Copy |

### 5.1. Điều kiện nhánh hạng đúng nguyên văn logic

Danh sách hiển thị:

```text
IF (
    @MaKH LIKE '%A1%'
    OR @MaKH LIKE '%A2%'
    OR @MaKH LIKE '%A%'
    OR @HangGPLX LIKE '%A3%'
)
```

Nhánh trên lọc:

```text
B.MaKhoaHoc = @MaKH
AND B.TT_XuLy IN ('03','04')
AND B.TrangThai = 1
```

Nhánh còn lại lọc:

```text
B.MaKhoaHoc = @MaKH
AND B.TT_XuLy = '09'
```

Procedure tổng hợp dùng nhánh gần tương đương:

```text
IF (@MaKH LIKE '%A%' OR @HangGPLX LIKE '%A3%')
```

nhưng update nhánh A chỉ có:

```text
WHERE MaKhoaHoc = @MaKH
  AND TT_XuLy IN ('03','04')
```

không có `TrangThai=1`.

## 6. Điều kiện loại học viên và trạng thái

### 6.1. Luồng lập theo cả khóa

| Nhánh | Được chọn | Bị loại |
| --- | --- | --- |
| Nhánh A theo expression ở trên | `TT_XuLy 03/04` và `TrangThai=1` | Mọi trạng thái khác; hoặc hồ sơ không active |
| Nhánh khác | `TT_XuLy=09` | Mọi trạng thái khác; `TrangThai` không được xét |

Mã trạng thái thực tế từ `DM_TrangThai`:

| Mã | Ý nghĩa |
| --- | --- |
| `03` | Đã nhận ảnh chân dung |
| `04` | Nhận hồ sơ từ VPĐK |
| `09` | Đạt tốt nghiệp |
| `11` | Đã lập BC2 |
| `12` | Đã gửi BC2 |
| `13` | Đạt BC2 |
| `14` | Chưa đạt BC2 |
| `16` | Đạt SH |
| `17` | Chưa đạt SH |
| `18` | Vắng SH |
| `19` | Đã nhận GPLX |

### 6.2. Thêm học viên thủ công

Procedure tìm thêm tay cho phép `TT_XuLy IN ('03','09','14','17','18')`, khóa đã bế giảng và hạng phù hợp. Đây là nghiệp vụ rộng hơn luồng tổng hợp cả khóa: học viên chưa đạt BCII/rớt/vắng SH có thể được đăng ký lại.

Không dùng kết quả tìm thêm tay làm bằng chứng rằng học viên chưa có BCII; điều kiện chống trùng đang bị comment.

### 6.3. Điểm chưa an toàn

- Tất cả procedure thêm/bỏ/update kết quả chủ yếu update theo `MaDK`; tham số `SoHoSo` thường không tham gia WHERE. Schema hiện cũng chỉ cho một row `NguoiLX_HoSo` trên mỗi `MaDK`.
- `usp_NguoiLX_HoSo_UpdateKQBC2` và `usp_NguoiLX_HoSo_UpdateKQSH` bỏ điều kiện `SoHoSo`.
- `usp_CSDT_PheDuyetKQDT_TiepNhan` nhận `@MaBC2` nhưng điều kiện `AND MaBC2=@MaBC2` bị comment.

## 7. Data ownership

### 7.1. V2-owned

Các domain sau là dữ liệu đào tạo/nguồn cần V2 → V1, với điều kiện mapping và guard schema:

- `DM_DonViGTVT` cho CSDT được đồng bộ;
- `KhoaHoc`;
- `BaoCaoI`;
- `NguoiLX`;
- phần đào tạo/hồ sơ ban đầu của `NguoiLX_HoSo`;
- `NguoiLXHS_GiayTo`;
- `GiaoVien`, `KhoaHoc_GiaoVien`, `XeTap`, `KhoaHoc_XeTap`;
- kết quả hoàn thành đào tạo: `SoGiayCNTN`, `SoCCN`, các cột tốt nghiệp và KQĐT;
- lookup đào tạo cần thiết nếu target thiếu.

### 7.2. V1-owned — không được Worker ghi đè

- toàn bộ `BaoCaoII`;
- `KySH`;
- `DM_LyDoTCBC2`, dữ liệu kết quả/duyệt sát hạch;
- `NguoiLX_GPLX` đã tồn tại ở V1;
- các cột BCII/sát hạch trong `NguoiLX_HoSo`:

  ```text
  NoiDungSH
  MaBC2, KetQuaBC2, MaLyDoTCBC2
  MaKySH, SoBD, LanSH, SoQDSH, NgayQDSH
  KetQua_LyThuyet, NhanXet_LyThuyet
  KetQuaSHM, NhanXet_MoPhong
  KetQua_Hinh, NhanXet_Hinh
  KetQua_Duong, NhanXet_Duong
  KetQuaSH
  SoQDTT, NgayQDTT, NguoiKy
  SoGPLXTmp
  NgayKTBC2, NguoiKTBC2
  MaIn, KetQuaDoiSanhTW, GhiChuKQDSTW, ChuKy
  TT_XuLy_Old
  CHON_IN_GPLX
  KetQuaPDSo
  DAT_QDThucHanh, DAT_TGThucHanh, DAT_KQCuc, DAT_ThoiGianLayKQ
  LyDoTuChoiKQDT
  ```

### 7.3. Shared/conditional trong `NguoiLX_HoSo`

| Cột/nhóm | Chính sách |
| --- | --- |
| `MaDK`, `IDs` | Immutable; không update |
| `NguoiTao`, `NgayTao` | Immutable trên row đã tồn tại |
| `NguoiSua`, `NgaySua` | Không copy mù audit V2 sang row V1 đã có; Worker ghi audit riêng nếu cần |
| `GhiChu` | Shared; preserve khi target đã có lifecycle BCII/SH |
| `TT_XuLy` | V2 được ghi các trạng thái đào tạo trước BCII; khi target có `MaBC2`, `MaKySH`, kết quả BCII/SH hoặc `TT_XuLy IN ('11','12','13','14','16','17','18','19')`, V1 thắng |
| `TrangThai` | Dữ liệu hồ sơ từ V2, nhưng cần preserve/merge nếu target đã vào lifecycle V1; SQL nhánh A phụ thuộc cột này |
| `GiaiTrinh` | Shared giữa dữ liệu KQĐT và xử lý phê duyệt; không update mù sau khi target có dữ liệu V1 |
| `GiayCNSK` | Nguồn đào tạo, nhưng cũng được procedure tiếp nhận KQĐT cập nhật; chỉ update trước lifecycle V1 hoặc theo policy được phê duyệt |
| `MaBC1`, `KQ_BC1*`, `NgayKTBC1`, `NguoiKTBC1` | Thuộc đào tạo/BCI; update được nếu không làm thay đổi liên kết của row đã có BCII |

Điều kiện nhận biết row đã có dữ liệu V1 cần preserve:

```text
MaBC2 non-empty
OR MaKySH non-null
OR KetQuaBC2/KetQuaSH non-null
OR TT_XuLy IN ('11','12','13','14','16','17','18','19')
OR bất kỳ cột kỳ/SBD/quyết định/kết quả sát hạch non-null
```

## 8. So sánh OTO và MOTO

| Hạng mục | Kết quả |
| --- | --- |
| Object liên quan V1_BAK | Giống nhau; 0 row khác biệt object |
| 20 bảng lõi V1_BAK | 0 khác biệt metadata cột |
| Module BCII | 0 khác biệt definition |
| Schema nguồn OTO_BAK/MOTO_BAK liên quan | 0 khác biệt metadata cột |
| Điều kiện BCII | Giống nhau |
| Domain chỉ OTO | Không thấy trong phạm vi object/schema BCII |
| Domain chỉ MOTO | Không thấy trong phạm vi object/schema BCII |
| Mapping | Dùng chung mapping logic hiện tại được; tách profile/stream và drift gate |

Khác biệt hiện tại chỉ ở dữ liệu tổng hợp:

| Chỉ số | OTO_BAK | MOTO_BAK |
| --- | ---: | ---: |
| Khóa | 3 | 2 |
| Hồ sơ/người lái | 108 | 5 |
| BCI | 0 | 0 |
| BCII | 0 | 0 |
| Kỳ SH | 0 | 0 |
| Giấy tờ hồ sơ | 324 | 15 |

Không dùng các số lượng này để suy ra khác biệt nghiệp vụ OTO/MOTO.

## 9. Missing data/mapping từ V2_BAK

### 9.1. Khác biệt schema V2_BAK → V1_BAK

| Bảng/cột | V2_BAK | V1_BAK | Phân loại | Phương án |
| --- | --- | --- | --- | --- |
| `DM_DonViGTVT.CoQuanQL` | `nvarchar(1000)` | `nvarchar(100)` | Khác độ dài | Reject nếu >100; không truncate |
| `DM_DonViGTVT.TenDV` | `nvarchar(1000)` | `nvarchar(100)` | Khác độ dài | Reject nếu >100 |
| `GiaoVien.GhiChu` | `nvarchar(500)` | `nvarchar(255)` | Khác độ dài | Guard |
| `GiaoVien.HangGPLX` | `nvarchar(50) NULL` | `varchar(3) NOT NULL` | Khác kiểu/độ dài/null | Mapping hạng + lookup; reject Unicode/không map |
| `GiaoVien.HinhThuc_TuyenDung` | `nvarchar(50)` | `varchar(2)` | Khác kiểu/độ dài | Mapping riêng |
| `GiaoVien.LoaiHinh_DaoTao` | `nvarchar(500)` | `varchar(2)` | Khác kiểu/độ dài | Mapping riêng |
| `GiaoVien.CacHangDaCo` | Có | Không có | V2-only | Không sync |
| `GiaoVien.CacMonHoc` | Có | Không có | V2-only | Không sync |
| `GiaoVien.LoaiGiaoVien` | Có | Không có | V2-only | Không sync |
| `GiaoVien.NgayHHGPLX` | Có | Không có | V2-only | Không sync |
| `GiaoVien.NoiCapGCN` | Có | Không có | V2-only | Không sync |
| `KhoaHoc_XeTap.MaGV` | NULL được | NOT NULL | Khác nullability | Chỉ insert row map được giáo viên |
| `NguoiLX_HoSo.QDThucHanhHinh` | Có | Không có | V2-only | Không sync; chưa thấy BCII V1 dùng |

Ngoài 13 dòng trên, metadata tên/kiểu/độ dài/nullability/identity của các bảng liên quan được so sánh là tương thích.

### 9.2. Dữ liệu V1 cần cho BCII

| Dữ liệu V1 cần | Có ở V2_BAK? | Bảng/cột V2 | Mapping V1 | Tình trạng hiện tại |
| --- | --- | --- | --- | --- |
| Đơn vị/CSDT | Có trực tiếp | `DM_DonViGTVT` | Cùng bảng/khóa | 36 row nguồn; cả 36 thiếu `SoGP` |
| Khóa | Có trực tiếp | `KhoaHoc` | Cùng bảng | OTO 3, MOTO 2 |
| BCI | Schema có, dữ liệu hiện không có | `BaoCaoI` | Cùng bảng | 0 cả hai; blocker tuyệt đối cho màn hình chọn |
| Người lái | Có trực tiếp | `NguoiLX` | Cùng bảng | OTO 108, MOTO 5 |
| Hồ sơ khóa | Có trực tiếp | `NguoiLX_HoSo` | Cùng bảng | OTO 108, MOTO 5 |
| Giấy tờ | Có trực tiếp | `NguoiLXHS_GiayTo` | Cùng bảng | Không phải SQL eligibility |
| Ảnh | Cột có | `NguoiLX_HoSo.DuongDanAnh` | Cùng cột | Thiếu OTO 40/108; MOTO 5/5 |
| Số CNTN | Cột có | `NguoiLX_HoSo.SoGiayCNTN` | Cùng cột `nvarchar(30)` | Thiếu OTO 108/108; MOTO 5/5 |
| GPLX cũ | Cột có | các cột `*GPLXDaCo` | Cùng cột | Thiếu số/hạng OTO 45/108; MOTO 5/5; chỉ cần theo loại hồ sơ |
| Kết quả đào tạo | Có cột | `KQLyThuyet`, `KQThucHanh`, điểm/thời gian, `KetLuanCSDT` | Cùng cột | Không phải filter eligibility hiện tại |
| Header BCII | Schema vật lý có nhưng không authoritative | `BaoCaoII` | Không mapping | V1 tự tạo |
| `MaBC2` và trạng thái/kết quả SH | Schema vật lý có nhưng không authoritative | `NguoiLX_HoSo` | Không mapping | V1 tự tạo/giữ |
| Kỳ SH/SBD/kết quả SH | Schema có, dữ liệu 0 | `KySH`, cột hồ sơ | Không mapping | V1 tự tạo |
| Giáo viên/xe/lưu lượng | Schema có | resource tables | Cùng bảng với guard | Dữ liệu hiện đều 0; core BCII không gate |

### 9.3. Dữ liệu source hiện chưa sẵn sàng

- Không có BCI nào trong OTO_BAK hoặc MOTO_BAK, nên `usp_BaoCaoI_Search(@TrangThai=2)` không thể trả khóa để lập BCII.
- Theo predicate chính xác của `usp_NguoiLX_Select_By_MaKH2`, số hồ sơ eligible hiện là 0 ở cả hai source:
  - OTO: `TT_XuLy 01 = 40`, `03 = 68`, nhưng theo nhánh khóa hiện tại cả 108 bị loại bởi trạng thái;
  - MOTO: `TT_XuLy 01 = 5`, cả 5 bị loại.
- Không có giáo viên, quan hệ khóa–giáo viên, xe, quan hệ khóa–xe hoặc lưu lượng trong hai source BAK.
- Đây là kết quả dataset hiện tại, không phải quy tắc tổng quát của V2.

## 10. Thống kê read-only

### 10.1. V1 target

Cả `CSDL_OTO_V1_BAK`, `CSDL_OTO_V1`, `CSDL_MOTO_V1_BAK`, `CSDL_MOTO_V1`:

- `BaoCaoI`: 0;
- `BaoCaoII`: 0;
- `KhoaHoc`: 0;
- `NguoiLX`: 0;
- `NguoiLX_HoSo`: 0;
- `NguoiLX_GPLX`: 0;
- `NguoiLXHS_GiayTo`: 0;
- `KySH`: 0.

Lookup mỗi V1_BAK:

- `DM_DonViGTVT`: 1.579;
- `DM_DVHC`: 15.755;
- `DM_GiayTo`: 25;
- `DM_HangDT`: 54;
- `DM_HangGPLX`: 26;
- `DM_LoaiHSo`: 3;
- `DM_TrangThai`: 19;
- `DM_NoiDungSH`: 17;
- `DM_LuuLuongDaoTao`: 0.

### 10.2. V2_BAK source

| Chỉ số | OTO_BAK | MOTO_BAK |
| --- | ---: | ---: |
| Khóa có BCI | 0 | 0 |
| BCI đủ predicate search | 0 | 0 |
| Khóa chưa có BCII trong tập trên | 0 | 0 |
| Học viên/khóa min–max–avg | 20–48–36,00 | 1–4–2,50 |
| Hồ sơ eligible theo display proc | 0 | 0 |
| Hồ sơ Worker tổng hợp sẽ update | 0 | 0 |
| Hồ sơ eligible đã có MaBC2 | 0 | 0 |
| Orphan BCI→khóa | 0 | 0 |
| Orphan BCII→BCI | 0 | 0 |
| Orphan hồ sơ→người lái/khóa/BCI/BCII/kỳ | 0 | 0 |
| Orphan giấy tờ→hồ sơ | 0 | 0 |

Không có mã khóa, họ tên, số định danh, địa chỉ hoặc dữ liệu cá nhân nào được xuất trong khảo sát.

## 11. Whitelist sync đề xuất

### 11.1. Thứ tự FK để đạt readiness

1. Lookup đã có hoặc insert-if-missing: `DM_DVHC`, `DM_QuocTich`, `DM_HangGPLX`, `DM_HangDT`, `DM_LoaiHSo`, `DM_HTCapGPLX`, `DM_GiayTo`.
2. `DM_DonViGTVT`.
3. Optional resource master: `GiaoVien`, `XeTap`, `DM_LuuLuongDaoTao`.
4. `KhoaHoc`.
5. Optional relation: `KhoaHoc_GiaoVien`, `KhoaHoc_XeTap`.
6. `BaoCaoI`.
7. `NguoiLX`.
8. `NguoiLX_HoSo`.
9. `NguoiLXHS_GiayTo`.
10. `NguoiLX_GPLX`: chỉ insert-if-missing sau review; không update row V1 đã có.

### 11.2. Bảng được INSERT

| Bảng | Chính sách |
| --- | --- |
| Lookup đào tạo | Insert-if-missing; không xóa; không đồng bộ lookup sát hạch từ V2 |
| `DM_DonViGTVT` | Insert-if-missing theo `MaDV`; update whitelist cho đúng CSDT |
| `KhoaHoc` | Insert-if-missing theo `MaKH` |
| `BaoCaoI` | Insert-if-missing theo `MaBCI`, sau khi khóa tồn tại |
| `NguoiLX` | Insert-if-missing theo `MaDK` |
| `NguoiLX_HoSo` | Insert-if-missing theo `MaDK`; cột V1-owned để default/null |
| `NguoiLXHS_GiayTo` | Insert-if-missing theo `(MaGT,MaDK)` |
| Resource tables | Insert-if-missing khi mapping kiểu/identity hợp lệ |
| `NguoiLX_GPLX` | Chỉ insert-if-missing nếu xác nhận row là GPLX cũ nguồn, không phải GPLX V1 phát sinh |

### 11.3. Bảng/cột được UPDATE

`DM_DonViGTVT`: chỉ các cột master đào tạo đã duyệt, với guard độ dài; không update key.

`KhoaHoc`: các cột đào tạo, không update `MaKH`, `NguoiTao`, `NgayTao`; không được dùng update để đổi khóa của row đã có BCII.

`BaoCaoI`: update dữ liệu BCI V2-owned, không update `MaBCI`; trước khi thay `MaKH`/`MaCSDT`, chặn nếu target đã có BCII hoặc hồ sơ lifecycle V1.

`NguoiLX`: update các cột nhận dạng/hồ sơ V2-owned; không update `MaDK`, `NguoiTao`, `NgayTao`; cần conflict policy nếu V1 đã nhận sửa từ nghiệp vụ ngoài V2.

`NguoiLX_HoSo`: update whitelist đào tạo sau:

```text
SoHoSo, MaCSDT, MaSoGTVT, MaDVNhanHSo
NgayNhanHSo, NguoiNhanHSo, NgayHenTra, MaLoaiHs
DuongDanAnh, ChatLuongAnh, NgayThuNhanAnh, NguoiThuNhanAnh
SoGPLXDaCo, HangGPLXDaCo, DonViCapGPLXDaCo, NoiCapGPLXDaCo
NgayCapGPLXDaCo, NgayHHGPLXDaCo, NgayTTGPLXDaCo
DonViHocLX, NamHocLX, HangGPLX, SoNamLX, SoKmLXAnToan
LyDoCapDoi, MucDichCapDoi
MaKhoaHoc, HangDaoTao, SoGiayCNTN, SoCCN
MaBC1, BC1_TuoiTS, BC1_ThamNien, NgayKTBC1, NguoiKTBC1
KQ_BC1, KQ_BC1_GhiChu
VaoSoCNNSo, NgayVaoSoCNN, XepLoaiTotNghiep, NgayCapCCN
SoQuyetDinhTN, NgayRaQDTN, SoSoTN, NgayVaoSoTN, NgayInGiayTN
NamcapLandau, MaTrichNgang
KQLyThuyet, KQThucHanh, TongQDThucHanh, KetLuanCSDT
DiemKQLyThuyet, DiemKQThucHanh
TGBatDau, TGKetThuc, TGThucHanhHinh, TGThucHanhDuong
```

Các cột `GiayCNSK`, `GiaiTrinh`, `TrangThai`, `GhiChu`, `TT_XuLy`, `MaBC1` chỉ update theo merge policy conditional ở mục 7.3.

### 11.4. Tuyệt đối không chạm

```text
dbo.BaoCaoII: toàn bộ bảng
dbo.KySH: toàn bộ bảng
dbo.DM_LyDoTCBC2 / lookup sát hạch phát sinh ở V1
dbo.NguoiLX_GPLX: mọi row đã tồn tại ở V1
dbo.NguoiLX_HoSo: toàn bộ danh sách cột V1-owned ở mục 7.2
```

Không DELETE target khi row biến mất ở V2 trong các domain có thể đã tham gia BCII/sát hạch.

### 11.5. Audit implementation hiện tại

`CsdtRealtimeDomainCatalog` không có `BaoCaoII`, `KySH`, đây là điểm đúng. Tuy nhiên:

- `CsdtRealtimeTableMetadata.WritableColumns` = mọi cột không computed;
- `UpdateChangedAsync` update mọi cột writable không phải PK/identity;
- catalog có `NguoiLX_HoSo` và `NguoiLX_GPLX`.

Vì vậy implementation hiện tại không đáp ứng whitelist cột ở trên. Chưa được phép tuyên bố “Ready for BaoCaoII” cho sync realtime cho đến khi có column-level ownership/merge guard và test chứng minh target V1-owned được giữ nguyên.

### 11.6. Điều kiện “Ready for BaoCaoII”

**SQL-enforced/application-observed readiness** cho một BCI/khóa:

1. `DM_DonViGTVT` và `KhoaHoc` tồn tại, FK hợp lệ.
2. `BaoCaoI` tồn tại.
3. `BaoCaoI.MaBCI NOT LIKE 'A1A2%'`.
4. `BaoCaoI.MaKH NOT LIKE '%DB%'`.
5. Không có `BaoCaoII` cùng `MaBCI` tại target; kiểm tra lại trong transaction trước insert.
6. Có ít nhất một `NguoiLX_HoSo` join được `NguoiLX` và thỏa nhánh trạng thái trong mục 5.1.
7. Không row dự định tổng hợp nào đã có `MaBC2` khác hoặc dữ liệu BCII/SH, trừ luồng đăng ký lại được định nghĩa rõ.
8. Sync không update bất kỳ cột V1-owned nào.

**Data-completeness khuyến nghị, chưa phải eligibility SQL**:

- kiểm tra ngày sinh/số định danh/ảnh;
- số CNTN hoặc CCN cần cho output;
- dữ liệu GPLX cũ cho hồ sơ nâng hạng;
- hạng/lookup join được;
- giấy tờ cần theo quy định ngoài SQL;
- ngày hoàn thành/tốt nghiệp và kết quả đào tạo hợp lệ;
- header count `TongSoThiSinh` khớp số detail thực tế.

## 12. Điều kiện chưa thể kết luận

1. `BaoCaoI.TrangThai` bắt buộc ở giá trị nào: filter trong `usp_BaoCaoI_Search` đang comment.
2. `KhoaHoc.TrangThai`/`TT_Xuly` nào là khóa hoạt động, khóa, hủy.
3. Đơn vị phải active, giấy phép còn hạn hay bắt buộc có `SoGP` hay không.
4. Ảnh, CCCD, giấy tờ, GPLX cũ, `SoGiayCNTN` có bắt buộc pháp lý hay chỉ bắt buộc khi xuất XML.
5. Giáo viên, xe tập, lưu lượng, giờ/km có phải gate bên application/code ngoài SQL đã khảo sát hay không.
6. Ý nghĩa authoritative của các bảng/cột sát hạch vẫn còn vật lý trong V2_BAK; theo nghiệp vụ đã xác nhận chúng không phải nguồn sync.
7. `BaoCaoII.TrangThai=1` chính xác là gửi, khóa, duyệt KQĐT hay nhiều ý nghĩa gộp.
8. `BaoCaoII.TT_Xuly` có state machine nào ngoài `Search_KQ`.
9. Policy cho học viên `14/17/18` đăng ký lại và việc tái sử dụng/đổi `MaBC2`.
10. Có cho phép nhiều BCII cho một BCI không; schema cho phép nhưng application có lookup chống trùng.
11. Khi hủy BCII, phải xóa `MaBC2` hay giữ lịch sử; hai procedure hiện không nhất quán.
12. `NguoiLX_HoSo.TrangThai=0` ở V2_BAK hiện có nghĩa inactive thật hay dữ liệu chưa khởi tạo.

## 13. Các query read-only đã chạy

Các query dưới đây là nhóm truy vấn thành công đã dùng. Tất cả đều chỉ `SELECT` metadata/count/aggregate. Các database OTO/MOTO tương ứng được chạy cùng mẫu.

### 13.1. Kiểm tra database

```sql
USE [master];
GO

SELECT name, state_desc, compatibility_level
FROM sys.databases
WHERE name IN (
    N'CSDL_OTO_V1_BAK', N'CSDL_MOTO_V1_BAK',
    N'CSDL_OTO_V1', N'CSDL_MOTO_V1',
    N'CSDL_OTO_BAK', N'CSDL_MOTO_BAK',
    N'CSDL_OTO', N'CSDL_MOTO'
)
ORDER BY name;
GO
```

### 13.2. Inventory tên và definition

```sql
USE [CSDL_OTO_V1_BAK];
GO

SELECT DISTINCT s.name, o.name, o.type_desc
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.is_ms_shipped = 0
  AND (
      o.name LIKE N'%BaoCaoII%'
      OR o.name LIKE N'%BaoCao2%'
      OR o.name LIKE N'%BaoCaoI%'
      OR o.name LIKE N'%SatHach%'
      OR o.name LIKE N'%KhoaHoc%'
      OR o.name LIKE N'%NguoiLX%'
      OR m.definition LIKE N'%BaoCaoII%'
      OR m.definition LIKE N'%BaoCao2%'
      OR m.definition LIKE N'%MaBC2%'
  )
ORDER BY o.type_desc, s.name, o.name;
GO
```

### 13.3. Module có BCII/BC2

```sql
USE [CSDL_MOTO_V1_BAK];
GO

SELECT s.name, o.name, o.type_desc, LEN(m.definition) AS DefinitionLength
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE m.definition LIKE N'%BaoCaoII%'
   OR m.definition LIKE N'%BaoCao2%'
   OR m.definition LIKE N'%MaBC2%'
   OR m.definition LIKE N'%BC2%'
ORDER BY o.type_desc, s.name, o.name;
GO
```

### 13.4. Cột, default, PK/UQ, FK, check, index, trigger

```sql
USE [CSDL_OTO_V1_BAK];
GO

SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    c.column_id,
    c.name AS ColumnName,
    ty.name AS DataType,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable,
    c.is_identity,
    c.is_computed,
    dc.name AS DefaultName,
    dc.definition
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
  ON dc.parent_object_id = c.object_id
 AND dc.parent_column_id = c.column_id
WHERE t.name IN (
    N'BaoCaoI', N'BaoCaoII', N'KhoaHoc', N'NguoiLX',
    N'NguoiLX_HoSo', N'NguoiLX_GPLX', N'NguoiLXHS_GiayTo',
    N'DM_DonViGTVT', N'DM_LuuLuongDaoTao',
    N'KhoaHoc_GiaoVien', N'KhoaHoc_XeTap', N'KySH'
)
ORDER BY t.name, c.column_id;
GO
```

### 13.5. Parameters và dependencies

```sql
USE [CSDL_OTO_V1_BAK];
GO

SELECT
    s.name,
    o.name,
    p.parameter_id,
    p.name AS ParameterName,
    TYPE_NAME(p.user_type_id) AS DataType,
    p.max_length,
    p.is_output
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.parameters p ON p.object_id = o.object_id
WHERE o.name LIKE N'%BaoCaoII%'
   OR o.name LIKE N'%BaoCao2%'
   OR o.name IN (
       N'usp_NguoiLX_Select_By_MaKH2',
       N'usp_NguoiLX_TongHop_By_MaKH2',
       N'usp_NguoiLX_HoSo_ThemHS_BC2'
   )
ORDER BY o.name, p.parameter_id;
GO
```

### 13.6. View/function/trigger/synonym

```sql
USE [CSDL_OTO_V1_BAK];
GO

SELECT o.type_desc, OBJECT_SCHEMA_NAME(o.object_id), o.name
FROM sys.objects o
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
WHERE o.type IN ('V','FN','IF','TF','TR')
  AND (
      o.name LIKE N'%BaoCao%'
      OR o.name LIKE N'%SatHach%'
      OR o.name LIKE N'%KhoaHoc%'
      OR m.definition LIKE N'%BaoCaoII%'
      OR m.definition LIKE N'%BaoCao2%'
      OR m.definition LIKE N'%MaBC2%'
  )
ORDER BY o.type_desc, o.name;
GO
```

### 13.7. SQL Agent job

```sql
USE [msdb];
GO

SELECT j.name, s.step_id, s.step_name, s.database_name, s.subsystem
FROM dbo.sysjobs j
JOIN dbo.sysjobsteps s ON s.job_id = j.job_id
WHERE j.name LIKE N'%BaoCao%'
   OR j.name LIKE N'%SatHach%'
   OR s.command LIKE N'%BaoCaoII%'
   OR s.command LIKE N'%BaoCao2%'
   OR s.command LIKE N'%MaBC2%'
ORDER BY j.name, s.step_id;
GO
```

### 13.8. So sánh schema BAK/live và OTO/MOTO

```sql
USE [master];
GO

WITH Bak AS (
    SELECT
        t.name COLLATE Latin1_General_CI_AS AS TableName,
        c.name COLLATE Latin1_General_CI_AS AS ColumnName,
        ty.name COLLATE Latin1_General_CI_AS AS DataType,
        c.max_length, c.precision, c.scale,
        c.is_nullable, c.is_identity, c.is_computed
    FROM CSDL_OTO_V1_BAK.sys.tables t
    JOIN CSDL_OTO_V1_BAK.sys.columns c ON c.object_id = t.object_id
    JOIN CSDL_OTO_V1_BAK.sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE t.name IN (
        N'BaoCaoI', N'BaoCaoII', N'KhoaHoc', N'NguoiLX',
        N'NguoiLX_HoSo', N'NguoiLX_GPLX', N'NguoiLXHS_GiayTo',
        N'DM_DonViGTVT', N'DM_GiayTo', N'DM_LuuLuongDaoTao',
        N'KhoaHoc_GiaoVien', N'KhoaHoc_XeTap', N'KySH',
        N'DM_TrangThai', N'DM_HangDT', N'DM_HangGPLX',
        N'DM_LoaiHSo', N'DM_NoiDungSH', N'GiaoVien', N'XeTap'
    )
),
Live AS (
    SELECT
        t.name COLLATE Latin1_General_CI_AS AS TableName,
        c.name COLLATE Latin1_General_CI_AS AS ColumnName,
        ty.name COLLATE Latin1_General_CI_AS AS DataType,
        c.max_length, c.precision, c.scale,
        c.is_nullable, c.is_identity, c.is_computed
    FROM CSDL_OTO_V1.sys.tables t
    JOIN CSDL_OTO_V1.sys.columns c ON c.object_id = t.object_id
    JOIN CSDL_OTO_V1.sys.types ty ON ty.user_type_id = c.user_type_id
    WHERE t.name IN (
        N'BaoCaoI', N'BaoCaoII', N'KhoaHoc', N'NguoiLX',
        N'NguoiLX_HoSo', N'NguoiLX_GPLX', N'NguoiLXHS_GiayTo',
        N'DM_DonViGTVT', N'DM_GiayTo', N'DM_LuuLuongDaoTao',
        N'KhoaHoc_GiaoVien', N'KhoaHoc_XeTap', N'KySH',
        N'DM_TrangThai', N'DM_HangDT', N'DM_HangGPLX',
        N'DM_LoaiHSo', N'DM_NoiDungSH', N'GiaoVien', N'XeTap'
    )
)
SELECT N'BAK_ONLY' AS Side, d.*
FROM (SELECT * FROM Bak EXCEPT SELECT * FROM Live) d
UNION ALL
SELECT N'LIVE_ONLY', d.*
FROM (SELECT * FROM Live EXCEPT SELECT * FROM Bak) d;
GO
```

Cùng query được chạy cho cặp MOTO BAK/live và thay hai phía bằng
OTO_V1_BAK/MOTO_V1_BAK, OTO_BAK/MOTO_BAK. Module được so sánh thêm bằng
tên/type và equality toàn bộ `sys.sql_modules.definition`.

### 13.9. Row count nghiệp vụ

```sql
USE [CSDL_OTO_BAK];
GO

SELECT N'BaoCaoI' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.BaoCaoI
UNION ALL SELECT N'BaoCaoII', COUNT_BIG(*) FROM dbo.BaoCaoII
UNION ALL SELECT N'KhoaHoc', COUNT_BIG(*) FROM dbo.KhoaHoc
UNION ALL SELECT N'NguoiLX', COUNT_BIG(*) FROM dbo.NguoiLX
UNION ALL SELECT N'NguoiLX_HoSo', COUNT_BIG(*) FROM dbo.NguoiLX_HoSo
UNION ALL SELECT N'NguoiLX_GPLX', COUNT_BIG(*) FROM dbo.NguoiLX_GPLX
UNION ALL SELECT N'NguoiLXHS_GiayTo', COUNT_BIG(*) FROM dbo.NguoiLXHS_GiayTo
UNION ALL SELECT N'KySH', COUNT_BIG(*) FROM dbo.KySH;
GO
```

### 13.10. Eligibility học viên

```sql
USE [CSDL_OTO_BAK];
GO

SELECT COUNT_BIG(*) AS EligibleRows
FROM dbo.NguoiLX_HoSo hs
JOIN dbo.KhoaHoc kh ON kh.MaKH = hs.MaKhoaHoc
JOIN dbo.NguoiLX nl ON nl.MaDK = hs.MaDK
WHERE (
        (
            kh.MaKH LIKE '%A1%'
            OR kh.MaKH LIKE '%A2%'
            OR kh.MaKH LIKE '%A%'
            OR kh.HangGPLX LIKE '%A3%'
        )
        AND hs.TT_XuLy IN ('03','04')
        AND hs.TrangThai = 1
      )
   OR (
        NOT (
            kh.MaKH LIKE '%A1%'
            OR kh.MaKH LIKE '%A2%'
            OR kh.MaKH LIKE '%A%'
            OR kh.HangGPLX LIKE '%A3%'
        )
        AND hs.TT_XuLy = '09'
      );
GO
```

### 13.11. Orphan/FK mismatch

```sql
USE [CSDL_MOTO_BAK];
GO

SELECT
    (SELECT COUNT_BIG(*)
     FROM dbo.BaoCaoII b
     LEFT JOIN dbo.BaoCaoI i ON i.MaBCI = b.MaBCI
     WHERE b.MaBCI IS NOT NULL AND i.MaBCI IS NULL) AS OrphanBaoCaoIIToBaoCaoI,
    (SELECT COUNT_BIG(*)
     FROM dbo.NguoiLX_HoSo h
     LEFT JOIN dbo.NguoiLX n ON n.MaDK = h.MaDK
     WHERE n.MaDK IS NULL) AS OrphanHoSoToNguoiLX,
    (SELECT COUNT_BIG(*)
     FROM dbo.NguoiLX_HoSo h
     LEFT JOIN dbo.KhoaHoc k ON k.MaKH = h.MaKhoaHoc
     WHERE h.MaKhoaHoc IS NOT NULL AND k.MaKH IS NULL) AS OrphanHoSoToCourse,
    (SELECT COUNT_BIG(*)
     FROM dbo.NguoiLX_HoSo h
     LEFT JOIN dbo.BaoCaoII b ON b.MaBCII = h.MaBC2
     WHERE NULLIF(LTRIM(RTRIM(h.MaBC2)),'') IS NOT NULL
       AND b.MaBCII IS NULL) AS OrphanHoSoToBaoCaoII;
GO
```

### 13.12. Thiếu trường tổng hợp

```sql
USE [CSDL_OTO_BAK];
GO

SELECT
    COUNT_BIG(*) AS TotalDossiers,
    SUM(CASE WHEN NULLIF(LTRIM(RTRIM(hs.DuongDanAnh)),'') IS NULL THEN 1 ELSE 0 END)
        AS MissingPhotoPath,
    SUM(CASE WHEN NULLIF(LTRIM(RTRIM(hs.SoGiayCNTN)),'') IS NULL THEN 1 ELSE 0 END)
        AS MissingSoGiayCNTN,
    SUM(CASE WHEN NULLIF(LTRIM(RTRIM(hs.SoGPLXDaCo)),'') IS NULL THEN 1 ELSE 0 END)
        AS MissingOldLicenseNumber
FROM dbo.NguoiLX_HoSo hs;
GO
```

## 14. Phụ lục schema đầy đủ của bảng detail/shared

### 14.1. `dbo.NguoiLX_HoSo`

```text
  1 MaDK varchar(25) NOT NULL PK/UQ
  2 SoHoSo varchar(18) NOT NULL
  3 MaCSDT varchar(6) NOT NULL
  4 MaSoGTVT varchar(6) NOT NULL
  5 MaDVNhanHSo varchar(6) NOT NULL
  6 NgayNhanHSo datetime NOT NULL
  7 NguoiNhanHSo nvarchar(50) NULL
  8 NgayHenTra datetime NULL
  9 MaLoaiHs int NOT NULL
 10 TT_XuLy varchar(2) NOT NULL
 11 DuongDanAnh nvarchar(255) NULL
 12 ChatLuongAnh int NULL
 13 NgayThuNhanAnh datetime NULL
 14 NguoiThuNhanAnh nvarchar(50) NULL
 15 SoGPLXDaCo varchar(100) NULL
 16 HangGPLXDaCo varchar(100) NULL
 17 DonViCapGPLXDaCo varchar(100) NULL
 18 NoiCapGPLXDaCo nvarchar(500) NULL
 19 NgayCapGPLXDaCo varchar(100) NULL
 20 NgayHHGPLXDaCo varchar(100) NULL
 21 NgayTTGPLXDaCo varchar(100) NULL
 22 DonViHocLX varchar(6) NULL
 23 NamHocLX int NULL
 24 HangGPLX varchar(3) NOT NULL
 25 SoNamLX int NULL
 26 SoKmLXAnToan int NULL
 27 GiayCNSK bit NULL DEFAULT 0
 28 LyDoCapDoi nvarchar(50) NULL
 29 MucDichCapDoi nvarchar(50) NULL
 30 NoiDungSH int NULL
 31 MaKhoaHoc varchar(13) NULL
 32 HangDaoTao varchar(20) NULL
 33 SoGiayCNTN nvarchar(30) NULL
 34 SoCCN nvarchar(20) NULL
 35 MaBC1 varchar(18) NULL
 36 BC1_TuoiTS bit NULL DEFAULT 1
 37 BC1_ThamNien bit NULL DEFAULT 1
 38 MaBC2 varchar(13) NULL
 39 KetQuaBC2 bit NULL
 40 MaLyDoTCBC2 int NULL
 41 MaKySH varchar(12) NULL
 42 SoBD varchar(3) NULL
 43 LanSH int NULL DEFAULT 1
 44 SoQDSH nvarchar(20) NULL
 45 NgayQDSH datetime NULL
 46 KetQua_LyThuyet int NULL DEFAULT 0
 47 NhanXet_LyThuyet nvarchar(50) NULL
 48 KetQua_Hinh int NULL DEFAULT 0
 49 NhanXet_Hinh nvarchar(50) NULL
 50 KetQua_Duong int NULL DEFAULT 0
 51 NhanXet_Duong nvarchar(50) NULL
 52 KetQuaSH varchar(2) NULL DEFAULT 1
 53 SoQDTT nvarchar(20) NULL
 54 NgayQDTT datetime NULL
 55 NguoiKy nvarchar(50) NULL
 56 GhiChu nvarchar(255) NULL
 57 NguoiTao nvarchar(30) NULL
 58 NguoiSua nvarchar(30) NULL
 59 NgayTao datetime NOT NULL DEFAULT GETDATE()
 60 NgaySua datetime NOT NULL DEFAULT GETDATE()
 61 SoGPLXTmp varchar(20) NULL
 62 NgayKTBC1 datetime NULL
 63 NguoiKTBC1 nvarchar(50) NULL
 64 NgayKTBC2 datetime NULL
 65 NguoiKTBC2 nvarchar(50) NULL
 66 MaIn nvarchar(255) NULL
 67 KetQuaDoiSanhTW bit NULL
 68 GhiChuKQDSTW nvarchar(255) NULL
 69 ChuKy nvarchar(255) NULL
 70 TrangThai bit NULL DEFAULT 0
 71 MaHTCap varchar(5) NULL
 72 IDs bigint NOT NULL IDENTITY
 73 TT_XuLy_Old varchar(2) NULL
 74 KQ_BC1 bit NULL
 75 KQ_BC1_GhiChu nvarchar(50) NULL
 76 Transfer_flag int NOT NULL DEFAULT 0
 77 VaoSoCNNSo nvarchar(150) NULL
 78 NgayVaoSoCNN datetime NULL
 79 XepLoaiTotNghiep nvarchar(150) NULL
 80 NgayCapCCN datetime NULL
 81 SoQuyetDinhTN nvarchar(50) NULL
 82 NgayRaQDTN datetime NULL
 83 SoSoTN nvarchar(50) NULL
 84 NgayVaoSoTN datetime NULL
 85 NgayInGiayTN datetime NULL
 86 NamcapLandau varchar(4) NULL
 87 MaTrichNgang nvarchar(30) NULL
 88 CoQuanQuanLyGPLX varchar(100) NULL
 89 CHON_IN_GPLX int NULL DEFAULT 0
 90 KetQuaSHM int NULL DEFAULT 0
 91 NhanXet_MoPhong nvarchar(50) NULL
 92 KQLyThuyet bit NULL
 93 KQThucHanh bit NULL
 94 TongQDThucHanh float NULL
 95 KetLuanCSDT bit NULL
 96 GiaiTrinh nvarchar(500) NULL
 97 DiemKQLyThuyet float NULL
 98 DiemKQThucHanh float NULL
 99 TGBatDau varchar(8) NULL
100 TGKetThuc varchar(8) NULL
101 TGThucHanhHinh float NULL
102 TGThucHanhDuong float NULL
103 KetQuaPDSo bit NULL
104 DAT_QDThucHanh varchar(10) NULL
105 DAT_TGThucHanh varchar(10) NULL
106 DAT_KQCuc bit NULL
107 DAT_ThoiGianLayKQ varchar(10) NULL
108 LyDoTuChoiKQDT nvarchar(500) NULL
```

Không computed column, không check constraint, không trigger. `MaDK` là PK/FK; `IDs` là identity nhưng không phải khóa.

### 14.2. Các bảng liên kết/nguồn lực

`NguoiLXHS_GiayTo`:

```text
MaGT int NOT NULL PK
MaDK varchar(25) NOT NULL PK
SoHoSo varchar(18) NOT NULL
TenGT nvarchar(150) NOT NULL
TrangThai bit NOT NULL DEFAULT 1
```

`DM_TrangThai`:

```text
MaTT varchar(2) NOT NULL PK
TenTT nvarchar(50) NOT NULL
GhiChu nvarchar(255) NULL
TrangThai bit NOT NULL DEFAULT 1
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
MaTTCDB nvarchar(2) NULL DEFAULT ''
TenTTCDB nvarchar(50) NULL DEFAULT ''
```

`DM_LuuLuongDaoTao`:

```text
MaCSDT varchar(6) NOT NULL PK
HangGPLX varchar(3) NOT NULL PK
LuuLuong int NULL
GhiChu nvarchar(255) NULL
NguoiTao nvarchar(50) NULL DEFAULT N'ADMIN'
NgayTao datetime NULL DEFAULT GETDATE()
NguoiSua nvarchar(50) NULL DEFAULT N'ADMIN'
NgaySua datetime NULL DEFAULT GETDATE()
```

`KhoaHoc_GiaoVien`:

```text
MaKH varchar(13) NOT NULL
MaGV varchar(8) NOT NULL
TenGV nvarchar(50) NOT NULL
BienSoXe varchar(10) NULL
LoaiGV char(2) NOT NULL
SoHV int NULL DEFAULT 0
NgayHL datetime NULL
NgayHetHL datetime NULL
GhiChu nvarchar(255) NOT NULL
TrangThai bit NOT NULL DEFAULT 1
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
MaLichLV int NOT NULL IDENTITY PK
NgayBD datetime NULL
NgayKT datetime NULL
IsKhoaHocGiaoVien bit NOT NULL DEFAULT 1
MaMonHoc int NULL
TenMonHoc nvarchar(255) NULL
```

`KhoaHoc_XeTap`:

```text
MaLichSD int NOT NULL IDENTITY PK
MaKH varchar(13) NOT NULL
BienSoXe varchar(10) NOT NULL
MaGV varchar(8) NOT NULL
MaHV nvarchar(25) NULL
DiaDiem nvarchar(255) NULL
GhiChu nvarchar(255) NOT NULL
TrangThai bit NOT NULL DEFAULT 1
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
NgayBD datetime NULL
NgayKT datetime NULL
IsKhoaHocXeTap bit NOT NULL DEFAULT 0
TenHV nvarchar(50) NULL
TenGV nvarchar(100) NULL
```

### 14.3. `dbo.KySH`

```text
MaKySH varchar(12) NOT NULL PK
MaTTSH varchar(6) NOT NULL
NgaySH datetime NULL
GioSH int NULL
SoQD nvarchar(20) NOT NULL
NgayQD datetime NOT NULL
NguoiQD nvarchar(50) NULL
ChuTich_HDSH nvarchar(50) NOT NULL
PhoChuTich_HDSH nvarchar(50) NULL
UV_GD_TTSH nvarchar(50) NULL
UV_ToTruong nvarchar(50) NULL
UV_ThuKy nvarchar(50) NULL
TongSoDK int NULL
SoDuSH int NULL
SoDat int NULL
SoKhongDat int NULL
SoVang int NULL
SoVangThiHinh int NULL
SoVangThiDuong int NULL
SoDuSHLanDau int NULL
SoDatSHLanDau int NULL
TyLeDat int NULL
SoViPham nvarchar(100) NULL
SHHaiLan int NULL
NX_ThucHien_QuyChe nvarchar(50) NULL
NX_TrinhDo_CBSH nvarchar(50) NULL
NX_CoSo_VCKT nvarchar(50) NULL
NX_DamBao_AnToan nvarchar(50) NULL
NX_VanDe_Khac nvarchar(50) NULL
NhanXet nvarchar(100) NULL
LePhi_LyThuyet int NULL
LePhi_ThiHinh int NULL
LePhi_ThiDuong int NULL
LePhi_CapGPLX int NULL
GhiChu nvarchar(255) NULL
TrangThai int NULL
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
TenKySH nvarchar(50) NULL
TT_Xuly int NULL
SoVangThiMoPhong int NULL DEFAULT 0
LePhi_MoPhong decimal(10,0) NULL DEFAULT 0
```

### 14.4. `dbo.KhoaHoc`

```text
 1 MaKH varchar(13) NOT NULL PK
 2 MaCSDT varchar(6) NOT NULL
 3 MaSoGTVT varchar(6) NOT NULL
 4 TenKH nvarchar(50) NOT NULL
 5 HangGPLX varchar(3) NOT NULL
 6 HangDT varchar(20) NULL
 7 SoQD_KhaiGiang nvarchar(20) NULL
 8 NgayQD_KhaiGiang datetime NULL
 9 NgayKG datetime NULL
10 NgayBG datetime NULL
11 MucTieuDT nvarchar(1000) NULL
12 NgayThi datetime NULL
13 NgaySH datetime NULL
14 TongSoHV int NULL
15 SoHVTotNghiep int NULL
16 SoHVDuocCapGPLX int NULL
17 ThoiGianDT int NULL
18 SoNgayOnKT int NULL
19 SoNgayThucHoc int NULL
20 SoNgayNghiLe int NULL
21 TongSoNgay int NULL
22 GhiChu nvarchar(255) NULL
23 TrangThai bit NOT NULL DEFAULT 1
24 NguoiTao nvarchar(30) NULL
25 NguoiSua nvarchar(30) NULL
26 NgayTao datetime NOT NULL DEFAULT GETDATE()
27 NgaySua datetime NOT NULL DEFAULT GETDATE()
28 TT_Xuly int NULL
29 HTDaoTao int NULL
```

PK clustered `(MaKH)`; unique constraint `(MaKH,MaCSDT,MaSoGTVT)`. FK tới
`DM_DonViGTVT` qua `MaCSDT`, `MaSoGTVT`; tới `DM_HangGPLX` qua `HangGPLX`;
tới `DM_HangDT` qua `HangDT`.

### 14.5. `dbo.NguoiLX`

```text
 1 MaDK varchar(25) NOT NULL PK
 2 DonViNhanHSo varchar(6) NOT NULL
 3 HoDemNLX nvarchar(30) NOT NULL
 4 TenNLX nvarchar(20) NOT NULL
 5 HoVaTen nvarchar(50) NOT NULL
 6 MaQuocTich varchar(3) NOT NULL
 7 NgaySinh varchar(8) NOT NULL
 8 NoiTT nvarchar(50) NOT NULL
 9 NoiTT_MaDVHC varchar(5) NOT NULL
10 NoiTT_MaDVQL varchar(5) NOT NULL
11 NoiCT nvarchar(50) NOT NULL
12 NoiCT_MaDVHC varchar(5) NOT NULL
13 NoiCT_MaDVQL varchar(5) NOT NULL
14 SoCMT varchar(20) NOT NULL
15 NgayCapCMT datetime NULL
16 NoiCapCMT nvarchar(50) NULL
17 GhiChu nvarchar(255) NULL
18 TrangThai bit NOT NULL DEFAULT 1
19 NguoiTao nvarchar(30) NULL
20 NguoiSua nvarchar(30) NULL
21 NgayTao datetime NOT NULL DEFAULT GETDATE()
22 NgaySua datetime NOT NULL DEFAULT GETDATE()
23 GioiTinh char(1) NOT NULL
24 HoVaTenIn nvarchar(25) NOT NULL
25 SO_CMND_CU varchar(20) NULL
```

FK tới `DM_DonViGTVT` qua `DonViNhanHSo`, tới `DM_QuocTich`, và composite
địa bàn cư trú/thường trú tới `DM_DVHC`. Các FK `NguoiLX` → `DM_DVHC`
đang `is_not_trusted=1`; các FK khác trong tập lõi được kiểm tra là trusted.

### 14.6. `dbo.NguoiLX_GPLX`

```text
 1 MaDK varchar(25) NOT NULL PK
 2 SoGPLX varchar(20) NOT NULL
 3 HangGPLX varchar(3) NOT NULL
 4 SoHoSo varchar(18) NULL
 5 SoGPLXCu varchar(20) NULL
 6 NoiCapGPLX varchar(6) NOT NULL
 7 NgayCapGPLX datetime NOT NULL
 8 CoQuanQLGPLX varchar(6) NOT NULL
 9 NgayHHGPLX datetime NULL
10 NgayTTGPLX datetime NOT NULL
11 MoTaVN nvarchar(255) NULL
12 MoTaEN nvarchar(255) NULL
13 NguoiKy nvarchar(255) NULL
14 MaHTCap varchar(5) NOT NULL
15 NoiHocGPLX varchar(6) NOT NULL
16 NamHocGPLX int NOT NULL
17 DuongDanAnh nvarchar(255) NULL
18 HoTenDem nvarchar(50) NOT NULL
19 TenNLX nvarchar(20) NOT NULL
20 HoVaTen nvarchar(70) NOT NULL
21 NgaySinh varchar(8) NOT NULL
22 MaQuocTich varchar(3) NOT NULL
23 NoiCT nvarchar(50) NULL
24 NoiCT_MaDVHC varchar(5) NULL
25 NoiCT_MaDVQL varchar(5) NULL
26 SoCMT varchar(20) NOT NULL
27 SoSeri varchar(20) NULL
28 NoiIn nvarchar(50) NULL
29 NgayIn datetime NULL
30 NgayTra datetime NULL
31 NguoiTra nvarchar(50) NULL
32 NoiTra nvarchar(50) NULL
33 GhiChu nvarchar(255) NULL
34 NguoiTao nvarchar(30) NULL
35 NguoiSua nvarchar(30) NULL
36 NgayTao datetime NOT NULL DEFAULT GETDATE()
37 NgaySua datetime NOT NULL DEFAULT GETDATE()
38 TrangThai bit NULL DEFAULT 0
39 GioiTinh char(1) NOT NULL
40 NgayTT_A1 datetime NULL
41 NgayTT_A2 datetime NULL
42 NgayTT_A3 datetime NULL
43 NgayTT_A4 datetime NULL
44 NgayTT_B1 datetime NULL
45 NgayTT_B2 datetime NULL
46 NgayTT_C datetime NULL
47 NgayTT_D datetime NULL
48 NgayTT_E datetime NULL
49 NgayTT_F datetime NULL
50 NgayTT_FB2 datetime NULL
51 NgayTT_FC datetime NULL
52 NgayTT_FD datetime NULL
53 NgayTT_FE datetime NULL
```

PK `(MaDK)`, unique `(MaDK,SoHoSo)`, FK `MaDK` → `NguoiLX_HoSo.MaDK`.
Các FK khác tới đơn vị, địa bàn, hạng, hình thức cấp và quốc tịch.

### 14.7. `dbo.DM_DonViGTVT`

```text
 1 MaDV varchar(6) NOT NULL PK
 2 MaDVQL varchar(6) NOT NULL
 3 LoaiDV varchar(2) NOT NULL
 4 TenDV nvarchar(100) NOT NULL
 5 CoQuanQL nvarchar(100) NULL
 6 LoaiTTSH int NULL
 7 CacHangGPLX varchar(50) NULL
 8 DienThoai varchar(20) NULL
 9 Fax varchar(20) NULL
10 DiaChi nvarchar(100) NULL
11 LuuLuongDT int NULL
12 SoGP nvarchar(20) NULL
13 NgayGP datetime NULL
14 LanhDao nvarchar(50) NULL
15 GhiChu nvarchar(255) NULL
16 TrangThai bit NOT NULL DEFAULT 1
17 NguoiTao nvarchar(30) NULL
18 NguoiSua nvarchar(30) NULL
19 NgayTao datetime NOT NULL DEFAULT GETDATE()
20 NgaySua datetime NOT NULL DEFAULT GETDATE()
21 Website varchar(100) NULL
22 DienTichSanTap int NULL
23 NgayHHGP datetime NULL
24 DiaDiemDaoTao nvarchar(300) NULL
25 MaDvOld varchar(6) NULL
```

### 14.8. Lookup giấy tờ/hạng liên quan

`DM_GiayTo`:

```text
MaGT int NOT NULL IDENTITY PK
TenGT nvarchar(150) NOT NULL
TenGTEN nvarchar(150) NULL
SoVBPL nvarchar(50) NULL
GhiChu nvarchar(255) NULL
TrangThai bit NOT NULL DEFAULT 1
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
```

`DM_HangDT`:

```text
MaHangDT varchar(20) NOT NULL PK
TenHangDT nvarchar(50) NOT NULL
HangGPLX varchar(5) NOT NULL
SoVBPL nvarchar(30) NULL
TuoiHV int NULL
ThamNien int NULL
KmLXAT int NULL
DKSH nvarchar(255) NULL
MucTieuDT nvarchar(500) NULL
GhiChu nvarchar(255) NULL
TrangThai bit NOT NULL DEFAULT 1
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
ThoiGianDaoTao int NULL
```

`DM_HangGPLX`:

```text
MaHang varchar(3) NOT NULL PK
TenHang nvarchar(50) NOT NULL
HanSuDung int NOT NULL DEFAULT 0
MoTaVN nvarchar(500) NULL
MoTaEN nvarchar(500) NULL
GhiChu nvarchar(255) NULL
TrangThai bit NOT NULL DEFAULT 1
NguoiTao nvarchar(30) NULL
NguoiSua nvarchar(30) NULL
NgayTao datetime NOT NULL DEFAULT GETDATE()
NgaySua datetime NOT NULL DEFAULT GETDATE()
MaHangMoi nvarchar(5) NULL
TenHangMoi nvarchar(50) NULL
LoaiHang int NULL
MoTaVNCu nvarchar(500) NULL
MoTaENCu nvarchar(500) NULL
HangDuocLai nvarchar(150) NULL
```

## 15. Xác nhận phạm vi

- Không chạy `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `ALTER`, `CREATE`, `DROP`, `TRUNCATE`.
- Không gọi procedure có ghi dữ liệu.
- Không chạy BAK test ghi.
- Không sửa code.
- Không sửa SQL patch.
- Không sửa reverse V1 → V2.
- Không stage/commit.
- Không xuất họ tên, CCCD, địa chỉ, mã học viên/mã khóa mẫu hoặc dữ liệu cá nhân.
- Chỉ tạo file báo cáo Markdown này theo yêu cầu.

## 16. Phụ lục triển khai realtime forward sau khảo sát

Phần 15 ghi nhận đúng phạm vi của lượt khảo sát read-only ban đầu. Ở lượt triển khai
tiếp theo ngày 25/07/2026, kết luận ownership của báo cáo này được áp dụng vào
luồng realtime V2 → V1 như sau:

- policy cột tập trung phân loại tường minh `V2`, `V1`, `Shared`, `Immutable`
  theo từng domain và theo quyền `INSERT`/`UPDATE`;
- cột source mới chưa được phân loại gây lỗi `UNCLASSIFIED_FORWARD_COLUMN`,
  không tự nhận quyền ghi do schema drift;
- `BaoCaoII`, `KySH`, `DM_LyDoTCBC2` và lookup kết quả sát hạch vẫn bị loại
  khỏi forward catalog;
- `NguoiLX_HoSo` chỉ insert/update whitelist đào tạo; toàn bộ cột mục 7.2
  không xuất hiện trong danh sách cột ghi;
- `TT_XuLy` từ V2 chỉ được merge tự động cho `03`, `04`, `09`; target đã có
  lifecycle BCII/sát hạch giữ `TT_XuLy`, `TrangThai`, `MaKhoaHoc`, `MaBC1`
  và các cột shared theo quy tắc V1-wins;
- `NguoiLX_GPLX` được đặt ở chế độ preserve-only: không update row V1 đã có
  và chưa insert row thiếu cho tới khi có policy chứng minh provenance GPLX cũ;
- thay đổi quan hệ `BaoCaoI.MaKH`/`MaCSDT` và update khóa học bị chặn khi target
  đã có liên kết/lifecycle BCII;
- chẩn đoán ownership chỉ lưu identity SHA-256, reason code và tên cột, không
  lưu giá trị cột hay PII;
- tombstone tiếp tục là audit-only, không xóa business row ở target;
- reverse V1 → V2 không dùng policy forward và không được thay đổi trong lượt này.
