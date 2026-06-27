namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Read-only pre-execute plan for HocVien V2 sync.
/// Contains counts and aggregate warning summaries only; no raw CCCD/GPLX values.
/// </summary>
public sealed class HocVienPreExecutePlanResultDto
{
    public bool IsReadOnly => true;

    public bool CanPlan { get; init; }

    public string Status { get; init; } = "ThieuCauHinh";

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();

    public HocVienPreExecutePlanDto? Plan { get; init; }
}

public sealed class HocVienPreExecutePlanDto
{
    public int SourceRows { get; init; }

    public int TargetRows { get; init; }

    public int WouldInsert { get; init; }

    public int WouldUpdate { get; init; }

    public int WouldSkip { get; init; }

    public int TargetOnlyRows { get; init; }

    public int WarningCount { get; init; }

    public IReadOnlyList<HocVienPreExecuteWarningSummaryDto> Warnings { get; init; } =
        Array.Empty<HocVienPreExecuteWarningSummaryDto>();
}

public sealed class HocVienPreExecuteWarningSummaryDto
{
    public string Code { get; init; } = string.Empty;

    public string Field { get; init; } = string.Empty;

    public int Count { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class HocVienTargetSyncSnapshotDto
{
    public string MaDK { get; init; } = string.Empty;

    public string? V2RowHash { get; init; }

    public bool IsDeleted { get; init; }
}
