# Nghiên cứu danh sách xe tập lái mới trong CSDL_OTO

## Phạm vi và kết luận

Nghiên cứu này chỉ đọc production `CSDL_OTO` ngày 2026-07-28. Nguồn chính xác của một xe là `dbo.XeTap`; hồ sơ pháp lý, kiểm định, ảnh và trạng thái đều được nhúng trong cùng bản ghi. `dbo.KhoaHoc_XeTap` là bảng quan hệ xe–khóa học/lịch sử dụng; `dbo.KhoaHoc_GiaoVien.BienSoXe` là một tham chiếu logic thứ hai nhưng không có foreign key.

Không có bảng độc lập cho hồ sơ pháp lý hoặc ảnh xe. Không có trạng thái riêng cho “thanh lý”, “chờ duyệt” hay “đã xóa mềm” trong nguồn. Chỉ có `XeTap.TrangThai` (`1` có hiệu lực, `0` không hiệu lực).

## Danh tính database đã xác minh

| Thuộc tính | Giá trị quan sát |
|---|---:|
| SQL Server | `CSDLTTTC` |
| Database / database_id | `CSDL_OTO` / `9` |
| Compatibility | `110` |
| Collation | `SQL_Latin1_General_CP1_CI_AS` |
| Schema / table / view | `4` / `51` / `0` |
| Snapshot isolation / RCSI | `ON` / `OFF` |
| Change Tracking database version | `2` |
| Change-tracked tables | `5`; không có `XeTap` hoặc hai bảng quan hệ |

## Mô hình đã chứng minh

| Vai trò | Object | Bằng chứng |
|---|---|---|
| Master một xe | `dbo.XeTap` | PK `BienSoXe`; 29 bản ghi |
| Pháp lý/đăng ký | Các cột trong `XeTap` | `SoDK`, `SoKhung`, `SoDongCo`, `SoGPXTL`, ngày cấp/hết hạn, kiểm định, bảo hiểm |
| Ảnh | `XeTap.DuongDanAnh` | Một đường dẫn trên master; không có bảng file/ảnh riêng |
| Cơ sở quản lý | `XeTap.MaCSDT`, `MaSoGTVT` | Hai FK đến `DM_DonViGTVT.MaDV` |
| Phân xe theo khóa/lịch | `dbo.KhoaHoc_XeTap` | FK đến `KhoaHoc` và `XeTap`; `MaLichSD` identity PK |
| Xe đi cùng phân công giáo viên | `dbo.KhoaHoc_GiaoVien.BienSoXe` | Cột nullable, không FK đến `XeTap` |
| Lịch học tổng quát | `dbo.LichHoc` | Có quan hệ khóa học nhưng hiện 0 dòng, không trực tiếp chứa xe |

`KhoaHoc_XeTap.MaGV` không có FK đến `GiaoVien`; `KhoaHoc_GiaoVien.BienSoXe` không có FK đến `XeTap`. Hai cột này phải được coi là liên kết logic cần guard orphan khi triển khai.

## Khóa và định nghĩa “xe mới”

- Source identity được chứng minh: `BienSoXe` vì là PK, `NOT NULL`, collation không phân biệt hoa/thường và là khóa upsert trong `usp_XeTap_TiepNhan`.
- Source identity tương lai trong QLHV phải là `(SourceProfileCode, normalized BienSoXe)`, không chỉ biển số trần, để ngăn va chạm giữa profile.
- Các khóa đối chiếu duplicate: biển số chuẩn hóa, `SoKhung`, `SoDongCo`, `SoDK`; `SoGPXTL` là thêm một khóa cảnh báo pháp lý. Nguồn chỉ cưỡng chế duy nhất biển số.
- Một xe là “mới đối với QLHV” khi source identity chưa có trong membership/target của đúng profile và không có secondary-key collision với xe khác.
- Nếu biển số chưa thấy nhưng số khung, số máy hoặc đăng ký trùng xe hiện hữu, kết quả là `MANUAL_REVIEW`, không phải insert mới.
- `NgayTao` không đủ để kết luận mới: cả 29 xe hiện có được tạo trong một cửa sổ dưới một giây ngày 2026-07-27; thủ tục update cũng cho phép ghi lại trường tạo/người tạo.

## Quy tắc trong stored procedure

Các kết luận sau được chứng minh trực tiếp từ `sys.sql_modules`:

- `usp_XeTap_Paging` chỉ trả `TrangThai=1`, do đó giao diện cũ có thể che hoàn toàn xe inactive.
- `usp_XeTap_Insert` dựa vào PK để chặn trùng; không có guard cho số khung, số máy hoặc số đăng ký.
- `usp_XeTap_Update` có thể cập nhật lại cả audit tạo, nên `NgayTao` không phải immutable event time.
- `usp_XeTap_Delete` xóa vật lý; `usp_XeTap_Update_TrangThai` là đường vô hiệu hóa mềm theo trạng thái.
- `usp_XeTap_TiepNhan` upsert theo `BienSoXe`, buộc `TrangThai=1`, và ghi `MaFileTiepNhanXML`/`ThoiGianTiepNhanXML`.
- `usp_VoHieuLucXTLConLai` đặt `TrangThai=0` cho bản ghi không thuộc file XML hiện tại. Đây là semantics snapshot/batch, không được tái sử dụng như delete realtime.
- `usp_CheckWarningXeTap` cảnh báo xe đang gắn với khóa chưa kết thúc.
- `usp_CheckWarningLichLSDXeTap` kiểm tra khoảng thời gian và chống trùng lịch cùng biển số.
- `usp_VoHieuLucKhoaHocXeTap` và các procedure delete quan hệ dùng xóa vật lý.

## Trạng thái dữ liệu hiện tại

| Chỉ số | Kết quả |
|---|---:|
| Tổng / active / inactive | `29 / 29 / 0` |
| Thiếu biển số / số khung / số máy / đăng ký / hạng xe | `0 / 0 / 0 / 0 / 0` |
| Nhóm trùng biển số chuẩn hóa / đăng ký / số khung / số máy | `0 / 0 / 0 / 0` |
| Hết hạn giấy phép xe tập lái / kiểm định | `0 / 0` |
| Bảo hiểm không hiệu lực | `4` |
| Thiếu đường dẫn ảnh | `0` |
| Đường dẫn ảnh tuyệt đối/rooted | `29` |
| Quan hệ `KhoaHoc_XeTap` | `0` |
| Xe chưa có quan hệ active | `29` |
| Tham chiếu biển số trong `KhoaHoc_GiaoVien` | `0` |
| Orphan đã quan sát | `0` |

Không thể báo số “thanh lý/chờ duyệt/xóa mềm” vì schema không biểu diễn các trạng thái đó; giá trị đúng là `N/A`, không phải suy diễn bằng `0`.

## Phân định ownership

Được chứng minh từ schema/procedure: toàn bộ cột hiện hành do `CSDL_OTO` sở hữu; các trường XML là dấu vết nhận dữ liệu; `NguoiTao/NguoiSua/NgayTao/NgaySua` là audit nguồn.

Đề xuất cho QLHV (chưa triển khai): giữ source-owned cho biển số, định danh xe, pháp lý, hạng/loại, trạng thái nguồn và audit nguồn. QLHV-owned gồm ghi chú nội bộ, nhãn/cảnh báo nội bộ, bản sao file được quản lý, trạng thái review và quan hệ tài khoản/người phụ trách do QLHV tạo. Realtime không được ghi đè các trường QLHV-owned.

## Kết luận thiết kế

`XeTap` đủ rõ để thiết kế import. Cơ chế an toàn ban đầu nên là batch có preview/guard và soft-inactivate/manual review; chưa thể dùng CT vì `XeTap`, `KhoaHoc_XeTap` và `KhoaHoc_GiaoVien` chưa được Change Tracking. Không dùng mất membership nguồn để hard-delete QLHV.
