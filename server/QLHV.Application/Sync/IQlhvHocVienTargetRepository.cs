using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Read and guarded write access to QLHV_APP.dbo.App_HocVien.
/// Write operations are used only by the manually confirmed execute path.
/// </summary>
public interface IQlhvHocVienTargetRepository
{
    /// <summary>Counts current non-deleted target rows. Read-only.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns target keys already present for the supplied MaDK values. Read-only.</summary>
    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts normalized HocVien rows into App_HocVien using transaction, temp staging, SqlBulkCopy, and MERGE.
    /// Implementations must reject when target writes are disabled.
    /// </summary>
    Task<UpsertCounts> UpsertBatchAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default);
}
