using System.Runtime.CompilerServices;
using QLHV.Infrastructure.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeOptionalDomainRetryPolicyTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Optional_domain_before_next_retry_is_deferred_and_not_counted_as_work()
    {
        var domain = Domain(optional: true, lastSuccessfulVersion: 10, nextRetryAtUtc: NowUtc.AddMinutes(5));

        Assert.True(CsdtRealtimeOptionalDomainRetryPolicy.ShouldDefer(domain, NowUtc));
        Assert.False(CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(domain, currentVersion: 11, NowUtc));
    }

    [Fact]
    public void Optional_domain_is_processed_once_retry_time_arrives()
    {
        var due = Domain(optional: true, lastSuccessfulVersion: 10, nextRetryAtUtc: NowUtc);
        var overdue = Domain(optional: true, lastSuccessfulVersion: 10, nextRetryAtUtc: NowUtc.AddTicks(-1));
        var dueWithoutVersionLag = Domain(optional: true, lastSuccessfulVersion: 11, nextRetryAtUtc: NowUtc);

        Assert.False(CsdtRealtimeOptionalDomainRetryPolicy.ShouldDefer(due, NowUtc));
        Assert.True(CsdtRealtimeOptionalDomainRetryPolicy.IsRetryDue(due, NowUtc));
        Assert.True(CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(due, currentVersion: 11, NowUtc));
        Assert.True(CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(overdue, currentVersion: 11, NowUtc));
        Assert.True(CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(dueWithoutVersionLag, currentVersion: 11, NowUtc));
    }

    [Fact]
    public void Optional_domain_success_resets_backoff_state()
    {
        var runtimeRepository = ReadInfrastructureFile(
            "Sync",
            "Realtime",
            "CsdtRealtimeRuntimeRepository.cs");
        var workerRepository = ReadInfrastructureFile(
            "Sync",
            "Realtime",
            "CsdtRealtimeWorkerStateRepository.cs");

        var completeDomain = Section(
            runtimeRepository,
            "internal async Task CompleteDomainAsync",
            "internal async Task FailDomainAsync");
        var checkpointOnly = Section(
            workerRepository,
            "internal async Task CompleteCheckpointOnlyDomainAsync",
            "internal async Task SkipOptionalDomainAsync");

        Assert.Contains("RetryCount = 0", completeDomain, StringComparison.Ordinal);
        Assert.Contains("NextRetryAtUtc = NULL", completeDomain, StringComparison.Ordinal);
        Assert.Contains("RetryCount = 0", checkpointOnly, StringComparison.Ordinal);
        Assert.Contains("NextRetryAtUtc = NULL", checkpointOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_domain_failure_increases_bounded_backoff()
    {
        var runtimeRepository = ReadInfrastructureFile(
            "Sync",
            "Realtime",
            "CsdtRealtimeRuntimeRepository.cs");
        var workerRepository = ReadInfrastructureFile(
            "Sync",
            "Realtime",
            "CsdtRealtimeWorkerStateRepository.cs");

        var failDomain = Section(
            runtimeRepository,
            "internal async Task FailDomainAsync",
            "internal async Task CompleteRunAsync");
        var skipOptional = Section(
            workerRepository,
            "internal async Task SkipOptionalDomainAsync",
            "internal async Task RecordStreamFailureAsync");

        foreach (var section in new[] { failDomain, skipOptional })
        {
            Assert.Contains("RetryCount = RetryCount + 1", section, StringComparison.Ordinal);
            Assert.Contains("WHEN RetryCount = 0 THEN 5", section, StringComparison.Ordinal);
            Assert.Contains("WHEN RetryCount = 1 THEN 15", section, StringComparison.Ordinal);
            Assert.Contains("WHEN RetryCount = 2 THEN 30", section, StringComparison.Ordinal);
            Assert.Contains("ELSE 60", section, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Unsupported_optional_schema_does_not_advance_success_checkpoint()
    {
        var workerRepository = ReadInfrastructureFile(
            "Sync",
            "Realtime",
            "CsdtRealtimeWorkerStateRepository.cs");
        var skipOptional = Section(
            workerRepository,
            "internal async Task SkipOptionalDomainAsync",
            "internal async Task RecordStreamFailureAsync");

        Assert.Contains("LastErrorCode = N'SKIPPED_UNSUPPORTED_SCHEMA'", skipOptional, StringComparison.Ordinal);
        Assert.Contains("NextRetryAtUtc = DATEADD", skipOptional, StringComparison.Ordinal);
        Assert.Contains("IF @@ROWCOUNT = 0", skipOptional, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO dbo.App_CsdtRealtimeRunDomain", skipOptional, StringComparison.Ordinal);
        Assert.Contains("WHEN LastSuccessfulVersion IS NULL THEN @ToVersion", skipOptional, StringComparison.Ordinal);
        Assert.DoesNotContain("LastSuccessfulVersion = @ToVersion", skipOptional, StringComparison.Ordinal);
        Assert.DoesNotContain("LastSuccessAtUtc = SYSUTCDATETIME()", skipOptional, StringComparison.Ordinal);
    }

    [Fact]
    public void Mandatory_domain_is_not_deferred_by_optional_backoff()
    {
        var domain = Domain(optional: false, lastSuccessfulVersion: 10, nextRetryAtUtc: NowUtc.AddMinutes(5));

        Assert.False(CsdtRealtimeOptionalDomainRetryPolicy.ShouldDefer(domain, NowUtc));
        Assert.True(CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(domain, currentVersion: 11, NowUtc));
    }

    [Fact]
    public void Processor_skips_waiting_optional_domain_before_reader_writer_or_checkpoint()
    {
        var processor = ReadInfrastructureFile(
            "Sync",
            "Realtime",
            "CsdtRealtimeStreamProcessor.cs");
        var skipBlock = Section(
            processor,
            "if (CsdtRealtimeOptionalDomainRetryPolicy.ShouldDefer",
            "minimumValidVersion = await _reader.GetMinimumValidVersionAsync");

        Assert.Contains("currentDomainState = await _state.GetRuntimeDomainAsync", processor, StringComparison.Ordinal);
        Assert.Contains("continue;", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMinimumValidVersionAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadPartitionSnapshotAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadChangedPartitionSnapshotAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("UpsertAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginDomainAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteDomainAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteCheckpointOnlyDomainAsync", skipBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("FailDomainAsync", skipBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Incremental_poll_does_not_busy_loop_when_only_optional_domain_is_waiting()
    {
        var domain = Domain(optional: true, lastSuccessfulVersion: 10, nextRetryAtUtc: NowUtc.AddMinutes(5));

        Assert.False(CsdtRealtimeOptionalDomainRetryPolicy.HasWorkDue(domain, currentVersion: 11, NowUtc));
    }

    private static CsdtRealtimeRuntimeDomain Domain(
        bool optional,
        long? lastSuccessfulVersion,
        DateTimeOffset? nextRetryAtUtc)
        => new(
            StreamId: 1,
            DomainCode: optional ? "GiaoVien" : "KhoaHoc",
            IsOptional: optional,
            DomainStatus: optional ? "ERROR" : "RUNNING",
            BaselineStatus: "COMPLETED",
            BaselineVersion: 10,
            LastSuccessfulVersion: lastSuccessfulVersion,
            NextRetryAtUtc: nextRetryAtUtc,
            RetryCount: nextRetryAtUtc.HasValue ? 1 : 0);

    private static string ReadInfrastructureFile(params string[] segments)
        => File.ReadAllText(FindWorkspaceFile(["QLHV.Infrastructure", .. segments]));

    private static string Section(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source[startIndex..endIndex];
    }

    private static string FindWorkspaceFile(
        string[] segments,
        [CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "QLHV.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, Path.Combine(segments));
    }
}
