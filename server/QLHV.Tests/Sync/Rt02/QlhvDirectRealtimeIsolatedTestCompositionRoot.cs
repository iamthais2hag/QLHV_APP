using QLHV.Application.Sync.QlhvDirectRealtime;
using System.Diagnostics;

namespace QLHV.Tests.Sync.Rt02;

/// <summary>
/// The only RT-02A composition root that can construct the isolated writer.
/// It belongs to the test assembly; production projects cannot reference it.
/// </summary>
internal sealed class QlhvDirectRealtimeIsolatedTestCompositionRoot
{
    public QlhvDirectRealtimeIsolatedTestCompositionRoot(
        IQlhvDirectRealtimeFaultInjector? faultInjector = null)
    {
        Store = new Rt02InMemoryTargetStore();
        Checkpoints = new Rt02InMemoryCheckpointStore();
        TransactionFactory = new Rt02InMemoryTransactionFactory(Store);
        Options = new QlhvDirectRealtimeOptions
        {
            EnableQlhvDirectRealtime = true,
            EnableQlhvDirectRealtimeShadow = false,
            EnableQlhvDirectRealtimeWrites = true,
            EnableQlhvDirectRealtimeDeletes = false,
            EnableQlhvDirectRealtimeIsolatedApply = true,
        };
        Cycle = new QlhvDirectRealtimeApplyCycle(
            Options,
            TransactionFactory,
            Checkpoints,
            faultInjector);
    }

    public QlhvDirectRealtimeOptions Options { get; }

    public Rt02InMemoryTargetStore Store { get; }

    public Rt02InMemoryCheckpointStore Checkpoints { get; }

    public Rt02InMemoryTransactionFactory TransactionFactory { get; }

    public QlhvDirectRealtimeApplyCycle Cycle { get; }
}

internal sealed class Rt02TestLearner
{
    public required string IdentityHmac { get; init; }

    public required string SourceProfile { get; init; }

    public required string HoTen { get; set; }

    public required string MappedHash { get; set; }

    public required string QlhvOwnedHash { get; set; }

    public bool Active { get; set; } = true;

    public bool SoftDeleted { get; set; }

    public Rt02TestLearner Clone()
        => new()
        {
            IdentityHmac = IdentityHmac,
            SourceProfile = SourceProfile,
            HoTen = HoTen,
            MappedHash = MappedHash,
            QlhvOwnedHash = QlhvOwnedHash,
            Active = Active,
            SoftDeleted = SoftDeleted,
        };
}

internal sealed class Rt02InMemoryTargetStore
{
    private readonly object _gate = new();

    public Dictionary<string, Rt02TestLearner> Learners { get; private set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, string> CurrentSourceHashes { get; } =
        new(StringComparer.Ordinal);

    public HashSet<string> AliasIdentities { get; } =
        new(StringComparer.Ordinal);

    public HashSet<string> ProfileConflictIdentities { get; } =
        new(StringComparer.Ordinal);

    public HashSet<string> InvalidParentIdentities { get; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, QlhvDirectRealtimeApplyMarker> Markers { get; private set; } =
        new(StringComparer.Ordinal);

    public List<QlhvDirectRealtimeManualReviewEvidence> ReviewEvidence { get; private set; } =
        [];

    public int OpenTransactionCount { get; set; }

    public int CommitCount { get; set; }

    public int RollbackCount { get; set; }

    public int QueryCount { get; set; }

    public TimeSpan LastTransactionDuration { get; set; }

    public string? LastLockName { get; set; }

    public bool CreateTargetBeforeInsert { get; set; }

    public bool ChangeTargetBeforeUpdate { get; set; }

    public bool FailUpdate { get; set; }

    public bool FailVerification { get; set; }

    public int ForcedUpdateRowCount { get; set; } = 1;

    public Exception? CommitFailure { get; set; }

    public (Dictionary<string, Rt02TestLearner> Learners,
        Dictionary<string, QlhvDirectRealtimeApplyMarker> Markers,
        List<QlhvDirectRealtimeManualReviewEvidence> Evidence) Snapshot()
    {
        lock (_gate)
        {
            return (
                Learners.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Clone(),
                    StringComparer.Ordinal),
                new Dictionary<string, QlhvDirectRealtimeApplyMarker>(
                    Markers,
                    StringComparer.Ordinal),
                [.. ReviewEvidence]);
        }
    }

    public void Commit(
        Dictionary<string, Rt02TestLearner> learners,
        Dictionary<string, QlhvDirectRealtimeApplyMarker> markers,
        List<QlhvDirectRealtimeManualReviewEvidence> evidence)
    {
        lock (_gate)
        {
            if (CommitFailure is not null)
            {
                var failure = CommitFailure;
                CommitFailure = null;
                throw failure;
            }

            Learners = learners;
            Markers = markers;
            ReviewEvidence = evidence;
            CommitCount++;
        }
    }
}

internal sealed class Rt02InMemoryTransactionFactory :
    IQlhvDirectRealtimeTargetTransactionFactory
{
    private readonly Rt02InMemoryTargetStore _store;

    public Rt02InMemoryTransactionFactory(Rt02InMemoryTargetStore store)
    {
        _store = store;
    }

    public Task<IQlhvDirectRealtimeTargetTransaction> OpenAsync(
        CancellationToken cancellationToken)
    {
        _store.OpenTransactionCount++;
        return Task.FromResult<IQlhvDirectRealtimeTargetTransaction>(
            new Rt02InMemoryTargetTransaction(_store));
    }

    public Task<QlhvDirectRealtimeApplyMarker?> FindCommittedMarkerAsync(
        string cycleId,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        _store.Markers.TryGetValue(cycleId, out var marker);
        return Task.FromResult(marker);
    }
}

internal sealed class Rt02InMemoryTargetTransaction :
    IQlhvDirectRealtimeTargetTransaction
{
    private readonly Stopwatch _transactionTimer = Stopwatch.StartNew();
    private readonly Rt02InMemoryTargetStore _store;
    private readonly Dictionary<string, Rt02TestLearner> _learners;
    private readonly Dictionary<string, QlhvDirectRealtimeApplyMarker> _markers;
    private readonly List<QlhvDirectRealtimeManualReviewEvidence> _evidence;
    private int _inserted;
    private int _updated;
    private int _retained;
    private bool _finished;

    public Rt02InMemoryTargetTransaction(Rt02InMemoryTargetStore store)
    {
        _store = store;
        (_learners, _markers, _evidence) = store.Snapshot();
    }

    public Task RevalidateIsolatedTargetIdentityAsync(
        QlhvDirectRealtimeIsolatedEnvironment environment,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        return Task.CompletedTask;
    }

    public Task AcquireSourceProfileLockAsync(
        string lockName,
        CancellationToken cancellationToken)
    {
        _store.LastLockName = lockName;
        _store.QueryCount++;
        return Task.CompletedTask;
    }

    public Task VerifyPlanFingerprintsAsync(
        QlhvDirectRealtimeApplyPlan plan,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        if (plan.MappingFingerprint.StartsWith(
                "DRIFT",
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "Injected mapping/schema drift.");
        }

        return Task.CompletedTask;
    }

    public Task InsertAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        if (_store.CreateTargetBeforeInsert)
        {
            _learners[operation.IdentityHmac] = new Rt02TestLearner
            {
                IdentityHmac = operation.IdentityHmac,
                SourceProfile = "CSDT_OTO",
                HoTen = "CONCURRENT",
                MappedHash = "CONCURRENT",
                QlhvOwnedHash = "QLHV-CONCURRENT",
            };
            _store.CreateTargetBeforeInsert = false;
        }

        if (_learners.ContainsKey(operation.IdentityHmac))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "Target identity exists, including an active or soft-deleted counterpart.");
        }

        if (_store.AliasIdentities.Contains(operation.IdentityHmac) ||
            _store.ProfileConflictIdentities.Contains(operation.IdentityHmac) ||
            _store.InvalidParentIdentities.Contains(operation.IdentityHmac))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "Alias, profile ownership or parent validation blocked insert.");
        }

        if (!_store.CurrentSourceHashes.TryGetValue(
                operation.IdentityHmac,
                out var currentSourceHash) ||
            !string.Equals(
                currentSourceHash,
                operation.SourceRowHash,
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.SourceChangedSinceShadow,
                "Source hash changed after staging.");
        }

        _learners.Add(
            operation.IdentityHmac,
            new Rt02TestLearner
            {
                IdentityHmac = operation.IdentityHmac,
                SourceProfile = "CSDT_OTO",
                HoTen = operation.DesiredHoTen ?? "SYNTHETIC INSERT",
                MappedHash = operation.SourceRowHash,
                QlhvOwnedHash = QlhvDirectRealtimeHash.Sha256("QLHV-DEFAULT"),
            });
        _inserted++;
        return Task.CompletedTask;
    }

    public Task UpdateSourceOwnedFieldsAsync(
        QlhvDirectRealtimeApplyOperation operation,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        if (_store.FailUpdate)
        {
            throw new InvalidOperationException("Injected update failure.");
        }

        if (!_learners.TryGetValue(operation.IdentityHmac, out var learner) ||
            !learner.Active ||
            learner.SoftDeleted)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "Update target identity is not one active row.");
        }

        if (_store.ChangeTargetBeforeUpdate)
        {
            learner.MappedHash = "CONCURRENT-TARGET-HASH";
            _store.ChangeTargetBeforeUpdate = false;
        }

        if (!string.Equals(
                learner.MappedHash,
                operation.StagedTargetMappedHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                learner.QlhvOwnedHash,
                operation.StagedQlhvOwnedHash,
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "Mapped or QLHV-owned target hash changed after shadow.");
        }

        if (!_store.CurrentSourceHashes.TryGetValue(
                operation.IdentityHmac,
                out var currentSourceHash) ||
            !string.Equals(
                currentSourceHash,
                operation.SourceRowHash,
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.SourceChangedSinceShadow,
                "Source hash changed after staging.");
        }

        if (_store.ForcedUpdateRowCount != 1)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "Update affected a row count other than one.");
        }

        learner.HoTen = operation.DesiredHoTen!;
        learner.MappedHash = operation.SourceRowHash;
        _updated++;
        return Task.CompletedTask;
    }

    public Task RetainAndRecordManualReviewAsync(
        QlhvDirectRealtimeManualReviewEvidence evidence,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        if (!_learners.TryGetValue(evidence.IdentityHmac, out var learner) ||
            !learner.Active ||
            learner.SoftDeleted ||
            evidence.TargetMutated ||
            !evidence.TargetRetainedActive)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.TargetChangedSinceShadow,
                "Target-only retention precondition failed.");
        }

        _evidence.Add(evidence);
        _retained++;
        return Task.CompletedTask;
    }

    public Task<QlhvDirectRealtimeTargetVerification> VerifyAsync(
        QlhvDirectRealtimeApplyPlan plan,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        if (_store.FailVerification)
        {
            throw new InvalidOperationException("Injected final verification failure.");
        }

        var qlhvHash = QlhvDirectRealtimeHash.Sha256(
            string.Join(
                "|",
                _learners.Values
                    .OrderBy(row => row.IdentityHmac, StringComparer.Ordinal)
                    .Select(row => row.QlhvOwnedHash)));
        return Task.FromResult(
            new QlhvDirectRealtimeTargetVerification(
                _inserted,
                _updated,
                _retained,
                qlhvHash));
    }

    public Task WriteApplyMarkerAsync(
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken)
    {
        _store.QueryCount++;
        if (_markers.TryGetValue(marker.CycleId, out var existing) &&
            !string.Equals(
                existing.MarkerHash,
                marker.MarkerHash,
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.CheckpointConflict,
                "Cycle marker already exists with different content.");
        }

        _markers[marker.CycleId] = marker;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        _store.Commit(_learners, _markers, _evidence);
        _transactionTimer.Stop();
        _store.LastTransactionDuration = _transactionTimer.Elapsed;
        _finished = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (!_finished)
        {
            _transactionTimer.Stop();
            _store.LastTransactionDuration = _transactionTimer.Elapsed;
            _store.RollbackCount++;
            _finished = true;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (!_finished)
        {
            _transactionTimer.Stop();
            _store.LastTransactionDuration = _transactionTimer.Elapsed;
            _store.RollbackCount++;
            _finished = true;
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class Rt02InMemoryCheckpointStore :
    IQlhvDirectRealtimeApplyCheckpointStore
{
    private readonly Dictionary<
        QlhvDirectRealtimeApplyCheckpointKey,
        QlhvDirectRealtimeApplyCheckpoint> _checkpoints = [];

    public IReadOnlyDictionary<
        QlhvDirectRealtimeApplyCheckpointKey,
        QlhvDirectRealtimeApplyCheckpoint> Checkpoints => _checkpoints;

    public int PublishCount { get; private set; }

    public Task<QlhvDirectRealtimeApplyCheckpoint?> ReadAsync(
        QlhvDirectRealtimeApplyCheckpointKey key,
        CancellationToken cancellationToken)
    {
        _checkpoints.TryGetValue(key, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task PublishAsync(
        QlhvDirectRealtimeApplyCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (_checkpoints.TryGetValue(checkpoint.Key, out var existing))
        {
            if (!string.Equals(
                    existing.CycleId,
                    checkpoint.CycleId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.MarkerHash,
                    checkpoint.MarkerHash,
                    StringComparison.Ordinal))
            {
                throw new QlhvDirectRealtimeSafetyException(
                    QlhvDirectRealtimeErrors.CheckpointConflict,
                    "Checkpoint publish content conflicts with the existing value.");
            }

            return Task.CompletedTask;
        }

        _checkpoints.Add(checkpoint.Key, checkpoint);
        PublishCount++;
        return Task.CompletedTask;
    }
}

internal sealed class Rt02CrashAfterCommitFaultInjector :
    IQlhvDirectRealtimeFaultInjector
{
    private bool _hasThrown;

    public Task AfterTargetCommitAsync(
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken)
    {
        if (!_hasThrown)
        {
            _hasThrown = true;
            throw new InvalidOperationException("Injected crash after target commit.");
        }

        return Task.CompletedTask;
    }
}
