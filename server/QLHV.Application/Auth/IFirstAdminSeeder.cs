namespace QLHV.Application.Auth;

public interface IFirstAdminSeeder
{
    Task<FirstAdminSeedResult> SeedAsync(
        FirstAdminSeedRequest request,
        CancellationToken cancellationToken = default);
}
