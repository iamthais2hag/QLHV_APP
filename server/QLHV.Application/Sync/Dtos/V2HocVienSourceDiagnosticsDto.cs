namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Safe read-only diagnostics for the CSDT_V2 HocVien source before any guarded execute run.
/// Contains aggregate counts only; never includes connection details or sensitive raw identity values.
/// </summary>
public sealed class V2HocVienSourceDiagnosticsResultDto
{
    public bool IsReadOnly => true;

    public bool CanRead { get; init; }

    public string Status { get; init; } = "ThieuCauHinh";

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();

    public V2HocVienSourceDiagnosticsDto? Diagnostics { get; init; }
}

public sealed class V2HocVienSourceDiagnosticsDto
{
    public DateTime CheckedAtUtc { get; init; }

    public int SourceRows { get; init; }

    public int DuplicateMaDkCount { get; init; }

    public int DuplicateMaDkRowCount { get; init; }

    public int MissingMaDkCount { get; init; }

    public int MissingHoTenCount { get; init; }

    public IReadOnlyList<SourceValueDistributionDto> GioiTinhDistribution { get; init; } =
        Array.Empty<SourceValueDistributionDto>();

    public SoCmtLengthDiagnosticsDto SoCmtLength { get; init; } = new();

    public int MissingNgaySinhCount { get; init; }

    public int NgaySinhParseIssueCount { get; init; }

    public int MissingHangDaoTaoCount { get; init; }

    public int HangDaoTaoUnmatchedDmHangDtCount { get; init; }

    public int MissingNoiTTCodesCount { get; init; }

    public int NoiTTUnmatchedDmDvhcCount { get; init; }

    public int MissingMaKhoaHocCount { get; init; }

    public int MaKhoaHocUnmatchedKhoaHocCount { get; init; }
}

public sealed class SourceValueDistributionDto
{
    public string Value { get; init; } = string.Empty;

    public int Total { get; init; }
}

public sealed class SoCmtLengthDiagnosticsDto
{
    public int NineDigits { get; init; }

    public int TwelveDigits { get; init; }

    public int Other { get; init; }

    public int NullOrEmpty { get; init; }
}
