namespace QLHV.Application.Sync;

public static class QlhvAutoSyncConstants
{
    public const string StartupTrigger = "STARTUP";
    public const string ManualTrigger = "MANUAL";
    public const string SessionStartTrigger = "SESSION_START";

    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string Succeeded = "SUCCEEDED";
    public const string PartialFailed = "PARTIAL_FAILED";
    public const string Failed = "FAILED";

    public const string ConnectingStage = "CONNECTING";
    public const string RefreshOtoStage = "REFRESH_OTO";
    public const string SyncOtoStage = "SYNC_OTO";
    public const string RefreshMotoStage = "REFRESH_MOTO";
    public const string SyncMotoStage = "SYNC_MOTO";
    public const string LoadingDataStage = "LOADING_DATA";
    public const string CompletedStage = "COMPLETED";
    public const string FailedStage = "FAILED";

    public static IReadOnlyList<string> NormalizeSourceOrder(IEnumerable<string>? sourceOrder)
    {
        var result = (sourceOrder ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .ToArray();
        if (result.Length != 2 ||
            !string.Equals(result[0], "OTO", StringComparison.Ordinal) ||
            !string.Equals(result[1], "MOTO", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "QlhvAutoSync.SourceOrder phai la OTO roi MOTO, moi nguon dung mot lan.");
        }

        return result;
    }

    public static string NormalizeTrigger(string? triggerType)
        => triggerType?.Trim().ToUpperInvariant() switch
        {
            StartupTrigger => StartupTrigger,
            ManualTrigger => ManualTrigger,
            SessionStartTrigger => SessionStartTrigger,
            _ => throw new ArgumentException("Auto Sync trigger khong hop le.", nameof(triggerType)),
        };

    public static string RefreshStage(string sourceType)
        => QlhvOperationSourceCatalog.GetRequired(sourceType).SourceType switch
        {
            "OTO" => RefreshOtoStage,
            "MOTO" => RefreshMotoStage,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType)),
        };

    public static string SyncStage(string sourceType)
        => QlhvOperationSourceCatalog.GetRequired(sourceType).SourceType switch
        {
            "OTO" => SyncOtoStage,
            "MOTO" => SyncMotoStage,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType)),
        };
}
