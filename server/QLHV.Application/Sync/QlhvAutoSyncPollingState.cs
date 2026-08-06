using System.Diagnostics;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public interface IQlhvAutoSyncPollingState
{
    QlhvAutoSyncPollingStatusDto Snapshot { get; }

    void Configure(bool enabled, string disabledReason, int pollingIntervalSeconds);

    void MarkPollStarted();

    void MarkPollCompleted(string decision, string? error, TimeSpan nextDelay);

    void MarkStopped();
}

public sealed class QlhvAutoSyncPollingState : IQlhvAutoSyncPollingState
{
    private readonly object _gate = new();
    private readonly DateTime _processStartedAtUtc = GetProcessStartedAtUtc();
    private bool _enabled;
    private bool _isPolling;
    private string? _disabledReason;
    private DateTime? _lastPollStartedAtUtc;
    private DateTime? _lastPollCompletedAtUtc;
    private DateTime? _nextPollAtUtc;
    private string? _lastDecision;
    private string? _lastError;

    public QlhvAutoSyncPollingStatusDto Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new QlhvAutoSyncPollingStatusDto
                {
                    Enabled = _enabled,
                    IsPolling = _isPolling,
                    DisabledReason = _disabledReason,
                    ProcessStartedAtUtc = _processStartedAtUtc,
                    LastPollStartedAtUtc = _lastPollStartedAtUtc,
                    LastPollCompletedAtUtc = _lastPollCompletedAtUtc,
                    NextPollAtUtc = _nextPollAtUtc,
                    LastDecision = _lastDecision,
                    LastError = _lastError,
                };
            }
        }
    }

    public void Configure(bool enabled, string disabledReason, int pollingIntervalSeconds)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _disabledReason = enabled ? null : Sanitize(disabledReason);
            _nextPollAtUtc = enabled
                ? DateTime.UtcNow.AddSeconds(Math.Clamp(pollingIntervalSeconds, 1, 3600))
                : null;
        }
    }

    public void MarkPollStarted()
    {
        lock (_gate)
        {
            _isPolling = true;
            _lastPollStartedAtUtc = DateTime.UtcNow;
            _nextPollAtUtc = null;
        }
    }

    public void MarkPollCompleted(string decision, string? error, TimeSpan nextDelay)
    {
        lock (_gate)
        {
            var completedAtUtc = DateTime.UtcNow;
            _isPolling = false;
            _lastPollCompletedAtUtc = completedAtUtc;
            _nextPollAtUtc = _enabled ? completedAtUtc.Add(nextDelay) : null;
            _lastDecision = Sanitize(decision);
            _lastError = Sanitize(error);
        }
    }

    public void MarkStopped()
    {
        lock (_gate)
        {
            _enabled = false;
            _isPolling = false;
            _nextPollAtUtc = null;
        }
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 300
            ? normalized
            : normalized[..300];
    }

    private static DateTime GetProcessStartedAtUtc()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
