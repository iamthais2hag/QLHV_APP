using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QLHV.Application.HocVien.Photos;

namespace QLHV.Infrastructure.HocVien.Photos;

public sealed class HocVienPhotoProcessingWorker : BackgroundService
{
    private static readonly TimeSpan IdleReconcileInterval = TimeSpan.FromSeconds(30);

    private readonly IHocVienPhotoProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HocVienPhotoProcessingWorker> _logger;

    public HocVienPhotoProcessingWorker(
        IHocVienPhotoProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<HocVienPhotoProcessingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverDurableBacklogSafelyAsync(
            includeInterruptedProcessing: true,
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_queue.PendingCount == 0)
            {
                await RecoverDurableBacklogSafelyAsync(
                    includeInterruptedProcessing: false,
                    stoppingToken);
            }

            HocVienPhotoProcessingWorkItem item;
            try
            {
                using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken);
                idleTimeout.CancelAfter(IdleReconcileInterval);
                item = await _queue.DequeueAsync(idleTimeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Wake periodically while idle so a transient database failure
                // cannot strand durable PENDING metadata until a process restart.
                continue;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetRequiredService<IHocVienPhotoProcessingService>();
                await service.ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Photo worker item failed unexpectedly. FailureType={FailureType}",
                    exception.GetType().Name);
            }
        }
    }

    private async Task RecoverDurableBacklogSafelyAsync(
        bool includeInterruptedProcessing,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider
                .GetRequiredService<IHocVienPhotoProcessingService>();
            var readiness = await service.GetReadinessAsync(cancellationToken);
            if (!readiness.IsReady)
            {
                return;
            }

            var repository = scope.ServiceProvider
                .GetRequiredService<IHocVienPhotoProcessingRepository>();
            var statuses = includeInterruptedProcessing
                ? new[]
                {
                    HocVienPhotoProcessingStatuses.Pending,
                    HocVienPhotoProcessingStatuses.Processing,
                }
                : new[] { HocVienPhotoProcessingStatuses.Pending };

            foreach (var status in statuses)
            {
                var records = await LoadSnapshotAsync(
                    repository,
                    status,
                    cancellationToken);
                foreach (var record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await service.ProcessAsync(
                        new HocVienPhotoProcessingWorkItem(
                            record.SourceProfileCode,
                            record.SourceMaDK,
                            record.MaKhoaHoc,
                            record.SourceImagePath,
                            record.SourcePathStatus ==
                                HocVienSourcePhotoStatuses.InvalidPath,
                            "SYSTEM_PHOTO_RECOVERY"),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Recovery is best-effort. A missing schema or transient database
            // failure must not stop the API process; durable PENDING/PROCESSING
            // rows remain available for the next drain cycle or process start.
            _logger.LogError(
                "Photo durable-backlog recovery failed. FailureType={FailureType}",
                exception.GetType().Name);
        }
    }

    private static async Task<IReadOnlyList<HocVienPhotoRecordDto>> LoadSnapshotAsync(
        IHocVienPhotoProcessingRepository repository,
        string status,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var records = new List<HocVienPhotoRecordDto>();
        var page = 1;
        while (true)
        {
            var result = await repository.SearchAsync(
                new HocVienPhotoSearchRequest
                {
                    Status = status,
                    Page = page,
                    PageSize = pageSize,
                },
                cancellationToken);
            records.AddRange(result.Items);
            if (page >= result.TotalPages)
            {
                return records;
            }

            page++;
        }
    }
}
