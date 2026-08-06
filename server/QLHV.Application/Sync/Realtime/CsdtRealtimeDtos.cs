namespace QLHV.Application.Sync.Realtime;

public sealed record CsdtRealtimeDomainStatusDto
{
    public string Domain { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public long SourceRows { get; init; }

    public long TargetRows { get; init; }

    public long InsertedRows { get; init; }

    public long UpdatedRows { get; init; }

    public long SkippedRows { get; init; }

    public long ErrorRows { get; init; }

    public string? LastError { get; init; }
}

public sealed record CsdtRealtimeStreamStatusDto
{
    public string StreamCode { get; init; } = string.Empty;

    public string VehicleType { get; init; } = string.Empty;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string SourceDatabaseName { get; init; } = string.Empty;

    public string TargetDatabaseName { get; init; } = string.Empty;

    public string MaCSDT { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string State { get; init; } = string.Empty;

    public string BaselineStatus { get; init; } = string.Empty;

    public long? BaselineVersion { get; init; }

    public long? LastSuccessfulVersion { get; init; }

    public long? CurrentSourceVersion { get; init; }

    public long? MinimumValidVersion { get; init; }

    public long? LagVersions { get; init; }

    public Guid? ActiveRunId { get; init; }

    public int RetryCount { get; init; }

    public DateTimeOffset? NextRetryAtUtc { get; init; }

    public DateTimeOffset? LastStartedAtUtc { get; init; }

    public DateTimeOffset? LastCompletedAtUtc { get; init; }

    public DateTimeOffset? LastSuccessAtUtc { get; init; }

    public long InsertedRows { get; init; }

    public long UpdatedRows { get; init; }

    public long SkippedRows { get; init; }

    public long ErrorRows { get; init; }

    public long DeleteTombstoneCount { get; init; }

    public long UnresolvedConflictCount { get; init; }

    public string? LastError { get; init; }

    public string CurrentUserRole { get; init; } = string.Empty;

    public bool WriteAuthorized { get; init; }

    public string StateToken { get; init; } = string.Empty;

    public IReadOnlyList<string> ActionBlockers { get; init; } = [];

    public IReadOnlyList<CsdtRealtimeDomainStatusDto> Domains { get; init; } = [];
}

public sealed record CsdtRealtimeStreamsResponseDto
{
    public DateTimeOffset ObservedAtUtc { get; init; }

    public IReadOnlyList<CsdtRealtimeStreamStatusDto> Streams { get; init; } = [];
}

public sealed record CsdtRealtimeRunDomainDto
{
    public string Domain { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public DateTimeOffset? LastAttemptAtUtc { get; init; }

    public DateTimeOffset? SucceededAtUtc { get; init; }

    public long InsertedRows { get; init; }

    public long UpdatedRows { get; init; }

    public long SkippedRows { get; init; }

    public long ErrorRows { get; init; }

    public string? Message { get; init; }
}

public sealed record CsdtRealtimeHistoryItemDto
{
    public Guid RunId { get; init; }

    public string StreamCode { get; init; } = string.Empty;

    public string RunType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public long? FromVersion { get; init; }

    public long? ToVersion { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public long InsertedRows { get; init; }

    public long UpdatedRows { get; init; }

    public long SkippedRows { get; init; }

    public long ErrorRows { get; init; }

    public string? Actor { get; init; }

    public string? ErrorMessage { get; init; }

    public bool CanRetry { get; init; }

    public IReadOnlyList<CsdtRealtimeRunDomainDto> Domains { get; init; } = [];
}

public sealed record CsdtRealtimeTombstoneDto
{
    public long Id { get; init; }

    public string StreamCode { get; init; } = string.Empty;

    public string Domain { get; init; } = string.Empty;

    public string SourceKey { get; init; } = string.Empty;

    public long ChangeVersion { get; init; }

    public DateTimeOffset DetectedAtUtc { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? Message { get; init; }
}

public sealed record CsdtRealtimeActionResultDto
{
    public bool Accepted { get; init; }

    public bool JoinedExisting { get; init; }

    public Guid? RunId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed record CsdtRealtimeEnableRequest
{
    public bool Enabled { get; init; }

    public string ExpectedStateToken { get; init; } = string.Empty;
}

public sealed record CsdtRealtimeBaselineRequest
{
    public string ExpectedStateToken { get; init; } = string.Empty;
}

public sealed record CsdtRealtimeRetryRequest
{
    public string ExpectedStateToken { get; init; } = string.Empty;
}

public sealed record CsdtReverseDomainPlanDto
{
    public string Domain { get; init; } = string.Empty;

    public long SourceRows { get; init; }

    public long SafeInsertRows { get; init; }

    public long SafeUpdateRows { get; init; }

    public long SkippedRows { get; init; }

    public long ReviewRows { get; init; }
}

public sealed record CsdtReversePlanDto
{
    public bool IsReadOnly { get; init; } = true;

    public string VehicleType { get; init; } = string.Empty;

    public string Direction { get; init; } = CsdtRealtimeDirections.V1ToV2;

    public string SourceDatabaseName { get; init; } = string.Empty;

    public string TargetDatabaseName { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string PlanToken { get; init; } = string.Empty;

    public long SourceRows { get; init; }

    public long SafeInsertRows { get; init; }

    public long SafeUpdateRows { get; init; }

    public long SkippedRows { get; init; }

    public long V1OnlyRequiresReview { get; init; }

    public long IdentityChanged { get; init; }

    public long ConflictRequiresReview { get; init; }

    public bool Executable { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<CsdtReverseDomainPlanDto> Domains { get; init; } = [];
}

public sealed record CsdtReverseExecuteRequest
{
    public string VehicleType { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public string ExpectedPlanToken { get; init; } = string.Empty;
}

public sealed record CsdtReverseExecuteResultDto
{
    public bool Accepted { get; init; }

    public bool JoinedExisting { get; init; }

    public Guid? RunId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public CsdtReversePlanDto? Plan { get; init; }
}

public static class CsdtRealtimeDirections
{
    public const string V1ToV2 = "V1_TO_V2";
}

public static class CsdtRealtimeActionStatuses
{
    public const string Queued = "QUEUED";
    public const string JoinedExisting = "JOINED_EXISTING";
    public const string Conflict = "CONFLICT";
    public const string Rejected = "REJECTED";
    public const string Unavailable = "UNAVAILABLE";
}
