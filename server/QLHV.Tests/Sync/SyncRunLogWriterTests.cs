using Microsoft.Extensions.Options;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Dtos;
using QLHV.Infrastructure.Sync;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class SyncRunLogWriterTests
{
    [Fact]
    public async Task Write_rejects_before_resolving_connection_when_target_writes_disabled()
    {
        var connections = new TrackingConnectionSettingsProvider();
        var writer = new SyncRunLogWriter(
            connections,
            Options.Create(new AppSyncOptions()),
            Options.Create(new SyncExecutionOptions
            {
                EnableTargetWrites = false,
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.WriteAsync(new SyncRunLogEntry()));

        Assert.Contains("EnableTargetWrites = false", exception.Message, StringComparison.Ordinal);
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
