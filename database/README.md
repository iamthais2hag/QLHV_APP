# Database scripts - QLHV_APP

Thư mục này chứa các script SQL cho QLHV_APP và cho việc chuyển/kiểm tra dữ liệu giữa
`CSDT_V1` và `CSDT_V2`.

> **Lưu ý quan trọng**
> - Các file được giữ nguyên tên và vị trí (không di chuyển) để không phá vỡ các tham chiếu
>   trong spec/code (ví dụ `design.md` trỏ tới `database/QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql`).
> - Phần dưới chỉ **phân nhóm logic** để dễ tra cứu.
> - Không lưu mật khẩu/connection string thật trong repo.
> - Tất cả script chuyển dữ liệu mặc định chạy ở chế độ **DryRun** và chỉ chạy trên **database backup test**.

## Phân nhóm logic

### 1. Khởi tạo schema QLHV_APP
| File | Mục đích |
| --- | --- |
| `QLHV_APP_DATABASE_SCHEMA_v2_PERFORMANCE.sql` | Tạo database `QLHV_APP` và toàn bộ bảng (bản tối ưu hiệu năng). |
| `QLHV_APP_DATABASE_PATCH_INDEX_JSON.sql` | Patch bổ sung index tra cứu / JSON check / lookup performance. |

### 2. Kiểm tra trước khi chuyển dữ liệu (pre-transfer)
| File | Mục đích |
| --- | --- |
| `CHECK_CCCD_CONFLICT_V1_V2.sql` | Phát hiện CCCD trùng nhưng MaDK khác giữa `CSDT_V1` và `CSDT_V2`. Chạy trước khi chuyển ALL khóa. |

### 3. Chuyển dữ liệu V1 → V2 (transfer)
| File | Mục đích |
| --- | --- |
| `TEST_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN.sql` | Chuyển ALL khóa/học viên V1 → V2. Mặc định `@DryRun = 1`. |
| `FORCE_V1_TO_V2_CHUYEN_ALL_KHOA_HOCVIEN_IGNORE_CCCD.sql` | Như trên nhưng tạm bỏ qua cảnh báo CCCD trùng MaDK khác. Chỉ chạy trên backup. |

### 4. Đổi mã CSĐT (remap)
| File | Mục đích |
| --- | --- |
| `REMAP_CSDT_CODE_IN_CSDT_V2.sql` | Đổi mã CSĐT trong `CSDT_V2` (ví dụ 66016 → 66026). Mặc định `@DryRun = 1`. |

### 5. Kiểm tra sau khi chuyển (post-transfer / validation)
| File | Mục đích |
| --- | --- |
| `VALIDATE_V1_TO_V2_CHUYEN_ALL_KHOA.sql` | Đối chiếu số lượng khóa/hồ sơ sau khi chuyển V1 → V2. |
| `V2_POST_TRANSFER_HEALTH_CHECK.sql` | Health check phát hiện lỗi sau khi chuyển dữ liệu. |

## Tài liệu liên quan
- Hướng dẫn cài đặt database: [`../docs/database-setup.md`](../docs/database-setup.md)
- Mô tả chi tiết từng script: [`../docs/sql-scripts-index.md`](../docs/sql-scripts-index.md)
