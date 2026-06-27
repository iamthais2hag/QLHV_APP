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

    public int DataV1SourceRows { get; init; }

    public int DataV2SourceRows { get; init; }

    public int DataV1DistinctSourceMaDk { get; init; }

    public int DataV2DistinctSourceMaDk { get; init; }

    public int DataV1DuplicateSourceMaDkCount { get; init; }

    public int DataV2DuplicateSourceMaDkCount { get; init; }

    public int DataV1InvalidNgaySinhCount { get; init; }

    public int DataV2InvalidNgaySinhCount { get; init; }

    public int MatchedByMaDkDataV1 { get; init; }

    public int MatchedByMaDkDataV2 { get; init; }

    public int ExactFieldMatchDataV1 { get; init; }

    public int ExactFieldMatchDataV2 { get; init; }

    public int V2RowHashMatchDataV1 { get; init; }

    public int V2RowHashMatchDataV2 { get; init; }

    public int StrongerMatchDataV1 { get; init; }

    public int StrongerMatchDataV2 { get; init; }

    public int DataV2OnlyMaDkCount { get; init; }

    public int DataV1OnlyMaDkCount { get; init; }

    public int OverlappingMaDkCount { get; init; }

    public int MatchedBoth { get; init; }

    public int MatchedNeither { get; init; }

    public string Recommendation { get; init; } = "CannotDetermine";

    public string Confidence { get; init; } = "Low";

    public IReadOnlyList<HocVienAttributionFieldDifferenceSummaryDto> ChangedFieldSummary { get; init; } =
        Array.Empty<HocVienAttributionFieldDifferenceSummaryDto>();

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

    public int DistinctSourceMaDk { get; init; }

    public int DuplicateSourceMaDkCount { get; init; }

    public int InvalidNgaySinhCount { get; init; }

    public int MatchedTargetRowsByMaDk { get; init; }

    public int ExactFieldMatchTargetRows { get; init; }

    public int V2RowHashMatchTargetRows { get; init; }

    public int StrongerMatchTargetRows { get; init; }

    public string? Issue { get; init; }
}

public sealed class HocVienAttributionFieldDifferenceSummaryDto
{
    public string FieldName { get; init; } = string.Empty;

    public int DataV1DifferentCount { get; init; }

    public int DataV2DifferentCount { get; init; }
}

public sealed class HocVienTargetSourceProfileDistributionDto
{
    public string SourceProfileCode { get; init; } = string.Empty;

    public int Total { get; init; }
}

public sealed class HocVienComparableAttributionRowDto
{
    public string MaDK { get; init; } = string.Empty;

    public string? SourceProfileCode { get; init; }

    public string? HoTenNormalized { get; init; }

    public DateTime? NgaySinh { get; init; }

    public string? GioiTinh { get; init; }

    public string? MaKhoa { get; init; }

    public string? TenKhoa { get; init; }

    public string? MaHangDT { get; init; }

    public string? HangGPLXHoc { get; init; }

    public string? V2RowHash { get; init; }
}

public sealed class HocVienSourceComparableReadResultDto
{
    public string SourceProfileCode { get; init; } = string.Empty;

    public bool CanRead { get; init; }

    public int SourceRows { get; init; }

    public int DistinctSourceMaDk { get; init; }

    public int DuplicateSourceMaDkCount { get; init; }

    public int InvalidNgaySinhCount { get; init; }

    public IReadOnlyCollection<HocVienComparableAttributionRowDto> Rows { get; init; } =
        Array.Empty<HocVienComparableAttributionRowDto>();

    public string? Issue { get; init; }

    public SyncErrorDto? Error { get; init; }
}
