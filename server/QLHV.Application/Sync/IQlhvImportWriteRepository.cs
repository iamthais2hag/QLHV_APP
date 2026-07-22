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
