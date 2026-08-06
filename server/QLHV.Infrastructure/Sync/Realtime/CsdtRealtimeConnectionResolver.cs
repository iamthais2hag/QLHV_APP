using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

internal sealed record CsdtRealtimeResolvedRoute(
    CsdtRealtimeRouteDefinition Route,
    string SourceConnectionString,
    string TargetConnectionString,
    string StateConnectionString);

internal sealed class CsdtRealtimeConnectionResolver
{
    private static readonly IReadOnlyDictionary<string, string> ConnectionNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CsdtRealtimeProfileCodes.OtoV2] = "CSDL_OTO",
            [CsdtRealtimeProfileCodes.OtoV1] = "CSDL_OTO_V1",
            [CsdtRealtimeProfileCodes.MotoV2] = "CSDL_MOTO",
            [CsdtRealtimeProfileCodes.MotoV1] = "CSDL_MOTO_V1",
            [CsdtRealtimeProfileCodes.OtoV2Bak] = "CSDL_OTO_BAK",
            [CsdtRealtimeProfileCodes.OtoV1Bak] = "CSDL_OTO_V1_BAK",
            [CsdtRealtimeProfileCodes.MotoV2Bak] = "CSDL_MOTO_BAK",
            [CsdtRealtimeProfileCodes.MotoV1Bak] = "CSDL_MOTO_V1_BAK",
        };

    private readonly IConfiguration _configuration;
    private readonly IConnectionSettingsProvider _connectionSettings;

    public CsdtRealtimeConnectionResolver(
        IConfiguration configuration,
        IConnectionSettingsProvider connectionSettings)
    {
        _configuration = configuration;
        _connectionSettings = connectionSettings;
    }

    public async Task<CsdtRealtimeResolvedRoute> ResolveAsync(
        CsdtRealtimeRouteDefinition route,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!IsFixedAllowedRoute(route))
        {
            throw new InvalidOperationException("Realtime route is not in the fixed server allowlist.");
        }

        var source = ResolveProfile(route.SourceProfileCode, route.SourceDatabaseName);
        var target = ResolveProfile(route.TargetProfileCode, route.TargetDatabaseName);
        var sourceBuilder = new SqlConnectionStringBuilder(source);
        var targetBuilder = new SqlConnectionStringBuilder(target);
        if (string.Equals(
                sourceBuilder.DataSource,
                targetBuilder.DataSource,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                sourceBuilder.InitialCatalog,
                targetBuilder.InitialCatalog,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Realtime source and target must be different fixed databases.");
        }

        var state = await _connectionSettings.GetQlhvAppConnectionAsync(cancellationToken);
        if (!state.IsUsable || string.IsNullOrWhiteSpace(state.ConnectionString))
        {
            throw new InvalidOperationException("QLHV_APP connection is not configured.");
        }

        ValidateInitialCatalog(state.ConnectionString, "QLHV_APP");
        return new CsdtRealtimeResolvedRoute(route, source, target, state.ConnectionString);
    }

    private static bool IsFixedAllowedRoute(CsdtRealtimeRouteDefinition route)
    {
        if (CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                route.StreamCode,
                route.SourceProfileCode,
                route.TargetProfileCode,
                out var forward) &&
            forward == route)
        {
            return true;
        }

        return CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                   route.StreamCode,
                   route.TargetProfileCode,
                   route.SourceProfileCode,
                   out forward) &&
               forward.Reverse() == route;
    }

    private string ResolveProfile(string profileCode, string expectedDatabase)
    {
        if (!ConnectionNames.TryGetValue(profileCode, out var connectionName))
        {
            throw new InvalidOperationException("Realtime profile is not in the fixed allowlist.");
        }

        var connectionString = _configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString) ||
            connectionString.Contains("__", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Realtime profile {profileCode} is not configured.");
        }

        ValidateInitialCatalog(connectionString, expectedDatabase);
        return connectionString;
    }

    internal static void ValidateInitialCatalog(string connectionString, string expectedDatabase)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog) ||
            !string.Equals(
                builder.InitialCatalog,
                expectedDatabase,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Realtime connection Initial Catalog does not match the fixed route.");
        }
    }
}
