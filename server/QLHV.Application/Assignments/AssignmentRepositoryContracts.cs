using QLHV.Shared.Paging;

namespace QLHV.Application.Assignments;

public interface IAssignmentRepository
{
    Task<PagedResult<SourceTeacherItem>> SearchTeachersAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken);

    Task<PagedResult<SourceVehicleItem>> SearchVehiclesAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken);

    Task<PagedResult<CourseItem>> SearchCoursesAsync(
        CourseSearchRequest request,
        CancellationToken cancellationToken);

    Task<PagedResult<DossierReceiverItem>> SearchDossierReceiversAsync(
        CatalogSearchRequest request,
        CancellationToken cancellationToken);

    Task<DossierReceiverItem> CreateDossierReceiverAsync(
        DossierReceiverWrite write,
        CancellationToken cancellationToken);

    Task<DossierReceiverItem> UpdateDossierReceiverAsync(
        long id,
        DossierReceiverWrite write,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    Task<DossierReceiverItem> InactivateDossierReceiverAsync(
        long id,
        string actor,
        string reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    Task SoftDeleteDossierReceiverAsync(
        long id,
        string actor,
        string reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    Task<DossierReceiverHistoryResult> GetDossierReceiverHistoryAsync(
        long id,
        CancellationToken cancellationToken);

    Task<CourseAssignmentDetail> GetCourseDetailAsync(
        long courseId,
        CourseDetailRequest request,
        CancellationToken cancellationToken);

    Task<TrainingGroupItem> CreateGroupAsync(
        long courseId,
        TrainingGroupWrite write,
        CancellationToken cancellationToken);

    Task<TrainingGroupItem> UpdateGroupAsync(
        long courseId,
        long groupId,
        TrainingGroupWrite write,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    Task<TrainingGroupItem> InactivateGroupAsync(
        long courseId,
        long groupId,
        string actor,
        string reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken);

    Task<AssignmentMutationPlan> BuildAssignmentPlanAsync(
        AssignmentPreviewRequest request,
        CancellationToken cancellationToken);

    Task<GroupDefaultsMutationPlan> BuildGroupDefaultsPlanAsync(
        long groupId,
        GroupDefaultsPreviewRequest request,
        CancellationToken cancellationToken);

    Task<AssignmentConfirmResult> ConfirmAssignmentPlanAsync(
        AssignmentMutationPlan plan,
        string actor,
        string reason,
        string operationId,
        string previewToken,
        string idempotencyKey,
        string planFingerprint,
        CancellationToken cancellationToken);

    Task<AssignmentConfirmResult> ConfirmGroupDefaultsPlanAsync(
        GroupDefaultsMutationPlan plan,
        string actor,
        string reason,
        string operationId,
        string previewToken,
        string idempotencyKey,
        string planFingerprint,
        CancellationToken cancellationToken);

    Task<AssignmentConfirmReplay?> TryReplayAssignmentConfirmAsync(
        string kind,
        long? scopeId,
        string actor,
        string idempotencyKey,
        string previewToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentHistoryItem>> GetStudentHistoryAsync(
        long hocVienId,
        CancellationToken cancellationToken);

    Task<PagedResult<AuditHistoryItem>> GetCourseHistoryAsync(
        long courseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AssignmentExportData> GetExportDataAsync(
        long courseId,
        CancellationToken cancellationToken);

    Task<AssignmentImportPlan> BuildImportPlanAsync(
        long courseId,
        string sourceProfileCode,
        string fileName,
        string fileSha256,
        IReadOnlyList<ParsedAssignmentImportRow> rows,
        CancellationToken cancellationToken);

    Task<AssignmentImportConfirmResult> ConfirmImportPlanAsync(
        AssignmentImportPlan plan,
        string actor,
        string reason,
        string idempotencyKey,
        string operationId,
        string previewToken,
        CancellationToken cancellationToken);

    Task<AssignmentImportConfirmResult?> TryReplayImportConfirmAsync(
        long courseId,
        string actor,
        string idempotencyKey,
        string previewToken,
        CancellationToken cancellationToken);

    Task<AssignmentExportData> GetImportResultAsync(
        long courseId,
        long sessionId,
        CancellationToken cancellationToken);
}

public sealed record DossierReceiverWrite(
    string Code,
    string FullName,
    string FullNameSearch,
    DateOnly? DateOfBirth,
    string? CitizenId,
    string Status,
    string? Note,
    string Actor,
    string Reason);

public sealed record TrainingGroupWrite(
    string Code,
    string Name,
    int DisplayOrder,
    long? DefaultTeacherId,
    long? DefaultTrainingVehicleId,
    long? DefaultFigure10VehicleId,
    string? Note,
    string Actor,
    string Reason);

public sealed record AssignmentSnapshot(
    long? GroupId,
    long? DossierReceiverId,
    long? ClassTeacherId,
    long? TrainingVehicleId,
    long? Figure10VehicleId,
    bool OverrideClassTeacher,
    bool OverrideTrainingVehicle,
    bool OverrideFigure10Vehicle)
{
    public bool HasAnyValue =>
        GroupId.HasValue || DossierReceiverId.HasValue || ClassTeacherId.HasValue ||
        TrainingVehicleId.HasValue || Figure10VehicleId.HasValue;

    public AssignmentStateDto ToDto() => new(
        GroupId,
        DossierReceiverId,
        ClassTeacherId,
        TrainingVehicleId,
        Figure10VehicleId,
        OverrideClassTeacher,
        OverrideTrainingVehicle,
        OverrideFigure10Vehicle);
}

public sealed record AssignmentMutationTarget(
    long HocVienId,
    string RegistrationCode,
    string LearnerName,
    string CourseCode,
    string SourceProfileCode,
    byte[] LearnerRowVersion,
    long? CurrentAssignmentId,
    byte[]? CurrentAssignmentRowVersion,
    AssignmentSnapshot? Before,
    AssignmentSnapshot? After,
    string Status,
    IReadOnlyList<string> Messages,
    SealedGroupDefaults? GroupDefaults = null);

public sealed record SealedGroupDefaults(
    long GroupId,
    long CourseId,
    long? ClassTeacherId,
    long? TrainingVehicleId,
    long? Figure10VehicleId,
    string Status,
    byte[] RowVersion);

public sealed record AssignmentMutationPlan(
    long CourseId,
    string CourseCode,
    string SourceProfileCode,
    byte[] CourseRowVersion,
    string Operation,
    string AssignmentSource,
    bool RequiresBulkPermission,
    IReadOnlyList<AssignmentMutationTarget> Targets,
    IReadOnlyList<string> Warnings);

public sealed record AssignmentConfirmReplay(
    AssignmentConfirmResult Result,
    bool RequiresBulkPermission);

public sealed record GroupDefaultsMutationPlan(
    long GroupId,
    long CourseId,
    string CourseCode,
    string SourceProfileCode,
    byte[] CourseRowVersion,
    byte[] GroupRowVersion,
    string Mode,
    long? CurrentDefaultTeacherId,
    long? CurrentDefaultTrainingVehicleId,
    long? CurrentDefaultFigure10VehicleId,
    long? DefaultTeacherId,
    long? DefaultTrainingVehicleId,
    long? DefaultFigure10VehicleId,
    bool RequiresBulkPermission,
    IReadOnlyList<AssignmentMutationTarget> Targets,
    IReadOnlyList<string> Warnings);

public sealed record AssignmentExportRow(
    long HocVienId,
    string SourceProfileCode,
    string RegistrationCode,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? CitizenId,
    string? PermanentAddress,
    string? TrainingClass,
    string? TrainingClassCode,
    string? ExistingLicenseNumber,
    string? ExistingLicenseClass,
    string? DossierReceiverName,
    string? CourseName,
    string CourseCode,
    string? ClassTeacherName,
    string? TrainingVehiclePlate,
    string? Figure10VehiclePlate,
    string? DossierReceiverCode,
    string? GroupCode,
    string? ClassTeacherCode,
    long? AssignmentId,
    string? AssignmentRowVersion);

public sealed record AssignmentExportData(
    long CourseId,
    string CourseCode,
    string SourceProfileCode,
    IReadOnlyList<AssignmentExportRow> Rows,
    AssignmentLookups Lookups,
    IReadOnlyList<TrainingGroupItem> Groups);

public sealed record ParsedAssignmentImportRow(
    int RowNumber,
    string RegistrationCode,
    string CourseCode,
    string? GroupCode,
    string? DossierReceiverCode,
    string? ClassTeacherCode,
    string? TrainingVehiclePlate,
    string? Figure10VehiclePlate,
    long? HocVienId,
    string? AssignmentRowVersion,
    string GroupAction,
    string DossierReceiverAction,
    string ClassTeacherAction,
    string TrainingVehicleAction,
    string Figure10VehicleAction,
    IReadOnlyList<string> ValidationMessages);

public sealed record AssignmentImportPlanRow(
    int RowNumber,
    string RegistrationCode,
    string Status,
    IReadOnlyList<string> Messages,
    AssignmentMutationTarget? Target);

public sealed record AssignmentImportPlan(
    long CourseId,
    string CourseCode,
    string SourceProfileCode,
    byte[] CourseRowVersion,
    string FileName,
    string FileSha256,
    IReadOnlyList<AssignmentImportPlanRow> Rows);
