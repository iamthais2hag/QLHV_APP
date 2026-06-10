using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>Read and guarded write access to QLHV_APP.dbo.App_HocVien.</summary>
public interface IQlhvHocVienTargetRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts one mapped batch using staging + MERGE in a SQL transaction.
    /// Implementations must reject when target writes are disabled.
    /// </summary>
    Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default);
}
