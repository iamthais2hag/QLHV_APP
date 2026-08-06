namespace QLHV.Infrastructure.Sync;

/// <summary>
/// SQL for the guarded QLHV BAK full-snapshot import. It never performs a physical delete.
/// Every target mutation is scoped by SourceProfileCode + SourceMaDK.
/// </summary>
internal static class QlhvFullSnapshotSyncSql
{
    public const string StagingTableName = "#QlhvFullSync_HocVien";

    public const string CreateStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_HocVien') IS NOT NULL DROP TABLE #QlhvFullSync_HocVien;
CREATE TABLE #QlhvFullSync_HocVien (
    SourceProfileCode NVARCHAR(50)  NOT NULL,
    SourceMaDK        NVARCHAR(50)  NOT NULL,
    SourceSystem      NVARCHAR(50)  NOT NULL,
    SourceVersion     NVARCHAR(50)  NULL,
    MaDK              NVARCHAR(50)  NOT NULL,
    MaKhoa            NVARCHAR(50)  NULL,
    TenKhoa           NVARCHAR(255) NULL,
    MaHangDT          NVARCHAR(20)  NULL,
    HangGPLXHoc       NVARCHAR(20)  NULL,
    HoTen             NVARCHAR(255) NULL,
    NgaySinh          DATE          NULL,
    GioiTinh          NVARCHAR(20)  NULL,
    SoCCCD            NVARCHAR(20)  NULL,
    DiaChiThuongTru   NVARCHAR(500) NULL,
    SoGPLXDaCo        NVARCHAR(50)  NULL,
    HangGPLXDaCo      NVARCHAR(20)  NULL,
    NguoiNhanHoSo     NVARCHAR(150) NULL,
    AnhRelativePath   NVARCHAR(500) NULL,
    ChatLuongAnh      INT           NULL,
    NgayThuNhanAnh    DATETIME2     NULL,
    NguoiThuNhanAnh   NVARCHAR(100) NULL,
    SourceOfTruth     NVARCHAR(30)  NOT NULL,
    V2RowHash         NVARCHAR(64)  NOT NULL,
    RetainReviewedTarget BIT        NOT NULL,
    PRIMARY KEY (SourceProfileCode, SourceMaDK)
);";

    public const string AtomicGuard = @"
SELECT
    (SELECT COUNT(1) FROM #QlhvFullSync_HocVien) AS StagedRows,
    (
        SELECT COUNT(1)
        FROM #QlhvFullSync_HocVien
        WHERE SourceProfileCode <> @SourceProfileCode
    ) AS InvalidSourceProfileRows,
    (
        SELECT COUNT(1)
        FROM dbo.App_HocVien AS target WITH (UPDLOCK, HOLDLOCK)
        WHERE target.SourceProfileCode = @SourceProfileCode
          AND NULLIF(LTRIM(RTRIM(target.SourceMaDK)), N'') IS NULL
    ) AS InvalidTargetIdentityRows,
    (
        SELECT COUNT(1)
        FROM (
            SELECT target.SourceMaDK
            FROM dbo.App_HocVien AS target WITH (UPDLOCK, HOLDLOCK)
            WHERE target.SourceProfileCode = @SourceProfileCode
              AND NULLIF(LTRIM(RTRIM(target.SourceMaDK)), N'') IS NOT NULL
            GROUP BY target.SourceMaDK
            HAVING COUNT(1) > 1
        ) AS duplicateIdentity
    ) AS DuplicateTargetIdentityRows;";

    public const string Merge = @"
MERGE dbo.App_HocVien WITH (HOLDLOCK) AS target
USING #QlhvFullSync_HocVien AS source
ON target.SourceProfileCode = source.SourceProfileCode
AND target.SourceMaDK = source.SourceMaDK

WHEN MATCHED AND source.RetainReviewedTarget = 0 AND (
       target.IsDeleted = 1
    OR ISNULL(target.V2RowHash, N'') <> ISNULL(source.V2RowHash, N'')
)
THEN UPDATE SET
    target.SourceSystem     = source.SourceSystem,
    target.SourceVersion    = source.SourceVersion,
    target.MaDK             = source.MaDK,
    target.MaKhoa           = source.MaKhoa,
    target.TenKhoa          = source.TenKhoa,
    target.MaHangDT         = source.MaHangDT,
    target.HangGPLXHoc      = source.HangGPLXHoc,
    target.HoTen            = source.HoTen,
    target.NgaySinh         = source.NgaySinh,
    target.GioiTinh         = source.GioiTinh,
    target.SoCCCD           = source.SoCCCD,
    target.DiaChiThuongTru  = source.DiaChiThuongTru,
    target.SoGPLXDaCo       = source.SoGPLXDaCo,
    target.HangGPLXDaCo     = source.HangGPLXDaCo,
    target.NguoiNhanHoSo    = source.NguoiNhanHoSo,
    target.AnhRelativePath  = source.AnhRelativePath,
    target.ChatLuongAnh     = source.ChatLuongAnh,
    target.NgayThuNhanAnh   = source.NgayThuNhanAnh,
    target.NguoiThuNhanAnh  = source.NguoiThuNhanAnh,
    target.SourceOfTruth    = source.SourceOfTruth,
    target.V2RowHash        = source.V2RowHash,
    target.LastSyncFromV2At = SYSDATETIME(),
    target.LastSyncStatus   = N'ThanhCong',
    target.LastSyncMessage  = NULL,
    target.IsDeleted        = 0,
    target.DeletedAt        = NULL,
    target.DeletedBy        = NULL,
    target.DeleteReason     = NULL,
    target.UpdatedAt        = SYSDATETIME(),
    target.UpdatedBy        = N'QlhvBakFullSync'

WHEN NOT MATCHED BY TARGET
THEN INSERT (
    SourceProfileCode, SourceMaDK, SourceSystem, SourceVersion,
    MaDK, MaKhoa, TenKhoa, MaHangDT, HangGPLXHoc, HoTen, NgaySinh, GioiTinh,
    SoCCCD, DiaChiThuongTru, SoGPLXDaCo, HangGPLXDaCo, NguoiNhanHoSo,
    AnhRelativePath, ChatLuongAnh, NgayThuNhanAnh, NguoiThuNhanAnh,
    SourceOfTruth, V2RowHash, LastSyncFromV2At, LastSyncStatus, LastSyncMessage,
    IsDeleted, CreatedBy
)
VALUES (
    source.SourceProfileCode, source.SourceMaDK, source.SourceSystem, source.SourceVersion,
    source.MaDK, source.MaKhoa, source.TenKhoa, source.MaHangDT, source.HangGPLXHoc,
    source.HoTen, source.NgaySinh, source.GioiTinh, source.SoCCCD, source.DiaChiThuongTru,
    source.SoGPLXDaCo, source.HangGPLXDaCo, source.NguoiNhanHoSo,
    source.AnhRelativePath, source.ChatLuongAnh, source.NgayThuNhanAnh, source.NguoiThuNhanAnh,
    source.SourceOfTruth, source.V2RowHash, SYSDATETIME(), N'ThanhCong', NULL,
    0, N'QlhvBakFullSync'
)

OUTPUT CASE
    WHEN $action = N'UPDATE' AND deleted.IsDeleted = 1 THEN N'REACTIVATE'
    ELSE $action
END AS SyncAction;";

    public const string SoftDeleteMissing = @"
UPDATE target
SET
    target.IsDeleted       = 1,
    target.DeletedAt       = SYSDATETIME(),
    target.DeletedBy       = N'QlhvBakFullSync',
    target.DeleteReason    = N'Khong con trong snapshot CSDT BAK',
    target.LastSyncFromV2At = SYSDATETIME(),
    target.LastSyncStatus  = N'ThanhCong',
    target.LastSyncMessage = N'Soft-delete vi khong con trong snapshot CSDT BAK',
    target.UpdatedAt       = SYSDATETIME(),
    target.UpdatedBy       = N'QlhvBakFullSync'
OUTPUT N'SOFT_DELETE' AS SyncAction
FROM dbo.App_HocVien AS target WITH (UPDLOCK, HOLDLOCK)
WHERE target.SourceProfileCode = @SourceProfileCode
  AND target.IsDeleted = 0
  AND NOT EXISTS (
      SELECT 1
      FROM #QlhvFullSync_HocVien AS source
      WHERE source.SourceProfileCode = target.SourceProfileCode
        AND source.SourceMaDK = target.SourceMaDK
  );";

    public const string DropStagingTable = @"
IF OBJECT_ID('tempdb..#QlhvFullSync_HocVien') IS NOT NULL DROP TABLE #QlhvFullSync_HocVien;";
}
