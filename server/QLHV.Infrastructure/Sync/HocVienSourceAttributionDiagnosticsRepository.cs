using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Infrastructure.Sync;

public sealed class HocVienSourceAttributionDiagnosticsRepository : IHocVienSourceAttributionDiagnosticsRepository
{
    private const string AuthModeSqlLogin = "SqlLogin";

    private readonly IConnectionSettingsProvider _connections;
    private readonly ICsdtConnectionProfileRepository _profileRepository;
    private readonly IConnectionPasswordProtector _passwordProtector;
    private readonly SyncOptions _options;

    public HocVienSourceAttributionDiagnosticsRepository(
        IConnectionSettingsProvider connections,
        ICsdtConnectionProfileRepository profileRepository,
        IConnectionPasswordProtector passwordProtector,
        IOptions<SyncOptions> options)
    {
        _connections = connections;
        _profileRepository = profileRepository;
        _passwordProtector = passwordProtector;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<HocVienTargetAttributionKeyDto>> ReadTargetKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);

        return await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
        {
            await using var connection = new SqlConnection(connectionString);
            var rows = await connection.QueryAsync<HocVienTargetAttributionKeyDto>(new CommandDefinition(
                TargetMaDkSql,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: ct));

            return (IReadOnlyList<HocVienTargetAttributionKeyDto>)rows.ToList();
        }, cancellationToken);
    }

    public async Task<HocVienSourceMaDkReadResultDto> ReadSourceKeysAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = CsdtConnectionProfileCodes.Normalize(sourceProfileCode);
        try
        {
            var profile = await _profileRepository.GetByCodeAsync(normalized, cancellationToken);
            var resolved = ResolveProfileConnectionString(profile, normalized);
            if (!resolved.CanRead)
            {
                return Failed(normalized, resolved.Issue, "SOURCE_PROFILE_NOT_CONFIGURED");
            }

            var keys = await SyncRetryPolicyFactory.CreateDefault(_options.MaxRetryAttempts).ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(resolved.ConnectionString);
                var rows = await connection.QueryAsync<string>(new CommandDefinition(
                    SourceMaDkSql,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: ct));

                return rows
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }, cancellationToken);

            return new HocVienSourceMaDkReadResultDto
            {
                SourceProfileCode = normalized,
                CanRead = true,
                MaDks = keys,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                normalized,
                $"Khong doc duoc MaDK tu profile {normalized}. Chi tiet: {ex.GetType().Name}.",
                "SOURCE_PROFILE_READ_FAILED");
        }
    }

    private async Task<string> ResolveQlhvAppAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP chua co cau hinh ket noi dung duoc (thieu hoac dang la placeholder).");
        }

        return target.ConnectionString;
    }

    private ProfileConnectionResolution ResolveProfileConnectionString(
        CsdtConnectionProfileRecord? profile,
        string sourceProfileCode)
    {
        if (profile is null)
        {
            return ProfileConnectionResolution.Failed($"Profile {sourceProfileCode} khong ton tai.");
        }

        if (string.IsNullOrWhiteSpace(profile.ServerName) || string.IsNullOrWhiteSpace(profile.DatabaseName))
        {
            return ProfileConnectionResolution.Failed(
                $"Profile {sourceProfileCode} chua co ServerName hoac DatabaseName.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = profile.ServerName,
                InitialCatalog = profile.DatabaseName,
                ConnectTimeout = Math.Min(Math.Max(_options.TimeoutSeconds, 5), 30),
                TrustServerCertificate = true,
                MultipleActiveResultSets = false,
            };

            if (string.Equals(profile.AuthMode, AuthModeSqlLogin, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(profile.UserName) ||
                    profile.PasswordCipherText is null ||
                    !profile.IsPasswordConfigured)
                {
                    return ProfileConnectionResolution.Failed(
                        $"Profile {sourceProfileCode} SQL Login chua du UserName/password da ma hoa.");
                }

                if (!_passwordProtector.IsAvailable)
                {
                    return ProfileConnectionResolution.Failed(
                        $"Profile {sourceProfileCode} can giai ma password nhung password protector chua san sang.");
                }

                builder.IntegratedSecurity = false;
                builder.UserID = profile.UserName;
                builder.Password = _passwordProtector.Unprotect(profile.PasswordCipherText);
            }
            else
            {
                builder.IntegratedSecurity = true;
            }

            return ProfileConnectionResolution.Success(builder.ConnectionString);
        }
        catch (Exception ex)
        {
            return ProfileConnectionResolution.Failed(
                $"Khong tao duoc ket noi an toan cho profile {sourceProfileCode}. Chi tiet: {ex.GetType().Name}.");
        }
    }

    private static HocVienSourceMaDkReadResultDto Failed(
        string sourceProfileCode,
        string issue,
        string code) => new()
    {
        SourceProfileCode = sourceProfileCode,
        CanRead = false,
        Issue = issue,
        Error = new SyncErrorDto
        {
            Code = code,
            Message = issue,
        },
    };

    private const string TargetMaDkSql = @"
SELECT
    LTRIM(RTRIM(MaDK)) AS MaDK,
    NULLIF(LTRIM(RTRIM(SourceProfileCode)), '') AS SourceProfileCode
FROM dbo.App_HocVien
WHERE IsDeleted = 0
  AND NULLIF(LTRIM(RTRIM(MaDK)), '') IS NOT NULL;";

    private const string SourceMaDkSql = @"
SELECT DISTINCT LTRIM(RTRIM(nlx.MaDK)) AS MaDK
FROM dbo.NguoiLX AS nlx
INNER JOIN dbo.NguoiLX_HoSo AS hs ON hs.MaDK = nlx.MaDK
WHERE NULLIF(LTRIM(RTRIM(nlx.MaDK)), '') IS NOT NULL;";

    private sealed class ProfileConnectionResolution
    {
        public bool CanRead { get; private init; }

        public string ConnectionString { get; private init; } = string.Empty;

        public string Issue { get; private init; } = string.Empty;

        public static ProfileConnectionResolution Success(string connectionString) => new()
        {
            CanRead = true,
            ConnectionString = connectionString,
        };

        public static ProfileConnectionResolution Failed(string issue) => new()
        {
            CanRead = false,
            Issue = issue,
        };
    }
}
