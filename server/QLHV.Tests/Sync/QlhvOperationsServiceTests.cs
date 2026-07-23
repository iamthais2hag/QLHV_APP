using Microsoft.Extensions.Options;
using QLHV.Application.Auth;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using AppSyncOptions = QLHV.Application.Sync.SyncOptions;

namespace QLHV.Tests.Sync;

public sealed class QlhvOperationsServiceTests
{
    [Fact]
    public async Task Refresh_rejects_unknown_source_before_history_or_queue()
    {
        var history = new FakeHistoryRepository();
        var queue = new FakeQueue();
        var service = CreateService(history, queue, dryRun: false, enableWrites: true);

        var result = await service.QueueRefreshBackupAsync(new QlhvRefreshBackupRequest
        {
            SourceType = "CUSTOM",
        });

        Assert.False(result.Accepted);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(0, history.CreateCalls);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Refresh_safety_configuration_blocks_before_history(
        bool dryRun,
        bool enableWrites)
    {
        var history = new FakeHistoryRepository();
        var queue = new FakeQueue();
        var service = CreateService(history, queue, dryRun, enableWrites);

        var result = await service.QueueRefreshBackupAsync(ValidRefreshRequest());

        Assert.False(result.Accepted);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(0, history.CreateCalls);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task Refresh_double_submit_is_rejected_by_durable_history_reservation()
    {
        var history = new FakeHistoryRepository { TryCreateResult = false };
        var queue = new FakeQueue();
        var service = CreateService(history, queue, dryRun: false, enableWrites: true);

        var result = await service.QueueRefreshBackupAsync(ValidRefreshRequest());

        Assert.False(result.Accepted);
        Assert.True(result.IsConflict);
        Assert.Equal("CONFLICT", result.Status);
        Assert.Equal(1, history.CreateCalls);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task Refresh_accepts_only_fixed_source_and_queues_operation_id()
    {
        var history = new FakeHistoryRepository();
        var queue = new FakeQueue();
        var service = CreateService(history, queue, dryRun: false, enableWrites: true);

        var result = await service.QueueRefreshBackupAsync(ValidRefreshRequest());

        Assert.True(result.Accepted);
        Assert.Equal(QlhvOperationTypes.Queued, result.Status);
        Assert.NotNull(result.OperationId);
        var created = Assert.Single(history.Created);
        Assert.Equal(result.OperationId, created.OperationId);
        Assert.Equal("OTO", created.Source.SourceType);
        Assert.Equal("CSDL_OTO", created.Source.LiveDatabaseName);
        Assert.Equal("CSDL_OTO_BAK", created.Source.BackupDatabaseName);
        Assert.Equal(QlhvOperationTypes.RefreshBackup, created.OperationType);
        Assert.Equal(QlhvOperationTypes.Queued, created.Status);
        Assert.Null(created.StartedAtUtc);
        Assert.Equal(result.OperationId, Assert.Single(queue.Items).OperationId);
    }

    [Fact]
    public async Task Queue_failure_closes_history_with_non_cancelable_cleanup()
    {
        var history = new FakeHistoryRepository();
        var queue = new FakeQueue { Exception = new InvalidOperationException("test queue failure") };
        var service = CreateService(history, queue, dryRun: false, enableWrites: true);

        var result = await service.QueueRefreshBackupAsync(ValidRefreshRequest());

        Assert.False(result.Accepted);
        Assert.True(result.IsUnavailable);
        var completion = Assert.Single(history.Completed);
        Assert.Equal(QlhvOperationTypes.Failed, completion.Value.Status);
        Assert.False(completion.Token.CanBeCanceled);
        Assert.DoesNotContain("test queue failure", completion.Value.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_allows_sync_only_for_nonempty_current_snapshot_with_no_failed_refresh()
    {
        var history = new FakeHistoryRepository();
        var operations = new FakeOperationsRepository
        {
            Snapshot = new QlhvOperationDataSnapshot(
                new QlhvOperationRowCountsDto { NguoiLX = 46 },
                new QlhvOperationRowCountsDto { NguoiLX = 46 },
                40,
                "snapshot-token"),
        };
        var service = CreateService(
            history,
            new FakeQueue(),
            dryRun: false,
            enableWrites: true,
            operations: operations);

        var healthy = await service.GetStatusAsync("OTO", AppRoles.Admin, writeAuthorized: true);
        Assert.True(healthy.CanRefresh);
        Assert.True(healthy.CanSync);
        Assert.False(healthy.DryRun);
        Assert.True(healthy.TargetWritesEnabled);
        Assert.True(healthy.WriteAuthorized);
        Assert.Equal(AppRoles.Admin, healthy.CurrentUserRole);
        Assert.Empty(healthy.RefreshBlockers);
        Assert.Empty(healthy.SyncBlockers);

        history.LatestRefresh = CompletedHistory(
            QlhvOperationTypes.RefreshBackup,
            QlhvOperationTypes.Failed);
        var failedRefresh = await service.GetStatusAsync("OTO", AppRoles.Admin, writeAuthorized: true);
        Assert.True(failedRefresh.CanRefresh);
        Assert.False(failedRefresh.CanSync);
        Assert.Contains(failedRefresh.SyncBlockers, blocker => blocker.Contains("thất bại", StringComparison.Ordinal));

        history.Active = ActiveHistory(QlhvOperationTypes.FullSync);
        var busy = await service.GetStatusAsync("OTO", AppRoles.Admin, writeAuthorized: true);
        Assert.False(busy.CanRefresh);
        Assert.False(busy.CanSync);
        Assert.Equal("syncing", busy.State);
        Assert.Contains("Nguồn đang có thao tác khác.", busy.RefreshBlockers);
    }

    [Theory]
    [InlineData(true, true, "Chế độ DryRun đang bật.")]
    [InlineData(false, false, "Quyền ghi dữ liệu đang tắt.")]
    public async Task Status_reports_the_exact_safety_configuration_blocker(
        bool dryRun,
        bool enableWrites,
        string expectedBlocker)
    {
        var service = CreateService(
            new FakeHistoryRepository(),
            new FakeQueue(),
            dryRun,
            enableWrites,
            HealthyOperations());

        var status = await service.GetStatusAsync("OTO", AppRoles.Admin, writeAuthorized: true);

        Assert.Equal(dryRun, status.DryRun);
        Assert.Equal(enableWrites, status.TargetWritesEnabled);
        Assert.False(status.CanRefresh);
        Assert.False(status.CanSync);
        Assert.Contains(expectedBlocker, status.RefreshBlockers);
        Assert.Contains(expectedBlocker, status.SyncBlockers);
    }

    [Fact]
    public async Task Viewer_status_never_grants_refresh_or_sync_when_runtime_flags_are_enabled()
    {
        var service = CreateService(
            new FakeHistoryRepository(),
            new FakeQueue(),
            dryRun: false,
            enableWrites: true,
            operations: HealthyOperations());

        var status = await service.GetStatusAsync("OTO", AppRoles.Viewer, writeAuthorized: false);

        Assert.Equal(AppRoles.Viewer, status.CurrentUserRole);
        Assert.False(status.WriteAuthorized);
        Assert.False(status.CanRefresh);
        Assert.False(status.CanSync);
        Assert.Contains("Bạn không có quyền Admin.", status.RefreshBlockers);
        Assert.Contains("Bạn không có quyền Admin.", status.SyncBlockers);
    }

    [Fact]
    public async Task Status_polling_does_not_open_bak_while_refresh_is_active()
    {
        var history = new FakeHistoryRepository
        {
            Active = ActiveHistory(QlhvOperationTypes.RefreshBackup),
        };
        var operations = new FakeOperationsRepository();
        var service = CreateService(
            history,
            new FakeQueue(),
            dryRun: false,
            enableWrites: true,
            operations: operations);

        var status = await service.GetStatusAsync("OTO", AppRoles.Admin, writeAuthorized: true);

        Assert.Equal("refreshing", status.State);
        Assert.False(status.CanRefresh);
        Assert.False(status.CanSync);
        Assert.Equal(0, operations.ReadCalls);
    }

    private static QlhvOperationsService CreateService(
        FakeHistoryRepository history,
        FakeQueue queue,
        bool dryRun,
        bool enableWrites,
        FakeOperationsRepository? operations = null)
        => new(
            operations ?? new FakeOperationsRepository(),
            history,
            queue,
            Options.Create(new AppSyncOptions { DryRun = dryRun }),
            Options.Create(new SyncExecutionOptions { EnableTargetWrites = enableWrites }));

    private static QlhvRefreshBackupRequest ValidRefreshRequest() => new()
    {
        SourceType = "OTO",
    };

    private static FakeOperationsRepository HealthyOperations() => new()
    {
        Snapshot = new QlhvOperationDataSnapshot(
            new QlhvOperationRowCountsDto { NguoiLX = 46 },
            new QlhvOperationRowCountsDto { NguoiLX = 46 },
            40,
            "snapshot-token"),
    };

    private static QlhvOperationHistoryDto CompletedHistory(string operationType, string status) => new()
    {
        OperationId = Guid.NewGuid(),
        SourceType = "OTO",
        OperationType = operationType,
        Status = status,
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        CompletedAtUtc = DateTime.UtcNow,
        ErrorMessage = status == QlhvOperationTypes.Failed ? "safe failure" : null,
    };

    private static QlhvOperationHistoryDto ActiveHistory(string operationType) => new()
    {
        OperationId = Guid.NewGuid(),
        SourceType = "OTO",
        OperationType = operationType,
        Status = QlhvOperationTypes.Running,
        StartedAtUtc = DateTime.UtcNow,
    };

    private sealed class FakeOperationsRepository : IQlhvOperationsRepository
    {
        public int ReadCalls { get; private set; }
        public QlhvOperationDataSnapshot Snapshot { get; init; } = new(
            new QlhvOperationRowCountsDto(),
            new QlhvOperationRowCountsDto(),
            0,
            null);

        public Task<QlhvOperationDataSnapshot> ReadStatusSnapshotAsync(
            QlhvOperationSourceDefinition source,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeQueue : IQlhvRefreshBackupQueue
    {
        public int EnqueueCalls { get; private set; }
        public Exception? Exception { get; init; }
        public List<QlhvRefreshBackupWorkItem> Items { get; } = new();

        public ValueTask EnqueueAsync(
            QlhvRefreshBackupWorkItem item,
            CancellationToken cancellationToken = default)
        {
            EnqueueCalls++;
            if (Exception is not null)
            {
                return ValueTask.FromException(Exception);
            }

            Items.Add(item);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHistoryRepository : IQlhvOperationHistoryRepository
    {
        public bool TryCreateResult { get; init; } = true;
        public int CreateCalls { get; private set; }
        public List<QlhvOperationHistoryCreate> Created { get; } = new();
        public List<(QlhvOperationHistoryCompletion Value, CancellationToken Token)> Completed { get; } = new();
        public QlhvOperationHistoryDto? Active { get; set; }
        public QlhvOperationHistoryDto? LatestRefresh { get; set; }
        public QlhvOperationHistoryDto? LatestSync { get; set; }

        public Task<bool> TryCreateAsync(
            QlhvOperationHistoryCreate entry,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            Created.Add(entry);
            return Task.FromResult(TryCreateResult);
        }

        public Task MarkRunningAsync(Guid operationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompleteAsync(
            QlhvOperationHistoryCompletion completion,
            CancellationToken cancellationToken = default)
        {
            Completed.Add((completion, cancellationToken));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QlhvOperationHistoryDto>> SearchAsync(
            string sourceType,
            int take,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QlhvOperationHistoryDto>>(Array.Empty<QlhvOperationHistoryDto>());

        public Task<QlhvOperationHistoryDto?> GetActiveAsync(
            string sourceType,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Active);

        public Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
            string sourceType,
            string operationType,
            CancellationToken cancellationToken = default)
            => Task.FromResult(operationType == QlhvOperationTypes.RefreshBackup
                ? LatestRefresh
                : LatestSync);
    }
}
