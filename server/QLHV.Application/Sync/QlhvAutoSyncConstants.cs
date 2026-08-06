namespace QLHV.Application.Sync;

public static class QlhvAutoSyncConstants
{
    private static readonly string[] FixedSourceOrder = ["OTO", "MOTO"];

    public const string StartupTrigger = "STARTUP";
    public const string ManualTrigger = "MANUAL";
    public const string SessionStartTrigger = "SESSION_START";
    public const string AppOpenTrigger = "APP_OPEN";

    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string Succeeded = "SUCCEEDED";
    public const string PartialSuccess = "PARTIAL_SUCCESS";
    public const string PartialFailed = "PARTIAL_FAILED";
    public const string NeedsPlan = "NEEDS_PLAN";
    public const string Failed = "FAILED";

    public const string NoSyncNeededDecision = "NO_SYNC_NEEDED";
    public const string ActiveOperationDecision = "ACTIVE_OPERATION";
    public const string CooldownDecision = "COOLDOWN";
    public const string StartedDecision = "STARTED";
    public const string NotReadyDecision = "NOT_READY";
    public const string FailedToQueueDecision = "FAILED_TO_QUEUE";
    public const string BlockedByRealtimePrimaryWriterDecision =
        "AUTOSYNC_BLOCKED_BY_REALTIME_PRIMARY_WRITER";
    public const string SupersededByRealtimeMasterDecision =
        "AUTOSYNC_SUPERSEDED_BY_REALTIME_MASTER";

    public const string ConnectingStage = "CONNECTING";
    public const string RefreshOtoStage = "REFRESH_OTO";
    public const string SyncOtoStage = "SYNC_OTO";
    public const string RefreshMotoStage = "REFRESH_MOTO";
    public const string SyncMotoStage = "SYNC_MOTO";
    public const string LoadingDataStage = "LOADING_DATA";
    public const string CompletedStage = "COMPLETED";
    public const string FailedStage = "FAILED";

    public static IReadOnlyList<string> CanonicalSourceOrder =>
        Array.AsReadOnly(FixedSourceOrder);

    public static string[] NormalizeSourceOrderTokens(IEnumerable<string?>? sourceOrder)
        => (sourceOrder ?? Array.Empty<string>())
            .Select(value => value?.Trim().ToUpperInvariant() ?? string.Empty)
            .ToArray();

    public static bool IsCanonicalSourceOrder(IEnumerable<string>? sourceOrder)
    {
        var result = NormalizeSourceOrderTokens(sourceOrder);
        return result.Length == FixedSourceOrder.Length &&
               result.SequenceEqual(FixedSourceOrder, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> NormalizeSourceOrder(IEnumerable<string>? sourceOrder)
    {
        var result = NormalizeSourceOrderTokens(sourceOrder);
        if (!IsCanonicalSourceOrder(result))
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
            AppOpenTrigger => AppOpenTrigger,
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
