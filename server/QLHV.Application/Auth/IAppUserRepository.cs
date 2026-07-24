namespace QLHV.Application.Auth;

public interface IAppUserRepository
{
    Task<AppUserCredential?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default);

    Task<AppUserCredential?> FindByIdAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<bool> TryRecordSuccessfulLoginAsync(
        long userId,
        string expectedPasswordHash,
        Guid expectedSecurityStamp,
        CancellationToken cancellationToken = default);

    Task RecordFailedLoginAsync(
        long userId,
        DateTime failedAtUtc,
        DateTime resetCutoffUtc,
        CancellationToken cancellationToken = default);

    Task<bool> TryUpdatePasswordHashAsync(
        long userId,
        string expectedPasswordHash,
        string passwordHash,
        CancellationToken cancellationToken = default);

    Task<FirstAdminCreateResult> TryCreateFirstAdminAsync(
        string username,
        string displayName,
        string passwordHash,
        CancellationToken cancellationToken = default);
}
