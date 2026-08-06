namespace QLHV.Infrastructure.Sync.Realtime;

internal static class CsdtRealtimeSourceIdentityPlanner
{
    internal const string PresentStatus = "PRESENT";
    internal const string MissingStatus = "MISSING";
    internal const string InferredDeleteOperation = "INFERRED_DELETE";

    internal static IReadOnlyList<CsdtRealtimeChange> InferMissingIdentities(
        IReadOnlyList<CsdtRealtimeSourceIdentityInventoryRow> inventory,
        IReadOnlyCollection<string> currentSourceIdentities,
        bool inferMissingIdentities)
    {
        if (!inferMissingIdentities || inventory.Count == 0)
        {
            return [];
        }

        var current = currentSourceIdentities.ToHashSet(StringComparer.Ordinal);
        return inventory
            .Where(item =>
                string.Equals(item.IdentityStatus, PresentStatus, StringComparison.Ordinal) &&
                !current.Contains(item.SourceIdentity))
            .GroupBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.LastSeenVersion).First())
            .OrderBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .Select(item => new CsdtRealtimeChange(
                item.LastSeenVersion,
                InferredDeleteOperation,
                item.SourceIdentity,
                CurrentRowIsInPartition: false))
            .ToArray();
    }
}
