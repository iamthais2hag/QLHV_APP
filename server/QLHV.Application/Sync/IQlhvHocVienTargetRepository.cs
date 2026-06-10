using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

/// <summary>Read and guarded write access to QLHV_APP.dbo.App_HocVien.</summary>
public interface IQlhvHocVienTargetRepository
{
    /// <summary>Counts current non-deleted target rows. Read-only.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns target keys already present for the supplied MaDK values. Read-only.</summary>
    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        IReadOnlyCollection<string> maDks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts source rows into App_HocVien using transaction, temp staging, SqlBulkCopy, and MERGE.
    /// The caller is responsible for checking execution guards before calling with dryRun=false.
    /// </summary>
    Task<HocVienUpsertResultDto> UpsertBatchAsync(
        IReadOnlyList<V2HocVienSourceRow> rows,
        bool dryRun,
        CancellationToken cancellationToken = default);
}
