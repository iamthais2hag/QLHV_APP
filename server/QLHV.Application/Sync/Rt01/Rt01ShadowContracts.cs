using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync.Rt01;

public static class Rt01ShadowModes
{
    public const string Shadow = "SHADOW";
}

public static class Rt01ShadowStatuses
{
    public const string Matched = "MATCHED";
    public const string DriftDetected = "DRIFT_DETECTED";
    public const string Blocked = "BLOCKED";
    public const string ReadFailed = "READ_FAILED";
}

public sealed record Rt01ShadowRoute(
    string SourceType,
    string SourceProfileCode,
    string SourceDatabaseName,
    string MaCsdt);

/// <summary>
/// Fixed RT-01 routes. They intentionally point from the two live CSDT
/// databases directly to QLHV_APP and never include BAK or V1 profiles.
/// </summary>
public static class Rt01ShadowRouteCatalog
{
    public static Rt01ShadowRoute Oto { get; } = new(
        "OTO",
        CsdtConnectionProfileCodes.CsdtOto,
        "CSDL_OTO",
        "66029");

    public static Rt01ShadowRoute Moto { get; } = new(
        "MOTO",
        CsdtConnectionProfileCodes.CsdtMoto,
        "CSDL_MOTO",
        "66030");

    public static IReadOnlyList<Rt01ShadowRoute> Ordered { get; } = [Oto, Moto];
}

/// <summary>
/// RT-01 is deliberately disabled unless an explicit, separately approved host
/// composes the shadow worker. There is no apply/write switch in this contract.
/// </summary>
public sealed class Rt01ShadowOptions
{
    public const string SectionName = "Rt01Shadow";

    public bool Enabled { get; set; }

    public string Mode { get; set; } = Rt01ShadowModes.Shadow;

    public int PollIntervalSeconds { get; set; } = 2;
}

public sealed class Rt01ShadowOptionsValidator : IValidateOptions<Rt01ShadowOptions>
{
    public ValidateOptionsResult Validate(string? name, Rt01ShadowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (!string.Equals(options.Mode, Rt01ShadowModes.Shadow, StringComparison.Ordinal))
        {
            failures.Add("RT-01 chi cho phep Mode=SHADOW.");
        }

        if (options.PollIntervalSeconds is < 1 or > 5)
        {
            failures.Add("RT-01 shadow PollIntervalSeconds phai nam trong khoang 1..5.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

public sealed record Rt01ShadowSnapshots(
    QlhvImportSourceSnapshot LiveSource,
    QlhvImportTargetSnapshot QlhvTarget,
    DateTime ReadStartedAtUtc,
    DateTime ReadCompletedAtUtc);

public sealed record Rt01ShadowObservation
{
    public string Mode { get; init; } = Rt01ShadowModes.Shadow;

    public bool IsReadOnly { get; init; } = true;

    public string SourceType { get; init; } = string.Empty;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string SourceDatabaseName { get; init; } = string.Empty;

    public string TargetDatabaseName { get; init; } = "QLHV_APP";

    public string MaCsdt { get; init; } = string.Empty;

    public string Status { get; init; } = Rt01ShadowStatuses.Blocked;

    public DateTime ObservedAtUtc { get; init; }

    public DateTime ReadStartedAtUtc { get; init; }

    public DateTime ReadCompletedAtUtc { get; init; }

    public int DetectionLatencyBudgetSeconds { get; init; }

    public string SourceFingerprint { get; init; } = string.Empty;

    public string TargetFingerprint { get; init; } = string.Empty;

    public bool SourceChangedSincePreviousObservation { get; init; }

    public int SourceRows { get; init; }

    public int TargetActiveRows { get; init; }

    public int TargetSoftDeletedRows { get; init; }

    public int PlannedInsertRows { get; init; }

    public int PlannedUpdateRows { get; init; }

    public int PlannedReactivateRows { get; init; }

    /// <summary>
    /// Active target identities absent from the live source. They are reported
    /// for comparison only and are never passed to a delete/deactivation path.
    /// </summary>
    public int TargetOnlyActiveRows { get; init; }

    public int PlannedNoChangeRows { get; init; }

    public int DuplicateSourceIdentityGroups { get; init; }

    public int DuplicateTargetIdentityGroups { get; init; }

    public int BusinessDataWrites { get; init; }

    public bool ApplyCheckpointPublished { get; init; }

    public bool ExistingAutoSyncTouched { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasDrift =>
        PlannedInsertRows +
        PlannedUpdateRows +
        PlannedReactivateRows +
        TargetOnlyActiveRows > 0;

    public static Rt01ShadowObservation ReadFailure(
        Rt01ShadowRoute route,
        int latencyBudgetSeconds,
        DateTime observedAtUtc,
        Exception exception)
        => new()
        {
            SourceType = route.SourceType,
            SourceProfileCode = route.SourceProfileCode,
            SourceDatabaseName = route.SourceDatabaseName,
            MaCsdt = route.MaCsdt,
            Status = Rt01ShadowStatuses.ReadFailed,
            ObservedAtUtc = observedAtUtc,
            ReadStartedAtUtc = observedAtUtc,
            ReadCompletedAtUtc = observedAtUtc,
            DetectionLatencyBudgetSeconds = latencyBudgetSeconds,
            Blockers = [$"Shadow read failed: {exception.GetType().Name}."],
        };
}

public interface IRt01ShadowSnapshotReader
{
    Task<Rt01ShadowSnapshots> ReadAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken = default);
}

public interface IRt01ShadowProbe
{
    Task<Rt01ShadowObservation> ObserveAsync(
        Rt01ShadowRoute route,
        string? previousSourceFingerprint,
        int detectionLatencyBudgetSeconds,
        CancellationToken cancellationToken = default);
}

public interface IRt01ShadowObservationSink
{
    Task ObserveAsync(
        Rt01ShadowObservation observation,
        CancellationToken cancellationToken = default);
}
