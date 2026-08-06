namespace QLHV.Application.Sync.Realtime.ControlPlane;

public enum SyncCycleStatus
{
    Preparing,
    Staged,
    Validated,
    TargetCommitting,
    TargetCommitted,
    CheckpointPublished,
    Complete,
    Failed,
    Conflict,
}

public enum SyncCycleDomainStatus
{
    Pending,
    Staged,
    Validated,
    Committed,
    Failed,
    Conflict,
    Skipped,
}

public static class SyncCycleStateMachine
{
    public static bool CanTransition(SyncCycleStatus before, SyncCycleStatus after)
    {
        if (before == after)
        {
            return true;
        }

        if (after is SyncCycleStatus.Failed or SyncCycleStatus.Conflict)
        {
            return before is not (
                SyncCycleStatus.Complete or
                SyncCycleStatus.Failed or
                SyncCycleStatus.Conflict);
        }

        return (before, after) switch
        {
            (SyncCycleStatus.Preparing, SyncCycleStatus.Staged) => true,
            (SyncCycleStatus.Staged, SyncCycleStatus.Validated) => true,
            (SyncCycleStatus.Validated, SyncCycleStatus.TargetCommitting) => true,
            (SyncCycleStatus.TargetCommitting, SyncCycleStatus.TargetCommitted) => true,
            (SyncCycleStatus.TargetCommitted, SyncCycleStatus.CheckpointPublished) => true,
            (SyncCycleStatus.CheckpointPublished, SyncCycleStatus.Complete) => true,
            _ => false,
        };
    }

    public static SyncCycleStatus Transition(
        SyncCycleStatus before,
        SyncCycleStatus after)
        => CanTransition(before, after)
            ? after
            : throw new InvalidOperationException(
                $"Invalid sync cycle transition from {before} to {after}.");
}

public sealed record StreamCoverageState(
    MembershipRoute Route,
    long BaselineSourceVersion,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint,
    ControlPlaneFingerprint SourceKeySetHash,
    long MembershipCount,
    bool IsComplete,
    Guid? CompletedCycleId,
    DateTimeOffset? CompletedAtUtc,
    ControlPlaneFingerprint? SourceSchemaFingerprint = null,
    ControlPlaneFingerprint? TargetSchemaFingerprint = null)
{
    public bool AllowsDeleteReconciliation(
        ControlPlaneFingerprint expectedMappingFingerprint,
        ControlPlaneFingerprint expectedRouteFingerprint)
        => IsComplete &&
           CompletedCycleId.HasValue &&
           CompletedAtUtc.HasValue &&
           BaselineSourceVersion >= 0 &&
           MembershipCount >= 0 &&
           MappingFingerprint.Equals(expectedMappingFingerprint) &&
           RouteFingerprint.Equals(expectedRouteFingerprint);

    public bool AllowsReconciliation(
        ControlPlaneFingerprint expectedMappingFingerprint,
        ControlPlaneFingerprint expectedRouteFingerprint,
        ControlPlaneFingerprint expectedSourceSchemaFingerprint,
        ControlPlaneFingerprint expectedTargetSchemaFingerprint)
        => AllowsDeleteReconciliation(
               expectedMappingFingerprint,
               expectedRouteFingerprint) &&
           SourceSchemaFingerprint?.Equals(expectedSourceSchemaFingerprint) == true &&
           TargetSchemaFingerprint?.Equals(expectedTargetSchemaFingerprint) == true;
}
