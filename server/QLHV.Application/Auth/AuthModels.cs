namespace QLHV.Application.Auth;

public sealed class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class AuthSessionDto
{
    public bool IsAuthenticated { get; init; } = true;

    public long Id { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public bool MustChangePassword { get; init; }
}

public sealed class AuthLoginResult
{
    public bool Succeeded { get; init; }

    public AuthSessionDto? Session { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Guid SecurityStamp { get; init; }

    public string FailureCode { get; init; } = string.Empty;

    public static AuthLoginResult InvalidCredentials() => new()
    {
        FailureCode = "INVALID_CREDENTIALS",
    };

    public static AuthLoginResult LockedOut() => new()
    {
        FailureCode = "ACCOUNT_LOCKED",
    };
}

public sealed class AppUserCredential
{
    public long Id { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string PasswordHash { get; init; } = string.Empty;

    public Guid SecurityStamp { get; init; }

    public bool IsActive { get; init; }

    public bool IsDeleted { get; init; }

    public int FailedLoginCount { get; init; }

    public DateTime? LastFailedLoginAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public bool MustChangePassword { get; init; }
}

public sealed class ChangeOwnPasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;

    public string NewPassword { get; set; } = string.Empty;
}

public sealed class FirstAdminSeedRequest
{
    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

public enum FirstAdminSeedStatus
{
    Created,
    AdminAlreadyExists,
    UsernameAlreadyExists,
    InvalidInput,
}

public sealed class FirstAdminSeedResult
{
    public FirstAdminSeedStatus Status { get; init; }

    public long? UserId { get; init; }

    public string Message { get; init; } = string.Empty;
}

public enum FirstAdminCreateStatus
{
    Created,
    AdminAlreadyExists,
    UsernameAlreadyExists,
}

public sealed record FirstAdminCreateResult(FirstAdminCreateStatus Status, long? UserId);
