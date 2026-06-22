# Bàn giao dự án QLHV_APP

## Repo và Git

* Local repo: `D:\QLHV_APP`
* Branch ảnh/in thẻ: `task5-phase-b3t-hocvien-photo-display`
* Commit: `edf25e4 Add Hoc Vien photo audit and card printing`
* Sau khi merge, mọi phát triển tiếp theo làm trên `main`.

## Quy tắc an toàn bắt buộc

* Không ghi DB.
* Không chạy SQL update/insert/delete.
* Không execute sync.
* Không bật `EnableTargetWrites`.
* Không sửa connection string thật.
* Không commit ảnh học viên thật.
* Không expose physical path từ backend ra frontend.
* Không tạo ảnh thumb/card/jpg phụ trên ổ đĩa.

## Ảnh học viên

Nguồn ảnh:

```text
D:\QLHV_APP\IM_GPLX\{MaKhoa}\{MaDK}.jp2
```

Ví dụ:

```text
D:\QLHV_APP\IM_GPLX\66016K26A0001\66016-20251229-145551540.jp2
```

Cấu hình đúng:

```json
"FileStorage": {
  "Root": "../..",
  "HocVienPhotoFolder": "IM_GPLX"
}
```

Vì API chạy từ:

```text
D:\QLHV_APP\server\QLHV.Api
```

và:

```text
../.. = D:\QLHV_APP
```

Đã có test:

```text
server/QLHV.Tests/HocVien/HocVienPhotoPathResolverTests.cs
```

Ảnh JP2 decode trong memory bằng Magick.NET. Không tạo ảnh phụ.

## Chức năng Học viên đã có

* Autocomplete Khóa:

  * tìm theo Tên khóa hoặc Mã khóa;
  * bắt buộc chọn gợi ý;
  * label dạng `AK01 - 66016K26A0001`.
* Autocomplete Hạng học:

  * tìm theo mã như `Am`, `A1m`;
  * không bắt nhập “Hạng Am”.
* Khi đã chọn Hạng học, lookup Khóa lọc theo hạng.
* Xuất Excel toàn bộ kết quả lọc.
* Excel Times New Roman 13, header bold, auto-fit có giới hạn.
* Cột web “Hạng học” hiển thị `HangGPLXHoc`; tooltip có `MaHangDT`.

## Ảnh và in thẻ

### Preview ảnh

```text
GET /api/hoc-vien/{hocVienId}/photo/preview
```

* Đọc JP2 từ `IM_GPLX/{MaKhoa}/{MaDK}.jp2`.
* Crop/resize 3:4 trong memory.
* Không có ảnh trả 404 để frontend dùng placeholder.

### Đối soát ảnh

```text
GET /api/hoc-vien/photos/audit
```

Hỗ trợ:

```text
maKhoa, maHangDT, keyword, page, pageSize,
validateDecode, onlyMissing, onlyInvalid
```

Trạng thái:

```text
HasPhoto, Missing, Invalid, Unsupported, UnsafePath
```

### In thẻ

Thẻ:

```text
85mm × 50mm
```

Ảnh trên thẻ:

```text
30mm × 40mm
```

A4 ngang:

```text
297mm × 210mm
3 cột × 4 dòng = 12 thẻ/tờ
Gap 1mm
Lề trái/phải 20mm
Lề trên/dưới 3.5mm
```

Endpoints:

```text
POST /api/hoc-vien/the-hoc-vien/print-preview
POST /api/hoc-vien/the-hoc-vien/print-a4
```

Modes đã hỗ trợ:

```text
single
selected
course
```

`teacherInCourse` chưa làm thật vì chưa xác nhận quan hệ giáo viên–học viên; phải trả 400 rõ ràng, không tự đoán.

Options:

```text
missingPhotoMode: placeholder | skip
sortBy: current | hoTen | maDK
```

## Build/test gần nhất

```text
Backend build: passed, 0 warnings, 0 errors
Backend test: passed, 89 passed, 1 skipped SQL opt-in safety test
Frontend build: passed
```

## Khi bắt đầu task mới

Luôn chạy:

```powershell
cd D:\QLHV_APP
git status
git branch --show-current
git log --oneline -5
```

Không được làm mất:

* autocomplete Khóa/Hạng học;
* logic Khóa phụ thuộc Hạng học;
* export Excel toàn bộ kết quả lọc;
* UI bảng Học viên;
* cấu trúc ảnh `IM_GPLX\{MaKhoa}\{MaDK}.jp2`;
* xử lý JP2 trong memory.
