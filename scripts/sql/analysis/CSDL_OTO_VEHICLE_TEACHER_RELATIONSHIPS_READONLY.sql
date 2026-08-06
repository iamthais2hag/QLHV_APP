USE [CSDL_OTO];
GO

/*
  READ-ONLY: inspect vehicle, teacher, course and schedule relationships.
  Output is aggregate or uses internal relation identifiers only.
*/

SELECT
    N'KHOAHOC_XETAP_ROWS' AS Metric,
    CONVERT(nvarchar(40), COUNT_BIG(*)) AS MetricValue
FROM dbo.KhoaHoc_XeTap
UNION ALL
SELECT N'KHOAHOC_GIAOVIEN_ROWS', CONVERT(nvarchar(40), COUNT_BIG(*)) FROM dbo.KhoaHoc_GiaoVien
UNION ALL
SELECT N'ACTIVE_DISTINCT_VEHICLES_KHXT', CONVERT(nvarchar(40), COUNT_BIG(DISTINCT BienSoXe)) FROM dbo.KhoaHoc_XeTap WHERE TrangThai = 1
UNION ALL
SELECT N'ACTIVE_DISTINCT_TEACHERS_KHGV', CONVERT(nvarchar(40), COUNT_BIG(DISTINCT MaGV)) FROM dbo.KhoaHoc_GiaoVien WHERE TrangThai = 1
UNION ALL
SELECT N'VEHICLES_WITHOUT_ACTIVE_KHXT_RELATION', CONVERT(nvarchar(40), COUNT_BIG(*))
FROM dbo.XeTap AS vehicle
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.KhoaHoc_XeTap AS relation
    WHERE relation.BienSoXe = vehicle.BienSoXe
      AND relation.TrangThai = 1
)
UNION ALL
SELECT N'TEACHERS_WITHOUT_ACTIVE_BUSINESS_RELATION', CONVERT(nvarchar(40), COUNT_BIG(*))
FROM dbo.GiaoVien AS teacher
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.KhoaHoc_GiaoVien AS relation
    WHERE relation.MaGV = teacher.MaGV
      AND relation.TrangThai = 1
)
  AND NOT EXISTS
(
    SELECT 1
    FROM dbo.KhoaHoc_XeTap AS relation
    WHERE relation.MaGV = teacher.MaGV
      AND relation.TrangThai = 1
);

SELECT
    N'ORPHAN_KHXT_VEHICLE' AS Metric,
    COUNT_BIG(*) AS MetricValue
FROM dbo.KhoaHoc_XeTap AS relation
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.XeTap AS vehicle WHERE vehicle.BienSoXe = relation.BienSoXe
)
UNION ALL
SELECT N'ORPHAN_KHXT_COURSE', COUNT_BIG(*)
FROM dbo.KhoaHoc_XeTap AS relation
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.KhoaHoc AS courseRow WHERE courseRow.MaKH = relation.MaKH
)
UNION ALL
SELECT N'ORPHAN_KHXT_TEACHER_LOGICAL', COUNT_BIG(*)
FROM dbo.KhoaHoc_XeTap AS relation
WHERE NULLIF(LTRIM(RTRIM(relation.MaGV)), N'') IS NOT NULL
  AND NOT EXISTS
(
    SELECT 1 FROM dbo.GiaoVien AS teacher WHERE teacher.MaGV = relation.MaGV
)
UNION ALL
SELECT N'ORPHAN_KHGV_TEACHER', COUNT_BIG(*)
FROM dbo.KhoaHoc_GiaoVien AS relation
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.GiaoVien AS teacher WHERE teacher.MaGV = relation.MaGV
)
UNION ALL
SELECT N'ORPHAN_KHGV_COURSE', COUNT_BIG(*)
FROM dbo.KhoaHoc_GiaoVien AS relation
WHERE NOT EXISTS
(
    SELECT 1 FROM dbo.KhoaHoc AS courseRow WHERE courseRow.MaKH = relation.MaKH
)
UNION ALL
SELECT N'ORPHAN_KHGV_VEHICLE_LOGICAL', COUNT_BIG(*)
FROM dbo.KhoaHoc_GiaoVien AS relation
WHERE NULLIF(LTRIM(RTRIM(relation.BienSoXe)), N'') IS NOT NULL
  AND NOT EXISTS
(
    SELECT 1 FROM dbo.XeTap AS vehicle WHERE vehicle.BienSoXe = relation.BienSoXe
);

SELECT
    relation.MaLichLV AS InternalRelationId,
    relation.TrangThai AS IsActive,
    relation.IsKhoaHocGiaoVien AS IsCourseLevelAssignment,
    relation.LoaiGV AS TeacherRole,
    CASE WHEN NULLIF(LTRIM(RTRIM(relation.BienSoXe)), N'') IS NULL THEN 0 ELSE 1 END AS HasVehicleReference,
    CASE WHEN relation.NgayBD IS NULL THEN 0 ELSE 1 END AS HasScheduleStart,
    CASE WHEN relation.NgayKT IS NULL THEN 0 ELSE 1 END AS HasScheduleEnd
FROM dbo.KhoaHoc_GiaoVien AS relation
ORDER BY relation.MaLichLV
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;

SELECT
    relation.MaLichSD AS InternalRelationId,
    relation.TrangThai AS IsActive,
    relation.IsKhoaHocXeTap AS IsCourseLevelAssignment,
    CASE WHEN NULLIF(LTRIM(RTRIM(relation.MaGV)), N'') IS NULL THEN 0 ELSE 1 END AS HasTeacherReference,
    CASE WHEN NULLIF(LTRIM(RTRIM(relation.MaHV)), N'') IS NULL THEN 0 ELSE 1 END AS HasLearnerReference,
    CASE WHEN relation.NgayBD IS NULL THEN 0 ELSE 1 END AS HasScheduleStart,
    CASE WHEN relation.NgayKT IS NULL THEN 0 ELSE 1 END AS HasScheduleEnd
FROM dbo.KhoaHoc_XeTap AS relation
ORDER BY relation.MaLichSD
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY;

SELECT @@TRANCOUNT AS SessionOpenTransactionCount;
GO
