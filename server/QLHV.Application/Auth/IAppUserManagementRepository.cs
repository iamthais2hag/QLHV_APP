namespace QLHV.Application.Auth;

public interface IAppUserManagementRepository
{
    Task<IReadOnlyList<AppUserListItemDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> CreateAsync(
        AppUserCreateCommand command,
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> UpdateAsync(
        AppUserUpdateCommand command,
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> ResetPasswordAsync(
        AppUserPasswordResetCommand command,
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> ChangeOwnPasswordAsync(
        AppUserOwnPasswordChangeCommand command,
        CancellationToken cancellationToken = default);
}
