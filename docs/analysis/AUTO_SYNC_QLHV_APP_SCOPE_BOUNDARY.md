# PHẠM VI TASK — AUTO SYNC NHẬP DỮ LIỆU CSDT VÀO QLHV_APP

## Tính năng cần xử lý

Màn hình:

```text
Nhập dữ liệu CSDT → Auto Sync
```

Pipeline đúng:

```text
Ô tô:
CSDL_OTO → CSDL_OTO_BAK → QLHV_APP

Mô tô:
CSDL_MOTO → CSDL_MOTO_BAK → QLHV_APP
```

Đây là tính năng nhập dữ liệu từ CSDL của cơ sở đào tạo vào dữ liệu nội bộ của ứng dụng QLHV_APP.

## Lỗi hiển thị hiện tại

UI đang hiển thị sai:

```text
CSDL_OTO → CSDL_OTO_BAK → CSDT_OTO
CSDL_MOTO → CSDL_MOTO_BAK → CSDT_MOTO
```

Phải sửa thành:

```text
CSDL_OTO → CSDL_OTO_BAK → QLHV_APP
CSDL_MOTO → CSDL_MOTO_BAK → QLHV_APP
```

Không đổi tên database thật chỉ để sửa nhãn giao diện.

## Tuyệt đối không nhầm với dự án sync V2 → V1

Task này KHÔNG PHẢI:

```text
CSDL_OTO → CSDL_OTO_V1
CSDL_MOTO → CSDL_MOTO_V1
```

Task này KHÔNG thuộc:

- H12 exclusion registry;
- H14 Change Tracking;
- H15 atomic mapped-table cycle;
- V2 → V1 realtime;
- đồng bộ Báo cáo II, sát hạch hoặc GPLX;
- membership registry;
- typed ownership claim;
- 75 stored-procedure guards;
- DELETE/deactivation/reactivation V2 → V1.

Không production-wire hoặc sửa các thành phần realtime V2 → V1 trong task Auto Sync này.

## Dấu hiệu lỗi cần điều tra

Từ màn hình hiện tại:

```text
Ô tô:
Live / NguoiLX = 152
BAK / NguoiLX = 148
QLHV active = 148
```

Ngoài ra:

- Auto Sync hiển thị đang bật;
- trạng thái là sẵn sàng;
- có thời gian sync thành công gần nhất;
- nhưng lần chạy gần nhất, trạng thái OTO/MOTO và lịch sử lại chưa có;
- card nguồn báo thành công một phần;
- Full sync yêu cầu lập kế hoạch mới.

Cần trace chính xác:

```text
Frontend Auto Sync screen
→ API
→ controller/service
→ background worker
→ BAK refresh
→ validate
→ build plan
→ import vào QLHV_APP
→ status/history persistence
```

## Điểm bắt đầu tìm code

Tìm theo các literal trên màn hình:

```text
Nhập dữ liệu CSDT
Auto Sync
Chạy Auto Sync ngay
Làm mới dữ liệu BAK
Kiểm tra dữ liệu
Lập kế hoạch
Đồng bộ vào QLHV_APP
Thành công một phần
Full sync: Cần lập kế hoạch mới trước khi đồng bộ.
Chạy tuần tự OTO rồi MOTO trên máy chủ
```

## Kết quả mong đợi

1. Sửa đúng nhãn pipeline về QLHV_APP.
2. Xác định và sửa lỗi vòng Auto Sync hiện hữu.
3. Đồng nhất trạng thái, lịch sử, last run và last success.
4. Giải thích hoặc chứng minh nguyên nhân lệch OTO 152/148/148.
5. Làm rõ lifecycle Refresh BAK → Validate → Plan → Import QLHV_APP.
6. Ngăn manual run và auto run chạy chồng.
7. Không ảnh hưởng đến nền tảng sync V2 → V1 đang phát triển riêng.

## Repo safety

Không reset, clean, stash, revert, stage, commit, merge hoặc push.

Không sửa:

```text
server/QLHV.Api/appsettings.Development.json
server/QLHV.Worker/appsettings.Development.json
```

Không chạy sync thật, backup/restore thật, SQL write, patch hoặc ALTER DATABASE nếu chưa có phê duyệt riêng.
