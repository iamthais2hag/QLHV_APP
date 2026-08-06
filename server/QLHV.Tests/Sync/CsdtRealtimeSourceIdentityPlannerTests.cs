using System.Runtime.CompilerServices;
using QLHV.Infrastructure.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeSourceIdentityPlannerTests
{
    private const string SourceA = "{\"MaDK\":\"66029-00000001-000001\"}";
    private const string SourceB = "{\"MaDK\":\"66029-00000001-000002\"}";
    private const string V1Only = "{\"MaDK\":\"LEGACY-V1-ONLY\"}";

    [Fact]
    public void First_baseline_never_infers_tombstones()
    {
        var result = CsdtRealtimeSourceIdentityPlanner.InferMissingIdentities(
            inventory: [],
            currentSourceIdentities: [SourceA],
            inferMissingIdentities: false);

        Assert.Empty(result);
    }

    [Fact]
    public void Expired_checkpoint_infers_only_previously_present_source_identity_that_disappeared()
    {
        CsdtRealtimeSourceIdentityInventoryRow[] inventory =
        [
            new(SourceA, CsdtRealtimeSourceIdentityPlanner.PresentStatus, 120),
            new(SourceB, CsdtRealtimeSourceIdentityPlanner.PresentStatus, 121),
        ];

        var result = CsdtRealtimeSourceIdentityPlanner.InferMissingIdentities(
            inventory,
            currentSourceIdentities: [SourceA],
            inferMissingIdentities: true);

        var tombstone = Assert.Single(result);
        Assert.Equal(SourceB, tombstone.KeyJson);
        Assert.Equal(121, tombstone.Version);
        Assert.Equal(CsdtRealtimeSourceIdentityPlanner.InferredDeleteOperation, tombstone.Operation);
    }

    [Fact]
    public void V1_only_identity_is_never_considered_a_source_delete()
    {
        CsdtRealtimeSourceIdentityInventoryRow[] inventory =
        [
            new(SourceA, CsdtRealtimeSourceIdentityPlanner.PresentStatus, 120),
        ];

        var result = CsdtRealtimeSourceIdentityPlanner.InferMissingIdentities(
            inventory,
            currentSourceIdentities: [],
            inferMissingIdentities: true);

        Assert.DoesNotContain(result, item => item.KeyJson == V1Only);
        Assert.Equal(SourceA, Assert.Single(result).KeyJson);
    }

    [Fact]
    public void Repeated_rebaseline_does_not_duplicate_identity_already_marked_missing()
    {
        CsdtRealtimeSourceIdentityInventoryRow[] inventory =
        [
            new(SourceB, CsdtRealtimeSourceIdentityPlanner.MissingStatus, 121),
        ];

        var result = CsdtRealtimeSourceIdentityPlanner.InferMissingIdentities(
            inventory,
            currentSourceIdentities: [],
            inferMissingIdentities: true);

        Assert.Empty(result);
    }

    [Fact]
    public void Checkpoint_is_committed_only_with_inventory_and_tombstone_state_transaction()
    {
        var repository = File.ReadAllText(FindWorkspaceFile(
            "server",
            "QLHV.Infrastructure",
            "Sync",
            "Realtime",
            "CsdtRealtimeRuntimeRepository.cs"));
        var completeDomain = Between(
            repository,
            "internal async Task CompleteDomainAsync(",
            "internal async Task CompleteNoChangeDomainAsync(");

        var persistIndex = completeDomain.IndexOf(
            "PersistSourceIdentitiesAndTombstonesAsync(",
            StringComparison.Ordinal);
        var checkpointIndex = completeDomain.IndexOf(
            "UPDATE dbo.App_CsdtRealtimeDomainState",
            StringComparison.Ordinal);
        var commitIndex = completeDomain.IndexOf(
            "await transaction.CommitAsync(cancellationToken);",
            StringComparison.Ordinal);

        Assert.True(persistIndex >= 0 && persistIndex < checkpointIndex);
        Assert.True(checkpointIndex < commitIndex);
        Assert.Contains("await SafeRollbackAsync(transaction);", completeDomain, StringComparison.Ordinal);
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing marker {start}.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing marker {end}.");
        return source[startIndex..endIndex];
    }

    private static string FindWorkspaceFile(
        string firstPathPart,
        params string[] remainingPathParts)
        => FindWorkspaceFileFromCaller(
            new[] { firstPathPart }.Concat(remainingPathParts).ToArray());

    private static string FindWorkspaceFileFromCaller(
        string[] pathParts,
        [CallerFilePath] string testFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate workspace file.", Path.Combine(pathParts));
    }
}
