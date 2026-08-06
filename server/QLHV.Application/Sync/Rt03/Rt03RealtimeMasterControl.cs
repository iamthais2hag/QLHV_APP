namespace QLHV.Application.Sync.Rt03;

public static class Rt03RealtimeControlStates
{
    public const string Off = "OFF";
    public const string On = "ON";
    public const string Blocked = "BLOCKED";

    public static bool IsValid(string value) =>
        string.Equals(value, Off, StringComparison.Ordinal) ||
        string.Equals(value, On, StringComparison.Ordinal) ||
        string.Equals(value, Blocked, StringComparison.Ordinal);
}

public static class Rt03RealtimeOutcomes
{
    public const string RealtimeOff = "REALTIME_OFF";
    public const string HealthyIdle = "HEALTHY_IDLE";
    public const string NoChange = "NO_CHANGE";
    public const string Applied = "APPLIED";
    public const string Blocked = "BLOCKED";
}

public static class Rt03RealtimeRunRequestStatuses
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string Blocked = "BLOCKED";
}

public static class Rt03RealtimeMasterErrors
{
    public const string ControlUnavailable = "RT03_MASTER_CONTROL_UNAVAILABLE";
    public const string ControlConcurrencyConflict = "RT03_MASTER_CONTROL_CONCURRENCY";
    public const string InvalidControlState = "RT03_MASTER_CONTROL_INVALID_STATE";
    public const string BacklogProbeFailed = "RT03_MASTER_BACKLOG_PROBE_FAILED";
    public const string PermissionRejected = "RT03_MASTER_PERMISSION_REJECTED";
    public const string LegacyAutoSyncDisabled = "RT03_MASTER_AUTHORITY_REQUIRED";
}

public sealed record Rt03RealtimeControlRecord(
    string State,
    DateTime UpdatedAtUtc,
    string UpdatedBy,
    string? Reason,
    byte[] RowVersion);

public sealed record Rt03RealtimeControlChangeRequest(string ExpectedRowVersion);

public sealed record Rt03RealtimeProfileBacklog(
    string SourceProfileCode,
    long CheckpointVersion,
    long CurrentVersion,
    long MinimumValidVersion)
{
    public long BacklogVersions => Math.Max(0, CurrentVersion - CheckpointVersion);

    public bool IsWindowValid =>
        CheckpointVersion >= MinimumValidVersion &&
        CurrentVersion >= CheckpointVersion;
}

public sealed record Rt03RealtimeRunRequest(
    Guid RunRequestId,
    string Status,
    string RequestedBy,
    DateTime RequestedAtUtc,
    string? WorkerInstanceId);

public sealed record Rt03RealtimeWorkerSnapshot(
    string Status,
    string? InstanceId,
    string? CurrentProfile,
    bool CycleActive,
    DateTime? LastHeartbeatUtc,
    DateTime? LastSuccessfulCycleUtc,
    string? LastCycleOutcome,
    string? LastErrorCode);

public sealed record Rt03RealtimeControlStatusDto
{
    public string State { get; init; } = Rt03RealtimeControlStates.Off;
    public DateTime UpdatedAtUtc { get; init; }
    public string UpdatedBy { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public string WorkerStatus { get; init; } = Rt03WorkerStatuses.Stopped;
    public bool WorkerRunning { get; init; }
    public string? WorkerInstanceId { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public DateTime? LastSuccessfulCycleUtc { get; init; }
    public string? CycleOutcome { get; init; }
    public string? BlockerReason { get; init; }
    public IReadOnlyList<Rt03RealtimeProfileBacklog> Profiles { get; init; } = [];
    public Rt03RealtimeRunRequest? ActiveRunOnce { get; init; }
}

public sealed record Rt03RealtimeIntegrityProfilePreview(
    string SourceProfileCode,
    string Status,
    int SourceRows,
    int TargetRows,
    int PlannedInsertRows,
    int PlannedUpdateRows,
    int TargetOnlyRows,
    int DuplicateGroups,
    int ManualReviewRows);

public sealed record Rt03RealtimeIntegrityPreviewDto(
    bool IsReadOnly,
    DateTime ObservedAtUtc,
    string Status,
    IReadOnlyList<Rt03RealtimeIntegrityProfilePreview> Profiles);

public interface IRt03RealtimeControlStore
{
    Task<Rt03RealtimeControlRecord> ReadAsync(
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeControlRecord> ChangeStateAsync(
        string state,
        string actor,
        string? reason,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeControlRecord> TransitionToBlockedAsync(
        string actor,
        string redactedReasonCode,
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeRunRequest> QueueRunOnceAsync(
        string actor,
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeRunRequest?> TryClaimRunOnceAsync(
        string workerInstanceId,
        CancellationToken cancellationToken = default);

    Task CompleteRunOnceAsync(
        Guid runRequestId,
        string status,
        string outcome,
        string? redactedReasonCode,
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeRunRequest?> ReadActiveRunOnceAsync(
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeWorkerSnapshot> ReadWorkerSnapshotAsync(
        CancellationToken cancellationToken = default);
}

public interface IRt03EventBacklogProbe
{
    Task<IReadOnlyList<Rt03RealtimeProfileBacklog>> ReadAsync(
        IReadOnlyCollection<string> sourceProfileCodes,
        CancellationToken cancellationToken = default);
}

public interface IRt03WorkerPermissionProbe
{
    Task VerifyAsync(CancellationToken cancellationToken = default);
}

public interface IRt03RealtimeControlService
{
    Task<Rt03RealtimeControlStatusDto> GetAsync(
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeControlStatusDto> EnableAsync(
        Rt03RealtimeControlChangeRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeControlStatusDto> DisableAsync(
        Rt03RealtimeControlChangeRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    Task<Rt03RealtimeRunRequest> RunOnceAsync(
        string actor,
        CancellationToken cancellationToken = default);
}

public interface IRt03RealtimeIntegrityPreviewService
{
    Task<Rt03RealtimeIntegrityPreviewDto> PreviewAsync(
        CancellationToken cancellationToken = default);
}

public sealed class Rt03RealtimeControlConcurrencyException : InvalidOperationException
{
    public Rt03RealtimeControlConcurrencyException()
        : base("Realtime control changed in another session; reload before retrying.")
    {
    }
}

public enum Rt03MasterWorkKind
{
    HeartbeatOnly,
    ContinuousCycle,
    RunOnce,
    BlockedHeartbeat,
}

public static class Rt03RealtimeMasterPolicy
{
    public static Rt03MasterWorkKind Decide(
        string state,
        bool hasPendingRunOnce)
    {
        if (string.Equals(state, Rt03RealtimeControlStates.Blocked,
                StringComparison.Ordinal))
        {
            return Rt03MasterWorkKind.BlockedHeartbeat;
        }

        return state switch
        {
            Rt03RealtimeControlStates.Off => Rt03MasterWorkKind.HeartbeatOnly,
            Rt03RealtimeControlStates.On => hasPendingRunOnce
                ? Rt03MasterWorkKind.RunOnce
                : Rt03MasterWorkKind.ContinuousCycle,
            _ => throw new Rt03SafetyException(
                Rt03RealtimeMasterErrors.InvalidControlState,
                "Realtime master control state is invalid."),
        };
    }

    public static string IdleOutcome(bool runOnce) =>
        Rt03RealtimeOutcomes.HealthyIdle;
}
