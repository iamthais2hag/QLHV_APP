using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.CsdtConnections;
using QLHV.Application.CsdtConnections.Dtos;
using QLHV.Application.Sync.Connections;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.CsdtConnections;

public sealed class CsdtConnectionProfileRepository : ICsdtConnectionProfileRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public CsdtConnectionProfileRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<CsdtConnectionProfileRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<CsdtConnectionProfileRecord>(new CommandDefinition(
            SelectAllSql,
            new { ProfileCodes = CsdtConnectionProfileCodes.FixedProfiles.ToArray() },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<CsdtConnectionProfileRecord?> GetByCodeAsync(
        string profileCode,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        return await connection.QuerySingleOrDefaultAsync<CsdtConnectionProfileRecord>(new CommandDefinition(
            SelectByCodeSql,
            new { ProfileCode = profileCode },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    public async Task<CsdtConnectionProfileRecord?> SaveAsync(
        string profileCode,
        SaveCsdtConnectionProfileRequest request,
        byte[]? passwordCipherText,
        bool updatePassword,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            SaveSql,
            new
            {
                ProfileCode = profileCode,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? profileCode
                    : request.DisplayName.Trim(),
                ServerName = DbTrim(request.ServerName),
                DatabaseName = DbTrim(request.DatabaseName),
                request.AuthMode,
                UserName = string.Equals(request.AuthMode, "Windows", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : DbTrim(request.UserName),
                PasswordCipherText = passwordCipherText,
                UpdatePassword = updatePassword,
                request.IsActive,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        return await GetByCodeAsync(profileCode, cancellationToken);
    }

    public async Task UpdateTestResultAsync(
        string profileCode,
        DateTime testedAt,
        string status,
        string safeMessage,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            UpdateTestResultSql,
            new
            {
                ProfileCode = profileCode,
                LastTestedAt = testedAt,
                LastTestStatus = status,
                LastTestMessage = safeMessage,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    public async Task AddAuditAsync(
        string profileCode,
        string action,
        string resultStatus,
        string safeMessage,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            InsertAuditSql,
            new
            {
                ProfileCode = profileCode,
                Action = action,
                ResultStatus = resultStatus,
                SafeMessage = safeMessage,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
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

    private static string? DbTrim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string SelectColumns = @"
Id,
ProfileCode,
DisplayName,
ProfileGroup,
ServerName,
DatabaseName,
AuthMode,
UserName,
PasswordCipherText,
PasswordUpdatedAt,
IsPasswordConfigured,
IsActive,
LastTestedAt,
LastTestStatus,
LastTestMessage,
CreatedAt,
UpdatedAt";

    private const string SelectAllSql = $@"
SELECT {SelectColumns}
FROM dbo.App_CsdtConnectionProfile
WHERE ProfileCode IN @ProfileCodes
ORDER BY CASE ProfileCode
    WHEN N'CSDT_MOTO' THEN 1
    WHEN N'CSDT_OTO' THEN 2
    WHEN N'CSDT_MOTO_GPLX' THEN 3
    WHEN N'CSDT_OTO_GPLX' THEN 4
    WHEN N'DATA_V1' THEN 5
    WHEN N'DATA_V2' THEN 6
    WHEN N'QLHV_APP' THEN 7
    ELSE 99
END;";

    private const string SelectByCodeSql = $@"
SELECT {SelectColumns}
FROM dbo.App_CsdtConnectionProfile
WHERE ProfileCode = @ProfileCode;";

    private const string SaveSql = @"
UPDATE dbo.App_CsdtConnectionProfile
SET
    DisplayName = @DisplayName,
    ServerName = @ServerName,
    DatabaseName = @DatabaseName,
    AuthMode = @AuthMode,
    UserName = @UserName,
    PasswordCipherText = CASE WHEN @UpdatePassword = 1 THEN @PasswordCipherText ELSE PasswordCipherText END,
    PasswordUpdatedAt = CASE WHEN @UpdatePassword = 1 THEN SYSUTCDATETIME() ELSE PasswordUpdatedAt END,
    IsPasswordConfigured = CASE WHEN @UpdatePassword = 1 THEN CAST(1 AS bit) ELSE IsPasswordConfigured END,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE ProfileCode = @ProfileCode;";

    private const string UpdateTestResultSql = @"
UPDATE dbo.App_CsdtConnectionProfile
SET
    LastTestedAt = @LastTestedAt,
    LastTestStatus = @LastTestStatus,
    LastTestMessage = @LastTestMessage,
    UpdatedAt = SYSUTCDATETIME()
WHERE ProfileCode = @ProfileCode;";

    private const string InsertAuditSql = @"
INSERT INTO dbo.App_CsdtConnectionProfileAudit
(
    ProfileId,
    ProfileCode,
    Action,
    ChangedBy,
    ResultStatus,
    SafeMessage
)
SELECT
    Id,
    ProfileCode,
    @Action,
    N'QLHV_APP',
    @ResultStatus,
    @SafeMessage
FROM dbo.App_CsdtConnectionProfile
WHERE ProfileCode = @ProfileCode;";
}
