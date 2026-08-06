namespace QLHV.Application.CourseCompletion;

public static class CourseCompletionContract
{
    public const string Version = "1.0";
    public const string Completed = "COMPLETED";
    public static readonly TimeSpan PreviewTtl = TimeSpan.FromMinutes(15);
}

public static class CourseCompletionCodes
{
    public const string Ready = "READY";
    public const string NotCompleted = "NOT_COMPLETED";
    public const string Completed = "COMPLETED";
    public const string NoChange = "NO_CHANGE";
    public const string CorrectionRequired = "CORRECTION_REQUIRED";
    public const string CourseNotFound = "COURSE_NOT_FOUND";
    public const string EmptyCourse = "EMPTY_COURSE";
    public const string StudentStatusInvalid = "STUDENT_STATUS_INVALID";
    public const string StudentResultIncomplete = "STUDENT_RESULT_INCOMPLETE";
    public const string DuplicateIdentity = "DUPLICATE_IDENTITY";
    public const string AmbiguousIdentity = "AMBIGUOUS_IDENTITY";
    public const string Conflict = "CONFLICT";
    public const string TimeAuthorityBlocked = "TIME_AUTHORITY_BLOCKED";
    public const string Blocked = "BLOCKED";
}

public sealed class CourseCompletionDomainException : Exception
{
    public CourseCompletionDomainException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed record CourseCompletionCourseSource(
    long KhoaHocId,
    string SourceProfileCode,
    string SourceCourseKey,
    string MaCsdt,
    string? MaSoGtvt,
    string? TrainingClass,
    string? TrainingForm,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool HasReportI,
    bool HasTeacher,
    bool HasVehicle,
    bool HasProgram);

public sealed record CourseCompletionLearnerSource(
    string? RegistrationCode,
    string? CourseKey,
    string? V2Status,
    string? V1Status,
    string? Conclusion,
    DateTime? TrainingStartedAt,
    DateTime? TrainingCompletedAt,
    string? TheoryResult,
    string? PracticeResult,
    string? TheoryScore,
    string? PracticeScore,
    string? FigurePracticeTime,
    string? RoadPracticeTime,
    string? FigureDistance,
    string? RoadDistance,
    bool HasReportII,
    bool HasExamLifecycle,
    bool HasLicense,
    bool IsV1Orphan = false);

public sealed record CourseCompletionSourceScope(
    CourseCompletionCourseSource Course,
    IReadOnlyList<CourseCompletionLearnerSource> Learners,
    IReadOnlyList<string> SourceDiagnostics);

public sealed record CourseCompletionLearnerSnapshot(
    string ProtectedIdentity,
    string SourceProfileCode,
    string SourceCourseKey,
    string LearnerCourseKey,
    string Status,
    string Classification,
    string ResultCompleteness,
    string DownstreamClassification,
    string CanonicalRowHash,
    IReadOnlyList<string> Blockers);

public sealed record CourseCompletionCanonicalSnapshot(
    string ContractVersion,
    string SourceProfileCode,
    string SourceCourseKey,
    string SnapshotHash,
    int LearnerCount,
    int PassedCount,
    int FailedCount,
    int DownstreamCount,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CourseCompletionLearnerSnapshot> Learners)
{
    public bool CanConfirm => Blockers.Count == 0;
}

public sealed class CourseCompletionPreviewRequest
{
    public string? SourceProfileCode { get; set; }
}

public sealed record CourseCompletionPreviewResult(
    string PreviewToken,
    DateTime ExpiresAtUtc,
    string Status,
    bool CanConfirm,
    string ContractVersion,
    string SourceProfileCode,
    string SourceCourseKey,
    string SourceSnapshotHash,
    int LearnerCount,
    int PassedCount,
    int FailedCount,
    int DownstreamCount,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed class CourseCompletionConfirmRequest
{
    public string PreviewToken { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateOnly? CompletionBusinessDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record CourseCompletionConfirmResult(
    Guid OperationId,
    long CourseCompletionId,
    string ResultCode,
    DateOnly CompletionBusinessDate,
    DateTime CompletedAtUtc,
    string CompletedBy,
    int LearnerCount,
    string ContractVersion,
    string SourceSnapshotHash);

public sealed record CourseCompletionStoredMarker(
    long CourseCompletionId,
    long KhoaHocId,
    string SourceProfileCode,
    string SourceCourseKey,
    string ContractVersion,
    DateOnly CompletionBusinessDate,
    string SourceSnapshotHash,
    int LearnerCount,
    DateTime CompletedAtUtc,
    string CompletedBy,
    string CompletionReason,
    IReadOnlyList<CourseCompletionStoredLearner> Learners);

public sealed record CourseCompletionStoredLearner(
    string ProtectedIdentity,
    string Status,
    string Classification,
    string ResultCompleteness,
    string CanonicalRowHash);

public sealed record CourseCompletionCourseIdentity(
    long KhoaHocId,
    string SourceProfileCode,
    string SourceCourseKey);

public sealed record CourseCompletionDriftDiagnostic(
    int AddedLearners,
    int MissingLearners,
    int ChangedLearners,
    int StatusOrResultChanges);

public sealed record CourseCompletionStatusResult(
    string Status,
    long KhoaHocId,
    string? SourceProfileCode,
    string? SourceCourseKey,
    DateOnly? CompletionBusinessDate,
    DateTime? CompletedAtUtc,
    string? CompletedBy,
    int? LearnerCount,
    string? ContractVersion,
    string? SourceSnapshotHash,
    CourseCompletionDriftDiagnostic? Drift,
    IReadOnlyList<string> Warnings);

public sealed record SealedCourseCompletionPreview(
    string Actor,
    long KhoaHocId,
    string SourceProfileCode,
    string SourceCourseKey,
    string ContractVersion,
    string SnapshotHash,
    int LearnerCount,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    CourseCompletionCanonicalSnapshot Snapshot);

public sealed record CourseCompletionConfirmCommand(
    SealedCourseCompletionPreview Preview,
    string Actor,
    string IdempotencyKeyHash,
    string RequestFingerprint,
    DateOnly CompletionBusinessDate,
    string Reason,
    Guid OperationId);

public interface ICourseCompletionRepository
{
    Task<CourseCompletionCourseIdentity> ReadCourseIdentityAsync(
        long courseId,
        CancellationToken cancellationToken);

    Task<CourseCompletionSourceScope> ReadSourceScopeAsync(
        long courseId,
        string? requiredProfile,
        CancellationToken cancellationToken);

    Task<CourseCompletionStoredMarker?> ReadMarkerAsync(
        long courseId,
        CancellationToken cancellationToken);

    Task<CourseCompletionConfirmResult> ConfirmAsync(
        CourseCompletionConfirmCommand command,
        CancellationToken cancellationToken);
}

public interface ICourseCompletionService
{
    Task<CourseCompletionStatusResult> GetStatusAsync(long courseId, CancellationToken cancellationToken);
    Task<CourseCompletionPreviewResult> PreviewAsync(long courseId, CourseCompletionPreviewRequest request, string actor, CancellationToken cancellationToken);
    Task<CourseCompletionConfirmResult> ConfirmAsync(long courseId, CourseCompletionConfirmRequest request, string actor, CancellationToken cancellationToken);
}
