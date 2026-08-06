using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.SystemData;
using QLHV.Application.Sync.Rt03;

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
    private readonly IQlhvAutoSyncPollingState? _pollingState;
    private readonly IRuntimeBuildIdentity? _buildIdentity;
    private readonly IQlhvOperationsStateProbe? _operationsState;
    private readonly IQlhvAutoSyncGlobalLock? _globalLock;
    private readonly IRt03RealtimeControlStore? _realtimeControl;

    public QlhvAutoSyncService(
        IQlhvAutoSyncRunRepository runs,
        IQlhvAutoSyncQueue queue,
        ISystemDataVersionRepository dataVersion,
        IQlhvSyncFreshnessService freshness,
        IQlhvOperationHistoryRepository history,
        IRuntimeReadinessService readiness,
        IOptions<QlhvAutoSyncOptions> options,
        IOptions<SyncOptions> syncOptions,
        IOptions<SyncExecutionOptions> executionOptions,
        IQlhvAutoSyncPollingState? pollingState = null,
        IRuntimeBuildIdentity? buildIdentity = null,
        IQlhvOperationsStateProbe? operationsState = null,
        IQlhvAutoSyncGlobalLock? globalLock = null,
        IRt03RealtimeControlStore? realtimeControl = null)
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
        _pollingState = pollingState;
        _buildIdentity = buildIdentity;
        _operationsState = operationsState;
        _globalLock = globalLock;
        _realtimeControl = realtimeControl;
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

    public Task<QlhvAutoSyncQueueResultDto> QueueEnsureFreshAsync(
        CancellationToken cancellationToken = default)
        => QueueIfNeededAsync(
            QlhvAutoSyncConstants.AppOpenTrigger,
            serverStartedByLauncher: false,
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
            return Unavailable(ex.Message, QlhvAutoSyncConstants.NotReadyDecision);
        }

        if (status.OperationActive)
        {
            if (status.ActiveRunId.HasValue)
            {
                var active = await _runs.GetByIdAsync(status.ActiveRunId.Value, cancellationToken);
                if (active is not null)
                {
                    return Joined(
                        active,
                        "Dang tham gia Auto Sync da duoc mot phien khac khoi tao.",
                        QlhvAutoSyncConstants.ActiveOperationDecision);
                }
            }

            return new QlhvAutoSyncQueueResultDto
            {
                IsConflict = true,
                Status = "CONFLICT",
                Decision = QlhvAutoSyncConstants.ActiveOperationDecision,
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
                Decision = QlhvAutoSyncConstants.NoSyncNeededDecision,
                Message = "Du lieu Live, BAK va QLHV_APP da dong bo; khong tao operation moi.",
            };
        }

        if (!status.CanStart)
        {
            return Rejected(
                status.Blockers.FirstOrDefault() ?? "Auto Sync chua du dieu kien khoi dong.",
                QlhvAutoSyncConstants.NotReadyDecision);
        }

        return await QueueCoreAsync(trigger, joinActiveRun: true, cancellationToken);
    }

    private async Task<QlhvAutoSyncQueueResultDto> QueueCoreAsync(
        string triggerType,
        bool joinActiveRun,
        CancellationToken cancellationToken)
    {
        if (_realtimeControl is not null)
        {
            try
            {
                await _realtimeControl.ReadAsync(cancellationToken);
                return Rejected(
                    "Auto Sync cu da duoc thay the boi cong tac Realtime tong; " +
                    "hay dung Chay mot lan trong Trang thai he thong.",
                    QlhvAutoSyncConstants.SupersededByRealtimeMasterDecision);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Unavailable(
                    "Khong xac minh duoc authority Realtime tong; Auto Sync cu bi chan fail-closed.",
                    QlhvAutoSyncConstants.SupersededByRealtimeMasterDecision);
            }
        }

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
            var operations = await ReadOperationsStateAsync(cancellationToken);
            if (operations is not null && IsManualFallbackBlocked(operations))
            {
                return Rejected(
                    "Auto Sync du phong bi chan: realtime primary writer dang hoat dong.",
                    QlhvAutoSyncConstants.BlockedByRealtimePrimaryWriterDecision);
            }

            if (joinActiveRun)
            {
                var active = await _runs.GetActiveAsync(cancellationToken);
                if (active is not null && ClassifyRun(active, DateTime.UtcNow).IsActive)
                {
                    return Joined(
                        active,
                        "Dang tham gia Auto Sync da duoc mot phien khac khoi tao.",
                        QlhvAutoSyncConstants.ActiveOperationDecision);
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
                        Decision = QlhvAutoSyncConstants.ActiveOperationDecision,
                        Message =
                            $"Dang co thao tac refresh/full sync thu cong cho {source.SourceType}; khong tao Auto Sync moi.",
                    };
                }
            }
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return Unavailable(ex.Message, QlhvAutoSyncConstants.NotReadyDecision);
        }
        catch (QlhvOperationsStoreUnavailableException ex)
        {
            return Unavailable(ex.Message, QlhvAutoSyncConstants.NotReadyDecision);
        }

        var startBlockers = GetStartBlockers();
        if (startBlockers.Count > 0)
        {
            return Rejected(startBlockers[0], QlhvAutoSyncConstants.NotReadyDecision);
        }

        IAsyncDisposable? globalLease = null;
        if (_globalLock is not null)
        {
            globalLease = await _globalLock.TryAcquireAsync(cancellationToken);
            if (globalLease is null)
            {
                return Rejected(
                    "Auto Sync du phong bi chan: realtime primary writer dang giu mutex.",
                    QlhvAutoSyncConstants.BlockedByRealtimePrimaryWriterDecision);
            }
        }

        await using var heldLease = globalLease;
        var stale = await _runs.GetActiveAsync(cancellationToken);
        if (stale is not null && !ClassifyRun(stale, DateTime.UtcNow).IsActive)
        {
            await _runs.MarkStaleFailedAsync(stale.RunId, DateTime.UtcNow, cancellationToken);
        }

        var runId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;
        var dedupeNotBeforeUtc = GetDedupeNotBeforeUtc(trigger, createdAtUtc);
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
                    dedupeNotBeforeUtc),
                cancellationToken);
        }
        catch (QlhvAutoSyncStoreUnavailableException ex)
        {
            return Unavailable(ex.Message, QlhvAutoSyncConstants.NotReadyDecision);
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
                            "Auto Sync da duoc mot process hoac phien launcher khac tiep nhan.",
                            QlhvAutoSyncConstants.ActiveOperationDecision);
                    }
                }
                catch (QlhvAutoSyncStoreUnavailableException ex)
                {
                    return Unavailable(ex.Message, QlhvAutoSyncConstants.NotReadyDecision);
                }
            }

            if (dedupeNotBeforeUtc.HasValue)
            {
                try
                {
                    var recent = await _runs.GetLatestByTriggerAsync(trigger, cancellationToken);
                    if (recent is not null &&
                        (recent.CompletedAtUtc ?? recent.CreatedAtUtc) >=
                            dedupeNotBeforeUtc.Value)
                    {
                        return Joined(
                            recent,
                            "Yeu cau mo ung dung nam trong thoi gian cooldown; su dung ket qua Auto Sync gan nhat.",
                            QlhvAutoSyncConstants.CooldownDecision);
                    }
                }
                catch (QlhvAutoSyncStoreUnavailableException ex)
                {
                    return Unavailable(ex.Message, QlhvAutoSyncConstants.NotReadyDecision);
                }
            }

            return new QlhvAutoSyncQueueResultDto
            {
                IsConflict = true,
                Status = "CONFLICT",
                Decision = QlhvAutoSyncConstants.ActiveOperationDecision,
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
                Decision = QlhvAutoSyncConstants.FailedToQueueDecision,
                Message = "Khong the dua Auto Sync vao hang doi.",
            };
        }

        return new QlhvAutoSyncQueueResultDto
        {
            Accepted = true,
            RunId = runId,
            Status = QlhvAutoSyncConstants.Queued,
            Decision = QlhvAutoSyncConstants.StartedDecision,
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
        var rawActive = await _runs.GetActiveAsync(cancellationToken);
        var active = ClassifyRun(rawActive, DateTime.UtcNow).IsActive
            ? rawActive
            : null;
        var appVersion = await GetDataVersionAsync(cancellationToken);
        var lastSuccessful = await _runs.GetLatestSuccessfulAsync(cancellationToken);
        if (active is not null)
        {
            return CreateRunStatus(
                active,
                startBlockers,
                appVersion,
                lastSuccessful?.CompletedAtUtc,
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
                    LastSuccessfulSyncUtc = lastSuccessful?.CompletedAtUtc,
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
                LastSuccessfulSyncUtc = lastSuccessful?.CompletedAtUtc,
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
            LastSuccessfulSyncUtc = lastSuccessful?.CompletedAtUtc,
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
        var lastSuccessful = await _runs.GetLatestSuccessfulAsync(cancellationToken);
        if (run is null)
        {
            return new QlhvSessionStartStatusDto
            {
                Found = false,
                ServerReady = true,
                CanStart = blockers.Count == 0,
                State = "not-found",
                AppDataVersion = appVersion,
                LastSuccessfulSyncUtc = lastSuccessful?.CompletedAtUtc,
                Blockers = blockers,
                Message = "Khong tim thay phien Auto Sync duoc yeu cau.",
            };
        }

        var latest = await _runs.GetLatestAsync(cancellationToken);
        return CreateRunStatus(
            run,
            blockers,
            appVersion,
            lastSuccessful?.CompletedAtUtc,
            latest?.CreatedAtUtc ?? run.CreatedAtUtc,
            latest?.ErrorMessage);
    }

    private static QlhvSessionStartStatusDto CreateRunStatus(
        QlhvAutoSyncRunRecord run,
        IReadOnlyList<string> blockers,
        SystemDataVersionDto? appVersion,
        DateTime? lastSuccessfulSyncUtc,
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
            LastSuccessfulSyncUtc = lastSuccessfulSyncUtc,
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
        var nowUtc = DateTime.UtcNow;
        var rawActive = runId.HasValue
            ? null
            : await _runs.GetActiveAsync(cancellationToken);
        var activeClassification = ClassifyRun(rawActive, nowUtc);
        var active = activeClassification.IsActive ? rawActive : null;
        var latest = runId.HasValue
            ? await _runs.GetByIdAsync(runId.Value, cancellationToken)
            : active ?? await _runs.GetLatestAsync(cancellationToken);
        var displayedClassification = runId.HasValue
            ? ClassifyRun(latest, nowUtc)
            : activeClassification;
        var lastSuccessful = await _runs.GetLatestSuccessfulAsync(cancellationToken);
        var history = await _runs.GetRecentAsync(20, cancellationToken);
        var operations = await ReadOperationsStateAsync(cancellationToken);
        var realtimeBlocked = operations is null || IsManualFallbackBlocked(operations);
        var masterAuthorityActive = await IsMasterAuthorityActiveAsync(cancellationToken);
        var manualDecision = masterAuthorityActive
            ? QlhvAutoSyncConstants.SupersededByRealtimeMasterDecision
            : realtimeBlocked
            ? QlhvAutoSyncConstants.BlockedByRealtimePrimaryWriterDecision
            : !_options.Enabled || !_options.FallbackModeEnabled
                ? QlhvAutoSyncConstants.NotReadyDecision
                : active is not null
                    ? QlhvAutoSyncConstants.ActiveOperationDecision
                    : "MANUAL_RUN_ALLOWED";
        var manualReason = masterAuthorityActive
            ? "Auto Sync cu da ngung lam authority; su dung cong tac Realtime tong."
            : operations is null
            ? "Khong xac minh duoc trang thai realtime; fallback bi chan fail-closed."
            : realtimeBlocked
            ? "Realtime service/writer/mutex/cycle chua duoc tat va nha hoan toan."
            : !_options.FallbackModeEnabled
                ? "Che do Auto Sync du phong chua duoc bat ro rang."
                : !_options.Enabled
                    ? "Auto Sync du phong dang tat trong cau hinh."
                    : active is not null
                        ? "Mot Auto Sync du phong thuc su dang hoat dong."
                        : "Realtime primary writer da dung; fallback co the duoc quan tri vien khoi dong.";

        return new QlhvAutoSyncStatusDto
        {
            Found = !runId.HasValue || latest is not null,
            Enabled = _options.Enabled,
            RunOnServerStartup = _options.RunOnServerStartup,
            RefreshBackupBeforeSync = _options.RefreshBackupBeforeSync,
            PollingIntervalSeconds = Math.Clamp(_options.PollingIntervalSeconds, 1, 3600),
            ResolvedSourceOrder =
                QlhvAutoSyncConstants.NormalizeSourceOrder(_options.SourceOrder),
            // Deprecated compatibility field. The API cannot claim parity with
            // the separately hosted Windows realtime worker from its own options graph.
            ApiWorkerConfigParity = false,
            Polling = _pollingState?.Snapshot ?? new QlhvAutoSyncPollingStatusDto
            {
                Enabled = false,
                DisabledReason = "Polling runtime state is unavailable.",
            },
            Runtime = _buildIdentity?.Current ?? new RuntimeBuildIdentityDto(),
            State = !_options.Enabled
                ? "disabled"
                : runId.HasValue && latest is null
                    ? "not-found"
                    : !runId.HasValue && rawActive is not null && active is null
                        ? "inactive-stale-run"
                    : ToState(latest),
            RunId = latest?.RunId,
            ActiveRunId = displayedClassification.IsActive ? latest?.RunId : active?.RunId,
            TriggerType = latest?.TriggerType,
            Actor = latest?.Actor,
            CurrentSourceType = latest?.CurrentSourceType,
            CurrentStage = latest?.CurrentStage,
            CreatedAtUtc = latest?.CreatedAtUtc,
            StartedAtUtc = latest?.StartedAtUtc,
            CompletedAtUtc = latest?.CompletedAtUtc,
            LastSuccessfulSyncUtc = lastSuccessful?.CompletedAtUtc,
            LastSuccessfulRunId = lastSuccessful?.RunId,
            Oto = latest?.Oto,
            Moto = latest?.Moto,
            History = history.Select(ToHistoryItem).ToArray(),
            LastError = latest?.ErrorMessage ??
                (runId.HasValue && latest is null
                    ? "Khong tim thay phien Auto Sync duoc yeu cau."
                    : null),
            Realtime = ToRealtimeState(operations, nowUtc),
            Configuration = new QlhvAutoSyncConfigurationStateDto
            {
                Enabled = _options.Enabled,
                RunOnStartup = _options.RunOnServerStartup,
                PollingEnabled = _options.PollingEnabled,
                PollIntervalSeconds = Math.Clamp(_options.PollingIntervalSeconds, 1, 3600),
                IsFallbackOnly = _options.IsFallbackOnly,
                FallbackModeEnabled = _options.FallbackModeEnabled,
                ManualRunAllowed = manualDecision == "MANUAL_RUN_ALLOWED",
                ManualRunDecision = manualDecision,
                ManualRunReason = manualReason,
            },
            AutoSyncRuntime = new QlhvAutoSyncRuntimeStateDto
            {
                IsRunActive = displayedClassification.IsActive,
                ActiveRunId = displayedClassification.IsActive ? latest?.RunId : null,
                Classification = latest is null ? "INACTIVE" : displayedClassification.Classification,
                Source = displayedClassification.IsActive ? latest?.CurrentSourceType : null,
                Step = displayedClassification.IsActive ? latest?.CurrentStage : null,
                StartedAtUtc = displayedClassification.IsActive ? latest?.StartedAtUtc : null,
                LastHeartbeatUtc = latest is null ? null : LastActivity(latest),
                HeartbeatFresh = displayedClassification.HeartbeatFresh,
                EffectiveActiveSlotCount = displayedClassification.IsActive ? 1 : 0,
                RawActiveSlotCount = operations?.RawAutoSyncSlots ?? (rawActive is null ? 0 : 1),
                ActiveOperationCount = operations?.ActiveOperations ?? 0,
            },
        };
    }

    public Task<QlhvSyncFreshnessResult> GetDiagnosticsAsync(
        CancellationToken cancellationToken = default)
        => _freshness.EvaluateAsync(cancellationToken);

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

        if (!_options.IsFallbackOnly || !_options.FallbackModeEnabled)
        {
            return ["Auto Sync chi la fallback va FallbackModeEnabled chua duoc bat."];
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

    private DateTime? GetDedupeNotBeforeUtc(string trigger, DateTime createdAtUtc)
    {
        if (!string.Equals(
                trigger,
                QlhvAutoSyncConstants.AppOpenTrigger,
                StringComparison.Ordinal))
        {
            return null;
        }

        var cooldownSeconds = Math.Clamp(
            _options.SessionStartDedupeWindowSeconds,
            0,
            3600);
        return cooldownSeconds == 0
            ? null
            : createdAtUtc.AddSeconds(-cooldownSeconds);
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
    {
        if (run is not null &&
            IsTerminal(run.Status) &&
            (string.Equals(
                 run.Oto?.Status,
                 QlhvAutoSyncConstants.NeedsPlan,
                 StringComparison.Ordinal) ||
             string.Equals(
                 run.Moto?.Status,
                 QlhvAutoSyncConstants.NeedsPlan,
                 StringComparison.Ordinal)))
        {
            return "needs-plan";
        }

        return run?.Status switch
        {
            QlhvAutoSyncConstants.Queued => "queued",
            QlhvAutoSyncConstants.Running => "running",
            QlhvAutoSyncConstants.Succeeded => "succeeded",
            QlhvAutoSyncConstants.PartialSuccess => "partial-success",
            QlhvAutoSyncConstants.PartialFailed => "partial-failed",
            QlhvAutoSyncConstants.Failed => "failed",
            _ => "idle",
        };
    }

    private QlhvAutoSyncHistoryItemDto ToHistoryItem(
        QlhvAutoSyncRunRecord run)
    {
        var classification = ClassifyRun(run, DateTime.UtcNow);
        return new()
        {
            RunId = run.RunId,
            TriggerType = run.TriggerType,
            Actor = run.Actor,
            Status = run.Status,
            CreatedAtUtc = run.CreatedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            Oto = run.Oto,
            Moto = run.Moto,
            ErrorMessage = run.ErrorMessage,
            Classification = classification.Classification,
            IsStale = classification.Classification == "INACTIVE_STALE_RUN",
            LastHeartbeatUtc = LastActivity(run),
        };
    }

    private async Task<QlhvOperationsStateSnapshot?> ReadOperationsStateAsync(
        CancellationToken cancellationToken)
    {
        if (_operationsState is null) return null;
        try { return await _operationsState.ReadAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private async Task<bool> IsMasterAuthorityActiveAsync(
        CancellationToken cancellationToken)
    {
        if (_realtimeControl is null) return false;
        try
        {
            await _realtimeControl.ReadAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A configured master authority whose store cannot be proven is
            // still fail-closed; legacy Auto Sync must not become a fallback.
            return true;
        }
    }

    private static bool IsManualFallbackBlocked(QlhvOperationsStateSnapshot state)
        => state.RealtimeWritesEnabled || state.MutexHeld || state.CycleActive ||
           state.ActiveOperations != 0 ||
           string.Equals(state.ServiceState, "RUNNING", StringComparison.Ordinal) ||
           string.Equals(state.ProcessState, "RUNNING", StringComparison.Ordinal);

    private QlhvAutoSyncRunClassification ClassifyRun(
        QlhvAutoSyncRunRecord? run, DateTime nowUtc)
        => QlhvAutoSyncRunClassifier.Classify(
            run, nowUtc, _options.ActiveRunHeartbeatTimeoutSeconds);

    private static DateTime LastActivity(QlhvAutoSyncRunRecord run)
        => QlhvAutoSyncRunClassifier.LastActivity(run);

    private static QlhvRealtimeOperationsStateDto ToRealtimeState(
        QlhvOperationsStateSnapshot? state, DateTime nowUtc)
    {
        if (state is null) return new QlhvRealtimeOperationsStateDto();
        var fresh = state.LastHeartbeatUtc >= nowUtc.AddSeconds(-30);
        var running = state.RealtimeEnabled && fresh &&
            string.Equals(state.ServiceState, "RUNNING", StringComparison.Ordinal) &&
            string.Equals(state.ProcessState, "RUNNING", StringComparison.Ordinal) &&
            !string.Equals(state.WorkerStatus, Rt03.Rt03WorkerStatuses.Stopped, StringComparison.Ordinal);
        return new QlhvRealtimeOperationsStateDto
        {
            ServiceState = state.ServiceState,
            ProcessState = state.ProcessState,
            OverallHealth = running ? state.WorkerStatus : "STALE",
            WorkerInstanceId = state.WorkerInstanceId,
            LastHeartbeatUtc = state.LastHeartbeatUtc,
            CurrentProfile = state.CurrentProfile,
            CycleActive = state.CycleActive,
            WriterEnabled = state.RealtimeWritesEnabled,
            MutexHeld = state.MutexHeld,
            LastFailureCode = state.LastErrorCode,
            Profiles = state.Profiles,
        };
    }

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
            QlhvAutoSyncConstants.AppOpenTrigger =>
                QlhvOperationActors.SystemAppOpen,
            QlhvAutoSyncConstants.ManualTrigger =>
                QlhvOperationActors.ManualAdmin,
            _ => QlhvOperationActors.SystemAutoSync,
        };

    private static QlhvAutoSyncQueueResultDto Joined(
        QlhvAutoSyncRunRecord run,
        string message,
        string decision = QlhvAutoSyncConstants.ActiveOperationDecision)
        => new()
        {
            Accepted = true,
            JoinedExisting = true,
            RunId = run.RunId,
            Status = run.Status,
            Decision = decision,
            Message = message,
        };

    private static QlhvAutoSyncQueueResultDto Rejected(
        string message,
        string decision = QlhvAutoSyncConstants.NotReadyDecision) => new()
    {
        Status = "REJECTED",
        Decision = decision,
        Message = message,
    };

    private static QlhvAutoSyncQueueResultDto Unavailable(
        string message,
        string decision = QlhvAutoSyncConstants.FailedToQueueDecision) => new()
    {
        IsUnavailable = true,
        Status = "UNAVAILABLE",
        Decision = decision,
        Message = message,
    };
}
