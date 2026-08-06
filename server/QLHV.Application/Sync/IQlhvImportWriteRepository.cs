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

    /// <summary>
    /// RT03 V5 recovery-only domain entry point. The caller owns the global,
    /// profile and source read-barrier leases and must invoke domains in the
    /// sealed order COURSE, TEACHER, VEHICLE, LEARNER, RELATION.
    /// </summary>
    Task<QlhvDomainWriteResult> FullSyncRecoveryDomainAsync(
        string sourceProfileCode,
        QlhvImportFullSyncPayload payload,
        string domain,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "This repository does not implement RT03 V5 domain recovery.");
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
    QlhvEntityWriteCounts Counts)
{
    public string? RequestReasonCode { get; init; }

    public bool Requested =>
        !string.Equals(Status, QlhvImportDomainStatuses.SkippedNotRequested, StringComparison.Ordinal) &&
        !string.Equals(Status, QlhvImportDomainStatuses.SkippedDisabled, StringComparison.Ordinal);

    public bool Enabled =>
        !string.Equals(Status, QlhvImportDomainStatuses.SkippedDisabled, StringComparison.Ordinal);

    public bool Required =>
        string.Equals(Domain, QlhvImportDomains.HocVien, StringComparison.Ordinal);

    public string SnapshotState => (RequestReasonCode ?? Status) switch
    {
        QlhvImportDomainStatuses.SkippedNotRequested => "NOT_REQUESTED",
        QlhvImportDomainStatuses.SkippedDisabled => "NOT_REQUESTED",
        QlhvImportDomainStatuses.SkippedSourceNotReady => "NOT_READY",
        QlhvImportDomainStatuses.SkippedDependencyNotReady => "NOT_READY",
        QlhvImportDomainStatuses.SkippedSchemaNotReady => "UNKNOWN",
        _ => "READY",
    };

    public string SchemaState => (RequestReasonCode ?? Status) switch
    {
        QlhvImportDomainStatuses.SkippedSchemaNotReady => "NOT_READY",
        QlhvImportDomainStatuses.SkippedNotRequested => "NOT_REQUESTED",
        QlhvImportDomainStatuses.SkippedDisabled => "NOT_REQUESTED",
        _ => "READY",
    };

    public bool Attempted =>
        string.Equals(Status, QlhvImportDomainStatuses.Succeeded, StringComparison.Ordinal) ||
        string.Equals(Status, QlhvImportDomainStatuses.NoOp, StringComparison.Ordinal) ||
        string.Equals(Status, QlhvImportDomainStatuses.Failed, StringComparison.Ordinal);

    public bool Committed =>
        string.Equals(Status, QlhvImportDomainStatuses.Succeeded, StringComparison.Ordinal) ||
        string.Equals(Status, QlhvImportDomainStatuses.NoOp, StringComparison.Ordinal);

    public bool Skipped => Status.StartsWith("SKIPPED_", StringComparison.Ordinal);

    public bool ContributesToPartial =>
        !Required &&
        Requested &&
        !string.Equals(Status, QlhvImportDomainStatuses.Succeeded, StringComparison.Ordinal) &&
        !string.Equals(Status, QlhvImportDomainStatuses.NoOp, StringComparison.Ordinal);

    public string? FailureCode =>
        string.Equals(Status, QlhvImportDomainStatuses.Failed, StringComparison.Ordinal) ||
        (Requested && Skipped)
            ? Status
            : null;

    public string? Reason => Message;

    public QlhvSkippedReasonCounts SkippedReasons
    {
        get
        {
            var skipped = Math.Max(0, Counts.Skipped);
            if (skipped == 0)
            {
                return QlhvSkippedReasonCounts.Empty;
            }

            return Status switch
            {
                QlhvImportDomainStatuses.Succeeded or QlhvImportDomainStatuses.NoOp =>
                    new QlhvSkippedReasonCounts(skipped, 0, 0, 0, 0),
                QlhvImportDomainStatuses.SkippedNotRequested =>
                    new QlhvSkippedReasonCounts(0, skipped, 0, 0, 0),
                QlhvImportDomainStatuses.SkippedDisabled =>
                    new QlhvSkippedReasonCounts(0, 0, skipped, 0, 0),
                QlhvImportDomainStatuses.Failed =>
                    new QlhvSkippedReasonCounts(0, 0, 0, skipped, 0),
                _ => new QlhvSkippedReasonCounts(0, 0, 0, 0, skipped),
            };
        }
    }
}

public sealed record QlhvSkippedReasonCounts(
    int NoChange,
    int NotRequested,
    int Disabled,
    int ValidationRejected,
    int Other)
{
    public static QlhvSkippedReasonCounts Empty { get; } = new(0, 0, 0, 0, 0);

    public int Total => NoChange + NotRequested + Disabled + ValidationRejected + Other;
}

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
