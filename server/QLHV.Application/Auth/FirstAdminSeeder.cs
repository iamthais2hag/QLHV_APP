using Microsoft.AspNetCore.Identity;

namespace QLHV.Application.Auth;

public sealed class FirstAdminSeeder : IFirstAdminSeeder
{
    public const int MinimumPasswordLength = 12;

    private readonly IAppUserRepository _users;
    private readonly IPasswordHasher<AppUserCredential> _passwordHasher;

    public FirstAdminSeeder(
        IAppUserRepository users,
        IPasswordHasher<AppUserCredential> passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<FirstAdminSeedResult> SeedAsync(
        FirstAdminSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var validationMessage = Validate(username, displayName, request.Password);
        if (validationMessage is not null)
        {
            return new FirstAdminSeedResult
            {
                Status = FirstAdminSeedStatus.InvalidInput,
                Message = validationMessage,
            };
        }

        var userForHashing = new AppUserCredential
        {
            Username = username,
            DisplayName = displayName,
            IsActive = true,
            Roles = new[] { AppRoles.Admin },
        };
        var passwordHash = _passwordHasher.HashPassword(userForHashing, request.Password);
        var createResult = await _users.TryCreateFirstAdminAsync(
            username,
            displayName,
            passwordHash,
            cancellationToken);

        return createResult.Status == FirstAdminCreateStatus.Created
            ? new FirstAdminSeedResult
            {
                Status = FirstAdminSeedStatus.Created,
                UserId = createResult.UserId,
                Message = "Initial Admin account created.",
            }
            : createResult.Status == FirstAdminCreateStatus.AdminAlreadyExists
                ? new FirstAdminSeedResult
                {
                    Status = FirstAdminSeedStatus.AdminAlreadyExists,
                    Message = "Seed refused because a non-deleted Admin account already exists.",
                }
                : new FirstAdminSeedResult
                {
                    Status = FirstAdminSeedStatus.UsernameAlreadyExists,
                    Message = "Seed refused because the requested username already exists.",
                };
    }

    private static string? Validate(string username, string displayName, string password)
    {
        if (username.Length is < 3 or > 100)
        {
            return "Username must contain between 3 and 100 characters.";
        }

        if (displayName.Length is < 1 or > 200)
        {
            return "Display name must contain between 1 and 200 characters.";
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            return $"Password must contain at least {MinimumPasswordLength} characters.";
        }

        if (password.Length > 512)
        {
            return "Password is too long.";
        }

        return null;
    }
}
