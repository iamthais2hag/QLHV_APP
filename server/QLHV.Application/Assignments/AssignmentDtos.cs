using QLHV.Shared.Paging;

namespace QLHV.Application.Assignments;

public class CatalogSearchRequest
{
    public string? Keyword { get; set; }
    public string? SourceProfileCode { get; set; }
    public string? TrangThai { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public CatalogSearchRequest Normalize() => new()
    {
        Keyword = AssignmentRules.NormalizeOptional(Keyword, 255),
        SourceProfileCode = AssignmentRules.NormalizeProfile(SourceProfileCode, required: false),
        TrangThai = AssignmentRules.NormalizeOptional(TrangThai, 50)?.ToUpperInvariant(),
        Page = Math.Max(1, Page),
        PageSize = Math.Clamp(PageSize <= 0 ? 25 : PageSize, 1, 200),
    };
}

public sealed class CourseSearchRequest : CatalogSearchRequest
{
    public string? MaKhoa { get; set; }
    public string? TenKhoa { get; set; }
    public string? HangDaoTao { get; set; }
    public string? LoaiDaoTao { get; set; }
    public DateOnly? TuNgay { get; set; }
    public DateOnly? DenNgay { get; set; }
}

public sealed record SourceTeacherItem(
    long GiaoVienId,
    string SourceProfileCode,
    string MaGv,
    string HoTen,
    DateOnly? NgaySinh,
    string? SoCccd,
    string? HangDaoTao,
    string TrangThai,
    bool IsActive,
    int CourseUsageCount,
    int StudentUsageCount,
    bool IsManualReview);

public sealed record SourceVehicleItem(
    long XeTapId,
    string SourceProfileCode,
    string MaXe,
    string BienSoXe,
    string? SoKhung,
    string? SoMay,
    string? HangXe,
    string? LoaiXe,
    string? HangDaoTao,
    string TrangThai,
    bool IsActive,
    int CourseUsageCount,
    int GroupUsageCount,
    int StudentUsageCount,
    bool IsManualReview);

public sealed record CourseItem(
    long KhoaHocId,
    string SourceProfileCode,
    string MaKhoa,
    string? TenKhoa,
    string? HangDaoTao,
    string? LoaiDaoTao,
    DateOnly? NgayKhaiGiang,
    DateOnly? NgayBeGiang,
    string? SoQuyetDinh,
    string TrangThai,
    bool IsActive,
    int LearnerCount,
    int UnassignedCount,
    int ManualReviewCount,
    string? RowVersion = null);

public sealed record DossierReceiverItem(
    long GiaoVienHsId,
    string MaGiaoVienHs,
    string HoTen,
    DateOnly? NgaySinh,
    string? SoCccd,
    string TrangThai,
    bool IsDeleted,
    int ReferenceCount,
    string RowVersion,
    DateTime? UpdatedAtUtc,
    string? UpdatedBy,
    string? GhiChu = null);

public sealed class SaveDossierReceiverRequest
{
    public string MaGiaoVienHs { get; set; } = string.Empty;
    public string HoTen { get; set; } = string.Empty;
    public DateOnly? NgaySinh { get; set; }
    public string? SoCccd { get; set; }
    public string TrangThai { get; set; } = "ACTIVE";
    public string? GhiChu { get; set; }
    public string? RowVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class RowVersionCommand
{
    public string RowVersion { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed record AuditHistoryItem(
    DateTime OccurredAtUtc,
    string Actor,
    string Action,
    string Reason,
    string? EntityLabel = null);

public sealed record DossierReceiverHistoryResult(
    int ReferenceCount,
    IReadOnlyList<AuditHistoryItem> Items);

public sealed record LookupRef(
    long Id,
    string Code,
    string Label,
    bool IsActive,
    bool IsManualReview = false,
    string? SourceProfileCode = null);

public sealed record TrainingGroupItem(
    long GroupId,
    string MaNhom,
    string TenNhom,
    int ThuTu,
    string TrangThai,
    bool IsActive,
    LookupRef? DefaultClassTeacher,
    LookupRef? DefaultTrainingVehicle,
    LookupRef? DefaultFigure10Vehicle,
    int StudentCount,
    string RowVersion);

public sealed class SaveTrainingGroupRequest
{
    public string MaNhom { get; set; } = string.Empty;
    public string TenNhom { get; set; } = string.Empty;
    public int ThuTu { get; set; }
    public long? DefaultClassTeacherId { get; set; }
    public long? DefaultTrainingVehicleId { get; set; }
    public long? DefaultFigure10VehicleId { get; set; }
    public string? GhiChu { get; set; }
    public string? RowVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record StudentAssignmentItem(
    long HocVienId,
    string MaDangKy,
    string HoTen,
    DateOnly? NgaySinh,
    string MaKhoa,
    string SourceProfileCode,
    string? HangHoc,
    long? GroupId,
    string? GroupCode,
    LookupRef? DossierReceiver,
    LookupRef? ClassTeacher,
    LookupRef? TrainingVehicle,
    LookupRef? Figure10Vehicle,
    bool OverrideClassTeacher,
    bool OverrideTrainingVehicle,
    bool OverrideFigure10Vehicle,
    string? AssignmentRowVersion,
    string AssignmentStatus,
    IReadOnlyList<string> Warnings);

public sealed record AssignmentLookups(
    IReadOnlyList<LookupRef> DossierReceivers,
    IReadOnlyList<LookupRef> Teachers,
    IReadOnlyList<LookupRef> Vehicles);

public sealed record CourseAssignmentSummary(
    int LearnerCount,
    int AssignedCount,
    int UnassignedCount,
    int ManualReviewCount);

public sealed record CourseAssignmentDetail(
    CourseItem Course,
    PagedResult<StudentAssignmentItem> Students,
    IReadOnlyList<TrainingGroupItem> Groups,
    AssignmentLookups Lookups,
    CourseAssignmentSummary Summary);

public sealed class CourseDetailRequest
{
    public string? StudentKeyword { get; set; }
    public long? GroupId { get; set; }
    public bool UnassignedOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public static class AssignmentAction
{
    public const string Keep = "KEEP";
    public const string Set = "SET";
    public const string Clear = "CLEAR";
    public const string Inherit = "INHERIT";

    public static bool IsValid(string? value, bool inheritAllowed) =>
        string.Equals(value, Keep, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Set, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Clear, StringComparison.OrdinalIgnoreCase) ||
        (inheritAllowed && string.Equals(value, Inherit, StringComparison.OrdinalIgnoreCase));
}

public static class AssignmentOperation
{
    public const string PutInGroup = "PUT_IN_GROUP";
    public const string BulkAssign = "BULK_ASSIGN";
    public const string StudentOverride = "STUDENT_OVERRIDE";
    public const string ClearAssignment = "CLEAR_ASSIGNMENT";
}

public static class GroupPropagationMode
{
    public const string UnoverriddenOnly = "UNOVERRIDDEN_ONLY";
    public const string ReplaceAll = "REPLACE_ALL";
    public const string NoCurrentChange = "NO_CURRENT_CHANGE";
}

public sealed class FieldActionRequest
{
    public string Action { get; set; } = AssignmentAction.Keep;
    public long? Id { get; set; }
}

public sealed class AssignmentFieldsRequest
{
    public FieldActionRequest? DossierReceiver { get; set; }
    public FieldActionRequest? ClassTeacher { get; set; }
    public FieldActionRequest? TrainingVehicle { get; set; }
    public FieldActionRequest? Figure10Vehicle { get; set; }
}

public sealed class AssignmentSelectionFilter
{
    public string? Keyword { get; set; }
    public long? GroupId { get; set; }
    public bool UnassignedOnly { get; set; }
}

public sealed class AssignmentSelectionRequest
{
    public string Mode { get; set; } = "IDS";
    public IReadOnlyList<long> HocVienIds { get; set; } = Array.Empty<long>();
    public AssignmentSelectionFilter? Filter { get; set; }
}

public sealed class AssignmentPreviewRequest
{
    public long KhoaHocId { get; set; }
    public string SourceProfileCode { get; set; } = string.Empty;
    public AssignmentSelectionRequest Selection { get; set; } = new();
    public string Operation { get; set; } = string.Empty;
    public long? GroupId { get; set; }
    public AssignmentFieldsRequest? Fields { get; set; }
    public IReadOnlyDictionary<string, string?> ExpectedRowVersions { get; set; }
        = new Dictionary<string, string?>();
    public string Reason { get; set; } = string.Empty;
}

public sealed record AssignmentStateDto(
    long? GroupId,
    long? DossierReceiverId,
    long? ClassTeacherId,
    long? TrainingVehicleId,
    long? Figure10VehicleId,
    bool OverrideClassTeacher,
    bool OverrideTrainingVehicle,
    bool OverrideFigure10Vehicle);

public sealed record AssignmentPreviewRow(
    long HocVienId,
    string MaDangKy,
    string HoTen,
    string Status,
    AssignmentStateDto? Before,
    AssignmentStateDto? After,
    IReadOnlyList<string> Messages);

public sealed record AssignmentPreviewResult(
    string PreviewToken,
    DateTime ExpiresAtUtc,
    string TargetFingerprint,
    int TotalTargets,
    int ReadyCount,
    int NoChangeCount,
    int ConflictCount,
    int InvalidCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AssignmentPreviewRow> Rows);

public sealed class ConfirmPreviewRequest
{
    public string PreviewToken { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed record AssignmentConfirmResult(
    string OperationId,
    int ChangedCount,
    int NoChangeCount,
    DateTime CompletedAtUtc);

public sealed class GroupDefaultsPreviewRequest
{
    public string RowVersion { get; set; } = string.Empty;
    public string Mode { get; set; } = GroupPropagationMode.NoCurrentChange;
    public long? DefaultClassTeacherId { get; set; }
    public long? DefaultTrainingVehicleId { get; set; }
    public long? DefaultFigure10VehicleId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record AssignmentHistoryItem(
    long AssignmentId,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsCurrent,
    string Source,
    string Actor,
    string Reason,
    LookupRef? Group,
    LookupRef? DossierReceiver,
    LookupRef? ClassTeacher,
    LookupRef? TrainingVehicle,
    LookupRef? Figure10Vehicle,
    bool OverrideClassTeacher,
    bool OverrideTrainingVehicle,
    bool OverrideFigure10Vehicle);

public sealed record AssignmentExportFile(string FileName, byte[] Content);

public sealed record ImportStatusCounts(
    int Ready,
    int NoChange,
    int NotFound,
    int Ambiguous,
    int InactiveReference,
    int Invalid,
    int Conflict);

public sealed record AssignmentImportPreviewRow(
    int RowNumber,
    string MaDangKy,
    string Status,
    IReadOnlyList<string> Messages);

public sealed record AssignmentImportPreviewResult(
    string PreviewToken,
    DateTime ExpiresAtUtc,
    string FileName,
    int TotalRows,
    ImportStatusCounts Counts,
    IReadOnlyList<AssignmentImportPreviewRow> Rows);

public sealed record AssignmentImportConfirmResult(
    long SessionId,
    string OperationId,
    int ChangedCount,
    int NoChangeCount,
    DateTime CompletedAtUtc);

public sealed class AssignmentDomainException : Exception
{
    public AssignmentDomainException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
