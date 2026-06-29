namespace QLHV.Application.Sync.Dtos;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MotoSyncDirection
{
    V1_TO_V2 = 1,
    V2_TO_V1 = 2,
}

public sealed class MotoSyncPlanRequest
{
    public MotoSyncDirection Direction { get; set; }

    public string SourceProfileCode { get; set; } = string.Empty;

    public string TargetProfileCode { get; set; } = string.Empty;

    public string? MaKhoaHoc { get; set; }

    public bool AllowDirtyData { get; set; }
}

public sealed class MotoSyncTestExecuteRequest
{
    public MotoSyncDirection Direction { get; set; }

    public string SourceProfileCode { get; set; } = string.Empty;

    public string TargetProfileCode { get; set; } = string.Empty;

    public string? MaKhoaHoc { get; set; }

    public string? ConfirmText { get; set; }
}

public sealed class MotoSyncPlanDto
{
    public bool IsReadOnly { get; init; } = true;

    public MotoSyncDirection Direction { get; init; }

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public bool AllowDirtyData { get; init; }

    public long SourceRows { get; init; }

    public long TargetRows { get; init; }

    public long ExactMaDkOverlap { get; init; }

    public long SourceOnly { get; init; }

    public long TargetOnly { get; init; }

    public long DuplicateBusinessKeyGroups { get; init; }

    public long ShortFullMaDkPairs { get; init; }

    public long MissingKhoaHocDependencies { get; init; }

    public long PlannedInsertNguoiLX { get; init; }

    public long PlannedInsertNguoiLXHoSo { get; init; }

    public long PlannedInsertGiayTo { get; init; }

    public long PlannedUpdate { get; init; }

    public bool Executable { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();
}

public sealed class MotoSyncExecuteResultDto
{
    public bool Executed { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public MotoSyncExecuteSummaryDto? Summary { get; init; }

    public MotoSyncPlanDto? Plan { get; init; }
}

public sealed class MotoSyncExecuteSummaryDto
{
    public MotoSyncDirection Direction { get; init; }

    public string SourceProfileCode { get; init; } = string.Empty;

    public string TargetProfileCode { get; init; } = string.Empty;

    public string? MaKhoaHoc { get; init; }

    public long InsertedNguoiLX { get; init; }

    public long InsertedNguoiLXHoSo { get; init; }

    public long InsertedGiayTo { get; init; }

    public long UpdatedRows { get; init; }

    public long DeletedRows { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    public long DurationMs { get; init; }
}
