namespace QLHV.Application.Auth;

public interface IAuthService
{
    Task<AuthLoginResult> AuthenticateAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default);
}
