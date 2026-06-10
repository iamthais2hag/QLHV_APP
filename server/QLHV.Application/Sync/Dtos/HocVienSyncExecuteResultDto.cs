namespace QLHV.Application.Sync.Dtos;

/// <summary>Safe result returned by the guarded manual execution endpoint.</summary>
public sealed class HocVienSyncExecuteResultDto
{
    public bool Accepted { get; init; }
    public string Status { get; init; } = "Rejected";
    public string Message { get; init; } = string.Empty;
    public SyncSummaryDto Summary { get; init; } = new();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
