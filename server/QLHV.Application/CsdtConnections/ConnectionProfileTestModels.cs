namespace QLHV.Application.CsdtConnections;

public sealed class ConnectionProfileTestSettings
{
    public string ProfileCode { get; init; } = string.Empty;

    public string? ServerName { get; init; }

    public string? DatabaseName { get; init; }

    public string AuthMode { get; init; } = "Windows";

    public string? UserName { get; init; }

    public string? PasswordPlainText { get; init; }
}

public sealed class ConnectionProfileTestOutcome
{
    public bool CanTest { get; init; }

    public bool Succeeded { get; init; }

    public string Status { get; init; } = "Unknown";

    public string SafeMessage { get; init; } = string.Empty;
}

public interface ICsdtConnectionTester
{
    Task<ConnectionProfileTestOutcome> TestSettingsAsync(
        ConnectionProfileTestSettings settings,
        CancellationToken cancellationToken = default);

    Task<ConnectionProfileTestOutcome> TestConnectionStringAsync(
        string profileCode,
        string? connectionString,
        CancellationToken cancellationToken = default);
}
