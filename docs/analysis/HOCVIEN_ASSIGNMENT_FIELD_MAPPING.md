# Final assignment field and ownership mapping

| Business value | Physical source | Assignment field | Match key | Owner |
|---|---|---|---|---|
| Learner registration | `App_HocVien.HocVienId`, `MaDK`, `MaKhoa` | `HocVienId` | exact `MaDangKy+MaKhoa` | Source identity / QLHV relation |
| Course group | `App_KhoaHoc_NhomDaoTao` | `NhomDaoTaoId` | exact `MaKhoa+MaNhom` | QLHV |
| Dossier receiver | `App_GiaoVien_hs` | `GiaoVienHoSoId` | exact `MaGiaoVienHs` | QLHV |
| Class teacher | `App_GiaoVien` | `GiaoVienDungLopId` | exact `MaGV` | Source master / QLHV relation |
| Training vehicle | `App_XeTap` | `XeTapId` | exact unique normalized plate | Source master / QLHV relation |
| Figure-10 vehicle | `App_XeTap` | `XeBaiSo10Id` | exact unique normalized plate | Source master / QLHV relation |
| Import session | existing `App_ImportBatch` | `ImportSessionId` → `ImportBatchId` | sealed session/idempotency key | QLHV |

`App_GiaoVien`, `App_XeTap`, `App_KhoaHoc`, `App_KhoaHoc_GiaoVien`, `App_KhoaHoc_XeTap` are source-owned and read-only in this UI. Realtime has no permission or mapper path for `App_GiaoVien_hs`, `App_KhoaHoc_NhomDaoTao`, `App_HocVien_PhanCong` or assignment import/audit data.

Names are display/search values only. Missing/inactive source masters are retained and warned; no cascade delete and no automatic replacement are allowed.
