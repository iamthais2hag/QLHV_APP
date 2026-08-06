namespace QLHV.Application.Runtime;

public static class TimeSyncDiagnosticClassifications
{
    public const string CurrentSuccess = "CURRENT_SUCCESS";
    public const string VerifiedStaleAfterFreshSuccess =
        "VERIFIED_STALE_AFTER_FRESH_SUCCESS";
    public const string CurrentFailure = "CURRENT_FAILURE";
    public const string Unclassified = "UNCLASSIFIED";
}

public sealed record TimeSyncDiagnosticEvidence(
    int? LastSyncErrorCode,
    DateTimeOffset? LastSyncErrorAtUtc,
    DateTimeOffset? LastSuccessfulSyncUtc,
    string? Source,
    int? Stratum,
    IReadOnlyList<double> PhaseOffsetMilliseconds,
    IReadOnlyList<double> ApiSqlSkewMilliseconds,
    int ConsecutiveStableSamples);

public static class TimeSyncDiagnosticClassifier
{
    public const int RequiredStableSamples = 3;
    public const double DiagnosticSkewMilliseconds = 2_000;

    public static string Classify(
        TimeSyncDiagnosticEvidence evidence,
        string approvedSource)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.LastSyncErrorCode is null ||
            string.IsNullOrWhiteSpace(approvedSource))
        {
            return TimeSyncDiagnosticClassifications.Unclassified;
        }

        if (evidence.LastSyncErrorCode == 0)
        {
            return Stable(evidence, approvedSource)
                ? TimeSyncDiagnosticClassifications.CurrentSuccess
                : TimeSyncDiagnosticClassifications.Unclassified;
        }

        var freshSuccessAfterError =
            evidence.LastSyncErrorAtUtc is { } error &&
            evidence.LastSuccessfulSyncUtc is { } success &&
            success > error;
        return freshSuccessAfterError && Stable(evidence, approvedSource)
            ? TimeSyncDiagnosticClassifications.VerifiedStaleAfterFreshSuccess
            : TimeSyncDiagnosticClassifications.CurrentFailure;
    }

    private static bool Stable(
        TimeSyncDiagnosticEvidence evidence,
        string approvedSource)
        => string.Equals(
               evidence.Source?.Trim(),
               approvedSource.Trim(),
               StringComparison.OrdinalIgnoreCase) &&
           evidence.Stratum is > 0 and < 16 &&
           evidence.ConsecutiveStableSamples >= RequiredStableSamples &&
           evidence.PhaseOffsetMilliseconds.Count >= RequiredStableSamples &&
           evidence.ApiSqlSkewMilliseconds.Count >= RequiredStableSamples &&
           evidence.PhaseOffsetMilliseconds.All(
               value => Math.Abs(value) <= DiagnosticSkewMilliseconds) &&
           evidence.ApiSqlSkewMilliseconds.All(
               value => Math.Abs(value) <= DiagnosticSkewMilliseconds);
}
