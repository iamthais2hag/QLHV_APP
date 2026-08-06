namespace QLHV.Application.Runtime;

public sealed class RuntimeBuildIdentityDto
{
    public string ApplicationVersion { get; init; } = "unknown";

    public string? CommitSha { get; init; }

    public string ApiBuildId { get; init; } = "unknown";

    public string WorkerBuildId { get; init; } = "unknown";

    public DateTime? ApiBuiltAtUtc { get; init; }

    public DateTime ProcessStartedAtUtc { get; init; }

    public string InstanceId { get; init; } = "unknown";

    public string HostProcess { get; init; } = "unknown";

    public string Environment { get; init; } = "unknown";

    public string WorkerInstanceId { get; init; } = "unknown";

    public string FrontendBuildId { get; init; } = "unknown";

    public DateTime? FrontendBuiltAtUtc { get; init; }
}

public interface IRuntimeBuildIdentity
{
    RuntimeBuildIdentityDto Current { get; }
}
