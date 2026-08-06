# Final resolved design decisions

The operator decisions supersede the former open items:

- Exact receiver table: `dbo.App_GiaoVien_hs`.
- Training groups are mandatory in Phase 1: `dbo.App_KhoaHoc_NhomDaoTao`.
- Assignment table: `dbo.App_HocVien_PhanCong`, FK by `HocVienId`.
- Group defaults are materialized into per-student snapshots with field-level override flags.
- Default changes never propagate silently; preview offers unoverridden-only, replace-all or no-current-change.
- History is close-current + insert-full-snapshot with source `MANUAL|EXCEL|BULK|GROUP`.
- Export column 18 is `Mã giáo viên hồ sơ`, mapped to `App_GiaoVien_hs.MaGiaoVienHs`.
- Import includes all seven exact key headers and never matches/creates master by display name.
- Source master inactivation preserves history and triggers manual review; no cascade/auto-replacement.
- Three new tables and assignment import/history/audit are QLHV-owned; realtime cannot write them.

Remaining values such as batch-size/TTL are configuration proposals, not unresolved data-model decisions. The pack proposes 5,000 rows and 15 minutes and requires performance testing before release.

No production migration was applied by this revision.
