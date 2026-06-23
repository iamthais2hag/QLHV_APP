# Mẫu số 02 - Phù hiệu học viên tập lái xe

## Tổng quan

Chức năng in thẻ học viên áp dụng template chính thức **Mẫu số 02 - Phù hiệu
học viên tập lái xe** và tạo PDF trực tiếp trong bộ nhớ từ dữ liệu đọc trong
`QLHV_APP.dbo.App_HocVien` và ảnh JP2 tại:

```text
IM_GPLX/{MaKhoa}/{MaDK}.jp2
```

Ảnh JP2 được decode trong bộ nhớ. Hệ thống không tạo thumbnail, JPG trung gian,
file thẻ hoặc PDF tạm trên ổ đĩa.

## API

### Xem trước danh sách in

```http
POST /api/hoc-vien/the-hoc-vien/print-preview
```

Endpoint chỉ trả metadata và dữ liệu an toàn cần cho preview frontend:

- `totalStudents`
- `totalPages`
- `cardsPerPage`
- `layoutName`
- `missingPhotoCount`
- nội dung tổ chức/title dùng trên thẻ
- `items[]` gồm dữ liệu an toàn phục vụ đối chiếu danh sách và dựng thẻ trang đầu

Endpoint không sinh PDF và không trả đường dẫn vật lý của ảnh.

### Tạo PDF A4

```http
POST /api/hoc-vien/the-hoc-vien/print-a4
```

PDF được tạo trong bộ nhớ và trả thẳng về response với content type
`application/pdf`.

### Request modes

- `single`: in một học viên theo `hocVienId`.
- `selected`: in danh sách `hocVienIds` đã chọn.
- `course`: in toàn bộ học viên theo `maKhoa`.
- `teacherInCourse`: chưa hỗ trợ vì chưa có quan hệ phân công giáo viên-học viên
  được xác nhận; API trả lỗi 400 rõ ràng.

Giới hạn an toàn hiện tại là 1.000 học viên cho một lần in.

## Tùy chọn

### missingPhotoMode

- `placeholder`: vẫn in thẻ; ô ảnh hiển thị placeholder rõ ràng.
- `skip`: bỏ học viên có trạng thái ảnh `Missing` hoặc `UnsafePath`.
- Ảnh invalid/unsupported không làm hỏng cả PDF; thẻ được in với placeholder.

### sortBy

- `current`: giữ thứ tự hiện tại từ repository.
- `hoTen`: sắp xếp theo họ tên, sau đó theo `HocVienId`.
- `maDK`: sắp xếp theo mã đăng ký, sau đó theo `HocVienId`.

## Quy cách layout

- Khổ giấy: A4 ngang, 297mm x 210mm.
- Lưới: 3 cột x 4 dòng, 12 thẻ/trang.
- Kích thước thẻ: 85mm x 50mm.
- Khoảng cách giữa thẻ: 1mm.
- Lề trái/phải: 20mm.
- Lề trên/dưới: 3,5mm.
- Header: 85mm x 10mm, chạy toàn chiều ngang thẻ.
- Body: 85mm x 40mm, nằm ngay dưới header.
- Ô ảnh bên trái: 30mm x 40mm.
- Ô nội dung bên phải: 55mm x 40mm.
- Có khung đen mảnh quanh thẻ, đường ngang dưới header và đường dọc phân cách
  ảnh với nội dung.

Mọi chuyển đổi mm sang point được thực hiện qua `HocVienCardLayout.MmToPoint`.
Frontend mô phỏng cùng tỷ lệ A4 và lưới 3x4 để kiểm tra trang đầu trước khi tải.

## Nội dung thẻ chính thức

Header gồm hai dòng căn giữa:

1. `SỞ XÂY DỰNG TỈNH GIA LAI`
2. `TRUNG TÂM ĐÀO TẠO LÁI XE THÀNH CÔNG`

Ô nội dung bên phải chỉ gồm ba dòng căn giữa:

1. `HỌC VIÊN TẬP LÁI XE`
2. Họ tên học viên viết hoa
3. `Tập lái xe hạng: {HangGPLXHoc}`

Mã đăng ký, tên khóa và mã khóa vẫn có thể xuất hiện trong bảng đối chiếu của
modal nhưng **không được in trên thẻ và không xuất hiện trong preview hình thẻ**.

Template nằm trong `HocVienCardTemplate`; nội dung tổ chức và các vị trí tương đối
không bị hardcode rải rác trong renderer.

## PDF và font

Renderer dùng PDFsharp 6.2.4 (MIT) để tạo PDF, đo text, nhúng font Unicode và vẽ
ảnh. Lý do thay renderer cũ là renderer cũ tự nối PDF bằng chuỗi ASCII, bỏ dấu
tiếng Việt và phụ thuộc nhiều tọa độ hardcode khó kiểm tra.

Template chính thức dùng Times New Roman hệ thống Windows thông qua
`GlobalFontSettings.UseWindowsFontsUnderWindows`. Không có font binary được commit
vào repository. Khi triển khai trên Linux/macOS phải cấu hình font resolver hợp lệ
trước khi dùng chức năng in.

## Hướng dẫn in

1. Chọn một học viên, nhiều học viên hoặc một khóa.
2. Mở modal **In thẻ học viên**.
3. Kiểm tra tổng số học viên, số trang, cảnh báo ảnh và preview A4 trang đầu.
4. Chọn chế độ ảnh thiếu và cách sắp xếp.
5. Chọn **Xem trước PDF** để tải PDF vào panel ngay trong modal. Cách này không
   phụ thuộc popup và không mở tab sau một request bất đồng bộ.
6. Có thể chọn **Mở PDF trong tab mới** từ panel; đây là thao tác trực tiếp của
   người dùng trên blob URL đã tạo.
7. Chọn **Tải PDF**. Nếu PDF đã được xem trước, frontend dùng lại chính blob đó;
   nếu chưa có, frontend gọi endpoint tạo PDF như trước.
8. Trong hộp thoại in PDF, chọn A4 ngang và **Actual size / 100%**.
9. Không chọn Fit/Shrink vì sẽ làm sai kích thước 85mm x 50mm.

Blob URL chỉ tồn tại trong bộ nhớ trình duyệt và được thu hồi khi đóng panel,
đổi tùy chọn in, tạo PDF mới hoặc đóng modal. PDF không được lưu tạm trên server.

Nên in thử một trang trên giấy thường và đo thẻ trước khi in hàng loạt.

## Quy tắc an toàn

- Không ghi database khi preview hoặc tạo PDF.
- Không chạy đồng bộ dữ liệu.
- Không tạo ảnh hoặc PDF phụ trên ổ đĩa.
- Không trả đường dẫn vật lý `D:\...` ra API/frontend.
- Frontend chỉ tải ảnh qua endpoint preview theo `hocVienId`.
- Ảnh thật trong `IM_GPLX` không được commit Git.

## Hạn chế đã biết

- `teacherInCourse` chưa thể triển khai cho đến khi quan hệ phân công
  giáo viên-học viên được xác nhận.
- Font Times New Roman hệ thống Windows là dependency runtime hiện tại.
- Preview frontend là mô phỏng để kiểm tra nhanh; PDF từ endpoint mới là tài liệu
  chuẩn dùng để in.
