namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Manual guarded execution request for HocVien sync.
/// The confirmation fields are intentionally explicit so Swagger cannot execute writes by accident.
/// </summary>
public sealed class HocVienSyncExecuteRequest
{
    public const string RequiredConfirmationText = "EXECUTE_DONG_BO_V2_HOC_VIEN";

    public bool ConfirmTargetWrites { get; init; }
    public string? ConfirmationText { get; init; }
    public HocVienSourceFilter? Filter { get; init; }

    /// <summary>Optional hard cap for first local test runs. Null means all rows matching the filter.</summary>
    public int? MaxRows { get; init; }
}
