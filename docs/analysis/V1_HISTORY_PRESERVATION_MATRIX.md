# V1 history preservation matrix for V2-deleted rows

Audit date: `2026-07-26`  
Database: `CSDL_OTO_V1`  
Direction: `CSDL_OTO → CSDL_OTO_V1`  
Mode: read-only; metadata, module text, and aggregate counts only.

## 1. Decision

V1 history cannot be preserved safely by treating any current business column as a universal inactive flag. A V2 deletion must be represented outside the historical business row. New-business consumers must consult that external exclusion state; history consumers must continue to read the original V1 rows and relations.

The live transactional sample is empty:

| Table | Current rows |
| --- | ---: |
| `KhoaHoc` | 0 |
| `BaoCaoI` | 0 |
| `NguoiLX` | 0 |
| `NguoiLX_HoSo` | 0 |
| `NguoiLXHS_GiayTo` | 0 |
| `BaoCaoII` | 0 |
| `KySH` | 0 |
| `NguoiLX_GPLX` | 0 |

`DM_DonViGTVT` has 1,579 rows. The absence of current transactional history is not positive evidence that future deletion is safe. The contract below is derived from schema, foreign keys, soft relations, procedure definitions, and the V1 ownership rules.

## 2. Historical relation map

| History domain | Physical/soft relation | Data that must remain readable | Consequence for deletion |
| --- | --- | --- | --- |
| Báo cáo II | `BaoCaoII.MaBCI → BaoCaoI.MaBCI` is a soft relation; dossier `MaBC2 → BaoCaoII.MaBCII` is also soft | Existing BCII header, source BCI/course/unit labels, included learner dossiers | Preserve BCI/course/unit/dossier shells whenever either relation exists |
| Kỳ sát hạch | `NguoiLX_HoSo.MaKySH → KySH.MaKySH` is soft | Exam session, candidate number, attempt, decision, exam date | Preserve dossier, learner, course, and unit shells |
| Số báo danh | `NguoiLX_HoSo.SoBD` is stored on the dossier | Historical candidate assignment | Never clear or reset the dossier |
| Kết quả sát hạch | Result and comment columns are stored on `NguoiLX_HoSo` | Theory, simulation, yard, road, aggregate result and remarks | Preserve the complete dossier row |
| Quyết định | `SoQDSH`, `NgayQDSH`, `SoQDTT`, `NgayQDTT`, `NguoiKy` are stored on the dossier | Historical examination/admission decisions | Preserve the complete dossier row |
| GPLX | `NguoiLX_GPLX.MaDK → NguoiLX_HoSo.MaDK` is an inbound `NO_ACTION` FK | Licence number, issue/expiry/print/return history and related snapshots | Preserve GPLX, dossier, learner and required lookup/unit shells |
| Giấy tờ | `NguoiLXHS_GiayTo` is a child of the dossier; reports and intake readers use it inconsistently | Documents shown in receipts, forms, XML/sync and historical dossier views | Do not use `TrangThai=0` as the sole preservation/exclusion mechanism |

No V2 delete may clear downstream columns, reset a downstream `TT_XuLy`, or delete `BaoCaoII`, `KySH`, or `NguoiLX_GPLX`.

## 3. A–F deletion matrix

Automated hard delete for A–E is disabled until the owner approves a versioned `HistoryProbeManifest` for each table, including every hard/soft relation, exact in-transaction probe identifier, null/default/sentinel semantics, child order and schema/mapping fingerprint. Until then—and whenever a relation, sentinel or probe version is unknown—`PRESERVED_EXCLUDED` is mandatory. Case F is never hard-deleted automatically.

| Case | Table/key | History and dependency evidence | Hard-delete allowed only when | Required preservation when hard delete is unsafe | New-business exclusion |
| --- | --- | --- | --- | --- | --- |
| A | `NguoiLXHS_GiayTo (MaGT, MaDK)` | Composite PK; dossier FK is `CASCADE`; no inbound FK. Thirteen SQL consumers were found. Some filter `TrangThai=1`, while `GetMaGiaytoByHosoForSync`, generic select/search procedures and report joins do not consistently treat it as an active predicate. | An in-transaction proof shows the parent dossier has no BCII/exam/decision/GPLX history and the document has not been consumed by a retained receipt, form, XML or audit flow. Absence of an inbound FK is insufficient. | Retain the document row and its original `TrangThai`; do not falsify historical completeness. | Mark only the document membership key inactive in the exclusion registry. New intake/document-selection branches exclude it; history readers do not. |
| B | `NguoiLX_HoSo (MaDK)` | PK is `MaDK`; document child is `CASCADE`; GPLX is inbound `NO_ACTION`. `MaBC2` and `MaKySH` are soft relations. Results, candidate number and decisions live on the row. | No GPLX; no documents requiring retention; no `MaBC2`, `MaKySH`, `SoBD`, result or decision signal; no BCI/BCII relationship; and no other V1-owned history consumer. Every probe must run in the target transaction. | Keep a dossier shell with all V1-owned/history columns unchanged. V2-owned columns follow the normal mapping only on reactivation; deletion does not clear them. | Registry entry for the dossier key; every new BCII, exam, result, decision and GPLX branch must exclude it. |
| C | `NguoiLX (MaDK)` | `NguoiLX_HoSo.MaDK → NguoiLX.MaDK` is `CASCADE`; the schema permits one dossier per learner because dossier PK is also `MaDK`. GPLX is protected indirectly through the dossier. Parent `NguoiLX.TrangThai` is not consulted by all dossier consumers. | No dossier row, no GPLX/history reachable through a dossier, and no other retained reference. The check must not rely on cascade. | Keep the learner shell required for names/identity snapshots and historical joins. Do not delete the dossier or GPLX through the learner cascade. | Mark learner membership inactive and also exclude any V2-owned child that is absent from the same source snapshot, while keeping all V1 history readable. |
| D | `BaoCaoI (MaBCI)` | Dossier `MaBC1` is an inbound `NO_ACTION` FK. `BaoCaoII.MaBCI` is a soft relation with no FK. | No dossier references `MaBC1`, no BCII references `MaBCI`, and no report/audit relation requires the BCI. | Retain the BCI shell so historical BCII and dossier joins continue to resolve. | Registry entry prevents the retained BCI from being offered for a new BCII. |
| E | `KhoaHoc (MaKH)` | SQL Server cascades from course to `BaoCaoI`, `NguoiLX_HoSo`, and `LichHoc`; teacher/vehicle relations use `NO_ACTION`. Descendants can contain V1 history and soft BCII/exam relations. | Explicit descendant probes prove that no dossier, BCI, BCII, exam, decision, GPLX, document, schedule or relation requiring history exists. A cascade plan is never a scope-discovery algorithm. | Retain a course shell and every history-bearing descendant. Do not rewrite dates/status merely to hide it. | Registry entry prevents course selection and propagates effective exclusion to new BCI/BCII/intake branches. |
| F | `DM_DonViGTVT (MaDV)` | National target table with 1,579 rows and 18 inbound `NO_ACTION` FKs in the audited schema. The sync route owns only its scoped CSDT membership, not the national table. | Never by the automated sync. Any exceptional manual retirement requires a separate owner-approved operation outside this algorithm and a target-wide dependency/history proof. | Retain the national row for all historical and other-stream joins. Never anti-join the whole target table against one source. | Registry key includes target/source profile and stream/MaCSDT. New business for that stream excludes only the routed membership without changing unrelated units or history. |

## 4. Why current columns cannot preserve both sides

### `TrangThai`

- It has different meanings across tables and consumers.
- Some new-business branches use `TrangThai=1`; many do not.
- Some history/report branches use `TrangThai=1`, so changing it to zero can hide historical output.
- A concrete unit lookup, `usp_DM_DonViGTVT_SelectAll_DT_SH`, filters only `LoaiDV`. Against the current aggregate it would return 1,280 rows, including 190 rows already having `TrangThai=0`.

### `TT_XuLy`

- Retry/re-registration intentionally uses downstream states such as `14`, `17`, and `18`.
- Downstream states are V1-owned and must not be reset to manufacture inactivity.
- Several write branches locate the dossier only by key or relationship and do not use `TT_XuLy` as an eligibility guard.

### Relationship absence

- No executable scoped module uses a universal `NOT EXISTS` deactivation rule.
- Commented anti-BCII checks in `usp_NguoiLX_HoSo_ThemHS_BC2` are not executable protection.
- Relation absence does not distinguish an active source row from a V2-deleted row and would incorrectly block legitimate retry business for active rows that already have history.

## 5. History-reader contract

History readers must:

1. read the physical V1 business/history tables without the inactive-membership filter;
2. continue resolving retained BCI, course, learner, dossier, document and unit shells;
3. preserve all V1-owned BCII/exam/result/decision/GPLX fields exactly;
4. expose a non-business diagnostic label such as “source row no longer active” only when authorized, never mutate the historical values to create that label;
5. never expose raw operational keys from the exclusion registry in logs, APIs or UI diagnostics.

New-business readers must use the separate active-membership contract. A `MIXED` procedure must place the exclusion predicate only in its new-business branch; applying it to the history branch is a test failure.

The membership predicate is a veto, never an eligibility grant. A new-business path keeps every existing date/scope/status/workflow rule and additionally requires active membership for the candidate and its relevant ancestors. An inactive course, BCI or learner therefore makes a dependent dossier ineligible even if the dossier's own membership record is active. Every final mapped intake/document, BCI/BCII approval, training/graduation, exam/result/decision, and GPLX issue/print/receive/return mutation must lock and recheck this condition in its target transaction; a filtered selector alone leaves a race.

## 6. Hard-delete proof protocol

Before any hard delete:

1. resolve the exact source profile, stream and durable membership key;
2. acquire the target cycle/row serialization boundary;
3. probe explicit hard FKs and every listed soft/history relation;
4. treat unknown/new relations as `BLOCK_DELETE_CONFLICT`;
5. enumerate children in reverse dependency order;
6. delete only explicitly proven V2-owned, history-free rows;
7. retain an inactive membership record so stale replay cannot recreate the row;
8. commit business delete and membership state under the approved cycle journal protocol.

An FK cascade may execute only after the same child set has already been proved safe. It must never be used to discover that set.

## 7. Reactivation

If the exact V2 key reappears:

- change the membership state to active only for the same source profile and stream;
- reject a different-stream/profile claim as `STREAM_OWNERSHIP_CONFLICT`; never auto-transfer historical ownership;
- reactivate parents before children and commit the target upsert plus membership transition atomically;
- upsert V2-owned columns using the ordinary mapping and dependency order;
- retain all V1-owned history and apply the established conditional merges for `TT_XuLy`, `GhiChu`, `GiayCNSK`, and `GiaiTrinh`;
- match the existing shell by immutable key; do not create a duplicate;
- do not reset BCII, exam, result, decision, document, or GPLX state;
- treat an already active/up-to-date reactivation as an idempotent no-op;
- record the reactivation cycle/version while preserving the earlier delete event in an append-only transition journal.

OTO and MOTO reuse this contract because their schema fingerprints match, but their membership rows, source versions, cycle journals and checkpoints remain independent.
