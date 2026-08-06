using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Realtime;

namespace QLHV.Infrastructure.Sync.Realtime;

/// <summary>
/// Hosts the two fixed realtime streams independently. Every polling pass owns
/// a SQL session applock, so a second Worker cannot execute the same stream.
/// </summary>
internal sealed class CsdtRealtimeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CsdtRealtimeSyncOptions _options;
    private readonly ILogger<CsdtRealtimeWorker> _logger;

    public CsdtRealtimeWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<CsdtRealtimeSyncOptions> options,
        ILogger<CsdtRealtimeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("CSDT realtime Worker is disabled by server configuration.");
            return Task.CompletedTask;
        }

        var routes = CsdtRealtimeStreamCatalog.GetConfiguredRoutes(_options)
            .Where(IsStreamEnabled)
            .ToArray();
        return Task.WhenAll(routes.Select(route => RunStreamAsync(route, stoppingToken)));
    }

    private async Task RunStreamAsync(
        CsdtRealtimeRouteDefinition route,
        CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnePassWithLockAsync(route, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "CSDT realtime stream {StreamCode} failed; checkpoint is unchanged.",
                    route.StreamCode);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessOnePassWithLockAsync(
        CsdtRealtimeRouteDefinition route,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<CsdtRealtimeConnectionResolver>();
        var state = scope.ServiceProvider.GetRequiredService<CsdtRealtimeStateRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<CsdtRealtimeStreamProcessor>();
        var resolved = await resolver.ResolveAsync(route, cancellationToken);

        await using var streamLock = await CsdtRealtimeStreamLock.TryAcquireAsync(
            resolved.StateConnectionString,
            route.StreamCode,
            cancellationToken);
        if (streamLock is null)
        {
            return;
        }

        await state.EnsureRuntimeRouteAsync(route, cancellationToken);
        var runtime = await state.GetRuntimeStreamAsync(route.StreamCode, cancellationToken);
        _ = await state.RecoverOrphanedWorkAsync(runtime.StreamId, cancellationToken);
        try
        {
            await processor.ProcessOnceAsync(route, resolved, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await state.RecordStreamFailureAsync(
                runtime.StreamId,
                exception,
                CancellationToken.None);
            throw;
        }
    }

    private bool IsStreamEnabled(CsdtRealtimeRouteDefinition route)
        => route.StreamCode switch
        {
            CsdtRealtimeStreamCodes.OtoV2ToV1 => _options.Streams.Oto.Enabled,
            CsdtRealtimeStreamCodes.MotoV2ToV1 => _options.Streams.Moto.Enabled,
            _ => false,
        };
}
