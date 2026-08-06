# Complete V2 → V1 table inventory

Audit date: `2026-07-25`  
Direction: `CSDL_OTO (V2) → CSDL_OTO_V1 (V1)`  
Approval state: `PENDING`  
Evidence policy: live metadata and aggregate-only profiles; no PII and no SQL write.

## 1. Executive result

- Both live databases contain 50 user tables, all under `dbo`.
- `COMMON_SAME_NAME=50`, `V2_ONLY=0`, `V1_ONLY=0`, `POSSIBLE_RENAMED_MAPPING=0`.
- The complete forward candidate set is 13 tables:
  - 6 `CORE_REQUIRED`;
  - 2 `OPTIONAL_NON_BLOCKING`;
  - 5 `DISABLED_PENDING_MAPPING`.
- The other 37 tables are lookup, V1-owned, or runtime tables and are excluded from forward writes.
- The current runtime catalog contains 9 tables, but `NguoiLX_GPLX` is V1-owned and must be removed from the effective forward set. The runtime therefore has only 8 legitimate forward candidates before the five pending domains are considered.
- Live OTO and MOTO schemas have identical table/column/key/FK/index fingerprints. Likewise, OTO V1 and MOTO V1 are identical:

```text
MOTO_REUSES_OTO_MAPPING = true
```

This statement is about schema reuse. Row counts and stream checkpoints remain profile-specific.

## 2. Schema fingerprints

The fingerprint input includes schema/table names, ordinal and column properties, type/length/precision/scale/nullability, identity/computed/default, PK/UQ, FK actions, indexes, included columns, order, and filters.

| Dimension | CSDL_OTO / CSDL_MOTO | CSDL_OTO_V1 / CSDL_MOTO_V1 |
| --- | --- | --- |
| Tables | `4B4B0019146440CFEE244C3DA8D9D53E2E90103F305BC8569123FF26563D3690` | same |
| Columns | `2310571E63B6D1256296B9B05003187A622EA927A38E7A22E4FBDA68DFB4EF7B` | `0859D355801081C9770DF64C044C2CE10506D892FE61808D922A94C3FEAA7AFC` |
| Keys | `C32897ADC1089AB1C8DE38EBE5308999466CBEEB54B28E4E77BEC5C3C9177FC8` | same |
| Foreign keys | `ADF6025A02630066EE949B619540F612729CC0D54211AF07DA3E8FE4E174E169` | `3B2E90B9DB395DD3861D7E76042248A42A31456F1F4BA696354B22EEBF49433A` |
| Indexes | `1F06F4B00025F8D557605CDC15C67040B8DBA20517C5FC8AC16E32023681F0FF` | same |

Database object totals:

| Database | Tables | Columns | PK | UQ | FK | Indexes | Views | Procedures | Triggers |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CSDL_OTO | 50 | 755 | 48 | 6 | 63 | 55 | 0 | 470 | 1 |
| CSDL_OTO_V1 | 50 | 749 | 48 | 6 | 65 | 55 | 0 | 464 | 1 |
| CSDL_MOTO | 50 | 755 | 48 | 6 | 63 | 55 | 0 | 470 | 1 |
| CSDL_MOTO_V1 | 50 | 749 | 48 | 6 | 65 | 55 | 0 | 464 | 1 |

The only live trigger is on `QTHT_NguoiDung`, outside the forward business scope; its definition hash is the same in all compared schemas.

Change Tracking, CDC, `ALLOW_SNAPSHOT_ISOLATION`, and `READ_COMMITTED_SNAPSHOT` are currently off on all four databases. This does not affect the read-only inventory, but it blocks the realtime delete/same-cycle consistency contract until an approved mechanism is enabled.

## 3. Complete live table inventory

Row counts are metadata aggregates from `sys.partitions`, not business-row exports.

| V2 table | V1 table | Classification | Role | Rows V2 | Rows V1 | Proposed sync scope | Evidence |
| --- | --- | --- | --- | ---: | ---: | --- | --- |
| BaoCaoI | BaoCaoI | COMMON_SAME_NAME | V2_DOCUMENT | 0 | 0 | CORE_REQUIRED | Owner lock; parent of dossier, BCII soft relation |
| BaoCaoII | BaoCaoII | COMMON_SAME_NAME | V1_BCII | 0 | 0 | V1_OWNED_EXCLUDED | V1 creates/manages BCII |
| DM_DiemSatHach | DM_DiemSatHach | COMMON_SAME_NAME | V1_EXAM | 36 | 36 | V1_OWNED_EXCLUDED | Exam lookup |
| DM_DonViGTVT | DM_DonViGTVT | COMMON_SAME_NAME | V2_MASTER | 38 | 1,579 | CORE_REQUIRED | Scope is one CSDT route, not the full national target table |
| DM_DVHC | DM_DVHC | COMMON_SAME_NAME | LOOKUP | 15,755 | 15,755 | V1_OWNED_EXCLUDED | Shared lookup; validate only |
| DM_GiayTo | DM_GiayTo | COMMON_SAME_NAME | LOOKUP | 25 | 25 | V1_OWNED_EXCLUDED | Shared lookup; validate only |
| DM_HangDT | DM_HangDT | COMMON_SAME_NAME | LOOKUP | 57 | 54 | V1_OWNED_EXCLUDED | Divergent lookup; no blind overwrite |
| DM_HangDT_MonHoc | DM_HangDT_MonHoc | COMMON_SAME_NAME | LOOKUP | 308 | 302 | V1_OWNED_EXCLUDED | Divergent curriculum lookup |
| DM_HangGPLX | DM_HangGPLX | COMMON_SAME_NAME | LOOKUP | 26 | 26 | V1_OWNED_EXCLUDED | Shared lookup; validate only |
| DM_HinhThucDT | DM_HinhThucDT | COMMON_SAME_NAME | LOOKUP | 2 | 2 | V1_OWNED_EXCLUDED | Shared lookup |
| DM_HTCapGPLX | DM_HTCapGPLX | COMMON_SAME_NAME | LOOKUP | 9 | 9 | V1_OWNED_EXCLUDED | GPLX lookup |
| DM_LoaiDV | DM_LoaiDV | COMMON_SAME_NAME | LOOKUP | 14 | 14 | V1_OWNED_EXCLUDED | Shared lookup |
| DM_LoaiHSo | DM_LoaiHSo | COMMON_SAME_NAME | LOOKUP | 3 | 3 | V1_OWNED_EXCLUDED | Shared lookup |
| DM_LoaiHSo_GiayTo | DM_LoaiHSo_GiayTo | COMMON_SAME_NAME | LOOKUP | 1,101 | 1,101 | V1_OWNED_EXCLUDED | Shared lookup relation |
| DM_LuuLuongDaoTao | DM_LuuLuongDaoTao | COMMON_SAME_NAME | V2_MASTER | 0 | 0 | DISABLED_PENDING_MAPPING | Training candidate; zero data and no approved ownership contract |
| DM_LyDoTCBC2 | DM_LyDoTCBC2 | COMMON_SAME_NAME | V1_EXAM | 14 | 7 | V1_OWNED_EXCLUDED | V1 BCII refusal lookup |
| DM_MonHoc | DM_MonHoc | COMMON_SAME_NAME | LOOKUP | 9 | 9 | V1_OWNED_EXCLUDED | Shared lookup |
| DM_NoiDungSH | DM_NoiDungSH | COMMON_SAME_NAME | V1_EXAM | 11 | 17 | V1_OWNED_EXCLUDED | Divergent exam-content lookup |
| DM_QuocTich | DM_QuocTich | COMMON_SAME_NAME | LOOKUP | 249 | 249 | V1_OWNED_EXCLUDED | Shared lookup; validate only |
| DM_TrangThai | DM_TrangThai | COMMON_SAME_NAME | LOOKUP | 19 | 19 | V1_OWNED_EXCLUDED | State-code lookup; validate only |
| GiaoVien | GiaoVien | COMMON_SAME_NAME | V2_MASTER | 0 | 0 | OPTIONAL_NON_BLOCKING | Runtime marks optional; schema transforms unresolved |
| KhoaHoc | KhoaHoc | COMMON_SAME_NAME | V2_TRANSACTION | 3 | 0 | CORE_REQUIRED | Owner lock; parent of BaoCaoI/dossier |
| KhoaHoc_GiaoVien | KhoaHoc_GiaoVien | COMMON_SAME_NAME | V2_RELATION | 0 | 0 | OPTIONAL_NON_BLOCKING | Runtime marks optional; not required by core BCII SQL |
| KhoaHoc_XeTap | KhoaHoc_XeTap | COMMON_SAME_NAME | V2_RELATION | 0 | 0 | DISABLED_PENDING_MAPPING | V1 requires non-null MaGV and adds FK |
| KySH | KySH | COMMON_SAME_NAME | V1_EXAM | 0 | 0 | V1_OWNED_EXCLUDED | V1 exam session |
| LichHoc | LichHoc | COMMON_SAME_NAME | V2_TRANSACTION | 0 | 0 | DISABLED_PENDING_MAPPING | Training candidate; not in catalog |
| NguoiLX | NguoiLX | COMMON_SAME_NAME | V2_MASTER | 108 | 0 | CORE_REQUIRED | Owner lock; dossier parent |
| NGUOILX_BK_UPDATE_DVHC | NGUOILX_BK_UPDATE_DVHC | COMMON_SAME_NAME | RUNTIME | 0 | 0 | V1_OWNED_EXCLUDED | Backup/runtime table |
| NguoiLX_GPLX | NguoiLX_GPLX | COMMON_SAME_NAME | V1_GPLX | 0 | 0 | V1_OWNED_EXCLUDED | V1 creates/manages GPLX; no forward write |
| NguoiLX_HoSo | NguoiLX_HoSo | COMMON_SAME_NAME | SHARED_PHYSICAL_TABLE | 108 | 0 | CORE_REQUIRED | V2 training data plus V1 BCII/exam columns |
| NguoiLXHS_GiayTo | NguoiLXHS_GiayTo | COMMON_SAME_NAME | V2_DOCUMENT | 324 | 0 | CORE_REQUIRED | Child of dossier |
| PhongHoc | PhongHoc | COMMON_SAME_NAME | V2_MASTER | 0 | 0 | DISABLED_PENDING_MAPPING | No MaCSDT or FK-based partition key |
| QTHT_ChucNang | QTHT_ChucNang | COMMON_SAME_NAME | RUNTIME | 64 | 64 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_NguoiDung | QTHT_NguoiDung | COMMON_SAME_NAME | RUNTIME | 3 | 3 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_NhatKyLoi | QTHT_NhatKyLoi | COMMON_SAME_NAME | RUNTIME | 33,667 | 33,353 | V1_OWNED_EXCLUDED | Runtime error log |
| QTHT_NhomCN | QTHT_NhomCN | COMMON_SAME_NAME | RUNTIME | 75 | 75 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_NhomNSD | QTHT_NhomNSD | COMMON_SAME_NAME | RUNTIME | 19 | 19 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_NSD_NhomNSD | QTHT_NSD_NhomNSD | COMMON_SAME_NAME | RUNTIME | 0 | 0 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_NSD_QUYEN | QTHT_NSD_QUYEN | COMMON_SAME_NAME | RUNTIME | 0 | 0 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_NSD_Quyen_CN | QTHT_NSD_Quyen_CN | COMMON_SAME_NAME | RUNTIME | 85 | 85 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_Quyen | QTHT_Quyen | COMMON_SAME_NAME | RUNTIME | 5 | 5 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_Quyen_CN | QTHT_Quyen_CN | COMMON_SAME_NAME | RUNTIME | 0 | 0 | V1_OWNED_EXCLUDED | Authorization/runtime |
| QTHT_ThamSoHT | QTHT_ThamSoHT | COMMON_SAME_NAME | RUNTIME | 22 | 22 | V1_OWNED_EXCLUDED | Application parameters |
| STT | STT | COMMON_SAME_NAME | RUNTIME | 1,000 | 0 | V1_OWNED_EXCLUDED | Sequence helper, no business PK |
| TRANS_CLI_HangDoiGui | TRANS_CLI_HangDoiGui | COMMON_SAME_NAME | RUNTIME | 467 | 443 | V1_OWNED_EXCLUDED | Transport queue |
| TRANS_CLI_HangDoiNhan | TRANS_CLI_HangDoiNhan | COMMON_SAME_NAME | RUNTIME | 378 | 378 | V1_OWNED_EXCLUDED | Transport queue |
| TRANS_CLI_NhatKyGui | TRANS_CLI_NhatKyGui | COMMON_SAME_NAME | RUNTIME | 0 | 0 | V1_OWNED_EXCLUDED | Transport log |
| TRANS_CLI_NhatKyNhan | TRANS_CLI_NhatKyNhan | COMMON_SAME_NAME | RUNTIME | 0 | 0 | V1_OWNED_EXCLUDED | Transport log |
| TRANS_LoaiDuLieu | TRANS_LoaiDuLieu | COMMON_SAME_NAME | RUNTIME | 16 | 16 | V1_OWNED_EXCLUDED | Transport lookup |
| XeTap | XeTap | COMMON_SAME_NAME | V2_MASTER | 0 | 0 | DISABLED_PENDING_MAPPING | Training resource candidate; not in catalog |

## 4. Authoritative live schema drift

| Table.Column | V2 | V1 | Required disposition |
| --- | --- | --- | --- |
| DM_DonViGTVT.TenDV | nvarchar(1000) | nvarchar(100) | COPY_WITH_VALIDATION; observed max 52, overflow 0 |
| DM_DonViGTVT.CoQuanQL | nvarchar(1000) | nvarchar(100) | TRANSFORM: for routed profiles 66029/66030 write constant `Sở Xây dựng tỉnh Đắk Lắk`; observed max 47, overflow 0; business authority remains PENDING |
| GiaoVien.HinhThuc_TuyenDung | nvarchar(50) | varchar(2) | TRANSFORM; owner-approved enum mapping required (PENDING) |
| GiaoVien.HangGPLX | nvarchar(50) NULL | varchar(3) NOT NULL | TRANSFORM; owner-approved code/non-null/target-lookup mapping required (PENDING) |
| GiaoVien.LoaiHinh_DaoTao | nvarchar(500) | varchar(2) | TRANSFORM; owner-approved enum mapping required (PENDING) |
| GiaoVien.GhiChu | nvarchar(500) | nvarchar(255) | COPY_WITH_VALIDATION; no truncation |
| GiaoVien.NgayHHGPLX | present | absent | V2_ONLY_SKIP |
| GiaoVien.NoiCapGCN | present | absent | V2_ONLY_SKIP |
| GiaoVien.CacMonHoc | present | absent | V2_ONLY_SKIP |
| GiaoVien.LoaiGiaoVien | present | absent | V2_ONLY_SKIP |
| GiaoVien.CacHangDaCo | present | absent | V2_ONLY_SKIP |
| KhoaHoc_XeTap.MaGV | varchar(8) NULL | varchar(8) NOT NULL | BLOCK_TABLE until non-null/FK rule is approved |
| NguoiLX_HoSo.QDThucHanhHinh | present | absent | V2_ONLY_SKIP |

V1 also has two extra FKs:

- `GiaoVien.HangGPLX → DM_HangGPLX.MaHang`;
- `KhoaHoc_XeTap.MaGV → GiaoVien.MaGV`.

The checked-in reference scripts are stale relative to live metadata: they show historical `DossierNo`, `DM_HinhThucDT`, and `KhoaHoc_XeTap` differences that do not exist in the live table-name inventory. They are supporting evidence only, not the source of truth.

## 5. Row key and match contract

For every approved exact-key mapping below, the source key is inserted exactly as supplied by V2 and is immutable afterward. A row marked `BLOCK` for identity strategy is not enabled and writes no key until the owner approves exact preservation or a proven alternate-key map. The writer must not call legacy generators for `MaDK`, `MaKH`, `MaKhoaHoc`, `MaBC1`, `SoGiayCNTN`, or related IDs.

| Table | Source key | Target key | Scope key | Match rule | Insert key rule | Collision evidence |
| --- | --- | --- | --- | --- | --- | --- |
| DM_DonViGTVT | MaDV | MaDV | MaDV=@MaCSDT | exact binary-normalized key | preserve V2 | 36/38 source keys currently exist in the national target table; route and guard validation mandatory |
| GiaoVien | MaGV | MaGV | MaCSDT | exact key + guard MaCSDT/MaSoGTVT | preserve V2 | source/target both 0 |
| KhoaHoc | MaKH | MaKH | MaCSDT | exact key + guard MaCSDT/MaSoGTVT | preserve V2 | 3/0 rows; duplicate source key 0 |
| KhoaHoc_GiaoVien | MaLichLV | MaLichLV | parent KhoaHoc | exact key + guard MaKH/MaGV | preserve exact V2 identity using serialized `IDENTITY_INSERT`; reject key collisions; always restore `IDENTITY_INSERT OFF`; verify next generated identity remains above target max | source/target both 0 |
| BaoCaoI | MaBCI | MaBCI | MaCSDT | exact key + guard SoBaoCao/MaKH/MaCSDT | preserve V2 | source/target both 0 |
| NguoiLX | MaDK | MaDK | DonViNhanHSo plus durable stream membership | exact binary-normalized key | preserve V2 | 108/0 rows; duplicate source key 0 |
| NguoiLX_HoSo | MaDK | MaDK | MaCSDT plus durable stream membership | exact key; one dossier per MaDK | preserve V2 | 108/0 rows; duplicate source key 0 |
| NguoiLXHS_GiayTo | MaGT + MaDK | MaGT + MaDK | parent dossier | exact composite key | preserve both V2 key parts | 324/0 rows; duplicate composite key 0 |
| XeTap | BienSoXe | BienSoXe | MaCSDT | exact key + guard MaCSDT/MaSoGTVT | preserve V2 | source/target both 0 |
| KhoaHoc_XeTap | MaLichSD | MaLichSD | parent KhoaHoc | exact key + guard MaKH/BienSoXe/MaGV | target identity: BLOCK until owner chooses exact-source-key `IDENTITY_INSERT` with collision/sequence guards or proves an alternate-key map; never regenerate silently | source/target both 0 |
| DM_LuuLuongDaoTao | MaCSDT + HangGPLX | same | MaCSDT | exact composite key | preserve V2 | source/target both 0 |
| LichHoc | MaLichHoc | MaLichHoc | parent KhoaHoc | exact key + guard MaKH | target identity: BLOCK until owner chooses exact-source-key `IDENTITY_INSERT` with collision/sequence guards or proves an alternate-key map; never regenerate silently | source/target both 0 |
| PhongHoc | MaPH | MaPH | UNKNOWN | blocked until a partition rule exists | preserve V2 if approved | source/target both 0 |

All 36 source FK checks and all 37 target FK checks for the candidate set currently report zero aggregate orphans. Two `NguoiLX → DM_DVHC` FKs are marked not trusted, so a zero current orphan count does not remove the requirement for explicit validation.

## 6. Aggregate profile highlights

- `NguoiLX_HoSo`: 108 rows; `TT_XuLy='03'` for all 108; `TrangThai=1` for all 108.
- `KhoaHoc`: 3 rows; `TrangThai=1` for all 3; `TT_Xuly` is null for all 3.
- `NguoiLX_HoSo.GiayCNSK`: null count 0.
- `NguoiLX_HoSo.GiaiTrinh`: null count 108.
- `NguoiLX_HoSo.GhiChu`: null count 0, blank count 108, max observed length 0.
- `NguoiLX_HoSo.MaKhoaHoc`: null count 0; parent orphan count 0.
- `NguoiLX_HoSo.MaBC1`: null count 108; non-null values are never exported.
- Target learner/course/BCI tables are currently empty, so current collision risk is low but future collision guards remain mandatory.

Per-column null/blank/distinct/max-length/min/max policy, safe enum sets, defaults, ownership, disposition, and source/target profiles are in `V2_V1_COMPLETE_COLUMN_MAPPING_MATRIX.json`. Min/max values for PII and free text are deliberately suppressed.

## 7. Proposed scope

### CORE_REQUIRED

```text
DM_DonViGTVT
KhoaHoc
BaoCaoI
NguoiLX
NguoiLX_HoSo
NguoiLXHS_GiayTo
```

This is the minimum set needed for V1 to continue forming Báo cáo II, assuming required lookup keys already exist and every FK/value validation succeeds.

### OPTIONAL_NON_BLOCKING

```text
GiaoVien
KhoaHoc_GiaoVien
```

They must not stop the core cycle when optional-domain policy says skip/retry. `GiaoVien` remains write-disabled until its transforms are approved.

### DISABLED_PENDING_MAPPING

```text
XeTap
KhoaHoc_XeTap
DM_LuuLuongDaoTao
LichHoc
PhongHoc
```

Zero current rows are evidence about current data only, not permission to omit these domains permanently.

### V1_OWNED_EXCLUDED

This group contains the 37 remaining V1-owned, lookup, authorization, transport, audit, and runtime tables. Forward sync may read their keys for validation but may not insert, update, or delete their rows.
