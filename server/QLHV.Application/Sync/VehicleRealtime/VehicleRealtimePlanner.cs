namespace QLHV.Application.Sync.VehicleRealtime;

public static class VehicleRealtimePlanner
{
    public static VehicleRealtimePlan PlanUpsert(
        VehicleMappingResult mapping,
        IReadOnlyCollection<VehicleTargetSnapshot> targetInventory,
        long sourceCtVersion)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(targetInventory);
        RequireVersion(sourceCtVersion);

        if (!mapping.IsSafe)
        {
            var code = mapping.Blockers.FirstOrDefault() ??
                       VehicleRealtimeReviewCodes.InvalidSourceIdentity;
            var separator = code.IndexOf(':');
            return Review(
                VehicleSourceChangeKind.Upsert,
                sourceProfileCode: mapping.Model?.Identity.SourceProfileCode ?? string.Empty,
                sourceBienSoXe: mapping.Model?.Identity.SourceBienSoXe ?? string.Empty,
                sourceCtVersion,
                mapping.Model,
                target: null,
                separator < 0 ? code : code[..separator],
                separator < 0 ? null : code[(separator + 1)..],
                conflictingTarget: null);
        }

        var source = mapping.Model!;
        var exactTargets = targetInventory
            .Where(target => IsExactIdentity(target, source.Identity))
            .ToArray();
        if (exactTargets.Length > 1)
        {
            return Review(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                source,
                exactTargets[0],
                VehicleRealtimeReviewCodes.TargetIdentityAmbiguous,
                VehicleRealtimeCollisionFields.BienSoXe,
                exactTargets[1]);
        }

        var target = exactTargets.SingleOrDefault();
        if (target is { IsDeleted: true })
        {
            return Review(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                source,
                target,
                VehicleRealtimeReviewCodes.TargetSoftDeleted,
                null,
                target);
        }

        if (target is not null && IsManualHold(target))
        {
            return Review(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                source,
                target,
                VehicleRealtimeReviewCodes.TargetManualHold,
                null,
                target);
        }

        var collision = FindCollision(source, targetInventory, target?.XeTapId);
        if (collision is not null)
        {
            return Review(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                source,
                target,
                collision.Value.Code,
                collision.Value.Field,
                collision.Value.Target);
        }

        if (!source.SourceTrangThai && target is { HasActiveAssignments: true })
        {
            return Review(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                source,
                target,
                VehicleRealtimeReviewCodes.SourceInactiveWithAssignment,
                null,
                target);
        }

        if (target is null)
        {
            return new VehicleRealtimePlan(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                VehicleRealtimeActions.InsertSourceRow,
                source.SourceLifecycle,
                source,
                null,
                null,
                null,
                null,
                null);
        }

        var hashMatches = string.Equals(
            source.SourceRowHash,
            target.SourceRowHash,
            StringComparison.OrdinalIgnoreCase);
        var lifecycleMatches = string.Equals(
            source.SourceLifecycle,
            target.SourceLifecycle,
            StringComparison.Ordinal);
        if (hashMatches &&
            lifecycleMatches &&
            target.SourceTrangThai == source.SourceTrangThai)
        {
            return new VehicleRealtimePlan(
                VehicleSourceChangeKind.Upsert,
                source.Identity.SourceProfileCode,
                source.Identity.SourceBienSoXe,
                sourceCtVersion,
                VehicleRealtimeActions.NoChange,
                source.SourceLifecycle,
                source,
                target.XeTapId,
                target.RowVersion.ToArray(),
                null,
                null,
                null);
        }

        return new VehicleRealtimePlan(
            VehicleSourceChangeKind.Upsert,
            source.Identity.SourceProfileCode,
            source.Identity.SourceBienSoXe,
            sourceCtVersion,
            source.SourceTrangThai
                ? VehicleRealtimeActions.UpdateSourceOwnedFields
                : VehicleRealtimeActions.MarkSourceInactive,
            source.SourceLifecycle,
            source,
            target.XeTapId,
            target.RowVersion.ToArray(),
            null,
            null,
            null);
    }

    public static VehicleRealtimePlan PlanDelete(
        VehicleSourceIdentity identity,
        IReadOnlyCollection<VehicleTargetSnapshot> targetInventory,
        long sourceCtVersion)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(targetInventory);
        VehicleRealtimeRouteCatalog.GetRequired(identity.SourceProfileCode);
        RequireVersion(sourceCtVersion);

        var exactTargets = targetInventory
            .Where(target => IsExactIdentity(target, identity))
            .ToArray();
        if (exactTargets.Length > 1)
        {
            return Review(
                VehicleSourceChangeKind.Delete,
                identity.SourceProfileCode,
                identity.SourceBienSoXe,
                sourceCtVersion,
                source: null,
                exactTargets[0],
                VehicleRealtimeReviewCodes.TargetIdentityAmbiguous,
                VehicleRealtimeCollisionFields.BienSoXe,
                exactTargets[1]);
        }

        var target = exactTargets.SingleOrDefault();
        if (target is null || target.IsDeleted ||
            string.Equals(
                target.SourceLifecycle,
                VehicleRealtimeLifecycles.SourceMissing,
                StringComparison.Ordinal))
        {
            return new VehicleRealtimePlan(
                VehicleSourceChangeKind.Delete,
                identity.SourceProfileCode,
                identity.SourceBienSoXe,
                sourceCtVersion,
                VehicleRealtimeActions.NoChange,
                target?.SourceLifecycle,
                null,
                target?.XeTapId,
                target?.RowVersion.ToArray(),
                null,
                null,
                null);
        }

        if (target.HasActiveAssignments)
        {
            return Review(
                VehicleSourceChangeKind.Delete,
                identity.SourceProfileCode,
                identity.SourceBienSoXe,
                sourceCtVersion,
                source: null,
                target,
                VehicleRealtimeReviewCodes.SourceMissingWithAssignment,
                null,
                target);
        }

        if (IsManualHold(target))
        {
            return Review(
                VehicleSourceChangeKind.Delete,
                identity.SourceProfileCode,
                identity.SourceBienSoXe,
                sourceCtVersion,
                source: null,
                target,
                VehicleRealtimeReviewCodes.TargetManualHold,
                null,
                target);
        }

        return new VehicleRealtimePlan(
            VehicleSourceChangeKind.Delete,
            identity.SourceProfileCode,
            identity.SourceBienSoXe,
            sourceCtVersion,
            VehicleRealtimeActions.MarkSourceMissing,
            VehicleRealtimeLifecycles.SourceMissing,
            null,
            target.XeTapId,
            target.RowVersion.ToArray(),
            null,
            null,
            null);
    }

    private static (
        string Code,
        string Field,
        VehicleTargetSnapshot Target)? FindCollision(
        VehicleSourceWriteModel source,
        IEnumerable<VehicleTargetSnapshot> targets,
        long? exactTargetId)
    {
        foreach (var target in targets.OrderBy(item => item.XeTapId))
        {
            if (target.XeTapId == exactTargetId)
            {
                continue;
            }

            if (EqualKey(source.NormalizedBienSoXe, target.NormalizedBienSoXe) ||
                EqualKey(
                    source.NormalizedBienSoXe,
                    VehicleSourceNormalizer.NormalizePlateCollisionKey(target.BienSoXe)))
            {
                var crossProfile = !string.Equals(
                    source.Identity.SourceProfileCode,
                    target.SourceProfileCode,
                    StringComparison.Ordinal);
                return (
                    crossProfile
                        ? VehicleRealtimeReviewCodes.CrossProfilePlateCollision
                        : VehicleRealtimeReviewCodes.PlateCollision,
                    VehicleRealtimeCollisionFields.BienSoXe,
                    target);
            }

            if (EqualKey(source.NormalizedSoDK, Normalized(
                    target.NormalizedSoDK,
                    target.SoDK)))
            {
                return (
                    VehicleRealtimeReviewCodes.RegistrationCollision,
                    VehicleRealtimeCollisionFields.SoDK,
                    target);
            }

            if (EqualKey(source.NormalizedSoKhung, Normalized(
                    target.NormalizedSoKhung,
                    target.SoKhung)))
            {
                return (
                    VehicleRealtimeReviewCodes.ChassisCollision,
                    VehicleRealtimeCollisionFields.SoKhung,
                    target);
            }

            if (EqualKey(source.NormalizedSoDongCo, Normalized(
                    target.NormalizedSoDongCo,
                    target.SoDongCo)))
            {
                return (
                    VehicleRealtimeReviewCodes.EngineCollision,
                    VehicleRealtimeCollisionFields.SoDongCo,
                    target);
            }
        }

        return null;
    }

    private static string? Normalized(string? stored, string? raw)
        => stored ?? VehicleSourceNormalizer.NormalizeSecondaryCollisionKey(raw);

    private static bool EqualKey(string? left, string? right)
        => left is not null &&
           right is not null &&
           string.Equals(left, right, StringComparison.Ordinal);

    private static bool IsExactIdentity(
        VehicleTargetSnapshot target,
        VehicleSourceIdentity identity)
        => string.Equals(
               target.SourceProfileCode,
               identity.SourceProfileCode,
               StringComparison.Ordinal) &&
           string.Equals(
               target.SourceBienSoXe?.Trim(),
               identity.SourceBienSoXe,
               StringComparison.OrdinalIgnoreCase);

    private static bool IsManualHold(VehicleTargetSnapshot target)
        => string.Equals(
               target.SourceLifecycle,
               VehicleRealtimeLifecycles.ManualReview,
               StringComparison.Ordinal) ||
           !string.IsNullOrWhiteSpace(target.ManualReviewCode);

    private static VehicleRealtimePlan Review(
        VehicleSourceChangeKind changeKind,
        string sourceProfileCode,
        string sourceBienSoXe,
        long sourceCtVersion,
        VehicleSourceWriteModel? source,
        VehicleTargetSnapshot? target,
        string reviewCode,
        string? collisionField,
        VehicleTargetSnapshot? conflictingTarget)
        => new(
            changeKind,
            sourceProfileCode,
            sourceBienSoXe,
            sourceCtVersion,
            VehicleRealtimeActions.ManualReview,
            VehicleRealtimeLifecycles.ManualReview,
            source,
            target?.XeTapId,
            target?.RowVersion.ToArray(),
            reviewCode,
            collisionField,
            conflictingTarget?.XeTapId);

    private static void RequireVersion(long sourceCtVersion)
    {
        if (sourceCtVersion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceCtVersion),
                "Vehicle convergence requires a non-negative sealed source version.");
        }
    }
}
