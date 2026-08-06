# Data dictionary xe và giáo viên CSDL_OTO

## Quy ước

- `NN`: `NOT NULL`; `N`: nullable; `I`: identity.
- Mọi object dưới đây thuộc schema `dbo`, loại `USER_TABLE`, trừ nhóm module.
- Mô tả “đã chứng minh” đến từ catalog, extended property, FK/index hoặc SQL module. Mọi đề xuất identity/ownership được ghi riêng là “đề xuất”.

## Object và key

| Object | Vai trò | PK | Unique/index khác | FK chính | Business/source key |
|---|---|---|---|---|---|
| `XeTap` | Master xe | `BienSoXe` | Chỉ PK | `MaCSDT`, `MaSoGTVT` → `DM_DonViGTVT.MaDV` | Biển số; secondary review: số khung, số máy, đăng ký |
| `GiaoVien` | Master giáo viên | `MaGV` | `UK_GiaoVien(MaGV,MaCSDT,MaSoGTVT)` dư thừa | Đơn vị và composite địa giới | `MaGV`; giấy tờ chuẩn hóa là collision key |
| `KhoaHoc_XeTap` | Phân xe/lịch xe | `MaLichSD` I | Không | `MaKH` → `KhoaHoc`; `BienSoXe` → `XeTap` | `(profile, MaLichSD)` đề xuất |
| `KhoaHoc_GiaoVien` | Phân giáo viên/lịch dạy | `MaLichLV` I | Không | `MaKH` → `KhoaHoc`; `MaGV` → `GiaoVien` | `(profile, MaLichLV)` đề xuất |
| `KhoaHoc` | Master khóa | `MaKH` | PK | Đơn vị/dictionary liên quan | `MaKH` |
| `LichHoc` | Lịch tổng quát | `MaLichHoc` I | PK | `MaKH` → `KhoaHoc` | `MaLichHoc` |

Không có FK cho `KhoaHoc_XeTap.MaGV`, `KhoaHoc_XeTap.MaHV` hoặc `KhoaHoc_GiaoVien.BienSoXe`.

## `dbo.XeTap`

| # | Cột | Kiểu | Null/default | Key/index/ref | Nghĩa đã chứng minh/suy ra |
|---:|---|---|---|---|---|
| 1 | `BienSoXe` | `varchar(10)` | NN | PK | Biển số; identity upsert nguồn |
| 2 | `MaSoGTVT` | `varchar(6)` | NN | FK | Cơ quan Sở GTVT |
| 3 | `MaCSDT` | `varchar(6)` | NN | FK | Cơ sở đào tạo |
| 4 | `SoDK` | `nvarchar(100)` | N | — | Số đăng ký xe |
| 5 | `SoHuu` | `bit` | NN/`0` | — | `0` chính chủ, `1` hợp đồng |
| 6 | `NhanHieu` | `nvarchar(100)` | N | — | Nhãn hiệu |
| 7 | `LoaiXe` | `nvarchar(100)` | N | — | Loại xe |
| 8 | `MacXe` | `nvarchar(100)` | N | — | Mác/model xe |
| 9 | `HangXe` | `nvarchar(100)` | N | — | Hãng xe |
| 10 | `MauXe` | `nvarchar(100)` | N | — | Màu xe |
| 11 | `SoDongCo` | `varchar(20)` | N | — | Số động cơ/số máy |
| 12 | `SoKhung` | `varchar(20)` | N | — | Số khung |
| 13 | `GiayPhepXTL` | `bit` | N/`1` | — | Có giấy phép xe tập lái |
| 14 | `SoGPXTL` | `nvarchar(60)` | N | — | Số giấy phép tập lái |
| 15 | `CoQuanCapGPXTL` | `nvarchar(100)` | N | — | Cơ quan cấp |
| 16 | `NgayCapGPXTL` | `datetime` | N | — | Ngày cấp giấy phép |
| 17 | `NgayHHGPXTL` | `datetime` | N | — | Ngày hết hạn giấy phép |
| 18 | `NamSX` | `int` | N | — | Năm sản xuất |
| 19 | `HeThongPP` | `bit` | N/`1` | — | Hệ thống phanh phụ |
| 20 | `NgayCapGCNKD` | `datetime` | N | — | Ngày cấp chứng nhận kiểm định |
| 21 | `NgayHHGCNKD` | `datetime` | N | — | Ngày hết hạn kiểm định |
| 22 | `BaoHiem` | `bit` | N/`1` | — | Bảo hiểm còn hiệu lực |
| 23 | `TuyenDuong` | `nvarchar(100)` | N | — | Tuyến đường tập lái |
| 24 | `ChatLuong` | `nvarchar(100)` | N | — | Chất lượng/tình trạng mô tả |
| 25 | `GhiChu` | `nvarchar(510)` | N | — | Ghi chú nguồn |
| 26 | `TrangThai` | `bit` | NN/`1` | — | `1` hiệu lực, `0` không hiệu lực |
| 27 | `NguoiTao` | `nvarchar(60)` | N | — | Người tạo nguồn |
| 28 | `NguoiSua` | `nvarchar(60)` | N | — | Người sửa nguồn |
| 29 | `NgayTao` | `datetime` | NN/`GETDATE()` | — | Audit tạo; procedure có thể ghi lại |
| 30 | `NgaySua` | `datetime` | NN/`GETDATE()` | — | Audit sửa |
| 31 | `DuongDanAnh` | `nvarchar(300)` | N | — | Đường dẫn ảnh; 29/29 hiện là absolute |
| 32 | `HangGPLXXe` | `varchar(10)` | N | — | Hạng GPLX tương ứng xe |
| 33 | `MaFileTiepNhanXML` | `nvarchar(100)` | N | — | Provenance file XML |
| 34 | `ThoiGianTiepNhanXML` | `datetime` | N | — | Thời gian nhận XML |

Không có column computed hoặc identity trong `XeTap`. Không có unique secondary key cho số đăng ký, số khung, số máy hoặc giấy phép.

## `dbo.GiaoVien`

| # | Cột | Kiểu | Null/default | Key/index/ref | Nghĩa đã chứng minh/suy ra |
|---:|---|---|---|---|---|
| 1 | `MaGV` | `varchar(8)` | NN | PK, UK | Mã giáo viên nguồn |
| 2 | `MaSoGTVT` | `varchar(6)` | NN | FK, UK | Sở GTVT |
| 3 | `MaCSDT` | `varchar(6)` | NN | FK, UK | Cơ sở đào tạo |
| 4 | `HoTenDem` | `nvarchar(100)` | NN | — | Họ và tên đệm; PII |
| 5 | `TenGV` | `nvarchar(100)` | NN | — | Tên; PII |
| 6 | `NgaySinh` | `varchar(8)` | NN | — | Description nói `YYYYMMDD`; data thực tế `DDMMYYYY` |
| 7 | `AnhCD` | `nvarchar(300)` | N | — | Đường dẫn ảnh chân dung; PII |
| 8 | `SoCMT` | `varchar(20)` | NN | Không unique | CMND/CCCD; sensitive collision key |
| 9 | `NoiCT` | `nvarchar(100)` | N | — | Nơi cư trú/công tác; PII |
| 10 | `NoiCT_MaDVHC` | `varchar(5)` | N | Composite FK | Mã đơn vị hành chính |
| 11 | `NoiCT_MaDVQL` | `varchar(5)` | N | Composite FK | Mã đơn vị quản lý |
| 12 | `GioiTinh` | `char(1)` | N | — | `U/M/F` |
| 13 | `DienThoai` | `varchar(50)` | N | — | Điện thoại; PII |
| 14 | `HinhThuc_TuyenDung` | `nvarchar(100)` | N | — | Mô tả tuyển dụng; description nêu `CT/HD` |
| 15 | `TrinhDo_VanHoa` | `nvarchar(100)` | N | — | Trình độ văn hóa |
| 16 | `TrinhDo_ChuyenMon` | `nvarchar(100)` | N | — | Trình độ chuyên môn |
| 17 | `TrinhDo_SuPham` | `nvarchar(100)` | N | — | Trình độ sư phạm |
| 18 | `HangGPLX` | `nvarchar(100)` | N | — | Các hạng GPLX, mô tả dùng dấu `|` |
| 19 | `NgayCapGPLX` | `datetime` | NN | — | Ngày cấp GPLX |
| 20 | `ThamNien_LaiXe` | `int` | N | — | Số năm thâm niên |
| 21 | `SoQD_GCN` | `nvarchar(60)` | N | — | Số quyết định/chứng nhận |
| 22 | `NgayQD_GCN` | `datetime` | N | — | Ngày quyết định/chứng nhận |
| 23 | `LoaiHinh_DaoTao` | `nvarchar(1000)` | N | — | Nội dung/loại hình dạy; data là mô tả dài, không chỉ `LT/TH/LH` |
| 24 | `GhiChu` | `nvarchar(1000)` | N | — | Ghi chú nguồn |
| 25 | `TrangThai` | `bit` | NN/`1` | — | Hiệu lực nguồn |
| 26 | `NguoiTao` | `nvarchar(60)` | N | — | Người tạo nguồn |
| 27 | `NguoiSua` | `nvarchar(60)` | N | — | Người sửa nguồn |
| 28 | `NgayTao` | `datetime` | NN/`GETDATE()` | — | Audit tạo; mutable qua procedure |
| 29 | `NgaySua` | `datetime` | NN/`GETDATE()` | — | Audit sửa |
| 30 | `CacHangGPLXDuocDT` | `nvarchar(100)` | N | — | Hạng GPLX được đào tạo |
| 31 | `CauTaoSuaChua` | `char(1)` | N | — | Cờ môn cấu tạo/sửa chữa |
| 32 | `DaoDucLaixe` | `char(1)` | N | — | Cờ môn đạo đức lái xe |
| 33 | `NghiepVuVanTai` | `char(1)` | N | — | Cờ môn nghiệp vụ vận tải |
| 34 | `LuatGTDB` | `char(1)` | N | — | Cờ môn luật GTĐB |
| 35 | `KyThuatLaixe` | `char(1)` | N | — | Cờ môn kỹ thuật lái xe |
| 36 | `MaFileTiepNhanXML` | `nvarchar(100)` | N | — | Provenance file XML |
| 37 | `ThoiGianTiepNhanXML` | `datetime` | N | — | Thời gian nhận XML |
| 38 | `NgayHHGPLX` | `datetime` | N | — | Ngày hết hạn GPLX |
| 39 | `NoiCapGCN` | `nvarchar(1000)` | N | — | Nơi cấp GCN |
| 40 | `CacMonHoc` | `nvarchar(1000)` | N | — | Danh sách môn |
| 41 | `LoaiGiaoVien` | `nvarchar(100)` | N | — | Loại giáo viên |
| 42 | `CacHangDaCo` | `nvarchar(1000)` | N | — | Các hạng đã có |

Không có thời hạn hợp đồng, ngày bắt đầu/kết thúc làm việc, chữ ký, user/account ID hoặc soft-delete fields.

## `dbo.KhoaHoc_XeTap`

| # | Cột | Kiểu | Null/default | Key/ref | Nghĩa |
|---:|---|---|---|---|---|
| 1 | `MaLichSD` | `int` | NN/I | PK | Source relation ID |
| 2 | `MaKH` | `varchar(13)` | NN | FK `KhoaHoc` | Khóa học |
| 3 | `BienSoXe` | `varchar(10)` | NN | FK `XeTap` | Xe |
| 4 | `MaGV` | `varchar(8)` | N | Không FK | Giáo viên logic |
| 5 | `MaHV` | `nvarchar(50)` | N | Không FK | Học viên logic |
| 6 | `DiaDiem` | `nvarchar(510)` | N | — | Địa điểm |
| 7 | `GhiChu` | `nvarchar(510)` | NN | — | Ghi chú |
| 8 | `TrangThai` | `bit` | NN/`1` | — | Hiệu lực |
| 9–12 | `NguoiTao`, `NguoiSua`, `NgayTao`, `NgaySua` | `nvarchar(60)`, `datetime` | audit | — | Audit nguồn |
| 13–14 | `NgayBD`, `NgayKT` | `datetime` | N | — | Khoảng lịch |
| 15 | `IsKhoaHocXeTap` | `bit` | NN/`0` | — | `1` cấp khóa; `0` slot lịch theo procedure |
| 16–17 | `TenHV`, `TenGV` | `nvarchar(100/200)` | N | — | Tên denormalized; không dùng làm key |

## `dbo.KhoaHoc_GiaoVien`

| # | Cột | Kiểu | Null/default | Key/ref | Nghĩa |
|---:|---|---|---|---|---|
| 1 | `MaKH` | `varchar(13)` | NN | FK `KhoaHoc` | Khóa học |
| 2 | `MaGV` | `varchar(8)` | NN | FK `GiaoVien` | Giáo viên |
| 3 | `TenGV` | `nvarchar(100)` | NN | — | Tên denormalized |
| 4 | `BienSoXe` | `varchar(10)` | N | Không FK | Xe logic |
| 5 | `LoaiGV` | `char(2)` | NN | — | Vai trò `LT/TH` |
| 6 | `SoHV` | `int` | N/`0` | — | Số học viên phân |
| 7–8 | `NgayHL`, `NgayHetHL` | `datetime` | N | — | Hiệu lực phân công |
| 9 | `GhiChu` | `nvarchar(510)` | NN | — | Ghi chú |
| 10 | `TrangThai` | `bit` | NN/`1` | — | Hiệu lực |
| 11–14 | `NguoiTao`, `NguoiSua`, `NgayTao`, `NgaySua` | `nvarchar(60)`, `datetime` | audit | — | Audit nguồn |
| 15 | `MaLichLV` | `int` | NN/I | PK | Source relation ID |
| 16–17 | `NgayBD`, `NgayKT` | `datetime` | N | — | Khoảng lịch |
| 18 | `IsKhoaHocGiaoVien` | `bit` | NN/`1` | — | `1` cấp khóa; khác `1` slot lịch |
| 19 | `MaMonHoc` | `int` | N | — | Mã môn |
| 20 | `TenMonHoc` | `nvarchar(510)` | N | — | Tên môn denormalized |

## Supporting objects

| Object | Cột liên quan | Vai trò |
|---|---|---|
| `KhoaHoc` | `MaKH`, `MaCSDT`, `NgayKG`, `NgayBG`, `TrangThai`, audit | Phạm vi khóa và validation thời gian |
| `LichHoc` | `MaLichHoc`, `MaKH`, `TuNgay`, `DenNgay` | Lịch tổng quát; hiện không có cột xe/GV |
| `DM_DonViGTVT` | `MaDV` | Đơn vị/Sở/CSDT tham chiếu |
| `DM_DVHC` | mã đơn vị quản lý/hành chính | Địa giới giáo viên |
| `DM_HangGPLX`, `DM_HangDT` | mã/tên hạng | Dictionary hạng GPLX/đào tạo |
| `QTHT_NguoiDung` | user master | Không có liên kết với `GiaoVien` |

## Module nghiệp vụ đáng chú ý

Vehicle: `usp_XeTap_*`, `usp_CheckWarningXeTap`, `usp_CheckWarningLichLSDXeTap`, `usp_VoHieuLucXTLConLai`, `usp_KhoaHoc_XeTap_*`, `usp_LichSD_KhoaHoc_XeTap_*`, `usp_VoHieuLucKhoaHocXeTap`.

Teacher: `CreateNewMaGiaoVien`, `usp_GiaoVien_*`, `usp_CheckWarningGiaoVien`, `usp_CheckWarningLichLVGiaoVien`, `usp_VoHieuLucGVConLai`, `usp_KhoaHoc_GiaoVien_*`, `usp_LichLV_KhoaHoc_GiaoVien_*`, `usp_VoHieuLucKhoaHocGiaoVien`.

Các module write chỉ được đọc definition; không procedure side-effect nào được gọi trong nghiên cứu.
