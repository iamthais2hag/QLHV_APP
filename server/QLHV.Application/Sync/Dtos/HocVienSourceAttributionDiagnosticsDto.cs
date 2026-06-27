namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Read-only diagnostics for deciding which DATA profile owns existing App_HocVien rows.
/// The API returns aggregate counts only and must not expose raw CCCD/GPLX or connection details.
/// </summary>
public sealed class HocVienSourceAttributionDiagnosticsResultDto
{
    public bool IsReadOnly => true;

    public bool CanRead { get; init; }

    public string Status { get; init; } = "Unknown";

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();

    public HocVienSourceAttributionDiagnosticsDto? Diagnostics { get; init; }
}

public sealed class HocVienSourceAttributionDiagnosticsDto
{
    public DateTime CheckedAtUtc { get; init; }

    public int TargetRows { get; init; }

    public int TargetRowsWithSourceProfileCode { get; init; }

    public int TargetRowsWithoutSourceProfileCode { get; init; }

    public int MatchedWithDataV1ByMaDk { get; init; }

    public int MatchedWithDataV2ByMaDk { get; init; }

    public int MatchedBoth { get; init; }

    public int MatchedNeither { get; init; }

    public string Recommendation { get; init; } = "CannotDetermine";

    public IReadOnlyList<HocVienSourceProfileAttributionDto> SourceProfiles { get; init; } =
        Array.Empty<HocVienSourceProfileAttributionDto>();

    public IReadOnlyList<HocVienTargetSourceProfileDistributionDto> TargetSourceProfileDistribution { get; init; } =
        Array.Empty<HocVienTargetSourceProfileDistributionDto>();
}

public sealed class HocVienSourceProfileAttributionDto
{
    public string SourceProfileCode { get; init; } = string.Empty;

    public bool CanRead { get; init; }

    public int SourceRows { get; init; }

    public int MatchedTargetRowsByMaDk { get; init; }

    public string? Issue { get; init; }
}

public sealed class HocVienTargetSourceProfileDistributionDto
{
    public string SourceProfileCode { get; init; } = string.Empty;

    public int Total { get; init; }
}

public sealed class HocVienTargetAttributionKeyDto
{
    public string MaDK { get; init; } = string.Empty;

    public string? SourceProfileCode { get; init; }
}

public sealed class HocVienSourceMaDkReadResultDto
{
    public string SourceProfileCode { get; init; } = string.Empty;

    public bool CanRead { get; init; }

    public IReadOnlyCollection<string> MaDks { get; init; } = Array.Empty<string>();

    public string? Issue { get; init; }

    public SyncErrorDto? Error { get; init; }
}
