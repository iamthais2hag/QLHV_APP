namespace QLHV.Application.Sync.Dtos;

/// <summary>
/// Manual guarded sync execution request. Defaults prevent accidental Swagger execution.
/// </summary>
public sealed class SyncExecuteRequest
{
    /// <summary>Must be explicitly true.</summary>
    public bool Confirm { get; set; } = false;

    /// <summary>Must exactly match EXECUTE_DONG_BO_V2_HOC_VIEN by default.</summary>
    public string? ConfirmationText { get; set; }

    /// <summary>Backward-compatible alias for older local clients.</summary>
    public string? ConfirmationPhrase { get; set; }
}
