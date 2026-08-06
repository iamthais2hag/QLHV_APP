# V1 new-business SQL consumer matrix

Audit date: `2026-07-26`  
Database: `CSDL_OTO_V1`  
Direction under review: `CSDL_OTO (V2) → CSDL_OTO_V1 (V1)`  
H12 status: `PENDING OWNER APPROVAL`  
Audit mode: metadata/module/application-source read-only; no PII was selected.

## 1. Result

```text
NO_UNIVERSAL_EXISTING_PREDICATE
H12 EXCLUSION REGISTRY REQUIRED
```

The database-wide search reviewed all `492` readable SQL module definitions. The relevant universe is exactly `182` unique modules:

| Measure | Count |
| --- | ---: |
| Core-table consumers | 161 |
| `BaoCaoII` / `KySH` / `NguoiLX_GPLX` consumers | 33 |
| Overlap | 12 |
| Unique relevant modules | 182 |
| Stored procedures | 171 |
| Scalar functions | 11 |
| Encrypted/unreadable definitions | 0 |
| Relevant views / TVFs / triggers / synonyms | 0 / 0 / 0 / 0 |
| Executable dynamic-SQL modules after comment stripping | 0 |
| Relevant SQL Agent job steps | 0 |

`180` objects were resolved by `sys.sql_expression_dependencies`. Two completeness additions are included:

- `dbo.usp_NguoiLX_SelectTen`: real malformed text reference `[dbo].dbo.NguoiLX`, missed by dependency resolution.
- `dbo.usp_NguoiLX_HoSo_SoHoSoOut`: indirect caller of `dbo.CreateNewSoHoso`.

`dbo.CreateNewMaDonViGTVT` is not in the roster because its only core-table occurrence is commented-out text.

The executable H12 guard surface is `75` paths:

```text
63 transactional NEW_BUSINESS branches
+ 12 ADMIN_MAINTENANCE unit/course resurrection hazards
= 75 guard-required paths
```

No path in either set currently tests active source membership: transactional coverage is `0/63`, administrative-hazard coverage is `0/12`, and combined H12 guard coverage is `0/75`.

## 2. Matrix legend

### Tables

| Code | Table |
| --- | --- |
| `DV` | `DM_DonViGTVT` |
| `KH` | `KhoaHoc` |
| `B1` | `BaoCaoI` |
| `NL` | `NguoiLX` |
| `HS` | `NguoiLX_HoSo` |
| `GT` | `NguoiLXHS_GiayTo` |
| `B2` | `BaoCaoII` |
| `KSH` | `KySH` |
| `G` | `NguoiLX_GPLX` |

`W:` means the object writes/touches that table; otherwise the entry is a read dependency. The list is deliberately limited to the nine audited tables.

### Classification

| Code | Classification |
| --- | --- |
| `NB` | `NEW_BUSINESS` |
| `HR` | `HISTORY_READ` |
| `MX` | `MIXED`; branches are split in section 4 |
| `AM` | `ADMIN_MAINTENANCE` |
| `RT` | `RUNTIME_ONLY` |
| `UK` | `UNKNOWN` |

### Predicate and boolean fields

| Code | Meaning |
| --- | --- |
| `K` | Key/search predicate only |
| `S` | Scope/date/stage predicate; not source-deletion evidence |
| `A` | Exact audited core-row `TrangThai = 1` gate |
| `AP` | `TrangThai` is parameterized, bypassable, or belongs to another entity |
| `X` | `TT_XuLy` eligibility gate only |
| `AX` | Exact core-row `TrangThai = 1` plus `TT_XuLy` gate |
| `R-` | Relation-absence test used to select/create when history is absent; not deletion evidence |
| `R+` | Positive downstream relation/stage test; not deletion evidence |
| `M` | Branch-specific; see section 4 |
| `Y/N/B` | Yes / No / branch-specific or wrong entity |

### Called-by and evidence

- `EXT0`: no SQL-module caller found; a legacy/external caller remains possible, while the production repository has zero business stored-procedure calls.
- `C1`: `usp_KhoaHoc_Insert`, `usp_KhoaHoc_InsertEx`.
- `C2`: `usp_NguoiLX_HoSo_Insert`, `Insert2`, `SoHoSoOut`, `Update2`.
- `C3`: `usp_NguoiLX_Hoso_SelectForSync`, `SelectForSyncOption`.
- `C4`: `usp_BaoCao1_A1_ViewRPT`, `usp_BaoCao2_ViewRPT`.
- `C5`: `usp_NguoiLX_HoSo_Insert2`, `usp_QuangLA_PM4_ThuNhanHS_ThemMoiCapNhat`.
- `C6`: `usp_QuangLA_PM4_ThuNhanHS_LoadThongTin`, `usp_rpt_PM4_InToKhai`.
- `D`: dependency metadata plus executable module definition.
- `T`: exact executable text reference.
- `C`: module call graph.
- `N01`–`N63`: exact transactional new-business branch evidence in section 5.
- `R01`–`R12`: master/control-plane resurrection hazards outside the new-business denominator, also in section 5.
- `H01`–`H05`: exact history counterexample in section 6.

## 3. Complete 182-object consumer roster

Every object appears exactly once in this roster. `MX` objects are decomposed in section 4.

| # | Object | Object type | Called by | Tables read | Purpose | Classification | Current active predicate | Uses TrangThai | Uses TT_XuLy | Reads V1 history | Must exclude V2-deleted row | Evidence |
| ---: | --- | --- | --- | --- | --- | --- | --- | :---: | :---: | :---: | :---: | --- |
| 001 | `dbo.CreateNewMaKhoaHoc` | FN | C1 | KH | Course-key generator | RT | K | N | N | N | N | D |
| 002 | `dbo.CreateNewMaKySH` | FN | EXT0 | KSH | Exam-key generator | RT | K | N | N | N | N | D |
| 003 | `dbo.CreateNewSoHoso` | FN | C2 | HS | Dossier-number generator | RT | K | N | N | N | N | D |
| 004 | `dbo.CreateNewSoHoso2` | FN | EXT0 | HS | Alternate dossier-number generator | RT | K | N | N | N | N | D |
| 005 | `dbo.GetMaGiaytoByHosoForSync` | FN | C3 | GT | Sync document helper | RT | K | N | N | N | N | D |
| 006 | `dbo.usf_BaBT_GetMaNoidungSathach` | FN | EXT0 | HS | Exam-content code helper | RT | K | N | N | B | N | D |
| 007 | `dbo.usf_BaBT_GetNoidungSathach` | FN | C4 | KH, HS | Exam-content display helper | HR | R+ | N | N | Y | N | D |
| 008 | `dbo.usf_GetMaHTCapByLoaiHso` | FN | C5 | NL | Issuance-form helper | RT | K | N | N | N | N | D |
| 009 | `dbo.usf_GetTenNoiCapCCN` | FN | EXT0 | DV | Historical issuer-name helper | HR | K | N | N | Y | N | D |
| 010 | `dbo.usf_QuangLA_PM4_ThuNhanHS_getTenDVCap` | FN | C6 | DV | Historical issuer-name helper | HR | K | N | N | Y | N | D |
| 011 | `dbo.usf_QuangLA_PM5_getTenDVCap` | FN | EXT0 | DV | Decision issuer-name helper | HR | K | N | N | Y | N | D |
| 012 | `dbo.LichSD_KhoaHoc_XeTap_Insert` | SP | EXT0 | NL | Schedule maintenance | AM | K | Y | N | N | N | D |
| 013 | `dbo.usp_BaBT_DM_DonviGTVT_InsertOrUpdate` | SP | EXT0 | W:DV | Unit upsert | AM | K | Y | N | N | B | D; R01–R02 |
| 014 | `dbo.usp_BaoCao_MonHoc_Select` | SP | EXT0 | KH | Report subject lookup | AM | K | N | N | N | N | D |
| 015 | `dbo.usp_BaoCao_TieuDe` | SP | EXT0 | DV, KH, HS | Existing-report heading | HR | K | B | N | Y | N | D |
| 016 | `dbo.usp_BaoCao1_A1_ViewRPT` | SP | EXT0 | KH, NL, HS | Existing BCI report | HR | AX | Y | Y | Y | N | D |
| 017 | `dbo.usp_BaoCao1_DangKySH` | SP | EXT0 | DV, KH, HS | Exam-registration heading | AM | K | N | N | B | N | D |
| 018 | `dbo.usp_BaoCao1_DSHS` | SP | EXT0 | KH, NL, HS | BCI/BCII candidate and export branches | MX | M | B | B | B | B | D; N12–N14 |
| 019 | `dbo.usp_BaoCao1_KetQua_update` | SP | EXT0 | W:HS | Record a new BCI result | NB | K; TT read/assignment is not a gate | N | Y | Y | Y | D; N37 |
| 020 | `dbo.usp_BaoCao1_ViewRPT` | SP | EXT0 | NL, HS | Existing BCI report | HR | AX | Y | Y | Y | N | D; H01 |
| 021 | `dbo.usp_BaoCao2_DSHS` | SP | EXT0 | KH, NL, HS | Existing BCII learner list | HR | R+ plus X | N | Y | Y | N | D |
| 022 | `dbo.usp_BaoCao2_ViewRPT` | SP | EXT0 | NL, HS | Existing BCII report | HR | R+ plus X | N | Y | Y | N | D |
| 023 | `dbo.usp_BaoCaoI_Delete` | SP | EXT0 | W:B1 | BCI delete maintenance | AM | K | N | N | Y | N | D |
| 024 | `dbo.usp_BaoCaoI_Insert` | SP | EXT0 | W:B1 | Create BCI | NB | K | Y | N | N | Y | D; N32 |
| 025 | `dbo.usp_BaoCaoI_Search` | SP | EXT0 | B1 | Existing BCI search / BCII source selection | MX | M | B | N | B | B | D; N09 |
| 026 | `dbo.usp_BaoCaoI_Select` | SP | EXT0 | B1 | Read existing BCI | HR | K | N | N | Y | N | D |
| 027 | `dbo.usp_BaoCaoI_SelectAll` | SP | EXT0 | B1 | Read existing BCI set | HR | — | N | N | Y | N | D |
| 028 | `dbo.usp_BaoCaoI_Update` | SP | EXT0 | W:B1 | BCI workflow update or correction | MX | M | B | N | Y | B | D; N38 |
| 029 | `dbo.usp_BaoCaoII_Delete` | SP | EXT0 | W:B2 | BCII maintenance | AM | K | N | N | Y | N | D |
| 030 | `dbo.usp_BaoCaoII_Insert` | SP | EXT0 | W:B2 | Create BCII | NB | K | Y | N | N | Y | D; N33 |
| 031 | `dbo.usp_BaoCaoII_Search` | SP | EXT0 | B2 | Search existing BCII | HR | K | N | N | Y | N | D |
| 032 | `dbo.usp_BaoCaoII_Search_KQ` | SP | EXT0 | B2 | Search BCII approval/result | HR | K | Y | Y | Y | N | D |
| 033 | `dbo.usp_BaoCaoII_SearchByBC1` | SP | EXT0 | B2 | Read BCII by BCI soft relation | HR | R+ | N | N | Y | N | D |
| 034 | `dbo.usp_BaoCaoII_Select` | SP | EXT0 | B2 | Read existing BCII | HR | K | Y | N | Y | N | D |
| 035 | `dbo.usp_BaoCaoII_SelectAll` | SP | EXT0 | B2 | Read existing BCII set | HR | — | Y | N | Y | N | D |
| 036 | `dbo.usp_BaoCaoII_Update` | SP | EXT0 | W:B2 | BCII workflow update or correction | MX | M | B | N | Y | B | D; N39 |
| 037 | `dbo.usp_BaoCaoII_Update_PheDuyetKQDT` | SP | EXT0 | W:B2 | Approve new BCII training result | NB | K; assigned B2 status is not a source gate | Y | N | Y | Y | D; N40 |
| 038 | `dbo.usp_CheckWarningGiaoVien` | SP | EXT0 | KH | Course-resource warning | AM | S | AP | N | N | N | D |
| 039 | `dbo.usp_CheckWarningLichLSDXeTap` | SP | EXT0 | KH | Course schedule warning | AM | S | AP | N | N | N | D |
| 040 | `dbo.usp_CheckWarningLichLVGiaoVien` | SP | EXT0 | KH | Course schedule warning | AM | S | AP | N | N | N | D |
| 041 | `dbo.usp_CheckWarningXeTap` | SP | EXT0 | KH | Course-resource warning | AM | S | AP | N | N | N | D |
| 042 | `dbo.usp_CSDT_PheDuyetKQDT_TiepNhan` | SP | EXT0 | W:NL, W:HS | Receive new training-result approval | NB | K | N | Y | Y | Y | D; N29 |
| 043 | `dbo.usp_CSDT_ThongTinChung_Select` | SP | EXT0 | DV | Unit information maintenance | AM | K | N | N | N | N | D |
| 044 | `dbo.usp_DM_DonViGTVT_ByLoaiDV` | SP | EXT0 | DV | Unit lookup | AM | AP | Y | N | N | N | D |
| 045 | `dbo.usp_DM_DonViGTVT_Delete` | SP | EXT0 | W:DV | Unit delete maintenance | AM | K | N | N | N | N | D |
| 046 | `dbo.usp_DM_DonViGTVT_Get_By_Id_Parent` | SP | EXT0 | DV | Unit hierarchy lookup | AM | K | N | N | N | N | D |
| 047 | `dbo.usp_DM_DonViGTVT_Insert` | SP | EXT0 | W:DV | Unit insert | AM | K | Y | N | N | B | D; R03 |
| 048 | `dbo.usp_DM_DonViGTVT_Insert2` | SP | EXT0 | W:DV | Unit insert variant | AM | K | Y | N | N | B | D; R04 |
| 049 | `dbo.usp_DM_DonViGTVT_Like_Id` | SP | EXT0 | DV | Unit lookup | AM | K | N | N | N | N | D |
| 050 | `dbo.usp_DM_DonViGTVT_Search` | SP | EXT0 | DV | Active unit search | AM | A | Y | N | N | N | D |
| 051 | `dbo.usp_DM_DonViGTVT_Search2` | SP | EXT0 | DV | Parameterized unit search | AM | AP | Y | N | N | N | D |
| 052 | `dbo.usp_DM_DonViGTVT_SearchLoaiDV` | SP | EXT0 | DV | Active unit search by type | AM | A | Y | N | N | N | D |
| 053 | `dbo.usp_DM_DonViGTVT_Select` | SP | EXT0 | DV | Unit detail | AM | K | Y | N | N | N | D |
| 054 | `dbo.usp_DM_DonViGTVT_Select_ByLoaiDT` | SP | EXT0 | DV | Active training-unit detail | AM | A | Y | N | N | N | D |
| 055 | `dbo.usp_DM_DonViGTVT_Select_TenMaDonVi_by_LoaiDV` | SP | EXT0 | DV | Active unit-name lookup | AM | A | Y | N | N | N | D |
| 056 | `dbo.usp_DM_DonViGTVT_SelectAll` | SP | EXT0 | DV | Active unit list | AM | A | Y | N | N | N | D |
| 057 | `dbo.usp_DM_DonViGTVT_SelectAll_DT_SH` | SP | EXT0 | DV | Training/test-centre list | AM | S | N | N | N | N | D; includes inactive rows |
| 058 | `dbo.usp_DM_DonViGTVT_SelectAll_SO` | SP | EXT0 | DV | Transport-department list | AM | S | N | N | N | N | D |
| 059 | `dbo.usp_DM_DonViGTVT_SelectAll_VP` | SP | EXT0 | DV | Office list | AM | S | N | N | N | N | D |
| 060 | `dbo.usp_DM_DonViGTVT_SelectAllItems` | SP | EXT0 | DV | Unit list maintenance | AM | AP | Y | N | N | N | D |
| 061 | `dbo.usp_DM_DonViGTVT_SelectByLoai` | SP | EXT0 | DV | Unit list by type | AM | S | N | N | N | N | D |
| 062 | `dbo.usp_DM_DonViGTVT_SelectItem` | SP | EXT0 | DV | Unit item detail | AM | K | Y | N | N | N | D |
| 063 | `dbo.usp_DM_DonViGTVT_Update` | SP | EXT0 | W:DV | Unit update | AM | K | Y | N | N | B | D; R05 |
| 064 | `dbo.usp_DM_DonViGTVT_Update2` | SP | EXT0 | W:DV | Unit update variant | AM | K | Y | N | N | B | D; R06 |
| 065 | `dbo.usp_KhoaHoc_Auto_Search` | SP | EXT0 | KH | General course list / BCI / BCII modes | MX | M | N | N | B | B | D; N01–N02 |
| 066 | `dbo.usp_KhoaHoc_Delete` | SP | EXT0 | W:KH | Course delete maintenance | AM | K | N | N | Y | N | D |
| 067 | `dbo.usp_KhoaHoc_DSHV_TrongKhoaHoc` | SP | EXT0 | NL, HS | Existing learners in course | HR | K | N | N | Y | N | D |
| 068 | `dbo.usp_KhoaHoc_GiaoVien_Paging` | SP | EXT0 | KH | Course-teacher maintenance paging | AM | S plus AP | AP | N | N | N | D |
| 069 | `dbo.usp_KhoaHoc_GiaoVien_SelectKhoaHocMoi` | SP | EXT0 | KH | Select new active course for teacher relation | NB | A plus R- plus future end | Y | N | N | Y | D; N10 |
| 070 | `dbo.usp_KhoaHoc_Insert` | SP | EXT0 | W:KH | Course insert maintenance | AM | K | Y | Y | N | Y | D; R07 |
| 071 | `dbo.usp_KhoaHoc_InsertEx` | SP | EXT0 | W:KH | Course insert maintenance | AM | K | Y | Y | N | Y | D; R08 |
| 072 | `dbo.usp_KhoaHoc_InsertOrUpdateXML` | SP | EXT0 | W:KH | Course XML maintenance | AM | K | Y | Y | N | Y | D; R09–R10 |
| 073 | `dbo.usp_KhoaHoc_Search` | SP | EXT0 | KH, B1 | General / BCI / BCII / result course modes | MX | M | B | N | B | B | D; N03–N05 |
| 074 | `dbo.usp_KhoaHoc_SearchMaKH` | SP | EXT0 | KH, B1 | Select course for a new dossier | NB | R- plus receipt/start date | N | N | N | Y | D; N06 |
| 075 | `dbo.usp_KhoaHoc_Select` | SP | EXT0 | KH, B1 | Historical course/BCI detail | HR | K plus R+ | N | N | Y | N | D |
| 076 | `dbo.usp_KhoaHoc_SelectAll` | SP | EXT0 | KH | Existing course set | HR | — | Y | Y | Y | N | D |
| 077 | `dbo.usp_KhoaHoc_Update` | SP | EXT0 | W:KH | Course update | AM | K | Y | Y | Y | Y | D; R11 |
| 078 | `dbo.usp_KhoaHoc_Update_TrangThai` | SP | EXT0 | W:KH | Course-status maintenance | AM | K | Y | N | Y | Y | D; R12 |
| 079 | `dbo.usp_KySH_Delete` | SP | EXT0 | W:KSH | Exam-session maintenance | AM | K | N | N | Y | N | D |
| 080 | `dbo.usp_KySH_Insert` | SP | EXT0 | W:KSH | Create exam session | NB | K | Y | N | N | Y | D; N31 |
| 081 | `dbo.usp_KySH_SearchMaKySH` | SP | EXT0 | KSH | Select active future exam session | NB | AP on KSH plus future date | Y | N | N | Y | D; N30 |
| 082 | `dbo.usp_KySH_Select` | SP | EXT0 | KSH | Existing exam-session detail | HR | K | Y | N | Y | N | D |
| 083 | `dbo.usp_KySH_SelectAll` | SP | EXT0 | KSH | Existing exam-session set | HR | — | Y | N | Y | N | D |
| 084 | `dbo.usp_KySH_Update` | SP | EXT0 | W:KSH | Exam-session workflow update or correction | MX | M | B | N | Y | B | D; N41 |
| 085 | `dbo.usp_LichGiaoVien_SelectKhoaHocMoi` | SP | EXT0 | KH | Select course for new teacher schedule | NB | AP on relation plus future end | Y | N | N | Y | D; N11 |
| 086 | `dbo.usp_LichLV_KhoaHoc_GiaoVien_Paging` | SP | EXT0 | KH | Teacher-schedule maintenance | AM | AP on relation | Y | N | N | N | D |
| 087 | `dbo.usp_LichSD_KhoaHoc_XeTap_Paging` | SP | EXT0 | KH | Vehicle-schedule maintenance | AM | AP on relation | Y | N | N | N | D |
| 088 | `dbo.usp_LichSD_KhoaHoc_XeTap_Update` | SP | EXT0 | W:NL | Vehicle-schedule maintenance | AM | K | Y | N | N | N | D |
| 089 | `dbo.usp_NguoiLX_Delete` | SP | EXT0 | W:NL | Learner delete maintenance | AM | K | N | N | Y | N | D |
| 090 | `dbo.usp_NguoiLX_Delete2` | SP | EXT0 | W:NL, W:HS, W:GT, G | Legacy deactivate/delete routine | AM | K | Y | N | Y | N | D |
| 091 | `dbo.usp_NguoiLX_GPLX_Delete` | SP | EXT0 | W:G | GPLX maintenance | AM | K | N | N | Y | N | D |
| 092 | `dbo.usp_NguoiLX_GPLX_Insert` | SP | EXT0 | W:G | Create GPLX record | NB | K | Y | N | Y | Y | D; N34 |
| 093 | `dbo.usp_NguoiLX_GPLX_Select` | SP | EXT0 | G | Existing GPLX detail | HR | K | Y | N | Y | N | D |
| 094 | `dbo.usp_NguoiLX_GPLX_SelectAll` | SP | EXT0 | G | Existing GPLX set | HR | — | Y | N | Y | N | D |
| 095 | `dbo.usp_NguoiLX_GPLX_SelectTen` | SP | EXT0 | NL | Historical learner-name lookup | HR | K | N | N | Y | N | D |
| 096 | `dbo.usp_NguoiLX_GPLX_Update` | SP | EXT0 | W:G | GPLX issue/print/return update or correction | MX | M | B | N | Y | B | D; N59 |
| 097 | `dbo.usp_NguoiLX_HoSo_CCNghe` | SP | EXT0 | KH, NL, HS | Existing vocational certificate | HR | K | N | N | Y | N | D |
| 098 | `dbo.usp_NguoiLX_HoSo_Delete` | SP | EXT0 | W:HS | Dossier delete maintenance | AM | K | N | N | Y | N | D |
| 099 | `dbo.usp_NguoiLX_HoSo_Get_Sua_HS` | SP | EXT0 | KH, NL, HS | Dossier edit detail | AM | K | N | N | Y | N | D |
| 100 | `dbo.usp_NguoiLX_HoSo_Insert` | SP | EXT0 | W:HS | Create new dossier/intake | NB | K; assigned statuses are not gates | Y | Y | N | Y | D; N44 |
| 101 | `dbo.usp_NguoiLX_HoSo_Insert2` | SP | EXT0 | W:HS | Create new dossier/intake variant | NB | K; assigned statuses are not gates | Y | Y | N | Y | D; N45 |
| 102 | `dbo.usp_NguoiLX_HoSo_RPT_CCNghe` | SP | EXT0 | KH, NL, HS | Historical vocational-certificate report | HR | K | N | N | Y | N | D |
| 103 | `dbo.usp_NguoiLX_HoSo_RPT_CNTN` | SP | EXT0 | DV, NL, HS | Historical graduation-certificate report | HR | K | N | N | Y | N | D |
| 104 | `dbo.usp_NguoiLX_HoSo_Search` | SP | EXT0 | NL, HS | Dossier administration search | AM | AP plus optional X | Y | Y | B | N | D |
| 105 | `dbo.usp_NguoiLX_HoSo_Search_TN` | SP | EXT0 | KH, NL, HS | Graduation administration search | AM | X plus course key | N | Y | B | N | D |
| 106 | `dbo.usp_NguoiLX_HoSo_Search_TN_ByKH` | SP | EXT0 | KH, NL, HS | Existing graduation/history branches | HR | A plus X | Y | Y | Y | N | D; history counterevidence |
| 107 | `dbo.usp_NguoiLX_HoSo_Search2` | SP | EXT0 | NL, HS | Dossier administration search | AM | AP plus optional X | Y | Y | B | N | D |
| 108 | `dbo.usp_NguoiLX_HoSo_Search3` | SP | EXT0 | NL, HS | Dossier administration search | AM | K plus X | Y | Y | B | N | D |
| 109 | `dbo.usp_NguoiLX_HoSo_SearchYCTN` | SP | EXT0 | HS | Graduation-request maintenance search | AM | X | Y | Y | B | N | D |
| 110 | `dbo.usp_NguoiLX_HoSo_SearchYCTN2` | SP | EXT0 | HS | Graduation-request maintenance search | AM | AX | Y | Y | B | N | D |
| 111 | `dbo.usp_NguoiLX_HoSo_Select` | SP | EXT0 | HS | Existing dossier detail | HR | K | Y | Y | Y | N | D |
| 112 | `dbo.usp_NguoiLX_HoSo_Select_Paging` | SP | EXT0 | KH, NL, HS | BCII-history / training-administration paging | MX | M | B | Y | B | N | D; see section 4 |
| 113 | `dbo.usp_NguoiLX_HoSo_SelectAll` | SP | EXT0 | HS | Existing dossier set | HR | — | Y | Y | Y | N | D |
| 114 | `dbo.usp_NguoiLX_Hoso_SelectForSync` | SP | EXT0 | NL, HS | Legacy sync export | RT | X | N | Y | N | N | D |
| 115 | `dbo.usp_NguoiLX_Hoso_SelectForSyncOption` | SP | EXT0 | NL, HS | Legacy sync export option | RT | X | N | Y | N | N | D |
| 116 | `dbo.usp_NguoiLX_HoSo_SoHoSoOut` | SP | EXT0 | HS indirect | Dossier-number runtime helper | RT | K | N | N | N | N | C |
| 117 | `dbo.usp_NguoiLX_HoSo_ThemHS_BC2` | SP | EXT0 | KH, NL, HS | Select learner for new BCII | NB | X; first branch has OR bypass | N | Y | Y | Y | D; N18–N19 |
| 118 | `dbo.usp_NguoiLX_HoSo_Update` | SP | EXT0 | W:HS | Full dossier workflow update or correction | MX | M | B | B | Y | B | D; N46 |
| 119 | `dbo.usp_NguoiLX_HoSo_Update_CCNghe` | SP | EXT0 | W:HS | Certificate/result update or correction | MX | M | N | B | Y | B | D; N48 |
| 120 | `dbo.usp_NguoiLX_HoSo_Update_CNTN` | SP | EXT0 | W:HS | Graduation result, retry, or correction | MX | M | N | B | Y | B | D; N49–N51 |
| 121 | `dbo.usp_NguoiLX_HoSo_Update_HSTotNghiep` | SP | EXT0 | W:HS | Graduation result/retry | NB | K; TT assigned/restored, not an activity gate | N | Y | Y | Y | D; N52–N53 |
| 122 | `dbo.usp_NguoiLX_HoSo_Update2` | SP | EXT0 | W:HS | Full dossier workflow update or correction | MX | M | B | B | Y | B | D; N47 |
| 123 | `dbo.usp_NguoiLX_HoSo_UpdateKQBC2` | SP | EXT0 | W:HS | Record new BCII/exam result | NB | K only | N | Y | Y | Y | D; N27 |
| 124 | `dbo.usp_NguoiLX_HoSo_UpdateKQSH` | SP | EXT0 | B2, W:HS | Record new exam/decision result | NB | R+ and AP on B2, not HS | Y | Y | Y | Y | D; N28 |
| 125 | `dbo.usp_NguoiLX_HoSo_UpdateTTXLy` | SP | EXT0 | W:HS | Workflow advance/retry or correction | MX | M | N | B | Y | B | D; N54 |
| 126 | `dbo.usp_NguoiLX_Insert` | SP | EXT0 | W:NL | Create new learner/intake | NB | K; assigned status is not a gate | Y | N | N | Y | D; N42 |
| 127 | `dbo.usp_NguoiLX_Insert2` | SP | EXT0 | W:NL | Create new learner/intake variant | NB | K; assigned status is not a gate | Y | N | N | Y | D; N43 |
| 128 | `dbo.usp_NguoiLX_KetQuaDaoTao_CSDT` | SP | EXT0 | B1, KH, NL, HS | Existing training-result views | HR | AX plus R+ | Y | Y | Y | N | D; H02–H03 |
| 129 | `dbo.usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat` | SP | EXT0 | W:HS | Training-result workflow update or correction | MX | M | N | B | Y | B | D; N55 |
| 130 | `dbo.usp_NguoiLX_KetQuaDaoTao_CSDT_GetHocVien` | SP | EXT0 | KH, NL, HS | Training-result maintenance lookup | AM | AX | Y | Y | B | N | D |
| 131 | `dbo.usp_NguoiLX_Select` | SP | EXT0 | NL | Existing learner detail | HR | K | N | N | Y | N | D |
| 132 | `dbo.usp_NguoiLX_Select_By_MaBC2` | SP | EXT0 | NL, HS | Select existing BCII list for new exam export | NB | X plus R+ | N | Y | Y | Y | D; N25 |
| 133 | `dbo.usp_NguoiLX_Select_By_MaBC2_2XML` | SP | EXT0 | NL, HS | Select existing BCII list for new XML/export | NB | R+ only | N | N | Y | Y | D; N26 |
| 134 | `dbo.usp_NguoiLX_Select_By_MaDK` | SP | EXT0 | NL, HS | Existing learner/dossier detail | HR | K | N | N | Y | N | D |
| 135 | `dbo.usp_NguoiLX_Select_By_MaKH` | SP | EXT0 | NL, HS | Select learners for new BCI | NB | AX | Y | Y | N | Y | D; N15 |
| 136 | `dbo.usp_NguoiLX_Select_By_MaKH2` | SP | EXT0 | KH, NL, HS | Select learners for new BCII | NB | AX or X by branch | B | Y | B | Y | D; N16–N17 |
| 137 | `dbo.usp_NguoiLX_SelectAll` | SP | EXT0 | NL | Existing learner set | HR | — | N | N | Y | N | D |
| 138 | `dbo.usp_NguoiLX_SelectTen` | SP | EXT0 | NL | Existing learner-name lookup | HR | K | N | N | Y | N | T; malformed identifier |
| 139 | `dbo.usp_NguoiLX_TongHop_By_MaKH2` | SP | EXT0 | KH, W:HS | Add learners to BCII / cancel aggregation | MX | M | N | Y | B | B | D; N20–N21 |
| 140 | `dbo.usp_NguoiLX_Update` | SP | EXT0 | W:NL | Learner reactivation-capable update or correction | MX | M | B | N | Y | B | D; N56 |
| 141 | `dbo.usp_NguoiLX_Update_Hs_CSDT` | SP | EXT0 | W:NL | Learner training-centre maintenance | AM | K | N | N | N | N | D |
| 142 | `dbo.usp_NguoiLX_Update_ThemHSBC2` | SP | EXT0 | W:HS | Add learner to new BCII | NB | K only | N | Y | Y | Y | D; N22 |
| 143 | `dbo.usp_NguoiLXHoSo_Update_DanhSachHocSinhBC2` | SP | EXT0 | W:HS | Add/remove/update BCII membership | MX | M | N | Y | B | B | D; N23–N24 |
| 144 | `dbo.usp_NguoiLXHS_GiayTo_Delete` | SP | EXT0 | W:GT | Document maintenance | AM | K | N | N | Y | N | D |
| 145 | `dbo.usp_NguoiLXHS_GiayTo_Delete2` | SP | EXT0 | W:GT | Document maintenance variant | AM | K | N | N | Y | N | D |
| 146 | `dbo.usp_NguoiLXHS_GiayTo_Insert` | SP | EXT0 | W:GT | Create new dossier document | NB | K; assigned status is not a gate | Y | N | N | Y | D; N57 |
| 147 | `dbo.usp_NguoiLXHS_GiayTo_Search_HSGT` | SP | EXT0 | GT | Existing document search | HR | K | N | N | Y | N | D |
| 148 | `dbo.usp_NguoiLXHS_GiayTo_Select` | SP | EXT0 | GT | Existing document detail | HR | K | N | N | Y | N | D |
| 149 | `dbo.usp_NguoiLXHS_GiayTo_SelectAll` | SP | EXT0 | GT | Existing document set | HR | — | N | N | Y | N | D |
| 150 | `dbo.usp_NguoiLXHS_GiayTo_Update` | SP | EXT0 | W:GT | Document processing/reactivation or correction | MX | M | B | N | Y | B | D; N58 |
| 151 | `dbo.usp_PM4_CapTra_ThongTinChiTiet` | SP | EXT0 | DV, NL, HS, G | Existing GPLX issuance/return detail | HR | R+ | N | Y | Y | N | D |
| 152 | `dbo.usp_pm4_captra_timkiem` | SP | EXT0 | NL, HS, G | Existing GPLX print/return-state search | HR | A-or-NULL plus X | Y | Y | Y | N | D; H04–H05 |
| 153 | `dbo.usp_PM4_ImportDS` | SP | EXT0 | NL, HS, W:G | Create/update GPLX from imported result | NB | R- or R+; no HS active gate | N | Y | Y | Y | D; N35–N36 |
| 154 | `dbo.usp_PM4_rpt_DSDangKyCapTra` | SP | EXT0 | DV, NL, HS, G | Historical GPLX registration report | HR | S | N | N | Y | N | D |
| 155 | `dbo.usp_PM4_rpt_HienTrangCapTra` | SP | EXT0 | DV, NL, HS | Historical GPLX issuance report | HR | S | N | N | Y | N | D |
| 156 | `dbo.usp_PM4_rpt_TiepNhanHSDangKy` | SP | EXT0 | DV, HS | Historical reception report | HR | A plus X | Y | Y | Y | N | D |
| 157 | `dbo.usp_PM4_TraCuuThongTin_TimKiem` | SP | EXT0 | NL, HS, G | Historical GPLX lookup | HR | A-or-NULL | Y | N | Y | N | D |
| 158 | `dbo.usp_PM4_UpdateThongTinCapTra` | SP | EXT0 | W:HS, W:G | Complete a new GPLX return | NB | K; TT assignment is not a gate | N | Y | Y | Y | D; N60 |
| 159 | `dbo.usp_PM4_XoaThongTinCapTra` | SP | EXT0 | W:HS, W:G | Reopen/retry GPLX return | NB | K; TT assignment is not a gate | N | Y | Y | Y | D; N61 |
| 160 | `dbo.usp_QuangLA_PM4_ImportDS_GetInfo` | SP | EXT0 | NL, HS | Import administration lookup | AM | K | N | N | B | N | D |
| 161 | `dbo.usp_QuangLA_PM4_ThuNhanHS_DSGiayTo` | SP | EXT0 | KH | Receipt document-template lookup | AM | S | N | N | N | N | D |
| 162 | `dbo.usp_QuangLA_PM4_ThuNhanHS_DSHocVien` | SP | EXT0 | NL, HS | Receipt learner maintenance list | AM | A | Y | N | N | N | D |
| 163 | `dbo.usp_QuangLA_PM4_ThuNhanHS_DSKhoaHoc` | SP | EXT0 | B1, KH, HS | Select course for new dossier receipt | NB | R- plus scope/duration | N | N | N | Y | D; N07 |
| 164 | `dbo.usp_QuangLA_PM4_ThuNhanHS_DSKhoaHoc_CapNhat` | SP | EXT0 | B1, KH, HS | Select course while updating receipt | NB | R+ plus scope; no course active gate | AP | N | Y | Y | D; N08 |
| 165 | `dbo.usp_QuangLA_PM4_ThuNhanHS_ExportExcel` | SP | EXT0 | NL, HS | Historical receipt export | HR | A | Y | N | Y | N | D |
| 166 | `dbo.usp_QuangLA_PM4_ThuNhanHS_LoadThongTin` | SP | EXT0 | NL, HS, GT | Receipt edit detail | AM | K; active documents only | Y | N | Y | N | D |
| 167 | `dbo.usp_QuangLA_PM4_ThuNhanHS_ThemMoiCapNhat` | SP | EXT0 | DV, KH, W:NL, W:HS, W:GT | Create/update a new intake and documents | NB | S plus local reference-data checks; no source membership | Y | Y | B | Y | D; N62–N63 |
| 168 | `dbo.usp_QuangLA_PM4_ThuNhanHS_Xoa` | SP | EXT0 | W:NL | Receipt delete maintenance | AM | K | N | N | Y | N | D |
| 169 | `dbo.usp_QuangLA_PM4_TimKiemHS_ExportExcel` | SP | EXT0 | B1, KH, NL, HS | Historical dossier export | HR | A plus scope | Y | N | Y | N | D |
| 170 | `dbo.usp_QuangLA_PM4_TimKiemHS_TimKiem` | SP | EXT0 | B1, KH, NL, HS | Historical dossier search | HR | A plus scope/X display | Y | Y | Y | N | D |
| 171 | `dbo.usp_QuangLA_PM5_RaQDTT_LoadThongTin` | SP | EXT0 | NL, HS | Historical decision detail | HR | K | N | N | Y | N | D |
| 172 | `dbo.usp_QuangLA_PM5_RaQDTT_SuaThongTinHocVien` | SP | EXT0 | W:NL | Decision-related learner maintenance | AM | K | AP on lookup | N | Y | N | D |
| 173 | `dbo.usp_rpt_BienBanTongHopKQSH_ChiTiet` | SP | EXT0 | DV, NL, HS | Historical exam-result minutes | HR | R+ plus X | N | Y | Y | N | D |
| 174 | `dbo.usp_rpt_GiayBienNhan` | SP | EXT0 | DV, NL, HS, G | Historical receipt | HR | K | N | N | Y | N | D |
| 175 | `dbo.usp_rpt_GiayHen` | SP | EXT0 | DV, KH, NL, HS | Historical appointment | HR | A-or-NULL | Y | N | Y | N | D |
| 176 | `dbo.usp_rpt_GiayHen_GiayTo` | SP | EXT0 | HS, GT | Historical appointment documents | HR | K; inactive doc returned as flag | Y | N | Y | N | D |
| 177 | `dbo.usp_rpt_GiayHenKG` | SP | EXT0 | DV, KH, NL, HS | Historical course appointment | HR | A | Y | N | Y | N | D |
| 178 | `dbo.usp_rpt_GiayHenSH` | SP | EXT0 | DV, KSH, NL, HS | Historical exam appointment | HR | R+ | N | N | Y | N | D |
| 179 | `dbo.usp_rpt_GiayHenTra` | SP | EXT0 | DV, NL, HS | Historical return appointment | HR | A | Y | N | Y | N | D |
| 180 | `dbo.usp_rpt_PM4_InToKhai` | SP | EXT0 | DV, NL, HS, GT | Historical application print | HR | active documents | Y | N | Y | N | D |
| 181 | `dbo.usp_TRANS_CLI_HangDoiGui_Search_v1.0` | SP | EXT0 | DV | Transport queue diagnostics | RT | AP on queue row | Y | N | N | N | D |
| 182 | `dbo.usp_TRANS_CLI_HangDoiNhan_Search_v1.0` | SP | EXT0 | DV | Transport queue diagnostics | RT | AP on queue row | Y | N | N | N | D |

Roster classification totals are:

| `NB` | `HR` | `MX` | `AM` | `RT` | `UK` | Total |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 31 | 56 | 19 | 64 | 12 | 0 | 182 |

For the final H12 metrics, a non-`MX` object is one classification unit; an `MX` object is replaced by its explicit branch rows below. Under that rule the exhaustive `HISTORY_READ` count is `59`: `56` dedicated HR objects plus `3` HR branches split from mixed objects. `UNKNOWN` is `0`. Section 6's five rows are only the minimum history-loss counterexample subset of those 59 classification units.

## 4. MIXED object branch split

The roster assigns each object one classification. These `MX` objects are decomposed here so new-business coverage is not inferred from a different branch in the same procedure.

| Object | Branch | Purpose | Branch classification | Current predicate | Must exclude |
| --- | --- | --- | --- | --- | :---: |
| `usp_BaoCao1_DSHS` | `@LoaiBC=1` | Build/select BCI learner list | NB | `HS.TrangThai=1` and `TT_XuLy IN ('01','02','03','04')` | Y |
| `usp_BaoCao1_DSHS` | `@LoaiBC=2`, A/A3 | Build/select BCII learner list | NB | `HS.TrangThai=1` and `TT_XuLy IN ('03','04')` | Y |
| `usp_BaoCao1_DSHS` | `@LoaiBC=3` | Export/read existing processed learners | HR | `HS.TrangThai=1` and downstream-capable `TT_XuLy NOT IN ('01','02')` | N |
| `usp_BaoCao1_DSHS` | non-A `ELSE` | Build/select BCII learner list | NB | `HS.TrangThai=1` and `TT_XuLy='09'` | Y |
| `usp_BaoCaoI_Search` | `@TrangThai=1` | Existing BCI listing | HR | No actual BCI active predicate; supplied parameter only controls a branch condition | N |
| `usp_BaoCaoI_Search` | `@TrangThai=2` | Select BCI for BCII | NB | BCI `TrangThai` filter is commented; `MaKH NOT LIKE '%DB%'` | Y |
| `usp_KhoaHoc_Auto_Search` | `@KieuApDung=0` | General course administration list | AM | CSDT/transport-department scope | N |
| `usp_KhoaHoc_Auto_Search` | `@KieuApDung=1` | Course for BCI | NB | Scope plus licence-class set; no course active gate | Y |
| `usp_KhoaHoc_Auto_Search` | `@KieuApDung=2` | Course for BCII | NB | Scope plus A1/A2 class set; no course active gate | Y |
| `usp_KhoaHoc_Search` | `@KieuApDung=0` | General course administration list | AM | Parameterized status; `@TrangThai=2` bypasses | N |
| `usp_KhoaHoc_Search` | `@KieuApDung=1` | Course for BCI | NB | Parameterized status; `@TrangThai=2` bypasses | Y |
| `usp_KhoaHoc_Search` | `@KieuApDung=2` | Course for BCII | NB | Parameterized status; `@TrangThai=2` bypasses | Y |
| `usp_KhoaHoc_Search` | `@KieuApDung=3` | Course for new result update | NB | Same bypass plus positive BCI relation | Y |
| `usp_NguoiLX_HoSo_Select_Paging` | BCII/downstream mode | Existing BCII dossier paging | HR | `MaBC2` and downstream `TT_XuLy` set | N |
| `usp_NguoiLX_HoSo_Select_Paging` | training mode | Training administration paging | AM | `MaKhoaHoc` and training `TT_XuLy` set | N |
| `usp_NguoiLX_TongHop_By_MaKH2` | A/A3 course | Add course learners to BCII | NB | Course plus `TT_XuLy IN ('03','04')`; no active gate | Y |
| `usp_NguoiLX_TongHop_By_MaKH2` | non-A course | Add course learners to BCII | NB | Course plus `TT_XuLy='09'`; no active gate | Y |
| `usp_NguoiLX_TongHop_By_MaKH2` | blank course | Cancel aggregation | AM | Existing `MaBC2` | N |
| `usp_NguoiLXHoSo_Update_DanhSachHocSinhBC2` | state `101` | Remove/cancel BCII membership | AM | `MaDK` only | N |
| `usp_NguoiLXHoSo_Update_DanhSachHocSinhBC2` | add/update branch | Add learner to BCII | NB | `MaDK` only | Y |
| `usp_NguoiLXHoSo_Update_DanhSachHocSinhBC2` | state `12` block | Advance new BCII workflow | NB | `MaDK` only | Y |

### Parameter-driven mixed writers added by the final-DML audit

For the first eleven objects below, the same executable DML block can advance new work or perform an authorized correction depending on parameter/caller intent. The SQL definition does not encode that intent and the legacy callers are outside this repository. H12 therefore treats the entire block as `NB` fail-closed until implementation provides a separately authorized `AM` correction entry point. This is a resolved conservative `MX` classification, not `UNKNOWN`.

| Object | Executable block/mode | NB capability | Non-NB capability | Current separation | H12 treatment |
| --- | --- | --- | --- | --- | --- |
| `usp_BaoCaoI_Update` | update BCI by `MaBCI` | update/reactivate a BCI used by a new BCII | correct an existing BCI | none | Treat block as NB; N38 |
| `usp_BaoCaoII_Update` | update BCII by `MaBCII` | bind BCI and advance current BCII state | correct existing BCII metadata | none | Treat block as NB; N39 |
| `usp_KySH_Update` | update exam session by `MaKySH` | update a current exam/decision aggregate | correct an old exam session | none | Treat block as NB; N41 |
| `usp_NguoiLX_GPLX_Update` | update GPLX by `MaDK` | issue/print/receive/return work | correct GPLX history | none | Treat block as NB; N59 |
| `usp_NguoiLX_HoSo_Update` | full dossier update by `MaDK` | overwrite BCII/exam/result/decision state | correct retained dossier history | none | Treat block as NB; N46 |
| `usp_NguoiLX_HoSo_Update_CCNghe` | certificate/result update by `MaDK` | write a new certificate/result | correct an existing certificate | none | Treat block as NB; N48 |
| `usp_NguoiLX_HoSo_Update2` | full dossier update by `MaDK` | regenerate dossier number and downstream state | correct retained dossier history | none | Treat block as NB; N47 |
| `usp_NguoiLX_HoSo_UpdateTTXLy` | assign supplied workflow state by `MaDK` | advance/reopen workflow | authorized state correction | none | Treat block as NB; N54 |
| `usp_NguoiLX_KetQuaDaoTao_CSDT_CapNhat` | result update by learner/course key | create/advance a training result | correct a recorded result | none | Treat block as NB; N55 |
| `usp_NguoiLX_Update` | learner update by `MaDK` | reactivate a learner parent | correct learner identity data | none | Treat block as NB; N56 |
| `usp_NguoiLXHS_GiayTo_Update` | update document/status by composite key | activate/process a dossier document | correct retained document metadata | none | Treat block as NB; N58 |
| `usp_NguoiLX_HoSo_Update_CNTN` | nonblank result, current TT `03` | create graduation result and advance to `09` | none | explicit SQL branch | NB; N49 |
| same | nonblank result, current TT other than `03` | update/finalize graduation result | may correct recorded result | not separable inside branch | Treat as NB; N50 |
| same | blank result, current TT `09` | clear result and reopen at `03` | none | explicit SQL branch | NB; N51 |
| same | blank result, current TT other than `09` | none proven | clear/correct result fields without state transition | explicit SQL branch | AM; not in NB denominator |

## 5. Exact 63-branch NEW_BUSINESS predicate ledger

Counting rule: a branch is a top-level mode-specific `SELECT`, `IF`/`ELSE`, or DML block that selects, creates, associates, advances, approves, retries/reopens, records a result/decision, or performs GPLX issue/print/receive/return work. A parameter-driven block that can perform either new work or correction is counted once as new-business-capable and fails closed. Scalar subqueries, projections, delete/deactivate routines, identity-only edits, schedules, and unit/course master-data CRUD are not transactional new-business branches.

| ID | Object / branch | Current predicate on audited row | TrangThai | TT_XuLy | Other required fields/predicates | P1 | P2 | P3 | P4 relation | P5 / verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| N01 | `KhoaHoc_Auto_Search`, mode 1 | CSDT/SoGTVT and licence classes | none | none | `MaCSDT`, `MaSoGTVT` | absent | absent | absent | none | No deletion marker |
| N02 | `KhoaHoc_Auto_Search`, mode 2 | CSDT/SoGTVT and A1/A2 | none | none | `MaCSDT`, `MaSoGTVT` | absent | absent | absent | none | No deletion marker |
| N03 | `KhoaHoc_Search`, mode 1 | `KH.TrangThai=CASE WHEN @TrangThai=2 THEN KH.TrangThai ELSE @TrangThai END` | bypassable | none | `MaCSDT`, `MaKH`, dates, `ThoiGianDT` | non-protective | absent | absent | none | Stage only |
| N04 | `KhoaHoc_Search`, mode 2 | Same parameterized status | bypassable | none | `MaCSDT`, `MaKH`, dates | non-protective | absent | absent | none | Stage only |
| N05 | `KhoaHoc_Search`, mode 3 | Same parameterized status | bypassable | none | dates, duration, `MaKH IN BaoCaoI` | non-protective | absent | absent | positive BCI | Stage only |
| N06 | `KhoaHoc_SearchMaKH` | No course active gate | none | none | class, receipt/start date, `MaKH NOT IN BaoCaoI` | absent | absent | absent | reverse/absence | No deletion marker |
| N07 | `QuangLA...DSKhoaHoc` | No course active gate | none | none | configured `MaCSDT`, duration, `NOT IN BaoCaoI` | absent | absent | absent | reverse/absence | No deletion marker |
| N08 | `QuangLA...DSKhoaHoc_CapNhat` | `HS.TrangThai` occurs only in displayed count | wrong context | none | CSDT scope, positive dossier relation | non-protective | absent | absent | positive HS | No course deletion marker |
| N09 | `BaoCaoI_Search`, mode 2 | BCI active predicate is commented | none | none | `MaCSDT`, `MaKH NOT LIKE '%DB%'`, dates | absent | absent | absent | none | No deletion marker |
| N10 | `KhoaHoc_GiaoVien_SelectKhoaHocMoi` | `KH.TrangThai=1` | exact | none | future `NgayBG`, `NOT IN KhoaHoc_GiaoVien` | exact | absent | absent | reverse/absence | P1 local only |
| N11 | `LichGiaoVien_SelectKhoaHocMoi` | `KhoaHoc_GiaoVien.TrangThai=1`, not course | wrong entity | none | positive relation, future `KH.NgayBG` | non-protective | absent | absent | positive relation | No course deletion marker |
| N12 | `BaoCao1_DSHS`, BC1 | `HS.TrangThai=1` | exact | `01`–`04` | `MaKhoaHoc` | exact | exact | exact | none | Local P3 only |
| N13 | `BaoCao1_DSHS`, BC2 A/A3 | `HS.TrangThai=1` | exact | `03`,`04` | `MaKhoaHoc` | exact | exact | exact | none | Local P3 only |
| N14 | `BaoCao1_DSHS`, non-A | `HS.TrangThai=1` | exact | `09` | `MaKhoaHoc` | exact | exact | exact | none | Local P3 only |
| N15 | `NguoiLX_Select_By_MaKH` | `HS.TrangThai=1` | exact | `01`–`04` | `MaKhoaHoc` | exact | exact | exact | none | Local P3 only |
| N16 | `NguoiLX_Select_By_MaKH2`, A/A3 | `HS.TrangThai=1` | exact | `03`,`04` | `MaKhoaHoc` | exact | exact | exact | none | Local P3 only |
| N17 | `NguoiLX_Select_By_MaKH2`, non-A | No active gate | none | `09` | `MaKhoaHoc` | absent | exact | absent | none | P2 only |
| N18 | `NguoiLX_HoSo_ThemHS_BC2`, A/B1m | No active gate; trailing `OR HangGPLX='B1m'` bypasses prior filters | none | syntactic but bypassable | learner filters, `MaKhoaHoc`, course ended; MaBC2 anti-filter commented | absent | bypassable | absent | none | Unsafe boolean precedence |
| N19 | `NguoiLX_HoSo_ThemHS_BC2`, non-A | No active gate | none | `03`,`09`,`14`,`17`,`18` | learner filters, `MaKhoaHoc`, course ended | absent | exact syntax | absent | none | Retry states overlap history |
| N20 | `NguoiLX_TongHop_By_MaKH2`, A/A3 | No active gate | none | `03`,`04` | `MaKhoaHoc` | absent | exact syntax | absent | none | Bulk write unprotected |
| N21 | `NguoiLX_TongHop_By_MaKH2`, non-A | No active gate | none | `09` | `MaKhoaHoc` | absent | exact syntax | absent | none | Bulk write unprotected |
| N22 | `NguoiLX_Update_ThemHSBC2` | `MaDK` only | none | assigned, not gate | `MaBC2`; `SoHoSo` predicate commented | absent | absent | absent | none | Direct writer unprotected |
| N23 | `NguoiLXHoSo_Update_DanhSachHocSinhBC2`, add | `MaDK` only | none | assigned, not gate | `MaBC2` | absent | absent | absent | none | Direct writer unprotected |
| N24 | Same object, state `12` | `MaDK` only | none | assigned, not gate | workflow input | absent | absent | absent | none | Direct writer unprotected |
| N25 | `NguoiLX_Select_By_MaBC2` | No active gate | none | `11` | positive `MaBC2` | absent | exact syntax | absent | positive B2 | New exam/export unprotected |
| N26 | `NguoiLX_Select_By_MaBC2_2XML` | No active/TT gate | none | none | positive `MaBC2` | absent | absent | absent | positive B2 | New XML/export unprotected |
| N27 | `NguoiLX_HoSo_UpdateKQBC2` | `MaDK` only | none | assigned, not gate | `MaBC2`, `MaKySH`, `SoBD`, BCII result/decision fields | absent | absent | absent | none | Direct writer unprotected |
| N28 | `NguoiLX_HoSo_UpdateKQSH` | `BaoCaoII.TrangThai=1`, not HS | wrong entity | assigned, not gate | positive `MaBC2`; `MaKySH`, `SoBD`, exam/result/decision fields | non-protective | absent | absent | positive B2 | Direct writer unprotected |
| N29 | `CSDT_PheDuyetKQDT_TiepNhan` | Direct keys only | none | assigned/merged, not gate | approval/result fields | absent | absent | absent | none | Direct writer unprotected |
| N30 | `KySH_SearchMaKySH` | `KySH.TrangThai=1`, not learner/dossier | wrong entity | none | future exam date | non-protective | absent | absent | none | No core deletion marker |
| N31 | `KySH_Insert` | Parameter write | none on core | none | exam session fields | absent | absent | absent | none | Direct creator unprotected |
| N32 | `BaoCaoI_Insert` | Parameter write | none on source relation | none | `MaKH`, `MaCSDT`, `KQ_BC1` context | absent | absent | absent | none | Direct creator unprotected |
| N33 | `BaoCaoII_Insert` | Parameter write | none | none | `MaBCI`, `MaCSDT` | absent | absent | absent | none | Direct creator unprotected |
| N34 | `NguoiLX_GPLX_Insert` | Parameter write | none on learner/dossier | none | GPLX/decision key fields | absent | absent | absent | none | Direct creator unprotected |
| N35 | `PM4_ImportDS`, GPLX insert | Existing HS by key; GPLX count `0` | none | assigned later, not gate | reverse/absence GPLX relation | absent | absent | absent | reverse/absence | Direct writer unprotected |
| N36 | `PM4_ImportDS`, GPLX update | Existing HS/GPLX by key | none | assigned later, not gate | positive GPLX relation | absent | absent | absent | positive G | Direct writer unprotected |
| N37 | `BaoCao1_KetQua_update` | `MaDK` plus dossier number; current TT is read only | none | assigned `06/07`, not gate | `MaBC1`, BCI result fields | absent | absent | absent | none | Final BCI-result writer unprotected |
| N38 | `BaoCaoI_Update` | `MaBCI` only | assigned parameter, not gate | none | `MaKH`, `MaCSDT`, BCI receipt/eligibility fields | absent | absent | absent | none | BCI update/reactivation block unprotected |
| N39 | `BaoCaoII_Update` | `MaBCII` only | assigned on BCII, not source gate | none | `MaBCI`, `MaCSDT`, BCII fields | absent | absent | absent | none | Final BCII update block unprotected |
| N40 | `BaoCaoII_Update_PheDuyetKQDT` | `MaBCII` only | assigns BCII status `1`, not source gate | none | approval timestamp | absent | absent | absent | none | BCII approval unprotected |
| N41 | `KySH_Update` | `MaKySH` only | assigned on exam session, not source gate | none | exam/decision/aggregate fields | absent | absent | absent | none | Final exam-session block unprotected |
| N42 | `NguoiLX_Insert` | No source-membership predicate | assigned parameter, not gate | none | supplied/generated learner key, intake identity | absent | absent | absent | none | New learner intake unprotected |
| N43 | `NguoiLX_Insert2` | No source-membership predicate | assigned parameter, not gate | none | supplied/generated learner key, intake identity | absent | absent | absent | none | New learner intake variant unprotected |
| N44 | `NguoiLX_HoSo_Insert` | No parent/source-membership predicate | assigned parameter, not gate | assigned parameter, not gate | generated dossier number; downstream fields accepted directly | absent | absent | absent | none | New dossier intake unprotected |
| N45 | `NguoiLX_HoSo_Insert2` | No parent/source-membership predicate | assigned parameter, not gate | assigned parameter, not gate | generated dossier number; downstream fields accepted directly | absent | absent | absent | none | New dossier intake variant unprotected |
| N46 | `NguoiLX_HoSo_Update` | `MaDK` only; dossier-number predicate commented | assigned parameter, not gate | assigned parameter, not gate | can overwrite BCII/exam/result/decision fields | absent | absent | absent | none | Conservative NB-capable full writer unprotected |
| N47 | `NguoiLX_HoSo_Update2` | `MaDK` only; old dossier-number predicate commented | assigned parameter, not gate | assigned parameter, not gate | regenerates dossier number; can overwrite downstream fields | absent | absent | absent | none | Conservative NB-capable full writer unprotected |
| N48 | `NguoiLX_HoSo_Update_CCNghe` | `MaDK` only | none | active assignment commented; no gate | vocational certificate/result fields | absent | absent | absent | none | Final certificate/result block unprotected |
| N49 | `NguoiLX_HoSo_Update_CNTN`, nonblank result and current TT `03` | `MaDK` only; dossier-number predicate commented | none | exact `03` branch; assigns `09` | graduation certificate/decision fields | absent | exact | absent | none | New graduation result unprotected |
| N50 | Same object, nonblank result and current TT other than `03` | `MaDK` only; implicit TT else | none | broad/non-protective else | updates graduation certificate/decision fields | absent | non-protective | absent | none | Result finalization/correction block unprotected |
| N51 | Same object, blank result and current TT `09` | `MaDK` only; dossier-number predicate commented | none | exact `09`; reverts to `03` | clears graduation fields | absent | exact | absent | none | Retry/reopen graduation workflow unprotected |
| N52 | `NguoiLX_HoSo_Update_HSTotNghiep`, blank certificate | `MaDK` only; dossier-number predicate commented | none | restores old TT, not gate | clears certificate/retry path | absent | absent | absent | none | Graduation retry unprotected |
| N53 | Same object, nonblank certificate | `MaDK` only; dossier-number predicate commented | none | assigns `09`, not gate | certificate fields | absent | absent | absent | none | New graduation result unprotected |
| N54 | `NguoiLX_HoSo_UpdateTTXLy` | `MaDK` only; dossier-number predicate commented | none | arbitrary parameter assignment; `04` sets transfer flag | workflow state | absent | absent | absent | none | Advance/retry state block unprotected |
| N55 | `NguoiLX_KetQuaDaoTao_CSDT_CapNhat` | `MaDK` plus course key | none | CASE merge/assignment, not eligibility gate | training result/conclusion fields | absent | absent | absent | none | Final training-result block unprotected |
| N56 | `NguoiLX_Update` | `MaDK` only | assigned parameter, not current-state gate | none | learner parent/identity fields | absent | absent | absent | none | Conservative learner-reactivation block unprotected |
| N57 | `NguoiLXHS_GiayTo_Insert` | No parent/source-membership predicate | assigned parameter, not gate | none | document type, learner/dossier key | absent | absent | absent | none | New document writer unprotected |
| N58 | `NguoiLXHS_GiayTo_Update` | document type plus learner/dossier key; dossier-number predicate commented | assigned parameter, not gate | none | document name/status | absent | absent | absent | none | Document-processing block unprotected |
| N59 | `NguoiLX_GPLX_Update` | `MaDK` only | assigned on GPLX, not source gate | none | issue/print/return/serial/decision fields | absent | absent | absent | none | Final GPLX block unprotected |
| N60 | `PM4_UpdateThongTinCapTra` | GPLX and dossier by learner key; dossier number commented | none | assigns `00`, not gate | return date/person/place | absent | absent | absent | none | GPLX return writer unprotected |
| N61 | `PM4_XoaThongTinCapTra` | GPLX by learner key; dossier by learner key and dossier number | none | assigns `19`, not gate | clears return fields | absent | absent | absent | none | GPLX retry/reopen writer unprotected |
| N62 | `QuangLA_PM4_ThuNhanHS_ThemMoiCapNhat`, blank/null learner key | duplicate/course/experience prechecks plus success code; no source membership | inserts core status `1`, not gate | inserts initial `01/03`, not gate | positive course/unit lookup; creates learner, dossier and documents | absent | absent | absent | none | New intake branch unprotected |
| N63 | Same object, nonblank learner key | same prechecks plus direct key; no source membership | no active gate | no TT gate | updates learner/dossier and replaces documents | absent | absent | absent | none | Intake/document update branch unprotected |

### Candidate coverage

| Candidate | Exact effective use | Present but bypassable/wrong meaning | Absent/partial | Deletion-safe coverage |
| --- | ---: | ---: | ---: | ---: |
| P1: exact core `TrangThai=1` | 6 | 7 | 50 | 0/63 |
| P2: `TT_XuLy` eligibility | 12 | 2 | 49 | 0/63 |
| P3: exact P1 + P2 | 5 | 0 | 58 | 0/63 |
| P4: no downstream relation | 0 | 11 relation tests with reverse/positive meaning | 52 | 0/63 |
| P5: one shared existing predicate | 0 | 63 use fragmented date/scope/stage/write facts | 63 lack one shared source-membership rule | 0/63 |

All `63/63` transactional new-business branches are unprotected against source deletion. Syntactic status usage is not proof: P2 intentionally includes retry/downstream states such as `14`, `17`, and `18`, which must be preserved.

### Twelve ADMIN_MAINTENANCE resurrection hazards outside the denominator

These paths create or reactivate unit/course master rows but do not themselves select, approve, record, retry or complete one of the counted transactional workflows. They remain `AM`, are not hidden, and cannot release inactive registry ownership. Course hazards require inactive membership exclusion; national-unit hazards require the exact routed-profile rule and must not affect target-native/other-stream units.

| ID | Object / path | Existing guard | Hazard | Required treatment |
| --- | --- | --- | --- | --- |
| R01 | `BaBT_DM_DonviGTVT_InsertOrUpdate`, existing-unit update | `EXISTS` and update by unit key | can set supplied status/fields on routed inactive unit | route-aware ownership/membership guard |
| R02 | same, missing-unit insert | key absence only | can recreate excluded routed unit | route-aware ownership claim; never global anti-join |
| R03 | `DM_DonViGTVT_Insert` | constraints only | unit creation/resurrection | route-aware ownership claim |
| R04 | `DM_DonViGTVT_Insert2` | type/key generation only | unit creation | route-aware ownership claim |
| R05 | `DM_DonViGTVT_Update` | unit key only | unit reactivation | inactive route ownership remains reserved |
| R06 | `DM_DonViGTVT_Update2` | unit key only | unit reactivation variant | inactive route ownership remains reserved |
| R07 | `KhoaHoc_Insert` | no membership/active-parent guard | course creation/resurrection | require valid active course membership/creation authority |
| R08 | `KhoaHoc_InsertEx` | no membership/active-parent guard | course creation variant | same |
| R09 | `KhoaHoc_InsertOrUpdateXML`, existing update | `EXISTS` course key | course reactivation | inactive membership remains reserved |
| R10 | same, missing insert | key absence only | course recreation | explicit source membership/ownership claim |
| R11 | `KhoaHoc_Update` | course key only | course reactivation through supplied state | cannot clear registry exclusion |
| R12 | `KhoaHoc_Update_TrangThai` | course key only | direct status reactivation | cannot clear registry exclusion |

Across the combined 75-path guard surface, P1 is `6 exact / 7 wrong-or-bypass / 62 absent`, P2 is `12 / 2 / 61`, P3 is `5 / 0 / 70`, and P4 is `0 protective / 11 different-meaning / 64 absent`. These combined counts do not change the declared taxonomy: R01–R12 remain `ADMIN_MAINTENANCE`, not transactional `NEW_BUSINESS`.

## 6. Exact HISTORY_READ counterexample set

This is a deliberately bounded counterexample set of five exact branch/query blocks. It is **not** claimed to be the total history universe.

| ID | Object / branch | Historical evidence read | Current predicate | Why `TrangThai=0` is unsafe |
| --- | --- | --- | --- | --- |
| H01 | `usp_BaoCao1_ViewRPT` | Existing BCI/processed learner report | `HS.TrangThai=1`, downstream-capable `TT_XuLy NOT IN ('01','02')` | Deactivation hides the report row |
| H02 | `usp_NguoiLX_KetQuaDaoTao_CSDT`, paged query | Training/graduation/result states, including downstream `09`–`19` | `HS.TrangThai=1` plus TT filters and positive BCI relation | Deactivation hides result history |
| H03 | Same object, unpaged query | Same history surface | Same active predicate | Same loss in alternate query block |
| H04 | `usp_pm4_captra_timkiem`, printed-not-returned mode | GPLX print/issuance state and GPLX relation | `HS.TrangThai=1 OR NULL`, downstream TT states | Deactivation hides issued/printed history |
| H05 | Same object, returned mode | GPLX return history | Same active predicate plus returned TT states | Deactivation hides return history |

Other history readers demonstrate the opposite inconsistency: `usp_BaoCao2_DSHS`, both `usp_BaoCao2_ViewRPT` branches, `usp_NguoiLX_Select_By_MaBC2`, `usp_NguoiLX_Select_By_MaBC2_2XML`, `usp_PM4_CapTra_ThongTinChiTiet`, `usp_BaoCaoI_Select`, and `usp_KhoaHoc_Select` read historical rows without requiring core `TrangThai=1`.

Therefore P1 is simultaneously under-inclusive for new business and over-inclusive for history.

## 7. Application-source audit

### Fifteen components reviewed

| # | Component | Role/classification |
| ---: | --- | --- |
| 1 | `server/QLHV.Infrastructure/Sync/HocVienSourceAttributionDiagnosticsRepository.cs` | Conditional diagnostics; AM |
| 2 | `server/QLHV.Infrastructure/Sync/HocVienV2SqlBuilder.cs` | Source SQL construction; RT |
| 3 | `server/QLHV.Infrastructure/Sync/MotoSyncDonViGTVTOptionPlanner.cs` | Sync option planning; AM |
| 4 | `server/QLHV.Infrastructure/Sync/MotoSyncKhoaHocOptionPlanner.cs` | Sync option planning; AM |
| 5 | `server/QLHV.Infrastructure/Sync/MotoSyncRepository.cs` | Sync administration; AM |
| 6 | `server/QLHV.Infrastructure/Sync/MotoSyncUpdateSqlBuilder.cs` | Sync SQL construction; RT |
| 7 | `server/QLHV.Infrastructure/Sync/QlhvBackupRefreshExecutor.cs` | Backup administration; AM |
| 8 | `server/QLHV.Infrastructure/Sync/QlhvImportReadRepository.cs` | Import administration; AM |
| 9 | `server/QLHV.Infrastructure/Sync/QlhvImportSqlBuilder.cs` | Import SQL construction; AM |
| 10 | `server/QLHV.Infrastructure/Sync/QlhvOperationsRepository.cs` | Operations administration; AM |
| 11 | `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeDomainCatalog.cs` | Fixed runtime domain/partition catalog; RT |
| 12 | `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeSourceReader.cs` | Snapshot/change reader; RT |
| 13 | `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeStreamProcessor.cs` | Realtime orchestration; RT |
| 14 | `server/QLHV.Infrastructure/Sync/Realtime/CsdtRealtimeTargetWriter.cs` | Realtime target writer/history probes; RT |
| 15 | `server/QLHV.Infrastructure/Sync/Realtime/CsdtReversePlanRepository.cs` | Reverse-plan administration; AM |

### Five exact `CSDL_OTO_V1` executable application query branches

| Branch | Path | Classification | Predicate / H12 observation |
| --- | --- | --- | --- |
| `FORWARD_OPTIONAL_CHECKPOINT_EXPIRED_FULL_SNAPSHOT` | `CsdtRealtimeStreamProcessor.cs:350-392`; snapshot `372-376`; writer `614-620` | RT | DomainCatalog partition only; V1 target; no active/deactivation predicate |
| `FORWARD_BASELINE_OR_RECONCILE_FULL_SNAPSHOT` | `CsdtRealtimeStreamProcessor.cs:403-424`; snapshot `405-409` | RT | Full partition snapshot; V1 target; no active/deactivation predicate |
| `FORWARD_INCREMENTAL_CHANGE_TRACKING` | `CsdtRealtimeStreamProcessor.cs:429-466`; change read `429-447` | RT | CT tombstones are runtime state only; they do not deactivate V1 business rows |
| `REVERSE_PLAN_FULL_SNAPSHOT` | controller `138-160` → `CsdtReversePlanRepository.BuildPlanAsync:32-36` → `ComputeAsync:131+`, V1 snapshot `150-159` | AM | Reads V1 physical rows by partition only |
| `REVERSE_EXECUTE_RECOMPUTE_AND_UPDATE_EXISTING` | controller `164-190` → `ExecuteAsync:38-128`; recompute `54-59`; update existing `102-107` | AM | Reverse administration; no new-business selection |

Supporting shapes:

- `CsdtRealtimeSourceReader.cs:84-88`: full snapshot.
- `CsdtRealtimeSourceReader.cs:129-144`: `CHANGETABLE`.
- `CsdtRealtimeSourceReader.cs:244-251`: changed-row read.
- `CsdtRealtimeDomainCatalog.cs:29-123`: fixed partitions; no `TrangThai`, `TT_XuLy`, or exclusion predicate.
- `CsdtRealtimeTargetWriter.cs:985-1105`: history/dependency probes.
- `ForwardWritePlanner.cs:319-332`: `TT_XuLy` locks downstream-history rows; it does not select new business.

A separate configurable `DATA_V1` diagnostics path was reviewed but is not counted among the five exact DB-bound branches because code does not prove that `DATA_V1` equals `CSDL_OTO_V1`.

### Business procedure call proof

Production-wide source search found:

- `0` `CommandType.StoredProcedure`;
- `0` business `usp_*` calls;
- `0` raw business `EXEC`;
- only system procedures such as `sys.sp_getapplock`, `sys.sp_releaseapplock`, extended-property helpers, and unrelated `xp_fileexist`;
- `0` hits for 23 priority NEW/MIXED SQL names and the broader static core-object name set in production `.cs/.ts/.tsx`;
- `IM_GPLX` contains only `README.md` and `.gitkeep`.

Application branch classification is therefore `3 RT`, `2 AM`, `0 NB`, `0 HR`, `0 MX`, `0 UK`. New-business predicate coverage is `N/A (0/0)`, not `100%`.

## 8. H12 consequence

The 63-branch transactional SQL ledger has deletion-safe coverage `0/63`; all `12` separately listed master/control-plane resurrection hazards also lack source-membership protection. There is no universal existing-column predicate:

- `TrangThai=0` misses many new-business branches and hides confirmed history branches.
- `TT_XuLy` is workflow/downstream state, protected by a trusted FK to the live 19-code lookup, and cannot be repurposed as a deletion sentinel.
- Combining both columns inherits both failures.
- No executable central `NOT EXISTS` exclusion exists.
- Dates, course completion, CSDT scope, BCI/BCII relation and exam state are lifecycle predicates, not source-deletion membership.

Required design direction: a stream-scoped exclusion registry must guard every `NB` read and direct writer, while `HR` branches must continue reading preserved V1 shells/history. Reactivation must remove the exclusion, resync V2-owned columns, preserve V1-owned BCII/exam/GPLX history, and retain the established special merges for `TT_XuLy`, `GhiChu`, `GiayCNSK`, and `GiaiTrinh`.

This artifact records analysis only. It does not authorize or implement database, stored-procedure, application, sync, Change Tracking, or configuration changes.
