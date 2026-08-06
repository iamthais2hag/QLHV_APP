using QLHV.Application.Runtime;

namespace QLHV.Application.CourseCompletion;

public sealed class CourseCompletionService : ICourseCompletionService
{
    private readonly ICourseCompletionRepository _repository;
    private readonly CourseCompletionCanonicalSnapshotBuilder _builder;
    private readonly CourseCompletionPreviewStore _previews;
    private readonly ITimeAuthorityService _timeAuthority;

    public CourseCompletionService(
        ICourseCompletionRepository repository,
        CourseCompletionCanonicalSnapshotBuilder builder,
        CourseCompletionPreviewStore previews,
        ITimeAuthorityService timeAuthority)
    {
        _repository = repository;
        _builder = builder;
        _previews = previews;
        _timeAuthority = timeAuthority;
    }

    public async Task<CourseCompletionStatusResult> GetStatusAsync(long courseId, CancellationToken cancellationToken)
    {
        RequireCourseId(courseId);
        var marker = await _repository.ReadMarkerAsync(courseId, cancellationToken);
        if (marker is null)
        {
            var identity = await _repository.ReadCourseIdentityAsync(courseId, cancellationToken);
            return new(CourseCompletionCodes.NotCompleted, courseId,
                identity.SourceProfileCode, identity.SourceCourseKey,
                null, null, null, null, null, null, null, []);
        }

        var scope = await _repository.ReadSourceScopeAsync(courseId, marker.SourceProfileCode, cancellationToken);
        var current = _builder.Build(scope);
        if (string.Equals(marker.SourceSnapshotHash, current.SnapshotHash, StringComparison.Ordinal) &&
            marker.LearnerCount == current.LearnerCount)
            return Status(marker, CourseCompletionCodes.Completed, null, current.Warnings);

        var stored = marker.Learners.ToDictionary(x => x.ProtectedIdentity, StringComparer.Ordinal);
        var currentGroups = current.Learners
            .GroupBy(x => x.ProtectedIdentity, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var shared = stored.Keys.Intersect(currentGroups.Keys, StringComparer.Ordinal).ToArray();
        var drift = new CourseCompletionDriftDiagnostic(
            currentGroups.Keys.Except(stored.Keys, StringComparer.Ordinal).Count(),
            stored.Keys.Except(currentGroups.Keys, StringComparer.Ordinal).Count(),
            shared.Count(key => currentGroups[key].Length != 1 ||
                                !string.Equals(stored[key].CanonicalRowHash, currentGroups[key][0].CanonicalRowHash, StringComparison.Ordinal)),
            shared.Count(key => currentGroups[key].Length != 1 ||
                                stored[key].Status != currentGroups[key][0].Status ||
                                stored[key].Classification != currentGroups[key][0].Classification ||
                                stored[key].ResultCompleteness != currentGroups[key][0].ResultCompleteness));
        return Status(marker, CourseCompletionCodes.CorrectionRequired, drift, current.Warnings);
    }

    public async Task<CourseCompletionPreviewResult> PreviewAsync(
        long courseId, CourseCompletionPreviewRequest request, string actor, CancellationToken cancellationToken)
    {
        RequireCourseId(courseId);
        actor = Required(actor, 100, "Actor");
        request ??= new CourseCompletionPreviewRequest();
        var scope = await _repository.ReadSourceScopeAsync(courseId, NormalizeProfile(request.SourceProfileCode), cancellationToken);
        var snapshot = _builder.Build(scope);
        var sealedPreview = new SealedCourseCompletionPreview(
            actor, courseId, snapshot.SourceProfileCode, snapshot.SourceCourseKey,
            snapshot.ContractVersion, snapshot.SnapshotHash, snapshot.LearnerCount,
            snapshot.Blockers, snapshot.Warnings, snapshot);
        var token = _previews.Put(sealedPreview);
        return new CourseCompletionPreviewResult(
            token.Token, token.ExpiresAtUtc,
            snapshot.CanConfirm ? CourseCompletionCodes.Ready : snapshot.Blockers.FirstOrDefault() ?? CourseCompletionCodes.Blocked,
            snapshot.CanConfirm, snapshot.ContractVersion, snapshot.SourceProfileCode,
            snapshot.SourceCourseKey, snapshot.SnapshotHash, snapshot.LearnerCount,
            snapshot.PassedCount, snapshot.FailedCount, snapshot.DownstreamCount,
            snapshot.Blockers, snapshot.Warnings);
    }

    public async Task<CourseCompletionConfirmResult> ConfirmAsync(
        long courseId, CourseCompletionConfirmRequest request, string actor, CancellationToken cancellationToken)
    {
        RequireCourseId(courseId);
        ArgumentNullException.ThrowIfNull(request);
        actor = Required(actor, 100, "Actor");
        var token = Required(request.PreviewToken, 128, "PreviewToken");
        var idempotency = Required(request.IdempotencyKey, 100, "IdempotencyKey");
        var reason = Required(request.Reason, 500, "Reason");
        if (request.CompletionBusinessDate is null)
            throw new CourseCompletionDomainException(CourseCompletionCodes.Blocked, "Ngày hoàn thành nghiệp vụ là bắt buộc.", 400);

        var preview = _previews.Get(token, actor, courseId);
        if (preview.Blockers.Count > 0)
            throw new CourseCompletionDomainException(preview.Blockers[0], "Preview còn điều kiện chặn và không thể xác nhận.", 409);

        var time = await _timeAuthority.GetWriteAuthorizationAsync(cancellationToken);
        if (!TimeAuthorityPolicy.IsMutationAllowed(time))
            throw new CourseCompletionDomainException(CourseCompletionCodes.TimeAuthorityBlocked, "SQL database clock không sẵn sàng; thao tác ghi bị chặn.", 503);

        var idempotencyHash = CourseCompletionCanonicalSnapshotBuilder.Sha256($"{actor.ToUpperInvariant()}|{idempotency}");
        var fingerprint = CourseCompletionCanonicalSnapshotBuilder.Sha256(string.Join("|",
            courseId, preview.SourceProfileCode, preview.SourceCourseKey, preview.SnapshotHash,
            request.CompletionBusinessDate.Value.ToString("yyyy-MM-dd"), reason));
        return await _repository.ConfirmAsync(new CourseCompletionConfirmCommand(
            preview, actor, idempotencyHash, fingerprint, request.CompletionBusinessDate.Value,
            reason, Guid.NewGuid()), cancellationToken);
    }

    private static CourseCompletionStatusResult Status(
        CourseCompletionStoredMarker marker, string status, CourseCompletionDriftDiagnostic? drift,
        IReadOnlyList<string> warnings) => new(
            status, marker.KhoaHocId, marker.SourceProfileCode, marker.SourceCourseKey,
            marker.CompletionBusinessDate, marker.CompletedAtUtc, marker.CompletedBy,
            marker.LearnerCount, marker.ContractVersion, marker.SourceSnapshotHash, drift, warnings);

    private static void RequireCourseId(long value)
    {
        if (value <= 0) throw new CourseCompletionDomainException(CourseCompletionCodes.CourseNotFound, "KhoaHocId không hợp lệ.", 400);
    }

    private static string Required(string? value, int maxLength, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
            throw new CourseCompletionDomainException(CourseCompletionCodes.Blocked, $"{name} là bắt buộc và không được vượt quá {maxLength} ký tự.", 400);
        return normalized;
    }

    private static string? NormalizeProfile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized is not ("CSDT_OTO" or "CSDT_MOTO"))
            throw new CourseCompletionDomainException(CourseCompletionCodes.AmbiguousIdentity, "SourceProfileCode không thuộc allowlist.", 400);
        return normalized;
    }
}
