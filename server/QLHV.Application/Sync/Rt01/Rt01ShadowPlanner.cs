using System.Security.Cryptography;
using System.Text;
using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync.Rt01;

public static class Rt01ShadowPlanner
{
    public static Rt01ShadowObservation Build(
        Rt01ShadowRoute route,
        Rt01ShadowSnapshots snapshots,
        string? previousSourceFingerprint,
        int detectionLatencyBudgetSeconds,
        DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(snapshots);

        var blockers = new List<string>();
        var warnings = new List<string>(snapshots.LiveSource.HocVienWarnings);
        if (!Rt01ShadowRouteCatalog.Ordered.Contains(route))
        {
            blockers.Add("RT-01 route khong nam trong allowlist OTO/MOTO live.");
        }

        if (!string.Equals(
                snapshots.LiveSource.SourceDatabaseName,
                route.SourceDatabaseName,
                StringComparison.Ordinal))
        {
            blockers.Add(
                $"Source database la {snapshots.LiveSource.SourceDatabaseName}; " +
                $"RT-01 bat buoc phai la {route.SourceDatabaseName}.");
        }

        var normalizedSourceKeys = snapshots.LiveSource.HocVienRows
            .Where(row => !string.IsNullOrWhiteSpace(row.MaDK))
            .Select(row => row.MaDK.Trim())
            .ToArray();
        var blankSourceKeys = snapshots.LiveSource.HocVienRows.Count - normalizedSourceKeys.Length;
        var duplicateSourceIdentityGroups = normalizedSourceKeys
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Skip(1).Any());
        if (snapshots.LiveSource.HocVienRows.Count == 0)
        {
            blockers.Add("Live source co 0 hoc vien; shadow plan bi chan.");
        }

        if (blankSourceKeys > 0)
        {
            blockers.Add($"Live source co {blankSourceKeys} hoc vien thieu MaDK.");
        }

        if (duplicateSourceIdentityGroups > 0)
        {
            blockers.Add(
                $"Live source co {duplicateSourceIdentityGroups} nhom SourceMaDK bi trung.");
        }

        if (snapshots.QlhvTarget.DuplicateHocVienTargetIdentityRows > 0)
        {
            blockers.Add(
                $"QLHV_APP co {snapshots.QlhvTarget.DuplicateHocVienTargetIdentityRows} " +
                "nhom HocVien source identity bi trung.");
        }

        if (snapshots.QlhvTarget.SourceProfileConstraintExists &&
            !snapshots.QlhvTarget.SourceProfileAllowedByConstraint)
        {
            blockers.Add(
                $"QLHV_APP khong cho phep source profile {route.SourceProfileCode}.");
        }

        var sourceIdentity = new HocVienSourceIdentityContext(
            route.SourceProfileCode,
            "V2");
        var mappedRows = new List<QlhvImportHocVienWriteModel>(
            snapshots.LiveSource.HocVienRows.Count);
        foreach (var sourceRow in snapshots.LiveSource.HocVienRows)
        {
            var mapped = QlhvImportHocVienMapper.MapAndValidate(sourceRow, sourceIdentity);
            warnings.AddRange(mapped.Warnings.Select(warning =>
                $"{warning.MaDK}: {warning.Code}."));
            blockers.AddRange(mapped.Blockers);
            if (!mapped.ShouldSkip && mapped.Model is not null)
            {
                mappedRows.Add(mapped.Model);
            }
        }

        if (snapshots.LiveSource.HocVienRows.Count > 0 && mappedRows.Count == 0)
        {
            blockers.Add("Khong co hoc vien live nao map duoc an toan.");
        }

        var sourceFingerprint = FingerprintSource(mappedRows);
        var targetFingerprint = FingerprintTarget(snapshots.QlhvTarget.HocVienRows);
        QlhvFullSyncPlan? plan = null;
        if (blockers.Count == 0)
        {
            try
            {
                plan = QlhvFullSyncPlanner.BuildPlan(
                    mappedRows,
                    snapshots.QlhvTarget.HocVienRows);
            }
            catch (InvalidOperationException exception)
            {
                blockers.Add(exception.Message);
            }
        }

        var targetActiveRows = snapshots.QlhvTarget.HocVienRows.Count(row => !row.IsDeleted);
        var targetSoftDeletedRows = snapshots.QlhvTarget.HocVienRows.Count(row => row.IsDeleted);
        var hasDrift = plan is not null &&
                       plan.PlannedInsertHocVienRows +
                       plan.PlannedUpdateHocVienRows +
                       plan.PlannedReactivateHocVienRows +
                       plan.PlannedSoftDeleteHocVienRows > 0;

        return new Rt01ShadowObservation
        {
            SourceType = route.SourceType,
            SourceProfileCode = route.SourceProfileCode,
            SourceDatabaseName = snapshots.LiveSource.SourceDatabaseName,
            MaCsdt = route.MaCsdt,
            Status = blockers.Count > 0
                ? Rt01ShadowStatuses.Blocked
                : hasDrift
                    ? Rt01ShadowStatuses.DriftDetected
                    : Rt01ShadowStatuses.Matched,
            ObservedAtUtc = observedAtUtc,
            ReadStartedAtUtc = snapshots.ReadStartedAtUtc,
            ReadCompletedAtUtc = snapshots.ReadCompletedAtUtc,
            DetectionLatencyBudgetSeconds = detectionLatencyBudgetSeconds,
            SourceFingerprint = sourceFingerprint,
            TargetFingerprint = targetFingerprint,
            SourceChangedSincePreviousObservation =
                !string.IsNullOrWhiteSpace(previousSourceFingerprint) &&
                !string.Equals(
                    previousSourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal),
            SourceRows = snapshots.LiveSource.HocVienRows.Count,
            TargetActiveRows = targetActiveRows,
            TargetSoftDeletedRows = targetSoftDeletedRows,
            PlannedInsertRows = plan?.PlannedInsertHocVienRows ?? 0,
            PlannedUpdateRows = plan?.PlannedUpdateHocVienRows ?? 0,
            PlannedReactivateRows = plan?.PlannedReactivateHocVienRows ?? 0,
            TargetOnlyActiveRows = plan?.PlannedSoftDeleteHocVienRows ?? 0,
            PlannedNoChangeRows = plan?.PlannedSkipHocVienRows ?? 0,
            DuplicateSourceIdentityGroups = duplicateSourceIdentityGroups,
            DuplicateTargetIdentityGroups =
                snapshots.QlhvTarget.DuplicateHocVienTargetIdentityRows,
            BusinessDataWrites = 0,
            ApplyCheckpointPublished = false,
            ExistingAutoSyncTouched = false,
            Blockers = blockers.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).Take(100).ToArray(),
        };
    }

    private static string FingerprintSource(
        IReadOnlyCollection<QlhvImportHocVienWriteModel> rows)
        => Fingerprint(rows
            .OrderBy(row => row.SourceMaDK, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
                $"{row.SourceProfileCode.Trim().ToUpperInvariant()}|" +
                $"{row.SourceMaDK.Trim()}|{row.V2RowHash}"));

    private static string FingerprintTarget(
        IReadOnlyCollection<QlhvFullSyncTargetRow> rows)
        => Fingerprint(rows
            .OrderBy(row => row.SourceMaDK, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
                $"{row.SourceMaDK.Trim()}|{row.V2RowHash ?? string.Empty}|" +
                $"{(row.IsDeleted ? "1" : "0")}"));

    private static string Fingerprint(IEnumerable<string> values)
    {
        var canonical = string.Join(
            "\n",
            values.Select(value => $"{value.Length}:{value}"));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
