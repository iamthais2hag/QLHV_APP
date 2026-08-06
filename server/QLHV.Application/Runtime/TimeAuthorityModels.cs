namespace QLHV.Application.Runtime;

/// <summary>
/// Contract 2.0 deliberately separates the authoritative SQL clock from
/// workstation and Windows Time diagnostics. Only SQL clock availability can
/// authorize or deny a write.
/// </summary>
public static class TimeHealthContract
{
    public const string Version = "2.0";
    public const string ApprovedPeer = "time.windows.com,0x9";
    public const string RunningServiceState = "Running";
}

public static class TimeHealthStatuses
{
    public const string Healthy = "HEALTHY";
    public const string Warning = "WARNING";
    public const string Blocked = "BLOCKED";
}

public static class TimeHealthReasonCodes
{
    public const string None = "NONE";
    public const string DatabaseUtcUnavailable = "DATABASE_UTC_UNAVAILABLE";
    public const string EvaluationUnavailable = "EVALUATION_UNAVAILABLE";
}

public sealed record TimeAuthorityObservation(
    DateTimeOffset ApiUtcAtQueryStart,
    DateTimeOffset ApiUtcAfterQuery,
    DateTimeOffset? DatabaseUtcNow,
    TimeSpan MonotonicQueryDuration,
    string ServerTimeZone,
    bool WindowsTimeRunning,
    string? ConfiguredPeer,
    string? CurrentSource,
    TimeSpan? TimeSinceLastGoodSync,
    double? NtpPhaseOffsetMilliseconds,
    int? LastSyncError)
{
    public TimeSpan? EffectivePollInterval { get; init; }
}

public sealed class TimeHealthDto
{
    public string TimeHealth { get; init; } = TimeHealthStatuses.Blocked;

    public string Health => TimeHealth;

    public string ReasonCode { get; init; } = TimeHealthReasonCodes.EvaluationUnavailable;

    public bool WritesAllowed { get; init; }

    public bool DatabaseClockAvailable { get; init; }

    public DateTimeOffset ServerUtcNow { get; init; }

    public DateTimeOffset? DatabaseUtcNow { get; init; }

    public double DatabaseUtcQueryMilliseconds { get; init; }

    /// <summary>Diagnostic only. This value never participates in write authorization.</summary>
    public double? ClockSkewMilliseconds { get; init; }

    public double MonotonicQueryMilliseconds { get; init; }

    public string TimeZone { get; init; } = string.Empty;

    public string DisplayTimeZone { get; init; } = "Asia/Ho_Chi_Minh";

    /// <summary>Diagnostic only.</summary>
    public string WindowsTimeServiceState { get; init; } = "Unknown";

    /// <summary>Diagnostic only.</summary>
    public string? ConfiguredPeer { get; init; }

    /// <summary>Diagnostic only.</summary>
    public string? CurrentSource { get; init; }

    /// <summary>Diagnostic only.</summary>
    public DateTimeOffset? LastSuccessfulSyncUtc { get; init; }

    /// <summary>Diagnostic only.</summary>
    public int? LastSyncError { get; init; }

    /// <summary>Diagnostic only.</summary>
    public double? PhaseOffsetMilliseconds { get; init; }

    /// <summary>Diagnostic only.</summary>
    public double? LastSuccessfulSyncAgeSeconds { get; init; }

    /// <summary>Diagnostic only.</summary>
    public double? EffectivePollIntervalSeconds { get; init; }

    public DateTimeOffset EvaluatedAtUtc { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public bool WindowsTimeRunning =>
        string.Equals(
            WindowsTimeServiceState,
            TimeHealthContract.RunningServiceState,
            StringComparison.Ordinal);
}

public sealed class TimeHealthContractDto
{
    public string TimeContractVersion { get; init; } = TimeHealthContract.Version;

    public TimeHealthDto Time { get; init; } = new();
}

public interface ITimeAuthorityService
{
    Task<TimeHealthDto> GetHealthAsync(
        CancellationToken cancellationToken = default);

    // Existing test doubles remain source-compatible. The production service
    // overrides this with the one-query SQL-only authorization path.
    Task<TimeHealthDto> GetWriteAuthorizationAsync(
        CancellationToken cancellationToken = default) =>
        GetHealthAsync(cancellationToken);
}

public static class DatabaseTimeAuthorityContract
{
    public const int QueryTimeoutSeconds = 2;
}

public interface IDatabaseTimeAuthorityProbe
{
    Task<DateTimeOffset?> ReadDatabaseUtcAsync(
        CancellationToken cancellationToken = default);
}

public static class TimeAuthorityPolicy
{
    /// <summary>
    /// A write is authorized only by a successful SQL SYSUTCDATETIME probe.
    /// Health labels and W32Time/NTP diagnostics cannot override this result.
    /// </summary>
    public static bool IsMutationAllowed(TimeHealthDto health)
    {
        ArgumentNullException.ThrowIfNull(health);
        return health.DatabaseClockAvailable &&
               health.DatabaseUtcNow is not null &&
               health.WritesAllowed;
    }

    public static TimeHealthDto Evaluate(TimeAuthorityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var databaseAvailable = observation.DatabaseUtcNow is not null;
        double? skewMilliseconds = null;
        if (observation.DatabaseUtcNow is { } databaseUtc)
        {
            var midpoint = observation.ApiUtcAtQueryStart
                .AddTicks(observation.MonotonicQueryDuration.Ticks / 2);
            skewMilliseconds = (midpoint - databaseUtc).TotalMilliseconds;
        }

        var syncAge = observation.TimeSinceLastGoodSync;
        var lastSync = syncAge is { } age && age >= TimeSpan.Zero
            ? observation.ApiUtcAfterQuery.Subtract(age)
            : (DateTimeOffset?)null;
        var messages = BuildDiagnosticMessages(observation, databaseAvailable);

        return new TimeHealthDto
        {
            TimeHealth = databaseAvailable
                ? TimeHealthStatuses.Healthy
                : TimeHealthStatuses.Blocked,
            ReasonCode = databaseAvailable
                ? TimeHealthReasonCodes.None
                : TimeHealthReasonCodes.DatabaseUtcUnavailable,
            WritesAllowed = databaseAvailable,
            DatabaseClockAvailable = databaseAvailable,
            ServerUtcNow = observation.ApiUtcAfterQuery,
            DatabaseUtcNow = observation.DatabaseUtcNow,
            DatabaseUtcQueryMilliseconds =
                observation.MonotonicQueryDuration.TotalMilliseconds,
            ClockSkewMilliseconds = skewMilliseconds,
            MonotonicQueryMilliseconds =
                observation.MonotonicQueryDuration.TotalMilliseconds,
            TimeZone = observation.ServerTimeZone,
            WindowsTimeServiceState = observation.WindowsTimeRunning
                ? TimeHealthContract.RunningServiceState
                : "NotRunningOrUnavailable",
            ConfiguredPeer = observation.ConfiguredPeer,
            CurrentSource = observation.CurrentSource,
            LastSuccessfulSyncUtc = lastSync,
            LastSyncError = observation.LastSyncError,
            PhaseOffsetMilliseconds = observation.NtpPhaseOffsetMilliseconds,
            LastSuccessfulSyncAgeSeconds = syncAge?.TotalSeconds,
            EffectivePollIntervalSeconds =
                observation.EffectivePollInterval?.TotalSeconds,
            EvaluatedAtUtc = observation.DatabaseUtcNow ?? observation.ApiUtcAfterQuery,
            Messages = messages,
        };
    }

    private static IReadOnlyList<string> BuildDiagnosticMessages(
        TimeAuthorityObservation observation,
        bool databaseAvailable)
    {
        var messages = new List<string>();
        if (!databaseAvailable)
        {
            messages.Add(
                "Không thể đọc SYSUTCDATETIME() từ SQL Server; thao tác ghi bị chặn.");
            return messages;
        }

        if (!observation.WindowsTimeRunning)
        {
            messages.Add(
                "Diagnostic: Windows Time không Running hoặc không đọc được; " +
                "điều này không chặn ghi khi SQL clock còn sẵn sàng.");
        }
        if (observation.LastSyncError is not null and not 0)
        {
            messages.Add(
                $"Diagnostic: Windows Time Last Sync Error = " +
                $"{observation.LastSyncError}; SQL clock vẫn là thẩm quyền ghi nhận.");
        }
        if (!string.IsNullOrWhiteSpace(observation.ConfiguredPeer) &&
            !string.Equals(
                observation.ConfiguredPeer.Trim(),
                TimeHealthContract.ApprovedPeer,
                StringComparison.OrdinalIgnoreCase))
        {
            messages.Add(
                "Diagnostic: Windows Time peer khác cấu hình vận hành đã biết; " +
                "không dùng diagnostic này làm write gate.");
        }

        if (messages.Count == 0)
        {
            messages.Add(
                "SQL Server SYSUTCDATETIME() sẵn sàng; Windows Time/NTP chỉ là diagnostic.");
        }
        return messages;
    }
}
