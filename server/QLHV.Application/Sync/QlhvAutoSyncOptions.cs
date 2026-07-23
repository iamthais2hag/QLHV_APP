namespace QLHV.Application.Sync;

public sealed class QlhvAutoSyncOptions
{
    public const string SectionName = "QlhvAutoSync";

    // Safe defaults: Development/Test and an unconfigured Production host do not run.
    public bool Enabled { get; set; }

    public bool RunOnServerStartup { get; set; }

    public bool RefreshBackupBeforeSync { get; set; } = true;

    public string[] SourceOrder { get; set; } = ["OTO", "MOTO"];

    public int QueueCapacity { get; set; } = 4;

    public int ReadinessPollSeconds { get; set; } = 5;

    public int OperationPollMilliseconds { get; set; } = 500;

    // Processes that reach readiness a little later than their peers join the
    // recent STARTUP run instead of refreshing the same databases a second time.
    public int StartupDedupeWindowSeconds { get; set; } = 300;

    // Repeated desktop-icon invocations within this short window observe the
    // same completed session instead of immediately creating a duplicate run.
    public int SessionStartDedupeWindowSeconds { get; set; } = 30;
}
