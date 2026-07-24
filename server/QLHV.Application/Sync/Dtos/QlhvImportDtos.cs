using System.Text.Json.Serialization;

using QLHV.Application.HocVien.Photos;

namespace QLHV.Application.Sync.Dtos;

public class QlhvImportRequest
{
    public string SourceProfileCode { get; set; } = string.Empty;

    public string MaCSDT { get; set; } = string.Empty;

    public string? MaKhoaHoc { get; set; }
}

public sealed class QlhvImportExecuteRequest : QlhvImportRequest
{
    public string? ExpectedSnapshotToken { get; set; }

    [JsonIgnore]
    public string Actor { get; set; } = QlhvOperationActors.ManualAdmin;
}

public sealed record QlhvImportPlanDto
{
    public bool IsReadOnly { get; init; } = true;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string SourceDatabaseName { get; init; } = string.Empty;

    public string BackupSnapshotToken { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public string MaCSDT { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public int SourceHocVienRows { get; init; }

    public int SourceDistinctMaDkRows { get; init; }

    public int DuplicateSourceMaDkRows { get; init; }

    public int SourceKhoaHocRows { get; init; }

    public int CurrentAppHocVienRows { get; init; }

    public int CurrentAppKhoaHocRows { get; init; }

    public int TargetRowsForSourceProfile { get; init; }

    public int TargetExactIdentityMatches { get; init; }

    public int TargetMaDkConflictsOtherProfiles { get; init; }

    public int SoftDeletedIdentityConflicts { get; init; }

    public bool SourceProfileConstraintExists { get; init; }

    public bool SourceProfileAllowedByConstraint { get; init; }

    public int PlannedInsertHocVienRows { get; init; }

    public int PlannedUpdateHocVienRows { get; init; }

    public int PlannedReactivateHocVienRows { get; init; }

    public int PlannedSoftDeleteHocVienRows { get; init; }

    public int PlannedSkipHocVienRows { get; init; }

    public int PlannedUpsertHocVienRows { get; init; }

    public int PlannedUpsertKhoaHocRows { get; init; }

    public QlhvEntitySyncCountsDto HocVien { get; init; } = new();

    public QlhvEntitySyncCountsDto KhoaHoc { get; init; } = new();

    public QlhvEntitySyncCountsDto GiaoVien { get; init; } = new();

    public QlhvEntitySyncCountsDto KhoaHocGiaoVien { get; init; } = new();

    public HocVienPhotoPlanDto Photo { get; init; } = new(0, 0, 0, 0, 0);

    public int DuplicateSourceKeys { get; init; }

    public int RelationConflicts { get; init; }

    internal int SourceRelationRows { get; init; }

    public string HocVienStatus { get; init; } = QlhvImportDomainStatuses.Blocked;

    public string KhoaHocStatus { get; init; } = QlhvImportDomainStatuses.SkippedSourceNotReady;

    public string GiaoVienStatus { get; init; } = QlhvImportDomainStatuses.SkippedSourceNotReady;

    public string RelationStatus { get; init; } = QlhvImportDomainStatuses.SkippedDependencyNotReady;

    public IReadOnlyList<string> HocVienBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> KhoaHocBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GiaoVienBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelationBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> OptionalWarnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExecutableDomains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SkippedDomains { get; init; } = Array.Empty<string>();

    public bool Executable =>
        Blockers.Count == 0 &&
        HocVienBlockers.Count == 0 &&
        ExecutableDomains.Contains(QlhvImportDomains.HocVien, StringComparer.Ordinal);

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class QlhvImportExecuteResultDto
{
    public Guid? OperationId { get; init; }

    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public QlhvImportPlanDto Plan { get; init; } = new();

    public int InsertedHocVienRows { get; init; }

    public int UpdatedHocVienRows { get; init; }

    public int ReactivatedHocVienRows { get; init; }

    public int SoftDeletedHocVienRows { get; init; }

    public int SkippedHocVienRows { get; init; }

    public QlhvEntitySyncCountsDto HocVien { get; init; } = new();

    public QlhvEntitySyncCountsDto KhoaHoc { get; init; } = new();

    public QlhvEntitySyncCountsDto GiaoVien { get; init; } = new();

    public QlhvEntitySyncCountsDto KhoaHocGiaoVien { get; init; } = new();

    public HocVienPhotoQueueBatchResult? PhotoQueue { get; init; }

    public IReadOnlyList<QlhvImportDomainResultDto> DomainResults { get; init; } =
        Array.Empty<QlhvImportDomainResultDto>();
}

public sealed class QlhvImportDomainResultDto
{
    public string Domain { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? Message { get; init; }

    public QlhvEntitySyncCountsDto Counts { get; init; } = new();
}

public sealed class QlhvEntitySyncCountsDto
{
    public int SourceRows { get; init; }

    public int Insert { get; init; }

    public int Update { get; init; }

    public int Reactivate { get; init; }

    public int SoftDelete { get; init; }

    public int Skip { get; init; }

    public int DuplicateSourceKeys { get; init; }

    public int Upsert => Insert + Update + Reactivate;
}

public sealed class QlhvImportDiagnosticsDto
{
    public bool IsReadOnly { get; init; } = true;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string SourceDatabaseName { get; init; } = string.Empty;

    public string MaCSDT { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public int SourceHocVienRows { get; init; }

    public int SourceDistinctMaDkRows { get; init; }

    public int DuplicateSourceMaDkRows { get; init; }

    public int CurrentAppHocVienRows { get; init; }

    public int TargetRowsForSourceProfile { get; init; }

    public int TargetExactIdentityMatches { get; init; }

    public int TargetMaDkConflictsOtherProfiles { get; init; }

    public int SoftDeletedIdentityConflicts { get; init; }

    public bool SourceProfileConstraintExists { get; init; }

    public bool SourceProfileAllowedByConstraint { get; init; }

    public int PlannedInsertHocVienRows { get; init; }

    public int PlannedUpdateHocVienRows { get; init; }

    public int PlannedReactivateHocVienRows { get; init; }

    public int PlannedSoftDeleteHocVienRows { get; init; }

    public int PlannedSkipHocVienRows { get; init; }

    public int PlannedUpsertHocVienRows { get; init; }

    public QlhvEntitySyncCountsDto HocVien { get; init; } = new();

    public QlhvEntitySyncCountsDto KhoaHoc { get; init; } = new();

    public QlhvEntitySyncCountsDto GiaoVien { get; init; } = new();

    public QlhvEntitySyncCountsDto KhoaHocGiaoVien { get; init; } = new();

    public HocVienPhotoPlanDto Photo { get; init; } = new(0, 0, 0, 0, 0);

    public int DuplicateSourceKeys { get; init; }

    public int RelationConflicts { get; init; }

    public string HocVienStatus { get; init; } = QlhvImportDomainStatuses.Blocked;

    public string KhoaHocStatus { get; init; } = QlhvImportDomainStatuses.SkippedSourceNotReady;

    public string GiaoVienStatus { get; init; } = QlhvImportDomainStatuses.SkippedSourceNotReady;

    public string RelationStatus { get; init; } = QlhvImportDomainStatuses.SkippedDependencyNotReady;

    public IReadOnlyList<string> HocVienBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> KhoaHocBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GiaoVienBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelationBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> OptionalWarnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExecutableDomains { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SkippedDomains { get; init; } = Array.Empty<string>();

    public bool Executable =>
        Blockers.Count == 0 &&
        HocVienBlockers.Count == 0 &&
        ExecutableDomains.Contains(QlhvImportDomains.HocVien, StringComparer.Ordinal);

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class QlhvImportSourceSnapshot
{
    public string SourceDatabaseName { get; init; } = string.Empty;

    public string BackupSnapshotToken { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public IReadOnlyList<V2HocVienSourceRow> HocVienRows { get; init; } = Array.Empty<V2HocVienSourceRow>();

    public int KhoaHocRows { get; init; }

    public IReadOnlyList<QlhvKhoaHocSourceRow> KhoaHocSourceRows { get; init; } =
        Array.Empty<QlhvKhoaHocSourceRow>();

    public IReadOnlyList<QlhvGiaoVienSourceRow> GiaoVienRows { get; init; } =
        Array.Empty<QlhvGiaoVienSourceRow>();

    public IReadOnlyList<QlhvKhoaHocGiaoVienSourceRow> KhoaHocGiaoVienRows { get; init; } =
        Array.Empty<QlhvKhoaHocGiaoVienSourceRow>();

    public IReadOnlyList<string> HocVienWarnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> KhoaHocBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GiaoVienBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelationBlockers { get; init; } = Array.Empty<string>();
}

public sealed class QlhvImportTargetSnapshot
{
    public int CurrentAppHocVienRows { get; init; }

    public int AppKhoaHocRows { get; init; }

    public IReadOnlyDictionary<string, string> ExistingHocVienHashes { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<global::QLHV.Application.Sync.QlhvFullSyncTargetRow> HocVienRows { get; init; } =
        Array.Empty<global::QLHV.Application.Sync.QlhvFullSyncTargetRow>();

    public IReadOnlyList<global::QLHV.Application.Sync.QlhvEntityFullSyncTargetRow> KhoaHocRows { get; init; } =
        Array.Empty<global::QLHV.Application.Sync.QlhvEntityFullSyncTargetRow>();

    public IReadOnlyList<global::QLHV.Application.Sync.QlhvEntityFullSyncTargetRow> GiaoVienRows { get; init; } =
        Array.Empty<global::QLHV.Application.Sync.QlhvEntityFullSyncTargetRow>();

    public IReadOnlyList<global::QLHV.Application.Sync.QlhvEntityFullSyncTargetRow> RelationRows { get; init; } =
        Array.Empty<global::QLHV.Application.Sync.QlhvEntityFullSyncTargetRow>();

    public int DuplicateTargetIdentityRows { get; init; }

    public int TargetRowsForSourceProfile { get; init; }

    public int TargetExactIdentityMatches { get; init; }

    public int TargetMaDkConflictsOtherProfiles { get; init; }

    public int SoftDeletedIdentityConflicts { get; init; }

    public bool SourceProfileConstraintExists { get; init; }

    public bool SourceProfileAllowedByConstraint { get; init; } = true;

    public int DuplicateHocVienTargetIdentityRows { get; init; }

    public int DuplicateKhoaHocTargetIdentityRows { get; init; }

    public int DuplicateGiaoVienTargetIdentityRows { get; init; }

    public int DuplicateRelationTargetIdentityRows { get; init; }

    public IReadOnlyList<string> KhoaHocBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GiaoVienBlockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RelationBlockers { get; init; } = Array.Empty<string>();
}
