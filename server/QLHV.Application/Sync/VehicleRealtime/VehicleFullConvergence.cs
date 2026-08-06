using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QLHV.Application.Sync.VehicleRealtime;

public sealed record VehicleFullConvergencePlan(
    Guid RecoveryId,
    string SourceProfileCode,
    Guid SourceDatabaseGuid,
    long AnchorVersion,
    string MappingFingerprint,
    string SourceSchemaFingerprint,
    string PlanToken,
    IReadOnlyList<VehicleRealtimePlan> Rows);

public sealed record VehicleFullConvergenceResult(
    Guid RecoveryId,
    string SourceProfileCode,
    long AnchorVersion,
    int SourceRows,
    int InsertedRows,
    int UpdatedRows,
    int InactiveRows,
    int MissingRows,
    int ManualReviewRows,
    int NoChangeRows,
    string PlanToken);

public interface IVehicleFullConvergenceTargetStore
{
    Task<IReadOnlyList<VehicleTargetSnapshot>> ReadInventoryAsync(
        CancellationToken cancellationToken = default);

    Task<VehicleFullConvergenceResult> CommitAsync(
        VehicleFullConvergencePlan plan,
        IReadOnlyList<VehicleTargetSnapshot> expectedInventory,
        CancellationToken cancellationToken = default);
}

public static class VehicleFullConvergencePlanner
{
    public static VehicleFullConvergencePlan Build(
        Guid recoveryId,
        VehicleRealtimeRoute route,
        Guid sourceDatabaseGuid,
        long anchorVersion,
        string sourceSchemaFingerprint,
        IReadOnlyCollection<VehicleSourceRow> sourceRows,
        IReadOnlyList<VehicleTargetSnapshot> targetInventory)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(targetInventory);
        if (recoveryId == Guid.Empty ||
            sourceDatabaseGuid != route.ExpectedProductionDatabaseGuid ||
            anchorVersion < 0 ||
            string.IsNullOrWhiteSpace(sourceSchemaFingerprint))
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Vehicle full-convergence identity/anchor is invalid.");
        }

        var duplicateTarget = targetInventory
            .Where(row =>
                string.Equals(
                    row.SourceProfileCode,
                    route.SourceProfileCode,
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(row.SourceBienSoXe))
            .GroupBy(
                row => row.SourceBienSoXe!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicateTarget is not null)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Vehicle full convergence found an ambiguous exact target identity.");
        }

        var mapped = new List<VehicleSourceWriteModel>(sourceRows.Count);
        foreach (var source in sourceRows)
        {
            var result = VehicleSourceMapper.Map(source, route.SourceProfileCode);
            if (!result.IsSafe)
            {
                throw new VehicleRealtimeSafetyException(
                    VehicleRealtimeErrorCodes.MappingMismatch,
                    result.Blockers.FirstOrDefault() ??
                    "Vehicle source mapping is unclassified.");
            }

            mapped.Add(result.Model!);
        }

        var duplicateSource = mapped
            .GroupBy(
                row => row.Identity.SourceBienSoXe,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicateSource is not null)
        {
            throw new VehicleRealtimeSafetyException(
                VehicleRealtimeErrorCodes.UnsafePlan,
                "Vehicle full convergence found duplicate source identities.");
        }

        var plans = mapped
            .OrderBy(row => row.Identity.SourceBienSoXe, StringComparer.OrdinalIgnoreCase)
            .Select(row => VehicleRealtimePlanner.PlanUpsert(
                new VehicleMappingResult(
                    row,
                    Array.Empty<string>(),
                    Array.Empty<string>()),
                targetInventory,
                anchorVersion))
            .ToList();
        var sourceKeys = mapped
            .Select(row => row.Identity.SourceBienSoXe)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        plans.AddRange(
            targetInventory
                .Where(target =>
                    string.Equals(
                        target.SourceProfileCode,
                        route.SourceProfileCode,
                        StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(target.SourceBienSoXe) &&
                    !sourceKeys.Contains(target.SourceBienSoXe.Trim()))
                .OrderBy(
                    target => target.SourceBienSoXe,
                    StringComparer.OrdinalIgnoreCase)
                .Select(target => VehicleRealtimePlanner.PlanDelete(
                    VehicleSourceIdentity.Create(
                        route.SourceProfileCode,
                        target.SourceBienSoXe!),
                    targetInventory,
                    anchorVersion)));
        GuardWithinSnapshotCollisions(plans);

        var mappingFingerprint = VehicleSourceMapper.ComputeMappingFingerprint();
        var token = ComputeToken(
            recoveryId,
            route,
            sourceDatabaseGuid,
            anchorVersion,
            mappingFingerprint,
            sourceSchemaFingerprint,
            plans);
        return new(
            recoveryId,
            route.SourceProfileCode,
            sourceDatabaseGuid,
            anchorVersion,
            mappingFingerprint,
            sourceSchemaFingerprint,
            token,
            plans);
    }

    private static void GuardWithinSnapshotCollisions(
        IList<VehicleRealtimePlan> plans)
    {
        Guard(
            model => model.NormalizedBienSoXe,
            VehicleRealtimeReviewCodes.PlateCollision,
            VehicleRealtimeCollisionFields.BienSoXe);
        Guard(
            model => model.NormalizedSoDK,
            VehicleRealtimeReviewCodes.RegistrationCollision,
            VehicleRealtimeCollisionFields.SoDK);
        Guard(
            model => model.NormalizedSoKhung,
            VehicleRealtimeReviewCodes.ChassisCollision,
            VehicleRealtimeCollisionFields.SoKhung);
        Guard(
            model => model.NormalizedSoDongCo,
            VehicleRealtimeReviewCodes.EngineCollision,
            VehicleRealtimeCollisionFields.SoDongCo);

        void Guard(
            Func<VehicleSourceWriteModel, string?> keySelector,
            string reviewCode,
            string field)
        {
            var candidates = plans
                .Select((plan, index) => (plan, index))
                .Where(item =>
                    item.plan.Source is not null &&
                    !item.plan.RequiresManualReview)
                .ToArray();
            foreach (var group in candidates
                         .Where(item => keySelector(item.plan.Source!) is not null)
                         .GroupBy(
                             item => keySelector(item.plan.Source!)!,
                             StringComparer.Ordinal)
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

    private static string ComputeToken(
        Guid recoveryId,
        VehicleRealtimeRoute route,
        Guid sourceDatabaseGuid,
        long anchorVersion,
        string mappingFingerprint,
        string sourceSchemaFingerprint,
        IReadOnlyCollection<VehicleRealtimePlan> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append("RT03_V5_VEHICLE_FULL_CONVERGENCE");
        Append(recoveryId.ToString("D"));
        Append(route.SourceProfileCode);
        Append(sourceDatabaseGuid.ToString("D"));
        Append(anchorVersion.ToString(CultureInfo.InvariantCulture));
        Append(mappingFingerprint);
        Append(sourceSchemaFingerprint);
        foreach (var row in rows.OrderBy(
                     item => item.SourceBienSoXe,
                     StringComparer.OrdinalIgnoreCase))
        {
            Append(row.SourceBienSoXe);
            Append(row.Source?.SourceRowHash);
            Append(row.Action);
            Append(row.TargetXeTapId?.ToString(CultureInfo.InvariantCulture));
            Append(row.ReviewCode);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
    }
}
