using QLHV.Application.Sync.Mapping;

namespace QLHV.Application.Sync;

/// <summary>A target row already scoped to exactly one SourceProfileCode partition.</summary>
public sealed record QlhvFullSyncTargetRow(
    string SourceMaDK,
    string? V2RowHash,
    bool IsDeleted);

public sealed record QlhvFullSyncPlan(
    string SourceProfileCode,
    int PlannedInsertHocVienRows,
    int PlannedUpdateHocVienRows,
    int PlannedReactivateHocVienRows,
    int PlannedSoftDeleteHocVienRows,
    int PlannedSkipHocVienRows)
{
    public int PlannedUpsertHocVienRows =>
        PlannedInsertHocVienRows + PlannedUpdateHocVienRows + PlannedReactivateHocVienRows;
}

/// <summary>
/// Pure full-snapshot planner. targetRows must contain only the target partition named
/// by the single SourceProfileCode present in sourceRows.
/// </summary>
public static class QlhvFullSyncPlanner
{
    public static QlhvFullSyncPlan BuildPlan(
        IReadOnlyList<QlhvImportHocVienWriteModel> sourceRows,
        IReadOnlyList<QlhvFullSyncTargetRow> targetRows)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(targetRows);

        if (sourceRows.Count == 0)
        {
            throw new InvalidOperationException(
                "Nguon hoc vien rong khong duoc phep lap ke hoach full snapshot.");
        }

        var sourceProfiles = sourceRows
            .Select(row => NormalizeRequired(row.SourceProfileCode, nameof(row.SourceProfileCode)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceProfiles.Length != 1)
        {
            throw new InvalidOperationException(
                "Full snapshot chi duoc chua mot SourceProfileCode.");
        }

        var sourceByKey = ToUniqueSourceDictionary(sourceRows);
        var targetByKey = ToUniqueTargetDictionary(targetRows);
        var inserts = 0;
        var updates = 0;
        var reactivates = 0;
        var skips = 0;

        foreach (var (sourceMaDk, source) in sourceByKey)
        {
            if (!targetByKey.TryGetValue(sourceMaDk, out var target))
            {
                inserts++;
            }
            else if (target.IsDeleted)
            {
                reactivates++;
            }
            else if (!string.Equals(
                         source.V2RowHash,
                         target.V2RowHash,
                         StringComparison.OrdinalIgnoreCase))
            {
                updates++;
            }
            else
            {
                skips++;
            }
        }

        var softDeletes = targetByKey.Count(pair =>
            !pair.Value.IsDeleted && !sourceByKey.ContainsKey(pair.Key));

        return new QlhvFullSyncPlan(
            sourceProfiles[0].ToUpperInvariant(),
            inserts,
            updates,
            reactivates,
            softDeletes,
            skips);
    }

    private static Dictionary<string, QlhvImportHocVienWriteModel> ToUniqueSourceDictionary(
        IReadOnlyList<QlhvImportHocVienWriteModel> rows)
    {
        var result = new Dictionary<string, QlhvImportHocVienWriteModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            if (!result.TryAdd(key, row))
            {
                throw new InvalidOperationException(
                    $"Nguon co SourceMaDK trung trong partition: {key}.");
            }
        }

        return result;
    }

    private static Dictionary<string, QlhvFullSyncTargetRow> ToUniqueTargetDictionary(
        IReadOnlyList<QlhvFullSyncTargetRow> rows)
    {
        var result = new Dictionary<string, QlhvFullSyncTargetRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = NormalizeRequired(row.SourceMaDK, nameof(row.SourceMaDK));
            if (!result.TryAdd(key, row))
            {
                throw new InvalidOperationException(
                    $"Target co SourceMaDK trung trong partition: {key}.");
            }
        }

        return result;
    }

    private static string NormalizeRequired(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thieu gia tri bat buoc: {name}.")
            : value.Trim();
}
