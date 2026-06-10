using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class HocVienSyncGuardTests
{
    [Fact]
    public async Task Execute_rejects_when_target_writes_disabled()
    {
        var fakes = TestFakes.Create(enableWrites: false);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = true,
            ConfirmationText = fakes.Execution.ConfirmationPhrase,
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("EnableTargetWrites", result.Message);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_manual_confirm_is_false()
    {
        var fakes = TestFakes.Create(enableWrites: true);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = false,
            ConfirmationText = fakes.Execution.ConfirmationPhrase,
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("Confirm", result.Message);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_confirmation_phrase_is_not_exact()
    {
        var fakes = TestFakes.Create(enableWrites: true);

        var result = await fakes.Service.ExecuteHocVienAsync(new SyncExecuteRequest
        {
            Confirm = true,
            ConfirmationText = "execute_dong_bo_v2_hoc_vien",
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("xac nhan", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    [Fact]
    public async Task Dry_run_remains_no_write()
    {
        var fakes = TestFakes.Create(enableWrites: true, sourceCount: 7);

        var result = await fakes.Service.DryRunHocVienAsync();

        Assert.Equal(7, result.SourceRecordCount);
        Assert.Equal(1, fakes.Source.CountCalls);
        Assert.Equal(0, fakes.Source.ReadPageCalls);
        Assert.Equal(0, fakes.Target.UpsertCalls);
        Assert.Equal(0, fakes.Log.WriteCalls);
    }

    private sealed class TestFakes
    {
        private TestFakes(
            HocVienSyncService service,
            SyncExecutionOptions execution,
            FakeV2Source source,
            FakeTarget target,
            FakeRunLog log)
        {
            Service = service;
            Execution = execution;
            Source = source;
            Target = target;
            Log = log;
        }

        public HocVienSyncService Service { get; }
        public SyncExecutionOptions Execution { get; }
        public FakeV2Source Source { get; }
        public FakeTarget Target { get; }
        public FakeRunLog Log { get; }

        public static TestFakes Create(bool enableWrites, int sourceCount = 0)
        {
            var sync = new AppSyncOptions
            {
                BatchSize = 100,
                TimeoutSeconds = 30,
            };
            var execution = new SyncExecutionOptions
            {
                EnableTargetWrites = enableWrites,
                RequireManualConfirmation = true,
                ConfirmationPhrase = "EXECUTE_DONG_BO_V2_HOC_VIEN",
            };
            var connections = new FakeConnectionSettingsProvider();
            var source = new FakeV2Source(sourceCount);
            var target = new FakeTarget();
            var log = new FakeRunLog();
            var service = new HocVienSyncService(
                Options.Create(sync),
                Options.Create(execution),
                connections,
                source,
                target,
                log);

            return new TestFakes(service, execution, source, target, log);
        }
    }

    private sealed class FakeConnectionSettingsProvider : IConnectionSettingsProvider
    {
        public Task<ResolvedConnection> GetQlhvAppConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Usable("QLHV_APP"));

        public Task<ResolvedConnection> GetSourceConnectionAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Usable(source.ToString()));

        public Task<ConnectionSettingsView> GetViewAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectionSettingsView
            {
                Key = source.ToString(),
                DisplayName = source.ToString(),
                IsConfigured = true,
                IsEnabled = true,
            });

        private static ResolvedConnection Usable(string name) => new()
        {
            Name = name,
            IsConfigured = true,
            IsPlaceholder = false,
            ConnectionString = "Server=(local);Database=TEST_ONLY;",
        };
    }

    private sealed class FakeV2Source : IV2HocVienSourceRepository
    {
        private readonly int _sourceCount;

        public FakeV2Source(int sourceCount)
        {
            _sourceCount = sourceCount;
        }

        public int CountCalls { get; private set; }
        public int ReadPageCalls { get; private set; }

        public Task<int> CountAsync(HocVienSourceFilter filter, CancellationToken cancellationToken = default)
        {
            CountCalls++;
            return Task.FromResult(_sourceCount);
        }

        public Task<IReadOnlyList<V2HocVienSourceRow>> ReadPageAsync(
            HocVienSourceFilter filter,
            int offset,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ReadPageCalls++;
            return Task.FromResult<IReadOnlyList<V2HocVienSourceRow>>(Array.Empty<V2HocVienSourceRow>());
        }
    }

    private sealed class FakeTarget : IQlhvHocVienTargetRepository
    {
        public int UpsertCalls { get; private set; }

        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
            IReadOnlyCollection<string> maDks,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        public Task<UpsertCounts> UpsertBatchAsync(
            IReadOnlyList<QLHV.Application.Sync.Mapping.HocVienTargetWriteModel> rows,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return Task.FromResult(UpsertCounts.Empty);
        }
    }

    private sealed class FakeRunLog : ISyncRunLogWriter
    {
        public int WriteCalls { get; private set; }

        public Task<long> WriteAsync(SyncRunLogEntry entry, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            return Task.FromResult(1L);
        }
    }
}
