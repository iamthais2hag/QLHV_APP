using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.Sync.Mapping;
using QLHV.Infrastructure.Sync;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class QlhvHocVienTargetRepositoryTests
{
    [Fact]
    public async Task Upsert_rejects_before_resolving_connection_when_sync_dry_run_is_true()
    {
        var connections = new TrackingConnectionSettingsProvider();
        var repository = new QlhvHocVienTargetRepository(
            connections,
            Options.Create(new AppSyncOptions { DryRun = true }),
            Options.Create(new SyncExecutionOptions { EnableTargetWrites = true }));

        var rows = new[]
        {
            new HocVienTargetWriteModel
            {
                MaDK = "TEST-MADK",
                V2RowHash = "hash",
            },
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpsertBatchAsync(rows));

        Assert.Contains("Sync:DryRun = true", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, connections.QlhvResolutionCalls);
    }

    private sealed class TrackingConnectionSettingsProvider : IConnectionSettingsProvider
    {
        public int QlhvResolutionCalls { get; private set; }

        public Task<ResolvedConnection> GetQlhvAppConnectionAsync(
            CancellationToken cancellationToken = default)
        {
            QlhvResolutionCalls++;
            return Task.FromResult(ResolvedConnection.NotConfigured("QLHV_APP"));
        }

        public Task<ResolvedConnection> GetSourceConnectionAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ResolvedConnection.NotConfigured(source.ToString()));

        public Task<ConnectionSettingsView> GetViewAsync(
            SourceSystem source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectionSettingsView
            {
                Key = source.ToString(),
                DisplayName = source.ToString(),
            });
    }
}
