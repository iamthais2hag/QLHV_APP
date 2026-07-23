namespace QLHV.Application.SystemData;

public sealed class SystemDataVersionDto
{
    public long HocVienVersion { get; init; }

    public long KhoaHocVersion { get; init; }

    public long GiaoVienVersion { get; init; }

    public long PhotoVersion { get; init; }

    public DateTime? LastSuccessfulSyncUtc { get; init; }
}

public interface ISystemDataVersionRepository
{
    Task<SystemDataVersionDto> GetAsync(
        CancellationToken cancellationToken = default);
}

public interface ISystemDataVersionService
{
    Task<SystemDataVersionDto> GetAsync(
        CancellationToken cancellationToken = default);
}
