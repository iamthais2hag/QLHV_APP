using Microsoft.Extensions.Options;

namespace QLHV.Application.Sync.Rt01;

/// <summary>
/// Dormant RT-01 shadow loop. It has no writer dependency and is intentionally
/// not registered by the API or Worker composition roots. Production polling
/// therefore remains off until a separate operator-approved activation task.
/// </summary>
public sealed class Rt01ShadowWorker
{
    private readonly IRt01ShadowProbe _probe;
    private readonly IRt01ShadowObservationSink _sink;
    private readonly Rt01ShadowOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, string> _sourceFingerprints =
        new(StringComparer.Ordinal);

    public Rt01ShadowWorker(
        IRt01ShadowProbe probe,
        IRt01ShadowObservationSink sink,
        IOptions<Rt01ShadowOptions> options,
        TimeProvider? timeProvider = null)
    {
        _probe = probe;
        _sink = sink;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await RunPassAsync(cancellationToken);
            await Task.Delay(
                TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                _timeProvider,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Rt01ShadowObservation>> RunPassAsync(
        CancellationToken cancellationToken = default)
    {
        var observations = new List<Rt01ShadowObservation>(
            Rt01ShadowRouteCatalog.Ordered.Count);
        foreach (var route in Rt01ShadowRouteCatalog.Ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sourceFingerprints.TryGetValue(route.SourceType, out var previousFingerprint);

            Rt01ShadowObservation observation;
            try
            {
                observation = await _probe.ObserveAsync(
                    route,
                    previousFingerprint,
                    _options.PollIntervalSeconds,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                observation = Rt01ShadowObservation.ReadFailure(
                    route,
                    _options.PollIntervalSeconds,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    exception);
            }

            if (!string.IsNullOrWhiteSpace(observation.SourceFingerprint) &&
                !string.Equals(
                    observation.Status,
                    Rt01ShadowStatuses.ReadFailed,
                    StringComparison.Ordinal))
            {
                _sourceFingerprints[route.SourceType] = observation.SourceFingerprint;
            }

            await _sink.ObserveAsync(observation, cancellationToken);
            observations.Add(observation);
        }

        return observations;
    }
}

/// <summary>Process-memory-only shadow status; never writes a database.</summary>
public sealed class InMemoryRt01ShadowObservationSink : IRt01ShadowObservationSink
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Rt01ShadowObservation> _latest =
        new(StringComparer.Ordinal);

    public Task ObserveAsync(
        Rt01ShadowObservation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _latest[observation.SourceType] = observation;
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<Rt01ShadowObservation> GetLatest()
    {
        lock (_gate)
        {
            return Rt01ShadowRouteCatalog.Ordered
                .Select(route =>
                    _latest.TryGetValue(route.SourceType, out var observation)
                        ? observation
                        : null)
                .Where(observation => observation is not null)
                .Cast<Rt01ShadowObservation>()
                .ToArray();
        }
    }
}
