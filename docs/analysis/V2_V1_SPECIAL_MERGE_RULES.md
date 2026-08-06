# V2 → V1 special merge rules

Audit date: `2026-07-25`  
Direction: `V2 → V1` only  
Approval state: every rule is `PENDING`; no checkbox is approved by this document.

## 1. Non-negotiable ownership decisions

For `NguoiLX_HoSo`, the project owner has already fixed these rules:

| Column | Owner | INSERT | UPDATE |
| --- | --- | --- | --- |
| TrangThai | V2 | copy bit 0/1 from V2 | always copy bit 0/1 from V2 |
| MaKhoaHoc | V2 | copy the V2 key after parent validation | always copy from V2; no lifecycle/relation lock |
| MaBC1 | V2 | copy the V2 key after parent validation | always copy from V2; no lifecycle/relation lock |

When only `TT_XuLy` is preserved, these columns and every other V2-owned training column on the same row must still update. A merge decision for one column must never suppress the whole row.

## 2. `NguoiLX_HoSo.TT_XuLy`

### 2.1. State ownership

V2 training/intake states:

```text
01, 02, 03, 04, 05, 06, 07, 09, 10
```

V1 downstream BCII/exam states:

```text
00, 11, 12, 13, 14, 16, 17, 18, 19, 90
```

Known values outside both sets, including `08` and `15`, are not guessed. Null, blank, wrong-width, and any other code are also unknown.

### 2.2. Merge truth table

| Row case | V2 state | Current V1 state | TT_XuLy result | Other V2-owned columns | Result code |
| --- | --- | --- | --- | --- | --- |
| INSERT, valid training source | training set | no row | copy V2 | insert from V2 | OK |
| INSERT, source claims downstream state | downstream set | no row | do not insert | do not create an invalid partial dossier | SOURCE_STATE_OUT_OF_SCOPE |
| INSERT, source state unknown | unknown/null/blank | no row | do not insert | do not insert | UNKNOWN_SOURCE_STATE |
| UPDATE before downstream | training set | training set | copy V2 | update from V2 | OK |
| UPDATE after V1 advanced | training set | downstream set | preserve V1 | still update from V2 | TARGET_DOWNSTREAM_STATE_PRESERVED |
| UPDATE, source claims downstream | downstream set | training set | preserve V1 | update other valid V2-owned columns | SOURCE_STATE_OUT_OF_SCOPE |
| UPDATE, both source and target downstream | downstream set | downstream set | preserve V1 | update other valid V2-owned columns | SOURCE_STATE_OUT_OF_SCOPE |
| UPDATE, target state unknown | any | unknown/null/blank | no state write | no checkpoint advance for the row/cycle | UNKNOWN_TARGET_STATE |
| UPDATE, source state unknown | unknown/null/blank | any | no state write | no checkpoint advance for the row/cycle | UNKNOWN_SOURCE_STATE |

Required example:

```text
V2 TT_XuLy = 09
V1 TT_XuLy = 17

Result:
V1 TT_XuLy remains 17.
TrangThai, MaKhoaHoc, MaBC1 and all V2-owned training columns still update from V2.
```

The implementation must compare explicit string-set membership. It must not compare codes numerically or assume that a larger number is a later state.

### 2.3. Evidence

Live V1 procedure definitions prove that the same physical column is advanced by different business stages:

- `usp_BaoCao1_KetQua_update` writes training/BCI states `06` or `07` and records `TT_XuLy_Old`;
- `usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat` preserves `90/13/14`, otherwise writes `09/10`;
- `usp_NguoiLX_HoSo_UpdateKQBC2` writes `13/14`;
- `usp_NguoiLX_HoSo_UpdateKQSH` writes `16/17/18`;
- `usp_CSDT_PheDuyetKQDT_TiepNhan` writes `13/14`;
- `usp_NguoiLX_HoSo_UpdateTTXLy` updates the state and retains the old value.

Live source distribution is 108 rows, all at `03`; live target currently has zero dossier rows. This proves current compatibility only, not the future state machine.

The current code is not the approved contract: it accepts only `03/04/09`, omits downstream `00/90`, and uses the obsolete lifecycle lock. Treat `CsdtRealtimeForwardWritePlanner` as implementation-gap evidence, not normative mapping.

## 3. Other columns where direct copy is unsafe

| Table.Column | Why direct copy is unsafe | V2 meaning | V1 meaning/evidence | Proposed merge rule | Confidence |
| --- | --- | --- | --- | --- | --- |
| NguoiLX_HoSo.GiayCNSK | Both systems write the same bit | training/health-document intake | live `usp_CSDT_PheDuyetKQDT_TiepNhan` updates it during downstream result approval | INSERT from V2; before downstream use V2; after downstream preserve V1 | HIGH |
| NguoiLX_HoSo.GiaiTrinh | V1 can deliberately clear/change it | training explanation | live `usp_CSDT_PheDuyetKQDT_TiepNhan` assigns an empty value during downstream processing | INSERT from V2; preserve V1 after any downstream signal | HIGH |
| NguoiLX_HoSo.GhiChu | Generic note field has writers on both sides | training/dossier note | live `usp_NguoiLX_HoSo_Update` and `Update2` write it; BCII/report modules read dossier notes | INSERT from V2; before downstream use V2; after downstream preserve V1 | HIGH |

These three proposed rules use the same downstream-state partition as `TT_XuLy`, augmented by non-default downstream evidence. They remain owner decisions because the live modules prove multiple writers but do not prove a universal last-writer policy.

### 3.1. Downstream evidence predicate

The predicate is semantic, not simply “column is non-null,” because several V1 columns have non-null defaults. A row is downstream when at least one reliable signal is present:

```text
TT_XuLy in (00,11,12,13,14,16,17,18,19,90)
OR nonblank MaBC2
OR nonblank MaKySH
OR a verified BCII/exam/result/decision relation exists
OR a downstream column differs from its documented default/sentinel
```

Non-null default values such as zero, one, or blank text are not sufficient by themselves. The predicate must use the column-specific default/sentinel table in the JSON matrix and the related row existence where available.

## 4. Reviewed candidates that are not special merges

| Candidate | Decision | Reason |
| --- | --- | --- |
| NguoiLX_HoSo.TrangThai | COPY_WITH_VALIDATION | Explicitly V2-owned; no lifecycle lock |
| NguoiLX_HoSo.MaKhoaHoc | COPY_WITH_VALIDATION | Explicitly V2-owned; parent KhoaHoc must exist first |
| NguoiLX_HoSo.MaBC1 | COPY_WITH_VALIDATION | Explicitly V2-owned; parent BaoCaoI must exist first |
| KhoaHoc.TT_Xuly | COPY_WITH_VALIDATION | No evidence that V1 advances it as a downstream BCII/exam state; KhoaHoc is V2-owned |
| BaoCaoI.TT_Xuly | COPY_WITH_VALIDATION | No evidence of a separate downstream owner; BaoCaoI is V2-owned |
| TrangThai/GhiChu on other V2-owned tables | COPY_FROM_V2 or COPY_WITH_VALIDATION | V1 procedure presence alone does not prove downstream ownership; table-domain ownership remains V2 |
| NguoiLX_HoSo.TT_XuLy_Old | PRESERVE_V1 | V1 audit/history column, not a merge candidate |

No other live mapped-table column has evidence comparable to `TT_XuLy` of two systems advancing the same state machine through different business stages.

## 5. V1-preserved dossier columns

The following columns are not conditional. They are V1-owned and must never be overwritten from V2:

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

`NguoiTao`, `NguoiSua`, `NgayTao`, `NgaySua`, and identity `IDs` use target defaults/audit behavior and are not copied blindly.

## 6. Fail-closed contract

- A source column not present in the approved matrix is `UNKNOWN` and blocks the table.
- An unknown state blocks the row/cycle; it never falls back to direct copy.
- A source downstream state never gains write authority merely because the target is empty.
- A failed conditional merge may not suppress unrelated V2-owned column updates unless the whole row is invalid for INSERT.
- No conditional decision advances a checkpoint until its conflict/diagnostic record and business write have committed consistently.
- Diagnostics contain table, column, reason code, schema/value hashes, and counts only; never raw `MaDK`, identity document numbers, names, or other PII.
