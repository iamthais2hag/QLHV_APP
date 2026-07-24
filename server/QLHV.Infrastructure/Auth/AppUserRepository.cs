using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Connections;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Infrastructure.Auth;

public sealed class AppUserRepository : IAppUserRepository, IAppUserManagementRepository
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
            new { NormalizedUsername = AppUserManagementService.NormalizeUsername(username) },
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
            SecurityStamp = first.SecurityStamp,
            IsActive = first.IsActive,
            IsDeleted = first.IsDeleted,
            FailedLoginCount = first.FailedLoginCount,
            LastFailedLoginAtUtc = first.LastFailedLoginAtUtc,
            UpdatedAtUtc = first.UpdatedAtUtc,
            MustChangePassword = first.MustChangePassword,
            Roles = rows
                .Select(row => row.RoleCode)
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public async Task<bool> TryRecordSuccessfulLoginAsync(
        long userId,
        string expectedPasswordHash,
        Guid expectedSecurityStamp,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            RecordSuccessfulLoginSql,
            new
            {
                UserId = userId,
                ExpectedPasswordHash = expectedPasswordHash,
                ExpectedSecurityStamp = expectedSecurityStamp,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        return affected == 1;
    }

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

    public async Task<bool> TryUpdatePasswordHashAsync(
        long userId,
        string expectedPasswordHash,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            UpdatePasswordHashSql,
            new
            {
                UserId = userId,
                ExpectedPasswordHash = expectedPasswordHash,
                PasswordHash = passwordHash,
            },
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        return affected == 1;
    }

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
            new { NormalizedUsername = AppUserManagementService.NormalizeUsername(username) },
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

    public async Task<IReadOnlyList<AppUserListItemDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        return (await connection.QueryAsync<AppUserListItemDto>(new CommandDefinition(
            ListUsersSql,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<AppUserManagementResult> CreateAsync(
        AppUserCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await AcquireManagementLockAsync(connection, transaction, cancellationToken);
        var usernameExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            UsernameExistsSql,
            new { command.NormalizedUsername },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (usernameExists)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.UsernameExists,
                "Tên đăng nhập đã tồn tại.");
        }

        var roleId = await FindRoleIdAsync(
            connection,
            transaction,
            command.Role,
            cancellationToken);
        if (roleId is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.RoleUnavailable,
                "Vai trò chưa được cấu hình trong QLHV_APP.");
        }

        long userId;
        try
        {
            userId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                InsertManagedUserSql,
                command,
                transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                InsertManagedUserRoleSql,
                new
                {
                    UserId = userId,
                    RoleId = roleId.Value,
                    command.ActorUsername,
                },
                transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
            await InsertAuditAsync(
                connection,
                transaction,
                "CREATE",
                userId,
                command.ActorUserId,
                command.ActorUsername,
                command.Role,
                command.IsActive,
                command.MustChangePassword,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.UsernameExists,
                "Tên đăng nhập đã tồn tại.");
        }

        var created = await ReadListItemAsync(
            connection,
            transaction,
            userId,
            cancellationToken);
        if (created is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.Conflict,
                "Tài khoản chưa thể được xác nhận sau khi tạo; giao dịch đã được hoàn tác.");
        }

        await transaction.CommitAsync(CancellationToken.None);
        return AppUserManagementResult.Success(created);
    }

    public async Task<AppUserManagementResult> UpdateAsync(
        AppUserUpdateCommand command,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await AcquireManagementLockAsync(connection, transaction, cancellationToken);
        var target = await connection.QuerySingleOrDefaultAsync<AppUserTargetRow>(
            new CommandDefinition(
                TargetUserForUpdateSql,
                new { command.UserId },
                transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
        if (target is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.NotFound,
                "Không tìm thấy tài khoản.");
        }

        if (command.ActorUserId == command.UserId && !command.IsActive)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.SelfDeactivationDenied,
                "Không thể tự khóa tài khoản đang đăng nhập.");
        }

        var removesActiveAdmin =
            target.IsActive &&
            string.Equals(target.Role, AppRoles.Admin, StringComparison.Ordinal) &&
            (!command.IsActive ||
             !string.Equals(command.Role, AppRoles.Admin, StringComparison.Ordinal));
        if (removesActiveAdmin)
        {
            var otherActiveAdminHashes = (await connection.QueryAsync<string>(
                new CommandDefinition(
                    CountOtherActiveAdminsSql,
                    new
                    {
                        command.UserId,
                        AdminRole = AppRoles.Admin,
                    },
                    transaction,
                    commandTimeout: _options.TimeoutSeconds,
                    cancellationToken: cancellationToken))).AsList();
            if (!otherActiveAdminHashes.Any(AppPasswordHashFormat.IsSupported))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return AppUserManagementResult.Failure(
                    AppUserManagementStatus.LastActiveAdminDenied,
                    "Không thể khóa hoặc hạ quyền Admin đang hoạt động cuối cùng.");
            }
        }

        var roleId = await FindRoleIdAsync(
            connection,
            transaction,
            command.Role,
            cancellationToken);
        if (roleId is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.RoleUnavailable,
                "Vai trò chưa được cấu hình trong QLHV_APP.");
        }

        var rotateSecurityStamp =
            target.IsActive != command.IsActive ||
            target.MustChangePassword != command.MustChangePassword ||
            !string.Equals(target.Role, command.Role, StringComparison.Ordinal);
        var updateParameters = new DynamicParameters(command);
        updateParameters.Add("RotateSecurityStamp", rotateSecurityStamp);
        await connection.ExecuteAsync(new CommandDefinition(
            UpdateManagedUserSql,
            updateParameters,
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            DeleteManagedUserRolesSql,
            new { command.UserId },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            InsertManagedUserRoleSql,
            new
            {
                command.UserId,
                RoleId = roleId.Value,
                command.ActorUsername,
            },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        await InsertAuditAsync(
            connection,
            transaction,
            "UPDATE",
            command.UserId,
            command.ActorUserId,
            command.ActorUsername,
            command.Role,
            command.IsActive,
            command.MustChangePassword,
            cancellationToken);

        var updated = await ReadListItemAsync(
            connection,
            transaction,
            command.UserId,
            cancellationToken);
        if (updated is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.Conflict,
                "Tài khoản chưa thể được xác nhận sau khi cập nhật; giao dịch đã được hoàn tác.");
        }

        await transaction.CommitAsync(CancellationToken.None);
        return AppUserManagementResult.Success(updated);
    }

    public async Task<AppUserManagementResult> ResetPasswordAsync(
        AppUserPasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireManagementLockAsync(connection, transaction, cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            ResetManagedUserPasswordSql,
            command,
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (affected != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.NotFound,
                "Không tìm thấy tài khoản.");
        }

        await InsertAuditAsync(
            connection,
            transaction,
            "RESET_PASSWORD",
            command.UserId,
            command.ActorUserId,
            command.ActorUsername,
            null,
            null,
            command.MustChangePassword,
            cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        return AppUserManagementResult.Success();
    }

    public async Task<AppUserManagementResult> ChangeOwnPasswordAsync(
        AppUserOwnPasswordChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await ResolveQlhvAppAsync(cancellationToken);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await AcquireManagementLockAsync(connection, transaction, cancellationToken);
        var securityStamp = await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(
                ChangeOwnPasswordSql,
                command,
                transaction,
                commandTimeout: _options.TimeoutSeconds,
                cancellationToken: cancellationToken));
        if (securityStamp is null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.Conflict,
                "Tài khoản đã thay đổi; vui lòng đăng nhập lại.");
        }

        await InsertAuditAsync(
            connection,
            transaction,
            "CHANGE_PASSWORD",
            command.UserId,
            command.UserId,
            command.ActorUsername,
            null,
            null,
            false,
            cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        return AppUserManagementResult.Success(securityStamp: securityStamp);
    }

    private async Task InsertAuditAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string actionCode,
        long targetUserId,
        long actorUserId,
        string actorUsername,
        string? newRole,
        bool? newIsActive,
        bool? newMustChangePassword,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            InsertUserManagementAuditSql,
            new
            {
                ActionCode = actionCode,
                TargetUserId = targetUserId,
                ActorUserId = actorUserId,
                ActorUsername = actorUsername,
                NewRole = newRole,
                NewIsActive = newIsActive,
                NewMustChangePassword = newMustChangePassword,
            },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private async Task AcquireManagementLockAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = await connection.QuerySingleAsync<int>(new CommandDefinition(
            AcquireManagementLockSql,
            transaction: transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));
        if (result < 0)
        {
            throw new InvalidOperationException(
                "Không thể khóa vùng quản lý tài khoản một cách an toàn.");
        }
    }

    private async Task<int?> FindRoleIdAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        string role,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            FindRoleIdSql,
            new { RoleCode = role },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

    private async Task<AppUserListItemDto?> ReadListItemAsync(
        SqlConnection connection,
        IDbTransaction transaction,
        long userId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<AppUserListItemDto>(new CommandDefinition(
            ReadUserListItemSql,
            new { UserId = userId },
            transaction,
            commandTimeout: _options.TimeoutSeconds,
            cancellationToken: cancellationToken));

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

        public Guid SecurityStamp { get; init; }

        public bool IsActive { get; init; }

        public bool IsDeleted { get; init; }

        public int FailedLoginCount { get; init; }

        public DateTime? LastFailedLoginAtUtc { get; init; }

        public DateTime? UpdatedAtUtc { get; init; }

        public bool MustChangePassword { get; init; }

        public string? RoleCode { get; init; }
    }

    private sealed class AppUserTargetRow
    {
        public bool IsActive { get; init; }

        public bool MustChangePassword { get; init; }

        public string Role { get; init; } = string.Empty;
    }

    private const string FindByUsernameSql = """
SELECT
    u.UserId AS Id,
    u.UserName AS Username,
    u.DisplayName,
    u.PasswordHash,
    u.SecurityStamp,
    u.IsActive,
    u.IsDeleted,
    u.FailedLoginCount,
    u.LastFailedLoginAt AS LastFailedLoginAtUtc,
    u.UpdatedAt AS UpdatedAtUtc,
    u.MustChangePassword,
    r.RoleCode
FROM dbo.App_User AS u
LEFT JOIN dbo.App_UserRole AS ur
    ON ur.UserId = u.UserId
LEFT JOIN dbo.App_Role AS r
    ON r.RoleId = ur.RoleId
   AND r.IsDeleted = 0
WHERE u.NormalizedUserName = @NormalizedUsername;
""";

    private const string FindByIdSql = """
SELECT
    u.UserId AS Id,
    u.UserName AS Username,
    u.DisplayName,
    u.PasswordHash,
    u.SecurityStamp,
    u.IsActive,
    u.IsDeleted,
    u.FailedLoginCount,
    u.LastFailedLoginAt AS LastFailedLoginAtUtc,
    u.UpdatedAt AS UpdatedAtUtc,
    u.MustChangePassword,
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
    LastFailedLoginAt = NULL,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = N'auth-login'
WHERE UserId = @UserId
  AND PasswordHash = @ExpectedPasswordHash
  AND SecurityStamp = @ExpectedSecurityStamp
  AND IsActive = 1
  AND IsDeleted = 0;
""";

    private const string RecordFailedLoginSql = """
UPDATE dbo.App_User
SET FailedLoginCount =
        CASE
            WHEN LastFailedLoginAt IS NULL OR LastFailedLoginAt < @ResetCutoffUtc THEN 1
            WHEN FailedLoginCount < 2147483647 THEN FailedLoginCount + 1
            ELSE FailedLoginCount
        END,
    LastFailedLoginAt = @FailedAtUtc,
    UpdatedAt = @FailedAtUtc,
    UpdatedBy = N'auth-login'
WHERE UserId = @UserId;
""";

    private const string UpdatePasswordHashSql = """
UPDATE dbo.App_User
SET PasswordHash = @PasswordHash,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = N'auth-rehash'
WHERE UserId = @UserId
  AND PasswordHash = @ExpectedPasswordHash
  AND IsActive = 1
  AND IsDeleted = 0;
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
WHERE NormalizedUserName = @NormalizedUsername;
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

    private const string RoleProjectionSql = """
SELECT TOP (1)
    r.RoleCode
FROM dbo.App_UserRole AS ur
INNER JOIN dbo.App_Role AS r
    ON r.RoleId = ur.RoleId
   AND r.IsDeleted = 0
WHERE ur.UserId = u.UserId
ORDER BY
    CASE r.RoleCode
        WHEN N'Admin' THEN 0
        WHEN N'Employee' THEN 1
        WHEN N'Viewer' THEN 2
        ELSE 3
    END,
    r.RoleCode
""";

    private static readonly string ListUsersSql = $"""
SELECT
    u.UserId AS Id,
    u.UserName AS Username,
    u.DisplayName,
    COALESCE(selectedRole.RoleCode, N'') AS Role,
    u.IsActive,
    u.MustChangePassword,
    u.LastLoginAt AS LastLoginAtUtc,
    u.CreatedAt AS CreatedAtUtc,
    u.CreatedBy
FROM dbo.App_User AS u
OUTER APPLY
(
{RoleProjectionSql}
) AS selectedRole
WHERE u.IsDeleted = 0
ORDER BY u.UserName, u.UserId;
""";

    private static readonly string ReadUserListItemSql = $"""
SELECT
    u.UserId AS Id,
    u.UserName AS Username,
    u.DisplayName,
    COALESCE(selectedRole.RoleCode, N'') AS Role,
    u.IsActive,
    u.MustChangePassword,
    u.LastLoginAt AS LastLoginAtUtc,
    u.CreatedAt AS CreatedAtUtc,
    u.CreatedBy
FROM dbo.App_User AS u
OUTER APPLY
(
{RoleProjectionSql}
) AS selectedRole
WHERE u.UserId = @UserId
  AND u.IsDeleted = 0;
""";

    private const string AcquireManagementLockSql = """
DECLARE @result int;
EXEC @result = sys.sp_getapplock
    @Resource = N'QLHV_APP:USER_MANAGEMENT',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 10000;
SELECT @result;
""";

    private const string UsernameExistsSql = """
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.App_User WITH (UPDLOCK, HOLDLOCK)
    WHERE NormalizedUserName = @NormalizedUsername
) THEN 1 ELSE 0 END AS bit);
""";

    private const string FindRoleIdSql = """
SELECT RoleId
FROM dbo.App_Role WITH (UPDLOCK, HOLDLOCK)
WHERE RoleCode = @RoleCode
  AND IsDeleted = 0;
""";

    private const string InsertManagedUserSql = """
INSERT INTO dbo.App_User
(
    UserName,
    DisplayName,
    PasswordHash,
    IsActive,
    MustChangePassword,
    FailedLoginCount,
    LastFailedLoginAt,
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
    @IsActive,
    @MustChangePassword,
    0,
    NULL,
    0,
    SYSUTCDATETIME(),
    @ActorUsername
);
""";

    private const string InsertManagedUserRoleSql = """
INSERT INTO dbo.App_UserRole
(
    UserId,
    RoleId,
    CreatedAt,
    CreatedBy
)
VALUES
(
    @UserId,
    @RoleId,
    SYSUTCDATETIME(),
    @ActorUsername
);
""";

    private static readonly string TargetUserForUpdateSql = $"""
SELECT
    u.IsActive,
    u.MustChangePassword,
    COALESCE(selectedRole.RoleCode, N'') AS Role
FROM dbo.App_User AS u WITH (UPDLOCK, HOLDLOCK)
OUTER APPLY
(
{RoleProjectionSql}
) AS selectedRole
WHERE u.UserId = @UserId
  AND u.IsDeleted = 0;
""";

    private const string CountOtherActiveAdminsSql = """
SELECT DISTINCT u.PasswordHash
FROM dbo.App_User AS u WITH (UPDLOCK, HOLDLOCK)
INNER JOIN dbo.App_UserRole AS ur WITH (UPDLOCK, HOLDLOCK)
    ON ur.UserId = u.UserId
INNER JOIN dbo.App_Role AS r WITH (UPDLOCK, HOLDLOCK)
    ON r.RoleId = ur.RoleId
WHERE u.UserId <> @UserId
  AND u.IsActive = 1
  AND u.IsDeleted = 0
  AND u.SecurityStamp <> '00000000-0000-0000-0000-000000000000'
  AND r.IsDeleted = 0
  AND r.RoleCode = @AdminRole;
""";

    private const string UpdateManagedUserSql = """
UPDATE dbo.App_User
SET DisplayName = @DisplayName,
    IsActive = @IsActive,
    MustChangePassword = @MustChangePassword,
    SecurityStamp =
        CASE WHEN @RotateSecurityStamp = 1 THEN NEWID() ELSE SecurityStamp END,
    FailedLoginCount = CASE WHEN @IsActive = 1 THEN 0 ELSE FailedLoginCount END,
    LastFailedLoginAt = CASE WHEN @IsActive = 1 THEN NULL ELSE LastFailedLoginAt END,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @ActorUsername
WHERE UserId = @UserId
  AND IsDeleted = 0;
""";

    private const string DeleteManagedUserRolesSql = """
DELETE FROM dbo.App_UserRole
WHERE UserId = @UserId;
""";

    private const string ResetManagedUserPasswordSql = """
UPDATE dbo.App_User
SET PasswordHash = @PasswordHash,
    SecurityStamp = NEWID(),
    MustChangePassword = @MustChangePassword,
    FailedLoginCount = 0,
    LastFailedLoginAt = NULL,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @ActorUsername
WHERE UserId = @UserId
  AND IsDeleted = 0;
""";

    private const string ChangeOwnPasswordSql = """
DECLARE @changed TABLE (SecurityStamp uniqueidentifier NOT NULL);

UPDATE dbo.App_User
SET PasswordHash = @PasswordHash,
    SecurityStamp = NEWID(),
    MustChangePassword = 0,
    FailedLoginCount = 0,
    LastFailedLoginAt = NULL,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @ActorUsername
OUTPUT INSERTED.SecurityStamp INTO @changed (SecurityStamp)
WHERE UserId = @UserId
  AND PasswordHash = @ExpectedPasswordHash
  AND IsActive = 1
  AND IsDeleted = 0;

SELECT SecurityStamp
FROM @changed;
""";

    private const string InsertUserManagementAuditSql = """
INSERT INTO dbo.App_UserManagementAudit
(
    TargetUserId,
    ActorUserId,
    ActorUsername,
    ActionCode,
    NewRole,
    NewIsActive,
    NewMustChangePassword,
    CreatedAtUtc
)
VALUES
(
    @TargetUserId,
    @ActorUserId,
    @ActorUsername,
    @ActionCode,
    @NewRole,
    @NewIsActive,
    @NewMustChangePassword,
    SYSUTCDATETIME()
);
""";
}
