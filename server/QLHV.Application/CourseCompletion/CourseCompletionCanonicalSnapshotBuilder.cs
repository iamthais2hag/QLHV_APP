using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.CourseCompletion;

public sealed class CourseCompletionCanonicalSnapshotBuilder
{
    public CourseCompletionCanonicalSnapshot Build(CourseCompletionSourceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var course = scope.Course;
        var blockers = new List<string>();
        var warnings = new List<string>(scope.SourceDiagnostics.Select(Normalize).Where(x => x.Length > 0));

        if (scope.Learners.Count == 0)
        {
            blockers.Add(CourseCompletionCodes.EmptyCourse);
        }

        if (!course.HasReportI) warnings.Add("REPORT_I_MISSING");
        if (!course.HasTeacher) warnings.Add("COURSE_TEACHER_MISSING");
        if (!course.HasVehicle) warnings.Add("COURSE_VEHICLE_MISSING");
        if (!course.HasProgram || string.IsNullOrWhiteSpace(course.TrainingForm) || string.IsNullOrWhiteSpace(course.TrainingClass))
            warnings.Add("COURSE_PROGRAM_INCOMPLETE");
        if (course.StartDate is null) warnings.Add("COURSE_START_DATE_MISSING");
        if (course.EndDate is null) warnings.Add("COURSE_END_DATE_MISSING");
        if (course.StartDate is { } start && course.EndDate is { } end && end < start)
            warnings.Add("COURSE_DATE_RANGE_INVALID");

        var duplicateKeys = scope.Learners
            .Select(row => Normalize(row.RegistrationCode))
            .Where(key => key.Length > 0)
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicateKeys.Count > 0) blockers.Add(CourseCompletionCodes.DuplicateIdentity);

        var learners = new List<CourseCompletionLearnerSnapshot>(scope.Learners.Count);
        foreach (var row in scope.Learners)
        {
            var learnerBlockers = new List<string>();
            var registrationCode = Normalize(row.RegistrationCode);
            var learnerCourseKey = Normalize(row.CourseKey);
            if (row.IsV1Orphan || registrationCode.Length == 0 || learnerCourseKey.Length == 0 ||
                !string.Equals(learnerCourseKey, course.SourceCourseKey, StringComparison.Ordinal))
            {
                learnerBlockers.Add(CourseCompletionCodes.AmbiguousIdentity);
            }
            if (duplicateKeys.Contains(registrationCode))
            {
                learnerBlockers.Add(CourseCompletionCodes.DuplicateIdentity);
            }

            var effectiveStatus = EffectiveStatus(row.V2Status, row.V1Status);
            string classification;
            string completeness;
            string downstream;
            if (effectiveStatus is "09" or "10")
            {
                classification = effectiveStatus == "09" ? "PASSED" : "FAILED";
                downstream = "TRAINING_RESULT_FINAL";
                completeness = IsTrainingResultComplete(row, course.TrainingClass)
                    ? "COMPLETE"
                    : "INCOMPLETE";
                if (completeness == "INCOMPLETE")
                    learnerBlockers.Add(CourseCompletionCodes.StudentResultIncomplete);
            }
            else if (TryStatusNumber(effectiveStatus, out var number) && number is >= 11 and <= 19)
            {
                classification = "DOWNSTREAM";
                completeness = "READ_ONLY_DOWNSTREAM";
                downstream = BuildDownstreamClassification(row);
            }
            else
            {
                classification = "UNCLASSIFIED";
                completeness = "NOT_APPLICABLE";
                downstream = effectiveStatus == "90" ? "MANUAL_REVIEW" : "NOT_FINAL";
                learnerBlockers.Add(CourseCompletionCodes.StudentStatusInvalid);
            }

            blockers.AddRange(learnerBlockers);
            var protectedIdentity = Sha256($"COURSE_COMPLETION_V1|{course.SourceProfileCode}|{registrationCode}");
            var canonicalRow = Join(
                protectedIdentity, course.SourceProfileCode, course.SourceCourseKey,
                learnerCourseKey, effectiveStatus, classification, completeness, downstream,
                Normalize(row.Conclusion), Date(row.TrainingStartedAt), Date(row.TrainingCompletedAt),
                Number(row.TheoryResult), Number(row.PracticeResult), Number(row.TheoryScore),
                Number(row.PracticeScore), Number(row.FigurePracticeTime), Number(row.RoadPracticeTime),
                Number(row.FigureDistance), Number(row.RoadDistance), Bool(row.HasReportII),
                Bool(row.HasExamLifecycle), Bool(row.HasLicense));
            learners.Add(new CourseCompletionLearnerSnapshot(
                protectedIdentity,
                course.SourceProfileCode,
                course.SourceCourseKey,
                learnerCourseKey,
                effectiveStatus,
                classification,
                completeness,
                downstream,
                Sha256(canonicalRow),
                learnerBlockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }

        var ordered = learners
            .OrderBy(row => row.ProtectedIdentity, StringComparer.Ordinal)
            .ThenBy(row => row.CanonicalRowHash, StringComparer.Ordinal)
            .ToArray();
        var canonicalHeader = Join(
            CourseCompletionContract.Version,
            course.SourceProfileCode,
            course.SourceCourseKey,
            Normalize(course.MaCsdt),
            Normalize(course.MaSoGtvt),
            Normalize(course.TrainingClass),
            Normalize(course.TrainingForm));
        var snapshotHash = Sha256(canonicalHeader + "\n" + string.Join("\n", ordered.Select(x => x.CanonicalRowHash)));
        var distinctBlockers = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var distinctWarnings = warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new CourseCompletionCanonicalSnapshot(
            CourseCompletionContract.Version,
            course.SourceProfileCode,
            course.SourceCourseKey,
            snapshotHash,
            ordered.Length,
            ordered.Count(x => x.Classification == "PASSED"),
            ordered.Count(x => x.Classification == "FAILED"),
            ordered.Count(x => x.Classification == "DOWNSTREAM"),
            distinctBlockers,
            distinctWarnings,
            ordered);
    }

    private static bool IsTrainingResultComplete(CourseCompletionLearnerSource row, string? trainingClass)
    {
        if (string.IsNullOrWhiteSpace(row.RegistrationCode) || string.IsNullOrWhiteSpace(row.CourseKey) ||
            string.IsNullOrWhiteSpace(row.Conclusion) || row.TrainingStartedAt is null ||
            row.TrainingCompletedAt is null || row.TrainingCompletedAt < row.TrainingStartedAt)
            return false;

        var normalizedClass = Normalize(trainingClass).ToUpperInvariant();
        var legacyReducedFields = normalizedClass.Contains('A') || normalizedClass == "B1M";
        if (legacyReducedFields) return true;

        return AllNumbers(
            row.TheoryResult, row.PracticeResult, row.TheoryScore, row.PracticeScore,
            row.FigurePracticeTime, row.RoadPracticeTime, row.FigureDistance, row.RoadDistance);
    }

    private static bool AllNumbers(params string?[] values) => values.All(value =>
        decimal.TryParse(Normalize(value), NumberStyles.Number | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out _));

    private static string EffectiveStatus(string? v2, string? v1)
    {
        var target = NormalizeStatus(v1);
        if (TryStatusNumber(target, out var targetNumber) && targetNumber is >= 11 and <= 19 or 90)
            return target;
        return NormalizeStatus(v2);
    }

    private static string BuildDownstreamClassification(CourseCompletionLearnerSource row)
    {
        if (row.HasLicense) return "LICENSE_ACTIVE";
        if (row.HasExamLifecycle) return "EXAM_ACTIVE";
        if (row.HasReportII) return "REPORT_II_ACTIVE";
        return "DOWNSTREAM_STATUS_ONLY";
    }

    private static bool TryStatusNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static string NormalizeStatus(string? value)
    {
        var normalized = Normalize(value);
        return int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("00", CultureInfo.InvariantCulture)
            : normalized.ToUpperInvariant();
    }

    public static string Normalize(string? value) => value?.TrimEnd().TrimStart() ?? string.Empty;
    private static string Date(DateTime? value) =>
        value?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? "~";
    private static string Number(string? value) => decimal.TryParse(Normalize(value), NumberStyles.Number | NumberStyles.AllowExponent,
        CultureInfo.InvariantCulture, out var number) ? number.ToString("G29", CultureInfo.InvariantCulture) : "~";
    private static string Bool(bool value) => value ? "1" : "0";
    private static string Join(params string[] values) => string.Join("|", values.Select(value => value.Length == 0 ? "~" : value));
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
