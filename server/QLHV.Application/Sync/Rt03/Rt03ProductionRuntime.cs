namespace QLHV.Application.Sync.Rt03;

public static class Rt03WorkerStatuses
{
    public const string Starting = "STARTING";
    public const string Healthy = "HEALTHY";
    public const string HealthyIdle = "HEALTHY_IDLE";
    public const string RealtimeOff = "REALTIME_OFF";
    public const string Blocked = "BLOCKED";
    public const string Stopping = "STOPPING";
    public const string Stopped = "STOPPED";
}

public static class Rt03CycleStatuses
{
    public const string HealthyNoChange = "HEALTHY_NO_CHANGE";
    public const string HealthyReviewedRetained = "HEALTHY_REVIEWED_RETAINED";
    public const string Applied = "APPLIED";
    public const string RecoveredCheckpoint = "RECOVERED_CHECKPOINT";
    public const string SkippedProfileOff = "SKIPPED_PROFILE_OFF";
    public const string Blocked = "BLOCKED";
}

public sealed record Rt03ProductionFeatureState(
    bool EnableProductionRealtime,
    bool EnableProductionShadow,
    bool EnableProductionWrites,
    bool EnableProductionCanary,
    bool EnableControlledCutover,
    bool EnableProductionDeletes);

public sealed record Rt03ProductionProfileState(
    string SourceProfileCode,
    bool Enabled,
    int SequenceOrder,
    string ExpectedMappingFingerprint,
    string ExpectedSourceSchemaFingerprint,
    string ExpectedTargetSchemaFingerprint,
    string? LastStatus);

public sealed record Rt03ProductionCycleResult(
    string SourceProfileCode,
    string Status,
    Guid CycleId,
    long CheckpointBefore,
    long CheckpointAfter,
    int InsertedRows,
    int UpdatedRows,
    int RetainedRows,
    int DeletedOrDeactivatedRows,
    int DuplicateActiveRows,
    DateTime CompletedAtUtc)
{
    public int ReviewedRetainedCount { get; init; }

    public IReadOnlyList<string> ReviewedRetainedDomains { get; init; } =
        Array.Empty<string>();

    public int ActiveReviewCount { get; init; }

    public int StaleReviewCount { get; init; }

    public int NewDriftCount { get; init; }

    public DateTime? OldestActiveReviewUtc { get; init; }

    public DateTime? NewestActiveReviewUtc { get; init; }

    public string CycleOutcome { get; init; } = string.Empty;
}

public sealed record Rt03ReviewedRetainedSummary(
    int ReviewedRetainedCount,
    IReadOnlyList<string> ReviewedRetainedDomains,
    int ActiveReviewCount,
    int StaleReviewCount,
    int NewDriftCount,
    DateTime? OldestActiveReviewUtc,
    DateTime? NewestActiveReviewUtc,
    string CycleOutcome,
    IReadOnlyList<Rt03ReviewedRetainedEvaluation> Evaluations)
{
    public IReadOnlySet<string> SafeSourceBusinessIdentityHashes { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static Rt03ReviewedRetainedSummary Empty(string outcome) =>
        new(0, [], 0, 0, 0, null, null, outcome, []);
}

public sealed record Rt03ReviewedRetainedRereviewRequest(
    string SourceProfileCode,
    IReadOnlyList<long> ReviewedEventVersions,
    bool Commit);

public sealed record Rt03ReviewedRetainedRereviewResult(
    string SourceProfileCode,
    long EvidenceAnchorVersion,
    int CreatedReviewCount,
    IReadOnlyList<string> DiagnosticIds,
    string EvidenceContractVersion,
    DateTime CompletedAtUtc)
{
    public bool CommitRequested { get; init; }

    public bool ValidationPassed { get; init; }
}

public interface IRt03ReviewedRetainedRereviewService
{
    Task<Rt03ReviewedRetainedRereviewResult> ExecuteAsync(
        Rt03ReviewedRetainedRereviewRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRt03ProductionRealtimeCycleProcessor
{
    Task<Rt03ProductionCycleResult> ProcessAsync(
        string sourceProfileCode,
        string workerInstanceId,
        CancellationToken cancellationToken = default);
}

public interface IRt03ProductionRuntimeStateStore
{
    Task<Rt03ProductionFeatureState> ReadFeatureStateAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Rt03ProductionProfileState>> ReadProfileStatesAsync(
        CancellationToken cancellationToken = default);

    Task RecordWorkerAsync(
        string instanceId,
        string status,
        string? currentProfile,
        bool cycleActive,
        string? lastErrorCode,
        CancellationToken cancellationToken = default);

    Task RecordCycleAsync(
        string instanceId,
        Rt03ProductionCycleResult result,
        CancellationToken cancellationToken = default);

    Task RecordIdleAsync(
        string instanceId,
        string outcome,
        CancellationToken cancellationToken = default);
}

public interface IQlhvDirectRealtimeGlobalLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        CancellationToken cancellationToken = default);
}
