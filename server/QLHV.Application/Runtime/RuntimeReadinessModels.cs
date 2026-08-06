namespace QLHV.Application.Runtime;

using QLHV.Application.Sync.Dtos;

public sealed record RuntimeConfigurationState(
    string Path,
    bool IsRequired,
    bool Exists,
    bool IsValid)
{
    public bool IsReady => !IsRequired || (Exists && IsValid);
}

public sealed class RuntimeReadinessProbeResult
{
    public bool DatabaseConnected { get; init; }

    public string? DatabaseName { get; init; }

    public bool RequiredSchemaReady { get; init; }

    public bool AuthenticationReady { get; init; }

    public bool BackupProfilesReady { get; init; }

    public bool BackupStorageReady { get; init; }

    public bool FileStorageReady { get; init; }

    public bool RuntimeStorageReady { get; init; }

    public Rt03ReviewedRetainedRuntimeDiagnosticsDto ReviewedRetained { get; init; } = new();

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public static RuntimeReadinessProbeResult Unavailable(string message) => new()
    {
        Messages = [message],
    };
}

public sealed class RuntimeStatusDto
{
    public bool IsReady { get; init; }

    public string Version { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public RuntimeBuildIdentityDto Build { get; init; } = new();

    public QlhvAutoSyncPollingStatusDto AutoSyncPolling { get; init; } = new();

    public IReadOnlyList<string> ResolvedAutoSyncSourceOrder { get; init; } =
        Array.Empty<string>();

    public bool AutoSyncApiWorkerConfigParity { get; init; }

    public string TimeContractVersion { get; init; } = TimeHealthContract.Version;

    public TimeHealthDto Time { get; init; } = new();

    public Rt03ReviewedRetainedRuntimeDiagnosticsDto ReviewedRetained { get; init; } = new();

    public bool ConfigurationReady { get; init; }

    public bool DatabaseConnected { get; init; }

    public string? DatabaseName { get; init; }

    public bool AuthenticationReady { get; init; }

    public bool RequiredSchemaReady { get; init; }

    public bool BackupProfilesReady { get; init; }

    public bool BackupStorageReady { get; init; }

    public bool FileStorageReady { get; init; }

    public bool RuntimeStorageReady { get; init; }

    public DateTime CheckedAtUtc { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

public sealed class Rt03ReviewedRetainedRuntimeDiagnosticsDto
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

public interface IRuntimeReadinessProbe
{
    Task<RuntimeReadinessProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default);
}

public interface IRuntimeReadinessService
{
    Task<RuntimeStatusDto> GetStatusAsync(
        CancellationToken cancellationToken = default);
}
