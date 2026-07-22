using Microsoft.AspNetCore.Identity;
using QLHV.Application.Auth;

namespace QLHV.Tests.Auth;

public sealed class AuthServiceTests
{
    private const string ValidPassword = "correct-horse-battery-staple";

    [Fact]
    public async Task Login_with_correct_password_returns_session_and_records_success()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository
        {
            User = CreateUser(hasher, ValidPassword, roles: [AppRoles.Admin]),
        };
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "  admin  ",
            Password = ValidPassword,
        });

        Assert.True(result.Succeeded);
        var session = Assert.IsType<AuthSessionDto>(result.Session);
        Assert.Equal("admin", session.Username);
        Assert.Equal("Administrator", session.DisplayName);
        Assert.Equal(AppRoles.Admin, session.Role);
        Assert.Contains(AppRoles.Admin, session.Roles);
        Assert.Equal("admin", repository.LastLookupUsername);
        Assert.Equal(1, repository.SuccessfulLoginCalls);
        Assert.Equal(0, repository.FailedLoginCalls);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_generic_failure_and_records_failure()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository
        {
            User = CreateUser(hasher, ValidPassword, roles: [AppRoles.Viewer]),
        };
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = "not-the-password",
        });

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Equal(0, repository.SuccessfulLoginCalls);
        Assert.Equal(1, repository.FailedLoginCalls);
        Assert.NotNull(repository.LastFailedAtUtc);
        Assert.NotNull(repository.LastResetCutoffUtc);
        Assert.InRange(
            repository.LastFailedAtUtc.Value - repository.LastResetCutoffUtc.Value,
            AuthService.LockoutDuration.Subtract(TimeSpan.FromSeconds(1)),
            AuthService.LockoutDuration.Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Temporarily_locked_user_cannot_login_with_correct_password()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository
        {
            User = CreateUser(
                hasher,
                ValidPassword,
                roles: [AppRoles.Admin],
                failedLoginCount: AuthService.MaxFailedLoginAttempts,
                updatedAtUtc: DateTime.UtcNow),
        };
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = ValidPassword,
        });

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Equal(0, repository.SuccessfulLoginCalls);
        Assert.Equal(0, repository.FailedLoginCalls);
    }

    [Fact]
    public async Task Expired_lockout_allows_correct_password_and_resets_failure_state()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository
        {
            User = CreateUser(
                hasher,
                ValidPassword,
                roles: [AppRoles.Admin],
                failedLoginCount: AuthService.MaxFailedLoginAttempts,
                updatedAtUtc: DateTime.UtcNow.Subtract(AuthService.LockoutDuration).Subtract(TimeSpan.FromMinutes(1))),
        };
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = ValidPassword,
        });

        Assert.True(result.Succeeded);
        Assert.Equal(1, repository.SuccessfulLoginCalls);
    }

    [Fact]
    public async Task Missing_user_still_runs_standard_password_verification()
    {
        var hasher = new CountingPasswordHasher();
        var service = new AuthService(new FakeAppUserRepository(), hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "does-not-exist",
            Password = ValidPassword,
        });

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Equal(1, hasher.VerifyCalls);
    }

    [Theory]
    [InlineData(101, 20)]
    [InlineData(10, 513)]
    public async Task Oversized_login_input_is_rejected_before_database_or_hashing(
        int usernameLength,
        int passwordLength)
    {
        var repository = new FakeAppUserRepository();
        var hasher = new CountingPasswordHasher();
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = new string('u', usernameLength),
            Password = new string('p', passwordLength),
        });

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Null(repository.LastLookupUsername);
        Assert.Equal(0, hasher.VerifyCalls);
    }

    [Fact]
    public async Task Inactive_user_cannot_login_even_with_correct_password()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository
        {
            User = CreateUser(
                hasher,
                ValidPassword,
                isActive: false,
                roles: [AppRoles.Admin]),
        };
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = ValidPassword,
        });

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Equal(0, repository.SuccessfulLoginCalls);
        Assert.Equal(0, repository.FailedLoginCalls);
    }

    [Fact]
    public async Task First_admin_seed_passes_only_a_standard_hash_to_repository()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository();
        var seeder = new FirstAdminSeeder(repository, hasher);

        var result = await seeder.SeedAsync(new FirstAdminSeedRequest
        {
            Username = "first-admin",
            DisplayName = "First Administrator",
            Password = ValidPassword,
        });

        Assert.Equal(FirstAdminSeedStatus.Created, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(repository.CreatedPasswordHash));
        Assert.NotEqual(ValidPassword, repository.CreatedPasswordHash);
        Assert.DoesNotContain(ValidPassword, repository.CreatedPasswordHash!, StringComparison.Ordinal);

        var user = new AppUserCredential { Username = "first-admin" };
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(user, repository.CreatedPasswordHash!, ValidPassword));
    }

    private static AppUserCredential CreateUser(
        IPasswordHasher<AppUserCredential> hasher,
        string password,
        bool isActive = true,
        IReadOnlyList<string>? roles = null,
        int failedLoginCount = 0,
        DateTime? updatedAtUtc = null)
    {
        var user = new AppUserCredential
        {
            Id = 42,
            Username = "admin",
            DisplayName = "Administrator",
            IsActive = isActive,
            Roles = roles ?? [AppRoles.Viewer],
        };
        return new AppUserCredential
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            PasswordHash = hasher.HashPassword(user, password),
            IsActive = user.IsActive,
            IsDeleted = user.IsDeleted,
            FailedLoginCount = failedLoginCount,
            UpdatedAtUtc = updatedAtUtc,
            Roles = user.Roles,
        };
    }

    private sealed class CountingPasswordHasher : IPasswordHasher<AppUserCredential>
    {
        private readonly PasswordHasher<AppUserCredential> _inner = new();

        public int VerifyCalls { get; private set; }

        public string HashPassword(AppUserCredential user, string password) =>
            _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(
            AppUserCredential user,
            string hashedPassword,
            string providedPassword)
        {
            VerifyCalls++;
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private sealed class FakeAppUserRepository : IAppUserRepository
    {
        public AppUserCredential? User { get; init; }

        public string? LastLookupUsername { get; private set; }

        public int SuccessfulLoginCalls { get; private set; }

        public int FailedLoginCalls { get; private set; }

        public DateTime? LastFailedAtUtc { get; private set; }

        public DateTime? LastResetCutoffUtc { get; private set; }

        public string? CreatedPasswordHash { get; private set; }

        public Task<AppUserCredential?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken = default)
        {
            LastLookupUsername = username;
            return Task.FromResult(User);
        }

        public Task<AppUserCredential?> FindByIdAsync(
            long userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(User?.Id == userId ? User : null);

        public Task RecordSuccessfulLoginAsync(
            long userId,
            CancellationToken cancellationToken = default)
        {
            SuccessfulLoginCalls++;
            return Task.CompletedTask;
        }

        public Task RecordFailedLoginAsync(
            long userId,
            DateTime failedAtUtc,
            DateTime resetCutoffUtc,
            CancellationToken cancellationToken = default)
        {
            FailedLoginCalls++;
            LastFailedAtUtc = failedAtUtc;
            LastResetCutoffUtc = resetCutoffUtc;
            return Task.CompletedTask;
        }

        public Task UpdatePasswordHashAsync(
            long userId,
            string passwordHash,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<FirstAdminCreateResult> TryCreateFirstAdminAsync(
            string username,
            string displayName,
            string passwordHash,
            CancellationToken cancellationToken = default)
        {
            CreatedPasswordHash = passwordHash;
            return Task.FromResult(new FirstAdminCreateResult(FirstAdminCreateStatus.Created, 1));
        }
    }
}
