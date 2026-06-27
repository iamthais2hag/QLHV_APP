using QLHV.Application.CsdtConnections.Dtos;

namespace QLHV.Application.CsdtConnections;

public interface ICsdtConnectionProfileService
{
    Task<IReadOnlyList<CsdtConnectionProfileListItemDto>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<CsdtConnectionProfileDetailDto?> GetProfileAsync(
        string profileCode,
        CancellationToken cancellationToken = default);

    Task<CsdtConnectionProfileDetailDto> SaveProfileAsync(
        string profileCode,
        SaveCsdtConnectionProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<TestCsdtConnectionProfileResultDto> TestProfileAsync(
        string profileCode,
        TestCsdtConnectionProfileRequest? request,
        CancellationToken cancellationToken = default);
}
