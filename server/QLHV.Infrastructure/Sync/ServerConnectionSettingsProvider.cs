using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;

namespace QLHV.Infrastructure.Sync;

/// <summary>
/// Phase A connection settings provider.
/// QLHV_APP is resolved from protected server configuration. CSDT_V1/CSDT_V2 currently resolve
/// from configuration placeholders only until the Admin connection settings screen is implemented.
/// </summary>
public sealed class ServerConnectionSettingsProvider : IConnectionSettingsProvider
{
    private readonly IConfiguration _configuration;
    private readonly SyncOptions _options;

    public ServerConnectionSettingsProvider(IConfiguration configuration, IOptions<SyncOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    public Task<ResolvedConnection> GetQlhvAppConnectionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Resolve(_options.QlhvAppConnectionName));

    public Task<ResolvedConnection> GetSourceConnectionAsync(
        SourceSystem source,
        CancellationToken cancellationToken = default)
    {
        var key = source switch
        {
            SourceSystem.V1 => _options.V1ConnectionName,
            SourceSystem.V2 => _options.V2ConnectionName,
            _ => source.ToString(),
        };

        return Task.FromResult(Resolve(key));
    }

    public Task<ConnectionSettingsView> GetViewAsync(
        SourceSystem source,
        CancellationToken cancellationToken = default)
    {
        var key = source switch
        {
            SourceSystem.V1 => _options.V1ConnectionName,
            SourceSystem.V2 => _options.V2ConnectionName,
            _ => source.ToString(),
        };
        var resolved = Resolve(key);

        return Task.FromResult(new ConnectionSettingsView
        {
            Key = key,
            DisplayName = key,
            IsConfigured = resolved.IsConfigured,
            IsPlaceholder = resolved.IsPlaceholder,
            IsEnabled = resolved.IsUsable,
            PasswordMasked = "********",
            LastTestedAt = null,
            LastTestResult = "Phase A: chua thuc hien test connection that.",
        });
    }

    private ResolvedConnection Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ResolvedConnection.NotConfigured("(missing)");
        }

        return ResolvedConnection.FromConfiguration(key, _configuration.GetConnectionString(key));
    }
}
