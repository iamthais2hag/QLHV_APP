using QLHV.Application.CsdtConnections.Dtos;

namespace QLHV.Application.CsdtConnections;

public interface ICsdtConnectionProfileRepository
{
    Task<IReadOnlyList<CsdtConnectionProfileRecord>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<CsdtConnectionProfileRecord?> GetByCodeAsync(
        string profileCode,
        CancellationToken cancellationToken = default);

    Task<CsdtConnectionProfileRecord?> SaveAsync(
        string profileCode,
        SaveCsdtConnectionProfileRequest request,
        byte[]? passwordCipherText,
        bool updatePassword,
        CancellationToken cancellationToken = default);

    Task UpdateTestResultAsync(
        string profileCode,
        DateTime testedAt,
        string status,
        string safeMessage,
        CancellationToken cancellationToken = default);

    Task AddAuditAsync(
        string profileCode,
        string action,
        string resultStatus,
        string safeMessage,
        CancellationToken cancellationToken = default);
}
