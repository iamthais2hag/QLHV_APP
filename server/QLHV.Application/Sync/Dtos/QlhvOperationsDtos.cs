using System.Text.Json.Serialization;

namespace QLHV.Application.Sync.Dtos;

public sealed class QlhvOperationSourceQuery
{
    public string SourceType { get; set; } = string.Empty;
}

public sealed class QlhvRefreshBackupRequest
{
    public string SourceType { get; set; } = string.Empty;

    [JsonIgnore]
    public string Actor { get; set; } = QlhvOperationActors.ManualAdmin;
}

public sealed class QlhvOperationRowCountsDto
{
    public int NguoiLX { get; init; }

    public int NguoiLXHoSo { get; init; }

    public int KhoaHoc { get; init; }
}

public sealed class QlhvOperationsStatusDto
{
    public string SourceType { get; init; } = string.Empty;

    public string LiveDatabaseName { get; init; } = string.Empty;

    public string BackupDatabaseName { get; init; } = string.Empty;

    public string TargetDatabaseName { get; init; } = "QLHV_APP";

    public string MaCSDT { get; init; } = string.Empty;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string State { get; init; } = "idle";

    public Guid? ActiveOperationId { get; init; }

    public DateTime? BackupLastRefreshTimeUtc { get; init; }

    public string? BackupSnapshotToken { get; init; }

    public QlhvOperationRowCountsDto LiveRows { get; init; } = new();

    public QlhvOperationRowCountsDto BackupRows { get; init; } = new();

    public int TargetActiveRows { get; init; }

    public DateTime? LastSyncTimeUtc { get; init; }

    public string? LastError { get; init; }

    public bool DryRun { get; init; }

    public bool TargetWritesEnabled { get; init; }

    public string CurrentUserRole { get; init; } = string.Empty;

    public bool WriteAuthorized { get; init; }

    public IReadOnlyList<string> RefreshBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SyncBlockers { get; init; } = Array.Empty<string>();

    public bool CanRefresh { get; init; }

    public bool CanSync { get; init; }
}

public sealed class QlhvRefreshBackupResultDto
{
    public bool Accepted { get; init; }

    public bool IsConflict { get; init; }

    public bool IsUnavailable { get; init; }

    public Guid? OperationId { get; init; }

    public string SourceType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed class QlhvOperationHistoryDto
{
    public Guid OperationId { get; init; }

    public string SourceType { get; init; } = string.Empty;

    public string OperationType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Actor { get; init; } = QlhvOperationActors.ManualAdmin;

    public DateTime StartedAtUtc { get; init; }

    public DateTime? CompletedAtUtc { get; init; }

    public int SourceRows { get; init; }

    public int InsertedRows { get; init; }

    public int UpdatedRows { get; init; }

    public int ReactivatedRows { get; init; }

    public int SoftDeletedRows { get; init; }

    public int SkippedRows { get; init; }

    public string? SnapshotToken { get; init; }

    public string? ErrorMessage { get; init; }

    public string? DetailJson { get; init; }
}

public sealed record QlhvOperationHistoryCreate(
    Guid OperationId,
    QlhvOperationSourceDefinition Source,
    string OperationType,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    string Actor = QlhvOperationActors.ManualAdmin);

public sealed record QlhvOperationHistoryCompletion(
    Guid OperationId,
    string Status,
    DateTime CompletedAtUtc,
    int SourceRows,
    int InsertedRows,
    int UpdatedRows,
    int ReactivatedRows,
    int SoftDeletedRows,
    int SkippedRows,
    string? SnapshotToken,
    string? ErrorMessage,
    string? DetailJson,
    int? LiveRows = null,
    int? BackupRows = null,
    int? TargetActiveRows = null);

public sealed record QlhvOperationDataSnapshot(
    QlhvOperationRowCountsDto LiveRows,
    QlhvOperationRowCountsDto BackupRows,
    int TargetActiveRows,
    string? BackupSnapshotToken);

public sealed record QlhvRefreshBackupWorkItem(Guid OperationId, string SourceType);

public sealed record QlhvRefreshBackupExecutionResult(
    QlhvOperationRowCountsDto LiveRows,
    QlhvOperationRowCountsDto BackupRows,
    string SnapshotToken,
    int ImagePathRows,
    string DetailJson);
