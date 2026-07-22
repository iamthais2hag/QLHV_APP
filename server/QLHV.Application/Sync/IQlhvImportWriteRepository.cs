using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Executes the import-specific conflict guards and App_HocVien upsert in one target transaction.
/// </summary>
public interface IQlhvImportWriteRepository
{
    Task<QlhvImportGuardedUpsertResult> UpsertWithGuardsAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default);

    Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
        string sourceProfileCode,
        IReadOnlyList<QlhvImportHocVienWriteModel> rows,
        CancellationToken cancellationToken = default);
}

public sealed record QlhvImportFullSyncWriteResult(
    int Inserted,
    int Updated,
    int Reactivated,
    int SoftDeleted,
    int Skipped,
    int InvalidSourceProfileRows,
    int InvalidTargetIdentityRows,
    int DuplicateTargetIdentityRows)
{
    public bool HasConflicts =>
        InvalidSourceProfileRows > 0 ||
        InvalidTargetIdentityRows > 0 ||
        DuplicateTargetIdentityRows > 0;
}

public sealed record QlhvImportGuardedUpsertResult(
    UpsertCounts Counts,
    int TargetMaDkConflictsOtherProfiles,
    int SoftDeletedIdentityConflicts)
{
    public bool HasConflicts =>
        TargetMaDkConflictsOtherProfiles > 0 || SoftDeletedIdentityConflicts > 0;

    public static QlhvImportGuardedUpsertResult Empty { get; } =
        new(UpsertCounts.Empty, 0, 0);
}
