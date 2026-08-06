using System.Security.Cryptography;
using QLHV.Application.Sync.Rt01;
using QLHV.Application.Sync.Rt03;
using QLHV.Infrastructure.Sync.Rt01;

namespace QLHV.Infrastructure.Sync.Rt03;

public sealed class Rt03RealtimeControlService : IRt03RealtimeControlService
{
    private static readonly TimeSpan WorkerFreshness = TimeSpan.FromSeconds(30);
    private readonly IRt03RealtimeControlStore _store;
    private readonly IRt03EventBacklogProbe _backlog;
    private readonly TimeProvider _timeProvider;

    public Rt03RealtimeControlService(
        IRt03RealtimeControlStore store,
        IRt03EventBacklogProbe backlog,
        TimeProvider timeProvider)
    {
        _store = store;
        _backlog = backlog;
        _timeProvider = timeProvider;
    }

    public async Task<Rt03RealtimeControlStatusDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var control = await _store.ReadAsync(cancellationToken);
        var worker = await _store.ReadWorkerSnapshotAsync(cancellationToken);
        var activeRun = await _store.ReadActiveRunOnceAsync(cancellationToken);
        IReadOnlyList<Rt03RealtimeProfileBacklog> profiles;
        try
        {
            profiles = await _backlog.ReadAsync(Rt03Profiles.Ordered, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            profiles = [];
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var workerRunning = worker.LastHeartbeatUtc.HasValue &&
            worker.LastHeartbeatUtc.Value >= nowUtc.Subtract(WorkerFreshness) &&
            !string.Equals(worker.Status, Rt03WorkerStatuses.Stopped,
                StringComparison.Ordinal);
        return new Rt03RealtimeControlStatusDto
        {
            State = control.State,
            UpdatedAtUtc = control.UpdatedAtUtc,
            UpdatedBy = control.UpdatedBy,
            Reason = control.Reason,
            RowVersion = Convert.ToBase64String(control.RowVersion),
            WorkerStatus = worker.Status,
            WorkerRunning = workerRunning,
            WorkerInstanceId = worker.InstanceId,
            LastHeartbeatUtc = worker.LastHeartbeatUtc,
            LastSuccessfulCycleUtc = worker.LastSuccessfulCycleUtc,
            CycleOutcome = worker.LastCycleOutcome,
            BlockerReason = string.Equals(control.State,
                Rt03RealtimeControlStates.Blocked, StringComparison.Ordinal)
                ? control.Reason ?? worker.LastErrorCode
                : null,
            Profiles = profiles,
            ActiveRunOnce = activeRun,
        };
    }

    public async Task<Rt03RealtimeControlStatusDto> EnableAsync(
        Rt03RealtimeControlChangeRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        await _store.ChangeStateAsync(
            Rt03RealtimeControlStates.On,
            actor,
            "OPERATOR_ENABLED",
            ParseRowVersion(request),
            cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async Task<Rt03RealtimeControlStatusDto> DisableAsync(
        Rt03RealtimeControlChangeRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        await _store.ChangeStateAsync(
            Rt03RealtimeControlStates.Off,
            actor,
            "OPERATOR_DISABLED",
            ParseRowVersion(request),
            cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public Task<Rt03RealtimeRunRequest> RunOnceAsync(
        string actor,
        CancellationToken cancellationToken = default) =>
        _store.QueueRunOnceAsync(actor, cancellationToken);

    private static byte[] ParseRowVersion(Rt03RealtimeControlChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var bytes = Convert.FromBase64String(request.ExpectedRowVersion ?? string.Empty);
            return bytes.Length == 8
                ? bytes
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new ArgumentException("ExpectedRowVersion is invalid.", nameof(request));
        }
    }
}

public sealed class Rt03RealtimeIntegrityPreviewService :
    IRt03RealtimeIntegrityPreviewService
{
    private readonly Rt01aOtoDriftEvidenceReader _reader;
    private readonly TimeProvider _timeProvider;

    public Rt03RealtimeIntegrityPreviewService(
        Rt01aOtoDriftEvidenceReader reader,
        TimeProvider timeProvider)
    {
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async Task<Rt03RealtimeIntegrityPreviewDto> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = new List<Rt03RealtimeIntegrityProfilePreview>();
        foreach (var route in Rt01ShadowRouteCatalog.Ordered)
        {
            var raw = await _reader.ReadAsync(route, cancellationToken);
            var evidence = Rt01aDriftClassifier.Classify(
                raw,
                RandomNumberGenerator.GetBytes(32),
                route.SourceProfileCode);
            var driftCount = evidence.WouldInsertRows + evidence.WouldUpdateRows +
                evidence.WouldReactivateRows + evidence.TargetOnlyActiveRows +
                evidence.ConflictRows;
            profiles.Add(new Rt03RealtimeIntegrityProfilePreview(
                route.SourceProfileCode,
                driftCount == 0 ? "MATCHED" : "DRIFT_DETECTED",
                evidence.SourceActiveRows,
                evidence.TargetActiveRows,
                evidence.WouldInsertRows,
                evidence.WouldUpdateRows + evidence.WouldReactivateRows,
                evidence.TargetOnlyActiveRows,
                evidence.ConflictRows,
                evidence.ManualReviewRows));
        }

        return new Rt03RealtimeIntegrityPreviewDto(
            true,
            _timeProvider.GetUtcNow().UtcDateTime,
            profiles.All(item => item.Status == "MATCHED")
                ? "MATCHED"
                : "DRIFT_DETECTED",
            profiles);
    }
}
