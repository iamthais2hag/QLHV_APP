# Đề xuất mapping CSDL_OTO → QLHV_APP cho xe và giáo viên

## Trạng thái QLHV_APP hiện tại

| Target | Rows | Mức sẵn sàng |
|---|---:|---|
| `App_GiaoVien` | `48` active từ profile `CSDT_OTO` | Có source identity/hash và snapshot pipeline; chưa có feature API/UI; ngày sinh/ảnh đang mất |
| `App_KhoaHoc_GiaoVien` | `8` active | Có source relation identity; không có FK vật lý |
| `App_XeTap` | `0` | Có schema business nhưng thiếu source identity/profile/hash và pipeline |
| `App_KhoaHoc_XeTap` | `0` | Có schema cơ bản nhưng thiếu source relation identity/sync metadata và FK |

Code hiện chỉ có teacher/course full-snapshot mapping trong luồng import. Không tìm thấy repository/controller/client feature dành riêng cho danh sách xe; client chỉ hiển thị thống kê giáo viên trong trang import. Hai danh mục chưa phải một chức năng quản lý production hoàn chỉnh.

## Vehicle mapping

| CSDL_OTO | QLHV hiện có/đề xuất | Ownership | Quy tắc |
|---|---|---|---|
| `BienSoXe` | `BienSoXe`; thêm `SourceBienSoXe` | Source | Trim + uppercase; không loại dấu nếu giá trị canonical cần giữ |
| profile cố định | thêm `SourceProfileCode` | Sync | Unique `(SourceProfileCode, SourceBienSoXe)` |
| row canonical | thêm `SourceHash` | Sync | Hash field có length-prefix, không hash raw file content vào log |
| `MaCSDT`, `MaSoGTVT` | thêm cột tương ứng | Source | Bắt buộc cho partition/ownership |
| `SoDK`, `SoDongCo`, `SoKhung` | cột hiện có | Source | Secondary unique/collision guard; normalize trước compare |
| `SoHuu` | `SoHuu`; derive UI labels | Source | Không cần đồng thời ba bit mâu thuẫn; `XeCuaCoSoDaoTao/XeHopDong` nên computed/read model hoặc constrained |
| nhãn/loại/mác/hãng/màu/năm | cột hiện có | Source | Trim, preserve Unicode |
| `HangGPLXXe` | `HangGPLXXe` | Source | Validate dictionary, không tự đổi hạng |
| `GiayPhepXTL` và chi tiết | cột hiện có | Source | Warning nếu cờ/chi tiết mâu thuẫn |
| `NgayCap/HHGCNKD` | cột hiện có | Source | Source không có số GCN; `SoGCNKiemDinh` là QLHV/manual |
| `BaoHiem`, `HeThongPP` | cột hiện có | Source | Nullable source phải được giữ, không mặc định giả |
| `TuyenDuong`, `ChatLuong`, `GhiChu` | source fields riêng | Source | Không ghi đè `GhiChuNoiBo` |
| `TrangThai` | `TrangThaiNguon bit` + lifecycle QLHV | Source/QLHV | Không gộp source inactive với QLHV soft-delete |
| audit/XML provenance | thêm source audit/provenance | Source | Read-only audit |
| `DuongDanAnh` | `AnhRelativePath` managed | Mixed | Copy qua allow-list; không lưu absolute source path để client dùng trực tiếp |
| — | `GhiChuNoiBo`, `CanhBaoDuLieu`, `GVQuanLy*` | QLHV | Realtime tuyệt đối không overwrite |

`App_XeTap` hiện unique toàn cục trên `BienSoXe`. Khi hỗ trợ nhiều profile, phải có quyết định rõ: hoặc biển số là globally unique đã normalize, hoặc đổi sang source-scoped identity và có collision review. Không được âm thầm merge hai profile cùng biển số.

## Teacher mapping

| CSDL_OTO | QLHV hiện có/đề xuất | Ownership | Quy tắc |
|---|---|---|---|
| `MaGV` | `SourceMaGV`; `MaGV=CSDT_OTO:<source>` hiện hành | Source/sync | Unique `(SourceProfileCode,SourceMaGV)` |
| họ tên | `HoTenDem`, `TenGV`, derived `HoTen` | Source | Không dùng làm identity |
| `NgaySinh` | `date` | Source | Profile contract hiện phải parse `DDMMYYYY`; reject mixed/invalid |
| `SoCMT` | `SoCCCD` | Source/PII | Normalize để collision check; mask API/log |
| địa chỉ/địa giới | `DiaChi`, mã DVHC/DVQL | Source/PII | Quyền truy cập riêng |
| giới tính/điện thoại | cột hiện có | Source/PII | Không trả trong list mặc định |
| tuyển dụng/trình độ | cột hiện có | Source | Source values là mô tả, không ép enum khi chưa có dictionary |
| GPLX/ngày cấp/hết hạn | cột hiện có | Source | Eligibility warning theo hạn |
| GCN/hạng được dạy/các môn | cột hiện có | Source | 2 row thiếu phải manual review |
| flags môn học | cột hiện có | Source | Preserve nullable/unknown |
| `AnhCD` | `AnhRelativePath` managed | Mixed | Copy + MIME/size/hash validation; không expose absolute path |
| `TrangThai` | `TrangThaiNguon` | Source | QLHV lifecycle tách `IsDeleted/review` |
| audit/XML provenance | cột hiện có một phần | Source | Không dùng làm target CreatedAt |
| — | `HopDongThoiHan`, `NgayTrungTuyen` | QLHV | Không có source; không overwrite |
| — | thêm `UserId` nullable | QLHV | Unique filtered, explicit link/unlink permission |

## Relation mapping

| Source | Target | Identity | Guard |
|---|---|---|---|
| `KhoaHoc_GiaoVien` | `App_KhoaHoc_GiaoVien` | `(profile, MaLichLV)` đã có | Course và teacher target phải active; vehicle nếu có phải resolve |
| `KhoaHoc_XeTap` | `App_KhoaHoc_XeTap` | cần thêm `(profile, MaLichSD)` | Course/vehicle bắt buộc; teacher/learner nullable nhưng phải resolve nếu có |

Target hiện không có FK giữa bốn bảng này. Đề xuất thêm surrogate FK IDs đến `App_KhoaHoc`, `App_GiaoVien`, `App_XeTap`, giữ source codes cho audit. Tên denormalized chỉ là display snapshot.

## Trường không được realtime ghi đè

- ghi chú nội bộ, cảnh báo/review và quyết định override;
- link user/role/permission;
- phân công QLHV-owned không có source relation identity;
- đường dẫn file managed, metadata upload và access control;
- `CreatedAt/CreatedBy` của QLHV;
- soft-delete/manual hold do operator;
- dữ liệu học viên và checkpoint learner.

## Duplicate và delete policy

Vehicle: source identity match thì update source-owned; source identity mới nhưng secondary key collision thì manual review. Teacher: source identity match thì update; mã mới nhưng CCCD trùng thì manual review; họ tên+ngày sinh chỉ cảnh báo.

Mất membership nguồn không được hard-delete. Trạng thái đề xuất: `ACTIVE`, `SOURCE_INACTIVE`, `MISSING_AT_SOURCE`, `MANUAL_HOLD`, `SOFT_DELETED_BY_QLHV`. Chỉ operator hoặc workflow được phê duyệt mới chuyển xóa mềm; relation missing cũng soft-inactive trước.

## Kế hoạch triển khai tiếp theo

1. Sửa contract ngày sinh và pipeline ảnh giáo viên; preview/backfill 48 row, không chạy trong task nghiên cứu.
2. Thêm source identity, hash, source audit và lifecycle cho `App_XeTap`/`App_KhoaHoc_XeTap`; thêm FK/unique/check constraints.
3. Viết read repository, mapper và data-quality guards; không dùng stored procedure side-effect nguồn.
4. Thêm API/UI read-only có pagination, role/PII projection và trạng thái review.
5. Chạy controlled batch riêng từng domain với sealed plan, canary một candidate thật và rollback exact target.
6. Sau vận hành ổn định mới cân nhắc CT trong task riêng; checkpoint không dùng chung learner.
