namespace QLHV.Application.Sync;

public sealed class QlhvAutoSyncOptions
{
    public const string SectionName = "QlhvAutoSync";

    // Safe defaults: Development/Test and an unconfigured Production host do not run.
    public bool Enabled { get; set; }

    public bool RunOnServerStartup { get; set; }

    // Existing Live -> BAK -> QLHV Auto Sync is a fallback path. Polling and
    // manual execution require their own explicit production switches.
    public bool PollingEnabled { get; set; }

    public bool IsFallbackOnly { get; set; } = true;

    public bool FallbackModeEnabled { get; set; }

    public int ActiveRunHeartbeatTimeoutSeconds { get; set; } = 120;

    public int HeartbeatIntervalSeconds { get; set; } = 15;

    public bool RefreshBackupBeforeSync { get; set; } = true;

    public string[] SourceOrder { get; set; } = ["OTO", "MOTO"];

    public int QueueCapacity { get; set; } = 4;

    public int ReadinessPollSeconds { get; set; } = 5;

    // Once the Production host is ready, re-check freshness at this bounded
    // interval. QueueIfNeeded prevents an up-to-date or active system from
    // creating a duplicate run.
    public int PollingIntervalSeconds { get; set; } = 60;

    public int OperationPollMilliseconds { get; set; } = 500;

    // Processes that reach readiness a little later than their peers join the
    // recent STARTUP run instead of refreshing the same databases a second time.
    public int StartupDedupeWindowSeconds { get; set; } = 300;

    // Repeated authenticated app-open requests within this short window observe
    // the same completed run instead of immediately creating a duplicate run.
    public int SessionStartDedupeWindowSeconds { get; set; } = 30;
}
