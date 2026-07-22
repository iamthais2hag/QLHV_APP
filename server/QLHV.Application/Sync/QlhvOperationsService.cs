using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed class QlhvOperationsService : IQlhvOperationsService
{
    public const string RefreshConfirmationText = "REFRESH CSDL BAK";

    private readonly IQlhvOperationsRepository _operations;
    private readonly IQlhvOperationHistoryRepository _history;
    private readonly IQlhvRefreshBackupQueue _queue;
    private readonly SyncOptions _syncOptions;
    private readonly SyncExecutionOptions _executionOptions;

    public QlhvOperationsService(
        IQlhvOperationsRepository operations,
        IQlhvOperationHistoryRepository history,
        IQlhvRefreshBackupQueue queue,
        IOptions<SyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions)
    {
        _operations = operations;
        _history = history;
        _queue = queue;
        _syncOptions = syncOptions.Value;
        _executionOptions = executionOptions.Value;
    }

    public async Task<QlhvOperationsStatusDto> GetStatusAsync(
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        var source = QlhvOperationSourceCatalog.GetRequired(sourceType);
        string? statusError = null;
        QlhvOperationHistoryDto? active = null;
        QlhvOperationHistoryDto? latestRefresh = null;
        QlhvOperationHistoryDto? latestSync = null;
        try
        {
            active = await _history.GetActiveAsync(source.SourceType, cancellationToken);
            latestRefresh = await _history.GetLatestCompletedAsync(
                source.SourceType,
                QlhvOperationTypes.RefreshBackup,
                cancellationToken);
            latestSync = await _history.GetLatestCompletedAsync(
                source.SourceType,
                QlhvOperationTypes.FullSync,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QlhvOperationsStoreUnavailableException ex)
        {
            statusError ??= ex.Message;
        }
        catch (Exception ex)
        {
            statusError ??= $"Khong doc duoc lich su van hanh. Chi tiet: {ex.GetType().Name}.";
        }

        QlhvOperationDataSnapshot data;
        if (active is { OperationType: QlhvOperationTypes.RefreshBackup })
        {
            // A RESTORE can temporarily put BAK in SINGLE_USER/RESTORING. Polling must not
            // compete for that database connection while the durable refresh row is active.
            data = new QlhvOperationDataSnapshot(new(), new(), 0, latestRefresh?.SnapshotToken);
        }
        else
        {
            try
            {
                data = await _operations.ReadStatusSnapshotAsync(source, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                data = new QlhvOperationDataSnapshot(new(), new(), 0, null);
                statusError ??= $"Khong doc duoc trang thai database. Chi tiet: {ex.GetType().Name}.";
            }
        }

        var latest = Latest(latestRefresh, latestSync);
        var hasActive = active is not null;
        var writesEnabled = !_syncOptions.DryRun && _executionOptions.EnableTargetWrites;
        var token = data.BackupSnapshotToken ?? latestRefresh?.SnapshotToken;
        var latestRefreshFailed = latestRefresh is { Status: QlhvOperationTypes.Failed };

        return new QlhvOperationsStatusDto
        {
            SourceType = source.SourceType,
            LiveDatabaseName = source.LiveDatabaseName,
            BackupDatabaseName = source.BackupDatabaseName,
            MaCSDT = source.MaCsdt,
            SourceProfileCode = source.SourceProfileCode,
            State = active is null ? ToIdleState(latest) : ToActiveState(active),
            ActiveOperationId = active?.OperationId,
            BackupLastRefreshTimeUtc = latestRefresh is { Status: QlhvOperationTypes.Succeeded }
                ? latestRefresh.CompletedAtUtc
                : null,
            BackupSnapshotToken = token,
            LiveRows = data.LiveRows,
            BackupRows = data.BackupRows,
            TargetActiveRows = data.TargetActiveRows,
            LastSyncTimeUtc = latestSync is { Status: QlhvOperationTypes.Succeeded }
                ? latestSync.CompletedAtUtc
                : null,
            LastError = statusError ?? (latest is { Status: QlhvOperationTypes.Failed }
                ? latest.ErrorMessage
                : null),
            CanRefresh = statusError is null && !hasActive && writesEnabled,
            CanSync = statusError is null &&
                      !hasActive &&
                      !latestRefreshFailed &&
                      writesEnabled &&
                      data.BackupRows.NguoiLX > 0 &&
                      !string.IsNullOrWhiteSpace(data.BackupSnapshotToken),
        };
    }

    public async Task<QlhvRefreshBackupResultDto> QueueRefreshBackupAsync(
        QlhvRefreshBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new QlhvRefreshBackupRequest();
        if (!QlhvOperationSourceCatalog.TryGet(request.SourceType, out var source))
        {
            return Rejected(request.SourceType, "SourceType chi ho tro OTO hoac MOTO.");
        }

        if (!string.Equals(request.ConfirmText, RefreshConfirmationText, StringComparison.Ordinal))
        {
            return Rejected(
                source.SourceType,
                $"ConfirmText phai khop chinh xac: {RefreshConfirmationText}.");
        }

        if (_syncOptions.DryRun)
        {
            return Rejected(source.SourceType, "Refresh BAK bi chan: Sync:DryRun = true.");
        }

        if (!_executionOptions.EnableTargetWrites)
        {
            return Rejected(
                source.SourceType,
                "Refresh BAK bi chan: SyncExecution.EnableTargetWrites = false.");
        }

        var operationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        bool created;
        try
        {
            created = await _history.TryCreateAsync(
                new QlhvOperationHistoryCreate(
                    operationId,
                    source,
                    QlhvOperationTypes.RefreshBackup,
                    QlhvOperationTypes.Queued,
                    now,
                    null),
                cancellationToken);
        }
        catch (QlhvOperationsStoreUnavailableException ex)
        {
            return new QlhvRefreshBackupResultDto
            {
                SourceType = source.SourceType,
                Status = "UNAVAILABLE",
                IsUnavailable = true,
                Message = ex.Message,
            };
        }

        if (!created)
        {
            return new QlhvRefreshBackupResultDto
            {
                SourceType = source.SourceType,
                Status = "CONFLICT",
                IsConflict = true,
                Message = $"Nguon {source.SourceType} dang co refresh hoac full sync chua ket thuc.",
            };
        }

        try
        {
            await _queue.EnqueueAsync(
                new QlhvRefreshBackupWorkItem(operationId, source.SourceType),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await SafeCompleteQueueFailureAsync(operationId, ex, CancellationToken.None);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new QlhvRefreshBackupResultDto
            {
                SourceType = source.SourceType,
                OperationId = operationId,
                Status = "UNAVAILABLE",
                IsUnavailable = true,
                Message = "Khong the dua refresh BAK vao hang doi.",
            };
        }

        return new QlhvRefreshBackupResultDto
        {
            Accepted = true,
            OperationId = operationId,
            SourceType = source.SourceType,
            Status = QlhvOperationTypes.Queued,
            Message = "Da xep lich lam moi database BAK.",
        };
    }

    public Task<IReadOnlyList<QlhvOperationHistoryDto>> GetHistoryAsync(
        string sourceType,
        CancellationToken cancellationToken = default)
    {
        var source = QlhvOperationSourceCatalog.GetRequired(sourceType);
        return _history.SearchAsync(source.SourceType, 50, cancellationToken);
    }

    private async Task SafeCompleteQueueFailureAsync(
        Guid operationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await _history.CompleteAsync(
                new QlhvOperationHistoryCompletion(
                    operationId,
                    QlhvOperationTypes.Failed,
                    DateTime.UtcNow,
                    0, 0, 0, 0, 0, 0,
                    null,
                    $"Queue failed: {exception.GetType().Name}.",
                    null),
                cancellationToken);
        }
        catch
        {
            // Preserve the original queue error. No secret or request header is logged here.
        }
    }

    private static QlhvRefreshBackupResultDto Rejected(string? sourceType, string message) => new()
    {
        SourceType = sourceType?.Trim().ToUpperInvariant() ?? string.Empty,
        Status = "REJECTED",
        Message = message,
    };

    private static QlhvOperationHistoryDto? Latest(
        QlhvOperationHistoryDto? first,
        QlhvOperationHistoryDto? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return Nullable.Compare(first.CompletedAtUtc, second.CompletedAtUtc) >= 0 ? first : second;
    }

    private static string ToActiveState(QlhvOperationHistoryDto active)
        => string.Equals(active.OperationType, QlhvOperationTypes.RefreshBackup, StringComparison.Ordinal)
            ? "refreshing"
            : "syncing";

    private static string ToIdleState(QlhvOperationHistoryDto? latest)
        => latest?.Status switch
        {
            QlhvOperationTypes.Succeeded => "succeeded",
            QlhvOperationTypes.Failed => "failed",
            _ => "idle",
        };
}
