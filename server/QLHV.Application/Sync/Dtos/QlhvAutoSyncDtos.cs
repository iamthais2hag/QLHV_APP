namespace QLHV.Application.Sync.Dtos;

using QLHV.Application.SystemData;

public sealed class QlhvAutoSyncQueueResultDto
{
    public bool Accepted { get; init; }

    public bool JoinedExisting { get; init; }

    public bool IsConflict { get; init; }

    public bool IsUnavailable { get; init; }

    public Guid? RunId { get; init; }

    public string Status { get; init; } = string.Empty;

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
}

public sealed class QlhvAutoSyncStatusDto
{
    public bool Found { get; init; } = true;

    public bool Enabled { get; init; }

    public bool RunOnServerStartup { get; init; }

    public bool RefreshBackupBeforeSync { get; init; }

    public string State { get; init; } = "idle";

    public Guid? RunId { get; init; }

    public Guid? ActiveRunId { get; init; }

    public string? TriggerType { get; init; }

    public string? Actor { get; init; }

    public string? CurrentSourceType { get; init; }

    public string? CurrentStage { get; init; }

    public DateTime? StartedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public DateTime? LastSuccessfulSyncUtc { get; init; }

    public QlhvAutoSyncSourceResultDto? Oto { get; init; }

    public QlhvAutoSyncSourceResultDto? Moto { get; init; }

    public string? LastError { get; init; }
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
