namespace QLHV.Application.Sync.QlhvDirectRealtime;

/// <summary>
/// Orchestrates an immutable, caller-owned isolated target transaction.
/// It has no source reader and therefore cannot silently rebuild or reread a
/// validated shadow plan.
/// </summary>
public sealed class QlhvDirectRealtimeApplyCycle
{
    private static readonly IReadOnlySet<string> SourceOwnedUpdateColumns =
        new HashSet<string>(["HoTen"], StringComparer.Ordinal);

    private readonly QlhvDirectRealtimeOptions _options;
    private readonly IQlhvDirectRealtimeTargetTransactionFactory _transactionFactory;
    private readonly IQlhvDirectRealtimeApplyCheckpointStore _checkpointStore;
    private readonly IQlhvDirectRealtimeFaultInjector _faultInjector;
    private readonly TimeProvider _timeProvider;

    public QlhvDirectRealtimeApplyCycle(
        QlhvDirectRealtimeOptions options,
        IQlhvDirectRealtimeTargetTransactionFactory transactionFactory,
        IQlhvDirectRealtimeApplyCheckpointStore checkpointStore,
        IQlhvDirectRealtimeFaultInjector? faultInjector = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transactionFactory = transactionFactory ??
            throw new ArgumentNullException(nameof(transactionFactory));
        _checkpointStore = checkpointStore ??
            throw new ArgumentNullException(nameof(checkpointStore));
        _faultInjector = faultInjector ?? new QlhvDirectRealtimeNoFaultInjector();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<QlhvDirectRealtimeApplyResult> ExecuteAsync(
        QlhvDirectRealtimeApplyPlan plan,
        QlhvDirectRealtimeIsolatedEnvironment environment,
        IReadOnlyCollection<QlhvDirectRealtimeDatabaseIdentity> identities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(identities);

        EnsureExplicitIsolatedWriteFlags();
        QlhvDirectRealtimeIsolatedEnvironmentValidator.Validate(
            environment,
            identities,
            _timeProvider.GetUtcNow().UtcDateTime);
        ValidatePlan(plan, environment);

        var checkpointKey = new QlhvDirectRealtimeApplyCheckpointKey(
            plan.SourceProfile,
            QlhvDirectRealtimeModes.DirectRealtimeApply,
            plan.MappingFingerprint,
            plan.EnvironmentId);
        var existingCheckpoint = await _checkpointStore.ReadAsync(
            checkpointKey,
            cancellationToken);
        if (existingCheckpoint is not null)
        {
            if (!string.Equals(
                    existingCheckpoint.CycleId,
                    plan.CycleId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existingCheckpoint.PlanHash,
                    plan.PlanHash,
                    StringComparison.Ordinal))
            {
                throw new QlhvDirectRealtimeSafetyException(
                    QlhvDirectRealtimeErrors.CheckpointConflict,
                    "The isolated checkpoint key already contains different content.");
            }

            return new QlhvDirectRealtimeApplyResult(
                plan.CycleId,
                "SUCCEEDED_IDEMPOTENT_REPLAY",
                0,
                0,
                0,
                true,
                true,
                true,
                existingCheckpoint.MarkerHash);
        }

        var committedMarker = await _transactionFactory.FindCommittedMarkerAsync(
            plan.CycleId,
            cancellationToken);
        if (committedMarker is not null)
        {
            EnsureMarkerMatchesPlan(committedMarker, plan);
            await PublishCheckpointAsync(
                checkpointKey,
                plan,
                committedMarker,
                cancellationToken);
            return ResultFromMarker(
                committedMarker,
                checkpointPublished: true,
                recovered: true);
        }

        await using var transaction =
            await _transactionFactory.OpenAsync(cancellationToken);
        var transactionCommitted = false;
        try
        {
            await transaction.RevalidateIsolatedTargetIdentityAsync(
                environment,
                cancellationToken);
            await transaction.AcquireSourceProfileLockAsync(
                BuildLockName(plan),
                cancellationToken);
            await transaction.VerifyPlanFingerprintsAsync(
                plan,
                cancellationToken);

            foreach (var operation in plan.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (operation.Kind)
                {
                    case QlhvDirectRealtimeApplyOperationKind.Insert:
                        ValidateInsert(operation);
                        await transaction.InsertAsync(operation, cancellationToken);
                        break;
                    case QlhvDirectRealtimeApplyOperationKind.Update:
                        ValidateUpdate(operation);
                        await transaction.UpdateSourceOwnedFieldsAsync(
                            operation,
                            cancellationToken);
                        break;
                    case QlhvDirectRealtimeApplyOperationKind.RetainForManualReview:
                        ValidateRetained(operation);
                        await transaction.RetainAndRecordManualReviewAsync(
                            new QlhvDirectRealtimeManualReviewEvidence(
                                plan.CycleId,
                                operation.OperationId,
                                operation.IdentityHmac,
                                operation.Disposition,
                                plan.DispositionHash,
                                TargetRetainedActive: true,
                                TargetMutated: false),
                            cancellationToken);
                        break;
                    default:
                        throw new QlhvDirectRealtimeSafetyException(
                            QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                            "Unknown apply operation kind.");
                }
            }

            var verification = await transaction.VerifyAsync(
                plan,
                cancellationToken);
            var marker = new QlhvDirectRealtimeApplyMarker(
                plan.CycleId,
                plan.PlanHash,
                plan.DispositionHash,
                verification.InsertedRows,
                verification.UpdatedRows,
                verification.RetainedRows,
                verification.PreservedQlhvOwnedHash,
                _timeProvider.GetUtcNow().UtcDateTime);
            await transaction.WriteApplyMarkerAsync(marker, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            transactionCommitted = true;

            await _faultInjector.AfterTargetCommitAsync(marker, cancellationToken);
            await PublishCheckpointAsync(
                checkpointKey,
                plan,
                marker,
                cancellationToken);

            return ResultFromMarker(
                marker,
                checkpointPublished: true,
                recovered: false);
        }
        catch
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private void EnsureExplicitIsolatedWriteFlags()
    {
        if (!_options.EnableQlhvDirectRealtime ||
            !_options.EnableQlhvDirectRealtimeWrites ||
            !_options.EnableQlhvDirectRealtimeIsolatedApply ||
            _options.EnableQlhvDirectRealtimeDeletes)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.FeatureDisabled,
                "Master, write and isolated-apply flags must be explicitly enabled " +
                "and delete must remain disabled.");
        }
    }

    private static void ValidatePlan(
        QlhvDirectRealtimeApplyPlan plan,
        QlhvDirectRealtimeIsolatedEnvironment environment)
    {
        if (!string.Equals(
                plan.EnvironmentId,
                environment.EnvironmentId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plan.CycleId) ||
            string.IsNullOrWhiteSpace(plan.SourceProfile) ||
            string.IsNullOrWhiteSpace(plan.MappingFingerprint) ||
            string.IsNullOrWhiteSpace(plan.SourceSchemaFingerprint) ||
            string.IsNullOrWhiteSpace(plan.TargetSchemaFingerprint) ||
            string.IsNullOrWhiteSpace(plan.StageHash) ||
            string.IsNullOrWhiteSpace(plan.ComparisonHash) ||
            string.IsNullOrWhiteSpace(plan.DispositionHash) ||
            string.IsNullOrWhiteSpace(plan.IdentityNormalizationVersion) ||
            plan.SourceWatermark < 0)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "The immutable plan identity/fingerprint contract is incomplete.");
        }

        if (plan.Operations.Select(operation => operation.OperationId)
            .Distinct(StringComparer.Ordinal).Count() != plan.Operations.Count)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "Operation IDs must be unique inside an apply cycle.");
        }
    }

    private static void ValidateInsert(
        QlhvDirectRealtimeApplyOperation operation)
    {
        if (!string.Equals(
                operation.Disposition,
                QlhvDirectRealtimeDispositions.WouldInsertSafeAfterApproval,
                StringComparison.Ordinal) ||
            operation.RequestedColumns.Count != 0 ||
            string.IsNullOrWhiteSpace(operation.SourceRowHash))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "Insert disposition or staged source hash is not approved.");
        }
    }

    private static void ValidateUpdate(
        QlhvDirectRealtimeApplyOperation operation)
    {
        if (!string.Equals(
                operation.Disposition,
                QlhvDirectRealtimeDispositions.StaleImportedValue,
                StringComparison.Ordinal) ||
            operation.RequestedColumns.Count != 1 ||
            !operation.RequestedColumns.All(SourceOwnedUpdateColumns.Contains) ||
            string.IsNullOrWhiteSpace(operation.DesiredHoTen) ||
            string.IsNullOrWhiteSpace(operation.SourceRowHash) ||
            string.IsNullOrWhiteSpace(operation.StagedTargetMappedHash) ||
            string.IsNullOrWhiteSpace(operation.StagedQlhvOwnedHash))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "Only the explicitly source-owned HoTen update is allowed.");
        }
    }

    private static void ValidateRetained(
        QlhvDirectRealtimeApplyOperation operation)
    {
        if (!string.Equals(
                operation.Disposition,
                QlhvDirectRealtimeDispositions.ManualReviewRequired,
                StringComparison.Ordinal) ||
            operation.RequestedColumns.Count != 0)
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "Target-only rows may only produce retained manual-review evidence.");
        }
    }

    private async Task PublishCheckpointAsync(
        QlhvDirectRealtimeApplyCheckpointKey key,
        QlhvDirectRealtimeApplyPlan plan,
        QlhvDirectRealtimeApplyMarker marker,
        CancellationToken cancellationToken)
    {
        EnsureMarkerMatchesPlan(marker, plan);
        await _checkpointStore.PublishAsync(
            new QlhvDirectRealtimeApplyCheckpoint(
                key,
                plan.CycleId,
                plan.PlanHash,
                marker.MarkerHash,
                plan.SourceWatermark,
                _timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }

    private static void EnsureMarkerMatchesPlan(
        QlhvDirectRealtimeApplyMarker marker,
        QlhvDirectRealtimeApplyPlan plan)
    {
        if (!string.Equals(marker.CycleId, plan.CycleId, StringComparison.Ordinal) ||
            !string.Equals(marker.PlanHash, plan.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(
                marker.DispositionHash,
                plan.DispositionHash,
                StringComparison.Ordinal))
        {
            throw new QlhvDirectRealtimeSafetyException(
                QlhvDirectRealtimeErrors.PlanFingerprintConflict,
                "The durable target marker does not match the immutable plan.");
        }
    }

    private static string BuildLockName(QlhvDirectRealtimeApplyPlan plan)
        => $"QLHV:DIRECT_REALTIME:{plan.EnvironmentId}:{plan.SourceProfile}";

    private static QlhvDirectRealtimeApplyResult ResultFromMarker(
        QlhvDirectRealtimeApplyMarker marker,
        bool checkpointPublished,
        bool recovered)
        => new(
            marker.CycleId,
            recovered ? "SUCCEEDED_RECOVERED" : "SUCCEEDED",
            marker.InsertedRows,
            marker.UpdatedRows,
            marker.RetainedRows,
            TransactionCommitted: true,
            CheckpointPublished: checkpointPublished,
            RecoveredFromDurableMarker: recovered,
            marker.MarkerHash);
}
