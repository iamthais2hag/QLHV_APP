namespace QLHV.Application.Auth;

public sealed class AppUserListItemDto
{
    public long Id { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool MustChangePassword { get; init; }

    public DateTime? LastLoginAtUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public string? CreatedBy { get; init; }
}

public sealed class CreateAppUserRequestDto
{
    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string TemporaryPassword { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; } = true;
}

public sealed class UpdateAppUserRequestDto
{
    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool MustChangePassword { get; set; }
}

public sealed class ResetAppUserPasswordRequestDto
{
    public string TemporaryPassword { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; } = true;
}

public sealed record AppUserCreateCommand(
    string Username,
    string NormalizedUsername,
    string DisplayName,
    string Role,
    string PasswordHash,
    bool IsActive,
    bool MustChangePassword,
    long ActorUserId,
    string ActorUsername);

public sealed record AppUserUpdateCommand(
    long UserId,
    string DisplayName,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    long ActorUserId,
    string ActorUsername);

public sealed record AppUserPasswordResetCommand(
    long UserId,
    string PasswordHash,
    bool MustChangePassword,
    long ActorUserId,
    string ActorUsername);

public sealed record AppUserOwnPasswordChangeCommand(
    long UserId,
    string ExpectedPasswordHash,
    string PasswordHash,
    string ActorUsername);

public enum AppUserManagementStatus
{
    Success,
    InvalidInput,
    NotFound,
    UsernameExists,
    InvalidCurrentPassword,
    PasswordUnchanged,
    SelfDeactivationDenied,
    LastActiveAdminDenied,
    RoleUnavailable,
    Conflict,
}

public sealed class AppUserManagementResult
{
    public AppUserManagementStatus Status { get; init; }

    public AppUserListItemDto? User { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? SecurityStamp { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool Succeeded => Status == AppUserManagementStatus.Success;

    public static AppUserManagementResult Success(
        AppUserListItemDto? user = null,
        Guid? securityStamp = null) => new()
    {
        Status = AppUserManagementStatus.Success,
        User = user,
        SecurityStamp = securityStamp,
    };

    public static AppUserManagementResult Failure(
        AppUserManagementStatus status,
        string message) => new()
        {
            Status = status,
            Message = message,
        };
}
