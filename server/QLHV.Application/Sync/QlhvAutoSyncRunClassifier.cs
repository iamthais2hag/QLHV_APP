using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed record QlhvAutoSyncRunClassification(
    bool IsActive,
    bool HeartbeatFresh,
    string Classification,
    DateTime? LastHeartbeatUtc);

public static class QlhvAutoSyncRunClassifier
{
    public static QlhvAutoSyncRunClassification Classify(
        QlhvAutoSyncRunRecord? run,
        DateTime nowUtc,
        int heartbeatTimeoutSeconds)
    {
        if (run is null) return new(false, false, "INACTIVE", null);
        if (IsTerminal(run.Status))
        {
            return new(false, false, "HISTORY", LastActivity(run));
        }

        var nominallyActive = string.Equals(
                run.Status, QlhvAutoSyncConstants.Queued, StringComparison.Ordinal) ||
            string.Equals(run.Status, QlhvAutoSyncConstants.Running, StringComparison.Ordinal);
        var lastActivity = LastActivity(run);
        var fresh = lastActivity >= nowUtc.AddSeconds(-Math.Clamp(
            heartbeatTimeoutSeconds, 30, 900));
        // Compatibility only for pre-contract in-memory callers. Durable SQL
        // rows always populate UpdatedAtUtc and ActiveSlot.
        var compatibilityRecord = run.UpdatedAtUtc == default;
        var hasSlot = run.ActiveSlot || compatibilityRecord;
        var hasWorkPosition = !string.IsNullOrWhiteSpace(run.CurrentSourceType) ||
            !string.IsNullOrWhiteSpace(run.CurrentStage) || compatibilityRecord;
        var active = nominallyActive && hasSlot && run.CompletedAtUtc is null &&
            fresh && hasWorkPosition;
        return new(
            active,
            fresh,
            active ? "ACTIVE" : "INACTIVE_STALE_RUN",
            lastActivity);
    }

    public static DateTime LastActivity(QlhvAutoSyncRunRecord run)
        => run.UpdatedAtUtc == default ? run.CreatedAtUtc : run.UpdatedAtUtc;

    private static bool IsTerminal(string status)
        => status is QlhvAutoSyncConstants.Succeeded or
            QlhvAutoSyncConstants.PartialSuccess or
            QlhvAutoSyncConstants.PartialFailed or
            QlhvAutoSyncConstants.Failed;
}
