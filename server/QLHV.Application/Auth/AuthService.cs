using Microsoft.AspNetCore.Identity;

namespace QLHV.Application.Auth;

public sealed class AuthService : IAuthService
{
    public const int MaxFailedLoginAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly AppUserCredential DummyUser = new()
    {
        Username = "__qlhv_timing_only__",
        DisplayName = "Timing only",
        IsActive = true,
        Roles = [AppRoles.Viewer],
    };

    private static readonly string DummyPasswordHash =
        new PasswordHasher<AppUserCredential>().HashPassword(
            DummyUser,
            "not-a-real-account-password");

    private readonly IAppUserRepository _users;
    private readonly IPasswordHasher<AppUserCredential> _passwordHasher;

    public AuthService(
        IAppUserRepository users,
        IPasswordHasher<AppUserCredential> passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<AuthLoginResult> AuthenticateAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(username) ||
            username.Length > 100 ||
            string.IsNullOrEmpty(request.Password) ||
            request.Password.Length > 512)
        {
            return AuthLoginResult.InvalidCredentials();
        }

        var user = await _users.FindByUsernameAsync(username, cancellationToken);
        if (user is null || !user.IsActive || user.IsDeleted || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            VerifyDummyPassword(request.Password);
            return AuthLoginResult.InvalidCredentials();
        }

        var now = DateTime.UtcNow;
        if (user.FailedLoginCount >= MaxFailedLoginAttempts &&
            user.UpdatedAtUtc is { } lastFailureAtUtc &&
            lastFailureAtUtc > now.Subtract(LockoutDuration))
        {
            VerifyDummyPassword(request.Password);
            return AuthLoginResult.LockedOut();
        }

        PasswordVerificationResult verification;
        try
        {
            verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        }
        catch (FormatException)
        {
            VerifyDummyPassword(request.Password);
            verification = PasswordVerificationResult.Failed;
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            await _users.RecordFailedLoginAsync(
                user.Id,
                now,
                now.Subtract(LockoutDuration),
                cancellationToken);
            return AuthLoginResult.InvalidCredentials();
        }

        var roles = user.Roles
            .Where(AppRoles.IsKnown)
            .Select(AppRoles.Normalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => string.Equals(role, AppRoles.Admin, StringComparison.Ordinal) ? 0 : 1)
            .ToArray();
        var primaryRole = AppRoles.SelectPrimary(roles);
        if (primaryRole is null)
        {
            return AuthLoginResult.InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var refreshedHash = _passwordHasher.HashPassword(user, request.Password);
            await _users.UpdatePasswordHashAsync(user.Id, refreshedHash, cancellationToken);
        }

        await _users.RecordSuccessfulLoginAsync(user.Id, cancellationToken);

        return new AuthLoginResult
        {
            Succeeded = true,
            Session = new AuthSessionDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = primaryRole,
                Roles = roles,
            },
        };
    }

    private void VerifyDummyPassword(string suppliedPassword)
    {
        try
        {
            _ = _passwordHasher.VerifyHashedPassword(
                DummyUser,
                DummyPasswordHash,
                suppliedPassword);
        }
        catch (FormatException)
        {
            // A custom hasher may reject the standard ASP.NET Core dummy hash.
            // Authentication still fails closed; no credential data is logged.
        }
    }
}
