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
        if (user is null ||
            !user.IsActive ||
            user.IsDeleted ||
            string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !AppPasswordHashFormat.IsSupported(user.PasswordHash) ||
            user.SecurityStamp == Guid.Empty)
        {
            VerifyDummyPassword(request.Password);
            return AuthLoginResult.InvalidCredentials();
        }

        var now = DateTime.UtcNow;
        if (user.FailedLoginCount >= MaxFailedLoginAttempts &&
            (user.LastFailedLoginAtUtc ?? user.UpdatedAtUtc) is { } lastFailureAtUtc &&
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
            .OrderBy(AppRoles.Priority)
            .ToArray();
        var primaryRole = AppRoles.SelectPrimary(roles);
        if (primaryRole is null)
        {
            return AuthLoginResult.InvalidCredentials();
        }

        var expectedPasswordHash = user.PasswordHash;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var refreshedHash = _passwordHasher.HashPassword(user, request.Password);
            var updated = await _users.TryUpdatePasswordHashAsync(
                user.Id,
                user.PasswordHash,
                refreshedHash,
                cancellationToken);
            if (!updated)
            {
                return AuthLoginResult.InvalidCredentials();
            }

            expectedPasswordHash = refreshedHash;
        }

        var loginRecorded = await _users.TryRecordSuccessfulLoginAsync(
            user.Id,
            expectedPasswordHash,
            user.SecurityStamp,
            cancellationToken);
        if (!loginRecorded)
        {
            return AuthLoginResult.InvalidCredentials();
        }

        return new AuthLoginResult
        {
            Succeeded = true,
            SecurityStamp = user.SecurityStamp,
            Session = new AuthSessionDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = primaryRole,
                Roles = roles,
                MustChangePassword = user.MustChangePassword,
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
