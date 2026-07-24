using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.SystemData;

namespace QLHV.Application.Sync;

public sealed class QlhvAutoSyncService : IQlhvAutoSyncService
{
    private readonly IQlhvAutoSyncRunRepository _runs;
    private readonly IQlhvAutoSyncQueue _queue;
    private readonly ISystemDataVersionRepository _dataVersion;
    private readonly IQlhvSyncFreshnessService _freshness;
    private readonly IQlhvOperationHistoryRepository _history;
    private readonly IRuntimeReadinessService _readiness;
    private readonly QlhvAutoSyncOptions _options;
    private readonly SyncOptions _syncOptions;
    private readonly SyncExecutionOptions _executionOptions;

    public QlhvAutoSyncService(
        IQlhvAutoSyncRunRepository runs,
        IQlhvAutoSyncQueue queue,
        ISystemDataVersionRepository dataVersion,
        IQlhvSyncFreshnessService freshness,
        IQlhvOperationHistoryRepository history,
        IRuntimeReadinessService readiness,
        IOptions<QlhvAutoSyncOptions> options,
        IOptions<SyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions)
    {
        _runs = runs;
        _queue = queue;
        _dataVersion = dataVersion;
        _freshness = freshness;
        _history = history;
        _readiness = readiness;
        _options = options.Value;
        _syncOptions = syncOptions.Value;
        _executionOptions = executionOptions.Value;
    }

    public async Task<QlhvAutoSyncQueueResultDto> QueueAsync(
        string triggerType,
        CancellationToken cancellationToken = default)
    {
        string trigger;
        try
        {
            trigger = QlhvAutoSyncConstants.NormalizeTrigger(triggerType);
        }
        catch (ArgumentException ex)
        {
            return Rejected(ex.Message);
        }

        return string.Equals(trigger, QlhvAutoSyncConstants.StartupTrigger, StringComparison.Ordinal)
            ? await QueueIfNeededAsync(trigger, serverStartedByLauncher: true, cancellationToken)
            : await QueueCoreAsync(trigger, joinActiveRun: false, cancellationToken);
    }

    public Task<QlhvAutoSyncQueueResultDto> QueueSessionStartAsync(
        bool serverStartedByLauncher,
        CancellationToken cancellationToken = default)
        => QueueIfNeededAsync(
            QlhvAutoSyncConstants.SessionStartTrigger,
            serverStartedByLauncher,
            cancellationToken);

    private async Task<QlhvAutoSyncQueueResultDto> QueueIfNeededAsync(
        string trigger,
        bool serverStartedByLauncher,
        CancellationToken cancellationToken)
    {
        QlhvSessionStartStatusDto status;
        try
        {
            status = await GetSessionStartStatusAsync(
                serverStartedByLauncher,
                runId: null,
                cancellationToken);
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return Unavailable(ex.Message);
        }

        if (status.OperationActive)
        {
            if (status.ActiveRunId.HasValue)
            {
                var active = await _runs.GetByIdAsync(status.ActiveRunId.Value, cancellationToken);
                if (active is not null)
                {
                    return Joined(active, "Dang tham gia Auto Sync da duoc mot phien khac khoi tao.");
                }
            }

            return new QlhvAutoSyncQueueResultDto
            {
                IsConflict = true,
                Status = "CONFLICT",
                Message =
                    "Dang co thao tac refresh/full sync thu cong; phien launcher se cho thao tac ket thuc.",
            };
        }

        if (!status.NeedSync)
        {
            return new QlhvAutoSyncQueueResultDto
            {
                Accepted = true,
                Status = "UP_TO_DATE",
                Message = "Du lieu Live, BAK va QLHV_APP da dong bo; khong tao operation moi.",
            };
        }

        if (!status.CanStart)
        {
            return Rejected(status.Blockers.FirstOrDefault() ?? "Auto Sync chua du dieu kien khoi dong.");
        }

        return await QueueCoreAsync(trigger, joinActiveRun: true, cancellationToken);
    }

    private async Task<QlhvAutoSyncQueueResultDto> QueueCoreAsync(
        string triggerType,
        bool joinActiveRun,
        CancellationToken cancellationToken)
    {
        string trigger;
        IReadOnlyList<string> sourceOrder;
        try
        {
            trigger = QlhvAutoSyncConstants.NormalizeTrigger(triggerType);
            sourceOrder = QlhvAutoSyncConstants.NormalizeSourceOrder(_options.SourceOrder);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Rejected(ex.Message);
        }

        try
        {
            if (joinActiveRun)
            {
                var active = await _runs.GetActiveAsync(cancellationToken);
                if (active is not null)
                {
                    return Joined(active, "Dang tham gia Auto Sync da duoc mot phien khac khoi tao.");
                }
            }

            foreach (var source in OrderedSources())
            {
                var manual = await _history.GetActiveAsync(source.SourceType, cancellationToken);
                if (manual is not null)
                {
                    return new QlhvAutoSyncQueueResultDto
                    {
                        IsConflict = true,
                        Status = "CONFLICT",
                        Message =
                            $"Dang co thao tac refresh/full sync thu cong cho {source.SourceType}; khong tao Auto Sync moi.",
                    };
                }
            }
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return Unavailable(ex.Message);
        }
        catch (QlhvOperationsStoreUnavailableException ex)
        {
            return Unavailable(ex.Message);
        }

        var startBlockers = GetStartBlockers();
        if (startBlockers.Count > 0)
        {
            return Rejected(startBlockers[0]);
        }

        var runId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;
        bool created;
        try
        {
            created = await _runs.TryCreateAsync(
                new QlhvAutoSyncRunCreate(
                    runId,
                    trigger,
                    ResolveActor(trigger),
                    sourceOrder,
                    createdAtUtc,
                    DedupeNotBeforeUtc: null),
                cancellationToken);
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return Unavailable(ex.Message);
        }

        if (!created)
        {
            if (joinActiveRun ||
                string.Equals(trigger, QlhvAutoSyncConstants.StartupTrigger, StringComparison.Ordinal))
            {
                try
                {
                    var existing = await _runs.GetActiveAsync(cancellationToken);
                    if (existing is not null)
                    {
                        return Joined(
                            existing,
                            "Auto Sync da duoc mot process hoac phien launcher khac tiep nhan.");
                    }
                }
                catch (QlhvAutoSyncStoreUnavailableException ex)
                {
                    return Unavailable(ex.Message);
                }
            }

            return new QlhvAutoSyncQueueResultDto
            {
                IsConflict = true,
                Status = "CONFLICT",
                Message = "Da co Auto Sync dang cho hoac dang chay tren mot tien trinh khac.",
            };
        }

        try
        {
            await _queue.EnqueueAsync(new QlhvAutoSyncWorkItem(runId), cancellationToken);
        }
        catch (Exception ex)
        {
            await SafeCompleteQueueFailureAsync(runId, ex);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new QlhvAutoSyncQueueResultDto
            {
                IsUnavailable = true,
                RunId = runId,
                Status = "UNAVAILABLE",
                Message = "Khong the dua Auto Sync vao hang doi.",
            };
        }

        return new QlhvAutoSyncQueueResultDto
        {
            Accepted = true,
            RunId = runId,
            Status = QlhvAutoSyncConstants.Queued,
            Message = "Da xep lich Auto Sync OTO roi MOTO.",
        };
    }

    public async Task<QlhvSessionStartStatusDto> GetSessionStartStatusAsync(
        bool serverStartedByLauncher,
        Guid? runId = null,
        CancellationToken cancellationToken = default)
    {
        if (runId.HasValue)
        {
            return await GetExactRunStatusAsync(runId.Value, cancellationToken);
        }

        var startBlockers = GetStartBlockers();
        var active = await _runs.GetActiveAsync(cancellationToken);
        var appVersion = await GetDataVersionAsync(cancellationToken);
        if (active is not null)
        {
            return CreateRunStatus(
                active,
                startBlockers,
                appVersion,
                lastAttemptUtc: active.CreatedAtUtc,
                lastError: active.ErrorMessage);
        }

        foreach (var source in OrderedSources())
        {
            var manual = await _history.GetActiveAsync(source.SourceType, cancellationToken);
            if (manual is not null)
            {
                return new QlhvSessionStartStatusDto
                {
                    ServerReady = true,
                    OperationActive = true,
                    NeedSync = true,
                    CanStart = false,
                    State = "waiting-operation",
                    CurrentSourceType = source.SourceType,
                    AppDataVersion = appVersion,
                    LastSuccessfulSyncUtc = appVersion?.LastSuccessfulSyncUtc,
                    LastAttemptUtc = manual.StartedAtUtc,
                    NeedSyncReasons = [$"{source.SourceType}:THAO_TAC_THU_CONG_DANG_CHAY"],
                    Blockers = startBlockers,
                    Message =
                        $"Dang co thao tac {source.SourceType}; launcher cho operation hien tai ket thuc.",
                };
            }
        }

        var runtime = await _readiness.GetStatusAsync(cancellationToken);
        if (!runtime.IsReady)
        {
            var runtimeBlockers = startBlockers
                .Concat(runtime.Messages)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new QlhvSessionStartStatusDto
            {
                ServerReady = false,
                NeedSync = true,
                CanStart = false,
                State = "not-ready",
                AppDataVersion = appVersion,
                LastSuccessfulSyncUtc = appVersion?.LastSuccessfulSyncUtc,
                NeedSyncReasons = ["SERVER_CHUA_SAN_SANG"],
                Blockers = runtimeBlockers,
                Message = runtimeBlockers.FirstOrDefault() ?? "May chu chua san sang.",
            };
        }

        var freshness = await _freshness.EvaluateAsync(cancellationToken);
        var latest = await _runs.GetLatestAsync(cancellationToken);
        var blockers = startBlockers
            .Concat(freshness.Blockers)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (appVersion is null)
        {
            blockers.Add("Khong doc duoc dbo.App_DataVersion.");
        }

        return new QlhvSessionStartStatusDto
        {
            ServerReady = true,
            OperationActive = false,
            NeedSync = freshness.NeedSync,
            CanStart = freshness.NeedSync && blockers.Count == 0,
            State = freshness.NeedSync ? "idle" : "up-to-date",
            LastSuccessfulSyncUtc = appVersion?.LastSuccessfulSyncUtc,
            LastAttemptUtc = latest?.CreatedAtUtc,
            LastError = latest?.ErrorMessage,
            NeedSyncReasons = freshness.Reasons,
            LiveSnapshot = freshness.LiveSnapshots,
            BackupSnapshot = freshness.BackupSnapshots,
            Partitions = freshness.Partitions,
            AppDataVersion = appVersion,
            Blockers = blockers,
            Message = freshness.NeedSync
                ? blockers.Count == 0
                    ? "Can khoi tao mot phien du lieu moi."
                    : blockers[0]
                : "Du lieu Live, BAK va QLHV_APP da dong bo.",
        };
    }

    private async Task<QlhvSessionStartStatusDto> GetExactRunStatusAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var blockers = GetStartBlockers();
        var run = await _runs.GetByIdAsync(runId, cancellationToken);
        var appVersion = await GetDataVersionAsync(cancellationToken);
        if (run is null)
        {
            return new QlhvSessionStartStatusDto
            {
                Found = false,
                ServerReady = true,
                CanStart = blockers.Count == 0,
                State = "not-found",
                AppDataVersion = appVersion,
                LastSuccessfulSyncUtc = appVersion?.LastSuccessfulSyncUtc,
                Blockers = blockers,
                Message = "Khong tim thay phien Auto Sync duoc yeu cau.",
            };
        }

        var latest = await _runs.GetLatestAsync(cancellationToken);
        return CreateRunStatus(
            run,
            blockers,
            appVersion,
            latest?.CreatedAtUtc ?? run.CreatedAtUtc,
            latest?.ErrorMessage);
    }

    private static QlhvSessionStartStatusDto CreateRunStatus(
        QlhvAutoSyncRunRecord run,
        IReadOnlyList<string> blockers,
        SystemDataVersionDto? appVersion,
        DateTime? lastAttemptUtc,
        string? lastError)
    {
        var isTerminal = IsTerminal(run.Status);
        var succeeded = string.Equals(
            run.Status,
            QlhvAutoSyncConstants.Succeeded,
            StringComparison.Ordinal) ||
            string.Equals(
                run.Status,
                QlhvAutoSyncConstants.PartialSuccess,
                StringComparison.Ordinal);
        return new QlhvSessionStartStatusDto
        {
            ServerReady = true,
            OperationActive = !isTerminal,
            ActiveRunId = isTerminal ? null : run.RunId,
            NeedSync = !succeeded,
            CanStart = isTerminal && blockers.Count == 0,
            RunId = run.RunId,
            State = ToState(run),
            CurrentSourceType = run.CurrentSourceType,
            CurrentStage = run.CurrentStage,
            IsTerminal = isTerminal,
            Succeeded = succeeded,
            CompletedAtUtc = run.CompletedAtUtc,
            LastSuccessfulSyncUtc = appVersion?.LastSuccessfulSyncUtc,
            LastAttemptUtc = lastAttemptUtc,
            ErrorMessage = run.ErrorMessage,
            LastError = lastError,
            AppDataVersion = appVersion,
            Blockers = blockers,
            Message = isTerminal
                ? "Phien Auto Sync da ket thuc."
                : "Dang theo doi phien Auto Sync hien tai.",
        };
    }

    public async Task<QlhvAutoSyncStatusDto> GetStatusAsync(
        Guid? runId = null,
        CancellationToken cancellationToken = default)
    {
        var active = runId.HasValue
            ? null
            : await _runs.GetActiveAsync(cancellationToken);
        var latest = runId.HasValue
            ? await _runs.GetByIdAsync(runId.Value, cancellationToken)
            : active ?? await _runs.GetLatestAsync(cancellationToken);
        var dataVersion = await GetDataVersionAsync(cancellationToken);

        return new QlhvAutoSyncStatusDto
        {
            Found = !runId.HasValue || latest is not null,
            Enabled = _options.Enabled,
            RunOnServerStartup = _options.RunOnServerStartup,
            RefreshBackupBeforeSync = _options.RefreshBackupBeforeSync,
            State = !_options.Enabled
                ? "disabled"
                : runId.HasValue && latest is null
                    ? "not-found"
                    : ToState(latest),
            RunId = latest?.RunId,
            ActiveRunId = latest is not null && !IsTerminal(latest.Status)
                ? latest.RunId
                : active?.RunId,
            TriggerType = latest?.TriggerType,
            Actor = latest?.Actor,
            CurrentSourceType = latest?.CurrentSourceType,
            CurrentStage = active?.CurrentStage ?? latest?.CurrentStage,
            StartedAtUtc = latest?.StartedAtUtc,
            CompletedAtUtc = latest?.CompletedAtUtc,
            LastSuccessfulSyncUtc = dataVersion?.LastSuccessfulSyncUtc,
            Oto = latest?.Oto,
            Moto = latest?.Moto,
            LastError = latest?.ErrorMessage ??
                (runId.HasValue && latest is null
                    ? "Khong tim thay phien Auto Sync duoc yeu cau."
                    : null),
        };
    }

    private async Task<SystemDataVersionDto?> GetDataVersionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dataVersion.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> GetStartBlockers()
    {
        if (!_options.Enabled)
        {
            return ["Auto Sync dang tat trong cau hinh."];
        }

        if (!_options.RefreshBackupBeforeSync)
        {
            return ["Auto Sync bi chan: QlhvAutoSync:RefreshBackupBeforeSync = false."];
        }

        if (_syncOptions.DryRun)
        {
            return ["Auto Sync bi chan: Sync:DryRun = true."];
        }

        if (!_executionOptions.EnableTargetWrites)
        {
            return ["Auto Sync bi chan: SyncExecution.EnableTargetWrites = false."];
        }

        return Array.Empty<string>();
    }

    private static IEnumerable<QlhvOperationSourceDefinition> OrderedSources()
        => QlhvOperationSourceCatalog.All.OrderBy(
            item => string.Equals(item.SourceType, "OTO", StringComparison.Ordinal) ? 0 : 1);

    private async Task SafeCompleteQueueFailureAsync(Guid runId, Exception exception)
    {
        try
        {
            await _runs.CompleteAsync(
                runId,
                new QlhvAutoSyncOutcome(
                    QlhvAutoSyncConstants.Failed,
                    $"Auto Sync queue failed: {exception.GetType().Name}.",
                    DateTime.UtcNow),
                CancellationToken.None);
        }
        catch
        {
            // Preserve the queue error and never expose connection details.
        }
    }

    private static string ToState(QlhvAutoSyncRunRecord? run)
        => run?.Status switch
        {
            QlhvAutoSyncConstants.Queued => "queued",
            QlhvAutoSyncConstants.Running => "running",
            QlhvAutoSyncConstants.Succeeded => "succeeded",
            QlhvAutoSyncConstants.PartialSuccess => "partial-success",
            QlhvAutoSyncConstants.PartialFailed => "partial-failed",
            QlhvAutoSyncConstants.Failed => "failed",
            _ => "idle",
        };

    private static bool IsTerminal(string status)
        => string.Equals(status, QlhvAutoSyncConstants.Succeeded, StringComparison.Ordinal) ||
           string.Equals(status, QlhvAutoSyncConstants.PartialSuccess, StringComparison.Ordinal) ||
           string.Equals(status, QlhvAutoSyncConstants.PartialFailed, StringComparison.Ordinal) ||
           string.Equals(status, QlhvAutoSyncConstants.Failed, StringComparison.Ordinal);

    private static string ResolveActor(string trigger)
        => trigger switch
        {
            QlhvAutoSyncConstants.SessionStartTrigger =>
                QlhvOperationActors.SystemSessionStart,
            QlhvAutoSyncConstants.ManualTrigger =>
                QlhvOperationActors.ManualAdmin,
            _ => QlhvOperationActors.SystemAutoSync,
        };

    private static QlhvAutoSyncQueueResultDto Joined(
        QlhvAutoSyncRunRecord run,
        string message)
        => new()
        {
            Accepted = true,
            JoinedExisting = true,
            RunId = run.RunId,
            Status = run.Status,
            Message = message,
        };

    private static QlhvAutoSyncQueueResultDto Rejected(string message) => new()
    {
        Status = "REJECTED",
        Message = message,
    };

    private static QlhvAutoSyncQueueResultDto Unavailable(string message) => new()
    {
        IsUnavailable = true,
        Status = "UNAVAILABLE",
        Message = message,
    };
}
