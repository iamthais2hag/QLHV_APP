# V1/BCII protection audit — V2 → V1

> Audit date: 2026-07-25  
> Mode: read-only; no sync and no SQL write executed.

## 1. Executive summary

- The forward implementation has one business-target writer: `CsdtRealtimeTargetWriter.UpsertAsync`, reached through `CsdtRealtimeStreamProcessor.WriteForwardAsync`. All normal execution modes converge on it.
- Five V1-owned tables are outside the fixed catalog and explicitly rejected. Four exist as protected V1 objects; `DM_KetQuaSatHach` was not present in the audited schemas but remains reserved V1-owned.
- The 36 V1-owned `NguoiLX_HoSo` columns are excluded from both INSERT and UPDATE in every forward mode. No current absolute V1-owned overwrite path was found.
- Seven shared/conditional `NguoiLX_HoSo` fields use one lifecycle detector. That detector has a real false-negative (`MaLyDoTCBC2` absent) and multiple false-positives from default/sentinel values. This is one violating implementation path shared by six execution modes.
- `NguoiLX_GPLX` automatic writes are disabled for all 53 columns. However, GPLX is still catalogued as mandatory, so a GPLX schema/CT failure can block unrelated domains; raw learner keys also escape through tombstone diagnostics.
- No business-target DELETE, soft-delete, FK clear, `MaBC2=NULL`, or `TT_XuLy` reset path was found. Tombstones mutate only runtime state/diagnostic storage.
- Of 392 accepted schema entries: 122 have no forward source write (36 V1-owned + 53 GPLX disabled + 29 target-default/audit + 4 V2-only skipped), 7 are conditional, 190 are unconditionally V2-writable after validation, 5 are explicitly domain-blocking, and 59 remain unclassified/fail-closed.

Audit conclusion: protection is structurally fail-closed for absolute V1 ownership, but owner approval and corrective work are required before production acceptance because P1 gaps remain.

## 2. Write-path inventory

| Path | Caller / route | Domain | Operation | Uses policy? | Can bypass policy? |
| --- | --- | --- | --- | --- | --- |
| Automatic baseline | Worker → `ProcessPartitionAsync` → `ExecuteBaselineAsync` → forward snapshot → `WriteForwardAsync` → `UpsertAsync` | Fixed catalog | INSERT missing + UPDATE changed | Yes | No current bypass |
| Manual baseline/full refresh | Manual baseline command → same baseline/write route | Fixed catalog | INSERT + UPDATE | Yes | No current bypass |
| Incremental Change Tracking | `ProcessIncrementalBatchAsync` → `ReadChangesAsync`/changed snapshot → same writer | Fixed catalog | INSERT + UPDATE | Yes | No current bypass |
| Scheduled reconcile | `ExecuteReconcileAsync` → forward partition snapshot → same writer | Fixed catalog | INSERT + UPDATE; retain target-only | Yes | No current bypass |
| Expired-checkpoint rebaseline | Checkpoint-expired branch → full forward snapshot → same writer → catch-up | Fixed catalog | INSERT + UPDATE | Yes | No current bypass |
| Retry/replay | `ExecuteRetryBatchAsync` → changed snapshot → same writer | Fixed catalog | INSERT + UPDATE | Yes | No current bypass |
| Worker restart/recovery | Restore state/checkpoint, then normal processor paths | Fixed catalog | No separate business write primitive | Yes when processing resumes | No current bypass |
| Optional-domain retry | Optional retry scheduler → same processor/writer | Optional fixed domains | INSERT + UPDATE | Yes | No current bypass |
| Tombstone | Processor/state repository persistence | Fixed catalog identities only | State INSERT/UPDATE only | N/A for business data | Does not reach target tables |
| Reverse V1 → V2 | Separate `UpdateExistingAtomicallyAsync` route | Fixed reverse route | UPDATE existing V2 only | Not the forward policy | Excluded from V1 target call graph; future misuse remains P2 |

Writer details:

- `UpsertAsync` calls `CsdtRealtimeColumnOwnershipPolicy.GetRequired(domain)`, validates the explicit read/insert/update projections, then runs staged `InsertMissing` and `UpdateChanged`.
- Source forward snapshots are policy-projected and reject unclassified source columns.
- Domain admission is a fixed `CsdtRealtimeDomainCatalog.Ordered` list. No reflection, schema enumeration, or drift-driven auto-add was found.
- SQL text containing `DELETE` operates on temporary staging or runtime state only; there is no business target `DELETE`/`MERGE` path.

## 3. Whole-table protection

| Table | In catalog | Reader reachable | Generic writer reachable | Reconcile/tombstone effect | Drift/reflection admission | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| BaoCaoII | No | No | Rejected | None on business table | None | PROTECTED |
| KySH | No | No | Rejected | None on business table | None | PROTECTED |
| DM_LyDoTCBC2 | No | No | Rejected | None on business table | None | PROTECTED |
| DM_DiemSatHach | No | No | Rejected | None on business table | None | PROTECTED |
| DM_KetQuaSatHach | No | No | Rejected | None on business table | None | OBJECT_NOT_PRESENT_BUT_RESERVED_V1_OWNED |

Both the catalog and the ownership policy hold deny/reservation knowledge, so an arbitrary domain name cannot be passed successfully into the current reader/writer route.

## 4. `NguoiLX_HoSo` absolute preserve

All 36 listed columns exist in the audited schema and are classified `V1_OWNED` with `AllowInsert=false` and `AllowUpdate=false`.

| Column | INSERT current | UPDATE current | Baseline | Incremental | Reconcile | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| NoiDungSH | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| MaBC2 | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQuaBC2 | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| MaLyDoTCBC2 | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| MaKySH | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| SoBD | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| LanSH | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| SoQDSH | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NgayQDSH | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQua_LyThuyet | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NhanXet_LyThuyet | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQuaSHM | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NhanXet_MoPhong | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQua_Hinh | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NhanXet_Hinh | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQua_Duong | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NhanXet_Duong | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQuaSH | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| SoQDTT | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NgayQDTT | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NguoiKy | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| SoGPLXTmp | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NgayKTBC2 | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| NguoiKTBC2 | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| MaIn | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQuaDoiSanhTW | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| GhiChuKQDSTW | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| ChuKy | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| TT_XuLy_Old | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| CHON_IN_GPLX | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| KetQuaPDSo | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| DAT_QDThucHanh | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| DAT_TGThucHanh | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| DAT_KQCuc | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| DAT_ThoiGianLayKQ | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |
| LyDoTuChoiKQDT | Omit; V1 default/NULL owns value | Omit | Protected | Protected | Protected | ABSOLUTE_PRESERVE / CURRENTLY_PROTECTED |

The forward reader may include these fields only to evaluate lifecycle/relationship locks. They are not admitted to the target write projection.

### Immutable/audit split

| Field | INSERT | UPDATE | Result |
| --- | --- | --- | --- |
| MaDK | Insert key for an accepted missing row | Never update | Immutable business identity |
| IDs | Target-generated/default | Never update from V2 | V1 authoritative |
| NguoiTao; NgayTao | Target-generated/default | Never update from V2 | V1 authoritative audit |
| NguoiSua; NgaySua | Target-generated/default | Never update from V2 | V1 authoritative audit |

## 5. Shared/conditional columns

| Column | Before V1 lifecycle | After V1 lifecycle | Proposed strong lock | Defaults/sentinels ignored | Current status |
| --- | --- | --- | --- | --- | --- |
| TT_XuLy | New row may take only 03/04/09; existing pre-lifecycle row may follow approved training state | Preserve V1 | Strong signals plus states 11,12,13,14,16,17,18,19 | Yes | Conditional implementation defective |
| TrangThai | V2 may update | Preserve V1 | Any strong signal | Default TrangThai ignored | Conditional implementation defective |
| MaKhoaHoc | V2 may update | Preserve V1; also preserve under relationship lock | Any strong signal / relation | Defaults ignored | Conditional implementation defective |
| MaBC1 | V2 may update | Preserve V1; preserve when referenced by BaoCaoII | Any strong signal / relation | Blank ignored | Conditional implementation defective |
| GhiChu | V2 may update | Preserve V1 | Any strong signal | Blank/default ignored | Conditional implementation defective |
| GiaiTrinh | V2 may update | Preserve V1 | Any strong signal | Blank/default ignored | Conditional implementation defective |
| GiayCNSK | V2 may update | Preserve V1 | Any strong signal | Blank/default ignored | Conditional implementation defective |

## 6. Lifecycle predicate review

### Proposed strong predicate

Lifecycle is active when at least one of these is present:

- non-empty: `MaBC2`, `MaKySH`, `SoBD`, `SoQDSH`, `NguoiKTBC2`, `SoQDTT`;
- non-null: `NgayQDSH`, `KetQuaBC2`, `MaLyDoTCBC2`, `NgayKTBC2`, `NgayQDTT`;
- `TT_XuLy` is one of `11,12,13,14,16,17,18,19`.

Do not activate lifecycle from `LanSH=1`, result zeros, `KetQuaSHM=0`, `KetQuaSH='1'`, `CHON_IN_GPLX=0`, default `TrangThai`, or audit timestamps.

### Current predicate gaps

| Type | Current evidence | Consequence |
| --- | --- | --- |
| False negative | `MaLyDoTCBC2` is omitted from the lifecycle signal set | If it is the only strong V1 signal, all seven conditional fields can be overwritten by the common forward writer |
| False positive | `KetQuaSH` non-empty is direct strong evidence even though `'1'` is a documented sentinel | Legitimate V2 changes can be frozen |
| False positive | Many numeric/default-prone columns use only non-null checks (`LanSH`, results, `CHON_IN_GPLX`, etc.) | Almost every newly inserted dossier may appear lifecycle-active on a later pass |
| Blank mismatch | C# checks trimmed/blank values in some places while SQL uses `NULLIF(value,'')`, not a whitespace-normalized equivalent | Plan-time and SQL-time lock decisions may disagree |

The defect is located in one shared planner/writer path and is reachable from baseline, incremental CT, reconcile, expired-checkpoint rebaseline, retry/replay, and optional-domain retry.

## 7. GPLX preserve review

Audited columns (53): `CoQuanQLGPLX`, `DuongDanAnh`, `GhiChu`, `GioiTinh`, `HangGPLX`, `HoTenDem`, `HoVaTen`, `MaDK`, `MaHTCap`, `MaQuocTich`, `MoTaEN`, `MoTaVN`, `NamHocGPLX`, `NgayCapGPLX`, `NgayHHGPLX`, `NgayIn`, `NgaySinh`, `NgaySua`, `NgayTao`, `NgayTra`, `NgayTT_A1`, `NgayTT_A2`, `NgayTT_A3`, `NgayTT_A4`, `NgayTT_B1`, `NgayTT_B2`, `NgayTT_C`, `NgayTT_D`, `NgayTT_E`, `NgayTT_F`, `NgayTT_FB2`, `NgayTT_FC`, `NgayTT_FD`, `NgayTT_FE`, `NgayTTGPLX`, `NguoiKy`, `NguoiSua`, `NguoiTao`, `NguoiTra`, `NoiCapGPLX`, `NoiCT`, `NoiCT_MaDVHC`, `NoiCT_MaDVQL`, `NoiHocGPLX`, `NoiIn`, `NoiTra`, `SoCMT`, `SoGPLX`, `SoGPLXCu`, `SoHoSo`, `SoSeri`, `TenNLX`, `TrangThai`.

| Scenario | Current behavior | Verdict |
| --- | --- | --- |
| Target row exists | Planner excludes domain row; no UPDATE | TARGET V1 PRESERVED |
| Target row missing | Planner excludes domain row; no INSERT | AUTOMATIC FORWARD WRITE DISABLED |
| Baseline/incremental/reconcile/rebaseline/retry | Same `Include=false` planner result | No write bypass |
| Domain health/schema/CT failure | GPLX is mandatory, so failure can block the stream | P1 requirement failure |
| Diagnostics | Key is read for identity and can be persisted/returned raw in tombstones | P1 privacy failure |

No non-key GPLX payload is read into the forward write set, but the domain must be made non-blocking and diagnostics must be de-identified before the requested preserve contract is fully met.

## 8. Relation-lock review

| Relation | Current protection | Residual issue |
| --- | --- | --- |
| BaoCaoI.MaBCI | Insert-only immutable key | None in forward path |
| BaoCaoI.MaKH / MaCSDT | Existing target values preserved when `BaoCaoII.MaBCI` exists or lifecycle dossier relationship locks the row; missing locked insert rejected | Uses the same defective lifecycle predicate for dossier-derived locking |
| KhoaHoc.MaKH | Immutable key | None in forward path |
| KhoaHoc mutable fields | Existing course preserved when connected BCI→BCII or lifecycle dossier exists; missing locked insert rejected | Same lifecycle predicate caveat |
| NguoiLX_HoSo.MaKhoaHoc / MaBC1 | Preserved after lifecycle; MaBC1 relation to BCII also locks | `MaLyDoTCBC2`-only lifecycle false-negative |
| NguoiLX_HoSo.MaDK / NguoiLX.MaDK | Immutable; no target delete | None in forward path |
| NguoiLXHS_GiayTo (MaGT,MaDK) | Composite key insert-only; missing accepted parent rejects child; missing source child does not delete target | None in forward path |

## 9. Delete/tombstone review

- Verdict for every BCII/exam-related business domain: `NO_DELETE`, `NO_CLEAR_RELATION`, `PRESERVE_V1_TARGET`.
- Reconcile only inserts missing and updates changed accepted rows; target-only rows remain.
- Source deletion creates/updates runtime tombstone and source-identity state. It does not delete, soft-delete, clear a foreign key, null `MaBC2`, or reset `TT_XuLy`.
- Worker route switching deletes only `App_CsdtRealtime*` metadata for the old route, not business rows.

## 10. Diagnostics privacy review

| Surface | Raw identity today | Exposure | Required rule |
| --- | --- | --- | --- |
| In-memory `KeyJson` / snapshot identity | Yes | Process memory; needed transiently for exact matching | Keep transient only; never persist/return/display |
| `App_CsdtRealtimeEntityState.EntityKey` | Yes | Runtime state database | Store SHA-256 identity hash only for diagnostics; separate protected transient matching design if exact key is required |
| `App_CsdtRealtimeSourceIdentity.SourceIdentity` | Yes | Runtime state database | SHA-256 identity hash only on diagnostic surface |
| `App_CsdtRealtimeTombstone.EntityKey` / `SourceKeyJson` | Yes | Runtime state database | SHA-256 identity hash only |
| Tombstone DTO `SourceKey` | Yes | API response to business-data viewers | Remove raw key; return hash only |
| Frontend tombstone table | Yes | Renders `sourceKey` directly | Render hash only |
| Conflict DTO/log identity | No raw key found | Uses `sha256:` diagnostic identity | Retain hash-only behavior |
| Reverse plan API | No entity identity | Aggregate counts/token only | Retain |
| History/error text | No intentional key field | Sanitizer removes credentials but does not generically hash arbitrary identity embedded in exceptions | Add identity-aware sanitization and tests |

Raw `MaDK` can therefore be persisted, returned by API, and displayed in the UI through tombstones. No raw `SoCMT` diagnostic surface was found in the current forward route. The approval target is `SHA-256 identity hash only` everywhere outside transient matching.

## 11. Policy vs acceptance-matrix gaps

The companion JSON contains all 1,588 operation rows. Grouped results:

| Domain.Column/group | Acceptance matrix | Code policy | Writer behavior | Match? | Risk |
| --- | --- | --- | --- | --- | --- |
| NguoiLX_HoSo: 36 V1-owned | V1_OWNED / preserve | Excluded INSERT+UPDATE | Excluded in all modes | Yes | Controlled high-impact boundary |
| NguoiLX_GPLX: 53 | DISABLED/PRESERVE_V1 (key immutable) | `Include=false` | No writes, but mandatory domain | Partial | P1 availability/privacy |
| NguoiLX_HoSo: 7 conditional | PRESERVE_V1 after lifecycle | Conditional projection | Detector differs from approved strong predicate | No | P1 overwrite/freeze |
| 29 target-default/audit | TARGET_DEFAULT/IMMUTABLE | Not source-read/write | Target-generated and update-immutable | Yes | Controlled |
| 4 source-only fields | SKIP_V2_ONLY | Excluded | Excluded | Yes | Controlled |
| 190 unconditional V2 fields | COPY/VALIDATE/TRANSFORM | Mostly direct V2 projection | Direct write when schema gate passes | Partial | Transform/validation availability gaps |
| GiaoVien: 5 target-missing fields | BLOCK_DOMAIN | V2-owned write permission exists | Safe only because target schema gate blocks domain | No | P2 latent write grant |
| DM_DonViGTVT/GiaoVien transform fields | Transform/validation required | Direct policy; no transform engine in forward writer | Exact-type/value gate blocks incompatible cases | No (safe failure) | P2 availability/mapping |
| 59 candidate-domain fields | UNKNOWN / SKIP_DOMAIN | No catalog/policy | Fail closed | Effective behavior yes; ownership unresolved | P2 owner decision |

Count reconciliation:

- acceptance entries: 392;
- unconditional V2-writable after required validation/transform: 190;
- conditional: 7;
- V1-owned: 36;
- GPLX disabled: 53;
- target-default/audit: 29;
- V2-only skipped: 4;
- GiaoVien blocked target-missing: 5;
- unclassified: 59;
- additional immutable insert-only keys are counted within the relevant ownership/disposition totals in the source matrix.

## 12. Severity register

### P0 — 0

No current path was found that directly writes or deletes the five reserved V1 tables or the 36 absolute V1-owned dossier columns.

### P1 — 4

1. Lifecycle predicate omits `MaLyDoTCBC2` and accepts sentinel/default-prone non-null signals, affecting all seven conditional columns through the common writer.
2. Raw entity/source keys, including learner `MaDK`, are persisted in state/tombstones and exposed by API/frontend.
3. GPLX is write-disabled but mandatory, so its failure can block the overall stream contrary to the requested non-blocking preserve contract.
4. Critical safety properties rely substantially on source-string tests; no real database integration test proves column projections and preservation across all forward modes.

### P2 — 4

1. Five GiaoVien fields are matrix-blocked but policy-granted; current safety depends on missing target columns/schema validation.
2. GiaoVien enum/code/length transformations remain unimplemented and must stay fail-closed.
3. DM_DonViGTVT fixed-authority and lossless-fit rules remain unimplemented; exact compatibility currently blocks instead of transforming.
4. The generic reverse writer does not assert the forward ownership policy; current fixed route prevents V1-target misuse, but a future call-site change needs a direction/target guard.

## 13. Unresolved owner decisions

1. Assign ownership and mappings for all 59 columns in `XeTap`, `KhoaHoc_XeTap`, and `DM_LuuLuongDaoTao`.
2. Decide the disposition of five GiaoVien source columns absent in V1.
3. Approve exact GiaoVien mapping/validation rules.
4. Approve DM_DonViGTVT authority and lossless-fit rules.
5. Approve whether any lifecycle signals beyond the proposed strong set are legitimate after excluding defaults/sentinels and normalizing whitespace.
6. Confirm GPLX remains fully V1-owned/disabled and must become non-blocking.
7. Confirm the required retention/security model for transient raw matching keys versus persisted hash-only diagnostics.

Owner decisions are intentionally left `PENDING` in the matrix and unchecked in the approval checklist.
