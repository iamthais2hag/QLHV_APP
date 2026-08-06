using Microsoft.AspNetCore.Authorization;
using QLHV.Api.Controllers;
using QLHV.Application.Auth;
using QLHV.Application.Sync.Rt03;

namespace QLHV.Tests.Sync.Rt03;

public sealed class Rt03MasterSwitchTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Off_without_run_once_is_heartbeat_only()
    {
        Assert.Equal(Rt03MasterWorkKind.HeartbeatOnly,
            Rt03RealtimeMasterPolicy.Decide(Rt03RealtimeControlStates.Off, false));
    }

    [Fact]
    public void Off_with_pending_run_once_remains_heartbeat_only()
    {
        Assert.Equal(Rt03MasterWorkKind.HeartbeatOnly,
            Rt03RealtimeMasterPolicy.Decide(Rt03RealtimeControlStates.Off, true));
        Assert.Equal(Rt03RealtimeOutcomes.HealthyIdle,
            Rt03RealtimeMasterPolicy.IdleOutcome(runOnce: true));
    }

    [Fact]
    public void On_executes_continuous_event_driven_cycle()
    {
        Assert.Equal(Rt03MasterWorkKind.ContinuousCycle,
            Rt03RealtimeMasterPolicy.Decide(Rt03RealtimeControlStates.On, false));
    }

    [Fact]
    public void Run_once_is_allowed_only_while_master_is_on()
    {
        Assert.Equal(Rt03MasterWorkKind.RunOnce,
            Rt03RealtimeMasterPolicy.Decide(Rt03RealtimeControlStates.On, true));
        var store = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03RealtimeControlStore.cs");
        Assert.Contains("available only while Master Realtime is ON", store,
            StringComparison.Ordinal);
    }

    [Fact]
    public void On_with_zero_backlog_uses_healthy_idle()
    {
        Assert.Equal(Rt03RealtimeOutcomes.HealthyIdle,
            Rt03RealtimeMasterPolicy.IdleOutcome(runOnce: false));
    }

    [Fact]
    public void Blocked_never_schedules_mutation_retry()
    {
        Assert.Equal(Rt03MasterWorkKind.BlockedHeartbeat,
            Rt03RealtimeMasterPolicy.Decide(Rt03RealtimeControlStates.Blocked, true));
    }

    [Fact]
    public void Invalid_control_state_fails_closed()
    {
        var error = Assert.Throws<Rt03SafetyException>(() =>
            Rt03RealtimeMasterPolicy.Decide("UNKNOWN", false));
        Assert.Equal(Rt03RealtimeMasterErrors.InvalidControlState, error.Code);
    }

    [Fact]
    public void Backlog_contract_classifies_window_and_count_without_row_counts()
    {
        var valid = new Rt03RealtimeProfileBacklog("CSDT_OTO", 124, 129, 100);
        var expired = new Rt03RealtimeProfileBacklog("CSDT_OTO", 25, 129, 70);
        Assert.True(valid.IsWindowValid);
        Assert.Equal(5, valid.BacklogVersions);
        Assert.False(expired.IsWindowValid);
    }

    [Fact]
    public void Worker_off_branch_precedes_time_ct_and_lock_resolution()
    {
        var source = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeWorker.cs");
        var off = source.IndexOf("workKind == Rt03MasterWorkKind.HeartbeatOnly",
            StringComparison.Ordinal);
        var time = source.IndexOf("GetRequiredService<ITimeAuthorityService>",
            StringComparison.Ordinal);
        var backlog = source.IndexOf("GetRequiredService<IRt03EventBacklogProbe>()",
            StringComparison.Ordinal);
        var writerLock = source.IndexOf("GetRequiredService<IQlhvDirectRealtimeGlobalLock>",
            StringComparison.Ordinal);
        Assert.True(off >= 0 && off < time && time < writerLock && writerLock < backlog);
    }

    [Fact]
    public void Worker_idle_branch_does_not_call_deep_drift_processor()
    {
        var source = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeWorker.cs");
        var idle = source.IndexOf("backlogs.All", StringComparison.Ordinal);
        var processor = source.IndexOf("IRt03ProductionRealtimeCycleProcessor",
            StringComparison.Ordinal);
        Assert.True(idle >= 0 && processor > idle);
        Assert.DoesNotContain("EvaluateNoChangeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_stays_running_when_blocked()
    {
        var source = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeWorker.cs");
        Assert.Contains("service remains running without mutation retries", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("StopApplication", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.ExitCode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Disable_is_rechecked_immediately_before_processor()
    {
        var source = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeWorker.cs");
        Assert.Contains("Re-read the authoritative switch immediately before", source,
            StringComparison.Ordinal);
        Assert.Contains("return Rt03RealtimeOutcomes.RealtimeOff", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Control_changes_use_rowversion_compare_and_audit_in_one_transaction()
    {
        var source = Read("server/QLHV.Infrastructure/Sync/Rt03/Rt03RealtimeControlStore.cs");
        Assert.Contains("SequenceEqual(expectedRowVersion)", source, StringComparison.Ordinal);
        Assert.Contains("InsertAuditAsync", source, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", source, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_defaults_to_off_and_allows_only_one_active_run_request()
    {
        var sql = Read("database/patches/20260731_add_rt03_master_switch.sql");
        Assert.Contains("N'DEFAULT_OFF'", sql, StringComparison.Ordinal);
        Assert.Contains("UX_App_Rt03RealtimeRunRequest_ActiveSlot", sql,
            StringComparison.Ordinal);
        Assert.Contains("WHERE ActiveSlot=1", sql, StringComparison.Ordinal);
        Assert.Contains("rowversion NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rollback_refuses_on_blocked_or_active_state()
    {
        var sql = Read("database/patches/20260731_rollback_rt03_master_switch.sql");
        Assert.Contains("State<>N'OFF'", sql, StringComparison.Ordinal);
        Assert.Contains("RT03_MASTER_ROLLBACK_RUN_ACTIVE", sql, StringComparison.Ordinal);
        Assert.Contains("RT03_MASTER_ROLLBACK_WORKER_ACTIVE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Master_schema_never_mutates_historical_reviews_or_checkpoint()
    {
        foreach (var path in new[]
                 {
                     "database/patches/20260731_add_rt03_master_switch.sql",
                     "database/patches/20260731_rt03_master_switch_worker_permissions.sql",
                 })
        {
            var sql = Read(path);
            Assert.DoesNotContain("UPDATE dbo.App_QlhvDirectRealtimeManualReview", sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM dbo.App_QlhvDirectRealtimeManualReview", sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE dbo.App_QlhvDirectRealtimeApplyCheckpoint", sql,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Worker_permission_extension_is_narrow_and_cannot_insert_run_requests()
    {
        var sql = Read(
            "database/patches/20260731_rt03_master_switch_worker_permissions.sql");
        Assert.Contains("DENY INSERT,DELETE ON OBJECT::dbo.App_Rt03RealtimeRunRequest", sql,
            StringComparison.Ordinal);
        Assert.Contains("DENY UPDATE,DELETE ON OBJECT::dbo.App_Rt03RealtimeControlAudit", sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GRANT CONTROL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ADD MEMBER [db_owner]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(SystemRealtimeControlController.Enable))]
    [InlineData(nameof(SystemRealtimeControlController.Disable))]
    [InlineData(nameof(SystemRealtimeControlController.RunOnce))]
    public void Mutating_api_endpoints_require_admin(string methodName)
    {
        var method = typeof(SystemRealtimeControlController).GetMethod(methodName)!;
        var authorize = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Equal(AuthPolicies.RequireAdmin, authorize.Policy);
    }

    [Fact]
    public void Client_cannot_supply_checkpoint_or_sql_identity()
    {
        var properties = typeof(Rt03RealtimeControlChangeRequest).GetProperties();
        Assert.Single(properties);
        Assert.Equal("ExpectedRowVersion", properties[0].Name);
    }

    [Fact]
    public void Integrity_preview_is_separate_and_read_only()
    {
        var controller = Read(
            "server/QLHV.Api/Controllers/SystemRealtimeControlController.cs");
        var service = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03RealtimeControlService.cs");
        Assert.Contains("realtime-integrity/preview", controller, StringComparison.Ordinal);
        Assert.Contains("new Rt03RealtimeIntegrityPreviewDto(\n            true", service,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IRt03ProductionRealtimeCycleProcessor", service,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_auto_sync_is_superseded_by_single_master_authority()
    {
        var source = Read("server/QLHV.Application/Sync/QlhvAutoSyncService.cs");
        var worker = Read("server/QLHV.Infrastructure/Sync/QlhvAutoSyncWorker.cs");
        Assert.Contains("SupersededByRealtimeMasterDecision", source,
            StringComparison.Ordinal);
        Assert.Contains("legacy Auto Sync must not become a fallback", source,
            StringComparison.Ordinal);
        Assert.Contains("IsSupersededByMasterAsync", worker, StringComparison.Ordinal);
        Assert.Contains("superseded by RT03 master authority", worker,
            StringComparison.Ordinal);
    }

    [Fact]
    public void New_event_path_does_not_consult_historical_review_versions()
    {
        var worker = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeWorker.cs");
        Assert.DoesNotContain("123", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("124", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("ReviewedRetained", worker, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_contains_confirmation_run_once_and_separate_integrity_actions()
    {
        var source = Read(
            "client/src/features/runtime-status/RealtimeMasterControlPanel.tsx");
        Assert.Contains("Bật Realtime sẽ xử lý toàn bộ thay đổi sau checkpoint hiện tại.",
            source, StringComparison.Ordinal);
        Assert.Contains("Đồng bộ ngay một lần", source, StringComparison.Ordinal);
        Assert.Contains("Kiểm tra toàn vẹn dữ liệu", source, StringComparison.Ordinal);
        Assert.Contains("user?.role === 'Admin'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_checkpoint_publication_remains_committed_marker_based()
    {
        var processor = Read(
            "server/QLHV.Infrastructure/Sync/Rt03/Rt03ProductionRealtimeCycleProcessor.cs");
        Assert.Contains("PublishCheckpointAsync", processor, StringComparison.Ordinal);
        Assert.Contains("Checkpoint optimistic update failed after committed marker", processor,
            StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
