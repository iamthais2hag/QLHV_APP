namespace QLHV.Infrastructure.Sync;

/// <summary>SQL fragments for guarded App_HocVien upsert. No physical delete clause exists by design.</summary>
internal static class HocVienTargetMergeSql
{
    public const string StagingTableName = "#Sync_HocVien_Staging";

    public const string CreateStagingTable = @"
IF OBJECT_ID('tempdb..#Sync_HocVien_Staging') IS NOT NULL DROP TABLE #Sync_HocVien_Staging;
CREATE TABLE #Sync_HocVien_Staging (
    MaDK            NVARCHAR(50)   NOT NULL PRIMARY KEY,
    MaKhoa          NVARCHAR(50)   NULL,
    TenKhoa         NVARCHAR(255)  NULL,
    HangGPLXHoc     NVARCHAR(20)   NULL,
    HoTen           NVARCHAR(255)  NULL,
    NgaySinh        DATE           NULL,
    GioiTinh        NVARCHAR(20)   NULL,
    SoCCCD          NVARCHAR(20)   NULL,
    DiaChiThuongTru NVARCHAR(500)  NULL,
    SoGPLXDaCo      NVARCHAR(50)   NULL,
    HangGPLXDaCo    NVARCHAR(20)   NULL,
    NguoiNhanHoSo   NVARCHAR(150)  NULL,
    SourceOfTruth   NVARCHAR(30)   NOT NULL,
    V2RowHash       NVARCHAR(64)   NOT NULL
);
";

    public const string MergeStatement = @"
MERGE dbo.App_HocVien AS tgt
USING #Sync_HocVien_Staging AS src
ON tgt.MaDK = src.MaDK

WHEN MATCHED AND ISNULL(tgt.V2RowHash, '') <> ISNULL(src.V2RowHash, '')
THEN UPDATE SET
    tgt.MaKhoa           = src.MaKhoa,
    tgt.TenKhoa          = src.TenKhoa,
    tgt.HangGPLXHoc      = src.HangGPLXHoc,
    tgt.HoTen            = src.HoTen,
    tgt.NgaySinh         = src.NgaySinh,
    tgt.GioiTinh         = src.GioiTinh,
    tgt.SoCCCD           = src.SoCCCD,
    tgt.DiaChiThuongTru  = src.DiaChiThuongTru,
    tgt.SoGPLXDaCo       = src.SoGPLXDaCo,
    tgt.HangGPLXDaCo     = src.HangGPLXDaCo,
    tgt.NguoiNhanHoSo    = src.NguoiNhanHoSo,
    tgt.SourceOfTruth    = src.SourceOfTruth,
    tgt.V2RowHash        = src.V2RowHash,
    tgt.LastSyncFromV2At = SYSDATETIME(),
    tgt.LastSyncStatus   = N'ThanhCong',
    tgt.LastSyncMessage  = NULL,
    tgt.IsDeleted        = 0,
    tgt.UpdatedAt        = SYSDATETIME(),
    tgt.UpdatedBy        = N'SyncV2'

WHEN NOT MATCHED BY TARGET
THEN INSERT (
    MaDK, MaKhoa, TenKhoa, HangGPLXHoc, HoTen, NgaySinh, GioiTinh,
    SoCCCD, DiaChiThuongTru, SoGPLXDaCo, HangGPLXDaCo, NguoiNhanHoSo,
    SourceOfTruth, V2RowHash, LastSyncFromV2At, LastSyncStatus, LastSyncMessage,
    CreatedBy
)
VALUES (
    src.MaDK, src.MaKhoa, src.TenKhoa, src.HangGPLXHoc, src.HoTen, src.NgaySinh, src.GioiTinh,
    src.SoCCCD, src.DiaChiThuongTru, src.SoGPLXDaCo, src.HangGPLXDaCo, src.NguoiNhanHoSo,
    src.SourceOfTruth, src.V2RowHash, SYSDATETIME(), N'ThanhCong', NULL,
    N'SyncV2'
)

OUTPUT $action AS MergeAction;
";

    public const string DropStagingTable = @"
IF OBJECT_ID('tempdb..#Sync_HocVien_Staging') IS NOT NULL DROP TABLE #Sync_HocVien_Staging;
";
}
