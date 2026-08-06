# Triển khai Excel phân công học viên

Ngày lập: 2026-07-30

Production migration: **NOT APPLIED**

## Export

Export luôn chạy từ exact course route và repository join learner với
`KhoaHocId + MaKhoa + SourceProfileCode`. Sheet `PhanCongHocVien` có đúng 18 cột đã duyệt:

1. STT
2. Mã đăng ký
3. Họ và tên
4. Ngày sinh
5. Giới tính
6. Số CCCD
7. Địa chỉ thường trú
8. Hạng học
9. Mã hạng học
10. Số GPLX đã có
11. Hạng GPLX đã có
12. Người nhận hồ sơ
13. Tên khóa
14. Mã khóa
15. Giáo viên đứng lớp
16. Xe tập lái
17. Xe bài số 10
18. Mã giáo viên hồ sơ

Mã đăng ký, CCCD, mã hạng, số GPLX, mã khóa, biển số và mã người nhận hồ sơ được ghi
dạng text. Ngày sinh là Excel date với format `dd/MM/yyyy`. Tất cả text bắt đầu bằng
`=`, `+`, `-`, `@` sau khoảng trắng đầu được prefix apostrophe để không trở thành formula.

## Technical/lookup sheet

Sheet `_QLHV_TECH` là `VeryHidden`, chứa:

- `TemplateVersion=HOCVIEN_ASSIGNMENT_V2`;
- `NormalizationVersion=V2`;
- exact `KhoaHocId`;
- exact `SourceProfileCode` (`CSDT_OTO` hoặc `CSDT_MOTO`);
- `HocVienId`, `MaDangKy`, `AssignmentRowVersion` từng dòng;
- lookup người nhận hồ sơ, giáo viên nguồn, xe nguồn và nhóm.

## Import template và semantics

Visible sheet `NhapPhanCong` có 14 technical input columns:

`MaDangKy`, `MaKhoa`, `MaNhom`, `MaGiaoVienHoSo`, `MaGiaoVienDungLop`,
`BienSoXeTap`, `BienSoXeBaiSo10`, `HocVienId`, `AssignmentRowVersion`,
`ActionNhom`, `ActionGiaoVienHoSo`, `ActionGiaoVienDungLop`, `ActionXeTap`,
`ActionXeBaiSo10`.

Template prefill exact learner identity/current codes và dùng `KEEP` làm default. Blank action
khi parse cũng được chuẩn hóa thành `KEEP`. `CLEAR` và `INHERIT` chỉ có hiệu lực qua action
rõ ràng; `INHERIT` chỉ hợp lệ khi có group phù hợp.

## Security và giới hạn

Parser áp dụng các guard trước mapping:

- chỉ `.xlsx`, tối đa 10 MiB;
- đúng một visible sheet;
- tối đa 100 cột và 5.000 data rows;
- header bắt buộc, không header trùng;
- từ chối macro, embedded/OLE object, external link/relationship, path traversal;
- từ chối `.rels` lớn hơn 2 MiB, package trên 500 entries, zip ratio trên 200 và
  tổng uncompressed vượt 50 MiB;
- file package phải có 1 byte đến đúng 10 MiB; đúng giới hạn được chấp nhận nếu mọi guard
  nội dung khác đều pass, vượt một byte bị từ chối;
- giới hạn tổng uncompressed size 50 MiB và compression ratio bất thường;
- từ chối formula trên key/action cells;
- normalize NFC/whitespace và length trước lookup;
- không log raw PII.

Nếu `_QLHV_TECH` tồn tại, metadata `A1:B4` phải đúng tuyệt đối: template version,
normalization version, `KhoaHocId` dương và exact `CSDT_OTO`/`CSDT_MOTO`. Workbook/XLSX
hỏng ở tầng ZIP/OpenXML được map thống nhất thành domain status `INVALID`, không rò lỗi thư viện.

## Scoped preview/confirm

Server cross-check:

- technical `KhoaHocId` với route;
- technical `SourceProfileCode` với course/profile route;
- `MaKhoa` từng dòng với course;
- `MaDangKy`, optional `HocVienId`, learner/course/profile identity;
- optional assignment RowVersion;
- active state và exact profile của teacher/vehicle references.

Preview preload một catalog course-scoped gồm learner/current assignment, nhóm, người nhận
hồ sơ active, giáo viên active và xe active bằng một batch query. Vòng lặp tối đa 5.000 dòng
chỉ resolve trên lookup trong bộ nhớ, không phát sinh database round-trip theo từng dòng.
Optional `HocVienId` chỉ được cross-check sau khi `MaDangKy + MaKhoa + SourceProfileCode`
trả về đúng một học viên; business key mơ hồ luôn là `AMBIGUOUS`.

Preview phân loại `READY`, `NO_CHANGE`, `NOT_FOUND`, `AMBIGUOUS`,
`INACTIVE_REFERENCE`, `INVALID`, `CONFLICT`. Chỉ file gồm READY/NO_CHANGE mới được confirm.
Confirm là một transaction: bất kỳ revalidation/affected-row assertion nào fail thì rollback
toàn bộ, không partial silent success. Import không auto-create teacher, vehicle, group hay
người nhận hồ sơ và không match theo tên.

Idempotency được bind qua durable `App_AssignmentOperation`: hash của key, operation type,
actor, file/payload SHA-256, exact `KhoaHocId` và `SourceProfileCode`; kết quả chỉ có thể tải
lại từ đúng course/profile-scoped result route. Ledger và mutation commit trong cùng
transaction. Retry sau process restart với cùng preview token hoặc một preview tương đương
của đúng logical plan trả lại kết quả đã hoàn tất; tái dùng key cho actor, operation,
course/profile hoặc payload khác trả conflict 409.

Focused tests bao phủ exact 18 headers, text/date, formula neutralization, hidden identity,
blank=KEEP, formula/external-link rejection và ranh giới đúng 5.000/5.001 dòng.

Kết quả focused hiện tại nằm trong suite assignment **85 PASS, 0 FAIL**; ba test riêng cho
giới hạn/parsing và set-based 5.000 dòng đều PASS. Frontend production build và broad backend
regression cũng PASS. Không có file import nào được ghi vào production.
