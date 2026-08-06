using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace QLHV.Application.Sync.Realtime.ControlPlane;

public sealed class CsdtAtomicMappedTableCycleCoordinator
{
    private readonly ICsdtSourceCycleReader _source;
    private readonly ICsdtAtomicCycleJournal _journal;
    private readonly ICsdtTargetCycleApplier _target;
    private readonly ICsdtGlobalCheckpointStore _checkpoint;
    private readonly CsdtAtomicMappedTableCycleValidator _validator;
    private readonly CsdtRealtimeSyncOptions _options;

    public CsdtAtomicMappedTableCycleCoordinator(
        ICsdtSourceCycleReader source,
        ICsdtAtomicCycleJournal journal,
        ICsdtTargetCycleApplier target,
        ICsdtGlobalCheckpointStore checkpoint,
        CsdtAtomicMappedTableCycleValidator validator,
        IOptions<CsdtRealtimeSyncOptions> options)
    {
        _source = source;
        _journal = journal;
        _target = target;
        _checkpoint = checkpoint;
        _validator = validator;
        _options = options.Value;
    }

    public async Task<CsdtAtomicCycleResult> ExecuteAsync(
        CsdtAtomicCycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.UseAtomicMappedTableCycle)
        {
            return Failed(
                request.CycleId,
                CsdtAtomicCycleErrorCodes.FeatureDisabled);
        }

        try
        {
            ValidateRequest(request);
        }
        catch (CsdtAtomicCycleException exception)
        {
            return Failed(request.CycleId, exception.ErrorCode);
        }

        var existing = await _journal.ReadMarkerAsync(
            request.CycleId,
            cancellationToken);
        if (existing is not null &&
            existing.Status is
                SyncCycleStatus.TargetCommitted or
                SyncCycleStatus.CheckpointPublished or
                SyncCycleStatus.Complete)
        {
            return await RecoverCommittedAsync(request, existing, cancellationToken);
        }

        if (existing?.Status is SyncCycleStatus.Failed or SyncCycleStatus.Conflict)
        {
            return new CsdtAtomicCycleResult(
                request.CycleId,
                existing.Status == SyncCycleStatus.Conflict
                    ? CsdtAtomicCycleOutcome.Conflict
                    : CsdtAtomicCycleOutcome.Failed,
                existing.Status,
                null,
                existing.Status == SyncCycleStatus.Conflict
                    ? "CYCLE_CONFLICT"
                    : "CYCLE_FAILED");
        }

        if (existing is not null)
        {
            return new CsdtAtomicCycleResult(
                request.CycleId,
                CsdtAtomicCycleOutcome.RebuildRequired,
                existing.Status,
                null,
                null);
        }

        var preflight = await _source.PreflightAsync(request, cancellationToken);
        if (!preflight.IsReady)
        {
            return Failed(request.CycleId, preflight.ErrorCode);
        }

        var cycleCreated = false;
        try
        {
            await using var snapshot = await _source.OpenSnapshotAsync(
                request,
                preflight,
                cancellationToken);
            await _journal.CreatePreparingAsync(
                request,
                snapshot.Watermark,
                cancellationToken);
            cycleCreated = true;

            var staged = await snapshot.StageCoreAsync(cancellationToken);
            _validator.ValidateStaged(request, preflight, staged);
            await _journal.MarkStagedAsync(staged, cancellationToken);

            _validator.ValidateBeforeTarget(request, staged);
            await _journal.MarkValidatedAsync(
                request.CycleId,
                cancellationToken);

            var committed = await _target.ApplyAsync(staged, cancellationToken);
            VerifyMarker(staged, committed);

            var reread = await _journal.ReadMarkerAsync(
                request.CycleId,
                cancellationToken);
            if (reread is null)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
            }

            VerifyMarker(staged, reread);
            VerifyCommittedReplay(committed, reread);
            return await PublishAndCompleteAsync(reread, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CsdtAtomicCycleException exception)
        {
            if (cycleCreated)
            {
                await TryMarkTerminalAsync(
                    request.CycleId,
                    IsConflict(exception.ErrorCode)
                        ? SyncCycleStatus.Conflict
                        : SyncCycleStatus.Failed,
                    ToPersistedErrorCode(exception.ErrorCode));
            }

            return new CsdtAtomicCycleResult(
                request.CycleId,
                IsConflict(exception.ErrorCode)
                    ? CsdtAtomicCycleOutcome.Conflict
                    : CsdtAtomicCycleOutcome.Failed,
                IsConflict(exception.ErrorCode)
                    ? SyncCycleStatus.Conflict
                    : SyncCycleStatus.Failed,
                null,
                exception.ErrorCode);
        }
        catch
        {
            if (cycleCreated)
            {
                await TryMarkTerminalAsync(
                    request.CycleId,
                    SyncCycleStatus.Failed,
                    "CYCLE_FAILED");
            }

            return Failed(request.CycleId, "CYCLE_FAILED");
        }
    }

    public async Task<CsdtAtomicCycleResult> RecoverAsync(
        CsdtAtomicCycleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.UseAtomicMappedTableCycle)
        {
            return Failed(
                request.CycleId,
                CsdtAtomicCycleErrorCodes.FeatureDisabled);
        }

        var marker = await _journal.ReadMarkerAsync(
            request.CycleId,
            cancellationToken);
        if (marker is null ||
            marker.Status is
                SyncCycleStatus.Preparing or
                SyncCycleStatus.Staged or
                SyncCycleStatus.Validated)
        {
            return new CsdtAtomicCycleResult(
                request.CycleId,
                CsdtAtomicCycleOutcome.RebuildRequired,
                marker?.Status,
                null,
                null);
        }

        if (!MarkerIdentityMatches(request, marker))
        {
            await TryMarkTerminalAsync(
                request.CycleId,
                SyncCycleStatus.Conflict,
                "MAPPING_FINGERPRINT_MISMATCH");
            return new CsdtAtomicCycleResult(
                request.CycleId,
                CsdtAtomicCycleOutcome.Conflict,
                SyncCycleStatus.Conflict,
                null,
                CsdtAtomicCycleErrorCodes.FingerprintMismatch);
        }

        if (marker.Status is SyncCycleStatus.Failed or SyncCycleStatus.Conflict)
        {
            return new CsdtAtomicCycleResult(
                request.CycleId,
                marker.Status == SyncCycleStatus.Conflict
                    ? CsdtAtomicCycleOutcome.Conflict
                    : CsdtAtomicCycleOutcome.Failed,
                marker.Status,
                null,
                marker.Status == SyncCycleStatus.Conflict
                    ? "CYCLE_CONFLICT"
                    : "CYCLE_FAILED");
        }

        return await RecoverCommittedAsync(request, marker, cancellationToken);
    }

    public async Task<CsdtAtomicCycleResult> ResumeStagedAsync(
        CsdtAtomicCycleRequest request,
        CsdtStagedCycle staged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(staged);
        if (!_options.UseAtomicMappedTableCycle)
        {
            return Failed(
                request.CycleId,
                CsdtAtomicCycleErrorCodes.FeatureDisabled);
        }

        ValidateRequest(request);
        var marker = await _journal.ReadMarkerAsync(
            request.CycleId,
            cancellationToken);
        if (marker is null)
        {
            return new CsdtAtomicCycleResult(
                request.CycleId,
                CsdtAtomicCycleOutcome.RebuildRequired,
                null,
                null,
                null);
        }

        if (marker.Status is
            SyncCycleStatus.TargetCommitted or
            SyncCycleStatus.CheckpointPublished or
            SyncCycleStatus.Complete)
        {
            return await RecoverCommittedAsync(request, marker, cancellationToken);
        }

        try
        {
            if (marker.Status is not (
                    SyncCycleStatus.Staged or
                    SyncCycleStatus.Validated) ||
                marker.EndSourceVersion != staged.EndSourceVersion ||
                marker.StagedKeySetHash?.Equals(staged.StagedKeySetHash) != true)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.FingerprintMismatch);
            }

            _validator.ValidateStaged(
                request,
                new CsdtSourceCapabilityResult(
                    CsdtSourceCapabilityStatus.Ready,
                    staged.EndSourceVersion,
                    CsdtAtomicCoreDomains.ApplyOrder.ToDictionary(
                        name => name,
                        _ => staged.StartSourceVersion,
                        StringComparer.Ordinal),
                    staged.SourceSchemaFingerprint),
                staged);
            _validator.ValidateBeforeTarget(request, staged);
            if (marker.Status == SyncCycleStatus.Staged)
            {
                await _journal.MarkValidatedAsync(
                    request.CycleId,
                    cancellationToken);
            }

            var committed = await _target.ApplyAsync(staged, cancellationToken);
            VerifyMarker(staged, committed);
            var reread = await _journal.ReadMarkerAsync(
                request.CycleId,
                cancellationToken);
            if (reread is null)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
            }

            VerifyMarker(staged, reread);
            VerifyCommittedReplay(committed, reread);
            return await PublishAndCompleteAsync(reread, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CsdtAtomicCycleException exception)
        {
            await TryMarkTerminalAsync(
                request.CycleId,
                IsConflict(exception.ErrorCode)
                    ? SyncCycleStatus.Conflict
                    : SyncCycleStatus.Failed,
                ToPersistedErrorCode(exception.ErrorCode));
            return new CsdtAtomicCycleResult(
                request.CycleId,
                IsConflict(exception.ErrorCode)
                    ? CsdtAtomicCycleOutcome.Conflict
                    : CsdtAtomicCycleOutcome.Failed,
                IsConflict(exception.ErrorCode)
                    ? SyncCycleStatus.Conflict
                    : SyncCycleStatus.Failed,
                null,
                exception.ErrorCode);
        }
    }

    private async Task<CsdtAtomicCycleResult> RecoverCommittedAsync(
        CsdtAtomicCycleRequest request,
        CsdtTargetCycleCommitMarker marker,
        CancellationToken cancellationToken)
    {
        if (!MarkerIdentityMatches(request, marker))
        {
            await TryMarkTerminalAsync(
                request.CycleId,
                SyncCycleStatus.Conflict,
                "MAPPING_FINGERPRINT_MISMATCH");
            return new CsdtAtomicCycleResult(
                request.CycleId,
                CsdtAtomicCycleOutcome.Conflict,
                SyncCycleStatus.Conflict,
                null,
                CsdtAtomicCycleErrorCodes.FingerprintMismatch);
        }

        ValidateCompleteMarker(marker);
        if (marker.Status == SyncCycleStatus.Complete)
        {
            var checkpoint = await _checkpoint.ReadAsync(
                marker.SourceProfile,
                marker.TargetProfile,
                marker.StreamCode,
                cancellationToken);
            VerifyCheckpoint(marker, checkpoint);
            if (!await _checkpoint.VerifyAsync(checkpoint!, cancellationToken))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.CheckpointMismatch);
            }

            return Complete(marker);
        }

        if (marker.Status == SyncCycleStatus.CheckpointPublished)
        {
            var checkpoint = await _checkpoint.ReadAsync(
                marker.SourceProfile,
                marker.TargetProfile,
                marker.StreamCode,
                cancellationToken);
            VerifyCheckpoint(marker, checkpoint);
            if (!await _checkpoint.VerifyAsync(checkpoint!, cancellationToken))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.CheckpointMismatch);
            }

            await _journal.MarkCompleteAsync(marker.CycleId, cancellationToken);
            return Complete(marker);
        }

        if (marker.Status != SyncCycleStatus.TargetCommitted)
        {
            return new CsdtAtomicCycleResult(
                marker.CycleId,
                CsdtAtomicCycleOutcome.RebuildRequired,
                marker.Status,
                null,
                null);
        }

        return await PublishAndCompleteAsync(marker, cancellationToken);
    }

    private async Task<CsdtAtomicCycleResult> PublishAndCompleteAsync(
        CsdtTargetCycleCommitMarker marker,
        CancellationToken cancellationToken)
    {
        ValidateCompleteMarker(marker);
        var checkpoint = ToCheckpoint(marker);
        await _checkpoint.PublishAsync(checkpoint, cancellationToken);
        var readback = await _checkpoint.ReadAsync(
            marker.SourceProfile,
            marker.TargetProfile,
            marker.StreamCode,
            cancellationToken);
        VerifyCheckpoint(marker, readback);
        if (!await _checkpoint.VerifyAsync(readback!, cancellationToken))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.CheckpointMismatch);
        }

        await _journal.MarkCheckpointPublishedAsync(
            marker.CycleId,
            cancellationToken);
        await _journal.MarkCompleteAsync(marker.CycleId, cancellationToken);
        return Complete(marker);
    }

    private static void ValidateRequest(CsdtAtomicCycleRequest request)
    {
        if (request.CycleId == Guid.Empty || request.StartSourceVersion < 0)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ValidationFailed);
        }

        CsdtAtomicCoreDomains.RequireExactScope(request.RequestedDomains);
        if (!CsdtRealtimeStreamCatalog.TryResolveAllowedRoute(
                request.Route.StreamCode,
                request.Route.SourceProfileCode,
                request.Route.TargetProfileCode,
                out var allowed) ||
            allowed != request.Route ||
            !request.RouteFingerprint.Equals(
                CsdtAtomicRouteFingerprint.Compute(request.Route)))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ProfileMismatch);
        }
    }

    private static bool MarkerIdentityMatches(
        CsdtAtomicCycleRequest request,
        CsdtTargetCycleCommitMarker marker)
        => marker.CycleId == request.CycleId &&
           string.Equals(
               marker.SourceProfile,
               request.Route.SourceProfileCode,
               StringComparison.Ordinal) &&
           string.Equals(
               marker.TargetProfile,
               request.Route.TargetProfileCode,
               StringComparison.Ordinal) &&
           string.Equals(
               marker.StreamCode,
               request.Route.StreamCode,
               StringComparison.Ordinal) &&
           string.Equals(marker.MaCsdt, request.Route.MaCSDT, StringComparison.Ordinal) &&
           marker.StartSourceVersion == request.StartSourceVersion &&
           marker.MappingFingerprint.Equals(request.MappingFingerprint) &&
           marker.RouteFingerprint.Equals(request.RouteFingerprint) &&
           marker.SourceSchemaFingerprint?.Equals(
               request.SourceSchemaFingerprint) == true &&
           marker.TargetSchemaFingerprint?.Equals(
               request.TargetSchemaFingerprint) == true;

    public static void VerifyMarker(
        CsdtStagedCycle staged,
        CsdtTargetCycleCommitMarker marker)
    {
        if (marker.CycleId != staged.CycleId ||
            marker.Status != SyncCycleStatus.TargetCommitted ||
            marker.StartSourceVersion != staged.StartSourceVersion ||
            marker.EndSourceVersion != staged.EndSourceVersion ||
            marker.EnabledDomainCount != CsdtAtomicCoreDomains.ApplyOrder.Count ||
            !string.Equals(marker.SourceProfile, staged.SourceProfile, StringComparison.Ordinal) ||
            !string.Equals(marker.TargetProfile, staged.TargetProfile, StringComparison.Ordinal) ||
            !string.Equals(marker.StreamCode, staged.StreamCode, StringComparison.Ordinal) ||
            !string.Equals(marker.MaCsdt, staged.MaCsdt, StringComparison.Ordinal) ||
            !marker.MappingFingerprint.Equals(staged.MappingFingerprint) ||
            !marker.RouteFingerprint.Equals(staged.RouteFingerprint) ||
            marker.SourceSchemaFingerprint?.Equals(
                staged.SourceSchemaFingerprint) != true ||
            marker.TargetSchemaFingerprint?.Equals(
                staged.TargetSchemaFingerprint) != true ||
            marker.StagedKeySetHash is null ||
            !marker.StagedKeySetHash.Equals(staged.StagedKeySetHash))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
        }

        ValidateCompleteMarker(marker);
        for (var index = 0; index < staged.Domains.Count; index++)
        {
            var source = staged.Domains[index];
            var result = marker.Domains[index];
            if (!string.Equals(source.DomainName, result.DomainName, StringComparison.Ordinal) ||
                source.SourceRowCount != result.SourceRowCount ||
                !source.SourceKeySetHash.Equals(result.SourceKeySetHash))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.DomainResultHashMismatch);
            }
        }
    }

    public static void ValidateCompleteMarker(CsdtTargetCycleCommitMarker marker)
    {
        if (marker.Status is not (
                SyncCycleStatus.TargetCommitted or
                SyncCycleStatus.CheckpointPublished or
                SyncCycleStatus.Complete) ||
            marker.StagedKeySetHash is null ||
            marker.EnabledDomainCount != CsdtAtomicCoreDomains.ApplyOrder.Count ||
            marker.Domains.Count != CsdtAtomicCoreDomains.ApplyOrder.Count)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
        }

        if (marker.SourceSchemaFingerprint is null ||
            marker.TargetSchemaFingerprint is null)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
        }

        for (var index = 0; index < CsdtAtomicCoreDomains.ApplyOrder.Count; index++)
        {
            var result = marker.Domains[index];
            if (!string.Equals(
                    result.DomainName,
                    CsdtAtomicCoreDomains.ApplyOrder[index],
                    StringComparison.Ordinal) ||
                result.SourceRowCount < 0 ||
                result.InsertCount < 0 ||
                result.UpdateCount < 0 ||
                result.SkippedCount < 0)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.DomainResultHashMismatch);
            }
        }
    }

    public static void VerifyCommittedReplay(
        CsdtTargetCycleCommitMarker committed,
        CsdtTargetCycleCommitMarker reread)
    {
        ValidateCompleteMarker(committed);
        ValidateCompleteMarker(reread);
        if (committed.CycleId != reread.CycleId ||
            committed.EndSourceVersion != reread.EndSourceVersion ||
            committed.SourceSchemaFingerprint?.Equals(
                reread.SourceSchemaFingerprint) != true ||
            committed.TargetSchemaFingerprint?.Equals(
                reread.TargetSchemaFingerprint) != true ||
            committed.Domains.Count != reread.Domains.Count)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified);
        }

        for (var index = 0; index < committed.Domains.Count; index++)
        {
            var left = committed.Domains[index];
            var right = reread.Domains[index];
            if (!string.Equals(left.DomainName, right.DomainName, StringComparison.Ordinal) ||
                !left.SourceKeySetHash.Equals(right.SourceKeySetHash) ||
                !left.ResultHash.Equals(right.ResultHash))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.DomainResultHashMismatch);
            }
        }
    }

    private static CsdtGlobalCheckpoint ToCheckpoint(
        CsdtTargetCycleCommitMarker marker)
        => new(
            marker.CycleId,
            marker.SourceProfile,
            marker.TargetProfile,
            marker.StreamCode,
            marker.EndSourceVersion,
            marker.MappingFingerprint,
            marker.RouteFingerprint,
            marker.StagedKeySetHash ??
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified),
            marker.SourceSchemaFingerprint ??
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified),
            marker.TargetSchemaFingerprint ??
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.TargetCommitNotVerified));

    private static void VerifyCheckpoint(
        CsdtTargetCycleCommitMarker marker,
        CsdtGlobalCheckpoint? checkpoint)
    {
        if (checkpoint is null ||
            checkpoint.CycleId != marker.CycleId ||
            checkpoint.SourceWatermark != marker.EndSourceVersion ||
            !string.Equals(
                checkpoint.SourceProfile,
                marker.SourceProfile,
                StringComparison.Ordinal) ||
            !string.Equals(
                checkpoint.TargetProfile,
                marker.TargetProfile,
                StringComparison.Ordinal) ||
            !string.Equals(
                checkpoint.StreamCode,
                marker.StreamCode,
                StringComparison.Ordinal) ||
            !checkpoint.MappingFingerprint.Equals(marker.MappingFingerprint) ||
            !checkpoint.RouteFingerprint.Equals(marker.RouteFingerprint) ||
            checkpoint.SourceSchemaFingerprint?.Equals(
                marker.SourceSchemaFingerprint) != true ||
            checkpoint.TargetSchemaFingerprint?.Equals(
                marker.TargetSchemaFingerprint) != true ||
            checkpoint.Status != CsdtCheckpointStatus.Active ||
            marker.StagedKeySetHash is null ||
            !checkpoint.StagedKeySetHash.Equals(marker.StagedKeySetHash))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.CheckpointMismatch);
        }
    }

    private async Task TryMarkTerminalAsync(
        Guid cycleId,
        SyncCycleStatus status,
        string errorCode)
    {
        try
        {
            var marker = await _journal.ReadMarkerAsync(
                cycleId,
                CancellationToken.None);
            if (marker?.Status is
                SyncCycleStatus.TargetCommitted or
                SyncCycleStatus.CheckpointPublished or
                SyncCycleStatus.Complete)
            {
                return;
            }

            await _journal.MarkFailedOrConflictAsync(
                cycleId,
                status,
                errorCode,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the original failure. A durable non-terminal state is
            // recovered without checkpoint advancement.
        }
    }

    private static bool IsConflict(string errorCode)
        => errorCode is
            CsdtAtomicCycleErrorCodes.FingerprintMismatch or
            CsdtAtomicCycleErrorCodes.ProfileMismatch or
            CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch or
            CsdtAtomicCycleErrorCodes.DomainResultHashMismatch or
            CsdtAtomicCycleErrorCodes.CheckpointMismatch or
            CsdtAtomicCycleErrorCodes.CheckpointConflict or
            CsdtAtomicCycleErrorCodes.CheckpointStale or
            CsdtAtomicCycleErrorCodes.CoverageIncomplete;

    private static string ToPersistedErrorCode(string errorCode)
        => errorCode switch
        {
            CsdtAtomicCycleErrorCodes.DeleteExecutionNotEnabled =>
                CsdtAtomicCycleErrorCodes.DeleteExecutionNotEnabled,
            CsdtAtomicCycleErrorCodes.TargetLockTimeout =>
                CsdtAtomicCycleErrorCodes.TargetLockTimeout,
            CsdtAtomicCycleErrorCodes.FingerprintMismatch =>
                "MAPPING_FINGERPRINT_MISMATCH",
            CsdtAtomicCycleErrorCodes.DomainResultHashMismatch or
            CsdtAtomicCycleErrorCodes.TargetCommitNotVerified or
            CsdtAtomicCycleErrorCodes.CheckpointMismatch =>
                "TARGET_COMMIT_NOT_VERIFIED",
            _ => "CYCLE_FAILED",
        };

    private static CsdtAtomicCycleResult Complete(
        CsdtTargetCycleCommitMarker marker)
        => new(
            marker.CycleId,
            CsdtAtomicCycleOutcome.Complete,
            SyncCycleStatus.Complete,
            marker.EndSourceVersion,
            null);

    private static CsdtAtomicCycleResult Failed(Guid cycleId, string errorCode)
        => new(
            cycleId,
            CsdtAtomicCycleOutcome.Failed,
            SyncCycleStatus.Failed,
            null,
            errorCode);
}

public sealed class CsdtAtomicMappedTableCycleValidator
{
    private static readonly IReadOnlySet<string> KnownTrainingStates =
        new HashSet<string>(
            ["01", "02", "03", "04", "05", "06", "07", "09", "10"],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> KnownDownstreamStates =
        new HashSet<string>(
            ["00", "11", "12", "13", "14", "16", "17", "18", "19", "90"],
            StringComparer.Ordinal);

    public void ValidateStaged(
        CsdtAtomicCycleRequest request,
        CsdtSourceCapabilityResult preflight,
        CsdtStagedCycle staged)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(staged);
        if (!preflight.IsReady ||
            staged.CycleId != request.CycleId ||
            staged.StartSourceVersion != request.StartSourceVersion ||
            staged.EndSourceVersion < staged.StartSourceVersion ||
            !string.Equals(
                staged.SourceProfile,
                request.Route.SourceProfileCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                staged.TargetProfile,
                request.Route.TargetProfileCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                staged.StreamCode,
                request.Route.StreamCode,
                StringComparison.Ordinal) ||
            !string.Equals(staged.MaCsdt, request.Route.MaCSDT, StringComparison.Ordinal) ||
            !staged.MappingFingerprint.Equals(request.MappingFingerprint) ||
            !staged.RouteFingerprint.Equals(request.RouteFingerprint) ||
            !staged.SourceSchemaFingerprint.Equals(request.SourceSchemaFingerprint) ||
            !staged.TargetSchemaFingerprint.Equals(request.TargetSchemaFingerprint) ||
            staged.KeySchemaVersion != 1 ||
            !string.Equals(
                staged.TargetEqualityProofId,
                TargetEqualityProof.ProofId,
                StringComparison.Ordinal))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.ValidationFailed);
        }

        CsdtAtomicCoreDomains.RequireExactScope(
            staged.Domains.Select(domain => domain.DomainName));
        if (!staged.StagedKeySetHash.Equals(
                CsdtAtomicStageFactory.ComputeCycleKeySetHash(staged.Domains)))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
        }

        foreach (var domain in staged.Domains)
        {
            if (domain.UnknownColumns.Count != 0)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.UnknownColumn);
            }

            var keys = domain.Rows.Select(row => row.CopyCanonicalKey()).ToArray();
            if (!domain.SourceKeySetHash.Equals(
                    CsdtAtomicStageFactory.ComputeKeySetHash(domain.CompleteKeys)) ||
                !domain.StageResultHash.Equals(
                    CsdtAtomicStageFactory.ComputeDomainResultHash(
                        domain.DomainName,
                        domain.OperationMode,
                        domain.Rows,
                        domain.Changes,
                        domain.CompleteKeys)) ||
                domain.OperationMode == CsdtAtomicOperationMode.FullSnapshot &&
                keys.Length != domain.CompleteKeys.Count)
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
            }

            EnsureNoTargetEqualityAliases(domain);
        }
    }

    public void ValidateBeforeTarget(
        CsdtAtomicCycleRequest request,
        CsdtStagedCycle staged)
    {
        if (staged.DeleteCount != 0)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.DeleteExecutionNotEnabled);
        }

        ValidateTrainingStates(staged);
        ValidateParents(request.Route.MaCSDT, staged);
    }

    private static void EnsureNoTargetEqualityAliases(CsdtStagedDomain domain)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in domain.Rows)
        {
            var normalized = string.Join(
                "\u001f",
                KeyColumns(domain.DomainName).Select(column =>
                {
                    var value = row.ReadValue(column);
                    return value is string text
                        ? text.TrimEnd(' ').ToUpperInvariant()
                        : Convert.ToString(
                            value,
                            System.Globalization.CultureInfo.InvariantCulture) ??
                          "<NULL>";
                }));
            if (!identities.Add(normalized))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.ValidationFailed,
                    "Target-equal staged keys are not unique.");
            }
        }
    }

    private static void ValidateTrainingStates(CsdtStagedCycle staged)
    {
        var dossier = staged.Domains.Single(domain =>
            domain.DomainName == "NguoiLX_HoSo");
        foreach (var row in dossier.Rows)
        {
            var state = Convert.ToString(
                row.ReadValue("TT_XuLy"),
                System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(state) ||
                !KnownTrainingStates.Contains(state) &&
                !KnownDownstreamStates.Contains(state))
            {
                throw new CsdtAtomicCycleException(
                    CsdtAtomicCycleErrorCodes.InvalidTrainingState);
            }
        }
    }

    private static void ValidateParents(string maCsdt, CsdtStagedCycle staged)
    {
        var byName = staged.Domains.ToDictionary(
            domain => domain.DomainName,
            StringComparer.Ordinal);
        var units = Values(byName["DM_DonViGTVT"], "MaDV");
        if (!units.Contains(Normalize(maCsdt)))
        {
            throw ParentMissing();
        }

        var courses = Values(byName["KhoaHoc"], "MaKH");
        foreach (var row in byName["KhoaHoc"].Rows)
        {
            RequireParent(units, row, "MaCSDT", nullable: false);
        }

        var reports = Values(byName["BaoCaoI"], "MaBCI");
        foreach (var row in byName["BaoCaoI"].Rows)
        {
            RequireParent(units, row, "MaCSDT", nullable: false);
            RequireParent(courses, row, "MaKH", nullable: false);
        }

        var learners = Values(byName["NguoiLX"], "MaDK");
        foreach (var row in byName["NguoiLX"].Rows)
        {
            RequireParent(units, row, "DonViNhanHSo", nullable: false);
        }

        var dossiers = Values(byName["NguoiLX_HoSo"], "MaDK");
        foreach (var row in byName["NguoiLX_HoSo"].Rows)
        {
            RequireParent(units, row, "MaCSDT", nullable: false);
            RequireParent(learners, row, "MaDK", nullable: false);
            RequireParent(courses, row, "MaKhoaHoc", nullable: false);
            RequireParent(reports, row, "MaBC1", nullable: true);
        }

        foreach (var row in byName["NguoiLXHS_GiayTo"].Rows)
        {
            RequireParent(dossiers, row, "MaDK", nullable: false);
        }
    }

    private static HashSet<string> Values(CsdtStagedDomain domain, string column)
        => domain.Rows.Select(row => Normalize(row.ReadValue(column)))
            .ToHashSet(StringComparer.Ordinal);

    private static void RequireParent(
        IReadOnlySet<string> parents,
        CsdtStagedRow row,
        string column,
        bool nullable)
    {
        var value = row.ReadValue(column);
        if (nullable &&
            (value is null || string.IsNullOrWhiteSpace(Convert.ToString(value))))
        {
            return;
        }

        if (!parents.Contains(Normalize(value)))
        {
            throw ParentMissing();
        }
    }

    private static string Normalize(object? value)
        => value is string text
            ? text.TrimEnd(' ').ToUpperInvariant()
            : Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture) ??
              "<NULL>";

    private static CsdtAtomicCycleException ParentMissing()
        => new(CsdtAtomicCycleErrorCodes.ParentMissing);

    private static IReadOnlyList<string> KeyColumns(string domain)
        => domain switch
        {
            "DM_DonViGTVT" => ["MaDV"],
            "KhoaHoc" => ["MaKH"],
            "BaoCaoI" => ["MaBCI"],
            "NguoiLX" => ["MaDK"],
            "NguoiLX_HoSo" => ["MaDK"],
            "NguoiLXHS_GiayTo" => ["MaGT", "MaDK"],
            _ => throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch),
        };
}
