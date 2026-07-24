namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Ordered SQL fragments for the course, teacher and assignment parts of a CSDT BAK snapshot.
/// They run in the same Serializable transaction as <see cref="QlhvFullSnapshotSyncSql"/>.
/// No fragment performs a physical delete.
/// </summary>
internal static class QlhvCourseTeacherFullSnapshotSyncSql
{
    public const string KhoaHocStagingTableName = "#QlhvFullSync_KhoaHoc";
    public const string GiaoVienStagingTableName = "#QlhvFullSync_GiaoVien";
    public const string RelationStagingTableName = "#QlhvFullSync_KhoaHocGiaoVien";

    public const string CreateKhoaHocStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHoc') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHoc;
CREATE TABLE #QlhvFullSync_KhoaHoc (
    SourceProfileCode NVARCHAR(50) NOT NULL,
    SourceMaKhoaHoc NVARCHAR(50) NOT NULL,
    SourceHash NVARCHAR(64) NOT NULL,
    MaKhoa NVARCHAR(50) NOT NULL,
    TenKhoa NVARCHAR(255) NULL,
    MaCSDT NVARCHAR(6) NULL,
    MaSoGTVT NVARCHAR(6) NULL,
    HangGPLX NVARCHAR(20) NULL,
    HangDaoTao NVARCHAR(20) NULL,
    SoQuyetDinhKhaiGiang NVARCHAR(50) NULL,
    NgayQuyetDinhKhaiGiang DATE NULL,
    NgayKhaiGiang DATE NULL,
    NgayBeGiang DATE NULL,
    MucTieuDaoTao NVARCHAR(1000) NULL,
    NgayThi DATE NULL,
    NgaySatHach DATE NULL,
    TongSoHocVien INT NULL,
    SoHocVienTotNghiep INT NULL,
    SoHocVienDuocCapGPLX INT NULL,
    ThoiGianDaoTao INT NULL,
    SoNgayOnKiemTra INT NULL,
    SoNgayThucHoc INT NULL,
    SoNgayNghiLe INT NULL,
    TongSoNgay INT NULL,
    GhiChu NVARCHAR(500) NULL,
    TrangThaiNguon BIT NOT NULL,
    TtXuLy INT NULL,
    HinhThucDaoTao INT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaKhoaHoc)
);";

    public const string CreateGiaoVienStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_GiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_GiaoVien;
CREATE TABLE #QlhvFullSync_GiaoVien (
    SourceProfileCode NVARCHAR(50) NOT NULL,
    SourceMaGV NVARCHAR(20) NOT NULL,
    SourceHash NVARCHAR(64) NOT NULL,
    MaGV NVARCHAR(20) NOT NULL,
    HoTen NVARCHAR(255) NOT NULL,
    HoTenDem NVARCHAR(150) NULL,
    TenGV NVARCHAR(100) NULL,
    NgaySinh DATE NULL,
    SoCCCD NVARCHAR(20) NULL,
    GioiTinh NVARCHAR(20) NULL,
    DienThoai NVARCHAR(50) NULL,
    DiaChi NVARCHAR(500) NULL,
    NoiCtMaDvhc NVARCHAR(5) NULL,
    NoiCtMaDvql NVARCHAR(5) NULL,
    HinhThucTuyenDung NVARCHAR(100) NULL,
    TrinhDoVanHoa NVARCHAR(100) NULL,
    TrinhDoChuyenMon NVARCHAR(255) NULL,
    TrinhDoSuPham NVARCHAR(255) NULL,
    HangGPLX NVARCHAR(100) NULL,
    NgayCapGPLX DATE NULL,
    NgayHetHanGPLX DATE NULL,
    ThamNienLaiXe INT NULL,
    MonHocGiangDay NVARCHAR(1000) NULL,
    AnhRelativePath NVARCHAR(500) NULL,
    GhiChu NVARCHAR(1000) NULL,
    MaCSDT NVARCHAR(6) NULL,
    MaSoGTVT NVARCHAR(6) NULL,
    SoQuyetDinhGCN NVARCHAR(30) NULL,
    NgayQuyetDinhGCN DATE NULL,
    LoaiHinhDaoTao NVARCHAR(500) NULL,
    CacHangGPLXDuocDaoTao NVARCHAR(50) NULL,
    CauTaoSuaChua NVARCHAR(1) NULL,
    DaoDucLaiXe NVARCHAR(1) NULL,
    NghiepVuVanTai NVARCHAR(1) NULL,
    LuatGTDB NVARCHAR(1) NULL,
    KyThuatLaiXe NVARCHAR(1) NULL,
    MaFileTiepNhanXml NVARCHAR(50) NULL,
    ThoiGianTiepNhanXml DATETIME2 NULL,
    NoiCapGCN NVARCHAR(500) NULL,
    LoaiGiaoVien NVARCHAR(50) NULL,
    CacHangDaCo NVARCHAR(500) NULL,
    TrangThaiNguon BIT NOT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaGV)
);";

    public const string CreateRelationStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHocGiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHocGiaoVien;
CREATE TABLE #QlhvFullSync_KhoaHocGiaoVien (
    SourceProfileCode NVARCHAR(50) NOT NULL,
    SourceMaLichLV BIGINT NOT NULL,
    SourceMaKhoaHoc NVARCHAR(50) NOT NULL,
    SourceMaGV NVARCHAR(20) NOT NULL,
    SourceHash NVARCHAR(64) NOT NULL,
    MaKhoa NVARCHAR(50) NOT NULL,
    MaGV NVARCHAR(20) NOT NULL,
    TenGV NVARCHAR(255) NULL,
    MaMonHoc NVARCHAR(50) NULL,
    TenMonHoc NVARCHAR(255) NULL,
    BienSoXe NVARCHAR(20) NULL,
    LoaiGV NVARCHAR(50) NULL,
    SoHocVien INT NULL,
    NgayHieuLuc DATE NULL,
    NgayHetHieuLuc DATE NULL,
    NgayBatDau DATE NULL,
    NgayKetThuc DATE NULL,
    IsKhoaHocGiaoVien BIT NOT NULL,
    GhiChu NVARCHAR(500) NULL,
    TrangThaiNguon BIT NOT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaLichLV)
);";

    public const string CreateStagingTables = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHoc') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHoc;
CREATE TABLE #QlhvFullSync_KhoaHoc (
    SourceProfileCode NVARCHAR(50) NOT NULL,
    SourceMaKhoaHoc NVARCHAR(50) NOT NULL,
    SourceHash NVARCHAR(64) NOT NULL,
    MaKhoa NVARCHAR(50) NOT NULL,
    TenKhoa NVARCHAR(255) NULL,
    MaCSDT NVARCHAR(6) NULL,
    MaSoGTVT NVARCHAR(6) NULL,
    HangGPLX NVARCHAR(20) NULL,
    HangDaoTao NVARCHAR(20) NULL,
    SoQuyetDinhKhaiGiang NVARCHAR(50) NULL,
    NgayQuyetDinhKhaiGiang DATE NULL,
    NgayKhaiGiang DATE NULL,
    NgayBeGiang DATE NULL,
    MucTieuDaoTao NVARCHAR(1000) NULL,
    NgayThi DATE NULL,
    NgaySatHach DATE NULL,
    TongSoHocVien INT NULL,
    SoHocVienTotNghiep INT NULL,
    SoHocVienDuocCapGPLX INT NULL,
    ThoiGianDaoTao INT NULL,
    SoNgayOnKiemTra INT NULL,
    SoNgayThucHoc INT NULL,
    SoNgayNghiLe INT NULL,
    TongSoNgay INT NULL,
    GhiChu NVARCHAR(500) NULL,
    TrangThaiNguon BIT NOT NULL,
    TtXuLy INT NULL,
    HinhThucDaoTao INT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaKhoaHoc)
);

IF OBJECT_ID('tempdb..#QlhvFullSync_GiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_GiaoVien;
CREATE TABLE #QlhvFullSync_GiaoVien (
    SourceProfileCode NVARCHAR(50) NOT NULL,
    SourceMaGV NVARCHAR(20) NOT NULL,
    SourceHash NVARCHAR(64) NOT NULL,
    MaGV NVARCHAR(20) NOT NULL,
    HoTen NVARCHAR(255) NOT NULL,
    HoTenDem NVARCHAR(150) NULL,
    TenGV NVARCHAR(100) NULL,
    NgaySinh DATE NULL,
    SoCCCD NVARCHAR(20) NULL,
    GioiTinh NVARCHAR(20) NULL,
    DienThoai NVARCHAR(50) NULL,
    DiaChi NVARCHAR(500) NULL,
    NoiCtMaDvhc NVARCHAR(5) NULL,
    NoiCtMaDvql NVARCHAR(5) NULL,
    HinhThucTuyenDung NVARCHAR(100) NULL,
    TrinhDoVanHoa NVARCHAR(100) NULL,
    TrinhDoChuyenMon NVARCHAR(255) NULL,
    TrinhDoSuPham NVARCHAR(255) NULL,
    HangGPLX NVARCHAR(100) NULL,
    NgayCapGPLX DATE NULL,
    NgayHetHanGPLX DATE NULL,
    ThamNienLaiXe INT NULL,
    MonHocGiangDay NVARCHAR(1000) NULL,
    AnhRelativePath NVARCHAR(500) NULL,
    GhiChu NVARCHAR(1000) NULL,
    MaCSDT NVARCHAR(6) NULL,
    MaSoGTVT NVARCHAR(6) NULL,
    SoQuyetDinhGCN NVARCHAR(30) NULL,
    NgayQuyetDinhGCN DATE NULL,
    LoaiHinhDaoTao NVARCHAR(500) NULL,
    CacHangGPLXDuocDaoTao NVARCHAR(50) NULL,
    CauTaoSuaChua NVARCHAR(1) NULL,
    DaoDucLaiXe NVARCHAR(1) NULL,
    NghiepVuVanTai NVARCHAR(1) NULL,
    LuatGTDB NVARCHAR(1) NULL,
    KyThuatLaiXe NVARCHAR(1) NULL,
    MaFileTiepNhanXml NVARCHAR(50) NULL,
    ThoiGianTiepNhanXml DATETIME2 NULL,
    NoiCapGCN NVARCHAR(500) NULL,
    LoaiGiaoVien NVARCHAR(50) NULL,
    CacHangDaCo NVARCHAR(500) NULL,
    TrangThaiNguon BIT NOT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaGV)
);

IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHocGiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHocGiaoVien;
CREATE TABLE #QlhvFullSync_KhoaHocGiaoVien (
    SourceProfileCode NVARCHAR(50) NOT NULL,
    SourceMaLichLV BIGINT NOT NULL,
    SourceMaKhoaHoc NVARCHAR(50) NOT NULL,
    SourceMaGV NVARCHAR(20) NOT NULL,
    SourceHash NVARCHAR(64) NOT NULL,
    MaKhoa NVARCHAR(50) NOT NULL,
    MaGV NVARCHAR(20) NOT NULL,
    TenGV NVARCHAR(255) NULL,
    MaMonHoc NVARCHAR(50) NULL,
    TenMonHoc NVARCHAR(255) NULL,
    BienSoXe NVARCHAR(20) NULL,
    LoaiGV NVARCHAR(50) NULL,
    SoHocVien INT NULL,
    NgayHieuLuc DATE NULL,
    NgayHetHieuLuc DATE NULL,
    NgayBatDau DATE NULL,
    NgayKetThuc DATE NULL,
    IsKhoaHocGiaoVien BIT NOT NULL,
    GhiChu NVARCHAR(500) NULL,
    TrangThaiNguon BIT NOT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaLichLV)
);
" + QlhvFullSnapshotSyncSql.CreateStagingTable;

    public const string AtomicGuard = @"
SELECT
    (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHoc) AS StagedKhoaHocRows,
    (SELECT COUNT(1) FROM #QlhvFullSync_GiaoVien) AS StagedGiaoVienRows,
    (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHocGiaoVien) AS StagedRelationRows,
    (SELECT COUNT(1) FROM #QlhvFullSync_HocVien) AS StagedHocVienRows,
    (
        (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHoc WHERE SourceProfileCode <> @SourceProfileCode) +
        (SELECT COUNT(1) FROM #QlhvFullSync_GiaoVien WHERE SourceProfileCode <> @SourceProfileCode) +
        (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHocGiaoVien WHERE SourceProfileCode <> @SourceProfileCode) +
        (SELECT COUNT(1) FROM #QlhvFullSync_HocVien WHERE SourceProfileCode <> @SourceProfileCode)
    ) AS InvalidSourceProfileRows,
    (
        (SELECT COUNT(1) FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
         WHERE SourceProfileCode = @SourceProfileCode AND NULLIF(LTRIM(RTRIM(SourceMaKhoaHoc)), N'') IS NULL) +
        (SELECT COUNT(1) FROM dbo.App_GiaoVien WITH (UPDLOCK, HOLDLOCK)
         WHERE SourceProfileCode = @SourceProfileCode AND NULLIF(LTRIM(RTRIM(SourceMaGV)), N'') IS NULL) +
        (SELECT COUNT(1) FROM dbo.App_KhoaHoc_GiaoVien WITH (UPDLOCK, HOLDLOCK)
         WHERE SourceProfileCode = @SourceProfileCode AND SourceMaLichLV IS NULL) +
        (SELECT COUNT(1) FROM dbo.App_HocVien WITH (UPDLOCK, HOLDLOCK)
         WHERE SourceProfileCode = @SourceProfileCode AND NULLIF(LTRIM(RTRIM(SourceMaDK)), N'') IS NULL)
    ) AS InvalidTargetIdentityRows,
    (
        (SELECT COUNT(1) FROM (
            SELECT SourceMaKhoaHoc FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceProfileCode = @SourceProfileCode AND SourceMaKhoaHoc IS NOT NULL
            GROUP BY SourceMaKhoaHoc HAVING COUNT(1) > 1) AS duplicates) +
        (SELECT COUNT(1) FROM (
            SELECT SourceMaGV FROM dbo.App_GiaoVien WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceProfileCode = @SourceProfileCode AND SourceMaGV IS NOT NULL
            GROUP BY SourceMaGV HAVING COUNT(1) > 1) AS duplicates) +
        (SELECT COUNT(1) FROM (
            SELECT SourceMaLichLV FROM dbo.App_KhoaHoc_GiaoVien WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceProfileCode = @SourceProfileCode AND SourceMaLichLV IS NOT NULL
            GROUP BY SourceMaLichLV HAVING COUNT(1) > 1) AS duplicates) +
        (SELECT COUNT(1) FROM (
            SELECT SourceMaDK FROM dbo.App_HocVien WITH (UPDLOCK, HOLDLOCK)
            WHERE SourceProfileCode = @SourceProfileCode AND SourceMaDK IS NOT NULL
            GROUP BY SourceMaDK HAVING COUNT(1) > 1) AS duplicates)
    ) AS DuplicateTargetIdentityRows,
    (
        SELECT COUNT(1)
        FROM #QlhvFullSync_KhoaHocGiaoVien AS relation
        LEFT JOIN #QlhvFullSync_KhoaHoc AS course
          ON course.SourceProfileCode = relation.SourceProfileCode
         AND course.SourceMaKhoaHoc = relation.SourceMaKhoaHoc
        LEFT JOIN #QlhvFullSync_GiaoVien AS teacher
          ON teacher.SourceProfileCode = relation.SourceProfileCode
         AND teacher.SourceMaGV = relation.SourceMaGV
        WHERE course.SourceMaKhoaHoc IS NULL OR teacher.SourceMaGV IS NULL
    ) + (
        SELECT COUNT(1)
        FROM #QlhvFullSync_HocVien AS student
        WHERE NULLIF(LTRIM(RTRIM(student.MaKhoa)), N'') IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM #QlhvFullSync_KhoaHoc AS course
              WHERE course.SourceProfileCode = student.SourceProfileCode
                AND course.MaKhoa = student.MaKhoa)
    ) AS RelationConflicts,
    (
        CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_KhoaHoc)
               AND EXISTS (SELECT 1 FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK) WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
             THEN 1 ELSE 0 END +
        CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_GiaoVien)
               AND EXISTS (SELECT 1 FROM dbo.App_GiaoVien WITH (UPDLOCK, HOLDLOCK) WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
             THEN 1 ELSE 0 END +
        CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_KhoaHocGiaoVien)
               AND EXISTS (SELECT 1 FROM dbo.App_KhoaHoc_GiaoVien WITH (UPDLOCK, HOLDLOCK) WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
             THEN 1 ELSE 0 END +
        CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_HocVien)
               AND EXISTS (SELECT 1 FROM dbo.App_HocVien WITH (UPDLOCK, HOLDLOCK) WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
             THEN 1 ELSE 0 END
    ) AS EmptyPartitionRiskGroups,
    (
        SELECT COUNT(1)
        FROM #QlhvFullSync_KhoaHoc AS source
        INNER JOIN dbo.App_KhoaHoc AS target WITH (UPDLOCK, HOLDLOCK)
          ON target.MaKhoa = source.MaKhoa
        WHERE target.SourceProfileCode IS NULL
           OR target.SourceProfileCode <> source.SourceProfileCode
           OR ISNULL(target.SourceMaKhoaHoc, N'') <> source.SourceMaKhoaHoc
    ) + (
        SELECT COUNT(1)
        FROM #QlhvFullSync_GiaoVien AS source
        INNER JOIN dbo.App_GiaoVien AS target WITH (UPDLOCK, HOLDLOCK)
          ON target.MaGV = source.MaGV
        WHERE target.SourceProfileCode IS NULL
           OR target.SourceProfileCode <> source.SourceProfileCode
           OR ISNULL(target.SourceMaGV, N'') <> source.SourceMaGV
    ) AS NaturalKeyConflicts;";

    public const string KhoaHocAtomicGuard = @"
SELECT
    (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHoc) AS StagedRows,
    (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHoc
     WHERE SourceProfileCode <> @SourceProfileCode) AS InvalidSourceProfileRows,
    (SELECT COUNT(1) FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
     WHERE SourceProfileCode = @SourceProfileCode
       AND NULLIF(LTRIM(RTRIM(SourceMaKhoaHoc)), N'') IS NULL) AS InvalidTargetIdentityRows,
    (SELECT COUNT(1) FROM (
        SELECT SourceMaKhoaHoc
        FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
          AND NULLIF(LTRIM(RTRIM(SourceMaKhoaHoc)), N'') IS NOT NULL
        GROUP BY SourceMaKhoaHoc
        HAVING COUNT(1) > 1
     ) AS duplicateIdentity) AS DuplicateTargetIdentityRows,
    CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_KhoaHoc)
              AND EXISTS (
                  SELECT 1 FROM dbo.App_KhoaHoc WITH (UPDLOCK, HOLDLOCK)
                  WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
         THEN 1 ELSE 0 END AS EmptyPartitionRiskGroups,
    (SELECT COUNT(1)
     FROM #QlhvFullSync_KhoaHoc AS source
     INNER JOIN dbo.App_KhoaHoc AS target WITH (UPDLOCK, HOLDLOCK)
       ON target.MaKhoa = source.MaKhoa
     WHERE target.SourceProfileCode IS NULL
        OR target.SourceProfileCode <> source.SourceProfileCode
        OR ISNULL(target.SourceMaKhoaHoc, N'') <> source.SourceMaKhoaHoc) AS NaturalKeyConflicts,
    CAST(0 AS int) AS RelationConflicts;";

    public const string GiaoVienAtomicGuard = @"
SELECT
    (SELECT COUNT(1) FROM #QlhvFullSync_GiaoVien) AS StagedRows,
    (SELECT COUNT(1) FROM #QlhvFullSync_GiaoVien
     WHERE SourceProfileCode <> @SourceProfileCode) AS InvalidSourceProfileRows,
    (SELECT COUNT(1) FROM dbo.App_GiaoVien WITH (UPDLOCK, HOLDLOCK)
     WHERE SourceProfileCode = @SourceProfileCode
       AND NULLIF(LTRIM(RTRIM(SourceMaGV)), N'') IS NULL) AS InvalidTargetIdentityRows,
    (SELECT COUNT(1) FROM (
        SELECT SourceMaGV
        FROM dbo.App_GiaoVien WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
          AND NULLIF(LTRIM(RTRIM(SourceMaGV)), N'') IS NOT NULL
        GROUP BY SourceMaGV
        HAVING COUNT(1) > 1
     ) AS duplicateIdentity) AS DuplicateTargetIdentityRows,
    CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_GiaoVien)
              AND EXISTS (
                  SELECT 1 FROM dbo.App_GiaoVien WITH (UPDLOCK, HOLDLOCK)
                  WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
         THEN 1 ELSE 0 END AS EmptyPartitionRiskGroups,
    (SELECT COUNT(1)
     FROM #QlhvFullSync_GiaoVien AS source
     INNER JOIN dbo.App_GiaoVien AS target WITH (UPDLOCK, HOLDLOCK)
       ON target.MaGV = source.MaGV
     WHERE target.SourceProfileCode IS NULL
        OR target.SourceProfileCode <> source.SourceProfileCode
        OR ISNULL(target.SourceMaGV, N'') <> source.SourceMaGV) AS NaturalKeyConflicts,
    CAST(0 AS int) AS RelationConflicts;";

    public const string RelationAtomicGuard = @"
SELECT
    (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHocGiaoVien) AS StagedRows,
    (SELECT COUNT(1) FROM #QlhvFullSync_KhoaHocGiaoVien
     WHERE SourceProfileCode <> @SourceProfileCode) AS InvalidSourceProfileRows,
    (SELECT COUNT(1) FROM dbo.App_KhoaHoc_GiaoVien WITH (UPDLOCK, HOLDLOCK)
     WHERE SourceProfileCode = @SourceProfileCode
       AND SourceMaLichLV IS NULL) AS InvalidTargetIdentityRows,
    (SELECT COUNT(1) FROM (
        SELECT SourceMaLichLV
        FROM dbo.App_KhoaHoc_GiaoVien WITH (UPDLOCK, HOLDLOCK)
        WHERE SourceProfileCode = @SourceProfileCode
          AND SourceMaLichLV IS NOT NULL
        GROUP BY SourceMaLichLV
        HAVING COUNT(1) > 1
     ) AS duplicateIdentity) AS DuplicateTargetIdentityRows,
    CASE WHEN NOT EXISTS (SELECT 1 FROM #QlhvFullSync_KhoaHocGiaoVien)
              AND EXISTS (
                  SELECT 1 FROM dbo.App_KhoaHoc_GiaoVien WITH (UPDLOCK, HOLDLOCK)
                  WHERE SourceProfileCode = @SourceProfileCode AND IsDeleted = 0)
         THEN 1 ELSE 0 END AS EmptyPartitionRiskGroups,
    CAST(0 AS int) AS NaturalKeyConflicts,
    (SELECT COUNT(1)
     FROM #QlhvFullSync_KhoaHocGiaoVien AS relation
     LEFT JOIN dbo.App_KhoaHoc AS course WITH (UPDLOCK, HOLDLOCK)
       ON course.SourceProfileCode = relation.SourceProfileCode
      AND course.SourceMaKhoaHoc = relation.SourceMaKhoaHoc
      AND course.IsDeleted = 0
     LEFT JOIN dbo.App_GiaoVien AS teacher WITH (UPDLOCK, HOLDLOCK)
       ON teacher.SourceProfileCode = relation.SourceProfileCode
      AND teacher.SourceMaGV = relation.SourceMaGV
      AND teacher.IsDeleted = 0
     WHERE course.SourceMaKhoaHoc IS NULL OR teacher.SourceMaGV IS NULL) AS RelationConflicts;";

    public const string MergeKhoaHoc = @"
MERGE dbo.App_KhoaHoc WITH (HOLDLOCK) AS target
USING #QlhvFullSync_KhoaHoc AS source
ON target.SourceProfileCode = source.SourceProfileCode
AND target.SourceMaKhoaHoc = source.SourceMaKhoaHoc
WHEN MATCHED AND (target.IsDeleted = 1 OR ISNULL(target.SourceHash, N'') <> source.SourceHash)
THEN UPDATE SET
    target.SourceHash = source.SourceHash,
    target.MaKhoa = source.MaKhoa,
    target.TenKhoa = source.TenKhoa,
    target.MaCSDT = source.MaCSDT,
    target.MaSoGTVT = source.MaSoGTVT,
    target.HangGPLX = source.HangGPLX,
    target.HangDaoTao = source.HangDaoTao,
    target.SoQuyetDinhKhaiGiang = source.SoQuyetDinhKhaiGiang,
    target.NgayQuyetDinhKhaiGiang = source.NgayQuyetDinhKhaiGiang,
    target.NgayKhaiGiang = source.NgayKhaiGiang,
    target.NgayBeGiang = source.NgayBeGiang,
    target.MucTieuDaoTao = source.MucTieuDaoTao,
    target.NgayThi = source.NgayThi,
    target.NgaySatHach = source.NgaySatHach,
    target.TongSoHocVien = source.TongSoHocVien,
    target.SoHocVienTotNghiep = source.SoHocVienTotNghiep,
    target.SoHocVienDuocCapGPLX = source.SoHocVienDuocCapGPLX,
    target.ThoiGianDaoTao = source.ThoiGianDaoTao,
    target.SoNgayOnKiemTra = source.SoNgayOnKiemTra,
    target.SoNgayThucHoc = source.SoNgayThucHoc,
    target.SoNgayNghiLe = source.SoNgayNghiLe,
    target.TongSoNgay = source.TongSoNgay,
    target.GhiChuV2 = source.GhiChu,
    target.TrangThaiNguon = source.TrangThaiNguon,
    target.TtXuLy = source.TtXuLy,
    target.HinhThucDaoTao = source.HinhThucDaoTao,
    target.SourceOfTruth = N'V2', target.V2RowHash = source.SourceHash,
    target.LastSyncFromV2At = SYSUTCDATETIME(), target.LastSyncStatus = N'ThanhCong', target.LastSyncMessage = NULL,
    target.IsDeleted = 0, target.DeletedAt = NULL, target.DeletedBy = NULL, target.DeleteReason = NULL,
    target.UpdatedAt = SYSUTCDATETIME(), target.UpdatedAtUtc = SYSUTCDATETIME(), target.UpdatedBy = N'QlhvBakFullSync'
WHEN NOT MATCHED BY TARGET THEN INSERT (
    SourceProfileCode, SourceMaKhoaHoc, SourceHash, MaKhoa, TenKhoa, MaCSDT, MaSoGTVT,
    HangGPLX, HangDaoTao, SoQuyetDinhKhaiGiang, NgayQuyetDinhKhaiGiang, NgayKhaiGiang,
    NgayBeGiang, MucTieuDaoTao, NgayThi, NgaySatHach, TongSoHocVien,
    SoHocVienTotNghiep, SoHocVienDuocCapGPLX, ThoiGianDaoTao, SoNgayOnKiemTra,
    SoNgayThucHoc, SoNgayNghiLe, TongSoNgay, GhiChuV2, TrangThaiNguon, TtXuLy,
    HinhThucDaoTao, SourceOfTruth, V2RowHash, LastSyncFromV2At, LastSyncStatus,
    IsDeleted, CreatedAtUtc, UpdatedAtUtc, CreatedBy)
VALUES (
    source.SourceProfileCode, source.SourceMaKhoaHoc, source.SourceHash, source.MaKhoa,
    source.TenKhoa, source.MaCSDT, source.MaSoGTVT, source.HangGPLX, source.HangDaoTao,
    source.SoQuyetDinhKhaiGiang, source.NgayQuyetDinhKhaiGiang, source.NgayKhaiGiang,
    source.NgayBeGiang, source.MucTieuDaoTao, source.NgayThi, source.NgaySatHach,
    source.TongSoHocVien, source.SoHocVienTotNghiep, source.SoHocVienDuocCapGPLX,
    source.ThoiGianDaoTao, source.SoNgayOnKiemTra, source.SoNgayThucHoc,
    source.SoNgayNghiLe, source.TongSoNgay, source.GhiChu, source.TrangThaiNguon,
    source.TtXuLy, source.HinhThucDaoTao, N'V2', source.SourceHash, SYSUTCDATETIME(),
    N'ThanhCong', 0, SYSUTCDATETIME(), SYSUTCDATETIME(), N'QlhvBakFullSync')
OUTPUT CASE WHEN $action = N'UPDATE' AND deleted.IsDeleted = 1 THEN N'REACTIVATE' ELSE $action END AS SyncAction;";

    public const string SoftDeleteKhoaHoc = @"
UPDATE target SET IsDeleted = 1, DeletedAt = SYSUTCDATETIME(), DeletedBy = N'QlhvBakFullSync',
    DeleteReason = N'Khong con trong snapshot CSDT BAK', LastSyncFromV2At = SYSUTCDATETIME(),
    LastSyncStatus = N'ThanhCong', LastSyncMessage = N'Soft-delete vi khong con trong snapshot CSDT BAK',
    UpdatedAt = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = N'QlhvBakFullSync'
OUTPUT N'SOFT_DELETE' AS SyncAction
FROM dbo.App_KhoaHoc AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.SourceProfileCode = @SourceProfileCode AND target.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM #QlhvFullSync_KhoaHoc AS source
                  WHERE source.SourceProfileCode = target.SourceProfileCode
                    AND source.SourceMaKhoaHoc = target.SourceMaKhoaHoc);";

    public const string MergeGiaoVien = @"
MERGE dbo.App_GiaoVien WITH (HOLDLOCK) AS target
USING #QlhvFullSync_GiaoVien AS source
ON target.SourceProfileCode = source.SourceProfileCode AND target.SourceMaGV = source.SourceMaGV
WHEN MATCHED AND (target.IsDeleted = 1 OR ISNULL(target.SourceHash, N'') <> source.SourceHash)
THEN UPDATE SET
    target.SourceHash = source.SourceHash, target.MaGV = source.MaGV, target.HoTen = source.HoTen,
    target.HoTenDem = source.HoTenDem, target.TenGV = source.TenGV, target.NgaySinh = source.NgaySinh,
    target.SoCCCD = source.SoCCCD, target.GioiTinh = source.GioiTinh, target.DienThoai = source.DienThoai,
    target.DiaChi = source.DiaChi, target.NoiCtMaDvhc = source.NoiCtMaDvhc,
    target.NoiCtMaDvql = source.NoiCtMaDvql, target.HinhThucTuyenDung = source.HinhThucTuyenDung,
    target.TrinhDoVanHoa = source.TrinhDoVanHoa, target.TrinhDoChuyenMon = source.TrinhDoChuyenMon,
    target.TrinhDoSuPham = source.TrinhDoSuPham, target.HangGPLX = source.HangGPLX,
    target.NgayCapGPLX = source.NgayCapGPLX, target.NgayHetHanGPLX = source.NgayHetHanGPLX,
    target.ThamNienLaiXe = source.ThamNienLaiXe, target.MonHocGiangDay = source.MonHocGiangDay,
    target.AnhRelativePath = source.AnhRelativePath, target.GhiChu = source.GhiChu,
    target.MaCSDT = source.MaCSDT, target.MaSoGTVT = source.MaSoGTVT,
    target.SoQuyetDinhGCN = source.SoQuyetDinhGCN, target.NgayQuyetDinhGCN = source.NgayQuyetDinhGCN,
    target.LoaiHinhDaoTao = source.LoaiHinhDaoTao,
    target.CacHangGPLXDuocDaoTao = source.CacHangGPLXDuocDaoTao,
    target.CauTaoSuaChua = source.CauTaoSuaChua, target.DaoDucLaiXe = source.DaoDucLaiXe,
    target.NghiepVuVanTai = source.NghiepVuVanTai, target.LuatGTDB = source.LuatGTDB,
    target.KyThuatLaiXe = source.KyThuatLaiXe, target.MaFileTiepNhanXml = source.MaFileTiepNhanXml,
    target.ThoiGianTiepNhanXml = source.ThoiGianTiepNhanXml, target.NoiCapGCN = source.NoiCapGCN,
    target.LoaiGiaoVien = source.LoaiGiaoVien, target.CacHangDaCo = source.CacHangDaCo,
    target.TrangThaiNguon = source.TrangThaiNguon, target.SourceOfTruth = N'V2',
    target.V2RowHash = source.SourceHash, target.LastSyncFromV2At = SYSUTCDATETIME(),
    target.LastSyncStatus = N'ThanhCong', target.LastSyncMessage = NULL,
    target.IsDeleted = 0, target.DeletedAt = NULL, target.DeletedBy = NULL, target.DeleteReason = NULL,
    target.UpdatedAt = SYSUTCDATETIME(), target.UpdatedAtUtc = SYSUTCDATETIME(), target.UpdatedBy = N'QlhvBakFullSync'
WHEN NOT MATCHED BY TARGET THEN INSERT (
    SourceProfileCode, SourceMaGV, SourceHash, MaGV, HoTen, HoTenDem, TenGV, NgaySinh,
    SoCCCD, GioiTinh, DienThoai, DiaChi, NoiCtMaDvhc, NoiCtMaDvql, HinhThucTuyenDung,
    TrinhDoVanHoa, TrinhDoChuyenMon, TrinhDoSuPham, HangGPLX, NgayCapGPLX,
    NgayHetHanGPLX, ThamNienLaiXe, MonHocGiangDay, AnhRelativePath, GhiChu, MaCSDT,
    MaSoGTVT, SoQuyetDinhGCN, NgayQuyetDinhGCN, LoaiHinhDaoTao,
    CacHangGPLXDuocDaoTao, CauTaoSuaChua, DaoDucLaiXe, NghiepVuVanTai, LuatGTDB,
    KyThuatLaiXe, MaFileTiepNhanXml, ThoiGianTiepNhanXml, NoiCapGCN, LoaiGiaoVien,
    CacHangDaCo, TrangThaiNguon, SourceOfTruth, V2RowHash, LastSyncFromV2At,
    LastSyncStatus, IsDeleted, CreatedAtUtc, UpdatedAtUtc, CreatedBy)
VALUES (
    source.SourceProfileCode, source.SourceMaGV, source.SourceHash, source.MaGV, source.HoTen,
    source.HoTenDem, source.TenGV, source.NgaySinh, source.SoCCCD, source.GioiTinh,
    source.DienThoai, source.DiaChi, source.NoiCtMaDvhc, source.NoiCtMaDvql,
    source.HinhThucTuyenDung, source.TrinhDoVanHoa, source.TrinhDoChuyenMon,
    source.TrinhDoSuPham, source.HangGPLX, source.NgayCapGPLX, source.NgayHetHanGPLX,
    source.ThamNienLaiXe, source.MonHocGiangDay, source.AnhRelativePath, source.GhiChu,
    source.MaCSDT, source.MaSoGTVT, source.SoQuyetDinhGCN, source.NgayQuyetDinhGCN,
    source.LoaiHinhDaoTao, source.CacHangGPLXDuocDaoTao, source.CauTaoSuaChua,
    source.DaoDucLaiXe, source.NghiepVuVanTai, source.LuatGTDB, source.KyThuatLaiXe,
    source.MaFileTiepNhanXml, source.ThoiGianTiepNhanXml, source.NoiCapGCN,
    source.LoaiGiaoVien, source.CacHangDaCo, source.TrangThaiNguon, N'V2', source.SourceHash,
    SYSUTCDATETIME(), N'ThanhCong', 0, SYSUTCDATETIME(), SYSUTCDATETIME(), N'QlhvBakFullSync')
OUTPUT CASE WHEN $action = N'UPDATE' AND deleted.IsDeleted = 1 THEN N'REACTIVATE' ELSE $action END AS SyncAction;";

    public const string SoftDeleteGiaoVien = @"
UPDATE target SET IsDeleted = 1, DeletedAt = SYSUTCDATETIME(), DeletedBy = N'QlhvBakFullSync',
    DeleteReason = N'Khong con trong snapshot CSDT BAK', LastSyncFromV2At = SYSUTCDATETIME(),
    LastSyncStatus = N'ThanhCong', LastSyncMessage = N'Soft-delete vi khong con trong snapshot CSDT BAK',
    UpdatedAt = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = N'QlhvBakFullSync'
OUTPUT N'SOFT_DELETE' AS SyncAction
FROM dbo.App_GiaoVien AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.SourceProfileCode = @SourceProfileCode AND target.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM #QlhvFullSync_GiaoVien AS source
                  WHERE source.SourceProfileCode = target.SourceProfileCode
                    AND source.SourceMaGV = target.SourceMaGV);";

    public const string MergeRelation = @"
MERGE dbo.App_KhoaHoc_GiaoVien WITH (HOLDLOCK) AS target
USING #QlhvFullSync_KhoaHocGiaoVien AS source
ON target.SourceProfileCode = source.SourceProfileCode AND target.SourceMaLichLV = source.SourceMaLichLV
WHEN MATCHED AND (target.IsDeleted = 1 OR ISNULL(target.SourceHash, N'') <> source.SourceHash)
THEN UPDATE SET
    target.SourceMaKhoaHoc = source.SourceMaKhoaHoc, target.SourceMaGV = source.SourceMaGV,
    target.SourceHash = source.SourceHash, target.MaKhoa = source.MaKhoa, target.MaGV = source.MaGV,
    target.TenGV = source.TenGV, target.MaMonHoc = source.MaMonHoc, target.TenMonHoc = source.TenMonHoc,
    target.BienSoXe = source.BienSoXe, target.LoaiGV = source.LoaiGV,
    target.SoHocVien = source.SoHocVien, target.NgayHieuLuc = source.NgayHieuLuc,
    target.NgayHetHieuLuc = source.NgayHetHieuLuc, target.NgayBatDau = source.NgayBatDau,
    target.NgayKetThuc = source.NgayKetThuc, target.IsKhoaHocGiaoVien = source.IsKhoaHocGiaoVien,
    target.GhiChu = source.GhiChu,
    target.TrangThaiNguon = source.TrangThaiNguon, target.SourceOfTruth = N'V2',
    target.V2RowHash = source.SourceHash, target.LastSyncFromV2At = SYSUTCDATETIME(),
    target.IsDeleted = 0, target.DeletedAt = NULL, target.DeletedBy = NULL, target.DeleteReason = NULL,
    target.UpdatedAt = SYSUTCDATETIME(), target.UpdatedAtUtc = SYSUTCDATETIME(), target.UpdatedBy = N'QlhvBakFullSync'
WHEN NOT MATCHED BY TARGET THEN INSERT (
    SourceProfileCode, SourceMaLichLV, SourceMaKhoaHoc, SourceMaGV, SourceHash,
    MaKhoa, MaGV, TenGV, MaMonHoc, TenMonHoc, BienSoXe, LoaiGV, SoHocVien,
    NgayHieuLuc, NgayHetHieuLuc, NgayBatDau, NgayKetThuc, IsKhoaHocGiaoVien,
    GhiChu, TrangThaiNguon,
    SourceOfTruth, V2RowHash, LastSyncFromV2At, IsDeleted, CreatedAtUtc, UpdatedAtUtc, CreatedBy)
VALUES (
    source.SourceProfileCode, source.SourceMaLichLV, source.SourceMaKhoaHoc, source.SourceMaGV,
    source.SourceHash, source.MaKhoa, source.MaGV, source.TenGV, source.MaMonHoc,
    source.TenMonHoc, source.BienSoXe, source.LoaiGV, source.SoHocVien, source.NgayHieuLuc,
    source.NgayHetHieuLuc, source.NgayBatDau, source.NgayKetThuc,
    source.IsKhoaHocGiaoVien, source.GhiChu,
    source.TrangThaiNguon, N'V2', source.SourceHash, SYSUTCDATETIME(), 0,
    SYSUTCDATETIME(), SYSUTCDATETIME(), N'QlhvBakFullSync')
OUTPUT CASE WHEN $action = N'UPDATE' AND deleted.IsDeleted = 1 THEN N'REACTIVATE' ELSE $action END AS SyncAction;";

    public const string SoftDeleteRelation = @"
UPDATE target SET IsDeleted = 1, DeletedAt = SYSUTCDATETIME(), DeletedBy = N'QlhvBakFullSync',
    DeleteReason = N'Khong con trong snapshot CSDT BAK', LastSyncFromV2At = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = N'QlhvBakFullSync'
OUTPUT N'SOFT_DELETE' AS SyncAction
FROM dbo.App_KhoaHoc_GiaoVien AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.SourceProfileCode = @SourceProfileCode AND target.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM #QlhvFullSync_KhoaHocGiaoVien AS source
                  WHERE source.SourceProfileCode = target.SourceProfileCode
                    AND source.SourceMaLichLV = target.SourceMaLichLV);";

    public const string DropStagingTables = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHocGiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHocGiaoVien;
IF OBJECT_ID('tempdb..#QlhvFullSync_GiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_GiaoVien;
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHoc') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHoc;
" + QlhvFullSnapshotSyncSql.DropStagingTable;

    public const string DropKhoaHocStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHoc') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHoc;";

    public const string DropGiaoVienStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_GiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_GiaoVien;";

    public const string DropRelationStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_KhoaHocGiaoVien') IS NOT NULL DROP TABLE #QlhvFullSync_KhoaHocGiaoVien;";
}
