namespace QLHV.Application.Sync.Dtos;

using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.SystemData;

public sealed class QlhvAutoSyncQueueResultDto
{
    public bool Accepted { get; init; }

    public bool JoinedExisting { get; init; }

    public bool IsConflict { get; init; }

    public bool IsUnavailable { get; init; }

    public Guid? RunId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Decision { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed class QlhvAutoSyncSourceResultDto
{
    public string SourceType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public Guid? RefreshOperationId { get; init; }

    public Guid? SyncOperationId { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime CompletedAtUtc { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<QlhvImportDomainResultDto> DomainResults { get; init; } =
        Array.Empty<QlhvImportDomainResultDto>();

    public QlhvImportDomainResultDto? PhotoProcessing { get; init; }

    public QlhvSkippedReasonCountsDto SkippedReasons { get; init; } = new();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class QlhvAutoSyncStatusDto
{
    public bool Found { get; init; } = true;

    public bool Enabled { get; init; }

    public bool RunOnServerStartup { get; init; }

    public bool RefreshBackupBeforeSync { get; init; }

    public int PollingIntervalSeconds { get; init; }

    public IReadOnlyList<string> ResolvedSourceOrder { get; init; } =
        Array.Empty<string>();

    public bool ApiWorkerConfigParity { get; init; }

    public QlhvAutoSyncPollingStatusDto Polling { get; init; } = new();

    public RuntimeBuildIdentityDto Runtime { get; init; } = new();

    public string State { get; init; } = "idle";

    public Guid? RunId { get; init; }

    public Guid? ActiveRunId { get; init; }

    public string? TriggerType { get; init; }

    public string? Actor { get; init; }

    public string? CurrentSourceType { get; init; }

    public string? CurrentStage { get; init; }

    public DateTime? CreatedAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public DateTime? LastSuccessfulSyncUtc { get; init; }

    public Guid? LastSuccessfulRunId { get; init; }

    public QlhvAutoSyncSourceResultDto? Oto { get; init; }

    public QlhvAutoSyncSourceResultDto? Moto { get; init; }

    public IReadOnlyList<QlhvAutoSyncHistoryItemDto> History { get; init; } =
        Array.Empty<QlhvAutoSyncHistoryItemDto>();

    public string? LastError { get; init; }

    public QlhvRealtimeOperationsStateDto Realtime { get; init; } = new();

    public QlhvAutoSyncConfigurationStateDto Configuration { get; init; } = new();

    public QlhvAutoSyncRuntimeStateDto AutoSyncRuntime { get; init; } = new();
}

public sealed class QlhvRealtimeOperationsStateDto
{
    public string ServiceState { get; init; } = "UNKNOWN";
    public string ProcessState { get; init; } = "UNKNOWN";
    public string OverallHealth { get; init; } = "UNKNOWN";
    public string? WorkerInstanceId { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public string? CurrentProfile { get; init; }
    public bool CycleActive { get; init; }
    public bool WriterEnabled { get; init; }
    public bool MutexHeld { get; init; }
    public string? LastFailureCode { get; init; }
    public IReadOnlyList<QlhvRealtimeProfileStateDto> Profiles { get; init; } =
        Array.Empty<QlhvRealtimeProfileStateDto>();
}

public sealed class QlhvRealtimeProfileStateDto
{
    public string ProfileCode { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Health { get; init; } = "UNKNOWN";
    public long CheckpointVersion { get; init; }
    public DateTime? LastCycleCompletedAtUtc { get; init; }
}

public sealed class QlhvAutoSyncConfigurationStateDto
{
    public bool Enabled { get; init; }
    public bool RunOnStartup { get; init; }
    public bool PollingEnabled { get; init; }
    public int PollIntervalSeconds { get; init; }
    public bool IsFallbackOnly { get; init; }
    public bool FallbackModeEnabled { get; init; }
    public bool ManualRunAllowed { get; init; }
    public string ManualRunDecision { get; init; } = string.Empty;
    public string ManualRunReason { get; init; } = string.Empty;
}

public sealed class QlhvAutoSyncRuntimeStateDto
{
    public bool IsRunActive { get; init; }
    public Guid? ActiveRunId { get; init; }
    public string Classification { get; init; } = "INACTIVE";
    public string? Source { get; init; }
    public string? Step { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? LastHeartbeatUtc { get; init; }
    public bool HeartbeatFresh { get; init; }
    public int EffectiveActiveSlotCount { get; init; }
    public int RawActiveSlotCount { get; init; }
    public int ActiveOperationCount { get; init; }
}

public sealed class QlhvAutoSyncPollingStatusDto
{
    public bool Enabled { get; init; }

    public bool IsPolling { get; init; }

    public string? DisabledReason { get; init; }

    public DateTime ProcessStartedAtUtc { get; init; }

    public DateTime? LastPollStartedAtUtc { get; init; }

    public DateTime? LastPollCompletedAtUtc { get; init; }

    public DateTime? NextPollAtUtc { get; init; }

    public string? LastDecision { get; init; }

    public string? LastError { get; init; }
}

public sealed class QlhvAutoSyncHistoryItemDto
{
    public Guid RunId { get; init; }

    public string TriggerType { get; init; } = string.Empty;

    public string Actor { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public QlhvAutoSyncSourceResultDto? Oto { get; init; }

    public QlhvAutoSyncSourceResultDto? Moto { get; init; }

    public string? ErrorMessage { get; init; }

    public string Classification { get; init; } = "HISTORY";

    public bool IsStale { get; init; }

    public DateTime? LastHeartbeatUtc { get; init; }
}

public sealed class QlhvAutoSyncDataGapDiagnosticsDto
{
    public bool IsReadOnly { get; init; } = true;

    public string ProbeVersion { get; init; } = "CSDT_AUTO_SYNC_DATA_GAP_V1";

    public DateTime CapturedAtUtc { get; init; }

    public string TargetDatabaseName { get; init; } = "QLHV_APP";

    public IReadOnlyList<QlhvOperationsStatusDto> SourceStatuses { get; init; } =
        Array.Empty<QlhvOperationsStatusDto>();

    public QlhvSyncFreshnessResult Freshness { get; init; } = new();

    public IReadOnlyList<string> ScopeNotes { get; init; } = Array.Empty<string>();
}

public sealed class QlhvSessionStartStatusDto
{
    public bool Found { get; init; } = true;

    public bool ServerReady { get; init; }

    public bool OperationActive { get; init; }

    public Guid? ActiveRunId { get; init; }

    public bool NeedSync { get; init; }

    public bool CanStart { get; init; }

    public Guid? RunId { get; init; }

    public string State { get; init; } = "idle";

    public string? CurrentSourceType { get; init; }

    public string? CurrentStage { get; init; }

    public bool IsTerminal { get; init; }

    public bool Succeeded { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public DateTime? LastSuccessfulSyncUtc { get; init; }

    public DateTime? LastAttemptUtc { get; init; }

    public string? ErrorMessage { get; init; }

    public string? LastError { get; init; }

    public IReadOnlyList<string> NeedSyncReasons { get; init; } = Array.Empty<string>();

    public IReadOnlyList<QlhvSyncSnapshotDto> LiveSnapshot { get; init; } =
        Array.Empty<QlhvSyncSnapshotDto>();

    public IReadOnlyList<QlhvSyncSnapshotDto> BackupSnapshot { get; init; } =
        Array.Empty<QlhvSyncSnapshotDto>();

    public IReadOnlyList<QlhvPartitionFreshnessDto> Partitions { get; init; } =
        Array.Empty<QlhvPartitionFreshnessDto>();

    public SystemDataVersionDto? AppDataVersion { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public string Message { get; init; } = string.Empty;
}

public sealed class QlhvSyncSnapshotDto
{
    public string SourceType { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public string ContentToken { get; init; } = string.Empty;

    public string? BackupSnapshotToken { get; init; }

    public QlhvSyncEntityCountsDto Rows { get; init; } = new();
}

public sealed class QlhvSyncEntityCountsDto
{
    public int HocVien { get; init; }

    public int KhoaHoc { get; init; }

    public int GiaoVien { get; init; }

    public int KhoaHocGiaoVien { get; init; }

    public int Total => HocVien + KhoaHoc + GiaoVien + KhoaHocGiaoVien;
}

public sealed class QlhvPartitionFreshnessDto
{
    public string SourceType { get; init; } = string.Empty;

    public string SourceProfileCode { get; init; } = string.Empty;

    public bool IsConsistent { get; init; }

    public string? AppliedBackupSnapshotToken { get; init; }

    public QlhvSyncEntityCountsDto AppliedRows { get; init; } = new();

    public DateTime? AppliedAtUtc { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

public sealed record QlhvAutoSyncRunCreate(
    Guid RunId,
    string TriggerType,
    string Actor,
    IReadOnlyList<string> SourceOrder,
    DateTime CreatedAtUtc,
    DateTime? DedupeNotBeforeUtc = null);

public sealed class QlhvAutoSyncRunRecord
{
    public Guid RunId { get; init; }

    public string TriggerType { get; init; } = string.Empty;

    public string Actor { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<string> SourceOrder { get; init; } = Array.Empty<string>();

    public string? CurrentSourceType { get; init; }

    public string? CurrentStage { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public bool ActiveSlot { get; init; }

    public QlhvAutoSyncSourceResultDto? Oto { get; init; }

    public QlhvAutoSyncSourceResultDto? Moto { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed record QlhvAutoSyncWorkItem(Guid RunId);

public sealed record QlhvAutoSyncOutcome(
    string Status,
    string? ErrorMessage,
    DateTime CompletedAtUtc);

public sealed class QlhvSessionStartSyncRequest
{
    public bool ServerStartedByLauncher { get; init; }
}
