namespace QLHV.Application.SystemData;

public sealed class SystemDataVersionService : ISystemDataVersionService
{
    private readonly ISystemDataVersionRepository _repository;

    public SystemDataVersionService(ISystemDataVersionRepository repository)
    {
        _repository = repository;
    }

    public Task<SystemDataVersionDto> GetAsync(
        CancellationToken cancellationToken = default)
        => _repository.GetAsync(cancellationToken);
}
