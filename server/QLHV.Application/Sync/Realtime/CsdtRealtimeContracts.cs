namespace QLHV.Application.Sync.Realtime;

public sealed record CsdtRealtimeUserContext(
    string Actor,
    string Role,
    bool WriteAuthorized);

public sealed record CsdtRealtimeCommand
{
    public string CommandType { get; init; } = string.Empty;

    public string StreamCode { get; init; } = string.Empty;

    public string VehicleType { get; init; } = string.Empty;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string SourceDatabaseName { get; init; } = string.Empty;

    public string TargetDatabaseName { get; init; } = string.Empty;

    public string MaCSDT { get; init; } = string.Empty;

    public string? ExpectedStateToken { get; init; }

    public bool? Enabled { get; init; }

    public string? MaKhoaHoc { get; init; }

    public string? ExpectedPlanToken { get; init; }

    public string RequestedBy { get; init; } = string.Empty;
}

public static class CsdtRealtimeCommandTypes
{
    public const string SetEnabled = "SET_ENABLED";
    public const string Baseline = "BASELINE";
    public const string Retry = "RETRY";
    public const string ReverseExecute = "V1_TO_V2_EXECUTE";
}

public interface ICsdtRealtimeStateRepository
{
    Task<IReadOnlyList<CsdtRealtimeStreamStatusDto>> GetStreamsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CsdtRealtimeHistoryItemDto>> GetHistoryAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CsdtRealtimeTombstoneDto>> GetTombstonesAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default);
}

public interface ICsdtRealtimeCommandRepository
{
    /// <summary>
    /// Atomically verifies ExpectedStateToken and any command-specific expectations,
    /// then either reserves one durable command or joins the matching active command.
    /// </summary>
    Task<CsdtRealtimeActionResultDto> EnqueueAsync(
        CsdtRealtimeCommand command,
        CancellationToken cancellationToken = default);

    Task<bool> HasRetryableReverseAsync(
        string streamCode,
        string? maKhoaHoc,
        string expectedPlanToken,
        CancellationToken cancellationToken = default);
}

public interface ICsdtReversePlanRepository
{
    /// <summary>
    /// Builds a read-only V1-to-V2 plan. The implementation must use only the
    /// server-selected route and must not infer or rewrite any source identity.
    /// </summary>
    Task<CsdtReversePlanDto> BuildPlanAsync(
        CsdtRealtimeRouteDefinition route,
        string? maKhoaHoc,
        CancellationToken cancellationToken = default);
}

public interface ICsdtReverseCommandExecutor
{
    /// <summary>
    /// Rebuilds the V1-to-V2 plan, verifies the exact stable plan token, and
    /// updates only existing rows that are still classified as safe.
    /// Implementations must never insert, delete, or change a primary key.
    /// </summary>
    Task<CsdtReverseCommandExecutionResult> ExecuteAsync(
        CsdtReverseExecutionContext context,
        CsdtRealtimeRouteDefinition reverseRoute,
        string? maKhoaHoc,
        string expectedPlanToken,
        CancellationToken cancellationToken = default);
}

public sealed record CsdtReverseExecutionContext(
    Guid RunId,
    long StreamId,
    Guid CommandId);

public sealed record CsdtReverseDomainExecutionResult(
    string Domain,
    string Status,
    long SourceRows,
    long UpdatedRows,
    long SkippedRows,
    int AttemptCount,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record CsdtReverseCommandExecutionResult(
    long UpdatedRows,
    string PlanToken,
    IReadOnlyList<CsdtReverseDomainExecutionResult> Domains,
    bool IsRecovery,
    bool HasOptionalSkips);

public interface ICsdtRealtimeService
{
    Task<CsdtRealtimeStreamsResponseDto> GetStreamsAsync(
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CsdtRealtimeHistoryItemDto>> GetHistoryAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CsdtRealtimeTombstoneDto>> GetTombstonesAsync(
        string streamCode,
        int take,
        CancellationToken cancellationToken = default);

    Task<CsdtRealtimeActionResultDto> SetEnabledAsync(
        string streamCode,
        CsdtRealtimeEnableRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default);

    Task<CsdtRealtimeActionResultDto> QueueBaselineAsync(
        string streamCode,
        CsdtRealtimeBaselineRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default);

    Task<CsdtRealtimeActionResultDto> QueueRetryAsync(
        string streamCode,
        CsdtRealtimeRetryRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default);

    Task<CsdtReversePlanDto> GetReversePlanAsync(
        string vehicleType,
        string? maKhoaHoc,
        CancellationToken cancellationToken = default);

    Task<CsdtReverseExecuteResultDto> ExecuteReverseAsync(
        CsdtReverseExecuteRequest request,
        CsdtRealtimeUserContext user,
        CancellationToken cancellationToken = default);
}

public sealed class CsdtRealtimeStoreUnavailableException : InvalidOperationException
{
    public CsdtRealtimeStoreUnavailableException(string message)
        : base(message)
    {
    }

    public CsdtRealtimeStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class CsdtRealtimeAuthorizationException : InvalidOperationException
{
    public CsdtRealtimeAuthorizationException()
        : base("Ban khong co quyen thuc hien thao tac dong bo nay.")
    {
    }
}
