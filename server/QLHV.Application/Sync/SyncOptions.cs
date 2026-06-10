namespace QLHV.Application.Sync;

/// <summary>
/// Non-secret configuration for one-way sync from CSDT_V2 to QLHV_APP.
/// Connection string values are never stored here. Only names/keys are stored.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>Connection string name for the QLHV_APP target database.</summary>
    public string QlhvAppConnectionName { get; set; } = "QLHV_APP";

    /// <summary>Connection settings key for CSDT_V1. Phase A only reserves the key for the later Admin screen.</summary>
    public string V1ConnectionName { get; set; } = "CSDT_V1";

    /// <summary>Connection settings key for CSDT_V2, the read-only source for this sync.</summary>
    public string V2ConnectionName { get; set; } = "CSDT_V2";

    /// <summary>Batch size planned for Phase B execution.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>Command timeout planned for Phase B execution, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Phase A is always dry-run. Phase B can use this as its default safety switch.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Maximum retry attempts for the Polly retry structure.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for exponential backoff, in seconds.</summary>
    public int RetryBaseDelaySeconds { get; set; } = 2;
}
