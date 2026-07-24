using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QLHV.Application.Runtime;
using QLHV.Application.Sync;
using QLHV.Application.Sync.Configuration;
using QLHV.Application.Sync.Dtos;
using QLHV.Application.SystemData;
using QLHV.Infrastructure.Sync;

namespace QLHV.Tests.Sync;

public sealed class QlhvAutoSyncTests
{
    [Fact]
    public async Task Concurrent_startup_triggers_create_one_run_and_join_the_other_process()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);

        var results = await Task.WhenAll(
            service.QueueAsync(QlhvAutoSyncConstants.StartupTrigger),
            service.QueueAsync(QlhvAutoSyncConstants.StartupTrigger));

        Assert.All(results, result => Assert.True(result.Accepted));
        Assert.Single(results.Where(result => !result.JoinedExisting));
        Assert.Single(results.Where(result => result.JoinedExisting));
        Assert.Single(queue.Items);
        Assert.Equal(QlhvOperationActors.SystemAutoSync, runs.Created.Single().Actor);
        Assert.Equal(new[] { "OTO", "MOTO" }, runs.Created.Single().SourceOrder);
    }

    [Fact]
    public async Task Delayed_second_startup_does_not_join_completed_run_by_time()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);

        var first = await service.QueueAsync(QlhvAutoSyncConstants.StartupTrigger);
        await runs.CompleteAsync(
            first.RunId!.Value,
            new QlhvAutoSyncOutcome(
                QlhvAutoSyncConstants.Succeeded,
                null,
                DateTime.UtcNow));

        var second = await service.QueueAsync(QlhvAutoSyncConstants.StartupTrigger);

        Assert.True(second.Accepted);
        Assert.False(second.JoinedExisting);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(2, queue.Items.Count);
    }

    [Fact]
    public async Task Exact_status_returns_requested_completed_run_when_a_newer_run_exists()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(runs, new FakeAutoQueue());
        var first = await service.QueueAsync(QlhvAutoSyncConstants.ManualTrigger);
        await runs.CompleteAsync(
            first.RunId!.Value,
            new QlhvAutoSyncOutcome(
                QlhvAutoSyncConstants.Succeeded,
                null,
                DateTime.UtcNow));

        var second = await service.QueueAsync(QlhvAutoSyncConstants.ManualTrigger);
        var status = await service.GetStatusAsync(first.RunId);

        Assert.True(status.Found);
        Assert.Equal(first.RunId, status.RunId);
        Assert.NotEqual(second.RunId, status.RunId);
        Assert.Equal("succeeded", status.State);
        Assert.Equal(QlhvAutoSyncConstants.CompletedStage, status.CurrentStage);
        Assert.Null(status.ActiveRunId);
    }

    [Fact]
    public async Task Exact_status_returns_not_found_for_unknown_run()
    {
        var service = CreateService(
            new FakeRunRepository(),
            new FakeAutoQueue());

        var status = await service.GetStatusAsync(Guid.NewGuid());

        Assert.False(status.Found);
        Assert.Equal("not-found", status.State);
        Assert.Null(status.RunId);
        Assert.Null(status.ActiveRunId);
    }

    [Fact]
    public async Task Exact_status_keeps_the_requested_running_source_and_stage()
    {
        var runs = new FakeRunRepository();
        var runId = Guid.NewGuid();
        runs.SeedRunning(
            runId,
            QlhvOperationActors.SystemSessionStart,
            currentSourceType: "OTO",
            currentStage: QlhvAutoSyncConstants.SyncOtoStage);
        var service = CreateService(runs, new FakeAutoQueue());

        var status = await service.GetStatusAsync(runId);

        Assert.True(status.Found);
        Assert.Equal(runId, status.RunId);
        Assert.Equal(runId, status.ActiveRunId);
        Assert.Equal("OTO", status.CurrentSourceType);
        Assert.Equal(QlhvAutoSyncConstants.SyncOtoStage, status.CurrentStage);
    }

    [Fact]
    public async Task Session_start_joins_active_startup_without_creating_a_second_run()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);
        var startup = await service.QueueAsync(QlhvAutoSyncConstants.StartupTrigger);

        var session = await service.QueueSessionStartAsync(serverStartedByLauncher: true);

        Assert.True(session.Accepted);
        Assert.True(session.JoinedExisting);
        Assert.Equal(startup.RunId, session.RunId);
        Assert.Single(queue.Items);
    }

    [Fact]
    public async Task Manual_auto_sync_is_rejected_while_a_manual_source_operation_is_active()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var history = new FakeOperationHistory
        {
            Active = new QlhvOperationHistoryDto
            {
                OperationId = Guid.NewGuid(),
                SourceType = "OTO",
                OperationType = QlhvOperationTypes.RefreshBackup,
                Status = QlhvOperationTypes.Running,
            },
        };
        var service = CreateService(runs, queue, history: history);

        var result = await service.QueueAsync(QlhvAutoSyncConstants.ManualTrigger);

        Assert.False(result.Accepted);
        Assert.True(result.IsConflict);
        Assert.Empty(runs.Created);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Session_start_on_existing_server_creates_system_session_actor()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);

        var result = await service.QueueSessionStartAsync(serverStartedByLauncher: false);

        Assert.True(result.Accepted);
        Assert.False(result.JoinedExisting);
        Assert.Equal(QlhvOperationActors.SystemSessionStart, runs.Created.Single().Actor);
        Assert.Equal(QlhvAutoSyncConstants.SessionStartTrigger, runs.Created.Single().TriggerType);
    }

    [Fact]
    public async Task App_open_ensure_fresh_uses_system_actor_and_server_side_cooldown()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);
        var before = DateTime.UtcNow.AddSeconds(-31);

        var result = await service.QueueEnsureFreshAsync();

        Assert.True(result.Accepted);
        Assert.False(result.JoinedExisting);
        Assert.Equal(QlhvAutoSyncConstants.StartedDecision, result.Decision);
        Assert.Single(queue.Items);
        var created = Assert.Single(runs.Created);
        Assert.Equal(QlhvAutoSyncConstants.AppOpenTrigger, created.TriggerType);
        Assert.Equal(QlhvOperationActors.SystemAppOpen, created.Actor);
        Assert.NotNull(created.DedupeNotBeforeUtc);
        Assert.True(created.DedupeNotBeforeUtc >= before);
        Assert.True(created.DedupeNotBeforeUtc < created.CreatedAtUtc);
    }

    [Fact]
    public async Task Concurrent_app_open_requests_create_one_run_and_join_the_other()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);

        var results = await Task.WhenAll(
            service.QueueEnsureFreshAsync(),
            service.QueueEnsureFreshAsync());

        Assert.All(results, result => Assert.True(result.Accepted));
        Assert.Contains(
            results,
            result => result.Decision == QlhvAutoSyncConstants.StartedDecision);
        Assert.Contains(
            results,
            result => result.Decision == QlhvAutoSyncConstants.ActiveOperationDecision);
        Assert.Single(results.Where(result => !result.JoinedExisting));
        Assert.Single(results.Where(result => result.JoinedExisting));
        Assert.Single(runs.Created);
        Assert.Single(queue.Items);
    }

    [Fact]
    public async Task Repeated_app_open_within_cooldown_observes_recent_terminal_run()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);
        var first = await service.QueueEnsureFreshAsync();
        await runs.CompleteAsync(
            first.RunId!.Value,
            new QlhvAutoSyncOutcome(
                QlhvAutoSyncConstants.Failed,
                "test failure",
                DateTime.UtcNow));

        var second = await service.QueueEnsureFreshAsync();

        Assert.True(second.Accepted);
        Assert.True(second.JoinedExisting);
        Assert.Equal(QlhvAutoSyncConstants.CooldownDecision, second.Decision);
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(QlhvAutoSyncConstants.Failed, second.Status);
        Assert.Single(runs.Created);
        Assert.Single(queue.Items);
    }

    [Fact]
    public async Task App_open_cooldown_starts_when_a_long_running_operation_completes()
    {
        var runs = new FakeRunRepository();
        var completedRunId = Guid.NewGuid();
        runs.SeedCompleted(new QlhvAutoSyncRunRecord
        {
            RunId = completedRunId,
            TriggerType = QlhvAutoSyncConstants.AppOpenTrigger,
            Actor = QlhvOperationActors.SystemAppOpen,
            Status = QlhvAutoSyncConstants.Failed,
            SourceOrder = ["OTO"],
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
        });
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);

        var result = await service.QueueEnsureFreshAsync();

        Assert.True(result.Accepted);
        Assert.True(result.JoinedExisting);
        Assert.Equal(QlhvAutoSyncConstants.CooldownDecision, result.Decision);
        Assert.Equal(completedRunId, result.RunId);
        Assert.Empty(runs.Created);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Failed_session_start_is_retryable_without_waiting_for_a_time_window()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue);
        var first = await service.QueueSessionStartAsync(serverStartedByLauncher: false);
        await runs.CompleteAsync(
            first.RunId!.Value,
            new QlhvAutoSyncOutcome(
                QlhvAutoSyncConstants.Failed,
                "failed",
                DateTime.UtcNow));

        var second = await service.QueueSessionStartAsync(serverStartedByLauncher: false);

        Assert.False(second.JoinedExisting);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(2, queue.Items.Count);
    }

    [Fact]
    public async Task Session_status_requests_sync_when_no_active_or_recent_run_exists()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(runs, new FakeAutoQueue());

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: false);

        Assert.True(status.Found);
        Assert.True(status.NeedSync);
        Assert.True(status.CanStart);
        Assert.Null(status.RunId);
        Assert.Equal("idle", status.State);
        Assert.Empty(status.Blockers);
    }

    [Fact]
    public async Task Session_status_joins_active_startup_instead_of_requesting_sync()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(runs, new FakeAutoQueue());
        var startup = await service.QueueAsync(QlhvAutoSyncConstants.StartupTrigger);

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: true);

        Assert.True(status.NeedSync);
        Assert.True(status.OperationActive);
        Assert.Equal(startup.RunId, status.ActiveRunId);
        Assert.Equal(startup.RunId, status.RunId);
        Assert.Equal("queued", status.State);
        Assert.False(status.IsTerminal);
    }

    [Fact]
    public async Task Session_status_tracks_the_requested_completed_run()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(runs, new FakeAutoQueue());
        var queued = await service.QueueSessionStartAsync(serverStartedByLauncher: false);
        await runs.CompleteAsync(
            queued.RunId!.Value,
            new QlhvAutoSyncOutcome(
                QlhvAutoSyncConstants.Succeeded,
                null,
                DateTime.UtcNow));

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: false,
            queued.RunId);

        Assert.True(status.Found);
        Assert.False(status.NeedSync);
        Assert.Equal(queued.RunId, status.RunId);
        Assert.Equal("succeeded", status.State);
        Assert.True(status.IsTerminal);
        Assert.True(status.Succeeded);
    }

    [Fact]
    public async Task Session_status_treats_partial_success_as_terminal_success()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(runs, new FakeAutoQueue());
        var queued = await service.QueueSessionStartAsync(serverStartedByLauncher: false);
        await runs.CompleteAsync(
            queued.RunId!.Value,
            new QlhvAutoSyncOutcome(
                QlhvAutoSyncConstants.PartialSuccess,
                "Optional domain skipped",
                DateTime.UtcNow));

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: false,
            queued.RunId);

        Assert.Equal("partial-success", status.State);
        Assert.True(status.IsTerminal);
        Assert.True(status.Succeeded);
        Assert.False(status.NeedSync);
        Assert.Equal("Optional domain skipped", status.ErrorMessage);
    }

    [Fact]
    public async Task Session_status_exposes_write_guard_without_creating_a_run()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(
            runs,
            new FakeAutoQueue(),
            dryRun: true);

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: false);

        Assert.True(status.NeedSync);
        Assert.False(status.CanStart);
        Assert.Contains(status.Blockers, blocker => blocker.Contains("DryRun", StringComparison.Ordinal));
        Assert.Empty(runs.Created);
    }

    [Fact]
    public async Task Session_status_is_up_to_date_without_returning_a_run_id()
    {
        var freshness = new FakeFreshnessService
        {
            Result = new QlhvSyncFreshnessResult { NeedSync = false },
        };
        var service = CreateService(
            new FakeRunRepository(),
            new FakeAutoQueue(),
            freshness: freshness);

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: false);

        Assert.False(status.NeedSync);
        Assert.False(status.OperationActive);
        Assert.Null(status.RunId);
        Assert.Null(status.ActiveRunId);
        Assert.Equal("up-to-date", status.State);
    }

    [Fact]
    public async Task Ensure_fresh_reports_no_sync_needed_without_creating_a_run()
    {
        var freshness = new FakeFreshnessService
        {
            Result = new QlhvSyncFreshnessResult { NeedSync = false },
        };
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue, freshness: freshness);

        var result = await service.QueueEnsureFreshAsync();

        Assert.True(result.Accepted);
        Assert.False(result.JoinedExisting);
        Assert.Null(result.RunId);
        Assert.Equal(QlhvAutoSyncConstants.NoSyncNeededDecision, result.Decision);
        Assert.Empty(runs.Created);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Refresh_backup_disabled_is_a_hard_start_blocker()
    {
        var runs = new FakeRunRepository();
        var service = CreateService(
            runs,
            new FakeAutoQueue(),
            refreshBackupBeforeSync: false);

        var status = await service.GetSessionStartStatusAsync(
            serverStartedByLauncher: false);

        Assert.False(status.CanStart);
        Assert.Contains(
            status.Blockers,
            blocker => blocker.Contains("RefreshBackupBeforeSync", StringComparison.Ordinal));
        Assert.Empty(runs.Created);
    }

    [Fact]
    public async Task Disabled_configuration_does_not_create_or_queue_a_run()
    {
        var runs = new FakeRunRepository();
        var queue = new FakeAutoQueue();
        var service = CreateService(runs, queue, enabled: false);

        var result = await service.QueueAsync(QlhvAutoSyncConstants.ManualTrigger);

        Assert.False(result.Accepted);
        Assert.Equal("REJECTED", result.Status);
        Assert.Empty(runs.Created);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Coordinator_runs_oto_then_moto_and_isolates_source_failure()
    {
        var runs = new FakeRunRepository();
        var runner = new FakeSourceRunner(sourceType =>
            sourceType == "OTO"
                ? SourceResult(sourceType, QlhvAutoSyncConstants.Failed, "OTO failed")
                : SourceResult(sourceType, QlhvAutoSyncConstants.Succeeded));
        var coordinator = new QlhvAutoSyncCoordinator(runs, runner);
        var run = NewRun();

        var outcome = await coordinator.ExecuteAsync(run);

        Assert.Equal(new[] { "OTO", "MOTO" }, runner.Calls);
        Assert.Equal(new[] { "OTO", "MOTO" }, runs.CurrentSources);
        Assert.Equal(2, runs.SourceResults.Count);
        Assert.Contains(QlhvAutoSyncConstants.LoadingDataStage, runs.CurrentStages);
        Assert.Equal(QlhvAutoSyncConstants.PartialFailed, outcome.Status);
        Assert.Equal("OTO failed", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Coordinator_reports_partial_success_when_optional_domain_is_skipped_in_one_source()
    {
        var runs = new FakeRunRepository();
        var runner = new FakeSourceRunner(sourceType =>
            sourceType == "OTO"
                ? SourceResult(
                    sourceType,
                    QlhvAutoSyncConstants.PartialSuccess,
                    "HocVien da dong bo; GiaoVien duoc bo qua.")
                : SourceResult(sourceType, QlhvAutoSyncConstants.Succeeded));
        var coordinator = new QlhvAutoSyncCoordinator(runs, runner);

        var outcome = await coordinator.ExecuteAsync(NewRun());

        Assert.Equal(new[] { "OTO", "MOTO" }, runner.Calls);
        Assert.Equal(QlhvAutoSyncConstants.PartialSuccess, outcome.Status);
        Assert.Equal("HocVien da dong bo; GiaoVien duoc bo qua.", outcome.ErrorMessage);
        Assert.All(
            runs.SourceResults,
            result => Assert.NotEqual(QlhvAutoSyncConstants.Failed, result.Status));
    }

    [Fact]
    public async Task Coordinator_reports_partial_failed_when_partial_success_is_followed_by_source_failure()
    {
        var runs = new FakeRunRepository();
        var runner = new FakeSourceRunner(sourceType =>
            sourceType == "OTO"
                ? SourceResult(
                    sourceType,
                    QlhvAutoSyncConstants.PartialSuccess,
                    "OTO optional domain skipped")
                : SourceResult(
                    sourceType,
                    QlhvAutoSyncConstants.Failed,
                    "MOTO refresh failed"));
        var coordinator = new QlhvAutoSyncCoordinator(runs, runner);

        var outcome = await coordinator.ExecuteAsync(NewRun());

        Assert.Equal(new[] { "OTO", "MOTO" }, runner.Calls);
        Assert.Equal(QlhvAutoSyncConstants.PartialFailed, outcome.Status);
        Assert.Equal("MOTO refresh failed", outcome.ErrorMessage);
    }

    [Fact]
    public async Task Coordinator_persists_committed_source_result_when_host_token_cancels_after_runner()
    {
        using var stopping = new CancellationTokenSource();
        var runs = new FakeRunRepository();
        var runner = new FakeSourceRunner(sourceType =>
        {
            stopping.Cancel();
            return SourceResult(sourceType, QlhvAutoSyncConstants.Succeeded);
        });
        var coordinator = new QlhvAutoSyncCoordinator(runs, runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ExecuteAsync(NewRun(), stopping.Token));

        Assert.Single(runs.SourceResults);
        Assert.Equal(CancellationToken.None, runs.SourceResultTokens.Single());
    }

    [Fact]
    public async Task Coordinator_resume_skips_partition_already_persisted_as_succeeded()
    {
        var runs = new FakeRunRepository();
        var runner = new FakeSourceRunner(sourceType =>
            SourceResult(sourceType, QlhvAutoSyncConstants.Succeeded));
        var coordinator = new QlhvAutoSyncCoordinator(runs, runner);
        var original = NewRun();
        var run = new QlhvAutoSyncRunRecord
        {
            RunId = original.RunId,
            TriggerType = original.TriggerType,
            Actor = original.Actor,
            Status = original.Status,
            SourceOrder = original.SourceOrder,
            CreatedAtUtc = original.CreatedAtUtc,
            StartedAtUtc = original.StartedAtUtc,
            Oto = SourceResult("OTO", QlhvAutoSyncConstants.Succeeded),
        };

        var outcome = await coordinator.ExecuteAsync(run);

        Assert.Equal(new[] { "MOTO" }, runner.Calls);
        Assert.Equal(QlhvAutoSyncConstants.Succeeded, outcome.Status);
    }

    [Fact]
    public async Task Refresh_failure_prevents_plan_and_full_sync_of_stale_backup()
    {
        var operationId = Guid.NewGuid();
        var operations = new FakeOperationsService(operationId);
        var history = new FakeOperationHistory
        {
            ById = new QlhvOperationHistoryDto
            {
                OperationId = operationId,
                SourceType = "OTO",
                OperationType = QlhvOperationTypes.RefreshBackup,
                Status = QlhvOperationTypes.Failed,
                ErrorMessage = "restore failed",
            },
        };
        var import = new FakeImportService();
        var runs = new FakeRunRepository();
        var runId = Guid.NewGuid();
        runs.SeedRunning(runId, QlhvOperationActors.SystemAutoSync);
        var runner = new QlhvAutoSyncSourceRunner(
            operations,
            history,
            import,
            runs,
            Options.Create(new QlhvAutoSyncOptions
            {
                RefreshBackupBeforeSync = true,
                OperationPollMilliseconds = 100,
            }));

        var result = await runner.RunAsync(
            runId,
            "OTO",
            QlhvOperationActors.SystemAutoSync);

        Assert.Equal(QlhvAutoSyncConstants.Failed, result.Status);
        Assert.Equal(0, import.PlanCalls);
        Assert.Equal(0, import.ExecuteCalls);
        Assert.Equal(QlhvOperationActors.SystemAutoSync, operations.LastActor);
        Assert.Equal(
            new[] { QlhvAutoSyncConstants.RefreshOtoStage },
            runs.CurrentStages);
    }

    [Theory]
    [InlineData(QlhvOperationActors.SystemAutoSync)]
    [InlineData(QlhvOperationActors.SystemSessionStart)]
    [InlineData(QlhvOperationActors.SystemAppOpen)]
    public async Task Successful_source_run_attributes_refresh_and_full_sync_to_system_actor(
        string actor)
    {
        var operationId = Guid.NewGuid();
        var operations = new FakeOperationsService(operationId);
        var history = new FakeOperationHistory
        {
            ById = new QlhvOperationHistoryDto
            {
                OperationId = operationId,
                SourceType = "OTO",
                OperationType = QlhvOperationTypes.RefreshBackup,
                Status = QlhvOperationTypes.Succeeded,
            },
        };
        var import = new FakeImportService
        {
            Plan = new QlhvImportPlanDto
            {
                SourceProfileCode = "CSDT_OTO",
                MaCSDT = "66029",
                BackupSnapshotToken = "snapshot-token",
                ExecutableDomains = [QlhvImportDomains.HocVien],
            },
            ExecuteResult = new QlhvImportExecuteResultDto
            {
                OperationId = Guid.NewGuid(),
                Executed = true,
                Status = "ThanhCong",
                Message = "ok",
            },
        };
        var runs = new FakeRunRepository();
        var runId = Guid.NewGuid();
        runs.SeedRunning(runId, actor);
        var runner = new QlhvAutoSyncSourceRunner(
            operations,
            history,
            import,
            runs,
            Options.Create(new QlhvAutoSyncOptions
            {
                RefreshBackupBeforeSync = true,
                OperationPollMilliseconds = 100,
            }));

        var result = await runner.RunAsync(
            runId,
            "OTO",
            actor);

        Assert.Equal(QlhvAutoSyncConstants.Succeeded, result.Status);
        Assert.Equal(actor, operations.LastActor);
        Assert.Equal(actor, import.LastExecuteActor);
        Assert.Equal("snapshot-token", import.LastExpectedSnapshotToken);
        Assert.Equal(
            new[]
            {
                QlhvAutoSyncConstants.RefreshOtoStage,
                QlhvAutoSyncConstants.SyncOtoStage,
            },
            runs.CurrentStages);
    }

    [Fact]
    public async Task Source_runner_preserves_partial_success_from_import_execution()
    {
        var operationId = Guid.NewGuid();
        var import = new FakeImportService
        {
            Plan = new QlhvImportPlanDto
            {
                SourceProfileCode = "CSDT_OTO",
                MaCSDT = "66029",
                BackupSnapshotToken = "snapshot-token",
                ExecutableDomains = [QlhvImportDomains.HocVien],
            },
            ExecuteResult = new QlhvImportExecuteResultDto
            {
                OperationId = Guid.NewGuid(),
                Executed = true,
                Status = QlhvImportOverallStatuses.PartialSuccess,
                Message = "HocVien da dong bo; module tuy chon duoc bo qua.",
            },
        };
        var runs = new FakeRunRepository();
        var runId = Guid.NewGuid();
        runs.SeedRunning(runId, QlhvOperationActors.SystemAutoSync);
        var runner = new QlhvAutoSyncSourceRunner(
            new FakeOperationsService(operationId),
            new FakeOperationHistory
            {
                ById = new QlhvOperationHistoryDto
                {
                    OperationId = operationId,
                    SourceType = "OTO",
                    OperationType = QlhvOperationTypes.RefreshBackup,
                    Status = QlhvOperationTypes.Succeeded,
                },
            },
            import,
            runs,
            Options.Create(new QlhvAutoSyncOptions
            {
                RefreshBackupBeforeSync = true,
                OperationPollMilliseconds = 100,
            }));

        var result = await runner.RunAsync(
            runId,
            "OTO",
            QlhvOperationActors.SystemAutoSync);

        Assert.Equal(QlhvAutoSyncConstants.PartialSuccess, result.Status);
        Assert.Equal(import.ExecuteResult.OperationId, result.SyncOperationId);
        Assert.Equal(import.ExecuteResult.Message, result.Message);
        Assert.Equal(1, import.ExecuteCalls);
    }

    [Fact]
    public async Task Source_runner_reports_failed_when_required_domain_failed_after_optional_commit()
    {
        var refreshOperationId = Guid.NewGuid();
        var syncOperationId = Guid.NewGuid();
        var import = new FakeImportService
        {
            Plan = new QlhvImportPlanDto
            {
                SourceProfileCode = "CSDT_OTO",
                MaCSDT = "66029",
                BackupSnapshotToken = "snapshot-token",
                ExecutableDomains = [QlhvImportDomains.HocVien],
            },
            ExecuteResult = new QlhvImportExecuteResultDto
            {
                OperationId = syncOperationId,
                Executed = true,
                Status = QlhvImportOverallStatuses.Failed,
                Message = "HocVien that bai sau khi KhoaHoc da commit.",
            },
        };
        var runs = new FakeRunRepository();
        var runId = Guid.NewGuid();
        runs.SeedRunning(runId, QlhvOperationActors.SystemAutoSync);
        var runner = new QlhvAutoSyncSourceRunner(
            new FakeOperationsService(refreshOperationId),
            new FakeOperationHistory
            {
                ById = new QlhvOperationHistoryDto
                {
                    OperationId = refreshOperationId,
                    SourceType = "OTO",
                    OperationType = QlhvOperationTypes.RefreshBackup,
                    Status = QlhvOperationTypes.Succeeded,
                },
            },
            import,
            runs,
            Options.Create(new QlhvAutoSyncOptions
            {
                RefreshBackupBeforeSync = true,
                OperationPollMilliseconds = 100,
            }));

        var result = await runner.RunAsync(
            runId,
            "OTO",
            QlhvOperationActors.SystemAutoSync);

        Assert.Equal(QlhvAutoSyncConstants.Failed, result.Status);
        Assert.Equal(syncOperationId, result.SyncOperationId);
        Assert.Equal(import.ExecuteResult.Message, result.Message);
        Assert.Equal(1, import.ExecuteCalls);
    }

    [Fact]
    public async Task Source_runner_never_syncs_when_refresh_before_sync_is_disabled()
    {
        var import = new FakeImportService();
        var runs = new FakeRunRepository();
        var runId = Guid.NewGuid();
        runs.SeedRunning(runId, QlhvOperationActors.SystemAutoSync);
        var runner = new QlhvAutoSyncSourceRunner(
            new FakeOperationsService(Guid.NewGuid()),
            new FakeOperationHistory(),
            import,
            runs,
            Options.Create(new QlhvAutoSyncOptions
            {
                RefreshBackupBeforeSync = false,
            }));

        var result = await runner.RunAsync(
            runId,
            "OTO",
            QlhvOperationActors.SystemAutoSync);

        Assert.Equal(QlhvAutoSyncConstants.Failed, result.Status);
        Assert.Equal(0, import.PlanCalls);
        Assert.Equal(0, import.ExecuteCalls);
    }

    [Fact]
    public async Task Freshness_detects_same_count_course_teacher_relation_update()
    {
        var snapshot = EmptySnapshot("snapshot-token");
        var import = new FakeImportService
        {
            Plan = new QlhvImportPlanDto
            {
                BackupSnapshotToken = "snapshot-token",
                ExecutableDomains =
                [
                    QlhvImportDomains.HocVien,
                    QlhvImportDomains.Relation,
                ],
                KhoaHocGiaoVien = new QlhvEntitySyncCountsDto
                {
                    SourceRows = 0,
                    Update = 1,
                },
            },
        };
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(snapshot, snapshot),
            new FakePartitionStateRepository(new QlhvPartitionSyncState
            {
                AppliedBackupSnapshotToken = "snapshot-token",
            }),
            import);

        var result = await service.EvaluateAsync();

        Assert.True(result.NeedSync);
        Assert.Contains(
            result.Reasons,
            reason => reason.EndsWith(":QLHV_APP_CHUA_KHOP_BAK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Freshness_content_token_is_stable_when_equal_rows_arrive_in_different_order()
    {
        var first = EmptySnapshot(
            "snapshot-token",
            [
                new QlhvKhoaHocGiaoVienSourceRow { MaLichLV = 1, MaKH = "K1", MaGV = "G2" },
                new QlhvKhoaHocGiaoVienSourceRow { MaLichLV = 1, MaKH = "K1", MaGV = "G1" },
            ]);
        var second = EmptySnapshot(
            "snapshot-token",
            first.KhoaHocGiaoVienRows.Reverse().ToArray());
        var state = new QlhvPartitionSyncState
        {
            AppliedBackupSnapshotToken = "snapshot-token",
            KhoaHocGiaoVienRows = 2,
        };
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(first, second),
            new FakePartitionStateRepository(state),
            new FakeImportService
            {
                Plan = new QlhvImportPlanDto
                {
                    BackupSnapshotToken = "snapshot-token",
                    KhoaHocGiaoVien = new QlhvEntitySyncCountsDto { SourceRows = 2 },
                },
            });

        var result = await service.EvaluateAsync();

        Assert.False(result.NeedSync);
        Assert.Equal(
            result.LiveSnapshots[0].ContentToken,
            result.BackupSnapshots[0].ContentToken);
    }

    [Fact]
    public async Task Freshness_without_partition_state_requests_sync_for_each_partition()
    {
        var snapshot = EmptySnapshot("snapshot-token");
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(snapshot, snapshot),
            new FakePartitionStateRepository(null),
            new FakeImportService
            {
                Plan = new QlhvImportPlanDto
                {
                    BackupSnapshotToken = "snapshot-token",
                },
            });

        var result = await service.EvaluateAsync();

        Assert.True(result.NeedSync);
        Assert.Contains("OTO:CHUA_TUNG_DONG_BO_THANH_CONG", result.Reasons);
        Assert.Contains("MOTO:CHUA_TUNG_DONG_BO_THANH_CONG", result.Reasons);
        Assert.Equal(2, result.Partitions.Count);
        Assert.All(result.Partitions, partition => Assert.False(partition.IsConsistent));
    }

    [Fact]
    public async Task Freshness_detects_live_change_when_row_counts_are_equal()
    {
        var live = EmptySnapshot(
            "live-token",
            [new QlhvKhoaHocGiaoVienSourceRow { MaLichLV = 1, MaKH = "K1", MaGV = "LIVE" }]);
        var backup = EmptySnapshot(
            "backup-token",
            [new QlhvKhoaHocGiaoVienSourceRow { MaLichLV = 1, MaKH = "K1", MaGV = "BAK" }]);
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(live, backup),
            new FakePartitionStateRepository(new QlhvPartitionSyncState
            {
                AppliedBackupSnapshotToken = "backup-token",
                KhoaHocGiaoVienRows = 1,
            }),
            new FakeImportService
            {
                Plan = new QlhvImportPlanDto
                {
                    BackupSnapshotToken = "backup-token",
                    KhoaHocGiaoVien = new QlhvEntitySyncCountsDto { SourceRows = 1 },
                },
            });

        var result = await service.EvaluateAsync();

        Assert.True(result.NeedSync);
        Assert.Contains(
            result.Reasons,
            reason => reason.EndsWith(":LIVE_KHAC_BAK", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Reasons,
            reason => reason.EndsWith(":SO_LUONG_LIVE_KHAC_BAK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Freshness_detects_backup_token_not_yet_applied_to_app()
    {
        var snapshot = EmptySnapshot("new-backup-token");
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(snapshot, snapshot),
            new FakePartitionStateRepository(new QlhvPartitionSyncState
            {
                AppliedBackupSnapshotToken = "old-backup-token",
            }),
            new FakeImportService
            {
                Plan = new QlhvImportPlanDto
                {
                    BackupSnapshotToken = "new-backup-token",
                },
            });

        var result = await service.EvaluateAsync();

        Assert.True(result.NeedSync);
        Assert.Contains(
            result.Reasons,
            reason => reason.EndsWith(":BAK_KHAC_TOKEN_DA_AP_DUNG", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Freshness_evaluates_oto_and_moto_partitions_independently()
    {
        var consistent = EmptySnapshot("snapshot-token");
        var changedOto = EmptySnapshot(
            "snapshot-token",
            [new QlhvKhoaHocGiaoVienSourceRow { MaLichLV = 1, MaKH = "K1", MaGV = "G1" }]);
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(
                request => request.SourceProfileCode == "CSDT_OTO" ? changedOto : consistent,
                _ => consistent),
            new FakePartitionStateRepository(new QlhvPartitionSyncState
            {
                AppliedBackupSnapshotToken = "snapshot-token",
            }),
            new FakeImportService
            {
                Plan = new QlhvImportPlanDto
                {
                    BackupSnapshotToken = "snapshot-token",
                },
            });

        var result = await service.EvaluateAsync();

        Assert.True(result.NeedSync);
        var oto = Assert.Single(result.Partitions, item => item.SourceType == "OTO");
        var moto = Assert.Single(result.Partitions, item => item.SourceType == "MOTO");
        Assert.False(oto.IsConsistent);
        Assert.True(moto.IsConsistent);
        Assert.Contains("OTO:LIVE_KHAC_BAK", oto.Reasons);
        Assert.Empty(moto.Reasons);
    }

    [Fact]
    public async Task Freshness_snapshot_read_error_is_explicit_and_conservative()
    {
        var snapshot = EmptySnapshot("snapshot-token");
        var service = new QlhvSyncFreshnessService(
            new FakeFreshnessSources(
                request => request.SourceProfileCode == "CSDT_OTO"
                    ? throw new InvalidOperationException("sensitive details")
                    : snapshot,
                _ => snapshot),
            new FakePartitionStateRepository(new QlhvPartitionSyncState
            {
                AppliedBackupSnapshotToken = "snapshot-token",
            }),
            new FakeImportService
            {
                Plan = new QlhvImportPlanDto
                {
                    BackupSnapshotToken = "snapshot-token",
                },
            });

        var result = await service.EvaluateAsync();

        Assert.True(result.NeedSync);
        Assert.Contains("OTO:KHONG_DOC_DUOC_SNAPSHOT_INVALIDOPERATIONEXCEPTION", result.Reasons);
        Assert.Contains(
            result.Blockers,
            blocker => blocker.StartsWith("OTO:", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Blockers,
            blocker => blocker.Contains("sensitive details", StringComparison.Ordinal));
        Assert.True(Assert.Single(result.Partitions, item => item.SourceType == "MOTO").IsConsistent);
    }

    [Fact]
    public async Task Startup_service_queues_exactly_once_after_production_readiness()
    {
        var autoSync = new FakeAutoSyncService();
        using var provider = new ServiceCollection()
            .AddScoped<IRuntimeReadinessService>(_ => new ReadyReadinessService())
            .AddScoped<IQlhvAutoSyncService>(_ => autoSync)
            .BuildServiceProvider();
        var service = new QlhvAutoSyncStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(Environments.Production),
            Options.Create(new QlhvAutoSyncOptions
            {
                Enabled = true,
                RunOnServerStartup = true,
                ReadinessPollSeconds = 1,
            }));

        await service.StartAsync(CancellationToken.None);
        await autoSync.Queued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, autoSync.QueueCalls);
        Assert.Equal(QlhvAutoSyncConstants.StartupTrigger, autoSync.LastTrigger);
    }

    [Fact]
    public async Task Startup_service_queue_failure_is_isolated_from_the_host()
    {
        var autoSync = new FakeAutoSyncService
        {
            QueueException = new InvalidOperationException("test startup queue failure"),
        };
        using var provider = new ServiceCollection()
            .AddScoped<IRuntimeReadinessService>(_ => new ReadyReadinessService())
            .AddScoped<IQlhvAutoSyncService>(_ => autoSync)
            .BuildServiceProvider();
        var service = new QlhvAutoSyncStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(Environments.Production),
            Options.Create(new QlhvAutoSyncOptions
            {
                Enabled = true,
                RunOnServerStartup = true,
                ReadinessPollSeconds = 1,
            }));

        await service.StartAsync(CancellationToken.None);
        await autoSync.Queued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, autoSync.QueueCalls);
        Assert.Equal(QlhvAutoSyncConstants.StartupTrigger, autoSync.LastTrigger);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task Startup_service_is_disabled_outside_production(string environmentName)
    {
        var autoSync = new FakeAutoSyncService();
        using var provider = new ServiceCollection()
            .AddScoped<IRuntimeReadinessService>(_ => new ReadyReadinessService())
            .AddScoped<IQlhvAutoSyncService>(_ => autoSync)
            .BuildServiceProvider();
        var service = new QlhvAutoSyncStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(environmentName),
            Options.Create(new QlhvAutoSyncOptions
            {
                Enabled = true,
                RunOnServerStartup = true,
            }));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, autoSync.QueueCalls);
    }

    private static QlhvAutoSyncService CreateService(
        FakeRunRepository runs,
        FakeAutoQueue queue,
        bool enabled = true,
        bool dryRun = false,
        bool enableTargetWrites = true,
        bool refreshBackupBeforeSync = true,
        FakeFreshnessService? freshness = null,
        FakeOperationHistory? history = null)
        => new(
            runs,
            queue,
            new FakeDataVersionRepository(),
            freshness ?? new FakeFreshnessService(),
            history ?? new FakeOperationHistory(),
            new ReadyReadinessService(),
            Options.Create(new QlhvAutoSyncOptions
            {
                Enabled = enabled,
                RunOnServerStartup = true,
                RefreshBackupBeforeSync = refreshBackupBeforeSync,
                SourceOrder = ["OTO", "MOTO"],
            }),
            Options.Create(new QLHV.Application.Sync.SyncOptions { DryRun = dryRun }),
            Options.Create(new SyncExecutionOptions
            {
                EnableTargetWrites = enableTargetWrites,
            }));

    private static QlhvImportSourceSnapshot EmptySnapshot(
        string snapshotToken,
        IReadOnlyList<QlhvKhoaHocGiaoVienSourceRow>? relationRows = null)
        => new()
        {
            SourceDatabaseName = "source",
            BackupSnapshotToken = snapshotToken,
            GeneratedAtUtc = DateTime.UtcNow,
            KhoaHocGiaoVienRows = relationRows ?? Array.Empty<QlhvKhoaHocGiaoVienSourceRow>(),
        };

    private static QlhvAutoSyncRunRecord NewRun()
        => new()
        {
            RunId = Guid.NewGuid(),
            TriggerType = QlhvAutoSyncConstants.StartupTrigger,
            Actor = QlhvOperationActors.SystemAutoSync,
            Status = QlhvAutoSyncConstants.Running,
            SourceOrder = ["OTO", "MOTO"],
            CreatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
        };

    private static QlhvAutoSyncSourceResultDto SourceResult(
        string sourceType,
        string status,
        string? message = null)
        => new()
        {
            SourceType = sourceType,
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            Message = message,
        };

    private sealed class FakeRunRepository : IQlhvAutoSyncRunRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, QlhvAutoSyncRunRecord> _records = new();
        private QlhvAutoSyncRunRecord? _active;
        private QlhvAutoSyncRunRecord? _latest;

        public List<QlhvAutoSyncRunCreate> Created { get; } = new();
        public List<string> CurrentSources { get; } = new();
        public List<string> CurrentStages { get; } = new();
        public List<QlhvAutoSyncSourceResultDto> SourceResults { get; } = new();
        public List<CancellationToken> SourceResultTokens { get; } = new();

        public Task<bool> TryCreateAsync(
            QlhvAutoSyncRunCreate entry,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_active is not null ||
                    (entry.DedupeNotBeforeUtc is not null &&
                     _latest is not null &&
                     string.Equals(
                         _latest.TriggerType,
                         entry.TriggerType,
                         StringComparison.Ordinal) &&
                     (_latest.CompletedAtUtc ?? _latest.CreatedAtUtc) >=
                        entry.DedupeNotBeforeUtc.Value))
                {
                    return Task.FromResult(false);
                }

                Created.Add(entry);
                var record = new QlhvAutoSyncRunRecord
                {
                    RunId = entry.RunId,
                    TriggerType = entry.TriggerType,
                    Actor = entry.Actor,
                    Status = QlhvAutoSyncConstants.Queued,
                    SourceOrder = entry.SourceOrder,
                    CreatedAtUtc = entry.CreatedAtUtc,
                };
                _records[record.RunId] = record;
                _active = record;
                _latest = record;
                return Task.FromResult(true);
            }
        }

        public void SeedRunning(
            Guid runId,
            string actor,
            string? currentSourceType = null,
            string? currentStage = null)
        {
            lock (_gate)
            {
                var record = new QlhvAutoSyncRunRecord
                {
                    RunId = runId,
                    TriggerType = QlhvAutoSyncConstants.StartupTrigger,
                    Actor = actor,
                    Status = QlhvAutoSyncConstants.Running,
                    SourceOrder = ["OTO", "MOTO"],
                    CurrentSourceType = currentSourceType,
                    CurrentStage = currentStage,
                    CreatedAtUtc = DateTime.UtcNow,
                    StartedAtUtc = DateTime.UtcNow,
                };
                _records[record.RunId] = record;
                _active = record;
                _latest = record;
            }
        }

        public Task<QlhvAutoSyncRunRecord?> GetByIdAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(
                    _records.TryGetValue(runId, out var record)
                        ? record
                        : null);
            }
        }

        public Task<QlhvAutoSyncRunRecord?> GetActiveAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(_active);

        public Task<QlhvAutoSyncRunRecord?> GetLatestAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(_latest);

        public Task<QlhvAutoSyncRunRecord?> GetLatestByTriggerAsync(
            string triggerType,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var latest = _records.Values
                    .Where(record => string.Equals(
                        record.TriggerType,
                        triggerType,
                        StringComparison.Ordinal))
                    .OrderByDescending(record => record.CreatedAtUtc)
                    .FirstOrDefault();
                return Task.FromResult(latest);
            }
        }

        public void SeedCompleted(QlhvAutoSyncRunRecord record)
        {
            lock (_gate)
            {
                _records[record.RunId] = record;
                _latest = record;
                _active = null;
            }
        }

        public Task<bool> MarkRunningAsync(
            Guid runId,
            DateTime startedAtUtc,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SetCurrentSourceAsync(
            Guid runId,
            string sourceType,
            CancellationToken cancellationToken = default)
        {
            CurrentSources.Add(sourceType);
            return Task.CompletedTask;
        }

        public Task SetCurrentStageAsync(
            Guid runId,
            string stage,
            CancellationToken cancellationToken = default)
        {
            CurrentStages.Add(stage);
            return Task.CompletedTask;
        }

        public Task SetSourceResultAsync(
            Guid runId,
            QlhvAutoSyncSourceResultDto result,
            CancellationToken cancellationToken = default)
        {
            SourceResults.Add(result);
            SourceResultTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            Guid runId,
            QlhvAutoSyncOutcome outcome,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_active?.RunId != runId)
                {
                    return Task.CompletedTask;
                }

                var completed = new QlhvAutoSyncRunRecord
                {
                    RunId = _active.RunId,
                    TriggerType = _active.TriggerType,
                    Actor = _active.Actor,
                    Status = outcome.Status,
                    SourceOrder = _active.SourceOrder,
                    CurrentSourceType = null,
                    CurrentStage = outcome.Status is
                        QlhvAutoSyncConstants.Succeeded or
                        QlhvAutoSyncConstants.PartialSuccess
                        ? QlhvAutoSyncConstants.CompletedStage
                        : QlhvAutoSyncConstants.FailedStage,
                    CreatedAtUtc = _active.CreatedAtUtc,
                    StartedAtUtc = _active.StartedAtUtc,
                    CompletedAtUtc = outcome.CompletedAtUtc,
                    ErrorMessage = outcome.ErrorMessage,
                };
                _records[completed.RunId] = completed;
                _latest = completed;
                _active = null;
            }

            return Task.CompletedTask;
        }

        public Task<bool> RequeueInterruptedAsync(
            Guid runId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeAutoQueue : IQlhvAutoSyncQueue
    {
        public List<QlhvAutoSyncWorkItem> Items { get; } = new();

        public ValueTask EnqueueAsync(
            QlhvAutoSyncWorkItem item,
            CancellationToken cancellationToken = default)
        {
            Items.Add(item);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSourceRunner : IQlhvAutoSyncSourceRunner
    {
        private readonly Func<string, QlhvAutoSyncSourceResultDto> _result;

        public FakeSourceRunner(Func<string, QlhvAutoSyncSourceResultDto> result)
        {
            _result = result;
        }

        public List<string> Calls { get; } = new();

        public Task<QlhvAutoSyncSourceResultDto> RunAsync(
            Guid runId,
            string sourceType,
            string actor,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(sourceType);
            return Task.FromResult(_result(sourceType));
        }
    }

    private sealed class FakeOperationsService : IQlhvOperationsService
    {
        private readonly Guid _operationId;

        public FakeOperationsService(Guid operationId)
        {
            _operationId = operationId;
        }

        public string? LastActor { get; private set; }

        public Task<QlhvOperationsStatusDto> GetStatusAsync(
            string sourceType,
            string currentUserRole,
            bool writeAuthorized,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvRefreshBackupResultDto> QueueRefreshBackupAsync(
            QlhvRefreshBackupRequest request,
            CancellationToken cancellationToken = default)
        {
            LastActor = request.Actor;
            return Task.FromResult(new QlhvRefreshBackupResultDto
            {
                Accepted = true,
                OperationId = _operationId,
                SourceType = request.SourceType,
                Status = QlhvOperationTypes.Queued,
                Message = "queued",
            });
        }

        public Task<IReadOnlyList<QlhvOperationHistoryDto>> GetHistoryAsync(
            string sourceType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeOperationHistory : IQlhvOperationHistoryRepository
    {
        public QlhvOperationHistoryDto? ById { get; init; }
        public QlhvOperationHistoryDto? Active { get; init; }

        public Task<QlhvOperationHistoryDto?> GetByOperationIdAsync(
            Guid operationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ById);

        public Task<bool> TryCreateAsync(
            QlhvOperationHistoryCreate entry,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task MarkRunningAsync(
            Guid operationId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CompleteAsync(
            QlhvOperationHistoryCompletion completion,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<QlhvOperationHistoryDto>> SearchAsync(
            string sourceType,
            int take,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvOperationHistoryDto?> GetActiveAsync(
            string sourceType,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                string.Equals(Active?.SourceType, sourceType, StringComparison.Ordinal)
                    ? Active
                    : null);

        public Task<QlhvOperationHistoryDto?> GetLatestCompletedAsync(
            string sourceType,
            string operationType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeImportService : IQlhvImportService
    {
        public int PlanCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public string? LastExecuteActor { get; private set; }
        public string? LastExpectedSnapshotToken { get; private set; }

        public QlhvImportPlanDto Plan { get; init; } = new();
        public QlhvImportExecuteResultDto ExecuteResult { get; init; } = new();

        public Task<QlhvImportPlanDto> GetPlanAsync(
            QlhvImportRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            return Task.FromResult(Plan);
        }

        public Task<QlhvImportDiagnosticsDto> GetDiagnosticsAsync(
            QlhvImportRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvImportExecuteResultDto> ExecuteAsync(
            QlhvImportExecuteRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            LastExecuteActor = request.Actor;
            LastExpectedSnapshotToken = request.ExpectedSnapshotToken;
            return Task.FromResult(ExecuteResult);
        }
    }

    private sealed class FakeDataVersionRepository : ISystemDataVersionRepository
    {
        public Task<SystemDataVersionDto> GetAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SystemDataVersionDto());
    }

    private sealed class FakeFreshnessService : IQlhvSyncFreshnessService
    {
        public QlhvSyncFreshnessResult Result { get; init; } = new()
        {
            NeedSync = true,
            Reasons = ["OTO:LIVE_KHAC_BAK"],
        };

        public Task<QlhvSyncFreshnessResult> EvaluateAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeFreshnessSources : IQlhvFreshnessSourceRepository
    {
        private readonly Func<QlhvImportRequest, QlhvImportSourceSnapshot> _live;
        private readonly Func<QlhvImportRequest, QlhvImportSourceSnapshot> _backup;

        public FakeFreshnessSources(
            QlhvImportSourceSnapshot live,
            QlhvImportSourceSnapshot backup)
            : this(_ => live, _ => backup)
        {
        }

        public FakeFreshnessSources(
            Func<QlhvImportRequest, QlhvImportSourceSnapshot> live,
            Func<QlhvImportRequest, QlhvImportSourceSnapshot> backup)
        {
            _live = live;
            _backup = backup;
        }

        public Task<QlhvImportSourceSnapshot> ReadLiveSourceAsync(
            QlhvImportRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_live(request));

        public Task<QlhvImportSourceSnapshot> ReadBackupSourceAsync(
            QlhvImportRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_backup(request));
    }

    private sealed class FakePartitionStateRepository : IQlhvPartitionSyncStateRepository
    {
        private readonly QlhvPartitionSyncState? _state;

        public FakePartitionStateRepository(QlhvPartitionSyncState? state)
        {
            _state = state;
        }

        public Task<QlhvPartitionSyncState?> GetAsync(
            string sourceType,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_state);
    }

    private sealed class ReadyReadinessService : IRuntimeReadinessService
    {
        public Task<RuntimeStatusDto> GetStatusAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RuntimeStatusDto { IsReady = true });
    }

    private sealed class FakeAutoSyncService : IQlhvAutoSyncService
    {
        public TaskCompletionSource<bool> Queued { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int QueueCalls { get; private set; }
        public string? LastTrigger { get; private set; }
        public Exception? QueueException { get; init; }

        public Task<QlhvAutoSyncQueueResultDto> QueueAsync(
            string triggerType,
            CancellationToken cancellationToken = default)
        {
            QueueCalls++;
            LastTrigger = triggerType;
            Queued.TrySetResult(true);
            if (QueueException is not null)
            {
                throw QueueException;
            }

            return Task.FromResult(new QlhvAutoSyncQueueResultDto { Accepted = true });
        }

        public Task<QlhvAutoSyncStatusDto> GetStatusAsync(
            Guid? runId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvSessionStartStatusDto> GetSessionStartStatusAsync(
            bool serverStartedByLauncher,
            Guid? runId = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvAutoSyncQueueResultDto> QueueSessionStartAsync(
            bool serverStartedByLauncher,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<QlhvAutoSyncQueueResultDto> QueueEnsureFreshAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "QLHV.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
