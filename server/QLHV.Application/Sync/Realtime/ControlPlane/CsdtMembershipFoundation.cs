using System.Collections.ObjectModel;

namespace QLHV.Application.Sync.Realtime.ControlPlane;

public enum CsdtCheckpointCasDecision
{
    FirstPublish,
    Advance,
    IdempotentReplay,
    StaleRejected,
    Conflict,
    TargetCommitRequired,
    CoverageRequired,
}

public static class CsdtCheckpointPublicationRules
{
    public static CsdtCheckpointCasDecision Evaluate(
        CsdtGlobalCheckpoint? current,
        CsdtGlobalCheckpoint candidate,
        bool targetCommitted,
        bool coverageComplete)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!targetCommitted)
        {
            return CsdtCheckpointCasDecision.TargetCommitRequired;
        }

        if (!coverageComplete)
        {
            return CsdtCheckpointCasDecision.CoverageRequired;
        }

        if (current is null)
        {
            return CsdtCheckpointCasDecision.FirstPublish;
        }

        if (!SameRoute(current, candidate) ||
            current.Status != CsdtCheckpointStatus.Active)
        {
            return CsdtCheckpointCasDecision.Conflict;
        }

        if (candidate.SourceWatermark < current.SourceWatermark)
        {
            return CsdtCheckpointCasDecision.StaleRejected;
        }

        if (!SameConfigurationFingerprints(current, candidate))
        {
            return CsdtCheckpointCasDecision.Conflict;
        }

        if (candidate.SourceWatermark > current.SourceWatermark)
        {
            return CsdtCheckpointCasDecision.Advance;
        }

        return candidate.CycleId == current.CycleId &&
               current.StagedKeySetHash.Equals(candidate.StagedKeySetHash)
            ? CsdtCheckpointCasDecision.IdempotentReplay
            : CsdtCheckpointCasDecision.Conflict;
    }

    private static bool SameRoute(
        CsdtGlobalCheckpoint left,
        CsdtGlobalCheckpoint right)
        => string.Equals(left.TargetProfile, right.TargetProfile, StringComparison.Ordinal) &&
           string.Equals(left.SourceProfile, right.SourceProfile, StringComparison.Ordinal) &&
           string.Equals(left.StreamCode, right.StreamCode, StringComparison.Ordinal);

    private static bool SameConfigurationFingerprints(
        CsdtGlobalCheckpoint left,
        CsdtGlobalCheckpoint right)
        => left.MappingFingerprint.Equals(right.MappingFingerprint) &&
           left.RouteFingerprint.Equals(right.RouteFingerprint) &&
           left.SourceSchemaFingerprint?.Equals(
               right.SourceSchemaFingerprint) == true &&
           left.TargetSchemaFingerprint?.Equals(
               right.TargetSchemaFingerprint) == true;
}

public enum CsdtTargetOnlyDisposition
{
    SourceOwnedActive,
    SourceOwnedInactive,
    TargetNative,
    V1HistoryRetained,
    UnclassifiedTargetOnly,
    OwnershipConflict,
}

public enum CsdtMembershipBootstrapState
{
    Absent,
    ActiveApplied,
    InactiveApplied,
    Conflict,
}

public enum CsdtMembershipBootstrapAction
{
    CreateMembership,
    ExistingVerified,
    ReactivationCandidate,
    Blocked,
}

public enum CsdtMembershipReconcileOutcome
{
    ObservedActive,
    InsertOrReactivateCandidate,
    AbsenceCandidate,
}

public enum CsdtTombstoneOwnershipOutcome
{
    ResolvedActiveOwner,
    AlreadyInactiveReplay,
    StaleTombstone,
    UnownedDeleteKey,
    MultipleOrAmbiguousOwner,
    MappingFingerprintMismatch,
    RouteFingerprintMismatch,
    StreamOwnershipConflict,
}

public enum CsdtReactivationOutcome
{
    Planned,
    AlreadyActiveReplay,
    StaleSourceVersion,
    DifferentStreamRejected,
    ParentMissing,
    OwnershipConflict,
}

public sealed class CsdtProtectedMembershipKey : IEquatable<CsdtProtectedMembershipKey>
{
    private readonly CanonicalBusinessKey _canonical;

    public CsdtProtectedMembershipKey(CanonicalBusinessKey canonical)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
    }

    public ushort KeySchemaVersion => _canonical.SchemaVersion;

    public CanonicalBusinessKey CopyCanonical()
        => CanonicalBusinessKey.FromEncoded(_canonical.ToArray());

    public bool Equals(CsdtProtectedMembershipKey? other)
        => other is not null && _canonical.Equals(other._canonical);

    public override bool Equals(object? obj)
        => Equals(obj as CsdtProtectedMembershipKey);

    public override int GetHashCode() => _canonical.GetHashCode();

    public override string ToString()
        => $"CsdtProtectedMembershipKey(Version={KeySchemaVersion}, Redacted=true)";
}

public sealed record CsdtMembershipEvidence(
    long MembershipId,
    MembershipRoute Route,
    CsdtProtectedMembershipKey Key,
    TypedTargetKeyClaim TypedTargetKey,
    SourceMembershipStatus Status,
    bool IsApplied,
    bool OwnershipReserved,
    long LastObservedSourceVersion,
    long? AppliedSourceVersion,
    long? DeletedAtSourceVersion,
    long? ReactivatedAtSourceVersion,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint)
{
    public override string ToString()
        => $"CsdtMembershipEvidence(Id={MembershipId}, Table={Route.TableName}, Status={Status}, Key=redacted)";
}

public sealed record CsdtTargetOnlyClassificationInput(
    string TableName,
    bool IsInsideMappedScope,
    bool IsExactRoutedUnit,
    bool HasV1History,
    CsdtMembershipEvidence? OwnershipEvidence);

public static class CsdtTargetOnlyClassifier
{
    public static CsdtTargetOnlyDisposition Classify(
        MembershipRoute requestedRoute,
        CsdtTargetOnlyClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(requestedRoute);
        ArgumentNullException.ThrowIfNull(input);
        CsdtControlPlaneCatalog.ValidateRoute(requestedRoute);
        if (!CsdtAtomicCoreDomains.Names.Contains(input.TableName) ||
            !string.Equals(
                requestedRoute.TableName,
                input.TableName,
                StringComparison.Ordinal))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }

        if (string.Equals(input.TableName, "DM_DonViGTVT", StringComparison.Ordinal) &&
            !input.IsExactRoutedUnit)
        {
            return CsdtTargetOnlyDisposition.TargetNative;
        }

        if (!input.IsInsideMappedScope)
        {
            return CsdtTargetOnlyDisposition.TargetNative;
        }

        var evidence = input.OwnershipEvidence;
        if (evidence is null)
        {
            return CsdtTargetOnlyDisposition.UnclassifiedTargetOnly;
        }

        if (!SameOwner(requestedRoute, evidence.Route) ||
            !evidence.OwnershipReserved)
        {
            return CsdtTargetOnlyDisposition.OwnershipConflict;
        }

        if (input.HasV1History &&
            evidence.Status == SourceMembershipStatus.Inactive)
        {
            return CsdtTargetOnlyDisposition.V1HistoryRetained;
        }

        return evidence.Status switch
        {
            SourceMembershipStatus.Active when evidence.IsApplied =>
                CsdtTargetOnlyDisposition.SourceOwnedActive,
            SourceMembershipStatus.Inactive when evidence.IsApplied =>
                CsdtTargetOnlyDisposition.SourceOwnedInactive,
            _ => CsdtTargetOnlyDisposition.OwnershipConflict,
        };
    }

    internal static bool SameOwner(MembershipRoute left, MembershipRoute right)
        => string.Equals(left.TargetProfile, right.TargetProfile, StringComparison.Ordinal) &&
           string.Equals(left.SourceProfile, right.SourceProfile, StringComparison.Ordinal) &&
           string.Equals(left.StreamCode, right.StreamCode, StringComparison.Ordinal) &&
           string.Equals(left.MaCsdt, right.MaCsdt, StringComparison.Ordinal) &&
           string.Equals(left.TableName, right.TableName, StringComparison.Ordinal);
}

public sealed record CsdtMembershipBootstrapObservation(
    string DomainName,
    CsdtProtectedMembershipKey Key,
    CsdtMembershipBootstrapState State,
    bool HasTypedOwnershipClaim,
    bool ParentMembershipReady,
    bool TargetRowVerified);

public sealed record CsdtMembershipBootstrapDomainPlan(
    string DomainName,
    long SourceCount,
    long MembershipCreateCount,
    long ExistingActiveCount,
    long ReactivationCandidateCount,
    long ConflictCount,
    ControlPlaneFingerprint SourceKeySetHash,
    bool Ready)
{
    public override string ToString()
        => $"CsdtMembershipBootstrapDomainPlan(Domain={DomainName}, Source={SourceCount}, Create={MembershipCreateCount}, Existing={ExistingActiveCount}, Conflicts={ConflictCount})";
}

public sealed class CsdtMembershipBootstrapPlan
{
    private readonly ReadOnlyCollection<CsdtMembershipBootstrapDomainPlan> _domains;

    internal CsdtMembershipBootstrapPlan(
        Guid cycleId,
        long watermark,
        IEnumerable<CsdtMembershipBootstrapDomainPlan> domains,
        IReadOnlyDictionary<CsdtTargetOnlyDisposition, long> targetOnlyCounts,
        bool canApply,
        string? blockingReason)
    {
        CycleId = cycleId;
        Watermark = watermark;
        _domains = Array.AsReadOnly(domains.ToArray());
        TargetOnlyCounts = new ReadOnlyDictionary<CsdtTargetOnlyDisposition, long>(
            targetOnlyCounts.ToDictionary());
        CanApply = canApply;
        BlockingReason = blockingReason;
    }

    public Guid CycleId { get; }

    public long Watermark { get; }

    public IReadOnlyList<CsdtMembershipBootstrapDomainPlan> Domains => _domains;

    public IReadOnlyDictionary<CsdtTargetOnlyDisposition, long> TargetOnlyCounts { get; }

    public bool CanApply { get; }

    public string? BlockingReason { get; }

    public override string ToString()
        => $"CsdtMembershipBootstrapPlan(CycleId={CycleId:D}, Watermark={Watermark}, Domains={Domains.Count}, Ready={CanApply})";
}

public static class CsdtMembershipBootstrapPlanner
{
    public static CsdtMembershipBootstrapPlan Plan(
        CsdtStagedCycle staged,
        IEnumerable<CsdtMembershipBootstrapObservation> observations,
        IEnumerable<CsdtTargetOnlyDisposition>? targetOnlyDispositions = null)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(observations);
        CsdtAtomicCoreDomains.RequireExactScope(
            staged.Domains.Select(domain => domain.DomainName));
        if (staged.OperationMode != CsdtAtomicOperationMode.FullSnapshot)
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
        }

        var byDomain = observations
            .GroupBy(item => item.DomainName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(item => item.Key),
                StringComparer.Ordinal);
        if (byDomain.Keys.Any(domain =>
                !CsdtAtomicCoreDomains.Names.Contains(domain)))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }

        var plans = new List<CsdtMembershipBootstrapDomainPlan>(
            CsdtAtomicCoreDomains.ApplyOrder.Count);
        var allReady = true;
        string? reason = null;
        foreach (var domain in staged.Domains)
        {
            var observationsForDomain = byDomain.GetValueOrDefault(domain.DomainName) ??
                new Dictionary<CsdtProtectedMembershipKey, CsdtMembershipBootstrapObservation>();
            var create = 0L;
            var existing = 0L;
            var reactivate = 0L;
            var conflict = 0L;
            foreach (var encoded in domain.CompleteKeys)
            {
                var key = new CsdtProtectedMembershipKey(
                    CanonicalBusinessKey.FromEncoded(encoded));
                if (!observationsForDomain.TryGetValue(key, out var observation))
                {
                    create++;
                    continue;
                }

                if (!observation.ParentMembershipReady)
                {
                    conflict++;
                    reason ??= CsdtMembershipReasonCodes.BootstrapParentMissing;
                    continue;
                }

                if (observation.State == CsdtMembershipBootstrapState.Conflict)
                {
                    conflict++;
                    reason ??= CsdtMembershipReasonCodes.OwnershipConflict;
                    continue;
                }

                switch (observation.State)
                {
                    case CsdtMembershipBootstrapState.Absent:
                        create++;
                        break;
                    case CsdtMembershipBootstrapState.ActiveApplied
                        when observation.HasTypedOwnershipClaim &&
                             observation.TargetRowVerified:
                        existing++;
                        break;
                    case CsdtMembershipBootstrapState.InactiveApplied:
                        reactivate++;
                        reason ??= CsdtMembershipReasonCodes.ReactivationCandidate;
                        break;
                    default:
                        conflict++;
                        reason ??= CsdtMembershipReasonCodes.OwnershipConflict;
                        break;
                }
            }

            var ready = conflict == 0 && reactivate == 0 &&
                        create + existing == domain.SourceRowCount;
            allReady &= ready;
            plans.Add(new CsdtMembershipBootstrapDomainPlan(
                domain.DomainName,
                domain.SourceRowCount,
                create,
                existing,
                reactivate,
                conflict,
                domain.SourceKeySetHash,
                ready));
        }

        var targetCounts = (targetOnlyDispositions ?? [])
            .GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.LongCount());
        var unclassified = targetCounts.GetValueOrDefault(
            CsdtTargetOnlyDisposition.UnclassifiedTargetOnly);
        var ownershipConflicts = targetCounts.GetValueOrDefault(
            CsdtTargetOnlyDisposition.OwnershipConflict);
        if (unclassified != 0 || ownershipConflicts != 0)
        {
            allReady = false;
            reason ??= unclassified != 0
                ? CsdtMembershipReasonCodes.TargetOnlyUnclassified
                : CsdtMembershipReasonCodes.OwnershipConflict;
        }

        return new CsdtMembershipBootstrapPlan(
            staged.CycleId,
            staged.EndSourceVersion,
            plans,
            targetCounts,
            allReady,
            reason);
    }
}

public sealed record CsdtCommittedMembershipDomain(
    string DomainName,
    long ActiveAppliedMembershipCount,
    long TypedOwnershipClaimCount,
    long ConflictCount,
    long UnclassifiedTargetOnlyCount,
    ControlPlaneFingerprint SourceKeySetHash,
    bool DomainResultCommitted);

public sealed record CsdtCoverageVerification(
    IReadOnlyList<StreamCoverageState> Domains,
    bool IsGlobalComplete,
    string? BlockingReason);

public static class CsdtCoverageProtocol
{
    public static CsdtCoverageVerification Verify(
        CsdtStagedCycle staged,
        IEnumerable<CsdtCommittedMembershipDomain> committedDomains)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(committedDomains);
        var committed = committedDomains.ToDictionary(
            value => value.DomainName,
            StringComparer.Ordinal);
        if (committed.Keys.Any(domain =>
                !CsdtAtomicCoreDomains.Names.Contains(domain)))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }

        var coverage = new List<StreamCoverageState>(
            CsdtAtomicCoreDomains.ApplyOrder.Count);
        var globalComplete = true;
        string? reason = null;
        foreach (var domain in staged.Domains)
        {
            var route = new MembershipRoute(
                staged.TargetProfile,
                staged.SourceProfile,
                staged.StreamCode,
                staged.MaCsdt,
                domain.DomainName);
            var complete =
                committed.TryGetValue(domain.DomainName, out var result) &&
                result.DomainResultCommitted &&
                result.ConflictCount == 0 &&
                result.UnclassifiedTargetOnlyCount == 0 &&
                result.ActiveAppliedMembershipCount == domain.SourceRowCount &&
                result.TypedOwnershipClaimCount == domain.SourceRowCount &&
                result.SourceKeySetHash.Equals(domain.SourceKeySetHash);
            globalComplete &= complete;
            if (!complete)
            {
                reason ??= CsdtMembershipReasonCodes.BootstrapIncomplete;
            }

            coverage.Add(new StreamCoverageState(
                route,
                staged.EndSourceVersion,
                staged.MappingFingerprint,
                staged.RouteFingerprint,
                staged.SourceKeySetHashFor(domain.DomainName),
                complete ? domain.SourceRowCount : 0,
                complete,
                complete ? staged.CycleId : null,
                complete ? DateTimeOffset.UtcNow : null,
                staged.SourceSchemaFingerprint,
                staged.TargetSchemaFingerprint));
        }

        return new CsdtCoverageVerification(
            Array.AsReadOnly(coverage.ToArray()),
            globalComplete && coverage.Count == CsdtAtomicCoreDomains.ApplyOrder.Count,
            reason);
    }

    private static ControlPlaneFingerprint SourceKeySetHashFor(
        this CsdtStagedCycle staged,
        string domainName)
        => staged.Domains.Single(domain =>
            string.Equals(domain.DomainName, domainName, StringComparison.Ordinal))
            .SourceKeySetHash;
}

public sealed record CsdtMembershipReconcileItem(
    string DomainName,
    CsdtProtectedMembershipKey Key,
    CsdtMembershipReconcileOutcome Outcome,
    bool IsReactivation)
{
    public override string ToString()
        => $"CsdtMembershipReconcileItem(Domain={DomainName}, Outcome={Outcome}, Key=redacted)";
}

public sealed class CsdtMembershipReconcilePlan
{
    private readonly ReadOnlyCollection<CsdtMembershipReconcileItem> _items;

    internal CsdtMembershipReconcilePlan(
        string domainName,
        IEnumerable<CsdtMembershipReconcileItem> items,
        bool coverageReady,
        bool checkpointReady,
        string? blockingReason)
    {
        DomainName = domainName;
        _items = Array.AsReadOnly(items.ToArray());
        CoverageReady = coverageReady;
        CheckpointReady = checkpointReady;
        BlockingReason = blockingReason;
    }

    public string DomainName { get; }

    public IReadOnlyList<CsdtMembershipReconcileItem> Items => _items;

    public bool CoverageReady { get; }

    public bool CheckpointReady { get; }

    public string? BlockingReason { get; }

    public override string ToString()
        => $"CsdtMembershipReconcilePlan(Domain={DomainName}, Items={Items.Count}, CheckpointReady={CheckpointReady})";
}

public static class CsdtMembershipReconcilePlanner
{
    public static CsdtMembershipReconcilePlan Plan(
        MembershipRoute route,
        IEnumerable<CanonicalBusinessKey> completeSourceKeys,
        IEnumerable<CsdtMembershipEvidence> memberships,
        StreamCoverageState? coverage,
        long sourceVersion,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint,
        ControlPlaneFingerprint sourceSchemaFingerprint,
        ControlPlaneFingerprint targetSchemaFingerprint,
        bool deleteExecutionEnabled)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(completeSourceKeys);
        ArgumentNullException.ThrowIfNull(memberships);
        ArgumentNullException.ThrowIfNull(mappingFingerprint);
        ArgumentNullException.ThrowIfNull(routeFingerprint);
        ArgumentNullException.ThrowIfNull(sourceSchemaFingerprint);
        ArgumentNullException.ThrowIfNull(targetSchemaFingerprint);
        CsdtControlPlaneCatalog.ValidateRoute(route);
        if (!CsdtAtomicCoreDomains.Names.Contains(route.TableName))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }

        var coverageReady = coverage is not null &&
            coverage.AllowsReconciliation(
                mappingFingerprint,
                routeFingerprint,
                sourceSchemaFingerprint,
                targetSchemaFingerprint);
        if (!coverageReady)
        {
            return new CsdtMembershipReconcilePlan(
                route.TableName,
                [],
                false,
                false,
                CsdtMembershipReasonCodes.CoverageIncomplete);
        }

        var source = completeSourceKeys
            .Select(key => new CsdtProtectedMembershipKey(key))
            .ToHashSet();
        var scoped = memberships
            .Where(item => CsdtTargetOnlyClassifier.SameOwner(route, item.Route))
            .ToDictionary(item => item.Key);
        var items = new List<CsdtMembershipReconcileItem>();
        foreach (var key in source)
        {
            if (scoped.TryGetValue(key, out var membership) &&
                membership.Status == SourceMembershipStatus.Active &&
                membership.IsApplied)
            {
                items.Add(new CsdtMembershipReconcileItem(
                    route.TableName,
                    key,
                    CsdtMembershipReconcileOutcome.ObservedActive,
                    IsReactivation: false));
            }
            else
            {
                items.Add(new CsdtMembershipReconcileItem(
                    route.TableName,
                    key,
                    CsdtMembershipReconcileOutcome.InsertOrReactivateCandidate,
                    IsReactivation: membership?.Status == SourceMembershipStatus.Inactive &&
                                    sourceVersion >
                                    Math.Max(
                                        membership.AppliedSourceVersion ?? -1,
                                        membership.ReactivatedAtSourceVersion ?? -1)));
            }
        }

        foreach (var membership in scoped.Values.Where(item =>
                     item.Status == SourceMembershipStatus.Active &&
                     item.IsApplied &&
                     !source.Contains(item.Key)))
        {
            items.Add(new CsdtMembershipReconcileItem(
                route.TableName,
                membership.Key,
                CsdtMembershipReconcileOutcome.AbsenceCandidate,
                IsReactivation: false));
        }

        var hasAbsence = items.Any(item =>
            item.Outcome == CsdtMembershipReconcileOutcome.AbsenceCandidate);
        return new CsdtMembershipReconcilePlan(
            route.TableName,
            items.OrderBy(item => item.Outcome).ToArray(),
            true,
            !hasAbsence || deleteExecutionEnabled,
            hasAbsence && !deleteExecutionEnabled
                ? CsdtMembershipReasonCodes.DeleteExecutionNotEnabled
                : null);
    }
}

public sealed record CsdtTombstoneOwnershipRequest(
    MembershipRoute Route,
    TypedTargetKeyClaim TypedTargetKey,
    long SourceVersion,
    ushort KeySchemaVersion,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint);

public sealed record CsdtDeletePendingPlan(
    long MembershipId,
    MembershipRoute Route,
    long TombstoneSourceVersion,
    CsdtProtectedMembershipKey Key,
    SourceMembershipReasonCode ReasonCode)
{
    public override string ToString()
        => $"CsdtDeletePendingPlan(MembershipId={MembershipId}, Table={Route.TableName}, Version={TombstoneSourceVersion}, Key=redacted)";
}

public sealed record CsdtTombstoneOwnershipResolution(
    CsdtTombstoneOwnershipOutcome Outcome,
    CsdtDeletePendingPlan? Plan,
    string ReasonCode)
{
    public override string ToString()
        => $"CsdtTombstoneOwnershipResolution(Outcome={Outcome}, Reason={ReasonCode}, Key=redacted)";
}

public static class CsdtTombstoneOwnershipResolver
{
    public static CsdtTombstoneOwnershipResolution Resolve(
        CsdtTombstoneOwnershipRequest request,
        IEnumerable<CsdtMembershipEvidence> authoritativeTypedClaims)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authoritativeTypedClaims);
        CsdtControlPlaneCatalog.ValidateRoute(request.Route);
        request.TypedTargetKey.ValidateForRoute(request.Route);
        if (request.SourceVersion < 0 ||
            request.KeySchemaVersion != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var canonical = new CsdtProtectedMembershipKey(
            CsdtTypedKeyCanonicalizer.Canonicalize(request.TypedTargetKey));
        var candidates = authoritativeTypedClaims.ToArray();
        if (candidates.Length == 0)
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.UnownedDeleteKey,
                CsdtMembershipReasonCodes.UnownedDeleteKey);
        }

        if (candidates.Length != 1)
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.MultipleOrAmbiguousOwner,
                CsdtMembershipReasonCodes.MultipleOrAmbiguousOwner);
        }

        var owner = candidates[0];
        try
        {
            owner.TypedTargetKey.ValidateForRoute(owner.Route);
        }
        catch (ArgumentException)
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.StreamOwnershipConflict,
                CsdtMembershipReasonCodes.StreamOwnershipConflict);
        }

        if (!CsdtTargetOnlyClassifier.SameOwner(request.Route, owner.Route) ||
            !owner.OwnershipReserved ||
            owner.Key.KeySchemaVersion != request.KeySchemaVersion ||
            !owner.Key.Equals(canonical) ||
            !new CsdtProtectedMembershipKey(
                CsdtTypedKeyCanonicalizer.Canonicalize(owner.TypedTargetKey))
                .Equals(canonical))
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.StreamOwnershipConflict,
                CsdtMembershipReasonCodes.StreamOwnershipConflict);
        }

        if (!owner.MappingFingerprint.Equals(request.MappingFingerprint))
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.MappingFingerprintMismatch,
                CsdtMembershipReasonCodes.MappingFingerprintMismatch);
        }

        if (!owner.RouteFingerprint.Equals(request.RouteFingerprint))
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.RouteFingerprintMismatch,
                CsdtMembershipReasonCodes.RouteFingerprintMismatch);
        }

        var latestPresence = Math.Max(
            owner.LastObservedSourceVersion,
            Math.Max(
                owner.AppliedSourceVersion ?? -1,
                owner.ReactivatedAtSourceVersion ?? -1));
        if (request.SourceVersion <= latestPresence)
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.StaleTombstone,
                CsdtMembershipReasonCodes.StaleTombstone);
        }

        if (owner.Status == SourceMembershipStatus.Inactive)
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.AlreadyInactiveReplay,
                CsdtMembershipReasonCodes.AlreadyInactiveReplay);
        }

        if (owner.Status != SourceMembershipStatus.Active || !owner.IsApplied)
        {
            return Result(
                CsdtTombstoneOwnershipOutcome.StreamOwnershipConflict,
                CsdtMembershipReasonCodes.StreamOwnershipConflict);
        }

        return new CsdtTombstoneOwnershipResolution(
            CsdtTombstoneOwnershipOutcome.ResolvedActiveOwner,
            new CsdtDeletePendingPlan(
                owner.MembershipId,
                owner.Route,
                request.SourceVersion,
                owner.Key,
                SourceMembershipReasonCode.SourceDelete),
            CsdtMembershipReasonCodes.ResolvedActiveOwner);
    }

    private static CsdtTombstoneOwnershipResolution Result(
        CsdtTombstoneOwnershipOutcome outcome,
        string reason)
        => new(outcome, null, reason);
}

public static class CsdtTypedKeyCanonicalizer
{
    public static CanonicalBusinessKey Canonicalize(TypedTargetKeyClaim key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.TableName switch
        {
            "DM_DonViGTVT" => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString(key.DmDonViGtvtMaDv!)),
            "KhoaHoc" => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString(key.KhoaHocMaKh!)),
            "BaoCaoI" => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString(key.BaoCaoIMaBci!)),
            "NguoiLX" => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString(key.NguoiLxMaDk!)),
            "NguoiLX_HoSo" => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromString(key.NguoiLxHoSoMaDk!)),
            "NguoiLXHS_GiayTo" => CanonicalBusinessKeyEncoder.Encode(
                1,
                CanonicalKeyComponent.FromInt32(key.GiayToMaGt!.Value),
                CanonicalKeyComponent.FromString(key.GiayToMaDk!)),
            _ => throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch),
        };
    }

    public static TypedTargetKeyClaim FromStagedRow(
        string domainName,
        CsdtStagedRow row)
        => domainName switch
        {
            "DM_DonViGTVT" => TypedTargetKeyClaim.ForDmDonViGtvt(
                RequiredText(row, "MaDV")),
            "KhoaHoc" => TypedTargetKeyClaim.ForKhoaHoc(
                RequiredText(row, "MaKH")),
            "BaoCaoI" => TypedTargetKeyClaim.ForBaoCaoI(
                RequiredText(row, "MaBCI")),
            "NguoiLX" => TypedTargetKeyClaim.ForNguoiLx(
                RequiredText(row, "MaDK")),
            "NguoiLX_HoSo" => TypedTargetKeyClaim.ForNguoiLxHoSo(
                RequiredText(row, "MaDK")),
            "NguoiLXHS_GiayTo" => TypedTargetKeyClaim.ForNguoiLxHsGiayTo(
                Convert.ToInt32(
                    row.ReadValue("MaGT"),
                    System.Globalization.CultureInfo.InvariantCulture),
                RequiredText(row, "MaDK")),
            _ => throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch),
        };

    private static string RequiredText(CsdtStagedRow row, string column)
        => Convert.ToString(
               row.ReadValue(column),
               System.Globalization.CultureInfo.InvariantCulture) ??
           throw new CsdtAtomicCycleException(
               CsdtAtomicCycleErrorCodes.ValidationFailed);
}

public sealed record CsdtReactivationRequest(
    MembershipRoute Route,
    CsdtMembershipEvidence Existing,
    long SourceVersion,
    IReadOnlyList<string> ReadyParentDomains,
    bool ExistingShellFound,
    bool CompletedLifecycle);

public sealed record CsdtReactivationPlan(
    CsdtReactivationOutcome Outcome,
    string DomainName,
    long SourceVersion,
    bool PreserveV1History,
    bool ResyncV2OwnedColumns,
    bool CreateDuplicateShell,
    bool ResetCompletedLifecycle,
    IReadOnlyList<string> ConditionalMergeColumns,
    string ReasonCode)
{
    public override string ToString()
        => $"CsdtReactivationPlan(Domain={DomainName}, Outcome={Outcome}, Version={SourceVersion}, Key=redacted)";
}

public static class CsdtReactivationPlanner
{
    private static readonly IReadOnlyList<string> SpecialMerges =
        Array.AsReadOnly(["TT_XuLy", "GhiChu", "GiayCNSK", "GiaiTrinh"]);

    public static CsdtReactivationPlan Plan(CsdtReactivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CsdtControlPlaneCatalog.ValidateRoute(request.Route);
        if (!CsdtAtomicCoreDomains.Names.Contains(request.Route.TableName))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.AtomicCoreScopeMismatch);
        }

        var existing = request.Existing;
        if (!CsdtTargetOnlyClassifier.SameOwner(request.Route, existing.Route))
        {
            return Blocked(
                request,
                CsdtReactivationOutcome.DifferentStreamRejected,
                CsdtMembershipReasonCodes.StreamOwnershipConflict);
        }

        if (!existing.OwnershipReserved || !request.ExistingShellFound)
        {
            return Blocked(
                request,
                CsdtReactivationOutcome.OwnershipConflict,
                CsdtMembershipReasonCodes.OwnershipConflict);
        }

        if (existing.Status == SourceMembershipStatus.Active && existing.IsApplied)
        {
            return Blocked(
                request,
                CsdtReactivationOutcome.AlreadyActiveReplay,
                CsdtMembershipReasonCodes.AlreadyActiveReplay);
        }

        if (existing.Status != SourceMembershipStatus.Inactive ||
            !existing.IsApplied)
        {
            return Blocked(
                request,
                CsdtReactivationOutcome.OwnershipConflict,
                CsdtMembershipReasonCodes.OwnershipConflict);
        }

        var latest = Math.Max(
            existing.LastObservedSourceVersion,
            Math.Max(
                existing.AppliedSourceVersion ?? -1,
                existing.ReactivatedAtSourceVersion ?? -1));
        if (request.SourceVersion <= latest)
        {
            return Blocked(
                request,
                CsdtReactivationOutcome.StaleSourceVersion,
                CsdtMembershipReasonCodes.StaleSourceVersion);
        }

        var position = Array.FindIndex(
            CsdtAtomicCoreDomains.ApplyOrder.ToArray(),
            domain => string.Equals(
                domain,
                request.Route.TableName,
                StringComparison.Ordinal));
        var requiredParents = CsdtAtomicCoreDomains.ApplyOrder.Take(position);
        if (requiredParents.Any(parent =>
                !request.ReadyParentDomains.Contains(parent, StringComparer.Ordinal)))
        {
            return Blocked(
                request,
                CsdtReactivationOutcome.ParentMissing,
                CsdtMembershipReasonCodes.BootstrapParentMissing);
        }

        return new CsdtReactivationPlan(
            CsdtReactivationOutcome.Planned,
            request.Route.TableName,
            request.SourceVersion,
            PreserveV1History: true,
            ResyncV2OwnedColumns: true,
            CreateDuplicateShell: false,
            ResetCompletedLifecycle: false,
            SpecialMerges,
            CsdtMembershipReasonCodes.ReactivationCandidate);
    }

    private static CsdtReactivationPlan Blocked(
        CsdtReactivationRequest request,
        CsdtReactivationOutcome outcome,
        string reason)
        => new(
            outcome,
            request.Route.TableName,
            request.SourceVersion,
            PreserveV1History: true,
            ResyncV2OwnedColumns: false,
            CreateDuplicateShell: false,
            ResetCompletedLifecycle: false,
            SpecialMerges,
            reason);
}

public sealed record CsdtMembershipDryRunDomain(
    string DomainName,
    long SourceCount,
    long MembershipCreateCount,
    long ExistingActiveCount,
    long ReactivationCandidateCount,
    long AbsenceCandidateCount,
    long ConflictCount,
    bool CoverageReady,
    ControlPlaneFingerprint SourceKeySetHash);

public sealed record CsdtMembershipDryRunReport(
    Guid CycleId,
    string TargetProfile,
    string SourceProfile,
    string StreamCode,
    long Watermark,
    IReadOnlyList<CsdtMembershipDryRunDomain> Domains,
    IReadOnlyDictionary<CsdtTargetOnlyDisposition, long> TargetOnlyClassifications,
    bool CoverageReady,
    bool CheckpointReady,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint SourceSchemaFingerprint,
    ControlPlaneFingerprint TargetSchemaFingerprint,
    ControlPlaneFingerprint StagedKeySetHash)
{
    public override string ToString()
        => $"CsdtMembershipDryRunReport(CycleId={CycleId:D}, Stream={StreamCode}, Watermark={Watermark}, Domains={Domains.Count}, CoverageReady={CoverageReady}, CheckpointReady={CheckpointReady}, RawKeys=false)";
}

public static class CsdtMembershipDryRunFactory
{
    public static CsdtMembershipDryRunReport Create(
        CsdtStagedCycle staged,
        CsdtMembershipBootstrapPlan bootstrap,
        IEnumerable<CsdtMembershipReconcilePlan>? reconcile = null,
        CsdtCoverageVerification? coverage = null)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(bootstrap);
        if (bootstrap.CycleId != staged.CycleId ||
            bootstrap.Watermark != staged.EndSourceVersion ||
            bootstrap.Domains.Count != CsdtAtomicCoreDomains.ApplyOrder.Count ||
            !bootstrap.Domains.Select(item => item.DomainName)
                .SequenceEqual(
                    CsdtAtomicCoreDomains.ApplyOrder,
                    StringComparer.Ordinal))
        {
            throw new CsdtAtomicCycleException(
                CsdtAtomicCycleErrorCodes.SourceStageIncomplete);
        }

        var reconcileByDomain = (reconcile ?? [])
            .ToDictionary(item => item.DomainName, StringComparer.Ordinal);
        var domains = bootstrap.Domains.Select(domain =>
        {
            var reconcilePlan = reconcileByDomain.GetValueOrDefault(domain.DomainName);
            return new CsdtMembershipDryRunDomain(
                domain.DomainName,
                domain.SourceCount,
                domain.MembershipCreateCount,
                domain.ExistingActiveCount,
                domain.ReactivationCandidateCount,
                reconcilePlan?.Items.LongCount(item =>
                    item.Outcome == CsdtMembershipReconcileOutcome.AbsenceCandidate) ?? 0,
                domain.ConflictCount,
                coverage?.Domains.SingleOrDefault(item =>
                    string.Equals(
                        item.Route.TableName,
                        domain.DomainName,
                        StringComparison.Ordinal))?.IsComplete ?? false,
                domain.SourceKeySetHash);
        }).ToArray();
        var coverageReady = coverage?.IsGlobalComplete ?? false;
        var checkpointReady = bootstrap.CanApply &&
            coverageReady &&
            reconcileByDomain.Values.All(item => item.CheckpointReady);
        return new CsdtMembershipDryRunReport(
            staged.CycleId,
            staged.TargetProfile,
            staged.SourceProfile,
            staged.StreamCode,
            staged.EndSourceVersion,
            Array.AsReadOnly(domains),
            bootstrap.TargetOnlyCounts,
            coverageReady,
            checkpointReady,
            staged.MappingFingerprint,
            staged.RouteFingerprint,
            staged.SourceSchemaFingerprint,
            staged.TargetSchemaFingerprint,
            staged.StagedKeySetHash);
    }
}

public static class CsdtMembershipReasonCodes
{
    public const string BootstrapIncomplete = "BOOTSTRAP_INCOMPLETE";
    public const string BootstrapParentMissing = "BOOTSTRAP_PARENT_MISSING";
    public const string ReactivationCandidate = "REACTIVATION_CANDIDATE";
    public const string OwnershipConflict = "OWNERSHIP_CONFLICT";
    public const string TargetOnlyUnclassified = "TARGET_ONLY_UNCLASSIFIED";
    public const string CoverageIncomplete = "COVERAGE_INCOMPLETE";
    public const string DeleteExecutionNotEnabled = "DELETE_EXECUTION_NOT_ENABLED";
    public const string UnownedDeleteKey = "UNOWNED_DELETE_KEY";
    public const string MultipleOrAmbiguousOwner = "MULTIPLE_OR_AMBIGUOUS_OWNER";
    public const string StreamOwnershipConflict = "STREAM_OWNERSHIP_CONFLICT";
    public const string MappingFingerprintMismatch = "MAPPING_FINGERPRINT_MISMATCH";
    public const string RouteFingerprintMismatch = "ROUTE_FINGERPRINT_MISMATCH";
    public const string StaleTombstone = "STALE_TOMBSTONE";
    public const string AlreadyInactiveReplay = "ALREADY_INACTIVE_REPLAY";
    public const string ResolvedActiveOwner = "RESOLVED_ACTIVE_OWNER";
    public const string AlreadyActiveReplay = "ALREADY_ACTIVE_REPLAY";
    public const string StaleSourceVersion = "STALE_SOURCE_VERSION";
}
