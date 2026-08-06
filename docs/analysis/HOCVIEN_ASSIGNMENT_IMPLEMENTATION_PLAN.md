# Final integrated implementation plan

Phase 1 includes all three new QLHV-owned tables: `App_GiaoVien_hs`, `App_KhoaHoc_NhomDaoTao`, `App_HocVien_PhanCong`, plus nullable assignment metadata extensions on existing `App_ImportBatch`.

Migration order: import-session extension → dossier receiver → course group → assignment/history → indexes/permissions. All FKs are `NO ACTION`; receiver uses soft-delete, group uses inactive state, assignment is append-oriented history. Realtime receives no write grant/code path.

Implementation order: catalog/lookups → groups/defaults → full-snapshot assignment writer → preview-confirm APIs → six-section course UI → 18-column export → V2 Excel import → security/performance/operations tests.

Rollback disables assignment writes and rolls back app first. Empty new schema may be dropped assignment → group → receiver → import extension. Once business data/history exists, schema is preserved and corrected forward; no destructive rollback.

Runtime safety uses CT/checkpoint/identity/per-record RowVersion revalidation, never fixed learner counts and never requires operators to stop entering learners.

Full gates and rollback detail: `handoff/HOCVIEN_ASSIGNMENT_REVIEW/08_IMPLEMENTATION_PHASES.md`.
