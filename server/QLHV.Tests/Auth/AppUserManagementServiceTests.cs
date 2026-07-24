using Microsoft.AspNetCore.Identity;
using QLHV.Application.Auth;

namespace QLHV.Tests.Auth;

public sealed class AppUserManagementServiceTests
{
    private const string CurrentPassword = "Current-password-123!";
    private const string TemporaryPassword = "Temporary-password-456!";

    [Fact]
    public void Employee_is_a_known_role_between_Admin_and_Viewer()
    {
        Assert.True(AppRoles.IsKnown("employee"));
        Assert.Equal(AppRoles.Employee, AppRoles.Normalize("EMPLOYEE"));
        Assert.Equal(
            AppRoles.Employee,
            AppRoles.SelectPrimary([AppRoles.Viewer, AppRoles.Employee]));
        Assert.Equal(
            AppRoles.Admin,
            AppRoles.SelectPrimary([AppRoles.Employee, AppRoles.Admin]));
    }

    [Fact]
    public async Task Admin_creates_Employee_with_normalized_username_and_standard_hash()
    {
        var passwordHasher = new PasswordHasher<AppUserCredential>();
        var management = new FakeManagementRepository
        {
            CreateResult = AppUserManagementResult.Success(new AppUserListItemDto
            {
                Id = 8,
                Username = "employee.one",
                DisplayName = "Nhân viên Một",
                Role = AppRoles.Employee,
                IsActive = true,
                MustChangePassword = true,
                CreatedAtUtc = DateTime.UtcNow,
            }),
        };
        var service = new AppUserManagementService(
            new FakeCredentialRepository(),
            management,
            passwordHasher);

        var result = await service.CreateAsync(
            new CreateAppUserRequestDto
            {
                Username = "  employee.one  ",
                DisplayName = "  Nhân viên Một  ",
                Role = "employee",
                TemporaryPassword = TemporaryPassword,
                IsActive = true,
                MustChangePassword = true,
            },
            actorUserId: 1,
            actorUsername: "admin");

        Assert.True(result.Succeeded);
        var command = Assert.IsType<AppUserCreateCommand>(management.Created);
        Assert.Equal("employee.one", command.Username);
        Assert.Equal("EMPLOYEE.ONE", command.NormalizedUsername);
        Assert.Equal(AppRoles.Employee, command.Role);
        Assert.NotEqual(TemporaryPassword, command.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(
                new AppUserCredential
                {
                    Username = command.Username,
                    DisplayName = command.DisplayName,
                    Roles = [command.Role],
                },
                command.PasswordHash,
                TemporaryPassword));
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("../unsafe")]
    [InlineData("ab")]
    [InlineData("name@domain")]
    public async Task Unsafe_username_is_rejected_before_repository(
        string username)
    {
        var management = new FakeManagementRepository();
        var service = CreateService(management);

        var result = await service.CreateAsync(
            new CreateAppUserRequestDto
            {
                Username = username,
                DisplayName = "Nhân viên",
                Role = AppRoles.Employee,
                TemporaryPassword = TemporaryPassword,
            },
            1,
            "admin");

        Assert.Equal(AppUserManagementStatus.InvalidInput, result.Status);
        Assert.Null(management.Created);
    }

    [Fact]
    public async Task Own_password_change_verifies_current_password_and_clears_force_flag()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var user = new AppUserCredential
        {
            Id = 12,
            Username = "employee",
            DisplayName = "Nhân viên",
            IsActive = true,
            MustChangePassword = true,
            Roles = [AppRoles.Employee],
        };
        user = CopyWithPasswordHash(
            user,
            hasher.HashPassword(user, CurrentPassword));
        var credentials = new FakeCredentialRepository { User = user };
        var management = new FakeManagementRepository
        {
            ChangeResult = AppUserManagementResult.Success(),
        };
        var service = new AppUserManagementService(
            credentials,
            management,
            hasher);

        var result = await service.ChangeOwnPasswordAsync(
            user.Id,
            user.Username,
            new ChangeOwnPasswordRequestDto
            {
                CurrentPassword = CurrentPassword,
                NewPassword = TemporaryPassword,
            });

        Assert.True(result.Succeeded);
        var command = Assert.IsType<AppUserOwnPasswordChangeCommand>(management.Changed);
        Assert.Equal(user.PasswordHash, command.ExpectedPasswordHash);
        Assert.NotEqual(TemporaryPassword, command.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, command.PasswordHash, TemporaryPassword));
    }

    [Fact]
    public async Task Wrong_current_password_never_updates_password()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var user = new AppUserCredential
        {
            Id = 13,
            Username = "employee",
            DisplayName = "Nhân viên",
            IsActive = true,
            Roles = [AppRoles.Employee],
        };
        user = CopyWithPasswordHash(
            user,
            hasher.HashPassword(user, CurrentPassword));
        var management = new FakeManagementRepository();
        var service = new AppUserManagementService(
            new FakeCredentialRepository { User = user },
            management,
            hasher);

        var result = await service.ChangeOwnPasswordAsync(
            user.Id,
            user.Username,
            new ChangeOwnPasswordRequestDto
            {
                CurrentPassword = "Wrong-password-999!",
                NewPassword = TemporaryPassword,
            });

        Assert.Equal(AppUserManagementStatus.InvalidCurrentPassword, result.Status);
        Assert.Null(management.Changed);
    }

    [Fact]
    public async Task Admin_password_reset_passes_only_hash_to_repository()
    {
        var hasher = new PasswordHasher<AppUserCredential>();
        var management = new FakeManagementRepository
        {
            ResetResult = AppUserManagementResult.Success(),
        };
        var service = new AppUserManagementService(
            new FakeCredentialRepository(),
            management,
            hasher);

        var result = await service.ResetPasswordAsync(
            20,
            new ResetAppUserPasswordRequestDto
            {
                TemporaryPassword = TemporaryPassword,
                MustChangePassword = true,
            },
            1,
            "admin");

        Assert.True(result.Succeeded);
        var command = Assert.IsType<AppUserPasswordResetCommand>(management.Reset);
        Assert.NotEqual(TemporaryPassword, command.PasswordHash);
        Assert.True(command.MustChangePassword);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(
                new AppUserCredential { Id = 20 },
                command.PasswordHash,
                TemporaryPassword));
    }

    private static AppUserManagementService CreateService(
        FakeManagementRepository management) =>
        new(
            new FakeCredentialRepository(),
            management,
            new PasswordHasher<AppUserCredential>());

    private static AppUserCredential CopyWithPasswordHash(
        AppUserCredential user,
        string hash) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            PasswordHash = hash,
            SecurityStamp = user.SecurityStamp == Guid.Empty
                ? Guid.NewGuid()
                : user.SecurityStamp,
            IsActive = user.IsActive,
            IsDeleted = user.IsDeleted,
            FailedLoginCount = user.FailedLoginCount,
            LastFailedLoginAtUtc = user.LastFailedLoginAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            Roles = user.Roles,
            MustChangePassword = user.MustChangePassword,
        };

    private sealed class FakeCredentialRepository : IAppUserRepository
    {
        public AppUserCredential? User { get; init; }

        public Task<AppUserCredential?> FindByUsernameAsync(
            string username,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(User);

        public Task<AppUserCredential?> FindByIdAsync(
            long userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(User?.Id == userId ? User : null);

        public Task<bool> TryRecordSuccessfulLoginAsync(
            long userId,
            string expectedPasswordHash,
            Guid expectedSecurityStamp,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task RecordFailedLoginAsync(
            long userId,
            DateTime failedAtUtc,
            DateTime resetCutoffUtc,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryUpdatePasswordHashAsync(
            long userId,
            string expectedPasswordHash,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<FirstAdminCreateResult> TryCreateFirstAdminAsync(
            string username,
            string displayName,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FirstAdminCreateResult(
                FirstAdminCreateStatus.AdminAlreadyExists,
                null));
    }

    private sealed class FakeManagementRepository : IAppUserManagementRepository
    {
        public AppUserCreateCommand? Created { get; private set; }

        public AppUserPasswordResetCommand? Reset { get; private set; }

        public AppUserOwnPasswordChangeCommand? Changed { get; private set; }

        public AppUserManagementResult CreateResult { get; init; } =
            AppUserManagementResult.Failure(
                AppUserManagementStatus.Conflict,
                "not configured");

        public AppUserManagementResult ResetResult { get; init; } =
            AppUserManagementResult.Failure(
                AppUserManagementStatus.Conflict,
                "not configured");

        public AppUserManagementResult ChangeResult { get; init; } =
            AppUserManagementResult.Failure(
                AppUserManagementStatus.Conflict,
                "not configured");

        public Task<IReadOnlyList<AppUserListItemDto>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppUserListItemDto>>([]);

        public Task<AppUserManagementResult> CreateAsync(
            AppUserCreateCommand command,
            CancellationToken cancellationToken = default)
        {
            Created = command;
            return Task.FromResult(CreateResult);
        }

        public Task<AppUserManagementResult> UpdateAsync(
            AppUserUpdateCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppUserManagementResult.Success());

        public Task<AppUserManagementResult> ResetPasswordAsync(
            AppUserPasswordResetCommand command,
            CancellationToken cancellationToken = default)
        {
            Reset = command;
            return Task.FromResult(ResetResult);
        }

        public Task<AppUserManagementResult> ChangeOwnPasswordAsync(
            AppUserOwnPasswordChangeCommand command,
            CancellationToken cancellationToken = default)
        {
            Changed = command;
            return Task.FromResult(ChangeResult);
        }
    }
}
