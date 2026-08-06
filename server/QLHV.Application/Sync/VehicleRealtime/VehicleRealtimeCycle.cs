using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.VehicleRealtime;

public static class VehicleRealtimeErrorCodes
{
    public const string NotInitialized = "VEHICLE_REALTIME_NOT_INITIALIZED";
    public const string SourceIdentityRejected = "VEHICLE_SOURCE_IDENTITY_REJECTED";
    public const string SourceSchemaMismatch = "VEHICLE_SOURCE_SCHEMA_MISMATCH";
    public const string ChangeTrackingUnavailable = "VEHICLE_CT_UNAVAILABLE";
    public const string ChangeTrackingExpired = "VEHICLE_CT_EXPIRED";
    public const string SourceChangedDuringPlan = "VEHICLE_SOURCE_CHANGED_DURING_PLAN";
    public const string TargetChangedDuringPlan = "VEHICLE_TARGET_CHANGED_DURING_PLAN";
    public const string MappingMismatch = "VEHICLE_MAPPING_MISMATCH";
    public const string UnsafePlan = "VEHICLE_UNSAFE_PLAN";
}

public sealed class VehicleRealtimeSafetyException : InvalidOperationException
{
    public VehicleRealtimeSafetyException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed record VehicleRealtimeCheckpoint(
    string SourceProfileCode,
    Guid SourceDatabaseGuid,
    long LastCtVersion,
    string MappingFingerprint,
    string SourceSchemaFingerprint,
    string State,
    byte[] RowVersion);

public sealed record VehicleSourceCapability(
    string SourceProfileCode,
    string DatabaseName,
    Guid DatabaseGuid,
    bool SnapshotIsolationEnabled,
    bool ChangeTrackingEnabled,
    bool TrackColumnsUpdated,
    long CurrentCtVersion,
    long MinimumValidVersion,
    string SourceSchemaFingerprint);

public sealed record VehicleSourceChange(
    long SourceCtVersion,
    VehicleSourceChangeKind ChangeKind,
    VehicleSourceIdentity Identity,
    VehicleSourceRow? CurrentRow);

public sealed record VehicleSourceBatch(
    VehicleSourceCapability Capability,
    long CheckpointBefore,
    long CheckpointAfter,
    long SealedCurrentVersion,
    IReadOnlyList<VehicleSourceChange> Changes)
{
    public bool HasChanges => Changes.Count != 0;
}

public sealed record VehicleTargetPlanningSnapshot(
    VehicleRealtimeCheckpoint Checkpoint,
    IReadOnlyList<VehicleTargetSnapshot> Vehicles);

public sealed record VehicleRealtimeSealedPlan(
    Guid CycleId,
    string SourceProfileCode,
    Guid SourceDatabaseGuid,
    long CheckpointBefore,
    long CheckpointAfter,
    long SealedSourceVersion,
    string MappingFingerprint,
    string SourceSchemaFingerprint,
    string PlanToken,
    IReadOnlyList<VehicleRealtimePlan> Rows);

public sealed record VehicleRealtimeCycleResult(
    Guid CycleId,
    string SourceProfileCode,
    long CheckpointBefore,
    long CheckpointAfter,
    int InsertedRows,
    int UpdatedRows,
    int InactiveRows,
    int MissingRows,
    int ManualReviewRows,
    int NoChangeRows,
    string PlanToken);

public interface IVehicleRealtimeSourceFeed
{
    /// <summary>
    /// Reads one complete Change Tracking commit version (all XeTap keys sharing
    /// that version) under a source snapshot. An empty batch may advance only to
    /// its sealed current version. It must verify the route database name/GUID,
    /// dbo.XeTap schema, PK and CT minimum-valid version.
    /// </summary>
    Task<VehicleSourceBatch> ReadNextAsync(
        VehicleRealtimeRoute route,
        VehicleRealtimeCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revalidates only keys in the sealed batch. Unrelated learner/course CT
    /// commits must not invalidate a vehicle plan. A later XeTap change to one
    /// of these keys returns false so the cycle retries without checkpoint write.
    /// </summary>
    Task<bool> RevalidateKeysAsync(
        VehicleRealtimeRoute route,
        VehicleSourceBatch batch,
        CancellationToken cancellationToken = default);
}

public interface IVehicleRealtimeTargetStore
{
    Task<VehicleTargetPlanningSnapshot> ReadPlanningSnapshotAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the complete sealed CT version atomically. The store must acquire
    /// the vehicle/profile transaction lock, CAS the checkpoint RowVersion,
    /// re-read every target RowVersion/assignment reference/collision key, apply
    /// only VehicleRealtimeTargetOwnership.SourceOwnedColumns, record every
    /// manual-review/no-change event idempotently, and then advance checkpoint.
    /// </summary>
    Task<VehicleRealtimeCycleResult> CommitAsync(
        VehicleRealtimeSealedPlan plan,
        VehicleTargetPlanningSnapshot expectedTarget,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Database-agnostic vehicle realtime orchestration. It never uses table counts
/// as a guard and never asks Auto Sync to apply a fallback snapshot.
/// </summary>
public sealed class VehicleRealtimeCycleProcessor
{
    private readonly IVehicleRealtimeSourceFeed _source;
    private readonly IVehicleRealtimeTargetStore _target;

    public VehicleRealtimeCycleProcessor(
        IVehicleRealtimeSourceFeed source,
        IVehicleRealtimeTargetStore target)
    {
        _source = source;
        _target = target;
    }

    public async Task<VehicleRealtimeCycleResult> ProcessAsync(
        string sourceProfileCode,
        CancellationToken cancellationToken = default)
    {
        var route = VehicleRealtimeRouteCatalog.GetRequired(sourceProfileCode);
        return await ProcessAsync(route, cancellationToken);
    }

    public async Task<VehicleRealtimeCycleResult> ProcessAsync(
        VehicleRealtimeRoute route,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var mappingFingerprint = VehicleSourceMapper.ComputeMappingFingerprint();
        var target = await _target.ReadPlanningSnapshotAsync(
            route.SourceProfileCode,
            cancellationToken);
        ValidateCheckpoint(route, target.Checkpoint, mappingFingerprint);

        var source = await _source.ReadNextAsync(
            route,
            target.Checkpoint,
            cancellationToken);
        ValidateSource(route, target.Checkpoint, source);

        var plans = BuildPlans(source, target.Vehicles);
        var plan = Seal(route, mappingFingerprint, source, plans);
        if (!await _source.RevalidateKeysAsync(route, source, cancellationToken))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceChangedDuringPlan,
                "A planned dbo.XeTap key changed after its sealed source snapshot.");
        }

        return await _target.CommitAsync(plan, target, cancellationToken);
    }

    public static VehicleRealtimeSealedPlan Seal(
        VehicleRealtimeRoute route,
        string mappingFingerprint,
        VehicleSourceBatch source,
        IReadOnlyList<VehicleRealtimePlan> plans)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plans);
        var cycleId = Guid.NewGuid();
        var token = ComputePlanToken(
            route,
            mappingFingerprint,
            source,
            plans);
        return new VehicleRealtimeSealedPlan(
            cycleId,
            route.SourceProfileCode,
            source.Capability.DatabaseGuid,
            source.CheckpointBefore,
            source.CheckpointAfter,
            source.SealedCurrentVersion,
            mappingFingerprint,
            source.Capability.SourceSchemaFingerprint,
            token,
            plans);
    }

    public static string ComputePlanToken(
        VehicleRealtimeRoute route,
        string mappingFingerprint,
        VehicleSourceBatch source,
        IReadOnlyList<VehicleRealtimePlan> plans)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append("VEHICLE_REALTIME_PLAN_V1");
        Append(route.SourceProfileCode);
        Append(route.SourceDatabaseName);
        Append(source.Capability.DatabaseGuid.ToString("D"));
        Append(source.CheckpointBefore.ToString(CultureInfo.InvariantCulture));
        Append(source.CheckpointAfter.ToString(CultureInfo.InvariantCulture));
        Append(source.SealedCurrentVersion.ToString(CultureInfo.InvariantCulture));
        Append(mappingFingerprint);
        Append(source.Capability.SourceSchemaFingerprint);
        foreach (var row in plans
                     .OrderBy(item => item.SourceCtVersion)
                     .ThenBy(item => item.SourceBienSoXe, StringComparer.OrdinalIgnoreCase))
        {
            Append(row.SourceCtVersion.ToString(CultureInfo.InvariantCulture));
            Append(((int)row.ChangeKind).ToString(CultureInfo.InvariantCulture));
            Append(row.SourceProfileCode);
            Append(row.SourceBienSoXe);
            Append(row.Action);
            Append(row.Lifecycle);
            Append(row.Source?.SourceRowHash);
            Append(row.TargetXeTapId?.ToString(CultureInfo.InvariantCulture));
            Append(row.ExpectedTargetRowVersion is null
                ? null
                : Convert.ToHexString(row.ExpectedTargetRowVersion));
            Append(row.ReviewCode);
            Append(row.CollisionField);
            Append(row.ConflictingXeTapId?.ToString(CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
    }

    private static IReadOnlyList<VehicleRealtimePlan> BuildPlans(
        VehicleSourceBatch source,
        IReadOnlyList<VehicleTargetSnapshot> targetInventory)
    {
        var plans = new List<VehicleRealtimePlan>(source.Changes.Count);
        foreach (var change in source.Changes
                     .OrderBy(item => item.SourceCtVersion)
                     .ThenBy(item => item.Identity.SourceBienSoXe,
                         StringComparer.OrdinalIgnoreCase))
        {
            if (change.SourceCtVersion <= 0)
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.UnsafePlan,
                    "A Change Tracking event must have a positive source version.");
            }

            if (change.SourceCtVersion != source.CheckpointAfter)
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.UnsafePlan,
                    "A vehicle batch must contain exactly one complete CT commit version.");
            }

            VehicleRealtimePlan plan;
            if (change.ChangeKind == VehicleSourceChangeKind.Delete)
            {
                if (change.CurrentRow is not null)
                {
                    throw new VehicleRealtimeSafetyException(
                        VehicleRealtimeErrorCodes.UnsafePlan,
                        "A deleted vehicle CT event unexpectedly carried a current source row.");
                }

                plan = VehicleRealtimePlanner.PlanDelete(
                    change.Identity,
                    targetInventory,
                    change.SourceCtVersion);
            }
            else
            {
                if (change.CurrentRow is null)
                {
                    throw new VehicleRealtimeSafetyException(
                        VehicleRealtimeErrorCodes.UnsafePlan,
                        "A vehicle insert/update CT event is missing its current source row.");
                }

                var mapping = VehicleSourceMapper.Map(
                    change.CurrentRow,
                    change.Identity.SourceProfileCode);
                if (!mapping.IsSafe)
                {
                    var blocker = mapping.Blockers.FirstOrDefault() ??
                                  VehicleRealtimeReviewCodes.InvalidSourceIdentity;
                    var separator = blocker.IndexOf(':');
                    plan = new VehicleRealtimePlan(
                        VehicleSourceChangeKind.Upsert,
                        change.Identity.SourceProfileCode,
                        change.Identity.SourceBienSoXe,
                        change.SourceCtVersion,
                        VehicleRealtimeActions.ManualReview,
                        VehicleRealtimeLifecycles.ManualReview,
                        null,
                        null,
                        null,
                        separator < 0 ? blocker : blocker[..separator],
                        separator < 0 ? null : blocker[(separator + 1)..],
                        null);
                }
                else
                {
                    plan = VehicleRealtimePlanner.PlanUpsert(
                        mapping,
                        targetInventory,
                        change.SourceCtVersion);
                }

                if (mapping.Model is not null &&
                    !string.Equals(
                        mapping.Model.Identity.SourceBienSoXe,
                        change.Identity.SourceBienSoXe,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new VehicleRealtimeSafetyException(
                        VehicleRealtimeErrorCodes.SourceIdentityRejected,
                        "The CT key and current dbo.XeTap primary key do not match.");
                }
            }

            plans.Add(plan);
        }

        ApplyWithinBatchSecondaryCollisionGuards(plans);
        return plans;
    }

    private static void ApplyWithinBatchSecondaryCollisionGuards(
        IList<VehicleRealtimePlan> plans)
    {
        var safeUpserts = plans
            .Select((plan, index) => (plan, index))
            .Where(item =>
                item.plan.Source is not null &&
                !item.plan.RequiresManualReview)
            .ToArray();
        Guard(
            safeUpserts,
            model => model.NormalizedBienSoXe,
            VehicleRealtimeReviewCodes.PlateCollision,
            VehicleRealtimeCollisionFields.BienSoXe);
        Guard(
            safeUpserts,
            model => model.NormalizedSoDK,
            VehicleRealtimeReviewCodes.RegistrationCollision,
            VehicleRealtimeCollisionFields.SoDK);
        Guard(
            safeUpserts,
            model => model.NormalizedSoKhung,
            VehicleRealtimeReviewCodes.ChassisCollision,
            VehicleRealtimeCollisionFields.SoKhung);
        Guard(
            safeUpserts,
            model => model.NormalizedSoDongCo,
            VehicleRealtimeReviewCodes.EngineCollision,
            VehicleRealtimeCollisionFields.SoDongCo);

        void Guard(
            IReadOnlyCollection<(VehicleRealtimePlan plan, int index)> candidates,
            Func<VehicleSourceWriteModel, string?> key,
            string reviewCode,
            string field)
        {
            foreach (var group in candidates
                         .Where(item => key(item.plan.Source!) is not null)
                         .GroupBy(item => key(item.plan.Source!)!, StringComparer.Ordinal)
                         .Where(group => group
                             .Select(item => item.plan.SourceBienSoXe)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Skip(1)
                             .Any()))
            {
                foreach (var item in group)
                {
                    plans[item.index] = item.plan with
                    {
                        Action = VehicleRealtimeActions.ManualReview,
                        Lifecycle = VehicleRealtimeLifecycles.ManualReview,
                        ReviewCode = reviewCode,
                        CollisionField = field,
                    };
                }
            }
        }
    }

    private static void ValidateCheckpoint(
        VehicleRealtimeRoute route,
        VehicleRealtimeCheckpoint checkpoint,
        string mappingFingerprint)
    {
        if (!string.Equals(
                checkpoint.SourceProfileCode,
                route.SourceProfileCode,
                StringComparison.Ordinal) ||
            checkpoint.SourceDatabaseGuid != route.ExpectedProductionDatabaseGuid ||
            checkpoint.LastCtVersion < 0 ||
            !string.Equals(checkpoint.State, "ACTIVE", StringComparison.Ordinal))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.NotInitialized,
                "Vehicle realtime checkpoint is absent or not the exact active production registration.");
        }

        if (!string.Equals(
                checkpoint.MappingFingerprint,
                mappingFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.MappingMismatch,
                "Vehicle realtime mapping fingerprint does not match its sealed checkpoint.");
        }

        if (string.IsNullOrWhiteSpace(checkpoint.SourceSchemaFingerprint))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceSchemaMismatch,
                "Vehicle realtime checkpoint has no sealed source schema fingerprint.");
        }
    }

    private static void ValidateSource(
        VehicleRealtimeRoute route,
        VehicleRealtimeCheckpoint checkpoint,
        VehicleSourceBatch source)
    {
        var capability = source.Capability;
        if (!string.Equals(
                capability.SourceProfileCode,
                route.SourceProfileCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                capability.DatabaseName,
                route.SourceDatabaseName,
                StringComparison.Ordinal) ||
            capability.DatabaseGuid != route.ExpectedProductionDatabaseGuid ||
            capability.DatabaseGuid != checkpoint.SourceDatabaseGuid)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceIdentityRejected,
                "Vehicle source database identity does not match the fixed live route.");
        }

        if (!capability.SnapshotIsolationEnabled ||
            !capability.ChangeTrackingEnabled ||
            !capability.TrackColumnsUpdated)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.ChangeTrackingUnavailable,
                "dbo.XeTap Change Tracking and source snapshot isolation are required.");
        }

        if (!string.Equals(
                capability.SourceSchemaFingerprint,
                checkpoint.SourceSchemaFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.SourceSchemaMismatch,
                "dbo.XeTap schema does not match the sealed checkpoint fingerprint.");
        }

        if (checkpoint.LastCtVersion < capability.MinimumValidVersion)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.ChangeTrackingExpired,
                "Vehicle checkpoint is older than CHANGE_TRACKING_MIN_VALID_VERSION.");
        }

        if (source.CheckpointBefore != checkpoint.LastCtVersion ||
            source.CheckpointAfter < source.CheckpointBefore ||
            source.CheckpointAfter > source.SealedCurrentVersion ||
            source.SealedCurrentVersion > capability.CurrentCtVersion)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Vehicle source batch has an invalid sealed CT window.");
        }
    }
}
