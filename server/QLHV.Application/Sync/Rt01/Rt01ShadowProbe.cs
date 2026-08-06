namespace QLHV.Application.Sync.Rt01;

public sealed class Rt01ShadowProbe : IRt01ShadowProbe
{
    private readonly IRt01ShadowSnapshotReader _reader;
    private readonly TimeProvider _timeProvider;

    public Rt01ShadowProbe(
        IRt01ShadowSnapshotReader reader,
        TimeProvider? timeProvider = null)
    {
        _reader = reader;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Rt01ShadowObservation> ObserveAsync(
        Rt01ShadowRoute route,
        string? previousSourceFingerprint,
        int detectionLatencyBudgetSeconds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _reader.ReadAsync(route, cancellationToken);
        return Rt01ShadowPlanner.Build(
            route,
            snapshots,
            previousSourceFingerprint,
            detectionLatencyBudgetSeconds,
            _timeProvider.GetUtcNow().UtcDateTime);
    }
}
