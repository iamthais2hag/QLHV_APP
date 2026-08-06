using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Connections;
using QLHV.Application.Sync.Realtime;
using QLHV.Infrastructure.Sync.Realtime;

namespace QLHV.Tests.Sync;

public sealed class CsdtRealtimeStateRepositoryTests
{
    [Fact]
    public async Task Enqueue_rejects_command_route_that_does_not_match_configured_backup_route_before_opening_database()
    {
        var connections = new TrackingConnectionSettingsProvider();
        var repository = new CsdtRealtimeStateRepository(
            connections,
            Options.Create(BackupOptions()));

        var liveCommand = CommandFor(CsdtRealtimeStreamCatalog.LiveRoutes[0]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.EnqueueAsync(liveCommand));
        Assert.Equal(0, connections.QlhvAppCalls);
    }

    [Fact]
    public void Concurrent_identical_reverse_execute_joins_the_active_command_only()
    {
        const string request =
            """{"Enabled":null,"MaKhoaHoc":"66029K260001","ExpectedPlanToken":"PLAN"}""";

        Assert.True(CsdtRealtimeStateRepository.IsSameActiveCommand(
            CsdtRealtimeCommandTypes.ReverseExecute,
            request,
            CsdtRealtimeCommandTypes.ReverseExecute,
            request));
        Assert.False(CsdtRealtimeStateRepository.IsSameActiveCommand(
            CsdtRealtimeCommandTypes.ReverseExecute,
            request,
            CsdtRealtimeCommandTypes.ReverseExecute,
            request.Replace("PLAN", "OTHER", StringComparison.Ordinal)));
        Assert.False(CsdtRealtimeStateRepository.IsSameActiveCommand(
            CsdtRealtimeCommandTypes.Baseline,
            request,
            CsdtRealtimeCommandTypes.ReverseExecute,
            request));
    }

    private static CsdtRealtimeCommand CommandFor(CsdtRealtimeRouteDefinition route)
        => new()
        {
            CommandType = CsdtRealtimeCommandTypes.Baseline,
            StreamCode = route.StreamCode,
            VehicleType = route.VehicleType,
            SourceProfileCode = route.SourceProfileCode,
            TargetProfileCode = route.TargetProfileCode,
            SourceDatabaseName = route.SourceDatabaseName,
            TargetDatabaseName = route.TargetDatabaseName,
            MaCSDT = route.MaCSDT,
            ExpectedStateToken = "state-token",
            RequestedBy = "admin",
        };

    private static CsdtRealtimeSyncOptions BackupOptions()
        => new()
        {
            Enabled = true,
            UseBackupProfiles = true,
            Streams = new CsdtRealtimeStreamsOptions
            {
                Oto = new CsdtRealtimeStreamOptions
                {
                    Enabled = true,
                    StreamCode = CsdtRealtimeStreamCodes.OtoV2ToV1,
                    SourceProfile = CsdtRealtimeProfileCodes.OtoV2Bak,
                    TargetProfile = CsdtRealtimeProfileCodes.OtoV1Bak,
                    MaCSDT = "66029",
                },
                Moto = new CsdtRealtimeStreamOptions
                {
                    Enabled = true,
                    StreamCode = CsdtRealtimeStreamCodes.MotoV2ToV1,
                    SourceProfile = CsdtRealtimeProfileCodes.MotoV2Bak,
                    TargetProfile = CsdtRealtimeProfileCodes.MotoV1Bak,
                    MaCSDT = "66030",
                },
            },
        };

    private sealed class TrackingConnectionSettingsProvider : IConnectionSettingsProvider
    {
        public int QlhvAppCalls { get; private set; }

        public Task<ResolvedConnection> GetQlhvAppConnectionAsync(
            CancellationToken cancellationToken = default)
        {
            QlhvAppCalls++;
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
