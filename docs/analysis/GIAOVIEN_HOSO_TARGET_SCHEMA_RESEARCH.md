# Final schema decision: `App_GiaoVien_hs`

`dbo.App_GiaoVien_hs` is a wholly QLHV-owned dossier-receiver catalog. It is not `App_GiaoVien`, is not sourced from CSDL_OTO/MOTO and is outside every realtime write allowlist.

Required fields: `GiaoVienHsId`, unique nonblank `MaGiaoVienHs`, `HoTen`, derived `HoTenSearch`, nullable `NgaySinh`/`SoCCCD`, `TrangThai`, `GhiChu`, soft-delete fields, audit fields and `RowVersion`. `SoCCCD` has a filtered unique index when non-null. Names are not unique and are never automatic matching keys.

The QLHV_APP collation is accent-sensitive, so `HoTenSearch` is a versioned QLHV-derived search value. `ACTIVE` rows can be selected for new assignment; inactive/deleted rows remain resolvable for history. Referenced rows are never hard-deleted; the assignment FK is `ON DELETE NO ACTION`.

Excel maps `MaGiaoVienHoSo` exactly to `MaGiaoVienHs`. Export column 12 is `HoTen`; column 18 is `MaGiaoVienHs`. Legacy `App_HocVien.NguoiNhanHoSo` text is not auto-backfilled or converted to an FK.

Exact comment-only DDL is in `handoff/HOCVIEN_ASSIGNMENT_REVIEW/01_APP_GIAOVIEN_HS_PROPOSED_SCHEMA.sql`. No production migration was applied.
