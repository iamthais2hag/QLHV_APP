using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>
/// Executes source-partition guards and the selected snapshot groups as independent,
/// ordered target transactions. A skipped group must never reach a target mutation.
/// </summary>
public interface IQlhvImportWriteRepository
{
    Task<QlhvImportGuardedUpsertResult> UpsertWithGuardsAsync(
        IReadOnlyList<HocVienTargetWriteModel> rows,
        CancellationToken cancellationToken = default);

    Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
        string sourceProfileCode,
        QlhvImportFullSyncPayload payload,
        CancellationToken cancellationToken = default)
        => FullSyncAsync(sourceProfileCode, payload.HocVienRows, cancellationToken);

    // Compatibility surface retained while existing HocVien-only callers/tests migrate.
    Task<QlhvImportFullSyncWriteResult> FullSyncAsync(
        string sourceProfileCode,
        IReadOnlyList<QlhvImportHocVienWriteModel> rows,
        CancellationToken cancellationToken = default);
}

public sealed record QlhvImportFullSyncWriteResult(
    QlhvEntityWriteCounts KhoaHoc,
    QlhvEntityWriteCounts GiaoVien,
    QlhvEntityWriteCounts Relation,
    QlhvEntityWriteCounts HocVien,
    int InvalidSourceProfileRows,
    int InvalidTargetIdentityRows,
    int DuplicateTargetIdentityRows,
    int RelationConflicts,
    int EmptyPartitionRiskGroups,
    int NaturalKeyConflicts)
{
    public IReadOnlyList<QlhvDomainWriteResult> DomainResults { get; init; } =
        Array.Empty<QlhvDomainWriteResult>();

    // Legacy HocVien-only counters remain available to existing API/history consumers.
    public int Inserted => HocVien.Inserted;
    public int Updated => HocVien.Updated;
    public int Reactivated => HocVien.Reactivated;
    public int SoftDeleted => HocVien.SoftDeleted;
    public int Skipped => HocVien.Skipped;

    public int TotalSourceRows => KhoaHoc.SourceRows + GiaoVien.SourceRows + Relation.SourceRows + HocVien.SourceRows;
    public int TotalInserted => KhoaHoc.Inserted + GiaoVien.Inserted + Relation.Inserted + HocVien.Inserted;
    public int TotalUpdated => KhoaHoc.Updated + GiaoVien.Updated + Relation.Updated + HocVien.Updated;
    public int TotalReactivated => KhoaHoc.Reactivated + GiaoVien.Reactivated + Relation.Reactivated + HocVien.Reactivated;
    public int TotalSoftDeleted => KhoaHoc.SoftDeleted + GiaoVien.SoftDeleted + Relation.SoftDeleted + HocVien.SoftDeleted;
    public int TotalSkipped => KhoaHoc.Skipped + GiaoVien.Skipped + Relation.Skipped + HocVien.Skipped;

    public bool RequiredDomainFailed =>
        DomainResults.Any(result =>
            string.Equals(result.Domain, QlhvImportDomains.HocVien, StringComparison.Ordinal) &&
            string.Equals(result.Status, QlhvImportDomainStatuses.Failed, StringComparison.Ordinal));

    public bool HasConflicts =>
        RequiredDomainFailed ||
        (DomainResults.Count == 0 &&
         (InvalidSourceProfileRows > 0 ||
          InvalidTargetIdentityRows > 0 ||
          DuplicateTargetIdentityRows > 0 ||
          RelationConflicts > 0 ||
          EmptyPartitionRiskGroups > 0 ||
          NaturalKeyConflicts > 0));

    public QlhvImportFullSyncWriteResult(
        int inserted,
        int updated,
        int reactivated,
        int softDeleted,
        int skipped,
        int invalidSourceProfileRows,
        int invalidTargetIdentityRows,
        int duplicateTargetIdentityRows)
        : this(
            QlhvEntityWriteCounts.Empty,
            QlhvEntityWriteCounts.Empty,
            QlhvEntityWriteCounts.Empty,
            new QlhvEntityWriteCounts(
                inserted + updated + reactivated + skipped,
                inserted,
                updated,
                reactivated,
                softDeleted,
                skipped),
            invalidSourceProfileRows,
            invalidTargetIdentityRows,
            duplicateTargetIdentityRows,
            0,
            0,
            0)
    {
    }
}

public sealed record QlhvDomainWriteResult(
    string Domain,
    string Status,
    string? Message,
    QlhvEntityWriteCounts Counts);

public sealed record QlhvEntityWriteCounts(
    int SourceRows,
    int Inserted,
    int Updated,
    int Reactivated,
    int SoftDeleted,
    int Skipped)
{
    public static QlhvEntityWriteCounts Empty { get; } = new(0, 0, 0, 0, 0, 0);
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
