using System.Security.Cryptography;

namespace QLHV.Application.Sync.Realtime.ControlPlane;

public enum SourceMembershipStatus
{
    InsertPending,
    Active,
    DeletePending,
    Inactive,
    ReactivatePending,
    Conflict,
}

public enum SourceMembershipTargetAction
{
    None,
    Upserted,
    ExistingVerified,
    HardDeleted,
    PreservedExcluded,
}

public enum SourceMembershipReasonCode
{
    None,
    SourcePresent,
    SourceDelete,
    FullReconcileAbsent,
    ReactivatedAtSource,
    TargetActionApplied,
    DuplicateReplay,
    LateSourceEvent,
    StreamOwnershipConflict,
    MappingFingerprintMismatch,
    RouteFingerprintMismatch,
    TargetEqualityUnproven,
    BootstrapIncomplete,
    BootstrapMembershipCreated,
    BootstrapMembershipVerified,
    SourceRowObserved,
    CtDeleteObserved,
    DeletePendingNotApplied,
    ReactivationCandidate,
    TargetOnlyUnclassified,
    OwnershipConflict,
    CoverageComplete,
    CheckpointConflict,
    BootstrapParentMissing,
    DeleteExecutionNotEnabled,
    UnownedDeleteKey,
    BlockDeleteConflict,
    ManualConflict,
}

public enum MembershipTransitionOutcome
{
    Applied,
    IdempotentNoOp,
    Conflict,
}

public sealed class ControlPlaneFingerprint : IEquatable<ControlPlaneFingerprint>
{
    private readonly byte[] _bytes;

    public ControlPlaneFingerprint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("Fingerprint must be exactly 32 bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public byte[] ToArray() => _bytes.ToArray();

    public bool Equals(ControlPlaneFingerprint? other)
        => other is not null &&
           CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);

    public override bool Equals(object? obj) => Equals(obj as ControlPlaneFingerprint);

    public override int GetHashCode()
        => HashCode.Combine(_bytes[0], _bytes[1], _bytes[^2], _bytes[^1]);

    public override string ToString() => "ControlPlaneFingerprint(Sha256=redacted)";
}

public sealed record SourceMembershipState(
    SourceMembershipStatus Status,
    bool IsActive,
    bool ClaimsTargetKey,
    bool OwnershipReserved,
    long LastObservedSourceVersion,
    long? AppliedSourceVersion,
    long? DeletedAtSourceVersion,
    long? ReactivatedAtSourceVersion,
    SourceMembershipTargetAction TargetAction,
    SourceMembershipReasonCode ReasonCode,
    ControlPlaneFingerprint MappingFingerprint,
    ControlPlaneFingerprint RouteFingerprint)
{
    public override string ToString()
        => $"SourceMembershipState(Status={Status}, Active={IsActive}, Claims={ClaimsTargetKey}, " +
           $"OwnershipReserved={OwnershipReserved}, LastObserved={LastObservedSourceVersion})";
}

public sealed record MembershipTransitionResult(
    SourceMembershipState State,
    MembershipTransitionOutcome Outcome);

public static class SourceMembershipStateMachine
{
    public static bool CanTransition(
        SourceMembershipStatus? before,
        SourceMembershipStatus after)
    {
        if (after == SourceMembershipStatus.Conflict)
        {
            return true;
        }

        return (before, after) switch
        {
            (null, SourceMembershipStatus.InsertPending) => true,
            (SourceMembershipStatus.InsertPending, SourceMembershipStatus.Active) => true,
            (SourceMembershipStatus.Active, SourceMembershipStatus.Active) => true,
            (SourceMembershipStatus.Active, SourceMembershipStatus.DeletePending) => true,
            (SourceMembershipStatus.DeletePending, SourceMembershipStatus.Inactive) => true,
            (SourceMembershipStatus.Inactive, SourceMembershipStatus.Inactive) => true,
            (SourceMembershipStatus.Inactive, SourceMembershipStatus.ReactivatePending) => true,
            (SourceMembershipStatus.ReactivatePending, SourceMembershipStatus.Active) => true,
            _ => false,
        };
    }

    public static SourceMembershipState CreateInsertPending(
        long sourceVersion,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint)
    {
        RequireVersion(sourceVersion);
        ArgumentNullException.ThrowIfNull(mappingFingerprint);
        ArgumentNullException.ThrowIfNull(routeFingerprint);
        return new SourceMembershipState(
            SourceMembershipStatus.InsertPending,
            IsActive: false,
            ClaimsTargetKey: true,
            OwnershipReserved: true,
            LastObservedSourceVersion: sourceVersion,
            AppliedSourceVersion: null,
            DeletedAtSourceVersion: null,
            ReactivatedAtSourceVersion: null,
            SourceMembershipTargetAction.None,
            SourceMembershipReasonCode.SourcePresent,
            mappingFingerprint,
            routeFingerprint);
    }

    public static MembershipTransitionResult ApplyActive(SourceMembershipState current)
    {
        RequireState(current, SourceMembershipStatus.InsertPending);
        return Applied(current with
        {
            Status = SourceMembershipStatus.Active,
            IsActive = true,
            ClaimsTargetKey = true,
            AppliedSourceVersion = current.LastObservedSourceVersion,
            TargetAction = SourceMembershipTargetAction.Upserted,
            ReasonCode = SourceMembershipReasonCode.TargetActionApplied,
        });
    }

    public static MembershipTransitionResult ObserveActive(
        SourceMembershipState current,
        long sourceVersion,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint)
    {
        if (current.Status != SourceMembershipStatus.Active)
        {
            throw InvalidTransition(current.Status, SourceMembershipStatus.Active);
        }

        var fingerprintConflict = CheckFingerprints(
            current,
            mappingFingerprint,
            routeFingerprint);
        if (fingerprintConflict is not null)
        {
            return fingerprintConflict;
        }

        RequireVersion(sourceVersion);
        if (sourceVersion <= current.LastObservedSourceVersion)
        {
            return NoOp(current, SourceMembershipReasonCode.DuplicateReplay);
        }

        return Applied(current with
        {
            LastObservedSourceVersion = sourceVersion,
            AppliedSourceVersion = sourceVersion,
            TargetAction = SourceMembershipTargetAction.Upserted,
            ReasonCode = SourceMembershipReasonCode.SourcePresent,
        });
    }

    public static MembershipTransitionResult CreateDeletePending(
        SourceMembershipState current,
        long sourceVersion,
        SourceMembershipReasonCode reasonCode,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint)
    {
        RequireVersion(sourceVersion);
        if (reasonCode is not (
                SourceMembershipReasonCode.SourceDelete or
                SourceMembershipReasonCode.FullReconcileAbsent))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        var fingerprintConflict = CheckFingerprints(
            current,
            mappingFingerprint,
            routeFingerprint);
        if (fingerprintConflict is not null)
        {
            return fingerprintConflict;
        }

        var newestAppliedPresence = Math.Max(
            current.AppliedSourceVersion ?? -1,
            current.ReactivatedAtSourceVersion ?? -1);
        if (sourceVersion <= newestAppliedPresence ||
            sourceVersion <= current.LastObservedSourceVersion)
        {
            return NoOp(current, SourceMembershipReasonCode.LateSourceEvent);
        }

        if (current.Status == SourceMembershipStatus.Inactive)
        {
            return Applied(current with
            {
                LastObservedSourceVersion = sourceVersion,
                AppliedSourceVersion = sourceVersion,
                DeletedAtSourceVersion = sourceVersion,
                ReasonCode = reasonCode,
            });
        }

        if (current.Status != SourceMembershipStatus.Active)
        {
            throw InvalidTransition(current.Status, SourceMembershipStatus.DeletePending);
        }

        return Applied(current with
        {
            Status = SourceMembershipStatus.DeletePending,
            IsActive = false,
            ClaimsTargetKey = false,
            LastObservedSourceVersion = sourceVersion,
            TargetAction = SourceMembershipTargetAction.None,
            ReasonCode = reasonCode,
        });
    }

    public static MembershipTransitionResult ApplyInactive(
        SourceMembershipState current,
        SourceMembershipTargetAction targetAction)
    {
        RequireState(current, SourceMembershipStatus.DeletePending);
        if (targetAction is not (
                SourceMembershipTargetAction.HardDeleted or
                SourceMembershipTargetAction.PreservedExcluded))
        {
            throw new ArgumentOutOfRangeException(nameof(targetAction));
        }

        return Applied(current with
        {
            Status = SourceMembershipStatus.Inactive,
            IsActive = false,
            ClaimsTargetKey = false,
            OwnershipReserved = true,
            AppliedSourceVersion = current.LastObservedSourceVersion,
            DeletedAtSourceVersion = current.LastObservedSourceVersion,
            TargetAction = targetAction,
            ReasonCode = SourceMembershipReasonCode.TargetActionApplied,
        });
    }

    public static MembershipTransitionResult CreateReactivatePending(
        SourceMembershipState current,
        long sourceVersion,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint)
    {
        RequireVersion(sourceVersion);
        var fingerprintConflict = CheckFingerprints(
            current,
            mappingFingerprint,
            routeFingerprint);
        if (fingerprintConflict is not null)
        {
            return fingerprintConflict;
        }

        var newestApplied = Math.Max(
            current.AppliedSourceVersion ?? -1,
            current.ReactivatedAtSourceVersion ?? -1);
        if (sourceVersion <= newestApplied ||
            sourceVersion <= current.LastObservedSourceVersion)
        {
            return NoOp(current, SourceMembershipReasonCode.DuplicateReplay);
        }

        if (current.Status != SourceMembershipStatus.Inactive)
        {
            throw InvalidTransition(current.Status, SourceMembershipStatus.ReactivatePending);
        }

        return Applied(current with
        {
            Status = SourceMembershipStatus.ReactivatePending,
            IsActive = false,
            ClaimsTargetKey = true,
            LastObservedSourceVersion = sourceVersion,
            TargetAction = SourceMembershipTargetAction.None,
            ReasonCode = SourceMembershipReasonCode.ReactivatedAtSource,
        });
    }

    public static MembershipTransitionResult ApplyReactivated(SourceMembershipState current)
    {
        RequireState(current, SourceMembershipStatus.ReactivatePending);
        return Applied(current with
        {
            Status = SourceMembershipStatus.Active,
            IsActive = true,
            ClaimsTargetKey = true,
            OwnershipReserved = true,
            AppliedSourceVersion = current.LastObservedSourceVersion,
            DeletedAtSourceVersion = null,
            ReactivatedAtSourceVersion = current.LastObservedSourceVersion,
            TargetAction = SourceMembershipTargetAction.Upserted,
            ReasonCode = SourceMembershipReasonCode.TargetActionApplied,
        });
    }

    public static MembershipTransitionResult MarkConflict(
        SourceMembershipState current,
        SourceMembershipReasonCode reasonCode)
    {
        if (reasonCode is not (
                SourceMembershipReasonCode.StreamOwnershipConflict or
                SourceMembershipReasonCode.MappingFingerprintMismatch or
                SourceMembershipReasonCode.RouteFingerprintMismatch or
                SourceMembershipReasonCode.TargetEqualityUnproven or
                SourceMembershipReasonCode.BootstrapIncomplete or
                SourceMembershipReasonCode.UnownedDeleteKey or
                SourceMembershipReasonCode.BlockDeleteConflict or
                SourceMembershipReasonCode.ManualConflict))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCode));
        }

        return new MembershipTransitionResult(
            current with
            {
                Status = SourceMembershipStatus.Conflict,
                IsActive = false,
                ClaimsTargetKey = false,
                OwnershipReserved = true,
                TargetAction = SourceMembershipTargetAction.None,
                ReasonCode = reasonCode,
            },
            MembershipTransitionOutcome.Conflict);
    }

    public static bool IsDifferentStreamOwnershipConflict(
        OwnershipReservation existing,
        MembershipRoute requested)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(requested);
        return !string.Equals(existing.TargetProfile, requested.TargetProfile, StringComparison.Ordinal) ||
               !string.Equals(existing.SourceProfile, requested.SourceProfile, StringComparison.Ordinal) ||
               !string.Equals(existing.StreamCode, requested.StreamCode, StringComparison.Ordinal) ||
               !string.Equals(existing.TableName, requested.TableName, StringComparison.Ordinal);
    }

    private static MembershipTransitionResult? CheckFingerprints(
        SourceMembershipState current,
        ControlPlaneFingerprint mappingFingerprint,
        ControlPlaneFingerprint routeFingerprint)
    {
        ArgumentNullException.ThrowIfNull(mappingFingerprint);
        ArgumentNullException.ThrowIfNull(routeFingerprint);
        if (!current.MappingFingerprint.Equals(mappingFingerprint))
        {
            return MarkConflict(
                current,
                SourceMembershipReasonCode.MappingFingerprintMismatch);
        }

        if (!current.RouteFingerprint.Equals(routeFingerprint))
        {
            return MarkConflict(
                current,
                SourceMembershipReasonCode.RouteFingerprintMismatch);
        }

        return null;
    }

    private static MembershipTransitionResult Applied(SourceMembershipState state)
        => new(state, MembershipTransitionOutcome.Applied);

    private static MembershipTransitionResult NoOp(
        SourceMembershipState state,
        SourceMembershipReasonCode reason)
        => new(state with { ReasonCode = reason }, MembershipTransitionOutcome.IdempotentNoOp);

    private static void RequireVersion(long sourceVersion)
    {
        if (sourceVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceVersion));
        }
    }

    private static void RequireState(
        SourceMembershipState current,
        SourceMembershipStatus required)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Status != required)
        {
            throw InvalidTransition(current.Status, required);
        }
    }

    private static InvalidOperationException InvalidTransition(
        SourceMembershipStatus from,
        SourceMembershipStatus to)
        => new($"Invalid source membership transition from {from} to {to}.");
}

public sealed record MembershipRoute(
    string TargetProfile,
    string SourceProfile,
    string StreamCode,
    string MaCsdt,
    string TableName)
{
    public override string ToString()
        => $"MembershipRoute(Target={TargetProfile}, Source={SourceProfile}, Stream={StreamCode}, Table={TableName})";
}

public sealed record OwnershipReservation(
    long MembershipId,
    string TargetProfile,
    string SourceProfile,
    string StreamCode,
    string MaCsdt,
    string TableName,
    bool OwnershipReserved);
