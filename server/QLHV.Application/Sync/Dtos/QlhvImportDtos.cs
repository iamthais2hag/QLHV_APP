namespace QLHV.Application.Sync.Dtos;

public class QlhvImportRequest
{
    public string SourceProfileCode { get; set; } = string.Empty;

    public string MaCSDT { get; set; } = string.Empty;

    public string? MaKhoaHoc { get; set; }
}

public sealed class QlhvImportExecuteRequest : QlhvImportRequest
{
    public string? ConfirmText { get; set; }
}

public sealed class QlhvImportPlanDto
{
    public bool IsReadOnly { get; init; } = true;

    public string SourceProfileCode { get; init; } = string.Empty;

    public string SourceDatabaseName { get; init; } = string.Empty;

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

    public bool Executable => Blockers.Count == 0;

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class QlhvImportExecuteResultDto
{
    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public QlhvImportPlanDto Plan { get; init; } = new();

    public int InsertedHocVienRows { get; init; }

    public int UpdatedHocVienRows { get; init; }

    public int ReactivatedHocVienRows { get; init; }

    public int SoftDeletedHocVienRows { get; init; }

    public int SkippedHocVienRows { get; init; }
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

    public bool Executable => Blockers.Count == 0;

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class QlhvImportSourceSnapshot
{
    public string SourceDatabaseName { get; init; } = string.Empty;

    public IReadOnlyList<V2HocVienSourceRow> HocVienRows { get; init; } = Array.Empty<V2HocVienSourceRow>();

    public int KhoaHocRows { get; init; }
}

public sealed class QlhvImportTargetSnapshot
{
    public int CurrentAppHocVienRows { get; init; }

    public int AppKhoaHocRows { get; init; }

    public IReadOnlyDictionary<string, string> ExistingHocVienHashes { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<global::QLHV.Application.Sync.QlhvFullSyncTargetRow> HocVienRows { get; init; } =
        Array.Empty<global::QLHV.Application.Sync.QlhvFullSyncTargetRow>();

    public int DuplicateTargetIdentityRows { get; init; }

    public int TargetRowsForSourceProfile { get; init; }

    public int TargetExactIdentityMatches { get; init; }

    public int TargetMaDkConflictsOtherProfiles { get; init; }

    public int SoftDeletedIdentityConflicts { get; init; }

    public bool SourceProfileConstraintExists { get; init; }

    public bool SourceProfileAllowedByConstraint { get; init; } = true;
}
