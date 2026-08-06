namespace QLHV.Application.Sync.VehicleRealtime;

public static class VehicleRealtimeProfiles
{
    public const string Oto = "CSDT_OTO";
    public const string Moto = "CSDT_MOTO";

    public static IReadOnlyList<string> Ordered { get; } = [Oto, Moto];
}

public static class VehicleRealtimeSourceObjects
{
    public const string Schema = "dbo";
    public const string Table = "XeTap";
    public const string QualifiedTable = "dbo.XeTap";
    public const string PrimaryKey = "BienSoXe";
}

public static class VehicleRealtimeTargetDatabase
{
    public const string Name = "QLHV_APP";

    public static Guid ExpectedProductionDatabaseGuid { get; } =
        Guid.Parse("9C44B304-8A84-4D0D-9A82-19C7233FF6BB");
}

public sealed record VehicleRealtimeRoute(
    string SourceProfileCode,
    string SourceDatabaseName,
    Guid ExpectedProductionDatabaseGuid,
    string ExpectedMaCsdt);

public static class VehicleRealtimeRouteCatalog
{
    public static VehicleRealtimeRoute Oto { get; } = new(
        VehicleRealtimeProfiles.Oto,
        "CSDL_OTO",
        Guid.Parse("9A8B9BC1-18F3-4823-8123-3DC197A9D540"),
        "66029");

    public static VehicleRealtimeRoute Moto { get; } = new(
        VehicleRealtimeProfiles.Moto,
        "CSDL_MOTO",
        Guid.Parse("308BDDA8-80F3-4ACB-9836-578D80A9E98E"),
        "66030");

    public static IReadOnlyList<VehicleRealtimeRoute> Ordered { get; } =
        [Oto, Moto];

    public static VehicleRealtimeRoute GetRequired(string sourceProfileCode)
        => Ordered.SingleOrDefault(route =>
               string.Equals(
                   route.SourceProfileCode,
                   sourceProfileCode,
                   StringComparison.Ordinal))
           ?? throw new ArgumentException(
               "Vehicle realtime only accepts the exact CSDT_OTO or CSDT_MOTO live profile.",
               nameof(sourceProfileCode));
}

public static class VehicleRealtimeLifecycles
{
    public const string Active = "ACTIVE";
    public const string SourceInactive = "SOURCE_INACTIVE";
    public const string SourceMissing = "SOURCE_MISSING";
    public const string ManualReview = "MANUAL_REVIEW";

    public static IReadOnlySet<string> Allowed { get; } =
        new HashSet<string>(
            [Active, SourceInactive, SourceMissing, ManualReview],
            StringComparer.Ordinal);
}

public static class VehicleRealtimeActions
{
    public const string InsertSourceRow = "INSERT_SOURCE_ROW";
    public const string UpdateSourceOwnedFields = "UPDATE_SOURCE_OWNED_FIELDS";
    public const string MarkSourceInactive = "MARK_SOURCE_INACTIVE";
    public const string MarkSourceMissing = "MARK_SOURCE_MISSING";
    public const string ManualReview = "MANUAL_REVIEW";
    public const string NoChange = "NO_CHANGE";
}

public static class VehicleRealtimeReviewCodes
{
    public const string InvalidSourceIdentity = "INVALID_SOURCE_IDENTITY";
    public const string WrongSourcePartition = "WRONG_SOURCE_PARTITION";
    public const string SourceValueTooLong = "SOURCE_VALUE_TOO_LONG";
    public const string CrossProfilePlateCollision = "CROSS_PROFILE_PLATE_COLLISION";
    public const string PlateCollision = "PLATE_COLLISION";
    public const string RegistrationCollision = "REGISTRATION_COLLISION";
    public const string ChassisCollision = "CHASSIS_COLLISION";
    public const string EngineCollision = "ENGINE_COLLISION";
    public const string TargetSoftDeleted = "TARGET_SOFT_DELETED";
    public const string TargetManualHold = "TARGET_MANUAL_HOLD";
    public const string SourceInactiveWithAssignment = "SOURCE_INACTIVE_WITH_ASSIGNMENT";
    public const string SourceMissingWithAssignment = "SOURCE_MISSING_WITH_ASSIGNMENT";
    public const string TargetIdentityAmbiguous = "TARGET_IDENTITY_AMBIGUOUS";
}

public static class VehicleRealtimeCollisionFields
{
    public const string BienSoXe = "BienSoXe";
    public const string SoDK = "SoDK";
    public const string SoKhung = "SoKhung";
    public const string SoDongCo = "SoDongCo";
}

public static class VehicleRealtimeWarnings
{
    public const string ManagedImageCopyRequired = "MANAGED_IMAGE_COPY_REQUIRED";
}

public enum VehicleSourceChangeKind
{
    Upsert = 1,
    Delete = 2,
}

public sealed record VehicleSourceIdentity(
    string SourceProfileCode,
    string SourceBienSoXe)
{
    public static VehicleSourceIdentity Create(
        string sourceProfileCode,
        string sourceBienSoXe)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        var key = sourceBienSoXe?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 10)
        {
            throw new ArgumentException(
                "XeTap.BienSoXe must be the non-empty source varchar(10) primary key.",
                nameof(sourceBienSoXe));
        }

        return new VehicleSourceIdentity(route.SourceProfileCode, key);
    }
}

/// <summary>
/// Exact read model for CSDL_OTO/CSDL_MOTO dbo.XeTap. The source has no MaXe
/// column: BienSoXe is both its primary key and its proven business identity.
/// </summary>
public sealed record VehicleSourceRow
{
    public string BienSoXe { get; init; } = string.Empty;
    public string MaSoGTVT { get; init; } = string.Empty;
    public string MaCSDT { get; init; } = string.Empty;
    public string? SoDK { get; init; }
    public bool SoHuu { get; init; }
    public string? NhanHieu { get; init; }
    public string? LoaiXe { get; init; }
    public string? MacXe { get; init; }
    public string? HangXe { get; init; }
    public string? MauXe { get; init; }
    public string? SoDongCo { get; init; }
    public string? SoKhung { get; init; }
    public bool? GiayPhepXTL { get; init; }
    public string? SoGPXTL { get; init; }
    public string? CoQuanCapGPXTL { get; init; }
    public DateTime? NgayCapGPXTL { get; init; }
    public DateTime? NgayHHGPXTL { get; init; }
    public int? NamSX { get; init; }
    public bool? HeThongPP { get; init; }
    public DateTime? NgayCapGCNKD { get; init; }
    public DateTime? NgayHHGCNKD { get; init; }
    public bool? BaoHiem { get; init; }
    public string? TuyenDuong { get; init; }
    public string? ChatLuong { get; init; }
    public string? GhiChu { get; init; }
    public bool TrangThai { get; init; }
    public string? NguoiTao { get; init; }
    public string? NguoiSua { get; init; }
    public DateTime NgayTao { get; init; }
    public DateTime NgaySua { get; init; }
    public string? DuongDanAnh { get; init; }
    public string? HangGPLXXe { get; init; }
    public string? MaFileTiepNhanXML { get; init; }
    public DateTime? ThoiGianTiepNhanXML { get; init; }
}

/// <summary>
/// Only source-owned values that the vehicle realtime writer may insert/update.
/// QLHV-owned values deliberately do not appear in this record.
/// </summary>
public sealed record VehicleSourceWriteModel(
    VehicleSourceIdentity Identity,
    string NormalizedBienSoXe,
    string SourceRowHash,
    string SourceOfTruth,
    string MaCSDT,
    string MaSoGTVT,
    string BienSoXe,
    string? SoDK,
    string? NormalizedSoDK,
    bool SoHuu,
    bool XeCuaCoSoDaoTao,
    bool XeHopDong,
    string? NhanHieu,
    string? LoaiXe,
    string? MacXe,
    string? HangXe,
    string? HangGPLXXe,
    string? MauXe,
    int? NamSX,
    string? SoDongCo,
    string? NormalizedSoDongCo,
    string? SoKhung,
    string? NormalizedSoKhung,
    bool? GiayPhepXTL,
    string? SoGPXTL,
    string? CoQuanCapGPXTL,
    DateTime? NgayCapGPXTL,
    DateTime? NgayHetHanGPXTL,
    bool? HeThongPhanhPhu,
    bool? BaoHiem,
    string? TuyenDuong,
    string? ChatLuong,
    DateTime? NgayCapGCNKD,
    DateTime? NgayHetHanGCNKD,
    string? GhiChuV2,
    bool SourceTrangThai,
    string SourceLifecycle,
    string? SourceCreatedBy,
    string? SourceUpdatedBy,
    DateTime SourceCreatedAt,
    DateTime SourceUpdatedAt,
    string? SourceImagePathHash,
    string? SourceMaFileTiepNhanXml,
    DateTime? SourceThoiGianTiepNhanXml);

public sealed record VehicleMappingResult(
    VehicleSourceWriteModel? Model,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings)
{
    public bool IsSafe => Model is not null && Blockers.Count == 0;
}

public sealed record VehicleTargetSnapshot(
    long XeTapId,
    string? SourceProfileCode,
    string? SourceBienSoXe,
    string BienSoXe,
    string? NormalizedBienSoXe,
    string? SourceRowHash,
    bool? SourceTrangThai,
    string? SourceLifecycle,
    string? ManualReviewCode,
    string? SoDK,
    string? NormalizedSoDK,
    string? SoKhung,
    string? NormalizedSoKhung,
    string? SoDongCo,
    string? NormalizedSoDongCo,
    bool IsDeleted,
    bool HasActiveAssignments,
    byte[] RowVersion);

public sealed record VehicleRealtimePlan(
    VehicleSourceChangeKind ChangeKind,
    string SourceProfileCode,
    string SourceBienSoXe,
    long SourceCtVersion,
    string Action,
    string? Lifecycle,
    VehicleSourceWriteModel? Source,
    long? TargetXeTapId,
    byte[]? ExpectedTargetRowVersion,
    string? ReviewCode,
    string? CollisionField,
    long? ConflictingXeTapId)
{
    public bool MutatesVehicle =>
        Action is VehicleRealtimeActions.InsertSourceRow
            or VehicleRealtimeActions.UpdateSourceOwnedFields
            or VehicleRealtimeActions.MarkSourceInactive
            or VehicleRealtimeActions.MarkSourceMissing;

    public bool RequiresManualReview =>
        string.Equals(Action, VehicleRealtimeActions.ManualReview, StringComparison.Ordinal);
}

public static class VehicleRealtimeTargetOwnership
{
    public static IReadOnlySet<string> SourceOwnedColumns { get; } =
        new HashSet<string>(
                [
                    "BienSoXe", "SoDK", "SoHuu", "XeCuaCoSoDaoTao", "XeHopDong",
                    "NhanHieu", "LoaiXe", "MacXe", "HangXe", "HangGPLXXe", "MauXe",
                    "NamSX", "SoDongCo", "SoKhung", "GiayPhepXTL", "SoGPXTL",
                    "CoQuanCapGPXTL", "NgayCapGPXTL", "NgayHetHanGPXTL",
                    "HeThongPhanhPhu", "BaoHiem", "TuyenDuong", "ChatLuong",
                    "NgayCapGCNKD", "NgayHetHanGCNKD", "GhiChuV2", "SourceOfTruth",
                    "SourceProfileCode", "SourceBienSoXe", "NormalizedBienSoXe",
                    "NormalizedSoDK", "NormalizedSoKhung", "NormalizedSoDongCo",
                    "MaCSDT", "MaSoGTVT", "SourceRowHash", "SourceTrangThai",
                    "SourceLifecycle", "SourceCtVersion", "SourceLastSeenAt",
                    "SourceMissingSince", "ManualReviewCode", "ManualReviewAt",
                    "SourceCreatedBy", "SourceUpdatedBy", "SourceCreatedAt",
                    "SourceUpdatedAt", "SourceImagePathHash",
                    "SourceMaFileTiepNhanXml", "SourceThoiGianTiepNhanXml",
                ],
                StringComparer.Ordinal);

    public static IReadOnlySet<string> QlhvOwnedColumns { get; } =
        new HashSet<string>(
                [
                    "SoGCNKiemDinh", "AnhRelativePath", "GVQuanLyMa", "GVQuanLyTen",
                    "GhiChuNoiBo", "TrangThai", "CanhBaoDuLieu", "V2RowHash",
                    "LastSyncFromV2At", "LastSyncStatus", "LastSyncMessage",
                    "IsDeleted", "DeletedAt", "DeletedBy", "DeleteReason",
                    "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "RowVersion",
                ],
                StringComparer.Ordinal);
}
