using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using Xunit;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class HocVienSyncGuardTests
{
    [Fact]
    public async Task Execute_rejects_when_EnableTargetWrites_false()
    {
        var fixture = CreateFixture(new SyncExecutionOptions { EnableTargetWrites = false });

        var result = await fixture.Service.ExecuteHocVienAsync(ConfirmedRequest());

        Assert.False(result.Accepted);
        Assert.Equal("Rejected", result.Status);
        Assert.Contains(result.Issues, issue => issue.Contains("EnableTargetWrites=false", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Source.CountCalls);
        Assert.Equal(0, fixture.Target.UpsertCalls);
        Assert.Equal(0, fixture.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_ConfirmTargetWrites_false()
    {
        var fixture = CreateFixture(new SyncExecutionOptions { EnableTargetWrites = true });

        var result = await fixture.Service.ExecuteHocVienAsync(new HocVienSyncExecuteRequest
        {
            ConfirmTargetWrites = false,
            ConfirmationText = HocVienSyncExecuteRequest.RequiredConfirmationText,
        });

        Assert.False(result.Accepted);
        Assert.Contains(result.Issues, issue => issue.Contains("ConfirmTargetWrites=true", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Source.CountCalls);
        Assert.Equal(0, fixture.Target.UpsertCalls);
        Assert.Equal(0, fixture.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_ConfirmationText_is_not_exact()
    {
        var fixture = CreateFixture(new SyncExecutionOptions { EnableTargetWrites = true });

        var result = await fixture.Service.ExecuteHocVienAsync(new HocVienSyncExecuteRequest
        {
            ConfirmTargetWrites = true,
            ConfirmationText = "execute_dong_bo_v2_hoc_vien",
        });

        Assert.False(result.Accepted);
        Assert.Contains(result.Issues, issue => issue.Contains(HocVienSyncExecuteRequest.RequiredConfirmationText, StringComparison.Ordinal));
        Assert.Equal(0, fixture.Source.CountCalls);
        Assert.Equal(0, fixture.Target.UpsertCalls);
        Assert.Equal(0, fixture.Log.WriteCalls);
    }

    [Fact]
    public async Task Dry_run_remains_no_write()
    {
        var fixture = CreateFixture(
            new SyncExecutionOptions { EnableTargetWrites = true },
            sourceRows: new[]
            {
                new V2HocVienSourceRow { MaDK = "DK001", HoVaTen = "Nguyen Van A", SoCMT = "012345678901" },
                new V2HocVienSourceRow { MaDK = "DK002", HoVaTen = "Tran Thi B", SoCMT = "123456789" },
            });

        var result = await fixture.Service.DryRunHocVienAsync();

        Assert.True(result.IsDryRun);
        Assert.Equal(2, result.SourceRecordCount);
        Assert.Equal(1, fixture.Source.CountCalls);
        Assert.Equal(0, fixture.Source.ReadPageCalls);
        Assert.Equal(0, fixture.Target.UpsertCalls);
        Assert.Equal(0, fixture.Log.WriteCalls);
    }

    private static HocVienSyncExecuteRequest ConfirmedRequest() => new()
    {
        ConfirmTargetWrites = true,
        ConfirmationText = HocVienSyncExecuteRequest.RequiredConfirmationText,
    };

    private static Fixture CreateFixture(
        SyncExecutionOptions execution,
        IReadOnlyList<V2HocVienSourceRow>? sourceRows = null)
    {
        execution.RequireManualConfirmation = true;
        execution.ConfirmationPhrase = HocVienSyncExecuteRequest.RequiredConfirmationText;

        var source = new FakeV2HocVienSourceRepository(sourceRows ?? Array.Empty<V2HocVienSourceRow>());
        var target = new FakeQlhvHocVienTargetRepository();
        var log = new FakeSyncRunLogWriter();
        var service = new HocVienSyncService(
            Options.Create(new AppSyncOptions { BatchSize = 100, TimeoutSeconds = 30 }),
            Options.Create(execution),
            new FakeConnectionSettingsProvider(),
            source,
            target,
            log);

        return new Fixture(service, source, target, log);
    }

    private sealed record Fixture(
        HocVienSyncService Service,
        FakeV2HocVienSourceRepository Source,
        FakeQlhvHocVienTargetRepository Target,
        FakeSyncRunLogWriter Log);

    private sealed class FakeConnectionSettingsProvider : IConnectionSettingsProvider
    {
        private const string FakeUsableConnection = "UnitTestConnection";

        public Task<ResolvedConnection> GetQlhvAppConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ResolvedConnection.FromConfiguration("QLHV_APP", FakeUsableConnection));

        public Task<ResolvedConnection> GetSourceConnectionAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ResolvedConnection.FromConfiguration(source.ToString(), FakeUsableConnection));

        public Task<ConnectionSettingsView> GetViewAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectionSettingsView());
    }

    private sealed class FakeV2HocVienSourceRepository : IV2HocVienSourceRepository
    {
        private readonly IReadOnlyList<V2HocVienSourceRow> _rows;

        public FakeV2HocVienSourceRepository(IReadOnlyList<V2HocVienSourceRow> rows)
        {
            _rows = rows;
        }

        public int CountCalls { get; private set; }
        public int ReadPageCalls { get; private set; }

        public Task<int> CountAsync(HocVienSourceFilter filter, CancellationToken cancellationToken = default)
        {
            CountCalls++;
            return Task.FromResult(_rows.Count);
        }

        public Task<IReadOnlyList<V2HocVienSourceRow>> ReadPageAsync(
            HocVienSourceFilter filter,
            int offset,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ReadPageCalls++;
            return Task.FromResult((IReadOnlyList<V2HocVienSourceRow>)_rows.Skip(offset).Take(pageSize).ToList());
        }
    }

    private sealed class FakeQlhvHocVienTargetRepository : IQlhvHocVienTargetRepository
    {
        public int UpsertCalls { get; private set; }

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
            IReadOnlyCollection<string> maDks,
            CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyCollection<string>)Array.Empty<string>());

        public Task<UpsertCounts> UpsertBatchAsync(
            IReadOnlyList<HocVienTargetWriteModel> rows,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return Task.FromResult(new UpsertCounts(rows.Count, 0, 0));
        }
    }

    private sealed class FakeSyncRunLogWriter : ISyncRunLogWriter
    {
        public int WriteCalls { get; private set; }

        public Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            return Task.FromResult(1L);
        }
    }
}
