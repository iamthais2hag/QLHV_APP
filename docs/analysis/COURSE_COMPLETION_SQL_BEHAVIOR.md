# Course completion — SQL behavior discovery

## 1. Kết quả khảo sát

Ngày khảo sát: **2026-07-31**. Phạm vi là source hiện tại, gói phần mềm CSDT cũ mới nhất tìm thấy và truy vấn production **read-only** trên `CSDL_OTO`, `CSDL_MOTO` cùng các database V1 tương ứng.

Không tìm thấy một thao tác SQL chính thức mang nghĩa “hoàn thành toàn bộ khóa học”. Phần mềm cũ có ba luồng dễ bị hiểu nhầm là cùng một chức năng, nhưng mutation khác nhau:

1. **Cập nhật kết quả đào tạo**: cập nhật **một học viên** trong `NguoiLX_HoSo` qua `dbo.usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat`.
2. **Khóa dữ liệu khóa học**: đặt `KhoaHoc.TrangThai = 0` qua `dbo.usp_KhoaHoc_Update_TrangThai`; đây là khóa sửa dữ liệu, không có bằng chứng là “hoàn thành khóa học”.
3. **Kết xuất xác nhận hoàn thành khóa đào tạo**: chỉ đọc dữ liệu và mở Crystal Report; không cập nhật database.

Luồng Báo cáo II và phê duyệt của Sở nằm **sau** kết quả đào tạo. Luồng này có mutation riêng trên hồ sơ học viên và có thể đưa học viên tới `TT_XuLy = '13'` (“Hoàn thành đào tạo”), nhưng không đặt một trạng thái hoàn thành trên `KhoaHoc`.

### 1.1 Operator Course Completion Contract V1

Contract được operator chốt sau discovery định nghĩa completion là marker QLHV-owned: **“Đã chốt kết quả đào tạo của khóa tại thời điểm xác nhận.”** Vì vậy việc phần mềm cũ không có course-wide mutation không còn là khoảng trống cần mô phỏng.

Mutation V1 mới được giới hạn như sau:

- chỉ ghi `App_CourseCompletion`, snapshot học viên, idempotency ledger và audit trong QLHV_APP;
- không cập nhật `KhoaHoc`, `NguoiLX_HoSo` hoặc bất kỳ object V2/V1 nào;
- không sinh/sửa chứng nhận, Báo cáo II, XML, `MaBC2`, sát hạch hoặc GPLX;
- không có reopen; sai sót đi qua correction workflow riêng.

Các SQL mutation cũ bên dưới được giữ làm bằng chứng phân loại và bảo vệ boundary, không phải implementation target.

## 2. Nguồn bằng chứng

### 2.1 Gói CSDT cũ

Gói được khảo sát:

`D:\Phần Mềm GPLX 2025-2026\17-06-2026\GPLX-CDB-BoCai-V03\GPLX-CDB-BoCai-V03\CSDT_V03_150626`

| Artifact | SHA-256 |
|---|---|
| `150626-Script-Update-Database-CSDT.sql` | `0B8B575AB6CA06357032DF68413F306F9412A25F1EEE0C46FE0297DE56517980` |
| `FPT.GPLX.CSDT.dll` | `B8D790A8BD5AA6C39105C0E9DDEBA96C76099932C2EB09B9F7A0616584AA630E` |
| `FPT.GPLX.BO.dll` | `E5577A2A29C3BCEE0C0EF555D8FB25F9075F424A2C8AA4BB36AD120C80BC01B9` |
| `FPT.GPLX.DAL.dll` | `74903B769206D5791BC14C79DA3C50420DDF820D40AB3618988D8F265AEAB1F5` |
| `PM1\Reports\rptHoanThanhKhoaDT.rpt` | `046C32B7CB35C5DF3BBFC0331D8AC64AA97705F92DFA779A8D80BB75B99D6E5F` |

Ba DLL có file version `1.0.0.0`, timestamp 2026-06-18. Không có source/PDB kèm theo, nên bằng chứng UI được ghi theo **assembly + fully qualified type + method**, không gán số dòng giả.

### 2.2 Source QLHV_APP hiện tại

- `server/QLHV.Api/Controllers/AssignmentControllers.cs:125-178`: controller khóa học chỉ có tìm kiếm, chi tiết phân công, nhóm và lịch sử; không có endpoint hoàn thành/mở lại.
- `client/src/features/course-assignment/CourseDetailPage.tsx:473-500`: trang chi tiết hiện chỉ hiển thị dữ liệu khóa và đánh dấu dữ liệu nguồn là chỉ đọc; không có nút hoàn thành.
- `server/QLHV.Infrastructure/Assignments/SqlAssignmentRepository.CatalogWrites.cs:273-290`: chi tiết khóa chỉ đọc `App_KhoaHoc`; không có completion mutation.
- `server/QLHV.Application/Assignments/AssignmentPolicies.cs:8-18`: capability hiện tại chỉ phục vụ phân công; chưa có quyền completion.
- `server/QLHV.Api/Program.cs:105-131`: mapping quyền hiện tại tách view/manage/import/export/history và yêu cầu tài khoản không còn trạng thái buộc đổi mật khẩu.

## 3. Trace giao diện cũ tới mutation cuối

### 3.1 “Cập nhật kết quả đào tạo”

Trace binary:

`FPT.GPLX.CSDT.PM1.ucQuanLyKetQuaDT.btnCapNhatKQ_Click`
→ `FPT.GPLX.CSDT.PM1.QLHocSinh.uc_CSDT_TimKhoaDT_UCCN`
→ `ClickCapNhat`
→ `frmCapNhatCCN_ByKH.UpdateKQDT`
→ `FPT.GPLX.BO.CSDT.NguoiLXBO.NguoiLX_KetQuaDaoTao_CSDT_CapNhat`
→ `dbo.usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat`.

Stored procedure nằm tại `150626-Script-Update-Database-CSDT.sql:17024-17078`.

Mutation thực tế trên `NguoiLX_HoSo`:

- `KQLyThuyet`, `KQThucHanh`;
- `TGBatDau`, `TGKetThuc`;
- `KetLuanCSDT`;
- `DiemLyThuyet`, `DiemThucHanh`;
- `TGThucHanhDuong`, `TGThucHanhHinh`;
- `SoKMDuong`, `SoKMHinh`;
- `QDThucHanhHinh` ở schema V2;
- `NgaySua = GETDATE()`;
- `TT_XuLy`: giữ nguyên nếu đang `90`, `13` hoặc `14`; nếu kết luận đạt thì `09`, không đạt thì `10`, trường hợp khác giữ trạng thái cũ.

Điều kiện định danh cuối là `MaDK` và `MaKhoaHoc`. Procedure dùng transaction và kiểm tra `@@ERROR`, nhưng không có `TRY/CATCH`, `THROW`, kiểm tra `@@ROWCOUNT`, idempotency key hoặc optimistic version. Do đó gọi procedure nhiều lần theo vòng lặp ngoài ứng dụng không tạo được một giao dịch atomic cho cả khóa.

Validation trong `frmCapNhatCCN_ByKH.UpdateKQDT` là theo **một học viên**:

- luôn yêu cầu `MaDK`, khóa học, `KetLuanCSDT`, thời gian bắt đầu/kết thúc đào tạo và `TGKetThuc >= TGBatDau`;
- với hạng không phải `B1m` và chuỗi hạng không chứa `A`, yêu cầu thêm kết quả/điểm lý thuyết, thực hành, thời gian và quãng đường hình/đường;
- nhánh hạng `A*`/`B1m` ghi các trường bổ sung là `NULL`.

Không thấy validation giáo viên, xe, chương trình, Báo cáo I, giấy tờ/ảnh hoặc trạng thái toàn bộ học viên trong save handler này.

### 3.2 “Khóa dữ liệu khóa học”

Trace binary:

`FPT.GPLX.CSDT.PM1.QLKhoaDT.uc_CSDT_TimKhoaDT.tsbKhoa_Click`
→ `KhoaHoc.TrangThai = false`
→ `FPT.GPLX.BO.CSDT.KhoaHocBO.UpdateTrangThaiKhoaHoc`
→ `dbo.usp_KhoaHoc_Update_TrangThai`.

SQL tại `150626-Script-Update-Database-CSDT.sql:12242-12265` chỉ cập nhật:

```text
KhoaHoc.TrangThai = @TrangThai
KhoaHoc.NguoiSua = @NguoiSua
KhoaHoc.NgaySua = GETDATE()
WHERE MaKH = @MaKH
```

Thông báo UI nói thao tác khóa sẽ vô hiệu hóa việc sửa thông tin khóa học. Không thấy mutation học viên, ngày hoàn thành, kết quả, chứng nhận hoặc Báo cáo II; cũng không thấy một lệnh UI chuyên biệt để “mở lại khóa đã hoàn thành”. Vì vậy không được dùng `TrangThai = 0` làm completion contract nếu chưa có quyết định nghiệp vụ riêng.

### 3.3 “Kết xuất hoàn thành khóa đào tạo”

Trace binary:

`FPT.GPLX.CSDT.PM1.ucQuanLyKetQuaDT.btnXacNhanKHKH_Click`
→ `FPT.GPLX.CSDT.PM1.QLKetQuaDT.ucKetXuatXacNhanHoanThanhKH`
→ `btnKetXuatHTKH_Click`
→ dataset `dbo.usp_NguoiLX_HoSo_RPT_CNTN`
→ `PM1\Reports\rptHoanThanhKhoaDT.rpt`.

Tìm kiếm của màn hình truyền trạng thái UI `08`; SQL ánh xạ sang `NguoiLX_HoSo.TT_XuLy = '13'` tại `150626-Script-Update-Database-CSDT.sql:16864-16947`. Nút kết xuất chỉ nạp dữ liệu report và hiển thị `CHUNG_NHAN_TOT_NGHIEP`; không có `INSERT`, `UPDATE` hay `DELETE`.

Procedure report tại `150626-Script-Update-Database-CSDT.sql:14780-14855` là read-only. Hai cột ngày khóa trong output còn được trả `NULL`; report dựa chủ yếu vào kết quả đào tạo/chứng nhận của học viên.

## 4. Các mutation downstream liên quan

### 4.1 Kết xuất XML/Báo cáo II

`ucKetXuatKetQuaDaoTao.btnKetXuatXML_Click` là workflow khác, chạy trên tập học viên được chọn. Sau khi tạo XML/queue, code LINQ đặt:

- `NguoiLX_HoSo.MaBC2`;
- `TT_XuLy = '12'`;
- `NgaySua = DateTime.Now`;
- `SoGiayCNTN = MaDK + '-' + GetHangGPLXChuanHoa(HangDeNghiSH)`;
- `NgayRaQDTN = DateTime.Now`.

Sau đó gọi `DataContext.SubmitChanges()`. Đồng hồ tiến trình (`DateTime.Now`) là giờ cục bộ của process cũ, không đáp ứng time-authority contract hiện tại. File/queue và database không có bằng chứng cùng nằm trong một distributed transaction. Đây là bước downstream, không phải completion mutation an toàn để tái sử dụng.

Procedure `dbo.usp_NguoiLX_TongHop_By_MaKH2` tại `150626-Script-Update-Database-CSDT.sql:17614-17658` chỉ đưa tập học viên đủ điều kiện vào trạng thái `11` và lưu trạng thái cũ; nhánh bỏ `MaKH` có thể khôi phục trạng thái cũ. Luồng cho phép chọn subset, nên không chứng minh quy tắc “tất cả học viên phải đạt”.

### 4.2 Tiếp nhận phê duyệt kết quả

`dbo.usp_BaoCaoII_Update_PheDuyetKQDT` tại `150626-Script-Update-Database-CSDT.sql:4057-4075` đặt `BaoCaoII.TrangThai = 1` và `NgaySua = GETDATE()`.

`dbo.usp_CSDT_PheDuyetKQDT_TiepNhan` tại `150626-Script-Update-Database-CSDT.sql:4407-4503` yêu cầu học viên đang ở trạng thái `12`, rồi cập nhật dữ liệu phê duyệt/định danh downstream và chuyển:

- phê duyệt đạt → `TT_XuLy = '13'`;
- không đạt → `TT_XuLy = '14'`.

Procedure này có `TRY/CATCH`, rollback transaction và kiểm tra số dòng. Trạng thái `13` là trạng thái **học viên** sau phê duyệt của Sở, không phải trạng thái của bảng `KhoaHoc`.

### 4.3 Hủy Báo cáo II và chứng nhận

- UI `uc_CSDT_DSBaoCao2.btnHuyBC_Click` chỉ cho hủy khi Báo cáo II chưa ở trạng thái đã cập nhật kết quả; nếu đã phê duyệt thì hiển thị “Báo cáo 2 đã cập nhật kết quả không hủy được”.
- `dbo.usp_NguoiLXHoSo_Update_DanhSachHocSinhBC2` tại `150626-Script-Update-Database-CSDT.sql:17812-17845` có nhánh bỏ học viên khỏi BC2 và khôi phục trạng thái cũ trong phạm vi nhất định.
- `dbo.usp_NguoiLX_HoSo_Update_CNTN` tại `150626-Script-Update-Database-CSDT.sql:16173-16255` chỉ đảo trạng thái `09 → 03` khi xóa chứng nhận; các trạng thái downstream khác được giữ nguyên.

Không có bằng chứng rollback toàn chuỗi sau khi đã phê duyệt Báo cáo II, tạo kỳ sát hạch hoặc cấp GPLX.

## 5. Schema production liên quan

Audit read-only xác nhận `CSDL_OTO` và `CSDL_MOTO` V2 có cùng cấu trúc cho các object cốt lõi; hai database V1 cũng giống nhau trong cặp của mình. Khác biệt đáng chú ý: V1 thiếu `NguoiLX_HoSo.QDThucHanhHinh`, ít cột `GiaoVien` hơn và `KhoaHoc_XeTap` khác nullability/FK.

| Table | Khóa/chính sách đáng chú ý |
|---|---|
| `KhoaHoc` | PK `MaKH`; unique `(MaKH, MaCSDT, MaSoGTVT)`; ngày khóa nullable; `TrangThai bit NOT NULL DEFAULT 1`; không có `CompletedAt` hay rowversion. |
| `NguoiLX_HoSo` | PK/unique `MaDK`; `MaKhoaHoc` nullable, FK tới `KhoaHoc` với cascade; `MaBC1` FK tới `BaoCaoI`; trường kết quả/chứng nhận phần lớn nullable; không có rowversion. |
| `BaoCaoI` | PK `MaBCI`; unique `SoBaoCao`; FK `MaKH` tới `KhoaHoc` với cascade. |
| `BaoCaoII` | PK `MaBCII`; `MaBCI` nullable nhưng không có FK tới `BaoCaoI`; `TrangThai DEFAULT 0`. |
| `KhoaHoc_GiaoVien` | PK `MaLichLV`; FK khóa học/giáo viên. |
| `KhoaHoc_XeTap` | PK `MaLichSD`; FK khóa học/xe; quan hệ giáo viên khác giữa V1/V2. |
| `NguoiLXHS_GiayTo` | PK `(MaGT, MaDK)`; FK học viên cascade; không có completion gate trong procedure đã tìm thấy. |

Các bảng trên không có trigger và không có check constraint liên quan đến completion. Không tìm thấy cột nghiệp vụ rõ ràng cho bảo lưu, nghỉ học, chuyển khóa hoặc ngừng đào tạo. `KhoaHoc.HTDaoTao` là **Hình thức đào tạo**, không phải “Hoàn thành đào tạo”.

## 6. Status code đã xác minh

| `TT_XuLy` | Ý nghĩa liên quan |
|---|---|
| `03` | Đã đăng ký khóa học |
| `05`, `06`, `07` | Các bước đang đào tạo |
| `09` | Đạt tốt nghiệp / đã cập nhật kết quả |
| `10` | Không đạt tốt nghiệp |
| `11` | Đã tạo Báo cáo II |
| `12` | Đã gửi Báo cáo II/chờ Sở phê duyệt |
| `13` | Phê duyệt đạt; màn hình trung tâm hiển thị “Hoàn thành đào tạo” |
| `14` | Phê duyệt không đạt |
| `16`, `17`, `18` | Các trạng thái sát hạch |
| `19` | Đã nhận GPLX |
| `90` | Trạng thái giải trình/ngoại lệ |

`dbo.usf_GetTrangThaiHocVien` tại `150626-Script-Update-Database-CSDT.sql:1779-1803` hiển thị `13` là hoàn thành. Nhánh `ELSE` của function cũng trả chữ “Hoàn thành”, nên function này chỉ phù hợp hiển thị; không được dùng làm eligibility gate.

## 7. Dữ liệu production quan sát được

Snapshot read-only trong ngày 2026-07-31, chỉ dùng để hiểu dữ liệu, **không dùng row count làm safety gate**:

| Profile | Khóa active | Hồ sơ active | Kết quả hiện tại | Downstream quan sát được |
|---|---:|---:|---|---|
| OTO | 5 | 189 | tất cả đang `03`, thiếu kết luận và thời gian kết quả | chưa có BCII/kỳ sát hạch/chứng nhận trong tập quan sát |
| MOTO | 2 | 5 | tất cả đang `01`, thiếu kết luận và thời gian kết quả | chưa có BCII/kỳ sát hạch/chứng nhận trong tập quan sát |

OTO có 3/5 khóa có Báo cáo I; MOTO chưa có Báo cáo I. Đây chỉ là hiện trạng mẫu, không chứng minh một business rule.

## 8. Mutation graph thực tế

```text
KhoaHoc (TrangThai=1/0: active/edit lock)
   |
   +--> NguoiLX_HoSo per learner
          |  usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat
          |  result fields + TT_XuLy 09/10
          v
       chọn/tổng hợp BCII -> TT_XuLy 11
          |
          v
       xuất/gửi BCII/XML -> MaBC2 + certificate fields + TT_XuLy 12
          |
          v
       Sở phê duyệt -> TT_XuLy 13/14
          |
          v
       sát hạch/GPLX -> TT_XuLy 16..19 và dữ liệu V1-owned
```

Luồng report “Xác nhận hoàn thành khóa đào tạo” đọc học viên ở trạng thái `13`; nó không nằm trên cạnh mutation nào.

Contract V1 bổ sung một nhánh độc lập, không nối vào mutation graph nguồn:

```text
V2/V1 authoritative state --read-only--> sealed learner snapshot/classification
                                              |
                                              v
                                  QLHV marker + ledger + audit
                                  (one QLHV transaction)
```

## 9. Kết luận SQL

Đã xác minh được các mutation thực tế và operator đã chọn contract marker-only, không sửa source/downstream. Transaction completion vì thế chỉ bao gồm marker, snapshot, idempotency ledger và audit của QLHV_APP. Reversal không còn thuộc V1 vì contract không cung cấp thao tác mở lại; mọi sai sót chuyển sang correction workflow riêng.

**READY FOR COURSE COMPLETION IMPLEMENTATION APPROVAL**
