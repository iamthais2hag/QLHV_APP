using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace QLHV.Application.Auth;

public sealed class AppUserManagementService : IAppUserManagementService
{
    public const int MinimumPasswordLength = 12;

    private static readonly Regex SafeUsernamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{2,99}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IAppUserRepository _credentials;
    private readonly IAppUserManagementRepository _users;
    private readonly IPasswordHasher<AppUserCredential> _passwordHasher;

    public AppUserManagementService(
        IAppUserRepository credentials,
        IAppUserManagementRepository users,
        IPasswordHasher<AppUserCredential> passwordHasher)
    {
        _credentials = credentials;
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public Task<IReadOnlyList<AppUserListItemDto>> ListAsync(
        CancellationToken cancellationToken = default) =>
        _users.ListAsync(cancellationToken);

    public async Task<AppUserManagementResult> CreateAsync(
        CreateAppUserRequestDto request,
        long actorUserId,
        string actorUsername,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var role = request.Role?.Trim() ?? string.Empty;
        var validation = ValidateActor(actorUserId, actorUsername) ??
                         ValidateUsername(username) ??
                         ValidateDisplayName(displayName) ??
                         ValidateRole(role) ??
                         ValidatePassword(request.TemporaryPassword);
        if (validation is not null)
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.InvalidInput,
                validation);
        }

        role = AppRoles.Normalize(role);
        var hashingUser = new AppUserCredential
        {
            Username = username,
            DisplayName = displayName,
            IsActive = request.IsActive,
            MustChangePassword = request.MustChangePassword,
            Roles = [role],
        };
        var passwordHash = _passwordHasher.HashPassword(
            hashingUser,
            request.TemporaryPassword);

        return await _users.CreateAsync(
            new AppUserCreateCommand(
                username,
                NormalizeUsername(username),
                displayName,
                role,
                passwordHash,
                request.IsActive,
                request.MustChangePassword,
                actorUserId,
                actorUsername.Trim()),
            cancellationToken);
    }

    public async Task<AppUserManagementResult> UpdateAsync(
        long userId,
        UpdateAppUserRequestDto request,
        long actorUserId,
        string actorUsername,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var role = request.Role?.Trim() ?? string.Empty;
        var validation = userId <= 0
            ? "Tài khoản không hợp lệ."
            : ValidateActor(actorUserId, actorUsername) ??
              ValidateDisplayName(displayName) ??
              ValidateRole(role);
        if (validation is not null)
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.InvalidInput,
                validation);
        }

        return await _users.UpdateAsync(
            new AppUserUpdateCommand(
                userId,
                displayName,
                AppRoles.Normalize(role),
                request.IsActive,
                request.MustChangePassword,
                actorUserId,
                actorUsername.Trim()),
            cancellationToken);
    }

    public async Task<AppUserManagementResult> ResetPasswordAsync(
        long userId,
        ResetAppUserPasswordRequestDto request,
        long actorUserId,
        string actorUsername,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = userId <= 0
            ? "Tài khoản không hợp lệ."
            : ValidateActor(actorUserId, actorUsername) ??
              ValidatePassword(request.TemporaryPassword);
        if (validation is not null)
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.InvalidInput,
                validation);
        }

        var hashingUser = new AppUserCredential { Id = userId };
        var passwordHash = _passwordHasher.HashPassword(
            hashingUser,
            request.TemporaryPassword);
        return await _users.ResetPasswordAsync(
            new AppUserPasswordResetCommand(
                userId,
                passwordHash,
                request.MustChangePassword,
                actorUserId,
                actorUsername.Trim()),
            cancellationToken);
    }

    public async Task<AppUserManagementResult> ChangeOwnPasswordAsync(
        long userId,
        string actorUsername,
        ChangeOwnPasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = userId <= 0 || string.IsNullOrWhiteSpace(actorUsername)
            ? "Phiên đăng nhập không hợp lệ."
            : string.IsNullOrEmpty(request.CurrentPassword) ||
              request.CurrentPassword.Length > 512
                ? "Mật khẩu hiện tại không hợp lệ."
                : ValidatePassword(request.NewPassword);
        if (validation is not null)
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.InvalidInput,
                validation);
        }

        var user = await _credentials.FindByIdAsync(userId, cancellationToken);
        if (user is null ||
            !user.IsActive ||
            user.IsDeleted ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.NotFound,
                "Tài khoản không tồn tại hoặc không còn hoạt động.");
        }

        PasswordVerificationResult currentVerification;
        try
        {
            currentVerification = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.CurrentPassword);
        }
        catch (FormatException)
        {
            currentVerification = PasswordVerificationResult.Failed;
        }

        if (currentVerification == PasswordVerificationResult.Failed)
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.InvalidCurrentPassword,
                "Mật khẩu hiện tại không đúng.");
        }

        PasswordVerificationResult unchangedVerification;
        try
        {
            unchangedVerification = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.NewPassword);
        }
        catch (FormatException)
        {
            unchangedVerification = PasswordVerificationResult.Failed;
        }

        if (unchangedVerification != PasswordVerificationResult.Failed)
        {
            return AppUserManagementResult.Failure(
                AppUserManagementStatus.PasswordUnchanged,
                "Mật khẩu mới phải khác mật khẩu hiện tại.");
        }

        var newHash = _passwordHasher.HashPassword(user, request.NewPassword);
        return await _users.ChangeOwnPasswordAsync(
            new AppUserOwnPasswordChangeCommand(
                userId,
                user.PasswordHash,
                newHash,
                actorUsername.Trim()),
            cancellationToken);
    }

    public static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();

    private static string? ValidateActor(long actorUserId, string actorUsername) =>
        actorUserId <= 0 ||
        string.IsNullOrWhiteSpace(actorUsername) ||
        actorUsername.Trim().Length > 100
            ? "Phiên quản trị không hợp lệ."
            : null;

    private static string? ValidateUsername(string username) =>
        SafeUsernamePattern.IsMatch(username)
            ? null
            : "Tên đăng nhập phải dài 3-100 ký tự và chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.";

    private static string? ValidateDisplayName(string displayName) =>
        displayName.Length is >= 1 and <= 200
            ? null
            : "Họ và tên phải dài từ 1 đến 200 ký tự.";

    private static string? ValidateRole(string role) =>
        AppRoles.IsKnown(role)
            ? null
            : "Vai trò không hợp lệ.";

    private static string? ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            return $"Mật khẩu phải có ít nhất {MinimumPasswordLength} ký tự.";
        }

        return password.Length <= 512
            ? null
            : "Mật khẩu quá dài.";
    }
}
