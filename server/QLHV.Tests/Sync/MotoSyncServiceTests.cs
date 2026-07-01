using QLHV.Application.Sync;
using QLHV.Application.Sync.Dtos;

namespace QLHV.Tests.Sync;

public sealed class MotoSyncServiceTests
{
    [Fact]
    public async Task Execute_rejects_when_confirm_text_is_missing()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(new MotoSyncTestExecuteRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "CSDT_V1",
            TargetProfileCode = "CSDT_V2",
            ConfirmText = "WRONG",
        });

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains("SYNC TEST DATABASE", result.Message);
        Assert.Equal(0, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_and_update_requires_update_confirm_text()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1));
        var service = new MotoSyncService(repo);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;

        var result = await service.ExecuteTestAsync(request);

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(MotoSyncService.UpdateConfirmationText, result.Message);
        Assert.Equal(0, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Plan_blocks_non_test_profile()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.GetPlanAsync(new MotoSyncPlanRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "DATA_V1",
            TargetProfileCode = "CSDT_V2",
        });

        Assert.False(result.Executable);
        Assert.Contains(result.Blockers, blocker => blocker.Contains("CSDT_V1 va CSDT_V2", StringComparison.Ordinal));
        Assert.Equal(0, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_plan_has_short_full_madk_blocker()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(shortFullPairs: 1));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("MaDK ngan", StringComparison.Ordinal));
        Assert.Equal(1, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Execute_rejects_when_plan_has_missing_khoa_hoc_dependency()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(missingKhoaHoc: 1));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.False(result.Executed);
        Assert.Equal("BiChan", result.Status);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("KhoaHoc", StringComparison.Ordinal));
        Assert.Equal(1, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Execute_allows_missing_khoa_hoc_dependency_when_plan_can_insert_khoa_hoc()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(
            missingKhoaHoc: 1,
            plannedInsertKhoaHoc: 1,
            missingKhoaHocIsBlocker: false));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.NotNull(result.Plan);
        Assert.Equal(1L, result.Plan.PlannedInsertKhoaHoc);
        Assert.Equal(1L, result.Summary!.InsertedKhoaHoc);
        Assert.Empty(result.Plan.Blockers);
        Assert.Equal(2, repo.PlanCalls);
        Assert.Equal(1, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Execute_success_returns_before_plan_after_plan_and_keeps_old_plan_as_before_plan()
    {
        var beforePlan = CleanPlan(plannedInsertKhoaHoc: 1);
        var afterPlan = CleanPlan(
            plannedInsertNguoiLx: 0,
            plannedInsertHoSo: 0,
            plannedInsertGiayTo: 0);
        var repo = new FakeMotoSyncRepository(beforePlan)
        {
            AfterPlan = afterPlan,
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Same(result.BeforePlan, result.Plan);
        Assert.NotNull(result.BeforePlan);
        Assert.NotNull(result.AfterPlan);
        Assert.Equal(1L, result.BeforePlan!.PlannedInsertKhoaHoc);
        Assert.Equal(0L, result.AfterPlan!.PlannedInsertKhoaHoc);
        Assert.False(result.HasRemainingWork);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_still_has_planned_work()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                plannedInsertBaoCaoI: 0,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0,
                plannedUpdateNguoiLx: 1),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.Equal(1L, result.AfterPlan!.PlannedUpdate);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_still_has_baocao_i_insert()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                plannedInsertBaoCaoI: 1,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.Equal(1L, result.AfterPlan!.PlannedInsertBaoCaoI);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_still_has_nguoilx_gplx_insert()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                plannedInsertNguoiLXGplx: 1,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.Equal(1L, result.AfterPlan!.PlannedInsertNguoiLXGPLX);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_success_sets_has_remaining_work_when_after_plan_has_blockers()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            AfterPlan = CleanPlan(
                shortFullPairs: 1,
                plannedInsertNguoiLx: 0,
                plannedInsertHoSo: 0,
                plannedInsertGiayTo: 0),
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.True(result.HasRemainingWork);
        Assert.NotEmpty(result.AfterPlan!.Blockers);
        Assert.Equal(2, repo.PlanCalls);
    }

    [Fact]
    public async Task Clean_insert_only_plan_is_executable()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan());
        var service = new MotoSyncService(repo);

        var result = await service.GetPlanAsync(new MotoSyncPlanRequest
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "csdt_v1",
            TargetProfileCode = "csdt_v2",
            MaKhoaHoc = " 66016K26A0001 ",
        });

        Assert.True(result.Executable);
        Assert.Equal("CSDT_V1", result.SourceProfileCode);
        Assert.Equal("CSDT_V2", result.TargetProfileCode);
        Assert.Equal("66016K26A0001", result.MaKhoaHoc);
        Assert.Equal(2, result.PlannedInsertNguoiLX);
        Assert.Equal(2, result.PlannedInsertNguoiLXHoSo);
        Assert.Equal(3, result.PlannedInsertGiayTo);
        Assert.Equal(0, result.PlannedUpdate);
        Assert.Empty(result.Blockers);
        Assert.Equal(1, repo.PlanCalls);
    }

    [Fact]
    public async Task Execute_runs_insert_only_summary_without_update_or_delete()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan())
        {
            ExecuteSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = MotoSyncDirection.V1_TO_V2,
                SourceProfileCode = "CSDT_V1",
                TargetProfileCode = "CSDT_V2",
                InsertedBaoCaoI = 1,
                InsertedNguoiLX = 2,
                InsertedNguoiLXGPLX = 1,
                InsertedNguoiLXHoSo = 2,
                InsertedGiayTo = 3,
                UpdatedRows = 0,
                DeletedRows = 0,
            },
        };
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.InsertedBaoCaoI);
        Assert.Equal(2, result.Summary.InsertedNguoiLX);
        Assert.Equal(1, result.Summary.InsertedNguoiLXGPLX);
        Assert.Equal(2, result.Summary.InsertedNguoiLXHoSo);
        Assert.Equal(3, result.Summary.InsertedGiayTo);
        Assert.Equal(0, result.Summary.UpdatedRows);
        Assert.Equal(0, result.Summary.DeletedRows);
        Assert.Equal(2, repo.PlanCalls);
        Assert.Equal(1, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_only_mode_does_not_call_update_path_even_when_plan_has_updates()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1, plannedUpdateHoSo: 1));
        var service = new MotoSyncService(repo);

        var result = await service.ExecuteTestAsync(ConfirmedRequest());

        Assert.True(result.Executed);
        Assert.Equal(1, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_and_update_mode_uses_update_path_with_update_confirm_text()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(plannedUpdateNguoiLx: 1, plannedUpdateHoSo: 1))
        {
            UpdateSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = MotoSyncDirection.V1_TO_V2,
                SyncMode = MotoSyncMode.INSERT_AND_UPDATE,
                SourceProfileCode = "CSDT_V1",
                TargetProfileCode = "CSDT_V2",
                InsertedNguoiLX = 2,
                InsertedNguoiLXHoSo = 2,
                InsertedGiayTo = 3,
                UpdatedNguoiLX = 1,
                UpdatedNguoiLXHoSo = 1,
                UpdatedRows = 2,
                DeletedRows = 0,
            },
        };
        var service = new MotoSyncService(repo);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;
        request.ConfirmText = MotoSyncService.UpdateConfirmationText;
        var result = await service.ExecuteTestAsync(request);

        Assert.True(result.Executed);
        Assert.Equal("ThanhCong", result.Status);
        Assert.Equal(MotoSyncMode.INSERT_AND_UPDATE, result.Summary!.SyncMode);
        Assert.Equal(1, result.Summary.UpdatedNguoiLX);
        Assert.Equal(1, result.Summary.UpdatedNguoiLXHoSo);
        Assert.Equal(2, result.Summary.UpdatedRows);
        Assert.Equal(0, result.Summary.DeletedRows);
        Assert.Equal(2, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(1, repo.UpdateExecuteCalls);
    }

    [Fact]
    public async Task Insert_and_update_refuses_blockers()
    {
        var repo = new FakeMotoSyncRepository(CleanPlan(shortFullPairs: 1, plannedUpdateNguoiLx: 1));
        var service = new MotoSyncService(repo);

        var request = ConfirmedRequest();
        request.SyncMode = MotoSyncMode.INSERT_AND_UPDATE;
        request.ConfirmText = MotoSyncService.UpdateConfirmationText;
        var result = await service.ExecuteTestAsync(request);

        Assert.False(result.Executed);
        Assert.Contains(result.Plan!.Blockers, blocker => blocker.Contains("MaDK ngan", StringComparison.Ordinal));
        Assert.Equal(1, repo.PlanCalls);
        Assert.Equal(0, repo.ExecuteCalls);
        Assert.Equal(0, repo.UpdateExecuteCalls);
    }

    private static MotoSyncTestExecuteRequest ConfirmedRequest() => new()
    {
        Direction = MotoSyncDirection.V1_TO_V2,
        SourceProfileCode = "CSDT_V1",
        TargetProfileCode = "CSDT_V2",
        ConfirmText = MotoSyncService.ConfirmationText,
    };

    private static MotoSyncPlanDto CleanPlan(
        long shortFullPairs = 0,
        long missingKhoaHoc = 0,
        long plannedInsertKhoaHoc = 0,
        long plannedInsertBaoCaoI = 0,
        long plannedInsertNguoiLXGplx = 0,
        bool missingKhoaHocIsBlocker = true,
        long plannedInsertNguoiLx = 2,
        long plannedInsertHoSo = 2,
        long plannedInsertGiayTo = 3,
        long plannedUpdateNguoiLx = 0,
        long plannedUpdateHoSo = 0)
    {
        var blockers = new List<string>();
        if (shortFullPairs > 0)
        {
            blockers.Add($"Nguon co {shortFullPairs} cap MaDK ngan/day du nghi duplicate.");
        }

        if (missingKhoaHoc > 0 && missingKhoaHocIsBlocker)
        {
            blockers.Add($"Target thieu {missingKhoaHoc} MaKhoaHoc dependency. Task nay khong tu tao KhoaHoc.");
        }

        return new MotoSyncPlanDto
        {
            Direction = MotoSyncDirection.V1_TO_V2,
            SourceProfileCode = "CSDT_V1",
            TargetProfileCode = "CSDT_V2",
            SourceRows = 10,
            TargetRows = 8,
            ExactMaDkOverlap = 8,
            SourceOnly = 2,
            TargetOnly = 0,
            DuplicateBusinessKeyGroups = 0,
            ShortFullMaDkPairs = shortFullPairs,
            MissingKhoaHocDependencies = missingKhoaHoc,
            PlannedInsertKhoaHoc = plannedInsertKhoaHoc,
            PlannedInsertBaoCaoI = plannedInsertBaoCaoI,
            PlannedInsertNguoiLX = plannedInsertNguoiLx,
            PlannedInsertNguoiLXGPLX = plannedInsertNguoiLXGplx,
            PlannedInsertNguoiLXHoSo = missingKhoaHoc > 0 ? 1 : plannedInsertHoSo,
            PlannedInsertGiayTo = plannedInsertGiayTo,
            PlannedUpdate = plannedUpdateNguoiLx + plannedUpdateHoSo,
            PlannedUpdateNguoiLX = plannedUpdateNguoiLx,
            PlannedUpdateNguoiLXHoSo = plannedUpdateHoSo,
            Executable = blockers.Count == 0,
            Blockers = blockers,
        };
    }

    private sealed class FakeMotoSyncRepository : IMotoSyncRepository
    {
        private readonly MotoSyncPlanDto _plan;

        public FakeMotoSyncRepository(MotoSyncPlanDto plan)
        {
            _plan = plan;
            ExecuteSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = plan.Direction,
                SyncMode = MotoSyncMode.INSERT_ONLY,
                SourceProfileCode = plan.SourceProfileCode,
                TargetProfileCode = plan.TargetProfileCode,
                InsertedKhoaHoc = plan.PlannedInsertKhoaHoc,
                InsertedBaoCaoI = plan.PlannedInsertBaoCaoI,
                InsertedNguoiLX = plan.PlannedInsertNguoiLX,
                InsertedNguoiLXGPLX = plan.PlannedInsertNguoiLXGPLX,
                InsertedNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                InsertedGiayTo = plan.PlannedInsertGiayTo,
            };
            UpdateSummary = new MotoSyncExecuteSummaryDto
            {
                Direction = plan.Direction,
                SyncMode = MotoSyncMode.INSERT_AND_UPDATE,
                SourceProfileCode = plan.SourceProfileCode,
                TargetProfileCode = plan.TargetProfileCode,
                InsertedKhoaHoc = plan.PlannedInsertKhoaHoc,
                InsertedBaoCaoI = plan.PlannedInsertBaoCaoI,
                InsertedNguoiLX = plan.PlannedInsertNguoiLX,
                InsertedNguoiLXGPLX = plan.PlannedInsertNguoiLXGPLX,
                InsertedNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                InsertedGiayTo = plan.PlannedInsertGiayTo,
                UpdatedNguoiLX = plan.PlannedUpdateNguoiLX,
                UpdatedNguoiLXHoSo = plan.PlannedUpdateNguoiLXHoSo,
                UpdatedRows = plan.PlannedUpdate,
                DeletedRows = 0,
            };
        }

        public int PlanCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public int UpdateExecuteCalls { get; private set; }
        public MotoSyncExecuteSummaryDto ExecuteSummary { get; init; }
        public MotoSyncExecuteSummaryDto UpdateSummary { get; init; }
        public MotoSyncPlanDto? AfterPlan { get; init; }

        public Task<MotoSyncPlanDto> BuildPlanAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanCalls++;
            var plan = PlanCalls > 1 && AfterPlan is not null ? AfterPlan : _plan;
            return Task.FromResult(new MotoSyncPlanDto
            {
                Direction = request.Direction,
                SourceProfileCode = request.SourceProfileCode,
                TargetProfileCode = request.TargetProfileCode,
                MaKhoaHoc = request.MaKhoaHoc,
                AllowDirtyData = request.AllowDirtyData,
                SourceRows = plan.SourceRows,
                TargetRows = plan.TargetRows,
                ExactMaDkOverlap = plan.ExactMaDkOverlap,
                SourceOnly = plan.SourceOnly,
                TargetOnly = plan.TargetOnly,
                DuplicateBusinessKeyGroups = plan.DuplicateBusinessKeyGroups,
                ShortFullMaDkPairs = plan.ShortFullMaDkPairs,
                MissingKhoaHocDependencies = plan.MissingKhoaHocDependencies,
                PlannedInsertKhoaHoc = plan.PlannedInsertKhoaHoc,
                PlannedInsertBaoCaoI = plan.PlannedInsertBaoCaoI,
                PlannedInsertNguoiLX = plan.PlannedInsertNguoiLX,
                PlannedInsertNguoiLXGPLX = plan.PlannedInsertNguoiLXGPLX,
                PlannedInsertNguoiLXHoSo = plan.PlannedInsertNguoiLXHoSo,
                PlannedInsertGiayTo = plan.PlannedInsertGiayTo,
                PlannedUpdate = plan.PlannedUpdate,
                PlannedUpdateNguoiLX = plan.PlannedUpdateNguoiLX,
                PlannedUpdateNguoiLXHoSo = plan.PlannedUpdateNguoiLXHoSo,
                UpdateSamples = plan.UpdateSamples,
                Executable = plan.Executable,
                Blockers = plan.Blockers,
                Warnings = plan.Warnings,
                Errors = plan.Errors,
            });
        }

        public Task<MotoSyncExecuteSummaryDto> ExecuteInsertOnlyAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(ExecuteSummary);
        }

        public Task<MotoSyncExecuteSummaryDto> ExecuteInsertAndUpdateAsync(
            MotoSyncPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            UpdateExecuteCalls++;
            return Task.FromResult(UpdateSummary);
        }
    }
}
