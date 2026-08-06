# V2 → V1 mapping owner approval checklist

Audit date: `2026-07-25`  
Direction: `V2 → V1`  
Owner status: `PENDING`

No item below is checked on behalf of the project owner. The machine-readable matrix also keeps every table and column at `approvalStatus: "PENDING"`.

## A. Tables approved for sync

### CORE_REQUIRED

- [ ] `DM_DonViGTVT` — only the routed CSDT row set; never the full national target table.
- [ ] `KhoaHoc`.
- [ ] `BaoCaoI`.
- [ ] `NguoiLX`.
- [ ] `NguoiLX_HoSo`.
- [ ] `NguoiLXHS_GiayTo`.

Core approval means INSERT, UPDATE, and the approved per-table DELETE/deactivate behavior participate in one core cycle. It does not approve copying every physical column.

### OPTIONAL_NON_BLOCKING

- [ ] `GiaoVien` — approve only after all code transforms and five V2-only columns are resolved.
- [ ] `KhoaHoc_GiaoVien`.

### DISABLED_PENDING_MAPPING

- [ ] Approve `XeTap`, or keep it disabled.
- [ ] Approve `KhoaHoc_XeTap`, or keep it disabled.
- [ ] Approve `DM_LuuLuongDaoTao`, or keep it disabled.
- [ ] Approve `LichHoc`, or keep it disabled.
- [ ] Approve `PhongHoc`, or keep it disabled; an explicit CSDT partition rule is required.

## B. Tables excluded as V1-owned

- [ ] `BaoCaoII` — whole table is V1-owned.
- [ ] `KySH` — whole table is V1-owned.
- [ ] `NguoiLX_GPLX` — whole table is V1-owned for forward sync.
- [ ] `DM_DiemSatHach`, `DM_LyDoTCBC2`, `DM_NoiDungSH` — V1 exam/BCII lookups.
- [ ] All remaining `DM_*` shared lookups are validate-only, not blindly synchronized.
- [ ] All `QTHT_*`, `TRANS_*`, `STT`, and `NGUOILX_BK_UPDATE_DVHC` tables are runtime/system and excluded.

Approval of an exclusion means no forward INSERT, UPDATE, DELETE, deactivation, or default rewrite. Reading keys/counts for validation remains allowed.

## C. Columns copied from V2

- [ ] Immutable keys are preserved exactly from V2 and are never regenerated:

```text
DM_DonViGTVT.MaDV
GiaoVien.MaGV
KhoaHoc.MaKH
KhoaHoc_GiaoVien.MaLichLV
BaoCaoI.MaBCI
NguoiLX.MaDK
NguoiLX_HoSo.MaDK
NguoiLXHS_GiayTo.(MaGT, MaDK)
```

- [ ] `KhoaHoc_GiaoVien.MaLichLV` is a target identity key. Approve serialized, transaction-scoped `IDENTITY_INSERT` for exact V2 values; reject collisions; guarantee `IDENTITY_INSERT OFF` on every exit; verify the next target-generated identity remains above the target maximum.
- [ ] For blocked identity keys `KhoaHoc_XeTap.MaLichSD` and `LichHoc.MaLichHoc`, approve either the same exact-key strategy or a proven alternate-key map before enabling either table; silent identity regeneration is forbidden.

- [ ] `NguoiLX_HoSo.TrangThai` is always V2-owned bit 0/1 on INSERT and UPDATE.
- [ ] `NguoiLX_HoSo.MaKhoaHoc` is always copied from V2 after parent validation; no lifecycle lock.
- [ ] `NguoiLX_HoSo.MaBC1` is always copied from V2 after parent validation; no lifecycle lock.
- [ ] Approve the remaining V2-owned dossier intake/training columns:

```text
SoHoSo, MaCSDT, MaSoGTVT, MaDVNhanHSo
NgayNhanHSo, NguoiNhanHSo, NgayHenTra, MaLoaiHs
DuongDanAnh, ChatLuongAnh, NgayThuNhanAnh, NguoiThuNhanAnh
SoGPLXDaCo, HangGPLXDaCo, DonViCapGPLXDaCo, NoiCapGPLXDaCo
NgayCapGPLXDaCo, NgayHHGPLXDaCo, NgayTTGPLXDaCo
DonViHocLX, NamHocLX, HangGPLX, SoNamLX, SoKmLXAnToan
LyDoCapDoi, MucDichCapDoi, HangDaoTao
SoGiayCNTN, SoCCN
BC1_TuoiTS, BC1_ThamNien, NgayKTBC1, NguoiKTBC1
KQ_BC1, KQ_BC1_GhiChu
VaoSoCNNSo, NgayVaoSoCNN, XepLoaiTotNghiep, NgayCapCCN
SoQuyetDinhTN, NgayRaQDTN, SoSoTN, NgayVaoSoTN, NgayInGiayTN
NamcapLandau, MaTrichNgang
KQLyThuyet, KQThucHanh, TongQDThucHanh, KetLuanCSDT
DiemKQLyThuyet, DiemKQThucHanh
TGBatDau, TGKetThuc, TGThucHanhHinh, TGThucHanhDuong
```

- [ ] Approve direct/validated V2 ownership for `DM_DonViGTVT`, `KhoaHoc`, `BaoCaoI`, `NguoiLX`, and `NguoiLXHS_GiayTo` as enumerated in the JSON matrix.

The JSON matrix is authoritative for all 755 live source columns and all 353 forward-candidate columns; this checklist intentionally groups routine exact-copy columns.

## D. Conditional merge columns

- [ ] `NguoiLX_HoSo.TT_XuLy` — approve the explicit string-set truth table:
  - V2: `01,02,03,04,05,06,07,09,10`;
  - V1: `00,11,12,13,14,16,17,18,19,90`;
  - V1 downstream wins;
  - source downstream emits `SOURCE_STATE_OUT_OF_SCOPE`;
  - unknown values fail closed.
- [ ] `NguoiLX_HoSo.GhiChu` — approve V2-before-downstream, preserve-V1-after-downstream.
- [ ] `NguoiLX_HoSo.GiayCNSK` — approve V2-before-downstream, preserve-V1-after-downstream.
- [ ] `NguoiLX_HoSo.GiaiTrinh` — approve V2-before-downstream, preserve-V1-after-downstream.

Reject any implementation that preserves the whole row merely because one conditional column is V1-owned for that state.

## E. V1-preserved columns

- [ ] Preserve the complete downstream dossier set:

```text
NoiDungSH
MaBC2, KetQuaBC2, MaLyDoTCBC2
MaKySH, SoBD, LanSH, SoQDSH, NgayQDSH
KetQua_LyThuyet, NhanXet_LyThuyet
KetQuaSHM, NhanXet_MoPhong
KetQua_Hinh, NhanXet_Hinh
KetQua_Duong, NhanXet_Duong
KetQuaSH
SoQDTT, NgayQDTT, NguoiKy, SoGPLXTmp
NgayKTBC2, NguoiKTBC2
MaIn, KetQuaDoiSanhTW, GhiChuKQDSTW, ChuKy
TT_XuLy_Old, CHON_IN_GPLX, KetQuaPDSo
DAT_QDThucHanh, DAT_TGThucHanh, DAT_KQCuc, DAT_ThoiGianLayKQ
LyDoTuChoiKQDT
MaHTCap, CoQuanQuanLyGPLX
Transfer_flag, HosoDvcc4
```

- [ ] Target audit/identity columns use target behavior and are not blindly copied: `NguoiTao`, `NguoiSua`, `NgayTao`, `NgaySua`, `IDs`.
- [ ] `NguoiLX_HoSo.QDThucHanhHinh` is V2-only and skipped because no target column exists.
- [ ] Five V2-only `GiaoVien` columns remain skipped until an explicit target design is approved.

## F. Transform/validation decisions

- [ ] `DM_DonViGTVT.TenDV`: allow only lossless values of at most 100 characters; never truncate. Current max is 52 and overflow count is 0.
- [ ] `DM_DonViGTVT.CoQuanQL`: approve the exact transform `MaCSDT IN (66029,66030) → "Sở Xây dựng tỉnh Đắk Lắk"`; at most 100 characters; never truncate. Current max is 47 and overflow count is 0. Code evidence: `CsdtRealtimeTargetWriter.ApplyRequiredBusinessMappings`.
- [ ] `GiaoVien.HinhThuc_TuyenDung`: approve enum mapping to `varchar(2)`.
- [ ] `GiaoVien.HangGPLX`: approve trim/code mapping to non-null `varchar(3)` plus target lookup validation.
- [ ] `GiaoVien.LoaiHinh_DaoTao`: approve enum mapping to `varchar(2)`.
- [ ] `GiaoVien.GhiChu`: enforce at most 255 characters; never truncate.
- [ ] `KhoaHoc_XeTap.MaGV`: require non-null and existing `GiaoVien` if the table is enabled.
- [ ] Every non-null FK/code must resolve in the target before write; current aggregate orphan count is zero.
- [ ] Every new/unclassified source column fails closed; it never inherits write permission.
- [ ] Missing shared lookup key policy is approved: pre-seed and validate, or explicit insert-if-missing. No blind lookup replacement.

## G. Delete policies

- [ ] `DM_DonViGTVT`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] `GiaoVien`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] `KhoaHoc`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] `KhoaHoc_GiaoVien`: `HARD_DELETE` only for V2-owned/no-history rows.
- [ ] `BaoCaoI`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] `NguoiLX`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] `NguoiLX_HoSo`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] `NguoiLXHS_GiayTo`: `DEACTIVATE_PRESERVE_V1_HISTORY`.
- [ ] Pending domains keep `UNKNOWN` and fail closed until ownership/retention is approved.
- [ ] No automatic SQL cascade is used to decide the deletion set.
- [ ] If the approved deactivation cannot be proven to exclude a row from all new-business queries, use `BLOCK_DELETE_CONFLICT` and do not advance the cycle.
- [ ] Enable CT and approve a durable per-stream membership registry before realtime delete is enabled.
- [ ] Full reconcile compares source keys with the stream registry, never with every target row.

## H. Unknown/unresolved owner decisions

There are 15 unresolved decisions. Their IDs match the JSON matrix.

- [ ] `H01` — Approve `GiaoVien` transforms and handling of five V2-only columns.
- [ ] `H02` — Approve `KhoaHoc_GiaoVien` as an optional forward domain, including its explicit `MaLichLV` identity-insert/collision/sequence strategy.
- [ ] `H03` — Approve or exclude `XeTap`.
- [ ] `H04` — Approve or exclude `KhoaHoc_XeTap`, resolve target non-null `MaGV`, and choose the `MaLichSD` identity-key strategy.
- [ ] `H05` — Approve or exclude `DM_LuuLuongDaoTao`.
- [ ] `H06` — Approve or exclude `LichHoc` and choose the `MaLichHoc` identity-key strategy.
- [ ] `H07` — Approve or exclude `PhongHoc` and define its stream partition key.
- [ ] `H08` — Approve the proposed conditional merge for `NguoiLX_HoSo.GhiChu`.
- [ ] `H09` — Approve the proposed conditional merge for `NguoiLX_HoSo.GiayCNSK`.
- [ ] `H10` — Approve the proposed conditional merge for `NguoiLX_HoSo.GiaiTrinh`.
- [ ] `H11` — Approve the exact `DM_DonViGTVT.CoQuanQL` constant mapping for routed profiles 66029/66030.
- [ ] `H12` — Approve a safe deactivation mechanism for rows with V1 history; `TrangThai=0` alone is not proven sufficient.
- [ ] `H13` — Approve lookup provisioning: pre-seed/validate versus insert-if-missing.
- [ ] `H14` — Approve CT enablement and the durable stream membership registry.
- [ ] `H15` — Approve the source snapshot mechanism plus cycle journal/target commit-marker protocol. Both SQL Server snapshot options are currently off.

## Owner sign-off

```text
Owner:
Date:
Mapping version/fingerprint:
Approved core tables:
Approved optional tables:
Approved conditional columns:
Approved delete/deactivate mechanism:
Exceptions:
```
