using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync.Rt03;

public static class Rt03CourseBusinessActions
{
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string NoChange = "NO_CHANGE";
}

public sealed record Rt03CourseTargetIdentity(
    long KhoaHocId,
    string? SourceProfileCode,
    string? SourceMaKhoaHoc,
    string? SourceHash,
    string MaKhoa,
    bool TrangThaiNguon,
    bool IsDeleted);

public sealed record Rt03CourseBusinessPlan(
    string Action,
    QlhvImportKhoaHocWriteModel Source,
    Rt03CourseTargetIdentity? Target);

public sealed record Rt03LearnerReplayIdentity(
    string SourceProfileCode,
    string SourceMaDK,
    string SourceHash,
    bool IsDeleted = false);

public sealed record Rt03LearnerReplayEvent(
    string TableName,
    string Operation,
    string SourceMaDK);

public enum Rt03LearnerReplayDisposition
{
    Blocked,
    Converged,
    IdempotentDeleteAlreadyAbsent,
}

public static class Rt03LearnerReplayRules
{
    public static bool AreAllConverged(
        string sourceProfileCode,
        IReadOnlyCollection<string> eventSourceKeys,
        IReadOnlyCollection<Rt03LearnerReplayIdentity> sourceRows,
        IReadOnlyCollection<Rt03LearnerReplayIdentity> targetRows)
    {
        ArgumentNullException.ThrowIfNull(eventSourceKeys);
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(targetRows);
        var keys = eventSourceKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keys.Length == 0)
        {
            return false;
        }

        foreach (var key in keys)
        {
            var sources = sourceRows.Where(row =>
                string.Equals(
                    row.SourceProfileCode,
                    sourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(
                    row.SourceMaDK.Trim(),
                    key,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var targets = targetRows.Where(row =>
                !row.IsDeleted &&
                string.Equals(
                    row.SourceProfileCode,
                    sourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(
                    row.SourceMaDK.Trim(),
                    key,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (sources.Length != 1 ||
                targets.Length != 1 ||
                !string.Equals(
                    sources[0].SourceHash,
                    targets[0].SourceHash,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static Rt03LearnerReplayDisposition ClassifyConvergedReplay(
        string sourceProfileCode,
        IReadOnlyCollection<Rt03LearnerReplayEvent> events,
        IReadOnlyCollection<Rt03LearnerReplayIdentity> sourceRows,
        IReadOnlyCollection<Rt03LearnerReplayIdentity> targetRows)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(targetRows);
        if (events.Count == 0 || events.Any(item =>
                string.IsNullOrWhiteSpace(item.SourceMaDK) ||
                string.IsNullOrWhiteSpace(item.TableName) ||
                string.IsNullOrWhiteSpace(item.Operation)))
        {
            return Rt03LearnerReplayDisposition.Blocked;
        }

        var eventGroups = events
            .GroupBy(item => item.SourceMaDK.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var foundIdempotentDelete = false;
        foreach (var eventGroup in eventGroups)
        {
            var sources = sourceRows.Where(row =>
                string.Equals(row.SourceProfileCode, sourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(row.SourceMaDK.Trim(), eventGroup.Key,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var targets = targetRows.Where(row =>
                string.Equals(row.SourceProfileCode, sourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(row.SourceMaDK.Trim(), eventGroup.Key,
                    StringComparison.OrdinalIgnoreCase)).ToArray();

            if (sources.Length == 0)
            {
                var tables = eventGroup
                    .Select(item => item.TableName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var exactDeletePair = eventGroup.Count() == 2 &&
                                      eventGroup.All(item => string.Equals(
                                          item.Operation, "D",
                                          StringComparison.OrdinalIgnoreCase)) &&
                                      tables.SetEquals(
                                      [
                                          "dbo.NguoiLX",
                                          "dbo.NguoiLX_HoSo",
                                      ]);
                if (!exactDeletePair || targets.Length != 0)
                {
                    return Rt03LearnerReplayDisposition.Blocked;
                }

                foundIdempotentDelete = true;
                continue;
            }

            var activeTargets = targets.Where(row => !row.IsDeleted).ToArray();
            if (sources.Length != 1 ||
                activeTargets.Length != 1 ||
                !string.Equals(sources[0].SourceHash,
                    activeTargets[0].SourceHash,
                    StringComparison.Ordinal))
            {
                return Rt03LearnerReplayDisposition.Blocked;
            }
        }

        return foundIdempotentDelete
            ? Rt03LearnerReplayDisposition.IdempotentDeleteAlreadyAbsent
            : Rt03LearnerReplayDisposition.Converged;
    }
}

/// <summary>
/// Pure fail-closed rules shared by the production processor and behavioral
/// tests. A course is globally identified only by its source profile and exact
/// source key; MaKhoa alone is never a cross-profile identity.
/// </summary>
public static class Rt03CourseBusinessRules
{
    public static void RequireStableSource(
        QlhvImportKhoaHocWriteModel planned,
        QlhvImportKhoaHocWriteModel? current)
    {
        ArgumentNullException.ThrowIfNull(planned);
        if (current is null ||
            !string.Equals(
                current.SourceProfileCode,
                planned.SourceProfileCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                current.SourceMaKhoaHoc,
                planned.SourceMaKhoaHoc,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                current.SourceHash,
                planned.SourceHash,
                StringComparison.Ordinal))
        {
            throw new Rt03SafetyException(
                Rt03Errors.SourceChangedDuringPlan,
                $"KhoaHoc {planned.SourceProfileCode}/{planned.SourceMaKhoaHoc} " +
                "changed or disappeared after plan construction.");
        }
    }

    public static void RequireQlhvOwnedFingerprintUnchanged(
        string beforeFingerprint,
        string afterFingerprint)
    {
        if (!string.Equals(
                beforeFingerprint,
                afterFingerprint,
                StringComparison.Ordinal))
        {
            throw new Rt03SafetyException(
                Rt03Errors.TargetDrift,
                "A QLHV-owned KhoaHoc field changed during convergence.");
        }
    }

    public static Rt03CourseBusinessPlan Plan(
        QlhvImportKhoaHocWriteModel source,
        IReadOnlyCollection<Rt03CourseTargetIdentity> exactIdentityMatches,
        IReadOnlyCollection<Rt03CourseTargetIdentity> sameMaKhoaMatches)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exactIdentityMatches);
        ArgumentNullException.ThrowIfNull(sameMaKhoaMatches);

        if (!Rt03Profiles.Ordered.Contains(source.SourceProfileCode, StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(source.SourceMaKhoaHoc) ||
            string.IsNullOrWhiteSpace(source.SourceHash) ||
            string.IsNullOrWhiteSpace(source.MaKhoa))
        {
            throw new Rt03SafetyException(
                Rt03Errors.UnsupportedDrift,
                "KhoaHoc source mapping does not contain an exact supported identity.");
        }

        if (exactIdentityMatches.Count > 1)
        {
            throw Ambiguous(source, "more than one exact target identity");
        }

        var exact = exactIdentityMatches.SingleOrDefault();
        var conflictingLegacyOrSameProfile = sameMaKhoaMatches
            .Where(candidate => exact is null || candidate.KhoaHocId != exact.KhoaHocId)
            .Where(candidate =>
                string.IsNullOrWhiteSpace(candidate.SourceProfileCode) ||
                string.IsNullOrWhiteSpace(candidate.SourceMaKhoaHoc) ||
                string.Equals(
                    candidate.SourceProfileCode,
                    source.SourceProfileCode,
                    StringComparison.Ordinal))
            .ToArray();
        if (conflictingLegacyOrSameProfile.Length > 0)
        {
            throw Ambiguous(
                source,
                "MaKhoa collides with a legacy/unpartitioned or same-profile target");
        }

        if (exact is null)
        {
            return new Rt03CourseBusinessPlan(
                Rt03CourseBusinessActions.Insert,
                source,
                null);
        }

        var action =
            !exact.IsDeleted &&
            string.Equals(exact.SourceHash, source.SourceHash, StringComparison.Ordinal)
                ? Rt03CourseBusinessActions.NoChange
                : Rt03CourseBusinessActions.Update;
        return new Rt03CourseBusinessPlan(action, source, exact);
    }

    public static Rt03CourseTargetIdentity RequireLearnerCourse(
        string sourceProfileCode,
        string sourceMaKhoaHoc,
        IReadOnlyCollection<Rt03CourseTargetIdentity> exactIdentityMatches)
    {
        ArgumentNullException.ThrowIfNull(exactIdentityMatches);
        var candidates = exactIdentityMatches
            .Where(candidate =>
                string.Equals(
                    candidate.SourceProfileCode,
                    sourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(
                    candidate.SourceMaKhoaHoc,
                    sourceMaKhoaHoc,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    candidate.MaKhoa,
                    sourceMaKhoaHoc,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (candidates.Length != 1 ||
            candidates[0].IsDeleted ||
            !candidates[0].TrangThaiNguon)
        {
            throw new Rt03SafetyException(
                Rt03Errors.LearnerCourseNotConvergent,
                $"Learner course identity {sourceProfileCode}/{sourceMaKhoaHoc} " +
                "must resolve to exactly one active converged target course.");
        }

        return candidates[0];
    }

    private static Rt03SafetyException Ambiguous(
        QlhvImportKhoaHocWriteModel source,
        string reason)
        => new(
            Rt03Errors.AmbiguousCourseIdentity,
            $"KhoaHoc identity {source.SourceProfileCode}/{source.SourceMaKhoaHoc} " +
            $"is ambiguous: {reason}.");
}
