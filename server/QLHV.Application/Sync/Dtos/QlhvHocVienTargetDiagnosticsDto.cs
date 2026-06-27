namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Safe read-only diagnostics for the QLHV_APP HocVien target before any guarded execute run.
/// Does not include connection details or sensitive raw identity values.
/// </summary>
public sealed class QlhvHocVienTargetDiagnosticsResultDto
{
    public bool IsReadOnly => true;

    public bool CanRead { get; init; }

    public string Status { get; init; } = "ThieuCauHinh";

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SyncErrorDto> Errors { get; init; } = Array.Empty<SyncErrorDto>();

    public QlhvHocVienTargetDiagnosticsDto? Diagnostics { get; init; }
}

public sealed class QlhvHocVienTargetDiagnosticsDto
{
    public DateTime CheckedAtUtc { get; init; }

    public bool AppHocVienExists { get; init; }

    public bool AppDongBoLogExists { get; init; }

    public IReadOnlyList<RequiredColumnCheckDto> RequiredColumns { get; init; } =
        Array.Empty<RequiredColumnCheckDto>();

    public int? TargetRows { get; init; }

    public bool TargetRowsUseIsDeletedFilter { get; init; }

    public SoCmtLengthDiagnosticsDto? SoCccdLength { get; init; }
}

public sealed class RequiredColumnCheckDto
{
    public string ColumnName { get; init; } = string.Empty;

    public bool Exists { get; init; }
}
