namespace QLHV.Application.Sync;

/// <summary>A target row already scoped to one source-profile partition.</summary>
public sealed record QlhvEntityFullSyncTargetRow(
    string SourceKey,
    string? SourceHash,
    bool IsDeleted);

public sealed record QlhvEntityFullSyncPlan(
    int SourceRows,
    int Insert,
    int Update,
    int Reactivate,
    int SoftDelete,
    int Skip)
{
    public int Upsert => Insert + Update + Reactivate;

    public static QlhvEntityFullSyncPlan Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Pure planner shared by course, teacher and course-teacher snapshots. An empty source is a
/// valid no-op only when its target partition has no active row; this prevents accidental
/// mass soft-delete when a source table/read unexpectedly becomes empty.
/// </summary>
public static class QlhvEntityFullSyncPlanner
{
    public static QlhvEntityFullSyncPlan BuildPlan<TSource>(
        IReadOnlyList<TSource> sourceRows,
        IReadOnlyList<QlhvEntityFullSyncTargetRow> targetRows,
        Func<TSource, string?> sourceKey,
        Func<TSource, string?> sourceHash,
        string groupName)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(targetRows);
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(sourceHash);

        var targetByKey = ToUniqueTargetDictionary(targetRows, groupName);
        if (sourceRows.Count == 0)
        {
            if (targetByKey.Values.Any(row => !row.IsDeleted))
            {
                throw new InvalidOperationException(
                    $"Nguon {groupName} co 0 dong nhung target partition con dong active; full sync bi chan.");
            }

            return QlhvEntityFullSyncPlan.Empty;
        }

        var sourceByKey = new Dictionary<string, TSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sourceRows)
        {
            var key = NormalizeRequired(sourceKey(row), $"{groupName}.SourceKey");
            if (!sourceByKey.TryAdd(key, row))
            {
                throw new InvalidOperationException(
                    $"Nguon {groupName} co source key trung trong partition: {key}.");
            }
        }

        var inserts = 0;
        var updates = 0;
        var reactivates = 0;
        var skips = 0;
        foreach (var (key, row) in sourceByKey)
        {
            if (!targetByKey.TryGetValue(key, out var target))
            {
                inserts++;
            }
            else if (target.IsDeleted)
            {
                reactivates++;
            }
            else if (!string.Equals(
                         NormalizeHash(sourceHash(row)),
                         NormalizeHash(target.SourceHash),
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
        return new QlhvEntityFullSyncPlan(
            sourceRows.Count,
            inserts,
            updates,
            reactivates,
            softDeletes,
            skips);
    }

    private static Dictionary<string, QlhvEntityFullSyncTargetRow> ToUniqueTargetDictionary(
        IReadOnlyList<QlhvEntityFullSyncTargetRow> rows,
        string groupName)
    {
        var result = new Dictionary<string, QlhvEntityFullSyncTargetRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = NormalizeRequired(row.SourceKey, $"{groupName}.TargetSourceKey");
            if (!result.TryAdd(key, row))
            {
                throw new InvalidOperationException(
                    $"Target {groupName} co source key trung trong partition: {key}.");
            }
        }

        return result;
    }

    private static string NormalizeHash(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeRequired(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Thieu gia tri bat buoc: {name}.")
            : value.Trim();
}
