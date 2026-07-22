using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync;

namespace QLHV.Infrastructure.Sync;

public sealed class QlhvOperationConnectionResolver
{
    private const string SqlLogin = "SqlLogin";

    private readonly ICsdtConnectionProfileRepository _profiles;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly SyncOptions _syncOptions;

    public QlhvOperationConnectionResolver(
        ICsdtConnectionProfileRepository profiles,
        IConnectionPasswordProtector passwordProtector,
        IOptions<SyncOptions> syncOptions)
    {
        _profiles = profiles;
        _passwordProtector = passwordProtector;
        _syncOptions = syncOptions.Value;
    }

    internal async Task<ResolvedOperationProfile> ResolveAsync(
        string profileCode,
        string expectedDatabaseName,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetByCodeAsync(profileCode, cancellationToken)
            ?? throw new InvalidOperationException($"Profile {profileCode} khong ton tai.");
        if (!profile.IsActive)
        {
            throw new InvalidOperationException($"Profile {profileCode} dang tat.");
        }

        if (!string.Equals(profile.DatabaseName?.Trim(), expectedDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Profile {profileCode} phai khai bao database {expectedDatabaseName}.");
        }

        if (string.IsNullOrWhiteSpace(profile.ServerName))
        {
            throw new InvalidOperationException($"Profile {profileCode} thieu ServerName.");
        }

        var connectionString = BuildConnectionString(profile);
        return new ResolvedOperationProfile(
            profileCode,
            expectedDatabaseName,
            profile.ServerName.Trim(),
            connectionString);
    }

    private string BuildConnectionString(CsdtConnectionProfileRecord profile)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.ServerName,
            InitialCatalog = profile.DatabaseName,
            ConnectTimeout = Math.Clamp(_syncOptions.TimeoutSeconds, 5, 30),
            TrustServerCertificate = true,
            MultipleActiveResultSets = false,
            ApplicationName = "QLHV CSDT Operations",
        };

        if (string.Equals(profile.AuthMode, SqlLogin, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile.UserName) ||
                profile.PasswordCipherText is null ||
                !profile.IsPasswordConfigured ||
                !_passwordProtector.IsAvailable)
            {
                throw new InvalidOperationException(
                    $"Profile {profile.ProfileCode} SQL Login chua co credential dung duoc.");
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
}

internal sealed record ResolvedOperationProfile(
    string ProfileCode,
    string DatabaseName,
    string ConfiguredServerName,
    string ConnectionString)
{
    public string MasterConnectionString
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString)
            {
                InitialCatalog = "master",
            };
            return builder.ConnectionString;
        }
    }
}
