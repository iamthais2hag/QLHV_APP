# Final Excel export contract

The export has exactly 18 columns in this order:

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

Column 12 maps to `App_GiaoVien_hs.HoTen`; column 18 maps to `App_GiaoVien_hs.MaGiaoVienHs`. Column 15 maps to `App_GiaoVien.HoTen`; columns 16/17 use the relevant `App_XeTap` display label. The obsolete title “Hồ sơ giáo viên” is removed.

Current assignment must join exactly one `IsCurrent=1` row; duplicate current rows fail export. Identifiers are text, dates are true date cells formatted `dd/MM/yyyy`, ordering is deterministic and formula-injection values are neutralized. Export never logs raw PII.

Machine-readable mapping: `handoff/HOCVIEN_ASSIGNMENT_REVIEW/04_EXPORT_COLUMNS_AND_MAPPING.csv`.
