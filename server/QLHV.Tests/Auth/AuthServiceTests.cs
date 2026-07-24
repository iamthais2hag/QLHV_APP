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
        Assert.Equal(repository.User!.SecurityStamp, result.SecurityStamp);
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
        Assert.Equal("ACCOUNT_LOCKED", result.FailureCode);
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

    [Fact]
    public async Task Rehash_compare_and_swap_failure_rejects_racing_login()
    {
        var oldHash = new PasswordHasher<AppUserCredential>().HashPassword(
            new AppUserCredential(),
            "legacy-password");
        var repository = new FakeAppUserRepository
        {
            User = new AppUserCredential
            {
                Id = 42,
                Username = "admin",
                DisplayName = "Administrator",
                PasswordHash = oldHash,
                SecurityStamp = Guid.NewGuid(),
                IsActive = true,
                Roles = [AppRoles.Admin],
            },
            RehashUpdateResult = false,
        };
        var service = new AuthService(repository, new RehashNeededPasswordHasher());

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = ValidPassword,
        });

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Equal(1, repository.RehashUpdateCalls);
        Assert.Equal(oldHash, repository.LastExpectedPasswordHash);
        Assert.Equal(0, repository.SuccessfulLoginCalls);
    }

    [Fact]
    public async Task Successful_login_compare_and_swap_failure_rejects_racing_account_change()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var repository = new FakeAppUserRepository
        {
            User = CreateUser(hasher, ValidPassword, roles: [AppRoles.Admin]),
            LoginRecordResult = false,
        };
        var service = new AuthService(repository, hasher);

        var result = await service.AuthenticateAsync(new LoginRequestDto
        {
            Username = "admin",
            Password = ValidPassword,
        });

        Assert.False(result.Succeeded);
        Assert.Equal("INVALID_CREDENTIALS", result.FailureCode);
        Assert.Equal(1, repository.SuccessfulLoginCalls);
        Assert.Equal(repository.User!.PasswordHash, repository.LastLoginExpectedPasswordHash);
        Assert.Equal(repository.User.SecurityStamp, repository.LastLoginExpectedSecurityStamp);
    }

    [Fact]
    public void Password_hash_format_accepts_identity_hash_and_rejects_malformed_credentials()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var user = new AppUserCredential { Username = "admin" };
        var validHash = hasher.HashPassword(user, ValidPassword);
        var identityV2Payload = new byte[49];
        identityV2Payload[0] = 0x00;
        var unsupportedPrfPayload = Convert.FromBase64String(validHash);
        unsupportedPrfPayload[1] = 0x00;
        unsupportedPrfPayload[2] = 0x00;
        unsupportedPrfPayload[3] = 0x00;
        unsupportedPrfPayload[4] = 0x03;

        Assert.True(AppPasswordHashFormat.IsSupported(validHash));
        Assert.True(AppPasswordHashFormat.IsSupported(
            Convert.ToBase64String(identityV2Payload)));
        Assert.False(AppPasswordHashFormat.IsSupported(null));
        Assert.False(AppPasswordHashFormat.IsSupported("not-base64"));
        Assert.False(AppPasswordHashFormat.IsSupported(Convert.ToBase64String([0x7f, 0x00])));
        Assert.False(AppPasswordHashFormat.IsSupported(
            Convert.ToBase64String([0x01, 0x00, 0x00, 0x00])));
        Assert.False(AppPasswordHashFormat.IsSupported(
            Convert.ToBase64String(unsupportedPrfPayload)));
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
            SecurityStamp = Guid.NewGuid(),
            Roles = roles ?? [AppRoles.Viewer],
        };
        return new AppUserCredential
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            PasswordHash = hasher.HashPassword(user, password),
            SecurityStamp = user.SecurityStamp,
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

    private sealed class RehashNeededPasswordHasher : IPasswordHasher<AppUserCredential>
    {
        public string HashPassword(AppUserCredential user, string password) => "refreshed-hash";

        public PasswordVerificationResult VerifyHashedPassword(
            AppUserCredential user,
            string hashedPassword,
            string providedPassword) =>
            PasswordVerificationResult.SuccessRehashNeeded;
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

        public bool RehashUpdateResult { get; init; } = true;

        public int RehashUpdateCalls { get; private set; }

        public string? LastExpectedPasswordHash { get; private set; }

        public bool LoginRecordResult { get; init; } = true;

        public string? LastLoginExpectedPasswordHash { get; private set; }

        public Guid? LastLoginExpectedSecurityStamp { get; private set; }

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

        public Task<bool> TryRecordSuccessfulLoginAsync(
            long userId,
            string expectedPasswordHash,
            Guid expectedSecurityStamp,
            CancellationToken cancellationToken = default)
        {
            SuccessfulLoginCalls++;
            LastLoginExpectedPasswordHash = expectedPasswordHash;
            LastLoginExpectedSecurityStamp = expectedSecurityStamp;
            return Task.FromResult(LoginRecordResult);
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

        public Task<bool> TryUpdatePasswordHashAsync(
            long userId,
            string expectedPasswordHash,
            string passwordHash,
            CancellationToken cancellationToken = default)
        {
            RehashUpdateCalls++;
            LastExpectedPasswordHash = expectedPasswordHash;
            return Task.FromResult(RehashUpdateResult);
        }

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
