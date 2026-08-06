using QLHV.Application.Runtime;

namespace QLHV.Application.Sync.Rt03;

public static class Rt03RecoveryClassifications
{
    public const string IncrementalValid = "INCREMENTAL_VALID";
    public const string ExpiredRequiresFullConvergence =
        "EXPIRED_REQUIRES_FULL_CONVERGENCE";
    public const string CtDisabledRequiresSnapshot =
        "CT_DISABLED_REQUIRES_SNAPSHOT";
    public const string Unclassified = "UNCLASSIFIED";
    public const string UnsafeDeleteContract = "UNSAFE_DELETE_CONTRACT";
}

public sealed record Rt03TrackedTableAudit(
    string SourceProfileCode,
    string TableName,
    bool TableExists,
    bool ChangeTrackingEnabled,
    long? MinimumValidVersion,
    long CommittedCheckpoint,
    bool DeleteContractVerified);

public static class Rt03ChangeTrackingRecoveryClassifier
{
    public static string Classify(Rt03TrackedTableAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (!audit.TableExists || audit.CommittedCheckpoint < 0)
        {
            return Rt03RecoveryClassifications.Unclassified;
        }

        if (!audit.DeleteContractVerified)
        {
            return Rt03RecoveryClassifications.UnsafeDeleteContract;
        }

        if (!audit.ChangeTrackingEnabled)
        {
            return Rt03RecoveryClassifications.CtDisabledRequiresSnapshot;
        }

        if (audit.MinimumValidVersion is null ||
            audit.MinimumValidVersion.Value < 0)
        {
            return Rt03RecoveryClassifications.Unclassified;
        }

        return audit.CommittedCheckpoint < audit.MinimumValidVersion.Value
            ? Rt03RecoveryClassifications.ExpiredRequiresFullConvergence
            : Rt03RecoveryClassifications.IncrementalValid;
    }
}

public static class Rt03FullConvergenceDomains
{
    public const string Course = "COURSE";
    public const string Teacher = "TEACHER";
    public const string Vehicle = "VEHICLE";
    public const string Learner = "LEARNER";
    public const string Relation = "RELATION";

    public static IReadOnlyList<string> Ordered { get; } =
        [Course, Teacher, Vehicle, Learner, Relation];
}

public static class Rt03FullConvergenceLocks
{
    public const string Global = "QLHV:CSDT_AUTO_SYNC";

    public static IReadOnlyList<string> ForProfile(string sourceProfileCode)
    {
        var sourceType =
            QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(sourceProfileCode);
        return
        [
            Global,
            $"QLHV:RT03:RECOVERY:{sourceProfileCode}",
            QlhvOperationSourceCatalog.GetRequired(sourceType).LockResource,
            ..Rt03FullConvergenceDomains.Ordered.Select(
                domain => $"QLHV:RT03:RECOVERY:{sourceProfileCode}:{domain}"),
        ];
    }
}

public static class Rt03FullConvergenceActions
{
    public const string Insert = "INSERT";
    public const string UpdateSourceOwned = "UPDATE_SOURCE_OWNED";
    public const string NoChange = "NO_CHANGE";
    public const string MarkSourceInactive = "MARK_SOURCE_INACTIVE";
    public const string MarkSourceMissing = "MARK_SOURCE_MISSING";
    public const string ManualReview = "MANUAL_REVIEW";
    public const string BlockedAmbiguous = "BLOCKED_AMBIGUOUS";
    public const string BlockedOwnership = "BLOCKED_OWNERSHIP";
    public const string BlockedDeleteContract = "BLOCKED_DELETE_CONTRACT";
}

public sealed record Rt03FullConvergenceSourceRow(
    string SourceProfileCode,
    string Domain,
    string ExactIdentity,
    string SourceOwnedHash,
    bool IsActive);

public sealed record Rt03FullConvergenceTargetRow(
    long TargetId,
    string? SourceProfileCode,
    string Domain,
    string? ExactIdentity,
    string? SourceOwnedHash,
    string QlhvOwnedHash,
    bool IsDeleted,
    bool HasActiveAssignment,
    bool IsManualHold);

public sealed record Rt03FullConvergenceRowPlan(
    string SourceProfileCode,
    string Domain,
    string ExactIdentity,
    string Action,
    long? TargetId,
    string? ExpectedQlhvOwnedHash,
    string? Reason)
{
    public bool IsBlocked => Action.StartsWith("BLOCKED_", StringComparison.Ordinal);
}

public sealed record Rt03FullConvergenceDomainPlan(
    string SourceProfileCode,
    string Domain,
    IReadOnlyList<Rt03FullConvergenceRowPlan> Rows)
{
    public bool IsSafe => Rows.All(row => !row.IsBlocked);
}

public static class Rt03FullConvergencePlanner
{
    public static Rt03FullConvergenceDomainPlan Plan(
        string sourceProfileCode,
        string domain,
        IReadOnlyCollection<Rt03FullConvergenceSourceRow> sourceRows,
        IReadOnlyCollection<Rt03FullConvergenceTargetRow> targetRows,
        bool missingSourceLifecycleVerified)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(targetRows);
        RequireProfile(sourceProfileCode);
        RequireDomain(domain);

        var source = sourceRows
            .Where(row =>
                string.Equals(
                    row.SourceProfileCode,
                    sourceProfileCode,
                    StringComparison.Ordinal) &&
                string.Equals(row.Domain, domain, StringComparison.Ordinal))
            .ToArray();
        var targets = targetRows
            .Where(row =>
                string.Equals(row.Domain, domain, StringComparison.Ordinal) &&
                string.Equals(
                    row.SourceProfileCode,
                    sourceProfileCode,
                    StringComparison.Ordinal))
            .ToArray();
        var result = new List<Rt03FullConvergenceRowPlan>(
            source.Length + targets.Length);

        var duplicateSourceKeys = source
            .GroupBy(row => row.ExactIdentity, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var duplicateTargetKeys = targets
            .Where(row => !string.IsNullOrWhiteSpace(row.ExactIdentity))
            .GroupBy(row => row.ExactIdentity!, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in source.OrderBy(item => item.ExactIdentity, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(row.ExactIdentity) ||
                string.IsNullOrWhiteSpace(row.SourceOwnedHash))
            {
                result.Add(Blocked(
                    row,
                    Rt03FullConvergenceActions.BlockedOwnership,
                    "Source identity/hash is not classified."));
                continue;
            }

            if (duplicateSourceKeys.Contains(row.ExactIdentity) ||
                duplicateTargetKeys.Contains(row.ExactIdentity))
            {
                result.Add(Blocked(
                    row,
                    Rt03FullConvergenceActions.BlockedAmbiguous,
                    "Exact identity has multiple source or target matches."));
                continue;
            }

            var target = targets.SingleOrDefault(item =>
                string.Equals(
                    item.ExactIdentity,
                    row.ExactIdentity,
                    StringComparison.Ordinal));
            if (target is null)
            {
                result.Add(new(
                    sourceProfileCode,
                    domain,
                    row.ExactIdentity,
                    row.IsActive
                        ? Rt03FullConvergenceActions.Insert
                        : Rt03FullConvergenceActions.MarkSourceInactive,
                    null,
                    null,
                    null));
                continue;
            }

            if (target.IsManualHold)
            {
                result.Add(new(
                    sourceProfileCode,
                    domain,
                    row.ExactIdentity,
                    Rt03FullConvergenceActions.ManualReview,
                    target.TargetId,
                    target.QlhvOwnedHash,
                    "Target is under an explicit manual hold."));
                continue;
            }

            if (!row.IsActive)
            {
                result.Add(new(
                    sourceProfileCode,
                    domain,
                    row.ExactIdentity,
                    domain == Rt03FullConvergenceDomains.Vehicle &&
                    target.HasActiveAssignment
                        ? Rt03FullConvergenceActions.ManualReview
                        : Rt03FullConvergenceActions.MarkSourceInactive,
                    target.TargetId,
                    target.QlhvOwnedHash,
                    target.HasActiveAssignment
                        ? "Assigned vehicle cannot be deactivated automatically."
                        : null));
                continue;
            }

            result.Add(new(
                sourceProfileCode,
                domain,
                row.ExactIdentity,
                !target.IsDeleted &&
                string.Equals(
                    row.SourceOwnedHash,
                    target.SourceOwnedHash,
                    StringComparison.OrdinalIgnoreCase)
                    ? Rt03FullConvergenceActions.NoChange
                    : Rt03FullConvergenceActions.UpdateSourceOwned,
                target.TargetId,
                target.QlhvOwnedHash,
                null));
        }

        var sourceKeys = source
            .Select(row => row.ExactIdentity)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var missing in targets
                     .Where(row =>
                         !string.IsNullOrWhiteSpace(row.ExactIdentity) &&
                         !sourceKeys.Contains(row.ExactIdentity!))
                     .OrderBy(row => row.ExactIdentity, StringComparer.Ordinal))
        {
            var action = !missingSourceLifecycleVerified
                ? Rt03FullConvergenceActions.BlockedDeleteContract
                : domain == Rt03FullConvergenceDomains.Vehicle &&
                  missing.HasActiveAssignment
                    ? Rt03FullConvergenceActions.ManualReview
                    : Rt03FullConvergenceActions.MarkSourceMissing;
            result.Add(new(
                sourceProfileCode,
                domain,
                missing.ExactIdentity!,
                action,
                missing.TargetId,
                missing.QlhvOwnedHash,
                action switch
                {
                    Rt03FullConvergenceActions.BlockedDeleteContract =>
                        "Missing-source lifecycle has not been verified.",
                    Rt03FullConvergenceActions.ManualReview =>
                        "Missing assigned vehicle cannot be hard-deleted.",
                    _ => null,
                }));
        }

        return new(sourceProfileCode, domain, result);
    }

    public static void VerifyQlhvOwnedPreserved(
        Rt03FullConvergenceDomainPlan plan,
        IReadOnlyDictionary<long, string> afterHashes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(afterHashes);
        foreach (var row in plan.Rows.Where(item =>
                     item.TargetId.HasValue &&
                     item.ExpectedQlhvOwnedHash is not null &&
                     !item.IsBlocked))
        {
            if (!afterHashes.TryGetValue(row.TargetId!.Value, out var after) ||
                !string.Equals(
                    row.ExpectedQlhvOwnedHash,
                    after,
                    StringComparison.Ordinal))
            {
                throw new Rt03SafetyException(
                    Rt03Errors.OwnershipProofRejected,
                    "QLHV-owned data changed during full convergence.");
            }
        }
    }

    private static Rt03FullConvergenceRowPlan Blocked(
        Rt03FullConvergenceSourceRow source,
        string action,
        string reason)
        => new(
            source.SourceProfileCode,
            source.Domain,
            source.ExactIdentity,
            action,
            null,
            null,
            reason);

    private static void RequireProfile(string sourceProfileCode)
        => _ = QlhvOperationSourceCatalog.ResolveSourceTypeFromProfile(
            sourceProfileCode);

    private static void RequireDomain(string domain)
    {
        if (!Rt03FullConvergenceDomains.Ordered.Contains(
                domain,
                StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown full-convergence domain.", nameof(domain));
        }
    }
}

public static class Rt03RecoverySessionStatuses
{
    public const string Preparing = "PREPARING";
    public const string Verifying = "VERIFYING";
    public const string Completed = "COMPLETED";
    public const string Blocked = "BLOCKED";
}

public sealed record Rt03RecoverySessionSnapshot(
    Guid RecoveryId,
    string SourceProfileCode,
    Guid SourceDatabaseGuid,
    long CheckpointBefore,
    long AnchorVersion,
    string Status,
    IReadOnlySet<string> CommittedDomains,
    bool VerificationPassed,
    bool MarkerExists,
    long CurrentCheckpoint);

public static class Rt03RecoveryNextActions
{
    public const string ExecuteOrReplayDomains = "EXECUTE_OR_REPLAY_DOMAINS";
    public const string Verify = "VERIFY";
    public const string FinalizeAtomically = "FINALIZE_ATOMICALLY";
    public const string ReplayAfterAnchor = "REPLAY_AFTER_ANCHOR";
    public const string Blocked = "BLOCKED";
}

public static class Rt03RecoveryStateMachine
{
    public static string Next(
        Rt03RecoverySessionSnapshot session,
        string timeHealth,
        bool allDomainsClassified)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!string.Equals(timeHealth, TimeHealthStatuses.Healthy, StringComparison.Ordinal) ||
            !allDomainsClassified ||
            session.AnchorVersion < 0 ||
            session.CheckpointBefore < 0)
        {
            return Rt03RecoveryNextActions.Blocked;
        }

        if (string.Equals(
                session.Status,
                Rt03RecoverySessionStatuses.Completed,
                StringComparison.Ordinal))
        {
            return session.MarkerExists &&
                   session.CurrentCheckpoint == session.AnchorVersion
                ? Rt03RecoveryNextActions.ReplayAfterAnchor
                : Rt03RecoveryNextActions.Blocked;
        }

        if (session.CurrentCheckpoint != session.CheckpointBefore)
        {
            return Rt03RecoveryNextActions.Blocked;
        }

        // Every attempt replays all domains under a fresh source read barrier.
        // A crash may have occurred after a domain commit but before its durable
        // session row was updated, and CT-OFF tables must be reconverged.
        if (!Rt03FullConvergenceDomains.Ordered.All(
                session.CommittedDomains.Contains))
        {
            return Rt03RecoveryNextActions.ExecuteOrReplayDomains;
        }

        if (!session.VerificationPassed)
        {
            return Rt03RecoveryNextActions.Verify;
        }

        return !session.MarkerExists
            ? Rt03RecoveryNextActions.FinalizeAtomically
            : Rt03RecoveryNextActions.Blocked;
    }
}
