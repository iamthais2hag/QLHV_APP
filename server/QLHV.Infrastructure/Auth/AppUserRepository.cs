using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Connections;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Auth;

public sealed class AppUserRepository : IAppUserRepository
{
    private readonly IConnectionSettingsProvider _connections;
    private readonly AppSyncOptions _options;

    public AppUserRepository(
        IConnectionSettingsProvider connections,
        IOptions<AppSyncOptions> options)
    {
        _connections = connections;
        _options = options.Value;
    }

    public async Task<AppUserCredential?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
        => await FindAsync(
            FindByUsernameSql,
            new { Username = username },
            cancellationToken);

    public async Task<AppUserCredential?> FindByIdAsync(
        long userId,
        CancellationToken cancellationToken = default)
        => await FindAsync(
            FindByIdSql,
            new { UserId = userId },
            cancellationToken);

    private async Task<AppUserCredential?> FindAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var rows = (await connection.QueryAsync<AppUserRow>(new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).AsList();
        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows[0];
        return new AppUserCredential
        {
            Id = first.Id,
            Username = first.Username,
            DisplayName = first.DisplayName,
            PasswordHash = first.PasswordHash ?? string.Empty,
            IsActive = first.IsActive,
            IsDeleted = first.IsDeleted,
            FailedLoginCount = first.FailedLoginCount,
            UpdatedAtUtc = first.UpdatedAtUtc,
            Roles = rows
                .Select(row => row.RoleCode)
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public Task RecordSuccessfulLoginAsync(
        long userId,
        CancellationToken cancellationToken = default) =>
        ExecuteUserUpdateAsync(RecordSuccessfulLoginSql, new { UserId = userId }, cancellationToken);

    public Task RecordFailedLoginAsync(
        long userId,
        DateTime failedAtUtc,
        DateTime resetCutoffUtc,
        CancellationToken cancellationToken = default) =>
        ExecuteUserUpdateAsync(
            RecordFailedLoginSql,
            new
            {
                UserId = userId,
                FailedAtUtc = failedAtUtc,
                ResetCutoffUtc = resetCutoffUtc,
            },
            cancellationToken);

    public Task UpdatePasswordHashAsync(
        long userId,
        string passwordHash,
        CancellationToken cancellationToken = default) =>
        ExecuteUserUpdateAsync(
            UpdatePasswordHashSql,
            new { UserId = userId, PasswordHash = passwordHash },
            cancellationToken);

    public async Task<FirstAdminCreateResult> TryCreateFirstAdminAsync(
        string username,
        string displayName,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var adminCount = await connection.QuerySingleAsync<long>(new CommandDefinition(
            CountAdminsForSeedSql,
            new { RoleCode = AppRoles.Admin },
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (adminCount != 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new FirstAdminCreateResult(FirstAdminCreateStatus.AdminAlreadyExists, null);
        }

        var usernameCount = await connection.QuerySingleAsync<long>(new CommandDefinition(
            CountUsernameForSeedSql,
            new { Username = username },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (usernameCount != 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new FirstAdminCreateResult(FirstAdminCreateStatus.UsernameAlreadyExists, null);
        }

        var adminRoleId = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            FindAdminRoleSql,
            new { RoleCode = AppRoles.Admin },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (adminRoleId is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException(
                "Admin role is unavailable. Apply the App_User authorization database patch first.");
        }

        var userId = await connection.QuerySingleAsync<long>(new CommandDefinition(
            InsertFirstAdminSql,
            new
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = passwordHash,
            },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            InsertFirstAdminRoleSql,
            new { UserId = userId, RoleId = adminRoleId.Value },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(CancellationToken.None);
        return new FirstAdminCreateResult(FirstAdminCreateStatus.Created, userId);
    }

    private async Task ExecuteUserUpdateAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private async Task<string> ResolveQlhvAppAsync(CancellationToken cancellationToken)
    {
        var target = await _connections.GetQlhvAppConnectionAsync(cancellationToken);
        if (!target.IsUsable || string.IsNullOrWhiteSpace(target.ConnectionString))
        {
            throw new InvalidOperationException(
                "QLHV_APP does not have a usable connection for account authentication.");
        }

        return target.ConnectionString;
    }

    private sealed class AppUserRow
    {
        public long Id { get; init; }

        public string Username { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string? PasswordHash { get; init; }

        public bool IsActive { get; init; }

        public bool IsDeleted { get; init; }

        public int FailedLoginCount { get; init; }

        public DateTime? UpdatedAtUtc { get; init; }

        public string? RoleCode { get; init; }
    }

    private const string FindByUsernameSql = """
SELECT
    u.UserId AS Id,
    u.UserName AS Username,
    u.DisplayName,
    u.PasswordHash,
    u.IsActive,
    u.IsDeleted,
    u.FailedLoginCount,
    u.UpdatedAt AS UpdatedAtUtc,
    r.RoleCode
FROM dbo.App_User AS u
LEFT JOIN dbo.App_UserRole AS ur
    ON ur.UserId = u.UserId
LEFT JOIN dbo.App_Role AS r
    ON r.RoleId = ur.RoleId
   AND r.IsDeleted = 0
WHERE u.UserName = @Username;
""";

    private const string FindByIdSql = """
SELECT
    u.UserId AS Id,
    u.UserName AS Username,
    u.DisplayName,
    u.PasswordHash,
    u.IsActive,
    u.IsDeleted,
    u.FailedLoginCount,
    u.UpdatedAt AS UpdatedAtUtc,
    r.RoleCode
FROM dbo.App_User AS u
LEFT JOIN dbo.App_UserRole AS ur
    ON ur.UserId = u.UserId
LEFT JOIN dbo.App_Role AS r
    ON r.RoleId = ur.RoleId
   AND r.IsDeleted = 0
WHERE u.UserId = @UserId;
""";

    private const string RecordSuccessfulLoginSql = """
UPDATE dbo.App_User
SET LastLoginAt = SYSUTCDATETIME(),
    FailedLoginCount = 0,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = N'auth-login'
WHERE UserId = @UserId;
""";

    private const string RecordFailedLoginSql = """
UPDATE dbo.App_User
SET FailedLoginCount =
        CASE
            WHEN UpdatedAt IS NULL OR UpdatedAt < @ResetCutoffUtc THEN 1
            WHEN FailedLoginCount < 2147483647 THEN FailedLoginCount + 1
            ELSE FailedLoginCount
        END,
    UpdatedAt = @FailedAtUtc,
    UpdatedBy = N'auth-login'
WHERE UserId = @UserId;
""";

    private const string UpdatePasswordHashSql = """
UPDATE dbo.App_User
SET PasswordHash = @PasswordHash,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = N'auth-rehash'
WHERE UserId = @UserId;
""";

    private const string CountAdminsForSeedSql = """
SELECT COUNT_BIG(*)
FROM dbo.App_User AS u WITH (UPDLOCK, HOLDLOCK)
INNER JOIN dbo.App_UserRole AS ur WITH (UPDLOCK, HOLDLOCK)
    ON ur.UserId = u.UserId
INNER JOIN dbo.App_Role AS r WITH (UPDLOCK, HOLDLOCK)
    ON r.RoleId = ur.RoleId
WHERE u.IsDeleted = 0
  AND r.IsDeleted = 0
  AND r.RoleCode = @RoleCode;
""";

    private const string CountUsernameForSeedSql = """
SELECT COUNT_BIG(*)
FROM dbo.App_User WITH (UPDLOCK, HOLDLOCK)
WHERE UserName = @Username;
""";

    private const string FindAdminRoleSql = """
SELECT RoleId
FROM dbo.App_Role WITH (UPDLOCK, HOLDLOCK)
WHERE RoleCode = @RoleCode
  AND IsDeleted = 0;
""";

    private const string InsertFirstAdminSql = """
INSERT INTO dbo.App_User
(
    UserName,
    DisplayName,
    PasswordHash,
    IsActive,
    FailedLoginCount,
    IsDeleted,
    CreatedAt,
    CreatedBy
)
OUTPUT INSERTED.UserId
VALUES
(
    @Username,
    @DisplayName,
    @PasswordHash,
    1,
    0,
    0,
    SYSUTCDATETIME(),
    N'seed-admin'
);
""";

    private const string InsertFirstAdminRoleSql = """
INSERT INTO dbo.App_UserRole (UserId, RoleId, CreatedAt, CreatedBy)
VALUES (@UserId, @RoleId, SYSUTCDATETIME(), N'seed-admin');
""";
}
