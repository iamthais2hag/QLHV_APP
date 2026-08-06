namespace QLHV.Application.Sync.Rt03;

public static class Rt03ChangeTrackingClassifications
{
    public const string KhoaHocSourceInsert = "KHOAHOC_SOURCE_INSERT";
    public const string KhoaHocSourceUpdate = "KHOAHOC_SOURCE_UPDATE";
    public const string MultiFieldPhotoDrift = "MULTI_FIELD_PHOTO_DRIFT";
    public const string NoMappedChange = "NO_MAPPED_CHANGE";
    public const string UnclassifiedForwardColumn = "UNCLASSIFIED_FORWARD_COLUMN";
    public const string UnknownUnsafe = "UNKNOWN_UNSAFE";
}

/// <summary>
/// Classifies only the column mask recorded by SQL Server Change Tracking.
/// It never treats a mask as proof of the old value; current source/target
/// identity and mapped-row evidence must still be revalidated by the worker.
/// </summary>
public static class Rt03ChangeTrackingEventClassifier
{
    public const string ForwardColumnSentinel = "__UNCLASSIFIED_FORWARD_COLUMN__";

    public static IReadOnlySet<string> KnownCourseColumns { get; } =
        new HashSet<string>(
            [
                "MaKH",
                "MaCSDT",
                "MaSoGTVT",
                "TenKH",
                "HangGPLX",
                "HangDT",
                "SoQD_KhaiGiang",
                "NgayQD_KhaiGiang",
                "NgayKG",
                "NgayBG",
                "MucTieuDT",
                "NgayThi",
                "NgaySH",
                "TongSoHV",
                "SoHVTotNghiep",
                "SoHVDuocCapGPLX",
                "ThoiGianDT",
                "SoNgayOnKT",
                "SoNgayThucHoc",
                "SoNgayNghiLe",
                "TongSoNgay",
                "GhiChu",
                "TrangThai",
                "NguoiTao",
                "NguoiSua",
                "NgayTao",
                "NgaySua",
                "TT_Xuly",
                "HTDaoTao",
            ],
            StringComparer.Ordinal);

    private static readonly HashSet<string> PhotoEvidenceColumns =
        new(StringComparer.Ordinal)
        {
            "DuongDanAnh",
            "ChatLuongAnh",
            "NgayThuNhanAnh",
            "NguoiThuNhanAnh",
        };

    private static readonly HashSet<string> AllowedPhotoEventColumns =
        new(PhotoEvidenceColumns.Append("TT_XuLy"), StringComparer.Ordinal);

    private static readonly HashSet<string> ProjectionOnlyTables =
        new(StringComparer.Ordinal)
        {
            "dbo.GiaoVien",
            "dbo.XeTap",
            "dbo.KhoaHoc_GiaoVien",
            "dbo.KhoaHoc_XeTap",
        };

    public static string Classify(
        string tableName,
        string operation,
        IReadOnlyCollection<string> changedColumns)
    {
        ArgumentNullException.ThrowIfNull(changedColumns);
        if (ProjectionOnlyTables.Contains(tableName) &&
            operation is "I" or "U" or "D")
        {
            return Rt03ChangeTrackingClassifications.NoMappedChange;
        }

        if (string.Equals(tableName, "dbo.KhoaHoc", StringComparison.Ordinal))
        {
            if (changedColumns.Contains(ForwardColumnSentinel, StringComparer.Ordinal) ||
                changedColumns.Any(column => !KnownCourseColumns.Contains(column)))
            {
                return Rt03ChangeTrackingClassifications.UnclassifiedForwardColumn;
            }

            if (string.Equals(operation, "I", StringComparison.Ordinal) &&
                changedColumns.Count == 0)
            {
                return Rt03ChangeTrackingClassifications.KhoaHocSourceInsert;
            }

            if (string.Equals(operation, "U", StringComparison.Ordinal) &&
                changedColumns.Count > 0)
            {
                return Rt03ChangeTrackingClassifications.KhoaHocSourceUpdate;
            }

            return Rt03ChangeTrackingClassifications.UnknownUnsafe;
        }

        if (!string.Equals(tableName, "dbo.NguoiLX_HoSo", StringComparison.Ordinal) ||
            !string.Equals(operation, "U", StringComparison.Ordinal) ||
            changedColumns.Count == 0 ||
            changedColumns.Any(column => !AllowedPhotoEventColumns.Contains(column)))
        {
            return Rt03ChangeTrackingClassifications.UnknownUnsafe;
        }

        return changedColumns.Any(PhotoEvidenceColumns.Contains)
            ? Rt03ChangeTrackingClassifications.MultiFieldPhotoDrift
            : Rt03ChangeTrackingClassifications.NoMappedChange;
    }
}
