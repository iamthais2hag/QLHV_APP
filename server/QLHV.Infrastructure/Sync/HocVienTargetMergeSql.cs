namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Chuẩn bị câu lệnh MERGE dùng để upsert một lô App_HocVien từ bảng tạm (staging).
/// PHASE B3A: CHỈ KHAI BÁO cấu trúc SQL. KHÔNG được gọi thực thi.
///
/// Thiết kế upsert:
///   1. SqlBulkCopy nạp lô vào bảng tạm #Sync_HocVien_Staging.
///   2. MERGE #Staging → dbo.App_HocVien KEYED ON MaDK:
///      - WHEN NOT MATCHED → INSERT (mới).
///      - WHEN MATCHED AND hash khác → UPDATE (thay đổi).
///      - WHEN MATCHED AND hash giống → skip (không thay đổi).
///      - KHÔNG DELETE (không xóa vật lý theo quy tắc dữ liệu).
///   3. Bảng tạm bị drop cuối giao dịch.
///
/// Quy tắc dữ liệu áp dụng (xem docs/hoc-vien-data-rules.md):
///   - SoCCCD: giá trị đã trim, không chuyển đổi.
///   - GioiTinh: giá trị gốc.
///   - SourceOfTruth = N'V2' cho mọi dòng.
///   - LastSyncFromV2At = SYSDATETIME().
///   - Không xóa vật lý: MERGE không có DELETE clause.
/// </summary>
internal static class HocVienTargetMergeSql
{
    /// <summary>Tạo bảng staging tạm trong kết nối hiện tại (mở trong transaction).</summary>
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
    V2RowHash       NVARCHAR(64)   NULL
);
";

    /// <summary>
    /// MERGE từ staging sang App_HocVien. KEYED ON MaDK; UPDATE chỉ khi hash khác; không DELETE.
    /// CHƯA THỰC THI Ở PHASE B3A.
    /// </summary>
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
    tgt.V2RowHash        = src.V2RowHash,
    tgt.LastSyncFromV2At = SYSDATETIME(),
    tgt.LastSyncStatus   = N'ThanhCong',
    tgt.UpdatedAt        = SYSDATETIME(),
    tgt.UpdatedBy        = N'SyncV2'

WHEN NOT MATCHED BY TARGET
THEN INSERT (
    MaDK, MaKhoa, TenKhoa, HangGPLXHoc, HoTen, NgaySinh, GioiTinh,
    SoCCCD, DiaChiThuongTru, SoGPLXDaCo, HangGPLXDaCo, NguoiNhanHoSo,
    SourceOfTruth, V2RowHash, LastSyncFromV2At, LastSyncStatus,
    CreatedBy
)
VALUES (
    src.MaDK, src.MaKhoa, src.TenKhoa, src.HangGPLXHoc, src.HoTen, src.NgaySinh, src.GioiTinh,
    src.SoCCCD, src.DiaChiThuongTru, src.SoGPLXDaCo, src.HangGPLXDaCo, src.NguoiNhanHoSo,
    N'V2', src.V2RowHash, SYSDATETIME(), N'ThanhCong',
    N'SyncV2'
)

OUTPUT $action AS MergeAction;
";

    /// <summary>Xóa bảng staging sau MERGE.</summary>
    public const string DropStagingTable = @"
IF OBJECT_ID('tempdb..#Sync_HocVien_Staging') IS NOT NULL DROP TABLE #Sync_HocVien_Staging;
";
}
