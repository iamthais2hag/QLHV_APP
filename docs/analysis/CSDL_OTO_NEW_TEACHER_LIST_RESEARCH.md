# Nghiên cứu danh sách giáo viên mới trong CSDL_OTO

## Phạm vi và kết luận

Nguồn chính xác của một giáo viên là `dbo.GiaoVien`. Chứng chỉ, GPLX, hạng được đào tạo, môn dạy và ảnh đều là cột nhúng trong master; không có bảng con chứng chỉ/bằng cấp. Quan hệ khóa học/lịch dạy nằm ở `dbo.KhoaHoc_GiaoVien`; liên kết giáo viên–xe/lịch thực hành có thể xuất hiện ở `KhoaHoc_GiaoVien.BienSoXe` và `KhoaHoc_XeTap.MaGV`.

Không tìm thấy FK hoặc cột nối `GiaoVien` với `QTHT_NguoiDung`; tài khoản hệ thống hiện không phải một phần của mô hình giáo viên nguồn.

## Master, khóa và trạng thái

| Nội dung | Kết quả đã chứng minh |
|---|---|
| Master | `dbo.GiaoVien` — 48 bản ghi |
| Internal/source key | `MaGV varchar(8)` PK |
| Quy tắc sinh mã | `CreateNewMaGiaoVien`: `MaCSDT` + số thứ tự 3 chữ số theo `MAX+1` |
| Unique phụ | `(MaGV, MaCSDT, MaSoGTVT)`; dư thừa vì `MaGV` đã là PK |
| Business duplicate key | CCCD/CMND chuẩn hóa để chặn/review; họ tên + ngày sinh chỉ là tín hiệu review |
| Trạng thái | `TrangThai bit`: `1` hiệu lực, `0` không hiệu lực |
| Soft delete riêng | Không có |
| Chờ duyệt/bị khóa/nghỉ riêng | Không có; không được suy diễn từ `TrangThai=0` |

`MAX+1` trong hàm sinh mã có nguy cơ race nếu hai writer tạo đồng thời. `MaGV varchar(8)` cũng cần guard vì mô tả nói `MaCSDT` + 3 chữ số trong khi `MaCSDT` có độ dài tối đa 6.

## Định nghĩa “giáo viên mới”

- Source identity: `(SourceProfileCode, MaGV)`; `MaGV` đơn lẻ không đủ khi hợp nhất nhiều profile.
- “Mới đối với QLHV” nghĩa là source identity chưa có trong membership/target của profile và không xung đột CCCD/CMND chuẩn hóa với người hiện hữu.
- Không dùng họ tên làm unique key.
- Khi `MaGV` mới nhưng giấy tờ định danh trùng, chuyển `MANUAL_REVIEW`; không tự tạo người thứ hai.
- `NgayTao` không đủ chứng minh mới: cả 48 bản ghi hiện tại được tạo trong một cửa sổ dưới một giây ngày 2026-07-27 và các update procedure có thể ghi lại audit tạo.
- Với batch XML, `MaFileTiepNhanXML` và `ThoiGianTiepNhanXML` chỉ là provenance của lần nhận; chúng không thay source identity.

## Quy tắc trong stored procedure

Được chứng minh trực tiếp từ SQL modules:

- `usp_GiaoVien_Insert` bỏ qua mã truyền vào và gọi `CreateNewMaGiaoVien`; không guard trùng giấy tờ định danh.
- `usp_GiaoVien_TiepNhanXML` kiểm tra tồn tại bằng `MaGV`; nếu không có thì sinh mã mới thay vì giữ mã ngoài. Vì vậy một người có mã ngoài thay đổi có thể bị nhân đôi.
- `usp_VoHieuLucGVConLai` đặt inactive các giáo viên không thuộc file XML hiện tại. Đây là snapshot absence semantics, không phải delete realtime.
- `usp_GiaoVien_Delete` xóa vật lý; `usp_GiaoVien_Delete_Item` đặt `TrangThai=0`.
- `usp_GiaoVien_SelectItems_Paging` hỗ trợ active/inactive, nhưng logic hiển thị ngày sinh đối xử chuỗi như `DDMMYYYY`, trái với extended description `YYYYMMDD`.
- `usp_CheckWarningGiaoVien` cảnh báo giáo viên đang thuộc khóa hiện tại/tương lai.
- `usp_CheckWarningLichLVGiaoVien` kiểm tra ngày trong phạm vi khóa và chống trùng lịch cùng giáo viên.
- `usp_VoHieuLucKhoaHocGiaoVien` xóa vật lý các phân công khóa và lịch tương lai.

## Phát hiện định dạng ngày và ảnh

Đây là blocker dữ liệu cần sửa trước khi phát hành chức năng:

- Cả 48 `NgaySinh` đều dài 8, không dòng nào parse được theo `yyyyMMdd`/style 112.
- Cả 48 parse hợp lệ khi diễn giải `DDMMYYYY`; khoảng ngày sinh tổng hợp quan sát là 1965-03-24 đến 2002-01-01.
- Mapper QLHV hiện tại chỉ dùng `DateTime.TryParseExact(..., "yyyyMMdd")`, nên 48 `App_GiaoVien.NgaySinh` hiện đều `NULL`.
- Cả 48 `AnhCD` nguồn là đường dẫn absolute/rooted. Mapper hiện chỉ chấp nhận relative path an toàn, nên 48 `App_GiaoVien.AnhRelativePath` hiện đều `NULL`.

Không nên đổi parser sang `DDMMYYYY` mà không có contract/version guard: cần validator chấp nhận đúng một định dạng được profile khai báo và fail khi dữ liệu trộn lẫn hoặc mơ hồ.

## Chất lượng và quan hệ hiện tại

| Chỉ số | Kết quả |
|---|---:|
| Tổng / active / inactive | `48 / 48 / 0` |
| Thiếu mã / hạng GPLX / cơ sở / ảnh / ngày hết hạn GPLX | `0 / 0 / 0 / 0 / 0` |
| Thiếu số quyết định GCN / ngày quyết định / hạng được đào tạo | `2 / 2 / 2` |
| GPLX đã hết hạn | `0` |
| Nhóm trùng giấy tờ / họ tên+ngày sinh | `0 / 0` |
| Quan hệ `KhoaHoc_GiaoVien` | `8` |
| Giáo viên distinct đang được phân khóa | `8` |
| Phân công thuộc khóa hiện tại/tương lai theo logic nguồn | `8` |
| Giáo viên có xe được phân công | `0` |
| Không có quan hệ nghiệp vụ active | `40` |
| Orphan quan sát | `0` |

## Ownership đề xuất

Source-owned: mã nguồn, danh tính tối thiểu, GPLX/chứng nhận/hạng dạy, trạng thái nguồn, cơ sở, môn và provenance/audit nguồn. QLHV-owned: liên kết tài khoản, vai trò/quyền trong QLHV, ghi chú nội bộ, review/override có provenance, file ảnh đã sao chép vào kho được quản lý, lịch hoặc phân công tạo riêng trong QLHV.

CCCD, địa chỉ và điện thoại là dữ liệu nhạy cảm; API danh sách không được trả mặc định, log/hash không được chứa raw value, và quyền xem/chỉnh sửa phải tách khỏi quyền xem danh sách.

## Kết luận thiết kế

QLHV đã có target và pipeline snapshot cho giáo viên, nhưng chưa có chức năng quản lý riêng và đang mất ngày sinh/ảnh do sai contract. Khuyến nghị sửa, kiểm thử và chạy lại batch có kiểm soát trước; chỉ đánh giá CT sau khi source bật tracking cho cả master và quan hệ, có checkpoint riêng và tombstone policy không hard-delete.
