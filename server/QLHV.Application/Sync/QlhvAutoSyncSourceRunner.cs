using Microsoft.Extensions.Options;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Application.Sync;

public sealed class QlhvAutoSyncSourceRunner : IQlhvAutoSyncSourceRunner
{
    private readonly IQlhvOperationsService _operations;
    private readonly IQlhvOperationHistoryRepository _history;
    private readonly IQlhvImportService _import;
    private readonly IQlhvAutoSyncRunRepository _runs;
    private readonly QlhvAutoSyncOptions _options;

    public QlhvAutoSyncSourceRunner(
        IQlhvOperationsService operations,
        IQlhvOperationHistoryRepository history,
        IQlhvImportService import,
        IQlhvAutoSyncRunRepository runs,
        IOptions<QlhvAutoSyncOptions> options)
    {
        _operations = operations;
        _history = history;
        _import = import;
        _runs = runs;
        _options = options.Value;
    }

    public async Task<QlhvAutoSyncSourceResultDto> RunAsync(
        Guid runId,
        string sourceType,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var source = QlhvOperationSourceCatalog.GetRequired(sourceType);
        var operationActor = QlhvOperationActors.NormalizeInternal(actor);
        var startedAtUtc = DateTime.UtcNow;
        Guid? refreshOperationId = null;
        Guid? syncOperationId = null;

        try
        {
            if (!_options.RefreshBackupBeforeSync)
            {
                return Failed(
                    source.SourceType,
                    startedAtUtc,
                    null,
                    null,
                    "Auto Sync bi chan: bat buoc refresh BAK truoc full sync.");
            }

            if (_options.RefreshBackupBeforeSync)
            {
                await _runs.SetCurrentStageAsync(
                    runId,
                    QlhvAutoSyncConstants.RefreshStage(source.SourceType),
                    cancellationToken);
                var refresh = await _operations.QueueRefreshBackupAsync(
                    new QlhvRefreshBackupRequest
                    {
                        SourceType = source.SourceType,
                        Actor = operationActor,
                    },
                    cancellationToken);
                refreshOperationId = refresh.OperationId;
                if (!refresh.Accepted || refresh.OperationId is null)
                {
                    return Failed(
                        source.SourceType,
                        startedAtUtc,
                        refreshOperationId,
                        null,
                        $"Refresh BAK khong duoc chap nhan: {refresh.Message}");
                }

                var refreshHistory = await WaitForTerminalOperationAsync(
                    refresh.OperationId.Value,
                    cancellationToken);
                if (!string.Equals(
                        refreshHistory.Status,
                        QlhvOperationTypes.Succeeded,
                        StringComparison.Ordinal))
                {
                    return Failed(
                        source.SourceType,
                        startedAtUtc,
                        refreshOperationId,
                        null,
                        refreshHistory.ErrorMessage ??
                        "Refresh BAK that bai; full sync bi bo qua de khong dung snapshot cu.");
                }
            }

            await _runs.SetCurrentStageAsync(
                runId,
                QlhvAutoSyncConstants.SyncStage(source.SourceType),
                cancellationToken);
            var request = new QlhvImportRequest
            {
                SourceProfileCode = source.SourceProfileCode,
                MaCSDT = source.MaCsdt,
            };
            var plan = await _import.GetPlanAsync(request, cancellationToken);
            if (!plan.Executable || string.IsNullOrWhiteSpace(plan.BackupSnapshotToken))
            {
                var blocker = plan.Blockers.FirstOrDefault() ??
                              plan.HocVienBlockers.FirstOrDefault();
                return NeedsPlan(
                    source.SourceType,
                    startedAtUtc,
                    refreshOperationId,
                    null,
                    blocker ?? "Khong tao duoc plan full sync co snapshot token hop le.");
            }

            var execute = await _import.ExecuteAsync(
                new QlhvImportExecuteRequest
                {
                    SourceProfileCode = source.SourceProfileCode,
                    MaCSDT = source.MaCsdt,
                    ExpectedSnapshotToken = plan.BackupSnapshotToken,
                    Actor = operationActor,
                },
                cancellationToken);
            syncOperationId = execute.OperationId;
            if (!execute.Executed)
            {
                if (RequiresNewPlan(execute))
                {
                    return NeedsPlan(
                        source.SourceType,
                        startedAtUtc,
                        refreshOperationId,
                        syncOperationId,
                        execute.Message);
                }

                return Failed(
                    source.SourceType,
                    startedAtUtc,
                    refreshOperationId,
                    syncOperationId,
                    execute.Message,
                    execute.DomainResults,
                    execute.PhotoProcessing,
                    execute.SkippedReasons,
                    execute.Plan.Warnings);
            }

            if (string.Equals(
                    execute.Status,
                    QlhvImportOverallStatuses.Failed,
                    StringComparison.Ordinal))
            {
                return Failed(
                    source.SourceType,
                    startedAtUtc,
                    refreshOperationId,
                    syncOperationId,
                    execute.Message);
            }

            return new QlhvAutoSyncSourceResultDto
            {
                SourceType = source.SourceType,
                Status = string.Equals(
                    execute.Status,
                    QlhvImportOverallStatuses.PartialSuccess,
                    StringComparison.Ordinal)
                    ? QlhvAutoSyncConstants.PartialSuccess
                    : QlhvAutoSyncConstants.Succeeded,
                RefreshOperationId = refreshOperationId,
                SyncOperationId = syncOperationId,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTime.UtcNow,
                Message = execute.Message,
                DomainResults = execute.DomainResults,
                PhotoProcessing = execute.PhotoProcessing,
                SkippedReasons = execute.SkippedReasons,
                Warnings = execute.Plan.Warnings,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                source.SourceType,
                startedAtUtc,
                refreshOperationId,
                syncOperationId,
                $"Auto Sync source failed: {ex.GetType().Name}.");
        }
    }

    private async Task<QlhvOperationHistoryDto> WaitForTerminalOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(
            Math.Clamp(_options.OperationPollMilliseconds, 100, 5000));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = await _history.GetByOperationIdAsync(operationId, cancellationToken);
            if (row is null)
            {
                throw new InvalidOperationException("Khong tim thay operation refresh vua xep hang.");
            }

            if (string.Equals(row.Status, QlhvOperationTypes.Succeeded, StringComparison.Ordinal) ||
                string.Equals(row.Status, QlhvOperationTypes.Failed, StringComparison.Ordinal))
            {
                return row;
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private static QlhvAutoSyncSourceResultDto Failed(
        string sourceType,
        DateTime startedAtUtc,
        Guid? refreshOperationId,
        Guid? syncOperationId,
        string message,
        IReadOnlyList<QlhvImportDomainResultDto>? domainResults = null,
        QlhvImportDomainResultDto? photoProcessing = null,
        QlhvSkippedReasonCountsDto? skippedReasons = null,
        IReadOnlyList<string>? warnings = null)
        => new()
        {
            SourceType = sourceType,
            Status = QlhvAutoSyncConstants.Failed,
            RefreshOperationId = refreshOperationId,
            SyncOperationId = syncOperationId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            Message = message,
            DomainResults = domainResults ?? Array.Empty<QlhvImportDomainResultDto>(),
            PhotoProcessing = photoProcessing,
            SkippedReasons = skippedReasons ?? new QlhvSkippedReasonCountsDto(),
            Warnings = warnings ?? Array.Empty<string>(),
        };

    private static QlhvAutoSyncSourceResultDto NeedsPlan(
        string sourceType,
        DateTime startedAtUtc,
        Guid? refreshOperationId,
        Guid? syncOperationId,
        string message)
        => new()
        {
            SourceType = sourceType,
            Status = QlhvAutoSyncConstants.NeedsPlan,
            RefreshOperationId = refreshOperationId,
            SyncOperationId = syncOperationId,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTime.UtcNow,
            Message = message,
        };

    private static bool RequiresNewPlan(QlhvImportExecuteResultDto result)
        => result.Plan.Blockers
            .Concat(result.Plan.HocVienBlockers)
            .Any(blocker =>
                blocker.Contains("ExpectedSnapshotToken", StringComparison.OrdinalIgnoreCase) ||
                blocker.Contains("Plan da cu", StringComparison.OrdinalIgnoreCase) ||
                blocker.Contains("snapshot BAK", StringComparison.OrdinalIgnoreCase));
}
