namespace QLHV.Application.Runtime;

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
