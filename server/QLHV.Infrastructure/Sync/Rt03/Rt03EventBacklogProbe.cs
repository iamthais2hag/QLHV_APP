using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Infrastructure.Sync.Rt03;

/// <summary>
/// Reads only the durable checkpoint and Change Tracking watermarks. It never
/// materializes source/target business rows and is therefore safe for the
/// event-driven idle decision.
/// </summary>
public sealed class Rt03EventBacklogProbe : IRt03EventBacklogProbe
{
    private readonly ICsdtConnectionProfileRepository _profiles;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly IConnectionSettingsProvider _connections;
    private readonly SyncOptions _options;

    public Rt03EventBacklogProbe(
        ICsdtConnectionProfileRepository profiles,
        IConnectionPasswordProtector passwordProtector,
        IConnectionSettingsProvider connections,
        IOptions<SyncOptions> options)
    {
        _profiles = profiles;
        _passwordProtector = passwordProtector;
        _connections = connections;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Rt03RealtimeProfileBacklog>> ReadAsync(
        IReadOnlyCollection<string> sourceProfileCodes,
        CancellationToken cancellationToken = default)
    {
        if (sourceProfileCodes.Count == 0)
        {
            return [];
        }

        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new Rt03SafetyException(
                Rt03RealtimeMasterErrors.BacklogProbeFailed,
                "QLHV_APP checkpoint connection is unavailable.");
        }

        await using var targetConnection = new SqlConnection(target.ConnectionString);
        await targetConnection.OpenAsync(cancellationToken);
        var checkpoints = (await targetConnection.QueryAsync<CheckpointRow>(
            new CommandDefinition(
                CheckpointSql,
                new { SourceProfileCodes = sourceProfileCodes.ToArray() },
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken)))
            .ToDictionary(item => item.SourceProfileCode, StringComparer.Ordinal);

        var results = new List<Rt03RealtimeProfileBacklog>(sourceProfileCodes.Count);
        foreach (var profileCode in sourceProfileCodes)
        {
            var route = Rt01ShadowRouteCatalog.Ordered.SingleOrDefault(item =>
                string.Equals(item.SourceProfileCode, profileCode, StringComparison.Ordinal))
                ?? throw new Rt03SafetyException(
                    Rt03Errors.ProductionIdentityRejected,
                    "Backlog profile is outside the live OTO/MOTO allowlist.");
            if (!checkpoints.TryGetValue(profileCode, out var checkpoint))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.CheckpointConflict,
                    $"Checkpoint is missing for {profileCode}.");
            }

            var sourceConnectionString = await ResolveSourceAsync(route, cancellationToken);
            await using var sourceConnection = new SqlConnection(sourceConnectionString);
            await sourceConnection.OpenAsync(cancellationToken);
            var capability = await sourceConnection.QuerySingleAsync<CapabilityRow>(
                new CommandDefinition(
                    CapabilitySql,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken));
            results.Add(new Rt03RealtimeProfileBacklog(
                profileCode,
                checkpoint.CheckpointVersion,
                capability.CurrentVersion,
                capability.MinimumValidVersion));
        }

        return results;
    }

    private async Task<string> ResolveSourceAsync(
        Rt01ShadowRoute route,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByCodeAsync(route.SourceProfileCode, cancellationToken);
        if (profile is null || !profile.IsActive ||
            string.IsNullOrWhiteSpace(profile.ServerName) ||
            !string.Equals(profile.DatabaseName, route.SourceDatabaseName,
                StringComparison.Ordinal))
        {
            throw new Rt03SafetyException(
                Rt03Errors.ProductionIdentityRejected,
                $"{route.SourceProfileCode} does not resolve to the exact live database.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = profile.DatabaseName,
            ConnectTimeout = Math.Clamp(_options.TimeoutSeconds, 5, 30),
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
        };
        if (string.Equals(profile.AuthMode, "SqlLogin", StringComparison.OrdinalIgnoreCase))
        {
            if (!profile.IsPasswordConfigured || profile.PasswordCipherText is null ||
                string.IsNullOrWhiteSpace(profile.UserName) || !_passwordProtector.IsAvailable)
            {
                throw new Rt03SafetyException(
                    Rt03Errors.ProductionIdentityRejected,
                    "Live source credentials are unavailable.");
            }

            builder.IntegratedSecurity = false;
            builder.UserID = profile.UserName;
            builder.Password = _passwordProtector.Unprotect(profile.PasswordCipherText);
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private sealed class CheckpointRow
    {
        public string SourceProfileCode { get; init; } = string.Empty;
        public long CheckpointVersion { get; init; }
    }

    private sealed class CapabilityRow
    {
        public long CurrentVersion { get; init; }
        public long MinimumValidVersion { get; init; }
    }

    internal const string CheckpointSql = """
        SELECT SourceProfileCode,
               CONVERT(bigint, SourceChangeTrackingVersion) AS CheckpointVersion
        FROM dbo.App_QlhvDirectRealtimeApplyCheckpoint
        WHERE SourceProfileCode IN @SourceProfileCodes
          AND Mode=N'DIRECT_REALTIME_APPLY'
          AND EnvironmentId=N'PRODUCTION';
        """;

    internal const string CapabilitySql = """
        SELECT CONVERT(bigint, CHANGE_TRACKING_CURRENT_VERSION()) AS CurrentVersion,
               CONVERT(bigint,
                 (SELECT MAX(item.MinVersion)
                  FROM (VALUES
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.NguoiLX_HoSo'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.KhoaHoc'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.DM_HangDT'))),
                    (CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.DM_DVHC')))
                  ) item(MinVersion))) AS MinimumValidVersion;
        """;
}
