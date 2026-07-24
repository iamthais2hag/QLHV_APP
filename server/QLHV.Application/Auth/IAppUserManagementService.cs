namespace QLHV.Application.Auth;

public interface IAppUserManagementService
{
    Task<IReadOnlyList<AppUserListItemDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> CreateAsync(
        CreateAppUserRequestDto request,
        long actorUserId,
        string actorUsername,
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> UpdateAsync(
        long userId,
        UpdateAppUserRequestDto request,
        long actorUserId,
        string actorUsername,
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> ResetPasswordAsync(
        long userId,
        ResetAppUserPasswordRequestDto request,
        long actorUserId,
        string actorUsername,
        CancellationToken cancellationToken = default);

    Task<AppUserManagementResult> ChangeOwnPasswordAsync(
        long userId,
        string actorUsername,
        ChangeOwnPasswordRequestDto request,
        CancellationToken cancellationToken = default);
}
